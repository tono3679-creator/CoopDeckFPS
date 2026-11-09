using CoopDeck.Core;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CoopDeck.EditorTools
{
    /// <summary>
    /// Creates the two URP quality tiers and wires them to the quality levels:
    ///   High     (main PC)   : native resolution, 4x MSAA, 70 m / 2048 shadows, 4 cascades.
    ///   Handheld (ROG Ally X): native resolution (1080p; 0.833 + FSR made the first-person rifle and its one-pixel
    ///                          outline stair-step, Docs/WEAPON_VISUAL_QUALITY.md), no MSAA, 35 m / 1024 hard shadows,
    ///                          2 cascades, no SSAO.
    /// Both tiers anti-alias with SMAA High on the player camera stack (CharacterPrefabBuilder).
    /// Values are written through SerializedObject and read back through the public API so the
    /// log shows what actually took effect.
    /// </summary>
    public static class RenderingSetup
    {
        const string TemplatePcAsset = "Assets/Settings/PC_RPAsset.asset";
        const string TemplatePcRenderer = "Assets/Settings/PC_Renderer.asset";

        public static void Apply()
        {
            EditorUtil.EnsureFolder(GamePaths.Rendering);

            UniversalRenderPipelineAsset high = CopyIfMissing<UniversalRenderPipelineAsset>(TemplatePcAsset, GamePaths.Rendering + "/URP_High.asset");
            UniversalRenderPipelineAsset handheld = CopyIfMissing<UniversalRenderPipelineAsset>(TemplatePcAsset, GamePaths.Rendering + "/URP_Handheld.asset");
            UniversalRendererData highRenderer = CopyIfMissing<UniversalRendererData>(TemplatePcRenderer, GamePaths.Rendering + "/URP_High_Renderer.asset");
            UniversalRendererData handheldRenderer = CopyIfMissing<UniversalRendererData>(TemplatePcRenderer, GamePaths.Rendering + "/URP_Handheld_Renderer.asset");

            // desktop (RTX 4070 class): 4x MSAA keeps the first-person arms, rifle and outlines clean in the camera stack
            ConfigureAsset(high, highRenderer, renderScale: 1f, upscaling: UpscalingFilterSelection.Auto, msaa: 4,
                shadowDistance: 70f, shadowResolution: 2048, cascades: 4, softShadows: true);
            // ROG Ally X: native 1080p (about +0.7 ms GPU against 0.833 + FSR, Docs/WEAPON_VISUAL_QUALITY.md §4); 2x MSAA cost about 3 ms
            ConfigureAsset(handheld, handheldRenderer, renderScale: 1f, upscaling: UpscalingFilterSelection.Auto, msaa: 1,
                shadowDistance: 35f, shadowResolution: 1024, cascades: 2, softShadows: false);

            DisableFeature(handheldRenderer, "ScreenSpaceAmbientOcclusion");

            ConfigureQualityLevels(high, handheld);
            GraphicsSettings.defaultRenderPipeline = high;

            AssetDatabase.SaveAssets();
            Log("High", high);
            Log("Handheld", handheld);
        }

        static T CopyIfMissing<T>(string source, string destination) where T : Object
        {
            if (!EditorUtil.Exists(destination))
            {
                if (!AssetDatabase.CopyAsset(source, destination))
                {
                    throw new System.InvalidOperationException($"Could not copy {source} to {destination}.");
                }
            }

            var asset = AssetDatabase.LoadAssetAtPath<T>(destination);
            if (asset == null)
            {
                throw new System.InvalidOperationException($"{destination} is not a {typeof(T).Name}.");
            }

            return asset;
        }

        static void ConfigureAsset(UniversalRenderPipelineAsset asset, UniversalRendererData renderer, float renderScale,
            UpscalingFilterSelection upscaling, int msaa, float shadowDistance, int shadowResolution, int cascades, bool softShadows)
        {
            var so = new SerializedObject(asset);

            SerializedProperty renderers = so.FindProperty("m_RendererDataList");
            renderers.arraySize = 1;
            renderers.GetArrayElementAtIndex(0).objectReferenceValue = renderer;
            Set(so, "m_DefaultRendererIndex", p => p.intValue = 0);

            Set(so, "m_RenderScale", p => p.floatValue = renderScale);
            Set(so, "m_UpscalingFilter", p => p.intValue = (int)upscaling);
            Set(so, "m_MSAA", p => p.intValue = msaa);
            Set(so, "m_ShadowDistance", p => p.floatValue = shadowDistance);
            Set(so, "m_MainLightShadowmapResolution", p => p.intValue = shadowResolution);
            Set(so, "m_ShadowCascadeCount", p => p.intValue = cascades);
            Set(so, "m_SoftShadowsSupported", p => p.boolValue = softShadows);
            Set(so, "m_SupportsHDR", p => p.boolValue = true);

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
        }

        static void Set(SerializedObject so, string name, System.Action<SerializedProperty> apply)
        {
            SerializedProperty property = so.FindProperty(name);
            if (property == null)
            {
                Debug.LogWarning($"[CoopDeck] URP asset property '{name}' not found; skipped.");
                return;
            }

            apply(property);
        }

        static void DisableFeature(UniversalRendererData renderer, string typeNameContains)
        {
            foreach (ScriptableRendererFeature feature in renderer.rendererFeatures)
            {
                if (feature != null && feature.GetType().Name.Contains(typeNameContains))
                {
                    feature.SetActive(false);
                    EditorUtility.SetDirty(feature);
                    Debug.Log($"[CoopDeck] Disabled {feature.GetType().Name} on {renderer.name}.");
                }
            }

            EditorUtility.SetDirty(renderer);
        }

        /// <summary>Renames the template's "Mobile"/"PC" levels to "Handheld"/"High" and assigns the tier assets.</summary>
        static void ConfigureQualityLevels(UniversalRenderPipelineAsset high, UniversalRenderPipelineAsset handheld)
        {
            Object qualityAsset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset")[0];
            var so = new SerializedObject(qualityAsset);
            SerializedProperty levels = so.FindProperty("m_QualitySettings");

            bool foundHigh = false;
            bool foundHandheld = false;
            for (int i = 0; i < levels.arraySize; i++)
            {
                SerializedProperty level = levels.GetArrayElementAtIndex(i);
                SerializedProperty name = level.FindPropertyRelative("name");
                SerializedProperty pipeline = level.FindPropertyRelative("customRenderPipeline");

                // The template excludes its "Mobile" level from Standalone builds. Both tiers must
                // exist in Windows builds so the Ally X can switch to Handheld at runtime.
                SerializedProperty excluded = level.FindPropertyRelative("excludedTargetPlatforms");
                if (excluded != null && excluded.isArray)
                {
                    for (int e = excluded.arraySize - 1; e >= 0; e--)
                    {
                        if (excluded.GetArrayElementAtIndex(e).stringValue == "Standalone")
                        {
                            excluded.DeleteArrayElementAtIndex(e);
                        }
                    }
                }

                if (name.stringValue == "PC" || name.stringValue == PerformanceSettings.HighQualityLevelName)
                {
                    name.stringValue = PerformanceSettings.HighQualityLevelName;
                    pipeline.objectReferenceValue = high;
                    foundHigh = true;
                }
                else if (name.stringValue == "Mobile" || name.stringValue == PerformanceSettings.HandheldQualityLevelName)
                {
                    name.stringValue = PerformanceSettings.HandheldQualityLevelName;
                    pipeline.objectReferenceValue = handheld;
                    foundHandheld = true;
                }
            }

            so.ApplyModifiedPropertiesWithoutUndo();

            if (!foundHigh || !foundHandheld)
            {
                Debug.LogWarning("[CoopDeck] Expected template quality levels 'PC' and 'Mobile' were not both found. " +
                                 "Rename/assign quality levels manually: " + string.Join(", ", QualitySettings.names));
            }
        }

        static void Log(string label, UniversalRenderPipelineAsset asset)
        {
            Debug.Log($"[CoopDeck] URP {label}: renderScale={asset.renderScale}, upscaling={asset.upscalingFilter}, " +
                      $"msaa={asset.msaaSampleCount}, shadowDistance={asset.shadowDistance}, " +
                      $"shadowRes={asset.mainLightShadowmapResolution}, cascades={asset.shadowCascadeCount}, " +
                      $"renderer={new SerializedObject(asset).FindProperty("m_RendererDataList").GetArrayElementAtIndex(0).objectReferenceValue?.name}");
        }

        public static void ApplyPlayerSettings()
        {
            PlayerSettings.companyName = "CoopDeck";
            PlayerSettings.productName = "CoopDeckFPS";
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.defaultIsNativeResolution = true;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            PlayerSettings.enableFrameTimingStats = true; // CPU / GPU frame times for the unattended perf runs
        }
    }
}
