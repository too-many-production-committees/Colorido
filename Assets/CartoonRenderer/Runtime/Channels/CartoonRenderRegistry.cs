using System.Collections.Generic;
using UnityEngine;

namespace CartoonProjection
{
    /// <summary>
    /// Resolves which channel a Renderer belongs to and tracks the live character controllers.
    ///
    /// Resolution walks the parent chain and lets the nearest marker win, so a root marker is
    /// inherited by children while a child can still override it. Results are cached per
    /// Renderer and dropped as soon as any marker, controller or hierarchy change invalidates
    /// the registry, so runtime-spawned objects and pooled children are recognised without a
    /// periodic full scan.
    /// </summary>
    public static class CartoonRenderRegistry
    {
        public readonly struct Resolution
        {
            public readonly CartoonRenderChannel channel;
            public readonly CartoonCharacterRenderController controller;

            public Resolution(CartoonRenderChannel channel, CartoonCharacterRenderController controller)
            {
                this.channel = channel;
                this.controller = controller;
            }
        }

        struct CacheEntry
        {
            public int version;
            public Resolution resolution;
            /// <summary>
            /// The resolver's own object reference. Instance IDs are recycled when scenes unload,
            /// so the entry is only valid while it still describes this exact Renderer; comparing
            /// the managed references avoids Unity's instance-id equality, which would match a
            /// different object that happened to reuse the id.
            /// </summary>
            public Renderer owner;
        }

        static readonly Dictionary<int, CacheEntry> cache = new();
        static readonly List<CartoonCharacterRenderController> controllers = new();
        static int version;

        /// <summary>Bumped by every marker/controller change; used to invalidate derived caches.</summary>
        public static int Version => version;

        /// <summary>Live character controllers, refreshed only when the set changes.</summary>
        public static IReadOnlyList<CartoonCharacterRenderController> Controllers => controllers;

        /// <summary>Bumped whenever the controller list itself changes.</summary>
        public static int ControllersVersion { get; private set; }

        public static void Invalidate()
        {
            version++;
        }

        /// <summary>Total successful registrations; a diagnostic for OnEnable timing.</summary>
        public static int RegistrationCount { get; private set; }

        public static void Register(CartoonCharacterRenderController controller)
        {
            if (controller == null || controllers.Contains(controller))
                return;
            RegistrationCount++;
            controllers.Add(controller);
            ControllersVersion++;
            Invalidate();
        }

        public static void Unregister(CartoonCharacterRenderController controller)
        {
            if (controller == null || !controllers.Remove(controller))
                return;
            ControllersVersion++;
            Invalidate();
        }

        /// <summary>Drops every cached decision, for example when the renderer feature is rebuilt.</summary>
        public static void Clear()
        {
            cache.Clear();
            version++;
        }

        public static Resolution Resolve(Renderer renderer)
        {
            if (renderer == null)
                return new Resolution(CartoonRenderChannel.Exclude, null);

            int id = renderer.GetInstanceID();
            if (cache.TryGetValue(id, out var entry) && entry.version == version &&
                ReferenceEquals(entry.owner, renderer))
            {
                var controller = entry.resolution.controller;
                if (controller == null || controller)
                    return entry.resolution;
            }

            var resolution = ResolveUncached(renderer);
            cache[id] = new CacheEntry { version = version, resolution = resolution, owner = renderer };
            return resolution;
        }

        static Resolution ResolveUncached(Renderer renderer)
        {
            CartoonCharacterRenderController controller = null;
            for (Transform node = renderer.transform; node != null; node = node.parent)
            {
                var classifier = node.GetComponent<CartoonRenderClassifier>();
                if (classifier != null && classifier.isActiveAndEnabled)
                {
                    var channel = classifier.Channel;
                    if (channel.HasValue)
                        return new Resolution(channel.Value, controller);
                }

                var character = node.GetComponent<CartoonCharacterRenderController>();
                if (character != null && character.isActiveAndEnabled)
                {
                    // Nearest marker wins, so stop at the first controller and treat the whole
                    // subtree as the character channel.
                    return new Resolution(CartoonRenderChannel.Character, character);
                }
            }

            // Unmarked objects keep the pre-existing behaviour: 2D sprites stay untouched by
            // the scene capture, everything else that can be drawn is scene geometry.
            if (renderer is SpriteRenderer)
                return new Resolution(CartoonRenderChannel.Exclude, null);
            if (renderer is MeshRenderer || renderer is SkinnedMeshRenderer)
                return new Resolution(CartoonRenderChannel.Scene, null);
            return new Resolution(CartoonRenderChannel.Exclude, null);
        }

        /// <summary>Forgets cached renderers that no longer exist, bounding the cache.</summary>
        public static void PruneDestroyed()
        {
            if (cache.Count == 0)
                return;
            var dead = new List<int>();
            foreach (var pair in cache)
            {
                // Instance IDs are reused, so stale entries are also dropped when the version
                // moved on; only genuinely dead ids are collected here.
                if (pair.Value.version != version || !pair.Value.owner)
                    dead.Add(pair.Key);
            }
            foreach (int id in dead)
                cache.Remove(id);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnLoad()
        {
            // Domain reload can be disabled in play mode; never keep stale static state.
            cache.Clear();
            controllers.Clear();
            version = 0;
            ControllersVersion++;
            version++;
        }
    }
}
