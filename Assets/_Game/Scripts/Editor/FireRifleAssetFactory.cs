using System;
using System.IO;
using System.Linq;
using CoopDeck.Animation;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace CoopDeck.EditorTools
{
    public sealed class FireRifleAssets
    {
        public GameObject Prefab;
        /// <summary>World / third-person material.</summary>
        public Material Material;
        /// <summary>First-person material: the rifle sits 0.3–0.6 m from the camera, so its outline is thinner.</summary>
        public Material ViewModelMaterial;
    }

    /// <summary>
    /// Unity side of the Fire Rifle (Meshy multi-image → Blender clean-up with
    /// ArtSource/Tools/prepare_prop_for_unity.py, see Docs/CHARACTER_PIPELINE.md): import settings,
    /// an emission map for the glowing parts, toon materials and the weapon prefab. The FBX origin is the
    /// pistol grip (right hand); the "Muzzle" and "LeftHandGrip" sockets come from the FBX.
    /// </summary>
    public static class FireRifleAssetFactory
    {
        public const string ArtFolder = GamePaths.Root + "/Art/Weapons/FireRifle";
        public const string ModelPath = ArtFolder + "/Models/FireRifle.fbx";
        public const string TexturePath = ArtFolder + "/Textures/T_FireRifle_BaseColor.png";
        public const string EmissionPath = ArtFolder + "/Textures/T_FireRifle_Emission.png";
        public const string MaterialPath = ArtFolder + "/Materials/M_FireRifle_Toon.mat";
        public const string ViewModelMaterialPath = ArtFolder + "/Materials/M_FireRifle_Toon_FP.mat";
        public const string PrefabFolder = GamePaths.Root + "/Prefabs/Weapons";
        public const string PrefabPath = PrefabFolder + "/PF_Weapon_FireRifle.prefab";
        public const string MuzzleSocket = "Muzzle";
        public const string LeftHandSocket = "LeftHandGrip";

        const float WorldOutlineWidth = 0.003f;
        const float ViewModelOutlineWidth = 0.0007f;
        static readonly Color EmissionColor = new Color(2f, 2f, 2f, 1f); // above the bloom threshold (1)

        // Third-person hold: grip this far past the wrist along the hand, muzzle forward and pitched down (low ready).
        const float PalmOffset = 0.06f;
        const float LowReadyPitch = 35f;

        [MenuItem("CoopDeck/Art/Import Fire Rifle", priority = 22)]
        public static void RunFromMenu()
        {
            EditorGUIUtility.PingObject(EnsureAll().Prefab);
        }

        public static void RunBatch()
        {
            try
            {
                EnsureAll();
                Debug.Log("[CoopDeck] Fire Rifle import finished successfully.");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
            }
        }

        public static FireRifleAssets EnsureAll()
        {
            ConfigureTexture(TexturePath, 2048);
            EnsureEmissionMap();
            ConfigureModel();
            Material world = EnsureMaterial(MaterialPath, WorldOutlineWidth);
            Material viewModel = EnsureMaterial(ViewModelMaterialPath, ViewModelOutlineWidth);
            GameObject prefab = BuildPrefab(world);
            AssetDatabase.SaveAssets();

            Mesh mesh = prefab.GetComponentInChildren<MeshFilter>().sharedMesh;
            Debug.Log($"[CoopDeck] Fire Rifle: {mesh.triangles.Length / 3} triangles, {mesh.vertexCount} vertices, " +
                      $"mesh bounds {mesh.bounds.size}, muzzle at {prefab.transform.Find(MuzzleSocket).localPosition}, " +
                      $"outline normals in UV3: {mesh.uv4.Length == mesh.vertexCount}");
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
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.anisoLevel = 2;
            importer.SaveAndReimport();
        }

        /// <summary>
        /// Emission = the base colour where it is a bright, saturated orange-yellow (energy cells, muzzle core,
        /// flame emblem and fins), black elsewhere. Rewritten only when the result changes.
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

            ConfigureTexture(EmissionPath, 1024);
        }

        public static bool IsGlowing(Color32 pixel)
        {
            Color.RGBToHSV(pixel, out float hue, out float saturation, out float value);
            return hue >= 0.05f && hue <= 0.16f && saturation >= 0.55f && value >= 0.8f;
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
            importer.importTangents = ModelImporterTangents.None;
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

            throw new FileNotFoundException(path + " is missing: run ArtSource/Tools/prepare_prop_for_unity.py (Docs/CHARACTER_PIPELINE.md)");
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
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>A prefab variant of the FBX with the toon material; the sockets face the rifle's +Z.</summary>
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

            EditorUtil.EnsureFolder(PrefabFolder);
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(instance, PrefabPath);
            Object.DestroyImmediate(instance);
            return prefab;
        }

        // ---------------------------------------------------------------- holding it

        /// <summary>
        /// Offset of the rifle relative to a Humanoid's right hand: grip in the palm, muzzle forward and pitched
        /// down (low ready) in the body's idle pose. Measured on a posed copy of <paramref name="bodyPrefab"/> so
        /// the body being built keeps its bind pose; the result is meant for a child of the RightHand bone.
        /// </summary>
        public static void RightHandHold(GameObject bodyPrefab, out Vector3 localPosition, out Quaternion localRotation)
        {
            var posed = (GameObject)PrefabUtility.InstantiatePrefab(bodyPrefab);
            try
            {
                posed.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                Animator animator = posed.GetComponent<Animator>();
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.Rebind();
                animator.SetFloat(AnimatorParams.Speed, 0f);
                animator.Update(0f);
                animator.Update(0.5f);

                Transform hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
                Transform lowerArm = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
                Vector3 palm = hand.position + (hand.position - lowerArm.position).normalized * PalmOffset;
                Quaternion lowReady = Quaternion.LookRotation(Quaternion.Euler(LowReadyPitch, 0f, 0f) * Vector3.forward, Vector3.up);
                localPosition = hand.InverseTransformPoint(palm);
                localRotation = Quaternion.Inverse(hand.rotation) * lowReady;
            }
            finally
            {
                Object.DestroyImmediate(posed);
            }
        }
    }
}
