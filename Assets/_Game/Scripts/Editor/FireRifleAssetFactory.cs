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
    /// Unity side of the Fire Rifle (Meshy multi-image → Blender rebuild from the sculpt with
    /// ArtSource/Tools/rebuild_weapon_hq.py, see Docs/WEAPON_VISUAL_QUALITY.md): import settings, an emission map for
    /// the glowing parts, the hard-surface materials (CoopDeck/Weapon/HardSurfaceToon, third person and a
    /// thin-outline first-person one) and the weapon prefab. The FBX holds FireRifle_Body_LOD0 (about 110k
    /// triangles, always seen in first person) and _LOD1 (30k), so Unity builds a LOD group; its origin is the pistol
    /// grip (right hand) and the "Muzzle" and "LeftHandGrip" sockets come from the FBX.
    /// </summary>
    public static class FireRifleAssetFactory
    {
        public const string ArtFolder = GamePaths.Root + "/Art/Weapons/FireRifle";
        public const string ModelPath = ArtFolder + "/Models/FireRifle.fbx";
        public const string TexturePath = ArtFolder + "/Textures/T_FireRifle_BaseColor.png";
        public const string EmissionPath = ArtFolder + "/Textures/T_FireRifle_Emission.png";
        public const string NormalPath = ArtFolder + "/Textures/T_FireRifle_Normal.png"; // rebuild_weapon_hq.py
        /// <summary>R convex edges, G ambient occlusion, B concave creases (linear, from the sculpt).</summary>
        public const string SurfacePath = ArtFolder + "/Textures/T_FireRifle_Surface.png";
        /// <summary>R white paint, G red paint, B dark polymer, A bare metal; the rest is the gold accent (linear).</summary>
        public const string MaterialMaskPath = ArtFolder + "/Textures/T_FireRifle_MaterialMask.png";
        public const string MaterialPath = ArtFolder + "/Materials/M_FireRifle_Toon.mat";
        public const string ViewModelMaterialPath = ArtFolder + "/Materials/M_FireRifle_Toon_FP.mat";
        public const string WeaponShaderPath = HardSurfaceWeapon.ShaderPath;
        public const string PrefabFolder = GamePaths.Root + "/Prefabs/Weapons";
        public const string PrefabPath = PrefabFolder + "/PF_Weapon_FireRifle.prefab";
        public const string MuzzleSocket = "Muzzle";
        public const string LeftHandSocket = "LeftHandGrip";

        // Fire Rifle: red paint and gold accents (the mask's default hue ranges); emission above the bloom threshold (1)
        static readonly HardSurfaceWeapon.Look Look = new HardSurfaceWeapon.Look();
        static HardSurfaceWeapon.Maps Maps => new HardSurfaceWeapon.Maps
        {
            BaseColor = TexturePath, Normal = NormalPath, Emission = EmissionPath, MaterialMask = MaterialMaskPath, Surface = SurfacePath,
        };

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
            ConfigureTexture(TexturePath, 4096);
            ConfigureNormalMap();
            HardSurfaceWeapon.ConfigureDataTexture(SurfacePath, 4096, false);
            HardSurfaceWeapon.ConfigureDataTexture(MaterialMaskPath, 2048, true);
            EnsureEmissionMap();
            ConfigureModel();
            Material world = HardSurfaceWeapon.EnsureMaterial(MaterialPath, Maps, Look, HardSurfaceWeapon.View.ThirdPerson);
            Material viewModel = HardSurfaceWeapon.EnsureMaterial(ViewModelMaterialPath, Maps, Look, HardSurfaceWeapon.View.FirstPerson);
            GameObject prefab = BuildPrefab(world);
            AssetDatabase.SaveAssets();

            MeshFilter[] filters = prefab.GetComponentsInChildren<MeshFilter>(true);
            string lods = string.Join(", ", filters.Select(f => $"{f.name} {f.sharedMesh.triangles.Length / 3} triangles / {f.sharedMesh.vertexCount} vertices"));
            Mesh mesh = filters[0].sharedMesh;
            Debug.Log($"[CoopDeck] Fire Rifle: {lods}, mesh bounds {mesh.bounds.size}, muzzle at {prefab.transform.Find(MuzzleSocket).localPosition}, " +
                      $"outline normals in UV3: {mesh.uv4.Length == mesh.vertexCount}, LOD group: {prefab.GetComponent<LODGroup>() != null}");
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

        /// <summary>Tangent-space detail baked from Meshy's 437k-triangle sculpt (rebuild_weapon_hq.py).</summary>
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

            ConfigureTexture(EmissionPath, 2048);
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
            importer.importNormals = ModelImporterNormals.Import; // weighted normals, hard edges above 40° (Blender)
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

            throw new FileNotFoundException(path + " is missing: run ArtSource/Tools/refine_prop.py (Docs/CHARACTER_PIPELINE.md)");
        }

        // ---------------------------------------------------------------- generated assets

        /// <summary>A prefab variant of the FBX with the weapon material on both LODs; the sockets face the rifle's +Z.</summary>
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

            HardSurfaceWeapon.SetLodHeights(instance); // LOD0 above 12% of the screen height (first person always)

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
