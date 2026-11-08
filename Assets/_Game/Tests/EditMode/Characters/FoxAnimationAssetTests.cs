using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CoopDeck.Animation;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Animations.Rigging;
using Object = UnityEngine.Object;

namespace CoopDeck.Tests
{
    /// <summary>
    /// The Blender-authored Fox Gunner motion set as Unity sees it (Docs/FOX_ANIMATION.md): every requested motion
    /// is a humanoid clip, the clips leave the procedural tail/ears alone and carry the rifle + IK goals, the layered
    /// controller has a motion and a way out for every state, the masks split legs from the upper body, and the body
    /// prefab has its hand IK, look-at and secondary motion.
    /// </summary>
    public class FoxAnimationAssetTests
    {
        const string AnimFolder = "Assets/_Game/Animations/FoxGunner";
        const string SetPath = AnimFolder + "/A_FoxGunner_Set.fbx";
        const string TablePath = AnimFolder + "/A_FoxGunner_Clips.json";
        const string ControllerPath = AnimFolder + "/AC_FoxGunner_TP.controller";
        const string UpperMaskPath = AnimFolder + "/AM_FoxGunner_UpperBody.mask";
        const string BodyPrefabPath = "Assets/_Game/Prefabs/Characters/PF_FoxGunner_Body.prefab";
        const string ModelPath = "Assets/_Game/Art/Characters/FoxGunner/Models/FoxGunner.fbx";

        /// <summary>The motions the animation brief lists (01-42; 43-45 are procedural, see FoxSecondaryMotion).</summary>
        static readonly string[] Requested =
        {
            "Combat_Idle", "Walk_Forward", "Walk_Backward", "Walk_Left", "Walk_Right",
            "Run_Forward", "Run_Backward", "Run_Left", "Run_Right", "Sprint",
            "Jump_Start", "Jump_Loop", "Fall", "Land", "Dash_Forward", "Dash_Backward", "Dash_Left", "Dash_Right", "Slide",
            "Rifle_Idle", "Rifle_Fire", "Rifle_Reload", "Rifle_ADS_Enter", "Rifle_ADS_Exit", "Rifle_Equip", "Rifle_Inspect",
            "Skill_QuickCast", "Skill_Charged_Start", "Skill_Charged_Loop", "Skill_Charged_Release", "Skill_AreaCast",
            "Skill_SelfBuff", "Skill_Ultimate",
            "Hit_Light", "Hit_Heavy", "Knockback", "Downed_Start", "Downed_Loop", "Revived", "Death",
            "Pickup", "Interact", "Revive_Ally", "Use_Item", "Emote",
        };

        static readonly string[] Loops =
        {
            "Combat_Idle", "Walk_Forward", "Walk_Backward", "Walk_Left", "Walk_Right", "Run_Forward", "Run_Backward",
            "Run_Left", "Run_Right", "Sprint", "Jump_Loop", "Fall", "Rifle_Idle", "Skill_Charged_Loop", "Downed_Loop",
            "Revive_Ally", "Rifle_ADS_Idle", "Locomotion_Center",
        };

        [Serializable]
        class Table
        {
            public Entry[] clips;
        }

        [Serializable]
        class Entry
        {
            public string name;
            public float seconds;
            public bool loop;
            public string layer;
            public float speed;
            public float[] direction;
            public float cycle;
        }

        static Table LoadTable()
        {
            Assert.That(File.Exists(TablePath), TablePath + " missing: run ArtSource/Tools/fox_anim/build_clips.py");
            return JsonUtility.FromJson<Table>(File.ReadAllText(TablePath));
        }

        static Dictionary<string, AnimationClip> Clips()
        {
            return AssetDatabase.LoadAllAssetsAtPath(SetPath).OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal))
                .ToDictionary(c => c.name.Replace("A_FoxGunner_", ""));
        }

        static T Load<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.That(asset, Is.Not.Null, path + " missing: run the bootstrap");
            return asset;
        }

        [Test]
        public void EveryRequestedMotion_IsAHumanoidClip_WithTheRightLooping()
        {
            Dictionary<string, AnimationClip> clips = Clips();
            foreach (string name in Requested)
            {
                Assert.That(clips.ContainsKey(name), Is.True, name);
                AnimationClip clip = clips[name];
                Assert.That(clip.isHumanMotion, Is.True, name + " humanoid");
                Assert.That(clip.length, Is.GreaterThan(0.1f), name);
                Assert.That(clip.isLooping, Is.EqualTo(Loops.Contains(name)), name + " looping");
            }

            Assert.That(clips["Rifle_Reload"].length, Is.EqualTo(1.7f).Within(0.02f), "matches WD_AssaultRifle.reloadTime");
        }

        [Test]
        public void Clips_KeyTheRifleAndIKGoals_ButNeverTheTailOrEars()
        {
            foreach (KeyValuePair<string, AnimationClip> pair in Clips())
            {
                string[] paths = AnimationUtility.GetCurveBindings(pair.Value).Select(b => b.path).Distinct().ToArray();
                Assert.That(paths.Any(p => p.EndsWith("/Weapon", StringComparison.Ordinal)), Is.True, pair.Key + " animates the rifle");
                Assert.That(paths.Any(p => p.EndsWith("IK_Hand_L", StringComparison.Ordinal)), Is.True, pair.Key + " animates the left-hand goal");
                Assert.That(paths.Any(p => p.Contains("/Tail0") || p.EndsWith("Ear_L", StringComparison.Ordinal) || p.EndsWith("Ear_R", StringComparison.Ordinal)),
                    Is.False, pair.Key + ": tail and ears are procedural");
            }
        }

        [Test]
        public void ClipCurves_TellWhenTheLeftHandLeavesTheRifle()
        {
            Dictionary<string, AnimationClip> clips = Clips();
            float Min(string clip, string curve) => Sample(clips[clip], curve).Min();
            float Max(string clip, string curve) => Sample(clips[clip], curve).Max();

            Assert.That(Min("Combat_Idle", AnimatorParams.LeftHandIK), Is.EqualTo(1f).Within(0.01f), "both hands on the rifle at rest");
            Assert.That(Min("Run_Forward", AnimatorParams.LeftHandIK), Is.EqualTo(1f).Within(0.01f));
            Assert.That(Min("Rifle_Reload", AnimatorParams.LeftHandIK), Is.LessThan(0.2f), "left hand swaps the cell");
            Assert.That(Max("Skill_QuickCast", AnimatorParams.CastHand), Is.GreaterThan(0.8f), "left hand casts");
            Assert.That(Sample(clips["Skill_QuickCast"], AnimatorParams.LeftHandIK).Last(), Is.GreaterThan(0.95f), "back on the rifle after the cast");
        }

        static IEnumerable<float> Sample(AnimationClip clip, string curveName)
        {
            EditorCurveBinding binding = AnimationUtility.GetCurveBindings(clip)
                .FirstOrDefault(b => b.propertyName == curveName && b.type == typeof(Animator));
            Assert.That(binding.propertyName, Is.EqualTo(curveName), $"{clip.name} has the {curveName} curve");
            AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
            for (int i = 0; i <= 20; i++)
            {
                yield return curve.Evaluate(clip.length * i / 20f);
            }
        }

        [Test]
        public void LocomotionTable_HasRingsAtThePlayersSpeeds()
        {
            Table table = LoadTable();
            Entry Get(string n) => table.clips.First(c => c.name == "A_FoxGunner_" + n);
            Assert.That(Get("Walk_Forward").speed, Is.InRange(1.2f, 2f));
            Assert.That(Get("Run_Forward").speed, Is.EqualTo(6.5f).Within(0.3f), "MovementProfile.walkSpeed");
            Assert.That(Get("Sprint").speed, Is.EqualTo(9.5f).Within(0.3f), "MovementProfile.sprintSpeed");
            foreach (string dir in new[] { "Forward", "Backward", "Left", "Right" })
            {
                Assert.That(Get("Walk_" + dir).cycle, Is.EqualTo(Get("Walk_Forward").cycle).Within(1e-3f), "walk clips share one cycle (blend in phase)");
                Assert.That(Get("Run_" + dir).cycle, Is.EqualTo(Get("Run_Forward").cycle).Within(1e-3f), "run clips share one cycle");
                Assert.That(Get("Run_" + dir).loop, Is.True);
            }

            Assert.That(Get("Locomotion_Center").cycle, Is.EqualTo(Get("Walk_Forward").cycle).Within(1e-3f),
                "the blend-tree centre has the walk's cycle, so slow speeds do not stretch the stride");
        }

        [Test]
        public void Controller_HasFourLayers_WithMasksAndBlending()
        {
            var controller = Load<AnimatorController>(ControllerPath);
            var upperMask = Load<AvatarMask>(UpperMaskPath);
            AnimatorControllerLayer[] layers = controller.layers;
            Assert.That(layers.Select(l => l.name), Is.EqualTo(new[] { "Base Layer", "UpperBody", "Additive", "Reaction" }));
            Assert.That(layers[0].iKPass, Is.True, "look-at runs in the base layer's IK pass");
            Assert.That(layers[1].avatarMask, Is.EqualTo(upperMask));
            Assert.That(layers[1].blendingMode, Is.EqualTo(AnimatorLayerBlendingMode.Override));
            Assert.That(layers[2].avatarMask, Is.EqualTo(upperMask));
            Assert.That(layers[2].blendingMode, Is.EqualTo(AnimatorLayerBlendingMode.Additive));
            Assert.That(layers[3].avatarMask, Is.Null, "reactions take the whole body");
            Assert.That(layers.Skip(1).All(l => l.defaultWeight == 1f), Is.True);
        }

        [Test]
        public void Controller_EveryStateHasAMotion_AWayOut_AndKnownParameters()
        {
            var controller = Load<AnimatorController>(ControllerPath);
            var parameters = new HashSet<string>(controller.parameters.Select(p => p.name));
            var used = new HashSet<Motion>();
            foreach (AnimatorControllerLayer layer in controller.layers)
            {
                AnimatorStateMachine sm = layer.stateMachine;
                Assert.That(sm.defaultState, Is.Not.Null, layer.name + " default state");
                foreach (ChildAnimatorState child in sm.states)
                {
                    AnimatorState s = child.state;
                    if (s.name != "Empty")
                    {
                        Assert.That(s.motion, Is.Not.Null, $"{layer.name}/{s.name} has a motion");
                        Collect(s.motion, used);
                    }

                    bool looping = s.motion is BlendTree || (s.motion is AnimationClip clip && clip.isLooping) || s.name == "Empty" || s.name == "Death";
                    Assert.That(looping || s.transitions.Length > 0, Is.True, $"{layer.name}/{s.name} has a way out");
                    foreach (AnimatorStateTransition t in s.transitions)
                    {
                        Assert.That(t.conditions.All(c => parameters.Contains(c.parameter)), Is.True, $"{layer.name}/{s.name} conditions");
                    }
                }

                foreach (AnimatorStateTransition t in sm.anyStateTransitions)
                {
                    Assert.That(t.conditions.All(c => parameters.Contains(c.parameter)), Is.True, $"{layer.name} any-state conditions");
                }
            }

            // Rifle_Idle is the same pose as Combat_Idle: the UpperBody layer shows it by passing the base layer through
            // (an override with its own copy would freeze the spine against the running pelvis), so it is not in a state.
            string[] usedNames = used.Select(m => m.name.Replace("A_FoxGunner_", "")).ToArray();
            Assert.That(usedNames, Is.SupersetOf(Requested.Where(n => n != "Rifle_Idle")), "every requested clip is reachable from the controller");
        }

        static void Collect(Motion motion, HashSet<Motion> used)
        {
            if (motion is BlendTree tree)
            {
                foreach (ChildMotion child in tree.children)
                {
                    Collect(child.motion, used);
                }
            }
            else if (motion != null)
            {
                used.Add(motion);
            }
        }

        [Test]
        public void UpperBodyMask_LeavesLegsAndPelvisToLocomotion()
        {
            var mask = Load<AvatarMask>(UpperMaskPath);
            Assert.That(mask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.Root), Is.False);
            Assert.That(mask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftLeg), Is.False);
            Assert.That(mask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.RightLeg), Is.False);
            Assert.That(mask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.Body), Is.True);
            Assert.That(mask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftArm), Is.True);
            Assert.That(mask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.Head), Is.True);
            var active = new Dictionary<string, bool>();
            for (int i = 0; i < mask.transformCount; i++)
            {
                string path = mask.GetTransformPath(i);
                active[path.Substring(path.LastIndexOf('/') + 1)] = mask.GetTransformActive(i);
            }

            Assert.That(active["Weapon"], Is.True, "the rifle follows the upper-body clips");
            Assert.That(active["IK_Hand_L"], Is.True);
            Assert.That(active["Tail01"], Is.False);
        }

        [Test]
        public void Model_HasTheSecondaryBones_WithSkinWeights()
        {
            var model = Load<GameObject>(ModelPath);
            SkinnedMeshRenderer skinned = model.GetComponentInChildren<SkinnedMeshRenderer>();
            string[] bones = skinned.bones.Select(b => b.name).ToArray();
            foreach (string name in new[] { "Tail01", "Tail04", "Ear_L", "Ear_R", "LeftFingers1", "RightFingers2", "RightThumb" })
            {
                int index = Array.IndexOf(bones, name);
                Assert.That(index, Is.GreaterThanOrEqualTo(0), name + " is a skinned bone");
                int weighted = skinned.sharedMesh.boneWeights.Count(w =>
                    (w.boneIndex0 == index && w.weight0 > 0.3f) || (w.boneIndex1 == index && w.weight1 > 0.3f));
                Assert.That(weighted, Is.GreaterThan(10), name + " moves part of the mesh");
            }

            Transform[] all = model.GetComponentsInChildren<Transform>(true);
            Assert.That(all.Any(t => t.name == "Weapon"), Is.True);
            Assert.That(all.Count(t => t.name.StartsWith("IK_", StringComparison.Ordinal)), Is.EqualTo(4));
        }

        [Test]
        public void BodyPrefab_HasHandIK_LookAt_AndTailEarMotion()
        {
            var prefab = Load<GameObject>(BodyPrefabPath);
            var builder = prefab.GetComponent<RigBuilder>();
            Assert.That(builder, Is.Not.Null, "RigBuilder on the Animator");
            Assert.That(builder.layers.Count, Is.EqualTo(1));
            TwoBoneIKConstraint[] ik = prefab.GetComponentsInChildren<TwoBoneIKConstraint>(true);
            Assert.That(ik.Length, Is.EqualTo(2));
            foreach (TwoBoneIKConstraint c in ik)
            {
                string side = c.data.tip.name.StartsWith("Left", StringComparison.Ordinal) ? "L" : "R";
                Assert.That(c.data.target.name, Is.EqualTo("IK_Hand_" + side));
                Assert.That(c.data.hint.name, Is.EqualTo("IK_Elbow_" + side));
                Assert.That(c.data.target.parent.name, Is.EqualTo("Weapon"), "the hands follow the rifle");
                Assert.That(c.weight, Is.EqualTo(1f));
            }

            Assert.That(prefab.GetComponent<FoxBodyAnimator>(), Is.Not.Null);
            var secondary = new SerializedObject(prefab.GetComponent<FoxSecondaryMotion>());
            Assert.That(secondary.FindProperty("tail").arraySize, Is.EqualTo(4));
            Assert.That(secondary.FindProperty("earLeft").objectReferenceValue, Is.Not.Null);
            Assert.That(prefab.GetComponent<Animator>().runtimeAnimatorController, Is.EqualTo(Load<AnimatorController>(ControllerPath)));
        }

        [Test]
        public void FirstPersonArms_AreTheSculptsArms_WithJointedFingers_AndTheBodysProportions()
        {
            var model = Load<GameObject>("Assets/_Game/Art/Characters/FoxGunner/Models/FoxGunner_FPArms.fbx");
            SkinnedMeshRenderer skinned = model.GetComponentInChildren<SkinnedMeshRenderer>();
            Mesh mesh = skinned.sharedMesh;
            Assert.That(mesh.triangles.Length / 3, Is.InRange(30000, 50000), "the sculpt's arms, gloves and fingers (fox_fp_arms.py)");
            Assert.That(mesh.subMeshCount, Is.EqualTo(1), "one material");
            string[] names = skinned.bones.Select(b => b.name).ToArray();
            Assert.That(names, Is.SupersetOf(new[] { "LeftArm", "LeftForeArm", "LeftHand", "RightArm", "RightForeArm", "RightHand" }));
            foreach (string side in new[] { "Left", "Right" })
            {
                foreach (string digit in FirstPersonArms.Digits)
                {
                    for (int j = 1; j <= 3; j++)
                    {
                        Assert.That(names, Does.Contain(side + digit + j), "three joints per finger and thumb");
                    }
                }
            }

            // the body's own arm lengths (no first-person stretching)
            var body = Load<GameObject>("Assets/_Game/Art/Characters/FoxGunner/Models/FoxGunner.fbx");
            Transform B(GameObject root, string n) => root.GetComponentsInChildren<Transform>(true).First(t => t.name == n);
            foreach (string side in new[] { "Left", "Right" })
            {
                float Length(GameObject root, string a, string b) => Vector3.Distance(B(root, side + a).position, B(root, side + b).position);
                Assert.That(Length(model, "Arm", "ForeArm"), Is.EqualTo(Length(body, "Arm", "ForeArm")).Within(0.002f), side + " upper arm");
                Assert.That(Length(model, "ForeArm", "Hand"), Is.EqualTo(Length(body, "ForeArm", "Hand")).Within(0.002f), side + " forearm");
            }

            var player = Load<GameObject>("Assets/_Game/Prefabs/Characters/PF_Player.prefab");
            var arms = player.GetComponentInChildren<FirstPersonArms>(true);
            Assert.That(arms, Is.Not.Null, "FPArms in the view model");
            var so = new SerializedObject(arms);
            Assert.That(so.FindProperty("bodyGripLeft").objectReferenceValue, Is.Null, "the left hand keeps the body's own hold");
            Assert.That(so.FindProperty("bodyElbowLeft").objectReferenceValue, Is.Null, "first person keeps its own elbow directions");
            Assert.That(so.FindProperty("eye").objectReferenceValue, Is.Not.Null, "shoulders placed below the eyes");
            Assert.That(arms.gameObject.layer, Is.EqualTo(CoopDeck.Core.GameLayers.ViewModelIndex));
        }

        [Test]
        public void PlayerPrefab_DriverUsesTheSpeedRings()
        {
            var player = Load<GameObject>("Assets/_Game/Prefabs/Characters/PF_Player.prefab");
            var driver = player.GetComponent<CharacterAnimatorDriver>();
            Assert.That(driver.Rings.Count, Is.EqualTo(3), "walk, run, sprint");
            Assert.That(driver.Rings[1].speed, Is.EqualTo(6.5f).Within(0.3f));
        }
    }
}
