namespace CartoonProjection
{
    public enum CartoonSurfaceMode
    {
        // Camera color with source materials (legacy Preserve Source Materials = on).
        OriginalMaterial = 0,
        // Baked per-region colors from material textures; ignores PBR lighting.
        MaterialColorBlocks = 1,
        // Flat per-part CartoonShapePart.fillColor; fallback when no baked data exists.
        ManualPartColor = 2
    }

    public enum CartoonBackgroundMode
    {
        Camera = 0,
        SolidColor = 1
    }

    public enum CartoonLightingMode
    {
        None = 0,
        TwoBandFaceted = 1,
        ThreeBandFaceted = 2
    }

    // ANGJustinl fork paint layers: triangles are classified by deformed geometric face
    // normal against a fixed world light direction, then painted in the capture pass.
    public enum CartoonPaintLayerMode
    {
        BaseOnly = 0,
        ShadowBase = 1,
        ShadowBaseHighlight = 2
    }
}
