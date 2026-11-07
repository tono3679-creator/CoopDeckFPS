using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace CoopDeck.EditorTools
{
    /// <summary>
    /// Windows 64-bit builds into Builds/Windows (ignored by Git).
    ///
    /// Batch mode:
    ///   Unity.exe -batchmode -quit -projectPath . -executeMethod CoopDeck.EditorTools.BuildScript.BuildWindowsBatch [-development]
    /// </summary>
    public static class BuildScript
    {
        public const string OutputDirectory = "Builds/Windows";
        public const string ExecutableName = "CoopDeckFPS.exe";

        [MenuItem("CoopDeck/Build/Windows (Development)", priority = 100)]
        public static void BuildWindowsDevelopment() => BuildWindows(true);

        [MenuItem("CoopDeck/Build/Windows (Release)", priority = 101)]
        public static void BuildWindowsRelease() => BuildWindows(false);

        public static void BuildWindowsBatch()
        {
            bool development = Environment.GetCommandLineArgs().Contains("-development");
            BuildReport report = BuildWindows(development);
            if (report.summary.result != BuildResult.Succeeded)
            {
                EditorApplication.Exit(1);
            }
        }

        public static BuildReport BuildWindows(bool development)
        {
            string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0)
            {
                throw new InvalidOperationException("No scenes in Build Settings. Run CoopDeck > Setup > Run Phase 1 Bootstrap first.");
            }

            Directory.CreateDirectory(OutputDirectory);
            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = Path.Combine(OutputDirectory, ExecutableName),
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = development ? BuildOptions.Development : BuildOptions.None,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;
            Debug.Log($"[CoopDeck] Build {summary.result}: {summary.outputPath} " +
                      $"({summary.totalSize / (1024f * 1024f):0.0} MB, {summary.totalTime.TotalSeconds:0}s, " +
                      $"{summary.totalErrors} errors, {summary.totalWarnings} warnings)");
            return report;
        }
    }
}
