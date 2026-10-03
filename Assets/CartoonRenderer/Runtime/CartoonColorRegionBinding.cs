using UnityEngine;

namespace CartoonProjection
{
    // Marker component linking a renderer to its baked color-region data.
    // Read-only at runtime; used for warnings and inspector statistics.
    [DisallowMultipleComponent]
    public sealed class CartoonColorRegionBinding : MonoBehaviour
    {
        public CartoonColorRegionAsset asset;
        public string rendererPath;
        public int regionCount;
    }
}
