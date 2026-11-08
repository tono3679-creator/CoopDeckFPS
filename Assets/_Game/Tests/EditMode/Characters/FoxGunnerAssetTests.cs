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
        const string FirstPersonMaterialPath = "Assets/_Game/Art/Characters/FoxGunner/Materials/M_FoxGunner_SoftToon_FirstPerson.mat";
        const string NormalMapPath = "Assets/_Game/Art/Characters/FoxGunner/Textures/T_FoxGunner_Normal.png";
        const string PlayerPrefabPath = "Assets/_Game/Prefabs/Characters/PF_Player.prefab";

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
            // Rebuilt from Meshy's high-res sculpt at 40k (refine_character.py rebuild); Meshy's own 15k remesh was lumpy.
            Assert.That(mesh.triangles.Length / 3, Is.InRange(30000, 45000), "triangles");
            // The sculpt's fur cards and layered cloth split into many UV islands: ~1.3 vertices per triangle.
            Assert.That(mesh.vertexCount, Is.LessThanOrEqualTo(60000), "vertices after UV-seam splits");
            // 24 Meshy bones + tail 4, ears 2, fingers 6 (skinned) + Weapon, 4 IK goals and FP_Grip_L (no weights) = 42
            Assert.That(skinned.bones.Length, Is.LessThanOrEqualTo(42), "bones");
            Assert.That(mesh.subMeshCount, Is.EqualTo(1), "one draw call per pass");
        }

        [Test]
        public void BaseColor_Is4K_HighQuality_WithEvenTexelDensity()
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(BaseMapPath);
            Assert.That(importer.maxTextureSize, Is.EqualTo(4096));
            Assert.That(importer.textureCompression, Is.EqualTo(TextureImporterCompression.CompressedHQ), "BC7: no blocky gradients");
            Assert.That(importer.anisoLevel, Is.GreaterThanOrEqualTo(4));
            Assert.That(Load<Texture2D>(BaseMapPath).width, Is.EqualTo(4096));

            // Texels per unit of surface, area-weighted: Meshy's atlas spread 3x between the 5% and 95% points;
            // refine_character.py gives every part the same detail.
            Mesh mesh = SpawnBody().GetComponentInChildren<SkinnedMeshRenderer>().sharedMesh;
            Vector3[] v = mesh.vertices;
            Vector2[] uv = mesh.uv;
            int[] t = mesh.triangles;
            var samples = new List<(float density, float area)>();
            for (int i = 0; i < t.Length; i += 3)
            {
                float area3 = Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]).magnitude;
                Vector2 a = uv[t[i + 1]] - uv[t[i]], b = uv[t[i + 2]] - uv[t[i]];
                float area2 = Mathf.Abs(a.x * b.y - a.y * b.x);
                if (area3 > 1e-10f)
                {
                    samples.Add((Mathf.Sqrt(area2 / area3), area3));
                }
            }

            samples.Sort((p, q) => p.density.CompareTo(q.density));
            float total = samples.Sum(s => s.area);
            float Percentile(float q)
            {
                float acc = 0f;
                foreach (var s in samples)
                {
                    acc += s.area;
                    if (acc >= q * total) return s.density;
                }

                return samples[samples.Count - 1].density;
            }

            float spread = Percentile(0.95f) / Percentile(0.05f);
            Assert.That(spread, Is.LessThan(1.8f), $"texel density 95%/5% = {spread:F2}");
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
        public void Body_ShadesWithTheHighResNormalMap_AndTheFirstPersonArmsHaveTheirOwnBakes()
        {
            var normalMap = Load<Texture2D>(NormalMapPath);
            var importer = (TextureImporter)AssetImporter.GetAtPath(NormalMapPath);
            Assert.That(importer.textureType, Is.EqualTo(TextureImporterType.NormalMap));
            Assert.That(importer.textureCompression, Is.EqualTo(TextureImporterCompression.CompressedHQ), "BC5");
            Assert.That(normalMap.width, Is.EqualTo(4096));

            var softToon = Load<Material>(SoftToonMaterialPath);
            Assert.That(softToon.GetTexture("_NormalMap"), Is.EqualTo(normalMap));
            Assert.That(softToon.GetFloat("_NormalStrength"), Is.GreaterThan(0.5f));

            // Tangents (MikkTSpace, as baked) are needed for the tangent-space map.
            Mesh mesh = SpawnBody().GetComponentInChildren<SkinnedMeshRenderer>().sharedMesh;
            Vector4[] tangents = mesh.tangents;
            Assert.That(tangents.Length, Is.EqualTo(mesh.vertexCount));
            Assert.That(tangents.All(t => Mathf.Abs(new Vector3(t.x, t.y, t.z).magnitude - 1f) < 0.01f && Mathf.Abs(t.w) > 0.99f), Is.True);

            // The first-person arms (cut from the sculpt, own UVs): the same look with their own colour, normal map and
            // surface regions, baked from the same sources as the body's.
            var firstPerson = Load<Material>(FirstPersonMaterialPath);
            Assert.That(firstPerson.shader, Is.EqualTo(softToon.shader));
            Assert.That(firstPerson.GetTexture("_BaseMap").name, Is.EqualTo("T_FoxGunner_FPArms_BaseColor"));
            Assert.That(firstPerson.GetTexture("_NormalMap").name, Is.EqualTo("T_FoxGunner_FPArms_Normal"));
            Assert.That(firstPerson.GetTexture("_SurfaceMask").name, Is.EqualTo("T_FoxGunner_FPArms_SurfaceMask"));
            Assert.That(firstPerson.GetFloat("_NormalStrength"), Is.GreaterThan(0.5f));
            foreach (string name in new[] { "_ToonBlend", "_ToonWidth", "_RimStrength", "_AmbientStrength" })
            {
                Assert.That(firstPerson.GetFloat(name), Is.EqualTo(softToon.GetFloat(name)), "same look as the body: " + name);
            }
            Object.DestroyImmediate(instance);
            instance = (GameObject)PrefabUtility.InstantiatePrefab(Load<GameObject>(PlayerPrefabPath));
            Transform viewModel = instance.GetComponentsInChildren<Transform>(true).First(t => t.name == "ViewModel");
            SkinnedMeshRenderer[] arms = viewModel.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            Assert.That(arms, Is.Not.Empty, "first-person arms");
            Assert.That(arms.All(r => r.sharedMaterial == firstPerson), Is.True);
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
        public void Controller_HasTheSharedParameters_AndADirectionalSpeedBlend()
        {
            // The layered rifle controller (FoxGunnerAnimationFactory) keeps the shared parameter names; the full
            // structure is covered by FoxAnimationAssetTests.
            var controller = Load<AnimatorController>(ControllerPath);
            string[] required =
            {
                AnimatorParams.MoveX, AnimatorParams.MoveY, AnimatorParams.Speed, AnimatorParams.LocomotionSpeed,
                AnimatorParams.IsGrounded, AnimatorParams.VerticalSpeed, AnimatorParams.Fire, AnimatorParams.Reload,
                AnimatorParams.Attack, AnimatorParams.Hit, AnimatorParams.Dead,
            };
            Assert.That(controller.parameters.Select(p => p.name), Is.SupersetOf(required));

            AnimatorStateMachine sm = controller.layers[0].stateMachine;
            Assert.That(sm.defaultState.name, Is.EqualTo("Idle"));
            AnimatorState locomotion = sm.states.Select(s => s.state).First(s => s.name == "Locomotion");
            var tree = (BlendTree)locomotion.motion;
            Assert.That(tree.blendType, Is.EqualTo(BlendTreeType.FreeformDirectional2D));
            Assert.That(tree.blendParameter, Is.EqualTo(AnimatorParams.MoveX));
            Assert.That(tree.blendParameterY, Is.EqualTo(AnimatorParams.MoveY));
            // walk ring (~1.5 m/s), run ring (~6.5 m/s, the player's speed) and sprint forward (~9.5 m/s)
            float[] radii = tree.children.Select(c => c.position.magnitude).Where(r => r > 0.01f).Distinct().OrderBy(r => r).ToArray();
            Assert.That(radii.Length, Is.GreaterThanOrEqualTo(3));
            Assert.That(radii[0], Is.InRange(1.2f, 2f));
            Assert.That(radii.Any(r => Mathf.Abs(r - 6.5f) < 0.3f), Is.True, "run ring at the player's speed");
            Assert.That(radii.Last(), Is.InRange(9f, 10f), "sprint");
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
            animator.SetFloat(AnimatorParams.Speed, 6.5f);
            animator.SetFloat(AnimatorParams.MoveY, 6.5f); // run forward
            animator.Update(0f);
            animator.Update(0.3f); // Idle -> Locomotion

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
