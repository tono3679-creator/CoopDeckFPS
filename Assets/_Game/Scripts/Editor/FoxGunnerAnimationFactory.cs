using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CoopDeck.Animation;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Animations.Rigging;
using Object = UnityEngine.Object;

namespace CoopDeck.EditorTools
{
    public sealed class FoxGunnerAnimationSet
    {
        public AnimatorController Controller;
        public LocomotionRing[] Rings;
        public float ReloadClipSeconds;
        public Dictionary<string, AnimationClip> Clips;
        public AvatarMask UpperBodyMask;
    }

    /// <summary>
    /// Unity side of the Blender-authored Fox Gunner clips (ArtSource/Tools/fox_anim, Docs/FOX_ANIMATION.md):
    /// imports A_FoxGunner_Set.fbx as Humanoid with the model's avatar, applies the clip table
    /// (A_FoxGunner_Clips.json: loop flags, LeftHandIK / CastHand curves, additive reference), builds the avatar
    /// masks and the AC_FoxGunner_TP controller, and sets up the body's hand IK, look-at and tail/ear motion.
    ///
    /// Controller (parameters are AnimatorParams; layer order matters):
    ///   Base       full body: Idle, Locomotion (2D freeform, m/s), Jump start/loop, Fall, Land, Dash (4 directions), Slide
    ///   UpperBody  AM_FoxGunner_UpperBody, override: reload, ADS, equip, inspect, 7 casts, heavy hit, interactions
    ///   Additive   AM_FoxGunner_UpperBody, additive: fire recoil (restarted per shot), light hit
    ///   Reaction   full body, override: knockback, downed, revived, death
    /// </summary>
    public static class FoxGunnerAnimationFactory
    {
        public const string AnimFolder = FoxGunnerAssetFactory.AnimFolder;
        public const string SetPath = AnimFolder + "/A_FoxGunner_Set.fbx";
        public const string ClipTablePath = AnimFolder + "/A_FoxGunner_Clips.json";
        public const string UpperMaskPath = AnimFolder + "/AM_FoxGunner_UpperBody.mask";
        public const string ClipMaskPath = AnimFolder + "/AM_FoxGunner_ClipImport.mask";
        public const string Prefix = "A_FoxGunner_";

        public const string BaseLayer = "Base Layer";
        public const string UpperLayer = "UpperBody";
        public const string AdditiveLayer = "Additive";
        public const string ReactionLayer = "Reaction";

        // ------------------------------------------------------------------ clip table (JSON from build_clips.py)

        [Serializable]
        public class ClipTable
        {
            public string character;
            public int fps;
            public ClipEntry[] clips;
        }

        [Serializable]
        public class ClipEntry
        {
            public string name;
            public string take;
            public float seconds;
            public int frames;
            public bool loop;
            public string layer;
            public float speed;
            public float[] direction;
            public float cycle;
            public string notes;
            public CurveEntry[] curves;
        }

        [Serializable]
        public class CurveEntry
        {
            public string name;
            public float[] times;
            public float[] values;
        }

        public static ClipTable LoadTable()
        {
            if (!File.Exists(ClipTablePath))
            {
                throw new FileNotFoundException(ClipTablePath + " is missing: run ArtSource/Tools/fox_anim/build_clips.py");
            }

            return JsonUtility.FromJson<ClipTable>(File.ReadAllText(ClipTablePath));
        }

        // ------------------------------------------------------------------ entry point

        public static FoxGunnerAnimationSet EnsureAll(Avatar avatar)
        {
            ClipTable table = LoadTable();
            AvatarMask clipMask = EnsureMask(ClipMaskPath, ClipImportMask);
            AvatarMask upperMask = EnsureMask(UpperMaskPath, UpperBodyMask);
            Dictionary<string, AnimationClip> clips = ImportClips(table, avatar, clipMask);
            LocomotionRing[] rings = Rings(table);
            AnimatorController controller = BuildController(table, clips, upperMask, rings);
            ClipEntry reload = table.clips.First(c => c.name == Prefix + "Rifle_Reload");
            return new FoxGunnerAnimationSet
            {
                Controller = controller, Rings = rings, ReloadClipSeconds = reload.seconds, Clips = clips, UpperBodyMask = upperMask,
            };
        }

        public static LocomotionRing[] Rings(ClipTable table)
        {
            var rings = new List<LocomotionRing>();
            foreach (string name in new[] { "Walk_Forward", "Run_Forward", "Sprint" })
            {
                ClipEntry e = table.clips.First(c => c.name == Prefix + name);
                rings.Add(new LocomotionRing(e.speed, e.cycle));
            }

            return rings.ToArray();
        }

        // ------------------------------------------------------------------ masks

        static bool IsProcedural(string path)
        {
            string leaf = path.Substring(path.LastIndexOf('/') + 1);
            return leaf.StartsWith("Tail", StringComparison.Ordinal) || leaf.StartsWith("Ear_", StringComparison.Ordinal);
        }

        /// <summary>Every humanoid part and every transform except the procedural tail and ears.</summary>
        static void ClipImportMask(AvatarMask mask)
        {
            for (var part = AvatarMaskBodyPart.Root; part < AvatarMaskBodyPart.LastBodyPart; part++)
            {
                mask.SetHumanoidBodyPartActive(part, true);
            }

            for (int i = 0; i < mask.transformCount; i++)
            {
                mask.SetTransformActive(i, !IsProcedural(mask.GetTransformPath(i)));
            }
        }

        /// <summary>Spine, head, arms and fingers plus the rifle and IK goals; legs and the pelvis stay with locomotion.</summary>
        static void UpperBodyMask(AvatarMask mask)
        {
            for (var part = AvatarMaskBodyPart.Root; part < AvatarMaskBodyPart.LastBodyPart; part++)
            {
                bool upper = part == AvatarMaskBodyPart.Body || part == AvatarMaskBodyPart.Head
                             || part == AvatarMaskBodyPart.LeftArm || part == AvatarMaskBodyPart.RightArm
                             || part == AvatarMaskBodyPart.LeftFingers || part == AvatarMaskBodyPart.RightFingers
                             || part == AvatarMaskBodyPart.LeftHandIK || part == AvatarMaskBodyPart.RightHandIK;
                mask.SetHumanoidBodyPartActive(part, upper);
            }

            for (int i = 0; i < mask.transformCount; i++)
            {
                string path = mask.GetTransformPath(i);
                string leaf = path.Substring(path.LastIndexOf('/') + 1);
                bool upper = leaf == "Weapon" || leaf.StartsWith("IK_", StringComparison.Ordinal)
                             || leaf.Contains("Fingers") || leaf.EndsWith("Thumb", StringComparison.Ordinal);
                mask.SetTransformActive(i, upper);
            }
        }

        static AvatarMask EnsureMask(string path, Action<AvatarMask> fill)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(FoxGunnerAssetFactory.ModelPath);
            var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(path);
            bool created = mask == null;
            if (created)
            {
                mask = new AvatarMask();
            }

            mask.transformCount = 0;
            mask.AddTransformPath(model.transform, true);
            fill(mask);
            if (created)
            {
                AssetDatabase.CreateAsset(mask, path);
            }
            else
            {
                EditorUtility.SetDirty(mask);
            }

            return mask;
        }

        // ------------------------------------------------------------------ import

        static Dictionary<string, AnimationClip> ImportClips(ClipTable table, Avatar avatar, AvatarMask clipMask)
        {
            if (!(AssetImporter.GetAtPath(SetPath) is ModelImporter importer))
            {
                throw new FileNotFoundException(SetPath + " is missing: run ArtSource/Tools/fox_anim/build_clips.py");
            }

            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.bakeAxisConversion = true;
            importer.importBlendShapes = false;
            importer.importVisibility = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importAnimation = true;
            importer.importAnimatedCustomProperties = false;
            importer.resampleCurves = true;
            importer.animationCompression = ModelImporterAnimationCompression.Optimal;
            importer.animationRotationError = 0.2f;  // tighter than default: keeps planted feet and the grip in place
            importer.animationPositionError = 0.1f;
            importer.animationScaleError = 0.5f;
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
            importer.sourceAvatar = avatar;

            ModelImporterClipAnimation[] takes = importer.defaultClipAnimations;
            if (takes.Length == 0)
            {
                importer.SaveAndReimport(); // first import of a fresh file: Unity has not read its takes yet
                takes = importer.defaultClipAnimations;
            }

            var result = new List<ModelImporterClipAnimation>();
            foreach (ClipEntry entry in table.clips)
            {
                ModelImporterClipAnimation take = takes.FirstOrDefault(t => t.takeName == entry.name || t.takeName.EndsWith("|" + entry.name, StringComparison.Ordinal));
                if (take == null)
                {
                    throw new InvalidOperationException($"{SetPath}: take '{entry.name}' not found (re-run build_clips.py)");
                }

                take.name = entry.name;
                take.loopTime = entry.loop;
                take.loopPose = entry.loop;
                take.lockRootRotation = true;      // motor-driven: everything stays in the pose, no root motion
                take.keepOriginalOrientation = true;
                take.lockRootHeightY = true;
                take.keepOriginalPositionY = true;
                take.lockRootPositionXZ = true;
                take.keepOriginalPositionXZ = true; // authored around the origin between the feet
                take.heightFromFeet = false;
                take.maskType = ClipAnimationMaskType.CopyFromOther;
                take.maskSource = clipMask;
                bool additive = entry.layer == AdditiveLayer;
                take.hasAdditiveReferencePose = additive;
                take.additiveReferencePoseFrame = take.firstFrame;
                take.curves = entry.curves == null
                    ? Array.Empty<ClipAnimationInfoCurve>()
                    : entry.curves.Select(c => new ClipAnimationInfoCurve { name = c.name, curve = Curve(c, entry.seconds) }).ToArray();
                result.Add(take);
            }

            importer.clipAnimations = result.ToArray();
            importer.SaveAndReimport();

            var clips = AssetDatabase.LoadAllAssetsAtPath(SetPath).OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal))
                .ToDictionary(c => c.name);
            foreach (ClipEntry entry in table.clips)
            {
                if (!clips.TryGetValue(entry.name, out AnimationClip clip) || !clip.isHumanMotion)
                {
                    throw new InvalidOperationException($"{SetPath}: humanoid clip '{entry.name}' was not imported");
                }
            }

            return clips;
        }

        /// <summary>Importer curves use normalized clip time (0..1).</summary>
        static AnimationCurve Curve(CurveEntry c, float seconds)
        {
            var keys = new Keyframe[c.times.Length];
            for (int i = 0; i < keys.Length; i++)
            {
                keys[i] = new Keyframe(seconds > 0f ? c.times[i] / seconds : 0f, c.values[i]);
            }

            var curve = new AnimationCurve(keys);
            for (int i = 0; i < keys.Length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
                AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
            }

            return curve;
        }

        // ------------------------------------------------------------------ controller

        public static AnimatorController BuildController(ClipTable table, Dictionary<string, AnimationClip> clips, AvatarMask upperMask,
            LocomotionRing[] rings)
        {
            string path = FoxGunnerAssetFactory.ControllerPath;
            AssetDatabase.DeleteAsset(path); // generated asset: rebuilt from the clip table every run
            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            AddParameters(controller);

            AnimationClip Clip(string name) => clips[Prefix + name];

            // ---------------- Base: locomotion and movement actions (full body)
            AnimatorControllerLayer[] layers = controller.layers;
            layers[0].iKPass = true; // FoxBodyAnimator's look-at bends the spine toward the aim
            controller.layers = layers;
            AnimatorStateMachine baseSm = controller.layers[0].stateMachine;

            AnimatorState idle = baseSm.AddState("Idle", new Vector3(300, 0));
            idle.motion = Clip("Combat_Idle");
            AnimatorState locomotion = controller.CreateBlendTreeInController("Locomotion", out BlendTree tree, 0);
            tree.blendType = BlendTreeType.FreeformDirectional2D;
            tree.blendParameter = AnimatorParams.MoveX;
            tree.blendParameterY = AnimatorParams.MoveY;
            tree.useAutomaticThresholds = false;
            tree.AddChild(Clip("Locomotion_Center"), Vector2.zero);
            foreach (ClipEntry e in table.clips.Where(c => c.speed > 0f))
            {
                tree.AddChild(clips[e.name], new Vector2(e.direction[0], e.direction[1]) * e.speed);
            }

            locomotion.speedParameterActive = true;
            locomotion.speedParameter = AnimatorParams.LocomotionSpeed;
            baseSm.defaultState = idle;

            AnimatorState jumpStart = State(baseSm, "JumpStart", Clip("Jump_Start"), 600, 0);
            AnimatorState jumpLoop = State(baseSm, "JumpLoop", Clip("Jump_Loop"), 600, 80);
            AnimatorState fall = State(baseSm, "Fall", Clip("Fall"), 600, 160);
            AnimatorState land = State(baseSm, "Land", Clip("Land"), 600, 240);
            AnimatorState slide = State(baseSm, "Slide", Clip("Slide"), 300, 240);
            AnimatorState dash = controller.CreateBlendTreeInController("Dash", out BlendTree dashTree, 0);
            dashTree.blendType = BlendTreeType.SimpleDirectional2D;
            dashTree.blendParameter = AnimatorParams.DashX;
            dashTree.blendParameterY = AnimatorParams.DashY;
            dashTree.useAutomaticThresholds = false;
            dashTree.AddChild(Clip("Dash_Forward"), new Vector2(0f, 1f));
            dashTree.AddChild(Clip("Dash_Backward"), new Vector2(0f, -1f));
            dashTree.AddChild(Clip("Dash_Left"), new Vector2(-1f, 0f));
            dashTree.AddChild(Clip("Dash_Right"), new Vector2(1f, 0f));

            Go(idle, locomotion, 0.15f).AddCondition(AnimatorConditionMode.Greater, 0.15f, AnimatorParams.Speed);
            Go(locomotion, idle, 0.2f).AddCondition(AnimatorConditionMode.Less, 0.1f, AnimatorParams.Speed);
            foreach (AnimatorState from in new[] { idle, locomotion, land })
            {
                Go(from, jumpStart, 0.05f).AddCondition(AnimatorConditionMode.If, 0f, AnimatorParams.Jump);
                AnimatorStateTransition off = Go(from, fall, 0.2f);
                off.AddCondition(AnimatorConditionMode.IfNot, 0f, AnimatorParams.IsGrounded);
                off.AddCondition(AnimatorConditionMode.Less, -2.5f, AnimatorParams.VerticalSpeed);
            }

            Exit(jumpStart, jumpLoop, 0.95f, 0.1f);
            AnimatorStateTransition hop = Exit(jumpStart, land, 0.5f, 0.05f);
            hop.AddCondition(AnimatorConditionMode.If, 0f, AnimatorParams.IsGrounded);
            Go(jumpLoop, fall, 0.25f).AddCondition(AnimatorConditionMode.Less, -1.5f, AnimatorParams.VerticalSpeed);
            Go(jumpLoop, land, 0.05f).AddCondition(AnimatorConditionMode.If, 0f, AnimatorParams.IsGrounded);
            Go(fall, land, 0.05f).AddCondition(AnimatorConditionMode.If, 0f, AnimatorParams.IsGrounded);
            AnimatorStateTransition landMove = Exit(land, locomotion, 0.3f, 0.12f);
            landMove.AddCondition(AnimatorConditionMode.Greater, 1.0f, AnimatorParams.Speed);
            Exit(land, idle, 0.8f, 0.15f);

            AnimatorStateTransition toDash = baseSm.AddAnyStateTransition(dash);
            Setup(toDash, 0.04f);
            toDash.canTransitionToSelf = true;
            toDash.AddCondition(AnimatorConditionMode.If, 0f, AnimatorParams.Dash);
            Exit(dash, locomotion, 0.8f, 0.15f).AddCondition(AnimatorConditionMode.If, 0f, AnimatorParams.IsGrounded);
            Exit(dash, fall, 0.8f, 0.15f).AddCondition(AnimatorConditionMode.IfNot, 0f, AnimatorParams.IsGrounded);

            Go(locomotion, slide, 0.1f).AddCondition(AnimatorConditionMode.If, 0f, AnimatorParams.Slide);
            Go(slide, locomotion, 0.2f).AddCondition(AnimatorConditionMode.IfNot, 0f, AnimatorParams.Slide);

            // ---------------- UpperBody: rifle handling, casts, heavy hit, interactions
            AnimatorStateMachine upper = AddLayer(controller, UpperLayer, upperMask, AnimatorLayerBlendingMode.Override);
            AnimatorState empty = upper.AddState("Empty", new Vector3(300, 0));
            upper.defaultState = empty;

            AnimatorState reload = State(upper, "Reload", Clip("Rifle_Reload"), 600, -200);
            reload.speedParameterActive = true;
            reload.speedParameter = AnimatorParams.ReloadSpeed;
            Any(upper, reload, 0.1f, false).AddCondition(AnimatorConditionMode.If, 0f, AnimatorParams.Reload);
            Exit(reload, empty, 0.92f, 0.12f);

            AnimatorState adsIn = State(upper, "ADS_Enter", Clip("Rifle_ADS_Enter"), 0, 200);
            AnimatorState adsIdle = State(upper, "ADS_Idle", Clip("Rifle_ADS_Idle"), 0, 280);
            AnimatorState adsOut = State(upper, "ADS_Exit", Clip("Rifle_ADS_Exit"), 0, 360);
            Go(empty, adsIn, 0.05f).AddCondition(AnimatorConditionMode.If, 0f, AnimatorParams.Aiming);
            Exit(adsIn, adsIdle, 0.95f, 0.05f);
            Go(adsIn, adsOut, 0.05f).AddCondition(AnimatorConditionMode.IfNot, 0f, AnimatorParams.Aiming);
            Go(adsIdle, adsOut, 0.05f).AddCondition(AnimatorConditionMode.IfNot, 0f, AnimatorParams.Aiming);
            Exit(adsOut, empty, 0.95f, 0.05f);

            AnimatorState equip = State(upper, "Equip", Clip("Rifle_Equip"), 300, 200);
            Go(empty, equip, 0.05f).AddCondition(AnimatorConditionMode.If, 0f, AnimatorParams.Equip);
            Exit(equip, empty, 0.9f, 0.1f);
            AnimatorState inspect = State(upper, "Inspect", Clip("Rifle_Inspect"), 300, 280);
            Go(empty, inspect, 0.25f).AddCondition(AnimatorConditionMode.If, 0f, AnimatorParams.Inspect);
            Exit(inspect, empty, 0.95f, 0.2f);
            Go(inspect, empty, 0.2f).AddCondition(AnimatorConditionMode.Greater, 0.5f, AnimatorParams.Speed);

            var casts = new (SkillMotion motion, string state, string clip)[]
            {
                (SkillMotion.QuickCast, "Skill_QuickCast", "Skill_QuickCast"),
                (SkillMotion.AreaCast, "Skill_AreaCast", "Skill_AreaCast"),
                (SkillMotion.SelfBuff, "Skill_SelfBuff", "Skill_SelfBuff"),
                (SkillMotion.Ultimate, "Skill_Ultimate", "Skill_Ultimate"),
            };
            int row = 0;
            foreach (var cast in casts)
            {
                AnimatorState s = State(upper, cast.state, Clip(cast.clip), 900, -200 + 80 * row++);
                AnimatorStateTransition t = Any(upper, s, 0.05f, true);
                t.AddCondition(AnimatorConditionMode.If, 0f, AnimatorParams.Skill);
                t.AddCondition(AnimatorConditionMode.Equals, (int)cast.motion, AnimatorParams.SkillMotion);
                Exit(s, empty, 0.9f, 0.12f);
            }

            AnimatorState chargeStart = State(upper, "Skill_Charged_Start", Clip("Skill_Charged_Start"), 900, 120);
            AnimatorState chargeLoop = State(upper, "Skill_Charged_Loop", Clip("Skill_Charged_Loop"), 900, 200);
            AnimatorState chargeRelease = State(upper, "Skill_Charged_Release", Clip("Skill_Charged_Release"), 900, 280);
            AnimatorStateTransition toCharge = Any(upper, chargeStart, 0.05f, true);
            toCharge.AddCondition(AnimatorConditionMode.If, 0f, AnimatorParams.Skill);
            toCharge.AddCondition(AnimatorConditionMode.Equals, (int)SkillMotion.Charged, AnimatorParams.SkillMotion);
            Exit(chargeStart, chargeLoop, 0.95f, 0.05f).AddCondition(AnimatorConditionMode.If, 0f, AnimatorParams.Charging);
            Exit(chargeStart, chargeRelease, 0.6f, 0.05f).AddCondition(AnimatorConditionMode.IfNot, 0f, AnimatorParams.Charging);
            Go(chargeLoop, chargeRelease, 0.05f).AddCondition(AnimatorConditionMode.IfNot, 0f, AnimatorParams.Charging);
            Exit(chargeRelease, empty, 0.9f, 0.12f);

            AnimatorState heavy = State(upper, "Hit_Heavy", Clip("Hit_Heavy"), 600, 120);
            Any(upper, heavy, 0.04f, true).AddCondition(AnimatorConditionMode.If, 0f, AnimatorParams.HitHeavy);
            Exit(heavy, empty, 0.85f, 0.12f);

            var gestures = new (InteractionMotion motion, string clip)[]
            {
                (InteractionMotion.Pickup, "Pickup"), (InteractionMotion.Interact, "Interact"),
                (InteractionMotion.UseItem, "Use_Item"), (InteractionMotion.Emote, "Emote"),
            };
            row = 0;
            foreach (var g in gestures)
            {
                AnimatorState s = State(upper, g.clip, Clip(g.clip), 1200, -200 + 80 * row++);
                AnimatorStateTransition t = Any(upper, s, 0.08f, false);
                t.AddCondition(AnimatorConditionMode.If, 0f, AnimatorParams.Interact);
                t.AddCondition(AnimatorConditionMode.Equals, (int)g.motion, AnimatorParams.InteractKind);
                Exit(s, empty, 0.9f, 0.12f);
            }

            AnimatorState reviveAlly = State(upper, "Revive_Ally", Clip("Revive_Ally"), 1200, 200);
            Go(empty, reviveAlly, 0.25f).AddCondition(AnimatorConditionMode.If, 0f, AnimatorParams.Reviving);
            Go(reviveAlly, empty, 0.25f).AddCondition(AnimatorConditionMode.IfNot, 0f, AnimatorParams.Reviving);

            // ---------------- Additive: recoil and flinch (upper body)
            AnimatorStateMachine additive = AddLayer(controller, AdditiveLayer, upperMask, AnimatorLayerBlendingMode.Additive);
            AnimatorState addEmpty = additive.AddState("Empty", new Vector3(300, 0));
            additive.defaultState = addEmpty;
            AnimatorState fire = State(additive, "Fire", Clip("Rifle_Fire"), 600, -80);
            Exit(fire, addEmpty, 1f, 0.02f);
            AnimatorState hitLight = State(additive, "Hit_Light", Clip("Hit_Light"), 600, 80);
            Any(additive, hitLight, 0.02f, true).AddCondition(AnimatorConditionMode.If, 0f, AnimatorParams.Hit);
            Exit(hitLight, addEmpty, 1f, 0.05f);

            // ---------------- Reaction: knockback, down, revive, death (full body over everything)
            AnimatorStateMachine reaction = AddLayer(controller, ReactionLayer, null, AnimatorLayerBlendingMode.Override);
            AnimatorState rEmpty = reaction.AddState("Empty", new Vector3(300, 0));
            reaction.defaultState = rEmpty;
            AnimatorState knock = State(reaction, "Knockback", Clip("Knockback"), 600, -160);
            AnimatorState downStart = State(reaction, "Downed_Start", Clip("Downed_Start"), 600, 0);
            AnimatorState downLoop = State(reaction, "Downed_Loop", Clip("Downed_Loop"), 600, 80);
            AnimatorState revived = State(reaction, "Revived", Clip("Revived"), 900, 80);
            AnimatorState death = State(reaction, "Death", Clip("Death"), 600, 200);

            Go(rEmpty, knock, 0.05f).AddCondition(AnimatorConditionMode.If, 0f, AnimatorParams.Knockback);
            Exit(knock, rEmpty, 0.92f, 0.15f);
            foreach (AnimatorState from in new[] { rEmpty, knock })
            {
                AnimatorStateTransition down = Go(from, downStart, 0.08f);
                down.AddCondition(AnimatorConditionMode.If, 0f, AnimatorParams.IsDown);
                down.AddCondition(AnimatorConditionMode.IfNot, 0f, AnimatorParams.Dead);
            }

            Exit(downStart, downLoop, 0.95f, 0.08f);
            foreach (AnimatorState from in new[] { downStart, downLoop })
            {
                Go(from, revived, 0.1f).AddCondition(AnimatorConditionMode.If, 0f, AnimatorParams.Revive);
            }

            Exit(revived, rEmpty, 0.95f, 0.2f);
            AnimatorStateTransition respawn = Go(downLoop, rEmpty, 0.15f);
            respawn.AddCondition(AnimatorConditionMode.IfNot, 0f, AnimatorParams.IsDown);
            respawn.AddCondition(AnimatorConditionMode.IfNot, 0f, AnimatorParams.Dead);
            foreach (AnimatorState from in new[] { rEmpty, knock, downStart, downLoop, revived })
            {
                Go(from, death, 0.15f).AddCondition(AnimatorConditionMode.If, 0f, AnimatorParams.Dead);
            }

            Go(death, rEmpty, 0.1f).AddCondition(AnimatorConditionMode.IfNot, 0f, AnimatorParams.Dead);

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            return controller;
        }

        static void AddParameters(AnimatorController c)
        {
            void Float(string n, float d = 0f) => c.AddParameter(new AnimatorControllerParameter { name = n, type = AnimatorControllerParameterType.Float, defaultFloat = d });
            void Bool(string n, bool d = false) => c.AddParameter(new AnimatorControllerParameter { name = n, type = AnimatorControllerParameterType.Bool, defaultBool = d });
            void Int(string n) => c.AddParameter(n, AnimatorControllerParameterType.Int);
            void Trig(string n) => c.AddParameter(n, AnimatorControllerParameterType.Trigger);

            // the shared set first (same names as the placeholder controller), then the Fox Gunner additions
            Float(AnimatorParams.MoveX);
            Float(AnimatorParams.MoveY);
            Float(AnimatorParams.Speed);
            Float(AnimatorParams.LocomotionSpeed, 1f);
            Bool(AnimatorParams.IsGrounded, true);
            Float(AnimatorParams.VerticalSpeed);
            Trig(AnimatorParams.Fire);
            Trig(AnimatorParams.Reload);
            Trig(AnimatorParams.Attack);
            Trig(AnimatorParams.Hit);
            Bool(AnimatorParams.Dead);
            Bool(AnimatorParams.IsSprinting);
            Trig(AnimatorParams.Jump);
            Trig(AnimatorParams.Dash);
            Float(AnimatorParams.DashX);
            Float(AnimatorParams.DashY, 1f);
            Bool(AnimatorParams.Slide);
            Float(AnimatorParams.ReloadSpeed, 1f);
            Bool(AnimatorParams.Aiming);
            Trig(AnimatorParams.Equip);
            Trig(AnimatorParams.Inspect);
            Trig(AnimatorParams.Skill);
            Int(AnimatorParams.SkillMotion);
            Bool(AnimatorParams.Charging);
            Trig(AnimatorParams.HitHeavy);
            Trig(AnimatorParams.Knockback);
            Bool(AnimatorParams.IsDown);
            Trig(AnimatorParams.Revive);
            Trig(AnimatorParams.Interact);
            Int(AnimatorParams.InteractKind);
            Bool(AnimatorParams.Reviving);
            Float(AnimatorParams.LeftHandIK, 1f); // written by clip curves
            Float(AnimatorParams.CastHand);       // written by clip curves
        }

        static AnimatorStateMachine AddLayer(AnimatorController c, string name, AvatarMask mask, AnimatorLayerBlendingMode mode)
        {
            c.AddLayer(name);
            AnimatorControllerLayer[] layers = c.layers;
            AnimatorControllerLayer layer = layers[layers.Length - 1];
            layer.defaultWeight = 1f;
            layer.avatarMask = mask;
            layer.blendingMode = mode;
            layers[layers.Length - 1] = layer;
            c.layers = layers;
            return c.layers[layers.Length - 1].stateMachine;
        }

        static AnimatorState State(AnimatorStateMachine sm, string name, Motion motion, float x, float y)
        {
            AnimatorState s = sm.AddState(name, new Vector3(x, y));
            s.motion = motion;
            return s;
        }

        static void Setup(AnimatorStateTransition t, float duration)
        {
            t.hasFixedDuration = true;
            t.duration = duration;
            t.exitTime = 0f;
            t.hasExitTime = false;
            // Any State transitions (dash, casts, hits) are part of the current state's set: letting it interrupt keeps
            // a dash or cast from waiting for a running blend (e.g. Land -> Idle) to finish.
            t.interruptionSource = TransitionInterruptionSource.Source;
        }

        /// <summary>Immediate transition (conditions only).</summary>
        static AnimatorStateTransition Go(AnimatorState from, AnimatorState to, float duration)
        {
            AnimatorStateTransition t = from.AddTransition(to);
            Setup(t, duration);
            return t;
        }

        /// <summary>Transition once the clip reached <paramref name="exitTime"/> (normalized), plus any conditions added later.</summary>
        static AnimatorStateTransition Exit(AnimatorState from, AnimatorState to, float exitTime, float duration)
        {
            AnimatorStateTransition t = from.AddTransition(to);
            Setup(t, duration);
            t.hasExitTime = true;
            t.exitTime = exitTime;
            return t;
        }

        static AnimatorStateTransition Any(AnimatorStateMachine sm, AnimatorState to, float duration, bool toSelf)
        {
            AnimatorStateTransition t = sm.AddAnyStateTransition(to);
            Setup(t, duration);
            t.canTransitionToSelf = toSelf;
            return t;
        }

        // ------------------------------------------------------------------ body setup (hand IK, look-at, tail/ears)

        public static Transform FindBone(Transform root, string name)
        {
            if (root.name == name)
            {
                return root;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindBone(root.GetChild(i), name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        static Transform Require(Transform root, string name)
        {
            Transform t = FindBone(root, name);
            if (t == null)
            {
                throw new InvalidOperationException($"{FoxGunnerAssetFactory.ModelPath}: bone '{name}' is missing (re-run fox_rig.py)");
            }

            return t;
        }

        /// <summary>
        /// Adds to a body instance (the root holding the Animator): Animation Rigging hand IK onto the clip-keyed
        /// IK_Hand_* goals (so the authored grip survives Humanoid retargeting and blends), the look-at, and the
        /// procedural tail and ears.
        /// </summary>
        public static void SetupBody(GameObject body)
        {
            Transform root = body.transform;
            var animator = body.GetComponent<Animator>();

            var rigGo = new GameObject("HandIK");
            rigGo.transform.SetParent(root, false);
            var rig = rigGo.AddComponent<Rig>();
            rig.weight = 1f;
            foreach (var side in new[] { ("Left", "L"), ("Right", "R") })
            {
                var go = new GameObject(side.Item1 + "HandIK");
                go.transform.SetParent(rigGo.transform, false);
                var ik = go.AddComponent<TwoBoneIKConstraint>();
                ik.weight = 1f;
                ik.data.root = Require(root, side.Item1 + "Arm");
                ik.data.mid = Require(root, side.Item1 + "ForeArm");
                ik.data.tip = Require(root, side.Item1 + "Hand");
                ik.data.target = Require(root, "IK_Hand_" + side.Item2);
                ik.data.hint = Require(root, "IK_Elbow_" + side.Item2);
                ik.data.targetPositionWeight = 1f;
                ik.data.targetRotationWeight = 1f;
                ik.data.hintWeight = 1f;
                ik.data.maintainTargetPositionOffset = false;
                ik.data.maintainTargetRotationOffset = false;
            }

            var builder = body.GetComponent<RigBuilder>();
            if (builder == null)
            {
                builder = body.AddComponent<RigBuilder>();
            }

            builder.layers.Clear();
            builder.layers.Add(new RigLayer(rig, true));

            var look = body.AddComponent<FoxBodyAnimator>();
            look.Configure(null, Require(root, "LeftHand"), Require(root, "Weapon"));

            var secondary = body.AddComponent<FoxSecondaryMotion>();
            Transform[] tail = Enumerable.Range(1, 4).Select(i => Require(root, $"Tail{i:00}")).ToArray();
            secondary.Configure(null, Require(root, "Hips"), tail, Require(root, "Ear_L"), Require(root, "Ear_R"),
                body.GetComponentInChildren<SkinnedMeshRenderer>());
            animator.keepAnimatorStateOnDisable = true;
            // Four influences for the fox even where the quality level uses two (Handheld): two cut the blended
            // tail root and finger joints into flat, stretched-looking shapes.
            foreach (SkinnedMeshRenderer skinned in body.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                skinned.quality = SkinQuality.Bone4;
            }
        }

        // ------------------------------------------------------------------ first-person arms

        public const string FirstPersonArmsPath = FoxGunnerAssetFactory.ArtFolder + "/Models/FoxGunner_FPArms.fbx";

        /// <summary>
        /// Shoulders in ViewModel space, each on its own below the screen: the left forearm reaches in diagonally
        /// from the lower left with the elbow just under the bottom edge, the right forearm follows its hand from the
        /// lower right. The elbow hints (FPArms space = ViewModel space) bend the arms that way. Solved for the
        /// lengthened first-person arms (fox_fp_arms.lengthen_arms) and the rifle placement in CharacterPrefabBuilder
        /// with FoxAnimationCaptureTests.Tune_FirstPersonLayout.
        /// </summary>
        public static readonly Vector3 FirstPersonLeftShoulder = new Vector3(-0.224f, -0.304f, -0.143f);
        public static readonly Vector3 FirstPersonRightShoulder = new Vector3(0.281f, -0.299f, -0.361f);
        public static readonly Vector3 FirstPersonLeftElbow = new Vector3(-0.464f, 0.863f, -0.199f);
        public static readonly Vector3 FirstPersonRightElbow = new Vector3(0.141f, 0.825f, -0.546f);

        static GameObject ConfigureFirstPersonArms()
        {
            if (!(AssetImporter.GetAtPath(FirstPersonArmsPath) is ModelImporter importer))
            {
                throw new FileNotFoundException(FirstPersonArmsPath + " is missing: run ArtSource/Tools/fox_anim/fox_fp_arms.py");
            }

            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.bakeAxisConversion = true;
            importer.importBlendShapes = true; // the left hand's "Open" shape (casting)
            importer.importVisibility = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importAnimation = false;
            importer.animationType = ModelImporterAnimationType.Generic; // keeps the skinning; posed by FirstPersonArms
            importer.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.None;
            importer.optimizeGameObjects = false;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<GameObject>(FirstPersonArmsPath);
        }

        /// <summary>
        /// Puts the fox's first-person arms in the view model (replacing the placeholder gloves): same material as the
        /// body, ViewModel layer, no shadows, and a <see cref="FirstPersonArms"/> that copies the body's hand goals
        /// (relative to its rifle) onto the view-model rifle.
        /// </summary>
        public static GameObject AddFirstPersonArms(Transform viewModelRoot, Transform bodyRoot, int layer, CharacterAnimatorDriver driver)
        {
            GameObject model = ConfigureFirstPersonArms();
            var arms = (GameObject)PrefabUtility.InstantiatePrefab(model, viewModelRoot);
            arms.name = "FPArms";
            Animator unused = arms.GetComponent<Animator>();
            if (unused != null)
            {
                Object.DestroyImmediate(unused); // nothing animates these bones but FirstPersonArms
            }

            Transform t = arms.transform;
            Transform Bone(string n) => Require(t, n);
            t.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            t.localScale = Vector3.one;
            Bone("LeftArm").position = viewModelRoot.TransformPoint(FirstPersonLeftShoulder);
            Bone("RightArm").position = viewModelRoot.TransformPoint(FirstPersonRightShoulder);

            // the body's soft toon look minus its normal map (baked for the body mesh, not these arms)
            Material material = AssetDatabase.LoadAssetAtPath<Material>(FoxGunnerLookFactory.FirstPersonMaterialPath)
                                ?? bodyRoot.GetComponentInChildren<SkinnedMeshRenderer>().sharedMaterials[0];
            foreach (Transform child in arms.GetComponentsInChildren<Transform>(true))
            {
                child.gameObject.layer = layer;
            }

            foreach (SkinnedMeshRenderer skinned in arms.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                skinned.sharedMaterial = material;
                skinned.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                skinned.receiveShadows = false;
                skinned.updateWhenOffscreen = true; // camera-attached: never culled by stale bounds
                skinned.skinnedMotionVectors = false;
                skinned.quality = SkinQuality.Bone4;
            }

            Transform viewRifle = viewModelRoot.GetComponentsInChildren<Transform>(true).First(x => x.name == "FireRifle");
            var fp = arms.AddComponent<FirstPersonArms>();
            fp.Configure(Require(bodyRoot, "HeldRifle"), Require(bodyRoot, "IK_Hand_L"), Require(bodyRoot, "IK_Hand_R"), viewRifle,
                new[] { Bone("LeftArm"), Bone("LeftForeArm"), Bone("LeftHand") },
                new[] { Bone("RightArm"), Bone("RightForeArm"), Bone("RightHand") });
            fp.SetDriver(driver); // LeftHandIK / CastHand: whether the left hand is on the rifle or casting right now
            // elbows: fixed hints for these shoulders (the body's are for its own shoulders and would lift the elbow into view)
            fp.ConfigureExtras(Require(bodyRoot, "FP_Grip_L"), null, null,
                arms.GetComponentInChildren<SkinnedMeshRenderer>(true));
            fp.SetElbowHints(FirstPersonLeftElbow, FirstPersonRightElbow);
            return arms;
        }

        /// <summary>Weapon bone of a body instance: the rifle's grip frame the clips animate.</summary>
        public static Transform WeaponBone(Transform bodyRoot) => Require(bodyRoot, "Weapon");

        /// <summary>
        /// Local rotation for a rifle prefab (+Z muzzle, +Y up, origin at the grip) parented to the Weapon bone. In the
        /// bind pose the Weapon bone is authored as the rifle frame facing the body's forward.
        /// </summary>
        public static Quaternion RifleLocalRotation(Transform bodyRoot, Transform weaponBone)
        {
            return Quaternion.Inverse(weaponBone.rotation) * bodyRoot.rotation;
        }
    }
}
