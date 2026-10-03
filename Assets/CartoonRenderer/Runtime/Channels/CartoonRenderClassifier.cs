using UnityEngine;

namespace CartoonProjection
{
    /// <summary>Render channel an object belongs to. Only one channel per Renderer.</summary>
    public enum CartoonRenderChannel
    {
        /// <summary>Not projected: keeps the original material/sprite and only occludes.</summary>
        Exclude = 0,
        /// <summary>Captured and rebuilt by the scene projection pass.</summary>
        Scene = 1,
        /// <summary>Rebuilt by its own character path; never captured by the scene pass.</summary>
        Character = 2
    }

    /// <summary>
    /// Explicit channel override for a single object. Children inherit the nearest marker
    /// in their parent chain, so a child can override its root and vice versa.
    /// This is render-only: it never touches Unity layers, colliders or gameplay markers.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class CartoonRenderClassifier : MonoBehaviour
    {
        public enum Mode
        {
            /// <summary>No opinion: keep walking up the parent chain.</summary>
            Inherit = 0,
            Scene = 1,
            Character = 2,
            Exclude = 3
        }

        [Tooltip("Inherit keeps the nearest ancestor decision. Other values override it for this object and its children.")]
        public Mode mode = Mode.Inherit;

        public CartoonRenderChannel? Channel => mode switch
        {
            Mode.Scene => CartoonRenderChannel.Scene,
            Mode.Character => CartoonRenderChannel.Character,
            Mode.Exclude => CartoonRenderChannel.Exclude,
            _ => null
        };

        void OnEnable() => CartoonRenderRegistry.Invalidate();

        void OnDisable() => CartoonRenderRegistry.Invalidate();

        void OnTransformParentChanged() => CartoonRenderRegistry.Invalidate();

        void OnValidate() => CartoonRenderRegistry.Invalidate();
    }
}
