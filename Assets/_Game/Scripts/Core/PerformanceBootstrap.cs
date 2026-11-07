using System;
using UnityEngine;

namespace CoopDeck.Core
{
    /// <summary>
    /// Picks the performance tier before the first scene loads and applies the matching quality
    /// level and frame pacing.
    ///
    /// Command line (built player):
    ///   -tier high | -tier handheld   force a tier
    ///   -uncapped                     no vsync / frame cap (for measuring headroom)
    /// </summary>
    public static class PerformanceBootstrap
    {
        public const int HandheldTargetFrameRate = 60;

        public static PerformanceTier DetectedTier { get; private set; }
        public static string DetectionReason { get; private set; } = "";
        public static bool Uncapped { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Initialize()
        {
            string[] args = Environment.GetCommandLineArgs();
            Uncapped = HasArg(args, "-uncapped");

            string forced = GetArgValue(args, "-tier");
            if (!string.IsNullOrEmpty(forced) && TryParseTier(forced, out PerformanceTier tier))
            {
                DetectedTier = tier;
                DetectionReason = "command line";
            }
            else
            {
                DetectedTier = DetectTier(SystemInfo.deviceModel, SystemInfo.graphicsDeviceName, out string reason);
                DetectionReason = reason;
            }

            Apply(DetectedTier);
        }

        /// <summary>Applies a tier: quality level, frame pacing and effect scaling.</summary>
        public static void Apply(PerformanceTier tier)
        {
            PerformanceSettings.SetTier(tier);

            int level = Array.IndexOf(QualitySettings.names, PerformanceSettings.QualityLevelNameFor(tier));
            if (level >= 0 && level != QualitySettings.GetQualityLevel())
            {
                QualitySettings.SetQualityLevel(level, true);
            }

            ApplyFramePacing(tier);
        }

        static void ApplyFramePacing(PerformanceTier tier)
        {
            if (Uncapped)
            {
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = -1;
                return;
            }

            if (tier == PerformanceTier.Handheld)
            {
                // On a 120 Hz panel, vsync every 2nd refresh gives an even 60 fps without tearing.
                double refresh = Screen.currentResolution.refreshRateRatio.value;
                if (refresh >= 119.0)
                {
                    QualitySettings.vSyncCount = 2;
                    Application.targetFrameRate = -1;
                }
                else
                {
                    QualitySettings.vSyncCount = 0;
                    Application.targetFrameRate = HandheldTargetFrameRate;
                }
            }
            else
            {
                QualitySettings.vSyncCount = 1;
                Application.targetFrameRate = -1;
            }
        }

        /// <summary>Heuristic: handheld PCs report their model, and integrated Radeon GPUs report no model number.</summary>
        public static PerformanceTier DetectTier(string deviceModel, string gpuName, out string reason)
        {
            deviceModel ??= "";
            gpuName ??= "";

            if (deviceModel.IndexOf("ROG Ally", StringComparison.OrdinalIgnoreCase) >= 0 ||
                deviceModel.IndexOf("RC71L", StringComparison.OrdinalIgnoreCase) >= 0 ||
                deviceModel.IndexOf("RC72L", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                reason = $"device model '{deviceModel}'";
                return PerformanceTier.Handheld;
            }

            string gpu = gpuName.Trim();
            if (gpu.Equals("AMD Radeon Graphics", StringComparison.OrdinalIgnoreCase) ||
                gpu.Equals("AMD Radeon(TM) Graphics", StringComparison.OrdinalIgnoreCase))
            {
                reason = $"integrated GPU '{gpu}'";
                return PerformanceTier.Handheld;
            }

            reason = $"default (gpu '{gpu}')";
            return PerformanceTier.High;
        }

        public static bool TryParseTier(string value, out PerformanceTier tier)
        {
            switch (value.Trim().ToLowerInvariant())
            {
                case "high":
                case "pc":
                    tier = PerformanceTier.High;
                    return true;
                case "handheld":
                case "ally":
                case "low":
                    tier = PerformanceTier.Handheld;
                    return true;
                default:
                    tier = PerformanceTier.High;
                    return false;
            }
        }

        static bool HasArg(string[] args, string name)
        {
            foreach (string arg in args)
            {
                if (string.Equals(arg, name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public static string GetArgValue(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }

            return null;
        }
    }
}
