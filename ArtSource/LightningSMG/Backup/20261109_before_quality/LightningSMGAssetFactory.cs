using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace CoopDeck.EditorTools
{
    /// <summary>
    /// Unity side of the Lightning SMG, the Lightning Rabbit's weapon (Meshy multi-image → Blender rebuild from the
    /// sculpt with ArtSource/Tools/rebuild_prop_hq.py, see Docs/LIGHTNING_RABBIT.md §3.4): import settings, an emission
    /// map for the cyan energy windows, toon materials (third person and a thin-outline first-person one) and the weapon
    /// prefab. The FBX holds LightningSMG_Body_LOD0 (100k triangles, always seen in first person) and _LOD1 (30k), so
    /// Unity builds a LOD group; its origin is the pistol grip (right hand) and the "Muzzle" and "LeftHandGrip"
    /// (the vertical foregrip) sockets come from the FBX. Same conventions as <see cref="FireRifleAssetFactory"/>.
    /// </summary>
    public static class LightningSMGAssetFactory
    {
        public const string ArtFolder = GamePaths.Root + "/Art/Weapons/LightningSMG";
        public const string ModelPath = ArtFolder + "/Models/LightningSMG.fbx";
        public const string TexturePath = ArtFolder + "/Textures/T_LightningSMG_BaseColor.png";
        public const string EmissionPath = ArtFolder + "/Textures/T_LightningSMG_Emission.png";
        public const string NormalPath = ArtFolder + "/Textures/T_LightningSMG_Normal.png";
        public const string MaterialPath = ArtFolder + "/Materials/M_LightningSMG_Toon.mat";
        public const string ViewModelMaterialPath = ArtFolder + "/Materials/M_LightningSMG_Toon_FP.mat";
        public const string PrefabPath = FireRifleAssetFactory.PrefabFolder + "/PF_Weapon_LightningSMG.prefab";
        public const string MuzzleSocket = FireRifleAssetFactory.MuzzleSocket;
        public const string LeftHandSocket = FireRifleAssetFactory.LeftHandSocket;

        const float WorldOutlineWidth = 0.003f;
        const float ViewModelOutlineWidth = 0.0007f;
        static readonly Color EmissionColor = new Color(1.8f, 2.2f, 2.4f, 1f); // above the bloom threshold, a little blue

        [MenuItem("CoopDeck/Art/Import Lightning SMG", priority = 24)]
        public static void RunFromMenu()
        {
            EditorGUIUtility.PingObject(EnsureAll().Prefab);
        }

        public static FireRifleAssets EnsureAll()
        {
            ConfigureTexture(TexturePath, 4096);
            ConfigureNormalMap();
            EnsureEmissionMap();
            ConfigureModel();
            Material world = EnsureMaterial(MaterialPath, WorldOutlineWidth);
            Material viewModel = EnsureMaterial(ViewModelMaterialPath, ViewModelOutlineWidth);
            GameObject prefab = BuildPrefab(world);
            AssetDatabase.SaveAssets();

            MeshFilter[] filters = prefab.GetComponentsInChildren<MeshFilter>(true);
            string lods = string.Join(", ", filters.Select(f => $"{f.name} {f.sharedMesh.triangles.Length / 3}"));
            Debug.Log($"[CoopDeck] Lightning SMG: {lods} triangles, muzzle at {prefab.transform.Find(MuzzleSocket).localPosition}, " +
                      $"LOD group: {prefab.GetComponent<LODGroup>() != null}");
            return new FireRifleAssets { Prefab = prefab, Material = world, ViewModelMaterial = viewModel };
        }

        // ---------------------------------------------------------------- import

        static void ConfigureTexture(string path, int maxSize)
        {
            var importer = GetImporter<TextureImporter>(path);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.mipmapEnabled = true;
            importer.maxTextureSize = maxSize;
            importer.textureCompression = TextureImporterCompression.CompressedHQ; // BC7: clean painted gradients
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 8;
            importer.SaveAndReimport();
        }

        /// <summary>Tangent-space detail baked from Meshy's 427k-triangle sculpt (rebuild_prop_hq.py).</summary>
        static void ConfigureNormalMap()
        {
            var importer = GetImporter<TextureImporter>(NormalPath);
            importer.textureType = TextureImporterType.NormalMap;
            importer.mipmapEnabled = true;
            importer.maxTextureSize = 4096;
            importer.textureCompression = TextureImporterCompression.CompressedHQ; // BC5
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 8;
            importer.SaveAndReimport();
        }

        /// <summary>
        /// Emission = the base colour where it is a bright cyan (the energy windows on the side and the magazine),
        /// black elsewhere (the orange trim is paint, not light). Rewritten only when the result changes.
        /// </summary>
        static void EnsureEmissionMap()
        {
            var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            source.LoadImage(File.ReadAllBytes(TexturePath));
            Color32[] pixels = source.GetPixels32();
            var glow = new Color32[pixels.Length];
            var black = new Color32(0, 0, 0, 255);
            for (int i = 0; i < pixels.Length; i++)
            {
                glow[i] = IsGlowing(pixels[i]) ? pixels[i] : black;
            }

            var map = new Texture2D(source.width, source.height, TextureFormat.RGB24, false);
            map.SetPixels32(glow);
            map.Apply();
            byte[] png = map.EncodeToPNG();
            Object.DestroyImmediate(source);
            Object.DestroyImmediate(map);

            if (!File.Exists(EmissionPath) || !File.ReadAllBytes(EmissionPath).SequenceEqual(png))
            {
                File.WriteAllBytes(EmissionPath, png);
                AssetDatabase.ImportAsset(EmissionPath, ImportAssetOptions.ForceUpdate);
            }

            ConfigureTexture(EmissionPath, 2048);
        }

        public static bool IsGlowing(Color32 pixel)
        {
            Color.RGBToHSV(pixel, out float hue, out float saturation, out float value);
            return hue >= 0.46f && hue <= 0.6f && saturation >= 0.3f && value >= 0.72f;
        }

        static void ConfigureModel()
        {
            var importer = GetImporter<ModelImporter>(ModelPath);
            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.bakeAxisConversion = true;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importAnimation = false;
            importer.importBlendShapes = false;
            importer.importVisibility = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importNormals = ModelImporterNormals.Import; // hard edges above 40° from Blender
            importer.importTangents = ModelImporterTangents.CalculateMikk; // as Blender baked the normal map
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.isReadable = false;
            importer.weldVertices = true;
            importer.SaveAndReimport(); // ToonOutlineNormalsPostprocessor adds the outline normals (UV3)
        }

        static T GetImporter<T>(string path) where T : AssetImporter
        {
            if (AssetImporter.GetAtPath(path) is T importer)
            {
                return importer;
            }

            throw new FileNotFoundException(path + " is missing: copy it from ArtSource/LightningSMG/Export/Unity (Docs/LIGHTNING_RABBIT.md §3.4)");
        }

        // ---------------------------------------------------------------- generated assets

        static Material EnsureMaterial(string path, float outlineWidth)
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(FoxGunnerAssetFactory.ToonShaderPath);
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                EditorUtil.EnsureFolder(Path.GetDirectoryName(path)?.Replace('\\', '/'));
                material = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
                material.SetColor("_EmissionColor", EmissionColor);
                material.SetFloat("_OutlineWidth", outlineWidth);
                AssetDatabase.CreateAsset(material, path);
            }

            // Tuned colours and widths are kept on reruns; shader, textures and the two features are enforced.
            material.shader = shader;
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath));
            material.SetTexture("_EmissionMap", AssetDatabase.LoadAssetAtPath<Texture2D>(EmissionPath));
            material.SetFloat("_UseEmission", 1f);
            material.EnableKeyword("_EMISSION");
            material.SetFloat("_OutlineUV3Normals", 1f);
            material.EnableKeyword("_OUTLINE_UV3_NORMALS");
            material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(NormalPath));
            material.SetFloat("_BumpScale", 1f);
            material.SetFloat("_UseNormalMap", 1f);
            material.EnableKeyword("_NORMALMAP");
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>A prefab variant of the FBX with the toon material on both LODs; the sockets face the SMG's +Z.</summary>
        static GameObject BuildPrefab(Material material)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            instance.name = Path.GetFileNameWithoutExtension(PrefabPath);
            foreach (MeshRenderer renderer in instance.GetComponentsInChildren<MeshRenderer>(true))
            {
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.On;
            }

            foreach (string socket in new[] { MuzzleSocket, LeftHandSocket })
            {
                Transform t = instance.transform.Find(socket);
                if (t == null)
                {
                    throw new InvalidOperationException($"{ModelPath}: socket '{socket}' is missing");
                }

                t.localRotation = Quaternion.identity; // FBX empties can carry the axis conversion
            }

            EditorUtil.EnsureFolder(FireRifleAssetFactory.PrefabFolder);
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(instance, PrefabPath);
            Object.DestroyImmediate(instance);
            return prefab;
        }
    }
}
