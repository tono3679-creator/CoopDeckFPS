using System;
using System.IO;
using System.Linq;
using CoopDeck.Animation;
using CoopDeck.Characters;
using CoopDeck.Core;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace CoopDeck.EditorTools
{
    public sealed class LightningRabbitAssets
    {
        public GameObject BodyPrefab;
        public Avatar Avatar;
        public FoxGunnerAnimationSet Animations;
        public Material SoftToon;
        public Material Outline;
        public Material FirstPerson;
        public Material Glow;
        public Material FirstPersonGlow;
        public LightningRabbitLookFactory.FxSet Fx;
    }

    /// <summary>
    /// Unity side of the Lightning Rabbit, the second playable character (Meshy → Blender rebuild, rig and clips in
    /// ArtSource/Tools/rabbit, see Docs/LIGHTNING_RABBIT.md): import settings (Humanoid with the Meshy bone map the fox
    /// uses, three LODs), the soft toon look with the per-part surface mask and the glowing marks (LightningRabbitLookFactory),
    /// the clips and controller (the fox's clip set and layout, solved on the
    /// rabbit holding the Lightning SMG: <see cref="FoxGunnerAnimationFactory"/> with <see cref="AnimSpec"/>), the
    /// body prefab (hand IK, look-at, tail, three-joint ears and scarf tails on springs) and the character definitions.
    /// The FBX/PNG files are copied from ArtSource/LightningRabbit/Export/Unity.
    /// </summary>
    public static class LightningRabbitAssetFactory
    {
        public const string Id = "LightningRabbit";
        public const string ArtFolder = GamePaths.Root + "/Art/Characters/LightningRabbit";
        public const string ModelPath = ArtFolder + "/Models/LightningRabbit.fbx";
        public const string FirstPersonArmsPath = ArtFolder + "/Models/LightningRabbit_FPArms.fbx";
        public const string TexturePath = ArtFolder + "/Textures/T_LightningRabbit_BaseColor.png";
        public const string NormalMapPath = ArtFolder + "/Textures/T_LightningRabbit_Normal.png";
        public const string SurfaceMaskPath = ArtFolder + "/Textures/T_LightningRabbit_SurfaceMask.png";
        public const string FirstPersonBaseColorPath = ArtFolder + "/Textures/T_LightningRabbit_FPArms_BaseColor.png";
        public const string FirstPersonNormalPath = ArtFolder + "/Textures/T_LightningRabbit_FPArms_Normal.png";
        public const string SoftToonMaterialPath = ArtFolder + "/Materials/M_LightningRabbit_SoftToon.mat";
        public const string OutlineMaterialPath = ArtFolder + "/Materials/M_LightningRabbit_Outline.mat";
        public const string FirstPersonMaterialPath = ArtFolder + "/Materials/M_LightningRabbit_SoftToon_FirstPerson.mat";
        public const string AnimFolder = GamePaths.Root + "/Animations/LightningRabbit";
        public const string Prefix = "A_LightningRabbit_";
        public const string PrefabPath = GamePaths.CharacterPrefabs + "/PF_LightningRabbit_Body.prefab";
        public const string FoxDefinitionPath = GamePaths.CharacterData + "/CD_FoxGunner.asset";
        public const string DefinitionPath = GamePaths.CharacterData + "/CD_LightningRabbit.asset";

        /// <summary>Eye height of the rabbit's model (iris above the ground in the bind pose), for the look-at.</summary>
        public const float EyeHeight = 1.26f;

        /// <summary>LOD transitions (screen height share): the 160k body up close, 40k across the arena, 15k far.</summary>
        static readonly float[] LodScreenHeights = { 0.3f, 0.09f, 0.008f };

        public static readonly FoxGunnerAnimationFactory.CharacterAnimSpec AnimSpec = new FoxGunnerAnimationFactory.CharacterAnimSpec
        {
            ModelPath = ModelPath,
            SetPath = AnimFolder + "/A_LightningRabbit_Set.fbx",
            ClipTablePath = AnimFolder + "/A_LightningRabbit_Clips.json",
            UpperMaskPath = AnimFolder + "/AM_LightningRabbit_UpperBody.mask",
            ClipMaskPath = AnimFolder + "/AM_LightningRabbit_ClipImport.mask",
            ControllerPath = AnimFolder + "/AC_LightningRabbit_TP.controller",
            Prefix = Prefix,
            BuildTool = "ArtSource/Tools/rabbit/rabbit_build_clips.py",
        };

        /// <summary>
        /// The first-person arms: cut from the rabbit's sculpt (rabbit_fp_arms.py). The rabbit's real eyes sit 18 cm in
        /// front of its Head bone (a big head on a short neck), which would put its shoulders behind the camera and the
        /// SMG out of reach of its short arms; the view uses eyes just above the Head bone, as the fox's do.
        /// </summary>
        public static readonly FoxGunnerAnimationFactory.FirstPersonSpec FirstPerson = new FoxGunnerAnimationFactory.FirstPersonSpec
        {
            ArmsPath = FirstPersonArmsPath, MaterialPath = FirstPersonMaterialPath, ViewWeaponName = "LightningSMG", BodyWeaponName = "HeldSMG",
            EyeAboveHeadBone = 0.16f, EyeInFrontOfHeadBone = 0.0f,
            TorsoYaw = FoxGunnerAnimationFactory.FirstPersonTorsoYaw, TorsoLean = FoxGunnerAnimationFactory.FirstPersonTorsoLean,
            LeftElbow = FoxGunnerAnimationFactory.FirstPersonLeftElbow, RightElbow = FoxGunnerAnimationFactory.FirstPersonRightElbow,
            BuildTool = "ArtSource/Tools/rabbit/rabbit_fp_arms.py",
        };

        /// <summary>
        /// The SMG in the first-person view: grip right of and below the eyes like the fox's rifle, nearer and lower, the
        /// barrel along the camera's forward (yaw/pitch as the fox's, CharacterPrefabBuilder.ViewModelRifleYaw) so its line
        /// runs into the crosshair and tracers leave along it (a 10 deg turn pointed it left of where the bullets go,
        /// 2026-11-08). The short left arm reaches the forward foregrip with its elbow under the lower edge of the screen
        /// (LightningRabbitPlayModeTests checks it).
        /// </summary>
        public static readonly CharacterPrefabBuilder.ViewModelLayout ViewModel = new CharacterPrefabBuilder.ViewModelLayout
        {
            GripInCamera = new Vector3(0.13f, -0.25f, 0.33f), Pitch = CharacterPrefabBuilder.ViewModelRiflePitch, Yaw = CharacterPrefabBuilder.ViewModelRifleYaw,
        };

        [MenuItem("CoopDeck/Art/Import Lightning Rabbit", priority = 25)]
        public static void RunFromMenu()
        {
            EditorGUIUtility.PingObject(EnsureAll().BodyPrefab);
        }

        public static LightningRabbitAssets EnsureAll()
        {
            ConfigureColorTexture(TexturePath);
            ConfigureNormalMap(NormalMapPath);
            LightningRabbitLookFactory.ConfigureMasks(); // baked by rabbit_look.py
            Avatar avatar = ConfigureModel();
            FoxGunnerAnimationSet animations = FoxGunnerAnimationFactory.EnsureAll(avatar, AnimSpec);
            ConfigureColorTexture(FirstPersonBaseColorPath);
            ConfigureNormalMap(FirstPersonNormalPath);
            Material softToon = EnsureSoftToon();
            Material outline = EnsureOutline();
            Material firstPerson = EnsureFirstPersonMaterial(softToon);
            Material glow = LightningRabbitLookFactory.EnsureGlowMaterial(LightningRabbitLookFactory.GlowMaterialPath, TexturePath,
                LightningRabbitLookFactory.GlowMaskPath);
            Material firstPersonGlow = LightningRabbitLookFactory.EnsureGlowMaterial(LightningRabbitLookFactory.FirstPersonGlowMaterialPath,
                FirstPersonBaseColorPath, LightningRabbitLookFactory.FirstPersonGlowMaskPath);
            LightningRabbitLookFactory.FxSet fx = LightningRabbitLookFactory.EnsureFx();
            GameObject prefab = BuildBodyPrefab(new[] { softToon, outline }, glow, animations.Controller);
            AssetDatabase.SaveAssets();
            LogSummary(prefab, avatar);
            return new LightningRabbitAssets
            {
                BodyPrefab = prefab, Avatar = avatar, Animations = animations, SoftToon = softToon, Outline = outline, FirstPerson = firstPerson,
                Glow = glow, FirstPersonGlow = firstPersonGlow, Fx = fx,
            };
        }

        // ---------------------------------------------------------------- import settings

        static void ConfigureColorTexture(string path)
        {
            var importer = FoxGunnerAssetFactory.GetImporter<TextureImporter>(path);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.mipmapEnabled = true;
            importer.maxTextureSize = 4096; // even texel density at 4096 (refine_character.py, rabbit_fp_arms.py)
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 8;
            importer.SaveAndReimport();
        }

        static void ConfigureNormalMap(string path)
        {
            var importer = FoxGunnerAssetFactory.GetImporter<TextureImporter>(path);
            importer.textureType = TextureImporterType.NormalMap;
            importer.mipmapEnabled = true;
            importer.maxTextureSize = 4096;
            importer.textureCompression = TextureImporterCompression.CompressedHQ; // BC5
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 8;
            importer.SaveAndReimport();
        }

        static Avatar ConfigureModel()
        {
            var importer = FoxGunnerAssetFactory.GetImporter<ModelImporter>(ModelPath);
            FoxGunnerAssetFactory.ApplyCommonModelSettings(importer);
            importer.importAnimation = false;
            importer.isReadable = false;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.optimizeMeshPolygons = true;
            importer.optimizeMeshVertices = true;
            importer.weldVertices = true;
            importer.importNormals = ModelImporterNormals.Import;   // smooth normals from Blender
            importer.importTangents = ModelImporterTangents.CalculateMikk; // as Blender baked T_LightningRabbit_Normal
            importer.skinWeights = ModelImporterSkinWeights.Standard;
            importer.optimizeGameObjects = false;                  // keep bone transforms for IK, sockets and springs
            FoxGunnerAssetFactory.ApplyHumanoid(importer, null);
            return FoxGunnerAssetFactory.LoadAvatar(ModelPath);
        }

        // ---------------------------------------------------------------- materials

        /// <summary>The fox's tuned soft toon (shader and values) with the rabbit's textures; values tuned later are kept.</summary>
        static Material EnsureSoftToon()
        {
            Material m = EnsureCopy(SoftToonMaterialPath, FoxGunnerLookFactory.SoftToonMaterialPath);
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath));
            m.SetTexture("_SurfaceMask", AssetDatabase.LoadAssetAtPath<Texture2D>(SurfaceMaskPath));
            m.SetTexture("_NormalMap", AssetDatabase.LoadAssetAtPath<Texture2D>(NormalMapPath));
            m.SetFloat("_NormalStrength", 1f);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material EnsureOutline() => EnsureCopy(OutlineMaterialPath, FoxGunnerLookFactory.OutlineMaterialPath);

        static Material EnsureFirstPersonMaterial(Material softToon)
        {
            Material m = EnsureCopy(FirstPersonMaterialPath, SoftToonMaterialPath);
            m.CopyPropertiesFromMaterial(softToon);
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(FirstPersonBaseColorPath));
            m.SetTexture("_NormalMap", AssetDatabase.LoadAssetAtPath<Texture2D>(FirstPersonNormalPath));
            m.SetTexture("_SurfaceMask", AssetDatabase.LoadAssetAtPath<Texture2D>(LightningRabbitLookFactory.FirstPersonSurfaceMaskPath));
            m.SetFloat("_NormalStrength", 1f);
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Creates <paramref name="path"/> as a copy of <paramref name="source"/> once; afterwards only the shader is enforced.</summary>
        static Material EnsureCopy(string path, string source)
        {
            var template = AssetDatabase.LoadAssetAtPath<Material>(source);
            if (template == null)
            {
                throw new FileNotFoundException(source + " is missing: run CoopDeck > Art > Import Fox Gunner first");
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                EditorUtil.EnsureFolder(Path.GetDirectoryName(path)?.Replace('\\', '/'));
                material = new Material(template) { name = Path.GetFileNameWithoutExtension(path) };
                AssetDatabase.CreateAsset(material, path);
            }

            material.shader = template.shader;
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        // ---------------------------------------------------------------- body prefab

        /// <summary>A prefab variant of the FBX: Animator with the controller, the given materials on every LOD, no root motion.</summary>
        static GameObject BuildBodyPrefab(Material[] materials, Material glow, AnimatorController controller)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            instance.name = "LightningRabbit_Body";

            Animator animator = instance.GetComponent<Animator>();
            if (animator == null)
            {
                animator = instance.AddComponent<Animator>();
            }

            animator.avatar = FoxGunnerAssetFactory.LoadAvatar(ModelPath);
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

            SkinnedMeshRenderer[] skins = instance.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (SkinnedMeshRenderer skinned in skins)
            {
                skinned.sharedMaterials = materials; // one submesh: the second material redraws it (the outline)
                skinned.updateWhenOffscreen = false;
                skinned.skinnedMotionVectors = true;
                skinned.shadowCastingMode = ShadowCastingMode.On;
                skinned.receiveShadows = true;
            }

            ConfigureLods(instance, skins);
            AddGlow(instance, skins, glow);

            Transform root = instance.transform;
            FoxBodyAnimator look = FoxGunnerAnimationFactory.SetupHandIkAndLookAt(instance);
            EditorUtil.SetFloat(look, "eyeHeight", EyeHeight);

            SkinnedMeshRenderer lod0 = skins.OrderByDescending(s => s.sharedMesh.vertexCount).First();
            var tail = instance.AddComponent<FoxSecondaryMotion>(); // the tail only: the ears are chains below
            tail.Configure(null, Bone(root, "Hips"), new[] { Bone(root, "Tail01"), Bone(root, "Tail02") }, null, null, lod0);

            var chains = instance.AddComponent<ChainSecondaryMotion>();
            chains.Configure(null, lod0, new[]
            {
                Chain("Ear_L", ChainSecondaryMotion.ChainKind.Ear, -1f, root, "Ear_L1", "Ear_L2", "Ear_L3"),
                Chain("Ear_R", ChainSecondaryMotion.ChainKind.Ear, 1f, root, "Ear_R1", "Ear_R2", "Ear_R3"),
                Chain("Scarf_F", ChainSecondaryMotion.ChainKind.Cloth, 1f, root, "Scarf_F1", "Scarf_F2", "Scarf_F3", "Scarf_F4"),
                Chain("Scarf_B", ChainSecondaryMotion.ChainKind.Cloth, 1f, root, "Scarf_B1", "Scarf_B2", "Scarf_B3", "Scarf_B4"),
            });
            FoxGunnerAnimationFactory.FinishBody(instance);

            EditorUtil.EnsureFolder(GamePaths.CharacterPrefabs);
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(instance, PrefabPath);
            Object.DestroyImmediate(instance);
            return prefab;
        }

        /// <summary>The importer's LOD group (meshes named _LOD0.._LOD2) with the rabbit's transitions.</summary>
        static void ConfigureLods(GameObject instance, SkinnedMeshRenderer[] skins)
        {
            LODGroup group = instance.GetComponent<LODGroup>();
            if (group == null)
            {
                group = instance.AddComponent<LODGroup>();
            }

            Renderer[] Lod(int i) => skins.Where(s => s.name.EndsWith("_LOD" + i, StringComparison.Ordinal)).Cast<Renderer>().ToArray();
            var lods = Enumerable.Range(0, LodScreenHeights.Length)
                .Select(i => new LOD(LodScreenHeights[i], Lod(i)))
                .Where(l => l.renderers.Length > 0)
                .ToArray();
            if (lods.Length != LodScreenHeights.Length)
            {
                throw new InvalidOperationException($"{ModelPath}: expected meshes ending _LOD0.._LOD{LodScreenHeights.Length - 1} " +
                                                    $"(found {string.Join(", ", skins.Select(s => s.name))}); re-run rabbit_rig.py");
            }

            group.SetLODs(lods);
            group.RecalculateBounds();
        }

        /// <summary>
        /// The glowing marks: per LOD a copy of its glowing triangles (CH_GlowLines, same bones) in that LOD, the glove
        /// lens points on both hands and the ear tips (where ElectricBodyFx plays sparks and arcs).
        /// </summary>
        static void AddGlow(GameObject instance, SkinnedMeshRenderer[] skins, Material glow)
        {
            float[] mask = LightningRabbitLookFactory.ReadMask(LightningRabbitLookFactory.GlowMaskPath, out int size);
            LODGroup group = instance.GetComponent<LODGroup>();
            LOD[] lods = group.GetLODs();
            SkinnedMeshRenderer lod0Glow = null;
            for (int i = 0; i < lods.Length; i++)
            {
                SkinnedMeshRenderer source = skins.First(s => s.name.EndsWith("_LOD" + i, StringComparison.Ordinal));
                Mesh mesh = LightningRabbitLookFactory.BuildGlowMesh(source.sharedMesh, mask, size,
                    $"{LightningRabbitLookFactory.MeshFolder}/LightningRabbit_Glow_LOD{i}.asset");
                SkinnedMeshRenderer copy = LightningRabbitLookFactory.AddGlowRenderer(source, mesh, glow);
                lods[i].renderers = lods[i].renderers.Append(copy).ToArray();
                Debug.Log("[CoopDeck] Lightning Rabbit glow: " + LightningRabbitLookFactory.Describe(copy, source));
                if (i == 0)
                {
                    lod0Glow = copy;
                }
            }

            group.SetLODs(lods);
            SkinnedMeshRenderer lod0 = skins.First(s => s.name.EndsWith("_LOD0", StringComparison.Ordinal));
            Transform root = instance.transform;
            LightningRabbitLookFactory.AddGlowMarker(lod0Glow, Bone(root, "LeftHand"), LightningRabbitLookFactory.LeftLensName);
            LightningRabbitLookFactory.AddGlowMarker(lod0Glow, Bone(root, "RightHand"), LightningRabbitLookFactory.RightLensName);
            LightningRabbitLookFactory.AddTipMarker(lod0, Bone(root, "Ear_L3"), LightningRabbitLookFactory.LeftEarTipName);
            LightningRabbitLookFactory.AddTipMarker(lod0, Bone(root, "Ear_R3"), LightningRabbitLookFactory.RightEarTipName);
        }

        static ChainSecondaryMotion.Chain Chain(string name, ChainSecondaryMotion.ChainKind kind, float side, Transform root, params string[] bones)
        {
            return new ChainSecondaryMotion.Chain { name = name, kind = kind, side = side, bones = bones.Select(b => Bone(root, b)).ToArray() };
        }

        static Transform Bone(Transform root, string name)
        {
            Transform t = FoxGunnerAnimationFactory.FindBone(root, name);
            if (t == null)
            {
                throw new InvalidOperationException($"{ModelPath}: bone '{name}' is missing (re-run rabbit_rig.py)");
            }

            return t;
        }

        static void LogSummary(GameObject prefab, Avatar avatar)
        {
            SkinnedMeshRenderer[] skins = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            string lods = string.Join(", ", skins.Select(s => $"{s.name} {s.sharedMesh.triangles.Length / 3}"));
            Debug.Log($"[CoopDeck] Lightning Rabbit: {lods} triangles, {skins[0].bones.Length} bones, " +
                      $"avatar valid={avatar.isValid} human={avatar.isHuman}");
        }

        // ---------------------------------------------------------------- kit

        /// <summary>The rabbit's kit for the player (body, Lightning SMG, first-person arms) with its glow and lightning.</summary>
        public static CharacterKit BuildKit(LightningRabbitAssets rabbit, FireRifleAssets smg)
        {
            CharacterKit kit = CharacterPrefabBuilder.BuildCharacterKit(Id, rabbit.BodyPrefab, smg, "LightningSMG", "HeldSMG", AnimSpec,
                FirstPerson, ViewModel);
            LightningRabbitLookFactory.DecorateKit(kit, rabbit.Fx, rabbit.FirstPersonGlow);
            return AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GetAssetPath(kit)).GetComponent<CharacterKit>();
        }

        // ---------------------------------------------------------------- character definitions

        /// <summary>The playable characters: the fox (the player prefab's own body) and the rabbit (its kit).</summary>
        public static CharacterDefinition[] EnsureDefinitions(CharacterKit rabbitKit)
        {
            CharacterDefinition fox = EnsureDefinition(FoxDefinitionPath);
            fox.Configure("FoxGunner", "Fox Gunner", Element.Fire, null);
            EditorUtility.SetDirty(fox);
            CharacterDefinition rabbit = EnsureDefinition(DefinitionPath);
            rabbit.Configure(Id, "Lightning Rabbit", Element.Lightning, rabbitKit);
            EditorUtility.SetDirty(rabbit);
            AssetDatabase.SaveAssets();
            return new[] { fox, rabbit };
        }

        static CharacterDefinition EnsureDefinition(string path)
        {
            var definition = AssetDatabase.LoadAssetAtPath<CharacterDefinition>(path);
            if (definition == null)
            {
                EditorUtil.EnsureFolder(GamePaths.CharacterData);
                definition = ScriptableObject.CreateInstance<CharacterDefinition>();
                AssetDatabase.CreateAsset(definition, path);
            }

            return definition;
        }
    }
}
