namespace LayerSlide.Core
{
    /// <summary>Fixed rule constants from GDD 01 (ruleVersion = 1). All distances are in cells.</summary>
    public static class RuleConstants
    {
        public const int RuleVersion = 1;
        public const int SchemaVersion = 1;

        /// <summary>Drag distance that locks the movement axis.</summary>
        public const float AxisLockThreshold = 0.15f;

        /// <summary>Outward overshoot past the board edge that arms a gate.</summary>
        public const float ExitIntentThreshold = 0.25f;

        /// <summary>Tolerance for float comparisons on cell coordinates.</summary>
        public const float Epsilon = 0.000001f;

        public const int MaxLayers = 2;

        /// <summary>Upper bound used by the solver's packed state key (8 bits per block in a ulong).</summary>
        public const int MaxBlocks = 8;
    }
}
