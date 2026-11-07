using System;
using UnityEngine;

namespace CoopDeck.Core
{
    public enum PerformanceTier
    {
        /// <summary>Main PC (RTX 4070 class): full effects, native resolution.</summary>
        High = 0,

        /// <summary>ROG Ally X class handheld: ~900p internal, reduced shadows and particles, 60 fps.</summary>
        Handheld = 1,
    }

    /// <summary>
    /// Global performance knobs read by effects and rendering code. The tier is picked once at
    /// startup by <see cref="PerformanceBootstrap"/> and can be changed at runtime.
    /// </summary>
    public static class PerformanceSettings
    {
        public const string HighQualityLevelName = "High";
        public const string HandheldQualityLevelName = "Handheld";

        public static PerformanceTier Tier { get; private set; } = PerformanceTier.High;

        /// <summary>Multiplier for particle counts and emission rates (1 = authored values).</summary>
        public static float EffectDensity => Tier == PerformanceTier.High ? 1f : 0.55f;

        /// <summary>Whether short-lived effect lights (muzzle flash, explosions) are allowed.</summary>
        public static bool EffectLightsEnabled => Tier == PerformanceTier.High;

        public static event Action<PerformanceTier> TierChanged;

        public static void SetTier(PerformanceTier tier)
        {
            if (Tier == tier)
            {
                return;
            }

            Tier = tier;
            TierChanged?.Invoke(tier);
        }

        public static string QualityLevelNameFor(PerformanceTier tier) =>
            tier == PerformanceTier.Handheld ? HandheldQualityLevelName : HighQualityLevelName;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Tier = PerformanceTier.High;
            TierChanged = null;
        }
    }
}
