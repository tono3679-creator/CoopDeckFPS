using System.Collections.Generic;
using System.Linq;
using CoopDeck.Animation;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;

namespace CoopDeck.Tests
{
    /// <summary>
    /// Checks the Fox Gunner model, clips, material, controller and body prefab
    /// (run CoopDeck > Art > Import Fox Gunner, or FoxGunnerAssetFactory.RunBatch, first).
    /// </summary>
    public class FoxGunnerAssetTests
    {
        const string ModelPath = "Assets/_Game/Art/Characters/FoxGunner/Models/FoxGunner.fbx";
        const string MaterialPath = "Assets/_Game/Art/Characters/FoxGunner/Materials/M_FoxGunner_Toon.mat";
        const string WalkPath = "Assets/_Game/Animations/FoxGunner/A_FoxGunner_Walk.fbx";
        const string RunPath = "Assets/_Game/Animations/FoxGunner/A_FoxGunner_Run.fbx";
        const string IdlePath = "Assets/_Game/Animations/FoxGunner/A_FoxGunner_Idle.anim";
        const string ControllerPath = "Assets/_Game/Animations/FoxGunner/AC_FoxGunner_TP.controller";
        const string PrefabPath = "Assets/_Game/Prefabs/Characters/PF_FoxGunner_Body.prefab";
        const string ShaderName = "CoopDeck/Character/Toon";
        const string SoftToonShaderName = "CoopDeck/Character/SG_Character_SoftToon";
        const string BaseMapPath = "Assets/_Game/Art/Characters/FoxGunner/Textures/T_FoxGunner_BaseColor.png";
        const string SurfaceMaskPath = "Assets/_Game/Art/Characters/FoxGunner/Textures/T_FoxGunner_SurfaceMask.png";
        const string SoftToonMaterialPath = "Assets/_Game/Art/Characters/FoxGunner/Materials/M_FoxGunner_SoftToon.mat";
        const string OutlineMaterialPath = "Assets/_Game/Art/Characters/FoxGunner/Materials/M_FoxGunner_Outline.mat";
        const string BackupMaterialPath = "Assets/_Game/Art/Characters/FoxGunner/Materials/Backup/M_FoxGunner_Toon_Original.mat";

        GameObject instance;

        [TearDown]
        public void TearDown()
        {
            if (instance != null)
            {
                Object.DestroyImmediate(instance);
            }
        }

        static T Load<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.That(asset, Is.Not.Null, path + " missing: run CoopDeck > Art > Import Fox Gunner");
            return asset;
        }

        GameObject SpawnBody()
        {
            instance = (GameObject)PrefabUtility.InstantiatePrefab(Load<GameObject>(PrefabPath));
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            return instance;
        }

        static AnimationClip LoadClip(string path, string name)
        {
            AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => c.name == name);
            Assert.That(clip, Is.Not.Null, $"{name} in {path}");
            return clip;
        }

        [Test]
        public void Model_IsHumanoid_WithTheExplicitMeshyBoneMap()
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
            Assert.That(importer.animationType, Is.EqualTo(ModelImporterAnimationType.Human));
            Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Avatar>().Single();
            Assert.That(avatar.isValid && avatar.isHuman, Is.True);

            Dictionary<string, string> map = importer.humanDescription.human.ToDictionary(h => h.humanName, h => h.boneName);
            // Meshy numbers the spine top-down: the bone above the hips is Spine02.
            Assert.That(map["Spine"], Is.EqualTo("Spine02"));
            Assert.That(map["Chest"], Is.EqualTo("Spine01"));
            Assert.That(map["UpperChest"], Is.EqualTo("Spine"));
            Assert.That(map["LeftUpperArm"], Is.EqualTo("LeftArm"));
            Assert.That(map["RightLowerLeg"], Is.EqualTo("RightLeg"));
            Assert.That(map["LeftToes"], Is.EqualTo("LeftToeBase"));
            Assert.That(map.Count, Is.EqualTo(22));
        }

        /// <summary>World-space bounds of the skinned mesh in its current pose (sharedMesh.bounds is in bind space).</summary>
        static Bounds SkinnedWorldBounds(SkinnedMeshRenderer skinned)
        {
            var baked = new Mesh();
            skinned.BakeMesh(baked, true);
            Matrix4x4 toWorld = skinned.transform.localToWorldMatrix;
            Vector3[] vertices = baked.vertices;
            var bounds = new Bounds(toWorld.MultiplyPoint3x4(vertices[0]), Vector3.zero);
            foreach (Vector3 v in vertices)
            {
                bounds.Encapsulate(toWorld.MultiplyPoint3x4(v));
            }

            Object.DestroyImmediate(baked);
            return bounds;
        }

        [Test]
        public void Body_Is1_6mTall_StandsOnTheOrigin_AndFacesPlusZ()
        {
            Animator animator = SpawnBody().GetComponent<Animator>();
            Bounds bounds = SkinnedWorldBounds(instance.GetComponentInChildren<SkinnedMeshRenderer>());
            Assert.That(bounds.size.y, Is.EqualTo(1.6f).Within(0.05f), "height");
            Assert.That(bounds.min.y, Is.EqualTo(0f).Within(0.03f), "feet on the ground");

            Vector3 hips = animator.GetBoneTransform(HumanBodyBones.Hips).position;
            Assert.That(new Vector2(hips.x, hips.z).magnitude, Is.LessThan(0.05f), "pivot under the hips (CharacterController centre)");

            // "headfront" is a Meshy helper bone just in front of the head.
            Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
            Transform front = instance.GetComponentsInChildren<Transform>().First(t => t.name == "headfront");
            Assert.That(front.position.z, Is.GreaterThan(head.position.z), "the face points to +Z like the player");
            Vector3 leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand).position;
            Assert.That(leftHand.x, Is.LessThan(-0.3f), "left hand on the left (-X) in the T-pose");
        }

        [Test]
        public void Mesh_FitsTheCharacterBudget()
        {
            SkinnedMeshRenderer skinned = SpawnBody().GetComponentInChildren<SkinnedMeshRenderer>();
            Mesh mesh = skinned.sharedMesh;
            Assert.That(mesh.triangles.Length / 3, Is.LessThanOrEqualTo(20000), "triangles");
            Assert.That(mesh.vertexCount, Is.LessThanOrEqualTo(20000), "vertices after UV-seam splits");
            Assert.That(skinned.bones.Length, Is.LessThanOrEqualTo(32), "bones");
            Assert.That(mesh.subMeshCount, Is.EqualTo(1), "one draw call per pass");
        }

        [Test]
        public void Body_WearsTheSoftToonLook_WithAThinOutline_AndTheOriginalIsBackedUp()
        {
            var softToon = Load<Material>(SoftToonMaterialPath);
            var outline = Load<Material>(OutlineMaterialPath);
            Material[] worn = SpawnBody().GetComponentInChildren<SkinnedMeshRenderer>().sharedMaterials;
            Assert.That(worn, Is.EqualTo(new[] { softToon, outline }), "surface first, outline as a second pass over the same submesh");

            Assert.That(softToon.shader.name, Is.EqualTo(SoftToonShaderName));
            Assert.That(softToon.GetTexture("_BaseMap"), Is.EqualTo(Load<Texture2D>(BaseMapPath)), "the Meshy colours are unchanged");
            Assert.That(softToon.GetTexture("_SurfaceMask"), Is.EqualTo(Load<Texture2D>(SurfaceMaskPath)));
            Assert.That(softToon.GetFloat("_ToonBlend"), Is.LessThanOrEqualTo(0.6f), "shadow steps stay gentle");

            Assert.That(outline.shader.name, Is.EqualTo("CoopDeck/Character/Outline"));
            Assert.That(outline.GetFloat("_OutlineWidth"), Is.LessThanOrEqualTo(0.0025f), "no thick ink lines");

            // The cel material from before the look work is kept, untouched and backed up, for comparison.
            Assert.That(Load<Material>(MaterialPath).shader.name, Is.EqualTo(ShaderName));
            Assert.That(Load<Material>(BackupMaterialPath).shader.name, Is.EqualTo(ShaderName));
        }

        [Test]
        public void SurfaceMask_SplitsFurFaceClothLeatherAndMetal()
        {
            // R softness, G smoothness, B metallic, A warmth (FoxGunnerLookFactory presets).
            var texture = new Texture2D(2, 2);
            texture.LoadImage(System.IO.File.ReadAllBytes(SurfaceMaskPath));
            Color32[] p = texture.GetPixels32().Where(c => c.r + c.g + c.b + c.a > 0).ToArray(); // texels the mesh uses
            Object.DestroyImmediate(texture);
            float Share(System.Func<Color32, bool> test) => p.Count(test) / (float)p.Length;

            float soft = Share(c => c.r >= 200);                    // fur and face
            float face = Share(c => c.a >= 190);                    // face only (warmest)
            float matteCloth = Share(c => c.g <= 20 && c.r < 200);   // cloth
            float leather = Share(c => c.g >= 95 && c.g <= 120);    // leather sheen
            float metal = Share(c => c.b >= 128);                    // metallic
            Assert.That(soft, Is.InRange(0.3f, 0.6f), $"fur and face {soft:P0}");
            Assert.That(face, Is.InRange(0.01f, 0.12f), $"face {face:P0}");
            Assert.That(matteCloth, Is.InRange(0.1f, 0.4f), $"cloth {matteCloth:P0}");
            Assert.That(leather, Is.InRange(0.1f, 0.4f), $"leather {leather:P0}");
            Assert.That(metal, Is.InRange(0.005f, 0.12f), $"metal {metal:P0}");
        }

        [Test]
        public void SoftToonAndOutlineShaders_Compile_WithShadowAndDepthPasses()
        {
            foreach (string name in new[] { SoftToonShaderName, "CoopDeck/Character/Outline" })
            {
                Shader shader = Shader.Find(name);
                Assert.That(shader, Is.Not.Null, name);
                Assert.That(ShaderUtil.ShaderHasError(shader), Is.False, name + " has errors (see the console)");
            }

            // A Shader Graph shader carries several SubShaders; read every pass of every one.
            ShaderData data = ShaderUtil.GetShaderData(Shader.Find(SoftToonShaderName));
            var modes = new HashSet<string>();
            for (int s = 0; s < data.SubshaderCount; s++)
            {
                ShaderData.Subshader subshader = data.GetSubshader(s);
                for (int p = 0; p < subshader.PassCount; p++)
                {
                    modes.Add(subshader.GetPass(p).FindTagValue(new ShaderTagId("LightMode")).name.ToUpperInvariant());
                }
            }

            Assert.That(modes, Does.Contain("SHADOWCASTER"), "casts the body's shadow (the local player only sees that)");
            Assert.That(modes, Does.Contain("DEPTHONLY"));
            Assert.That(modes.Any(m => m.StartsWith("UNIVERSALFORWARD")), Is.True, "forward pass");
        }

        [Test]
        public void ToonShader_Compiles_AndHasEveryUrpPass()
        {
            Shader shader = Shader.Find(ShaderName);
            Assert.That(shader, Is.Not.Null);
            Assert.That(ShaderUtil.ShaderHasError(shader), Is.False, "shader errors (see the console)");

            // Unity reports built-in tag values in upper case (e.g. "SHADOWCASTER"), so compare without case.
            var modes = new HashSet<string>();
            for (int i = 0; i < shader.passCount; i++)
            {
                modes.Add(shader.FindPassTagValue(i, new ShaderTagId("LightMode")).name.ToUpperInvariant());
            }

            string[] required = { "UniversalForward", "SRPDefaultUnlit", "ShadowCaster", "DepthOnly", "DepthNormals" };
            Assert.That(modes, Is.SupersetOf(required.Select(m => m.ToUpperInvariant())));
        }

        [Test]
        public void Clips_AreLoopingInPlaceHumanoidMotions()
        {
            var clips = new[]
            {
                LoadClip(WalkPath, "A_FoxGunner_Walk"),
                LoadClip(RunPath, "A_FoxGunner_Run"),
                Load<AnimationClip>(IdlePath),
            };
            foreach (AnimationClip clip in clips)
            {
                Assert.That(clip.isHumanMotion, Is.True, clip.name);
                Assert.That(clip.isLooping, Is.True, clip.name + " loops");
                Assert.That(clip.length, Is.GreaterThan(0.3f), clip.name);
                AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
                Assert.That(settings.loopBlendPositionXZ, Is.True, clip.name + ": root XZ baked into the pose (no root motion)");
            }
        }

        [Test]
        public void Controller_HasTheSharedParameters_AndASpeedBlend()
        {
            var controller = Load<AnimatorController>(ControllerPath);
            string[] required =
            {
                AnimatorParams.MoveX, AnimatorParams.MoveY, AnimatorParams.Speed, AnimatorParams.LocomotionSpeed,
                AnimatorParams.IsGrounded, AnimatorParams.VerticalSpeed, AnimatorParams.Fire, AnimatorParams.Reload,
                AnimatorParams.Attack, AnimatorParams.Hit, AnimatorParams.Dead,
            };
            Assert.That(controller.parameters.Select(p => p.name), Is.SupersetOf(required));

            AnimatorState locomotion = controller.layers[0].stateMachine.defaultState;
            Assert.That(locomotion.name, Is.EqualTo("Locomotion"));
            var tree = (BlendTree)locomotion.motion;
            Assert.That(tree.blendParameter, Is.EqualTo(AnimatorParams.Speed));
            ChildMotion[] children = tree.children;
            Assert.That(children.Take(3).Select(c => c.motion.name), Is.EqualTo(new[] { "A_FoxGunner_Idle", "A_FoxGunner_Walk", "A_FoxGunner_Run" }));
            Assert.That(children.Take(3).Select(c => c.threshold), Is.EqualTo(new[] { 0f, 1.2f, 4f }).Within(0.001f));

            // Run repeated every 1 m/s up to 12 m/s, each at speed / 4: the cycle keeps pace at walk (6.5) and sprint (9.5).
            ChildMotion[] runs = children.Skip(2).ToArray();
            Assert.That(runs.All(c => c.motion.name == "A_FoxGunner_Run"), Is.True);
            Assert.That(runs.Select(c => c.threshold), Is.EqualTo(new[] { 4f, 5f, 6f, 7f, 8f, 9f, 10f, 11f, 12f }).Within(0.001f));
            foreach (ChildMotion run in runs)
            {
                Assert.That(run.timeScale, Is.EqualTo(run.threshold / 4f).Within(0.001f), $"run at {run.threshold} m/s");
            }
        }

        [Test]
        public void PlayerPrefab_BodyIsTheFoxModel_SeenOnlyAsAShadow()
        {
            var player = Load<GameObject>("Assets/_Game/Prefabs/Characters/PF_Player.prefab");
            Transform body = player.transform.Find("BodyVisual");
            Assert.That(body, Is.Not.Null, "PF_Player/BodyVisual");
            Assert.That(PrefabUtility.GetCorrespondingObjectFromOriginalSource(body.gameObject),
                Is.EqualTo(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath)), "nested PF_FoxGunner_Body (variant of the FBX)");
            Assert.That(body.localPosition, Is.EqualTo(Vector3.zero));
            Assert.That(body.localRotation, Is.EqualTo(Quaternion.identity));

            Animator animator = body.GetComponent<Animator>();
            Assert.That(animator.runtimeAnimatorController, Is.EqualTo(Load<AnimatorController>(ControllerPath)));
            Assert.That(animator.avatar != null && animator.avatar.isHuman, Is.True);
            Assert.That(animator.applyRootMotion, Is.False);

            var driver = player.GetComponent<CharacterAnimatorDriver>();
            Assert.That(driver.Animator, Is.EqualTo(animator), "the motor drives the fox's Animator");
            Renderer[] renderers = body.GetComponentsInChildren<Renderer>(true);
            Assert.That(renderers, Is.Not.Empty);
            Assert.That(renderers.All(r => r.shadowCastingMode == ShadowCastingMode.ShadowsOnly), Is.True,
                "the local player only sees the body's shadow");
            Assert.That(renderers.All(r => r.gameObject.layer == player.layer), Is.True, "body on the Player layer");
        }

        [Test]
        public void Prefab_UsesTheController_WithoutRootMotion()
        {
            Animator animator = SpawnBody().GetComponent<Animator>();
            Assert.That(animator.runtimeAnimatorController, Is.EqualTo(Load<AnimatorController>(ControllerPath)));
            Assert.That(animator.avatar != null && animator.avatar.isHuman, Is.True);
            Assert.That(animator.applyRootMotion, Is.False);
        }

        [Test]
        public void Running_MovesTheFeet_ButKeepsTheBodyOverTheRoot()
        {
            Animator animator = SpawnBody().GetComponent<Animator>();
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.Rebind();
            animator.SetFloat(AnimatorParams.Speed, 4f);
            animator.Update(0f);

            Transform foot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            Vector3 footStart = foot.position;
            float lowest = float.MaxValue;
            float travel = 0f;
            for (int i = 0; i < 20; i++)
            {
                animator.Update(1f / 30f);
                travel = Mathf.Max(travel, Vector3.Distance(foot.position, footStart));
                lowest = Mathf.Min(lowest, foot.position.y);
                Assert.That(new Vector2(hips.position.x, hips.position.z).magnitude, Is.LessThan(0.25f), "no root drift");
            }

            Assert.That(travel, Is.GreaterThan(0.2f), "the run cycle moves the foot");
            Assert.That(lowest, Is.LessThan(0.2f), "the foot reaches the ground");
            Assert.That(hips.position.y, Is.InRange(0.5f, 1.0f), "hips stay at running height");
        }
    }
}
