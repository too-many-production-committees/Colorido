namespace CartoonProjection
{
    // Standard is the fallback for unrecognized materials, so it must be value 0.
    public enum CartoonRegionPolicy
    {
        Standard = 0,
        HeroDetail = 1,
        Aggressive = 2
    }

    public static class CartoonRegionPolicyDefaults
    {
        public const int HeroMaxRegions = 16;
        public const int HeroMinTriangles = 1;
        public const float HeroMergeScale = 0.45f;

        public const int StandardMaxRegions = 8;
        public const int StandardMinTriangles = 4;
        public const float StandardMergeScale = 1f;

        public const int AggressiveMaxRegions = 4;
        public const int AggressiveMinTriangles = 12;
        public const float AggressiveMergeScale = 1.8f;

        // Base OKLab distances used before rule and global multipliers.
        public const float HeroBaseDistance = 0.035f;
        public const float StandardBaseDistance = 0.075f;
        public const float AggressiveBaseDistance = 0.13f;

        // Hard cap so the region-count limit can never force clearly different
        // identity colors (eyes, red ribbons) into one block.
        public const float ProtectedMergeDistance = 0.18f;

        // OKLab chroma above this marks a vivid identity color.
        public const float VividChroma = 0.08f;

        public static int MaxRegions(CartoonRegionPolicy policy) => policy switch
        {
            CartoonRegionPolicy.HeroDetail => HeroMaxRegions,
            CartoonRegionPolicy.Aggressive => AggressiveMaxRegions,
            _ => StandardMaxRegions
        };

        public static int MinTriangles(CartoonRegionPolicy policy) => policy switch
        {
            CartoonRegionPolicy.HeroDetail => HeroMinTriangles,
            CartoonRegionPolicy.Aggressive => AggressiveMinTriangles,
            _ => StandardMinTriangles
        };

        public static float MergeScale(CartoonRegionPolicy policy) => policy switch
        {
            CartoonRegionPolicy.HeroDetail => HeroMergeScale,
            CartoonRegionPolicy.Aggressive => AggressiveMergeScale,
            _ => StandardMergeScale
        };

        public static float BaseDistance(CartoonRegionPolicy policy) => policy switch
        {
            CartoonRegionPolicy.HeroDetail => HeroBaseDistance,
            CartoonRegionPolicy.Aggressive => AggressiveBaseDistance,
            _ => StandardBaseDistance
        };

        // Suggested policy from material/renderer/texture names. Names only produce
        // a suggestion; the user can always override it in the baker window.
        public static CartoonRegionPolicy SuggestFromNames(string materialName, string rendererName, string textureName)
        {
            string text = $"{materialName} {rendererName} {textureName}".ToLowerInvariant();
            if (ContainsAny(text, "eye", "face", "mouth", "brow", "ribbon", "emblem"))
                return CartoonRegionPolicy.HeroDetail;
            if (ContainsAny(text, "pattern", "detail", "lace", "boot", "grid", "noise"))
                return CartoonRegionPolicy.Aggressive;
            return CartoonRegionPolicy.Standard;
        }

        private static bool ContainsAny(string text, params string[] keywords)
        {
            foreach (string keyword in keywords)
                if (text.Contains(keyword))
                    return true;
            return false;
        }
    }
}
