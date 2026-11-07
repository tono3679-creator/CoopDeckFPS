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

    /// <summary>Effect detail level. Follows the tier unless the player picks one (F4 / -vfx).</summary>
    public enum VfxQuality
    {
        /// <summary>Fewest particles, no effect lights, secondary layers off, half screen shake.</summary>
        Low = 0,

        /// <summary>Handheld default: reduced particles, no effect lights.</summary>
        Medium = 1,

        /// <summary>Main PC default: authored particle counts and effect lights.</summary>
        High = 2,
    }

    /// <summary>
    /// Global performance knobs read by effects and rendering code. The tier is picked once at
    /// startup by <see cref="PerformanceBootstrap"/> and can be changed at runtime.
    /// </summary>
    public static class PerformanceSettings
    {
        public const string HighQualityLevelName = "High";
        public const string HandheldQualityLevelName = "Handheld";

        static bool vfxOverridden;

        public static PerformanceTier Tier { get; private set; } = PerformanceTier.High;

        /// <summary>Current effect detail. Defaults to the tier's value (High → High, Handheld → Medium).</summary>
        public static VfxQuality Vfx { get; private set; } = VfxQuality.High;

        /// <summary>True when <see cref="Vfx"/> was chosen explicitly instead of following the tier.</summary>
        public static bool VfxOverridden => vfxOverridden;

        /// <summary>Multiplier for particle counts and emission rates (1 = authored values).</summary>
        public static float EffectDensity => DensityFor(Vfx);

        /// <summary>Whether short-lived effect lights (muzzle flash, explosions) are allowed.</summary>
        public static bool EffectLightsEnabled => Vfx == VfxQuality.High;

        /// <summary>Scale for camera shake from explosions and combos (0 disables it).</summary>
        public static float ScreenShakeScale { get; set; } = 1f;

        public static event Action<PerformanceTier> TierChanged;
        public static event Action<VfxQuality> VfxChanged;

        public static float DensityFor(VfxQuality quality)
        {
            switch (quality)
            {
                case VfxQuality.Low: return 0.3f;
                case VfxQuality.Medium: return 0.55f;
                default: return 1f;
            }
        }

        public static VfxQuality DefaultVfxFor(PerformanceTier tier) =>
            tier == PerformanceTier.Handheld ? VfxQuality.Medium : VfxQuality.High;

        /// <summary>Shake multiplier for the effect quality: low detail also calms the camera.</summary>
        public static float ShakeFor(VfxQuality quality) => quality == VfxQuality.Low ? 0.5f : 1f;

        public static void SetTier(PerformanceTier tier)
        {
            if (Tier == tier)
            {
                return;
            }

            Tier = tier;
            TierChanged?.Invoke(tier);
            if (!vfxOverridden)
            {
                ApplyVfx(DefaultVfxFor(tier));
            }
        }

        /// <summary>Picks the effect detail explicitly; it no longer follows tier changes.</summary>
        public static void SetVfxQuality(VfxQuality quality)
        {
            vfxOverridden = true;
            ApplyVfx(quality);
        }

        /// <summary>Goes back to the tier's default effect detail.</summary>
        public static void ResetVfxQuality()
        {
            vfxOverridden = false;
            ApplyVfx(DefaultVfxFor(Tier));
        }

        /// <summary>High → Medium → Low → High (the F4 debug key).</summary>
        public static VfxQuality CycleVfxQuality()
        {
            VfxQuality next = Vfx == VfxQuality.High ? VfxQuality.Medium : Vfx == VfxQuality.Medium ? VfxQuality.Low : VfxQuality.High;
            SetVfxQuality(next);
            return next;
        }

        public static bool TryParseVfx(string value, out VfxQuality quality)
        {
            switch ((value ?? "").Trim().ToLowerInvariant())
            {
                case "low":
                    quality = VfxQuality.Low;
                    return true;
                case "medium":
                case "mid":
                    quality = VfxQuality.Medium;
                    return true;
                case "high":
                    quality = VfxQuality.High;
                    return true;
                default:
                    quality = VfxQuality.High;
                    return false;
            }
        }

        static void ApplyVfx(VfxQuality quality)
        {
            if (Vfx == quality)
            {
                return;
            }

            Vfx = quality;
            VfxChanged?.Invoke(quality);
        }

        public static string QualityLevelNameFor(PerformanceTier tier) =>
            tier == PerformanceTier.Handheld ? HandheldQualityLevelName : HighQualityLevelName;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Tier = PerformanceTier.High;
            Vfx = VfxQuality.High;
            vfxOverridden = false;
            ScreenShakeScale = 1f;
            TierChanged = null;
            VfxChanged = null;
        }
    }
}
