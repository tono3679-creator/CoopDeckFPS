using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CoopDeck.Animation;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace CoopDeck.EditorTools
{
    public sealed class FoxGunnerAssets
    {
        public GameObject BodyPrefab;
        public AnimatorController Controller;
        public Material Material;
        public Avatar Avatar;
    }

    /// <summary>
    /// Unity side of the Fox Gunner player model (Meshy multi-image → rig → Blender clean-up,
    /// see Docs/CHARACTER_PIPELINE.md). The FBX/PNG files come from ArtSource/Tools/prepare_for_unity.py;
    /// this sets their import settings (Humanoid with an explicit bone map) and generates the toon material,
    /// an idle clip, the Animator Controller and the body prefab.
    ///
    /// Batch mode:
    ///   Unity.exe -batchmode -quit -projectPath . -executeMethod CoopDeck.EditorTools.FoxGunnerAssetFactory.RunBatch
    ///   (add -coopdeckOverwriteAnimations to rebuild the idle clip and the controller)
    /// </summary>
    public static class FoxGunnerAssetFactory
    {
        public const string ArtFolder = GamePaths.Root + "/Art/Characters/FoxGunner";
        public const string ModelPath = ArtFolder + "/Models/FoxGunner.fbx";
        public const string TexturePath = ArtFolder + "/Textures/T_FoxGunner_BaseColor.png";
        public const string MaterialPath = ArtFolder + "/Materials/M_FoxGunner_Toon.mat";
        public const string ToonShaderPath = GamePaths.Root + "/Art/Characters/Shaders/CH_Toon.shader";
        public const string AnimFolder = GamePaths.Root + "/Animations/FoxGunner";
        public const string WalkPath = AnimFolder + "/A_FoxGunner_Walk.fbx";
        public const string RunPath = AnimFolder + "/A_FoxGunner_Run.fbx";
        public const string IdlePath = AnimFolder + "/A_FoxGunner_Idle.anim";
        public const string ControllerPath = AnimFolder + "/AC_FoxGunner_TP.controller";
        public const string PrefabPath = GamePaths.CharacterPrefabs + "/PF_FoxGunner_Body.prefab";
        public const string PreviewPath = "Screenshots/FoxGunner_Preview.png";

        public const string WalkClipName = "A_FoxGunner_Walk";
        public const string RunClipName = "A_FoxGunner_Run";

        // Ground speeds the in-place clips were authored for: speed of the planted foot, measured in Blender.
        public const float WalkClipSpeed = 1.2f;
        public const float RunClipSpeed = 4.0f;

        // The player walks at 6.5 m/s and sprints at 9.5 m/s, faster than the run clip. Extra Run children every
        // 1 m/s up to this speed play at threshold / RunClipSpeed, so the cycle speeds up with the ground speed and
        // the planted foot keeps pace. (A 1D blend mixes clip lengths, not rates, so two far-apart children would
        // under-speed the middle: 1.27x instead of 1.63x at 6.5 m/s. 1 m/s steps keep the error near 1%.)
        public const float FastRunThreshold = 12f;
        public const float RunStep = 1f;

        /// <summary>Unity humanoid bone → Meshy rig bone. Meshy numbers the spine top-down (Spine02 sits on the hips).</summary>
        public static readonly IReadOnlyList<KeyValuePair<string, string>> HumanBoneMap = new[]
        {
            Pair("Hips", "Hips"), Pair("Spine", "Spine02"), Pair("Chest", "Spine01"), Pair("UpperChest", "Spine"),
            Pair("Neck", "neck"), Pair("Head", "Head"),
            Pair("LeftShoulder", "LeftShoulder"), Pair("LeftUpperArm", "LeftArm"), Pair("LeftLowerArm", "LeftForeArm"), Pair("LeftHand", "LeftHand"),
            Pair("RightShoulder", "RightShoulder"), Pair("RightUpperArm", "RightArm"), Pair("RightLowerArm", "RightForeArm"), Pair("RightHand", "RightHand"),
            Pair("LeftUpperLeg", "LeftUpLeg"), Pair("LeftLowerLeg", "LeftLeg"), Pair("LeftFoot", "LeftFoot"), Pair("LeftToes", "LeftToeBase"),
            Pair("RightUpperLeg", "RightUpLeg"), Pair("RightLowerLeg", "RightLeg"), Pair("RightFoot", "RightFoot"), Pair("RightToes", "RightToeBase"),
        };

        static KeyValuePair<string, string> Pair(string human, string bone) => new KeyValuePair<string, string>(human, bone);

        [MenuItem("CoopDeck/Art/Import Fox Gunner", priority = 20)]
        public static void RunFromMenu()
        {
            FoxGunnerAssets assets = EnsureAll(overwriteAnimations: false);
            EditorGUIUtility.PingObject(assets.BodyPrefab);
        }

        public static void RunBatch()
        {
            try
            {
                bool overwrite = Array.IndexOf(Environment.GetCommandLineArgs(), "-coopdeckOverwriteAnimations") >= 0;
                EnsureAll(overwrite);
                CapturePreview(PreviewPath);
                Debug.Log("[CoopDeck] Fox Gunner import finished successfully.");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
            }
        }

        public static FoxGunnerAssets EnsureAll(bool overwriteAnimations)
        {
            ConfigureTexture();
            Avatar avatar = ConfigureModel();
            AnimationClip walk = ConfigureClip(WalkPath, WalkClipName);
            AnimationClip run = ConfigureClip(RunPath, RunClipName);
            AnimationClip idle = EnsureIdleClip(walk, overwriteAnimations);
            AnimatorController controller = EnsureController(idle, walk, run, overwriteAnimations);
            Material material = EnsureMaterial(); // the original cel material, kept for comparison (and backed up)
            // The body wears the soft-toon look (Shader Graph + surface mask) and a thin outline as a second material.
            FoxGunnerLookFactory.Look look = FoxGunnerLookFactory.EnsureAll(rebuildMask: false);
            GameObject prefab = BuildBodyPrefab(new[] { look.SoftToon, look.Outline }, controller);
            AssetDatabase.SaveAssets();

            LogSummary(prefab, avatar);
            return new FoxGunnerAssets { BodyPrefab = prefab, Controller = controller, Material = material, Avatar = avatar };
        }

        /// <summary>Size and facing of the bind pose, measured on the skinned result (sharedMesh.bounds is in bind space).</summary>
        static void LogSummary(GameObject prefab, Avatar avatar)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            try
            {
                SkinnedMeshRenderer skinned = instance.GetComponentInChildren<SkinnedMeshRenderer>();
                var baked = new Mesh();
                skinned.BakeMesh(baked, true);
                Matrix4x4 toWorld = skinned.transform.localToWorldMatrix;
                float minY = float.MaxValue, maxY = float.MinValue;
                foreach (Vector3 v in baked.vertices)
                {
                    float y = toWorld.MultiplyPoint3x4(v).y;
                    minY = Mathf.Min(minY, y);
                    maxY = Mathf.Max(maxY, y);
                }

                Object.DestroyImmediate(baked);
                Animator animator = instance.GetComponent<Animator>();
                Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
                Transform front = instance.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == "headfront");
                string facing = front == null ? "?" : front.position.z > head.position.z ? "+Z" : "-Z";
                Mesh mesh = skinned.sharedMesh;
                Debug.Log($"[CoopDeck] Fox Gunner: {mesh.triangles.Length / 3} triangles, {mesh.vertexCount} vertices, " +
                          $"{skinned.bones.Length} bones, height {maxY - minY:0.00} m (feet at {minY:0.00}), faces {facing}, " +
                          $"avatar valid={avatar.isValid} human={avatar.isHuman}");
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        // ---------------------------------------------------------------- import settings

        static void ConfigureTexture()
        {
            var importer = GetImporter<TextureImporter>(TexturePath);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.mipmapEnabled = true;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.anisoLevel = 2;
            importer.SaveAndReimport();
        }

        static Avatar ConfigureModel()
        {
            var importer = GetImporter<ModelImporter>(ModelPath);
            ApplyCommonModelSettings(importer);
            importer.importAnimation = false;
            importer.isReadable = false;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.optimizeMeshPolygons = true;
            importer.optimizeMeshVertices = true;
            importer.weldVertices = true;
            importer.importNormals = ModelImporterNormals.Import;   // smooth normals baked in Blender
            importer.importTangents = ModelImporterTangents.None;   // no normal map
            importer.skinWeights = ModelImporterSkinWeights.Standard; // 4 bones per vertex
            importer.optimizeGameObjects = false;                  // keep bone transforms for IK and sockets
            ApplyHumanoid(importer, null);
            return LoadAvatar(ModelPath);
        }

        static AnimationClip ConfigureClip(string path, string clipName)
        {
            var importer = GetImporter<ModelImporter>(path);
            ApplyCommonModelSettings(importer);
            importer.importAnimation = true;
            importer.resampleCurves = true;
            importer.animationCompression = ModelImporterAnimationCompression.Optimal;
            ApplyHumanoid(importer, i =>
            {
                ModelImporterClipAnimation[] existing = i.clipAnimations.Length > 0 ? i.clipAnimations : i.defaultClipAnimations;
                if (existing.Length == 0)
                {
                    throw new InvalidOperationException(path + " has no animation take");
                }

                ModelImporterClipAnimation clip = existing[0];
                clip.name = clipName;
                clip.loopTime = true;
                clip.loopPose = true;
                // Motor-driven locomotion: bake the root into the pose (no root motion), feet stay on the ground.
                clip.lockRootRotation = true;
                clip.keepOriginalOrientation = true;
                clip.lockRootHeightY = true;
                clip.keepOriginalPositionY = true;
                clip.lockRootPositionXZ = true;
                clip.keepOriginalPositionXZ = false; // centre of mass over the root
                clip.heightFromFeet = false;
                i.clipAnimations = new[] { clip };
            });

            AnimationClip result = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                .FirstOrDefault(c => c.name == clipName);
            if (result == null || !result.isHumanMotion)
            {
                throw new InvalidOperationException($"{path}: humanoid clip '{clipName}' was not imported");
            }

            return result;
        }

        static void ApplyCommonModelSettings(ModelImporter importer)
        {
            importer.globalScale = 1f;
            importer.useFileScale = true;      // Blender exports metres
            importer.bakeAxisConversion = true; // no -90° X rotation on the root
            importer.importBlendShapes = false;
            importer.importVisibility = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None; // the toon material is assigned by the prefab
        }

        /// <summary>
        /// Humanoid with the explicit bone map. The first import lets Unity collect the skeleton (bind pose);
        /// the second replaces the automatic mapping, which would guess the reversed spine names.
        /// </summary>
        static void ApplyHumanoid(ModelImporter importer, Action<ModelImporter> beforeFinalImport)
        {
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.autoGenerateAvatarMappingIfUnspecified = true;
            // Drop the skeleton stored by an earlier import: after a re-export it no longer matches the file.
            HumanDescription stale = importer.humanDescription;
            stale.human = Array.Empty<HumanBone>();
            stale.skeleton = Array.Empty<SkeletonBone>();
            importer.humanDescription = stale;
            importer.SaveAndReimport();

            HumanDescription description = importer.humanDescription;
            description.human = HumanBoneMap
                .Select(p => new HumanBone { humanName = p.Key, boneName = p.Value, limit = new HumanLimit { useDefaultValues = true } })
                .ToArray();
            importer.humanDescription = description;
            beforeFinalImport?.Invoke(importer);
            importer.SaveAndReimport();
        }

        static Avatar LoadAvatar(string path)
        {
            Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
            if (avatar == null || !avatar.isValid || !avatar.isHuman)
            {
                throw new InvalidOperationException(path + ": the humanoid avatar is missing or invalid (see the import log)");
            }

            return avatar;
        }

        static T GetImporter<T>(string path) where T : AssetImporter
        {
            if (AssetImporter.GetAtPath(path) is T importer)
            {
                return importer;
            }

            throw new FileNotFoundException(path + " is missing: run ArtSource/Tools/prepare_for_unity.py (Docs/CHARACTER_PIPELINE.md)");
        }

        // ---------------------------------------------------------------- generated assets

        /// <summary>
        /// Idle = the average pose of the walk cycle (arms at the sides, feet under the body) plus slow breathing.
        /// A placeholder until a real idle clip exists; it keeps the controller's Speed = 0 from walking in place.
        /// </summary>
        static AnimationClip EnsureIdleClip(AnimationClip walk, bool overwrite)
        {
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(IdlePath);
            if (existing != null && !overwrite)
            {
                return existing;
            }

            const float Length = 3f;
            const int Samples = 64;
            var means = new Dictionary<EditorCurveBinding, float>();
            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(walk))
            {
                AnimationCurve curve = AnimationUtility.GetEditorCurve(walk, binding);
                float sum = 0f;
                for (int i = 0; i < Samples; i++)
                {
                    sum += curve.Evaluate(walk.length * i / Samples);
                }

                means[binding] = sum / Samples;
            }

            NormalizeQuaternions(means);

            var idle = new AnimationClip { name = Path.GetFileNameWithoutExtension(IdlePath), frameRate = 30f };
            foreach (KeyValuePair<EditorCurveBinding, float> pair in means)
            {
                float breath = BreathAmplitude(pair.Key.propertyName);
                AnimationCurve curve = breath == 0f
                    ? AnimationCurve.Constant(0f, Length, pair.Value)
                    : Breathing(pair.Value, breath, Length);
                AnimationUtility.SetEditorCurve(idle, pair.Key, curve);
            }

            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(idle);
            settings.loopTime = true;
            settings.loopBlend = true;
            settings.loopBlendOrientation = true;
            settings.loopBlendPositionY = true;
            settings.loopBlendPositionXZ = true;
            settings.keepOriginalOrientation = true;
            settings.keepOriginalPositionY = true;
            settings.keepOriginalPositionXZ = false;
            AnimationUtility.SetAnimationClipSettings(idle, settings);

            EditorUtil.EnsureFolder(AnimFolder);
            if (existing != null)
            {
                EditorUtility.CopySerialized(idle, existing); // keep the GUID the controller points at
                Object.DestroyImmediate(idle);
                idle = existing;
            }
            else
            {
                AssetDatabase.CreateAsset(idle, IdlePath);
            }

            return idle;
        }

        static float BreathAmplitude(string muscle)
        {
            switch (muscle)
            {
                case "Chest Front-Back": return 0.03f;
                case "UpperChest Front-Back": return 0.02f;
                case "Left Shoulder Down-Up":
                case "Right Shoulder Down-Up": return 0.025f;
                case "Head Nod Down-Up": return -0.02f;
                default: return 0f;
            }
        }

        static AnimationCurve Breathing(float mean, float amplitude, float length)
        {
            var keys = new Keyframe[5];
            for (int i = 0; i < keys.Length; i++)
            {
                float t = length * i / (keys.Length - 1);
                keys[i] = new Keyframe(t, mean + amplitude * Mathf.Sin(t / length * Mathf.PI * 2f));
            }

            var curve = new AnimationCurve(keys);
            for (int i = 0; i < keys.Length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto);
                AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto);
            }

            return curve;
        }

        static readonly Regex QuaternionComponent = new Regex(@"^(.*(?:Q|m_LocalRotation))\.([xyzw])$");

        /// <summary>Averaged rotation components (root, IK goals, extra bones) are renormalized as quaternions.</summary>
        static void NormalizeQuaternions(Dictionary<EditorCurveBinding, float> means)
        {
            var groups = means.Keys
                .Select(b => (binding: b, match: QuaternionComponent.Match(b.propertyName)))
                .Where(x => x.match.Success)
                .GroupBy(x => (x.binding.path, x.binding.type, x.match.Groups[1].Value));
            foreach (var group in groups)
            {
                var parts = group.ToList();
                if (parts.Count != 4)
                {
                    continue;
                }

                float length = Mathf.Sqrt(parts.Sum(p => means[p.binding] * means[p.binding]));
                if (length < 1e-5f)
                {
                    continue;
                }

                foreach (var p in parts)
                {
                    means[p.binding] /= length;
                }
            }
        }

        static AnimatorController EnsureController(AnimationClip idle, AnimationClip walk, AnimationClip run, bool overwrite)
        {
            var existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (existing != null && !overwrite)
            {
                return existing;
            }

            EditorUtil.EnsureFolder(AnimFolder);
            AssetDatabase.DeleteAsset(ControllerPath);
            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

            // Same parameter set as the placeholder controller, so CharacterAnimatorDriver can drive it unchanged.
            controller.AddParameter(AnimatorParams.MoveX, AnimatorControllerParameterType.Float);
            controller.AddParameter(AnimatorParams.MoveY, AnimatorControllerParameterType.Float);
            controller.AddParameter(AnimatorParams.Speed, AnimatorControllerParameterType.Float);
            controller.AddParameter(new AnimatorControllerParameter
            {
                name = AnimatorParams.LocomotionSpeed, type = AnimatorControllerParameterType.Float, defaultFloat = 1f,
            });
            controller.AddParameter(new AnimatorControllerParameter
            {
                name = AnimatorParams.IsGrounded, type = AnimatorControllerParameterType.Bool, defaultBool = true,
            });
            controller.AddParameter(AnimatorParams.VerticalSpeed, AnimatorControllerParameterType.Float);
            controller.AddParameter(AnimatorParams.Fire, AnimatorControllerParameterType.Trigger);
            controller.AddParameter(AnimatorParams.Reload, AnimatorControllerParameterType.Trigger);
            controller.AddParameter(AnimatorParams.Attack, AnimatorControllerParameterType.Trigger);
            controller.AddParameter(AnimatorParams.Hit, AnimatorControllerParameterType.Trigger);
            controller.AddParameter(AnimatorParams.Dead, AnimatorControllerParameterType.Bool);

            AnimatorState locomotion = controller.CreateBlendTreeInController("Locomotion", out BlendTree tree, 0);
            tree.name = "Locomotion";
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = AnimatorParams.Speed;
            tree.useAutomaticThresholds = false;
            tree.AddChild(idle, 0f);
            tree.AddChild(walk, WalkClipSpeed);
            tree.AddChild(run, RunClipSpeed);
            for (float speed = RunClipSpeed + RunStep; speed <= FastRunThreshold + 0.001f; speed += RunStep)
            {
                tree.AddChild(run, speed);
            }

            ChildMotion[] children = tree.children;
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i].motion == run)
                {
                    children[i].timeScale = children[i].threshold / RunClipSpeed;
                }
            }

            tree.children = children;
            controller.layers[0].stateMachine.defaultState = locomotion;

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            return controller;
        }

        static Material EnsureMaterial()
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(ToonShaderPath);
            if (shader == null)
            {
                throw new FileNotFoundException(ToonShaderPath + " is missing");
            }

            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                EditorUtil.EnsureFolder(Path.GetDirectoryName(MaterialPath)?.Replace('\\', '/'));
                material = new Material(shader) { name = Path.GetFileNameWithoutExtension(MaterialPath) };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }

            // Tuned colour/shading values are kept on reruns; only the shader and the texture are enforced.
            material.shader = shader;
            material.SetTexture("_BaseMap", texture);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>A prefab variant of the FBX: Animator with the controller, the given materials, no root motion.</summary>
        static GameObject BuildBodyPrefab(Material[] materials, AnimatorController controller)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            instance.name = "FoxGunner_Body";

            Animator animator = instance.GetComponent<Animator>();
            if (animator == null)
            {
                animator = instance.AddComponent<Animator>();
            }

            animator.avatar = LoadAvatar(ModelPath);
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

            foreach (SkinnedMeshRenderer skinned in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                skinned.sharedMaterials = materials; // one submesh: extra materials redraw it (the outline)
                skinned.updateWhenOffscreen = false;
                skinned.skinnedMotionVectors = true;
                skinned.shadowCastingMode = ShadowCastingMode.On;
                skinned.receiveShadows = true;
            }

            EditorUtil.EnsureFolder(GamePaths.CharacterPrefabs);
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(instance, PrefabPath);
            Object.DestroyImmediate(instance);
            return prefab;
        }

        // ---------------------------------------------------------------- preview

        [MenuItem("CoopDeck/Art/Capture Fox Gunner Preview", priority = 21)]
        public static void CapturePreviewFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            string previousScene = EditorSceneManager.GetActiveScene().path;
            CapturePreview(PreviewPath);
            if (!string.IsNullOrEmpty(previousScene))
            {
                EditorSceneManager.OpenScene(previousScene);
            }

            EditorUtility.RevealInFinder(PreviewPath);
        }

        /// <summary>
        /// Renders the body prefab in a throwaway scene: idle from the front, side and back, then running,
        /// plus a close-up of the face. Used to check the toon shading, outline and rig in the Editor.
        /// </summary>
        public static void CapturePreview(string outputPath)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                throw new FileNotFoundException(PrefabPath + " is missing: run CoopDeck > Art > Import Fox Gunner");
            }

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var ambient = new SphericalHarmonicsL2();
            ambient.AddAmbientLight(new Color(0.42f, 0.45f, 0.55f));
            RenderSettings.ambientMode = AmbientMode.Custom;
            RenderSettings.ambientProbe = ambient;
            RenderSettings.fog = false;

            var lightGo = new GameObject("Sun");
            var sun = lightGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.96f, 0.9f);
            sun.intensity = 1.2f;
            sun.shadows = LightShadows.Soft;
            lightGo.transform.rotation = Quaternion.Euler(42f, 150f, 0f); // from the front-left of the characters

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.localScale = new Vector3(2f, 1f, 2f);
            var groundMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            groundMaterial.SetColor("_BaseColor", new Color(0.36f, 0.37f, 0.42f));
            ground.GetComponent<MeshRenderer>().sharedMaterial = groundMaterial;

            // (x, yaw, speed, time): idle front / side / back, then the run cycle from the side.
            // The camera looks down -Z, so +X is on the left of the image: this reads front → run from left to right.
            var poses = new (float x, float yaw, float speed, float time)[]
            {
                (2.1f, 0f, 0f, 0.8f), (0.7f, 90f, 0f, 0.8f), (-0.7f, 180f, 0f, 0.8f), (-2.1f, 90f, RunClipSpeed, 0.22f),
            };
            foreach (var pose in poses)
            {
                var body = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                body.transform.SetPositionAndRotation(new Vector3(pose.x, 0f, 0f), Quaternion.Euler(0f, pose.yaw, 0f));
                Animator animator = body.GetComponent<Animator>();
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.Rebind();
                animator.SetFloat(AnimatorParams.Speed, pose.speed);
                animator.Update(0f);
                animator.Update(pose.time);
            }

            var cameraGo = new GameObject("PreviewCamera");
            var camera = cameraGo.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.2f, 0.21f, 0.25f);
            camera.nearClipPlane = 0.05f;
            camera.fieldOfView = 26f;
            UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;

            // Wide shot of the four poses, then a close-up of the first one's face.
            cameraGo.transform.position = new Vector3(0f, 1.0f, 7.5f);
            cameraGo.transform.LookAt(new Vector3(0f, 0.8f, 0f));
            Texture2D wide = Render(camera, 1920, 820);
            camera.fieldOfView = 25f;
            cameraGo.transform.position = new Vector3(2.1f, 1.3f, 2.0f);
            cameraGo.transform.LookAt(new Vector3(2.1f, 1.2f, 0f));
            Texture2D close = Render(camera, 820, 820);

            var sheet = new Texture2D(wide.width + close.width, wide.height, TextureFormat.RGB24, false);
            sheet.SetPixels(0, 0, wide.width, wide.height, wide.GetPixels());
            sheet.SetPixels(wide.width, 0, close.width, close.height, close.GetPixels());
            sheet.Apply();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath)) ?? ".");
            File.WriteAllBytes(outputPath, sheet.EncodeToPNG());
            Object.DestroyImmediate(wide);
            Object.DestroyImmediate(close);
            Object.DestroyImmediate(sheet);
            Object.DestroyImmediate(groundMaterial);
            Debug.Log("[CoopDeck] Fox Gunner preview written to " + Path.GetFullPath(outputPath));
        }

        static Texture2D Render(Camera camera, int width, int height)
        {
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            target.Create();
            var request = new RenderPipeline.StandardRequest { destination = target };
            if (RenderPipeline.SupportsRenderRequest(camera, request))
            {
                RenderPipeline.SubmitRenderRequest(camera, request);
            }
            else
            {
                camera.targetTexture = target;
                camera.Render();
                camera.targetTexture = null;
            }

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            image.Apply();
            RenderTexture.active = previous;
            target.Release();
            Object.DestroyImmediate(target);
            return image;
        }
    }
}
