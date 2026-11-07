using System.IO;
using CoopDeck.Input;
using CoopDeck.Player;
using CoopDeck.Weapons;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CoopDeck.EditorTools
{
    /// <summary>All placeholder materials used by the Phase 1 sandbox.</summary>
    public sealed class MaterialSet
    {
        public Material Ground;
        public Material WallLight;
        public Material WallDark;
        public Material Teal;
        public Material Coral;
        public Material Sun;
        public Material Violet;
        public Material GlowCyan;
        public Material GlowPink;
        public Material PlayerBody;
        public Material GunBody;
        public Material GunMetal;
        public Material GunAccent;
        public Material GunGlow;
        public Material Glove;
        public Material EnemyBody;
        public Material EnemyHead;
        public Material EnemyEye;
        public Material EnemyBase;
        public Material BarBackground;
        public Material BarFill;
        public Material Sky;
        public Material FxMuzzle;
        public Material FxSpark;
        public Material FxEnemySpark;
        public Material FxTracer;
        public Material FxDeath;
    }

    /// <summary>Creates (or refreshes) materials, textures, the input asset and data assets.</summary>
    public static class GameAssetFactory
    {
        const string LitShader = "Universal Render Pipeline/Lit";
        const string UnlitShader = "Universal Render Pipeline/Unlit";
        const string FxShaderName = "CoopDeck/FX/Additive Soft";

        public static MaterialSet EnsureMaterials()
        {
            Texture2D grid = EnsureGridTexture();
            var set = new MaterialSet
            {
                Ground = Lit(GamePaths.EnvironmentMaterials, "M_Ground_Grid", new Color(0.62f, 0.66f, 0.86f), 0.15f, null, grid, new Vector2(45f, 45f)),
                WallLight = Lit(GamePaths.EnvironmentMaterials, "M_Wall_Light", new Color(0.86f, 0.84f, 0.95f), 0.1f),
                WallDark = Lit(GamePaths.EnvironmentMaterials, "M_Wall_Dark", new Color(0.32f, 0.28f, 0.52f), 0.1f),
                Teal = Lit(GamePaths.EnvironmentMaterials, "M_Block_Teal", new Color(0.18f, 0.78f, 0.74f), 0.2f),
                Coral = Lit(GamePaths.EnvironmentMaterials, "M_Block_Coral", new Color(1f, 0.45f, 0.4f), 0.2f),
                Sun = Lit(GamePaths.EnvironmentMaterials, "M_Block_Sun", new Color(1f, 0.8f, 0.25f), 0.2f),
                Violet = Lit(GamePaths.EnvironmentMaterials, "M_Block_Violet", new Color(0.6f, 0.42f, 0.95f), 0.2f),
                GlowCyan = Lit(GamePaths.EnvironmentMaterials, "M_Glow_Cyan", new Color(0.2f, 0.9f, 1f), 0.4f, new Color(0.2f, 1.6f, 2.2f)),
                GlowPink = Lit(GamePaths.EnvironmentMaterials, "M_Glow_Pink", new Color(1f, 0.35f, 0.75f), 0.4f, new Color(2.2f, 0.4f, 1.4f)),

                PlayerBody = Lit(GamePaths.CharacterMaterials, "M_Player_Body", new Color(0.25f, 0.55f, 1f), 0.3f),
                Glove = Lit(GamePaths.CharacterMaterials, "M_Player_Glove", new Color(0.18f, 0.2f, 0.3f), 0.25f),
                EnemyBody = Lit(GamePaths.CharacterMaterials, "M_Enemy_Body", new Color(0.95f, 0.38f, 0.32f), 0.2f),
                EnemyHead = Lit(GamePaths.CharacterMaterials, "M_Enemy_Head", new Color(1f, 0.72f, 0.3f), 0.25f),
                EnemyEye = Lit(GamePaths.CharacterMaterials, "M_Enemy_Eye", new Color(1f, 1f, 0.8f), 0.5f, new Color(3f, 2.6f, 1.2f)),
                EnemyBase = Lit(GamePaths.CharacterMaterials, "M_Enemy_Base", new Color(0.2f, 0.18f, 0.28f), 0.3f),
                BarBackground = Unlit(GamePaths.CharacterMaterials, "M_HealthBar_Back", new Color(0.08f, 0.05f, 0.12f, 1f)),
                BarFill = Unlit(GamePaths.CharacterMaterials, "M_HealthBar_Fill", new Color(1f, 0.3f, 0.35f, 1f)),

                GunBody = Lit(GamePaths.WeaponMaterials, "M_Rifle_Body", new Color(0.2f, 0.22f, 0.3f), 0.35f),
                GunMetal = Lit(GamePaths.WeaponMaterials, "M_Rifle_Metal", new Color(0.12f, 0.12f, 0.15f), 0.55f),
                GunAccent = Lit(GamePaths.WeaponMaterials, "M_Rifle_Accent", new Color(1f, 0.55f, 0.2f), 0.3f),
                GunGlow = Lit(GamePaths.WeaponMaterials, "M_Rifle_Glow", new Color(0.3f, 1f, 1f), 0.5f, new Color(0.4f, 2.4f, 2.8f)),

                FxMuzzle = Fx("M_FX_Muzzle", new Color(1f, 0.75f, 0.35f), 3f, 1f, 1.6f),
                FxSpark = Fx("M_FX_Spark", new Color(1f, 0.85f, 0.5f), 2.5f, 1f, 1.2f),
                FxEnemySpark = Fx("M_FX_EnemySpark", new Color(1f, 0.35f, 0.75f), 3f, 1f, 1.2f),
                FxTracer = Fx("M_FX_Tracer", new Color(1f, 0.85f, 0.45f), 2.2f, 0f, 2f),
                FxDeath = Fx("M_FX_Death", new Color(1f, 0.5f, 0.3f), 2.5f, 1f, 1.4f),
            };

            set.Sky = EnsureSkybox();
            AssetDatabase.SaveAssets();
            return set;
        }

        static Material LoadOrCreate(string folder, string name, string shaderName)
        {
            EditorUtil.EnsureFolder(folder);
            string path = $"{folder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find(shaderName);
            if (shader == null)
            {
                throw new System.InvalidOperationException($"Shader '{shaderName}' not found. Is URP installed and the project compiled?");
            }

            if (material == null)
            {
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }

            return material;
        }

        static Material Lit(string folder, string name, Color color, float smoothness, Color? emission = null,
            Texture2D baseMap = null, Vector2? tiling = null)
        {
            Material m = LoadOrCreate(folder, name, LitShader);
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", 0f);
            if (baseMap != null)
            {
                m.SetTexture("_BaseMap", baseMap);
                m.SetTextureScale("_BaseMap", tiling ?? Vector2.one);
            }

            if (emission.HasValue)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", emission.Value);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            else
            {
                m.DisableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", Color.black);
            }

            EditorUtility.SetDirty(m);
            return m;
        }

        static Material Unlit(string folder, string name, Color color)
        {
            Material m = LoadOrCreate(folder, name, UnlitShader);
            m.SetColor("_BaseColor", color);
            EditorUtility.SetDirty(m);
            return m;
        }

        public static Material Fx(string name, Color color, float intensity, float radial, float falloff)
        {
            Material m = LoadOrCreate(GamePaths.VfxMaterials, name, FxShaderName);
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Intensity", intensity);
            m.SetFloat("_Radial", radial);
            m.SetFloat("_Falloff", falloff);
            m.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material EnsureSkybox()
        {
            Material sky = LoadOrCreate(GamePaths.EnvironmentMaterials, "M_Sky_Procedural", "Skybox/Procedural");
            sky.SetColor("_SkyTint", new Color(0.55f, 0.6f, 1f));
            sky.SetColor("_GroundColor", new Color(0.45f, 0.4f, 0.55f));
            sky.SetFloat("_AtmosphereThickness", 0.75f);
            sky.SetFloat("_Exposure", 1.25f);
            sky.SetFloat("_SunSize", 0.035f);
            EditorUtility.SetDirty(sky);
            return sky;
        }

        /// <summary>Light grid texture (2 m cells on the ground) so speed and distance are easy to read.</summary>
        static Texture2D EnsureGridTexture()
        {
            EditorUtil.EnsureFolder(GamePaths.EnvironmentTextures);
            string path = GamePaths.EnvironmentTextures + "/T_Grid.png";
            if (!File.Exists(path))
            {
                const int size = 256;
                var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
                var fillColor = new Color(0.93f, 0.93f, 1f);
                var minorLine = new Color(0.8f, 0.8f, 0.92f);
                var majorLine = new Color(0.55f, 0.55f, 0.75f);
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        bool major = x < 4 || y < 4;
                        bool minor = x % 64 < 2 || y % 64 < 2;
                        texture.SetPixel(x, y, major ? majorLine : minor ? minorLine : fillColor);
                    }
                }

                File.WriteAllBytes(path, texture.EncodeToPNG());
                Object.DestroyImmediate(texture);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.filterMode = FilterMode.Trilinear;
                importer.anisoLevel = 8;
                importer.mipmapEnabled = true;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        public static InputActionAsset EnsureInputActions(bool overwrite)
        {
            EditorUtil.EnsureFolder(Path.GetDirectoryName(GamePaths.InputActions)?.Replace('\\', '/'));
            if (!overwrite && File.Exists(GamePaths.InputActions) && MissingActions(AssetDatabase.LoadAssetAtPath<InputActionAsset>(GamePaths.InputActions)))
            {
                // The code added actions the saved asset does not have (e.g. card selection):
                // regenerate it, otherwise PlayerInputReader would fail to find them.
                Debug.LogWarning("[CoopDeck] GameControls.inputactions is missing actions; regenerating it from GameInputActions.");
                overwrite = true;
            }

            if (overwrite || !File.Exists(GamePaths.InputActions))
            {
                InputActionAsset asset = GameInputActions.CreateDefaultAsset();
                File.WriteAllText(GamePaths.InputActions, asset.ToJson());
                Object.DestroyImmediate(asset);
                AssetDatabase.ImportAsset(GamePaths.InputActions, ImportAssetOptions.ForceSynchronousImport);
            }

            var imported = AssetDatabase.LoadAssetAtPath<InputActionAsset>(GamePaths.InputActions);
            if (imported == null)
            {
                throw new System.InvalidOperationException("GameControls.inputactions did not import as an InputActionAsset.");
            }

            return imported;
        }

        static bool MissingActions(InputActionAsset asset)
        {
            if (asset == null)
            {
                return true;
            }

            foreach (string action in GameInputActions.AllPlayerActions)
            {
                if (asset.FindAction($"{GameInputActions.PlayerMap}/{action}") == null)
                {
                    return true;
                }
            }

            return false;
        }

        public static MovementProfile EnsureMovementProfile()
        {
            EditorUtil.EnsureFolder(GamePaths.CharacterData);
            string path = GamePaths.CharacterData + "/MP_Default.asset";
            var profile = AssetDatabase.LoadAssetAtPath<MovementProfile>(path);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<MovementProfile>();
                AssetDatabase.CreateAsset(profile, path);
            }

            return profile;
        }

        /// <summary>Creates the assault rifle once; on later runs only fills in missing effect references.</summary>
        public static WeaponDefinition EnsureAssaultRifle(FxPrefabSet fx)
        {
            EditorUtil.EnsureFolder(GamePaths.WeaponData);
            string path = GamePaths.WeaponData + "/WD_AssaultRifle.asset";
            var weapon = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(path);
            if (weapon == null)
            {
                weapon = ScriptableObject.CreateInstance<WeaponDefinition>();
                weapon.weaponId = "weapon.assault_rifle";
                weapon.displayName = "プロトAR";
                AssetDatabase.CreateAsset(weapon, path);
            }

            if (weapon.muzzleFlashPrefab == null) weapon.muzzleFlashPrefab = fx.MuzzleFlash;
            if (weapon.tracerPrefab == null) weapon.tracerPrefab = fx.Tracer;
            if (weapon.impactPrefab == null) weapon.impactPrefab = fx.ImpactEnvironment;
            if (weapon.damageableImpactPrefab == null) weapon.damageableImpactPrefab = fx.ImpactDamageable;
            EditorUtility.SetDirty(weapon);
            return weapon;
        }

        public static VolumeProfile EnsureSandboxVolumeProfile()
        {
            EditorUtil.EnsureFolder(GamePaths.Rendering);
            string path = GamePaths.Rendering + "/VP_Sandbox.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile != null)
            {
                return profile;
            }

            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);

            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(1f);
            bloom.intensity.Override(0.55f);
            bloom.scatter.Override(0.65f);
            AssetDatabase.AddObjectToAsset(bloom, profile);

            var tonemapping = profile.Add<Tonemapping>(true);
            tonemapping.mode.Override(TonemappingMode.Neutral);
            AssetDatabase.AddObjectToAsset(tonemapping, profile);

            var color = profile.Add<ColorAdjustments>(true);
            color.contrast.Override(8f);
            color.saturation.Override(18f);
            AssetDatabase.AddObjectToAsset(color, profile);

            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.18f);
            AssetDatabase.AddObjectToAsset(vignette, profile);

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }
    }
}
