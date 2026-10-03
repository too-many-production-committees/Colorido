using UnityEngine;

namespace CartoonProjection
{
    // Render-only part metadata, deliberately independent from gameplay IDs.
    [ExecuteAlways, DisallowMultipleComponent]
    [RequireComponent(typeof(Renderer))]
    public sealed class CartoonShapePart : MonoBehaviour
    {
        public Color fillColor = Color.white;
        [Range(1, 255)] public int shapeId = 1;

        [Tooltip("Optional base map used only for alpha cutout during cartoon capture. Assigned by the region baker.")]
        public Texture2D cutoutTexture;
        [Tooltip("Alpha clip threshold; 0 disables cutout in the capture pass.")]
        [Range(0f, 1f)] public float alphaCutoff;
        [Tooltip("Source material _BaseMap tiling/offset, applied when sampling cutoutTexture.")]
        public Vector4 baseMapScaleOffset = new(1f, 1f, 0f, 0f);

        private Renderer cachedRenderer;
        private MaterialPropertyBlock properties;

        private void OnEnable() => Apply();
        private void OnValidate() => Apply();

        public void Apply()
        {
            cachedRenderer ??= GetComponent<Renderer>();
            properties ??= new MaterialPropertyBlock();
            cachedRenderer.GetPropertyBlock(properties);
            properties.SetColor("_CartoonShapeColor", fillColor);
            properties.SetFloat("_CartoonShapeId", shapeId / 255f);
            if (cutoutTexture != null && alphaCutoff > 0.001f)
            {
                properties.SetTexture("_CartoonCutoutTex", cutoutTexture);
                properties.SetFloat("_CartoonCutoff", alphaCutoff);
                properties.SetVector("_CartoonMainTex_ST", baseMapScaleOffset);
            }
            else
            {
                // MaterialPropertyBlock rejects null textures. White also clears
                // any previously assigned cutout without affecting opaque objects.
                properties.SetTexture("_CartoonCutoutTex", Texture2D.whiteTexture);
                properties.SetFloat("_CartoonCutoff", 0f);
                properties.SetVector("_CartoonMainTex_ST", new Vector4(1f, 1f, 0f, 0f));
            }
            cachedRenderer.SetPropertyBlock(properties);
        }
    }
}
