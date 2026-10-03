using System.Collections.Generic;
using UnityEngine;

namespace CartoonProjection
{
    /// <summary>
    /// Per-character render mode. Its presence also marks the whole subtree as the Character
    /// channel, so a character never needs a second classifier component.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class CartoonCharacterRenderController : MonoBehaviour
    {
        public enum Mode
        {
            /// <summary>Follow the project-wide character channel setting.</summary>
            Inherit = 0,
            /// <summary>Keep the original material or sprite for this character.</summary>
            Original = 1,
            /// <summary>Rebuild this character's 2D shapes (2D sprites) or capture its meshes.</summary>
            ProjectedShapes = 2
        }

        [Tooltip("Inherit follows the renderer feature's character channel. Original opts this character out; ProjectedShapes opts it in when the channel is on.")]
        public Mode mode = Mode.Inherit;

        [Tooltip("Optional per-character style override. Null keeps the shared character style.")]
        public CartoonCharacterStyleOverride styleOverride;

        void OnEnable()
        {
            CartoonRenderRegistry.Register(this);
        }

        void OnDisable()
        {
            CartoonRenderRegistry.Unregister(this);
        }

        void OnTransformParentChanged() => CartoonRenderRegistry.Invalidate();

        void OnValidate() => CartoonRenderRegistry.Invalidate();
    }
}
