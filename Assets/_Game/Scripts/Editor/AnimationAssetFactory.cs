using System;
using System.Collections.Generic;
using CoopDeck.Animation;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace CoopDeck.EditorTools
{
    public sealed class AnimationSet
    {
        public AnimatorController Humanoid;
        public AnimatorOverrideController Shooter;
    }

    /// <summary>
    /// Generates placeholder clips for the <see cref="PlaceholderRigBuilder"/> rig and the shared
    /// full-body controller (ANIMATION_GUIDELINES.md §3):
    ///   Base layer : Locomotion (2D Freeform Directional: idle + 8 walk directions, speed scaled
    ///                by LocomotionSpeed) ⇄ Airborne; AnyState → Death while Dead.
    ///   UpperBody  : AvatarMask without hips/legs; Empty ⇄ Attack / Hit / Fire / Reload.
    /// A shooter override swaps only the attack clip. Clip names follow A_Placeholder_&lt;Action&gt;.
    /// </summary>
    public static class AnimationAssetFactory
    {
        public const string Folder = GamePaths.Root + "/Animations/Placeholder";
        public const string ControllerPath = Folder + "/AC_PlaceholderHumanoid.controller";
        public const string ShooterOverridePath = Folder + "/AOC_Shooter.overrideController";
        public const string UpperBodyMaskPath = Folder + "/AM_UpperBody.mask";
        const float Hips = PlaceholderRigBuilder.HipsHeight;

        public static AnimationSet EnsureAnimations(bool overwrite)
        {
            EditorUtil.EnsureFolder(Folder);
            if (!overwrite && EditorUtil.Exists(ControllerPath) && EditorUtil.Exists(ShooterOverridePath))
            {
                return new AnimationSet
                {
                    Humanoid = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath),
                    Shooter = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(ShooterOverridePath),
                };
            }

            AnimationClip idle = Save(BuildIdle());
            var walks = new List<(Vector2 position, AnimationClip clip)>();
            string[] names = { "F", "FR", "R", "BR", "B", "BL", "L", "FL" };
            for (int i = 0; i < 8; i++)
            {
                float angle = i * 45f;
                Vector2 position = new Vector2(Mathf.Sin(angle * Mathf.Deg2Rad), Mathf.Cos(angle * Mathf.Deg2Rad));
                walks.Add((position, Save(BuildWalk(names[i], angle))));
            }

            AnimationClip fall = Save(BuildFall());
            AnimationClip death = Save(BuildDeath());
            AnimationClip melee = Save(BuildMeleeAttack());
            AnimationClip shoot = Save(BuildShootAttack());
            AnimationClip hit = Save(BuildHit());
            AnimationClip fire = Save(BuildFire());
            AnimationClip reload = Save(BuildReload());
            AvatarMask upperMask = EnsureUpperBodyMask();

            AssetDatabase.DeleteAsset(ControllerPath);
            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            controller.AddParameter(AnimatorParams.MoveX, AnimatorControllerParameterType.Float);
            controller.AddParameter(AnimatorParams.MoveY, AnimatorControllerParameterType.Float);
            controller.AddParameter(AnimatorParams.Speed, AnimatorControllerParameterType.Float);
            controller.AddParameter(AnimatorParams.LocomotionSpeed, AnimatorControllerParameterType.Float);
            controller.AddParameter(AnimatorParams.IsGrounded, AnimatorControllerParameterType.Bool);
            controller.AddParameter(AnimatorParams.VerticalSpeed, AnimatorControllerParameterType.Float);
            controller.AddParameter(AnimatorParams.Fire, AnimatorControllerParameterType.Trigger);
            controller.AddParameter(AnimatorParams.Reload, AnimatorControllerParameterType.Trigger);
            controller.AddParameter(AnimatorParams.Attack, AnimatorControllerParameterType.Trigger);
            controller.AddParameter(AnimatorParams.Hit, AnimatorControllerParameterType.Trigger);
            controller.AddParameter(AnimatorParams.Dead, AnimatorControllerParameterType.Bool);
            SetDefault(controller, AnimatorParams.IsGrounded, true);
            SetDefault(controller, AnimatorParams.LocomotionSpeed, 1f);

            // Base layer.
            AnimatorStateMachine baseLayer = controller.layers[0].stateMachine;
            AnimatorState locomotion = controller.CreateBlendTreeInController("Locomotion", out BlendTree tree, 0);
            tree.blendType = BlendTreeType.FreeformDirectional2D;
            tree.blendParameter = AnimatorParams.MoveX;
            tree.blendParameterY = AnimatorParams.MoveY;
            tree.AddChild(idle, Vector2.zero);
            foreach ((Vector2 position, AnimationClip clip) in walks)
            {
                tree.AddChild(clip, position);
            }

            locomotion.speedParameterActive = true;
            locomotion.speedParameter = AnimatorParams.LocomotionSpeed;
            baseLayer.defaultState = locomotion;

            AnimatorState airborne = baseLayer.AddState("Airborne");
            airborne.motion = fall;
            Transition(locomotion, airborne, 0.12f, AnimatorConditionMode.IfNot, AnimatorParams.IsGrounded);
            Transition(airborne, locomotion, 0.1f, AnimatorConditionMode.If, AnimatorParams.IsGrounded);

            AnimatorState dead = baseLayer.AddState("Death");
            dead.motion = death;
            AnimatorStateTransition toDeath = baseLayer.AddAnyStateTransition(dead);
            toDeath.AddCondition(AnimatorConditionMode.If, 0f, AnimatorParams.Dead);
            toDeath.duration = 0.1f;
            toDeath.canTransitionToSelf = false;
            Transition(dead, locomotion, 0.2f, AnimatorConditionMode.IfNot, AnimatorParams.Dead);

            // Upper-body layer: actions play over whatever the legs are doing.
            controller.AddLayer("UpperBody");
            AnimatorControllerLayer[] layers = controller.layers;
            layers[1].avatarMask = upperMask;
            layers[1].defaultWeight = 1f;
            layers[1].blendingMode = AnimatorLayerBlendingMode.Override;
            controller.layers = layers;

            AnimatorStateMachine upper = controller.layers[1].stateMachine;
            AnimatorState empty = upper.AddState("Empty");
            upper.defaultState = empty;
            Action(upper, empty, "Attack", melee, AnimatorParams.Attack);
            Action(upper, empty, "Hit", hit, AnimatorParams.Hit);
            Action(upper, empty, "Fire", fire, AnimatorParams.Fire);
            Action(upper, empty, "Reload", reload, AnimatorParams.Reload);

            EditorUtility.SetDirty(controller);

            AssetDatabase.DeleteAsset(ShooterOverridePath);
            var shooter = new AnimatorOverrideController(controller) { name = "AOC_Shooter" };
            shooter[melee] = shoot;
            AssetDatabase.CreateAsset(shooter, ShooterOverridePath);
            AssetDatabase.SaveAssets();

            return new AnimationSet { Humanoid = controller, Shooter = shooter };
        }

        static void SetDefault(AnimatorController controller, string name, object value)
        {
            AnimatorControllerParameter[] parameters = controller.parameters;
            foreach (AnimatorControllerParameter p in parameters)
            {
                if (p.name != name)
                {
                    continue;
                }

                if (value is bool b) p.defaultBool = b;
                if (value is float f) p.defaultFloat = f;
            }

            controller.parameters = parameters;
        }

        static void Transition(AnimatorState from, AnimatorState to, float duration, AnimatorConditionMode mode, string parameter)
        {
            AnimatorStateTransition t = from.AddTransition(to);
            t.hasExitTime = false;
            t.duration = duration;
            t.AddCondition(mode, 0f, parameter);
        }

        static void Action(AnimatorStateMachine machine, AnimatorState empty, string name, AnimationClip clip, string trigger)
        {
            AnimatorState state = machine.AddState(name);
            state.motion = clip;
            AnimatorStateTransition into = empty.AddTransition(state);
            into.hasExitTime = false;
            into.duration = 0.05f;
            into.AddCondition(AnimatorConditionMode.If, 0f, trigger);

            AnimatorStateTransition back = state.AddTransition(empty);
            back.hasExitTime = true;
            back.exitTime = 0.9f;
            back.duration = 0.12f;
        }

        static AvatarMask EnsureUpperBodyMask()
        {
            AssetDatabase.DeleteAsset(UpperBodyMaskPath);
            var mask = new AvatarMask { name = "AM_UpperBody" };
            var paths = new List<(string path, bool active)> { ("", false) };
            foreach (string p in RigParts.LowerBodyPaths) paths.Add((p, false));
            foreach (string p in RigParts.UpperBodyPaths) paths.Add((p, true));
            mask.transformCount = paths.Count;
            for (int i = 0; i < paths.Count; i++)
            {
                mask.SetTransformPath(i, paths[i].path);
                mask.SetTransformActive(i, paths[i].active);
            }

            AssetDatabase.CreateAsset(mask, UpperBodyMaskPath);
            return mask;
        }

        // ---------------------------------------------------------------- clips

        sealed class ClipBuilder
        {
            const int Samples = 16;
            public readonly AnimationClip Clip;
            readonly float length;

            public ClipBuilder(string name, float length, bool loop)
            {
                this.length = length;
                Clip = new AnimationClip { name = name, frameRate = 30f };
                AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(Clip);
                settings.loopTime = loop;
                AnimationUtility.SetAnimationClipSettings(Clip, settings);
            }

            /// <summary>Rotation over normalized time t (0..1) as Euler degrees.</summary>
            public void Rotate(string path, Func<float, Vector3> euler)
            {
                var keys = new Keyframe[4][];
                for (int c = 0; c < 4; c++) keys[c] = new Keyframe[Samples + 1];
                for (int i = 0; i <= Samples; i++)
                {
                    float t = i / (float)Samples;
                    Quaternion q = Quaternion.Euler(euler(t));
                    float time = t * length;
                    keys[0][i] = new Keyframe(time, q.x);
                    keys[1][i] = new Keyframe(time, q.y);
                    keys[2][i] = new Keyframe(time, q.z);
                    keys[3][i] = new Keyframe(time, q.w);
                }

                Clip.SetCurve(path, typeof(Transform), "localRotation.x", Smooth(keys[0]));
                Clip.SetCurve(path, typeof(Transform), "localRotation.y", Smooth(keys[1]));
                Clip.SetCurve(path, typeof(Transform), "localRotation.z", Smooth(keys[2]));
                Clip.SetCurve(path, typeof(Transform), "localRotation.w", Smooth(keys[3]));
            }

            public void Move(string path, Func<float, Vector3> position)
            {
                var x = new Keyframe[Samples + 1];
                var y = new Keyframe[Samples + 1];
                var z = new Keyframe[Samples + 1];
                for (int i = 0; i <= Samples; i++)
                {
                    float t = i / (float)Samples;
                    Vector3 p = position(t);
                    x[i] = new Keyframe(t * length, p.x);
                    y[i] = new Keyframe(t * length, p.y);
                    z[i] = new Keyframe(t * length, p.z);
                }

                Clip.SetCurve(path, typeof(Transform), "localPosition.x", Smooth(x));
                Clip.SetCurve(path, typeof(Transform), "localPosition.y", Smooth(y));
                Clip.SetCurve(path, typeof(Transform), "localPosition.z", Smooth(z));
            }

            static AnimationCurve Smooth(Keyframe[] keys)
            {
                var curve = new AnimationCurve(keys);
                for (int i = 0; i < curve.length; i++)
                {
                    curve.SmoothTangents(i, 0f);
                }

                return curve;
            }

            public AnimationClip Finish()
            {
                Clip.EnsureQuaternionContinuity();
                return Clip;
            }
        }

        static AnimationClip Save(AnimationClip clip)
        {
            string path = $"{Folder}/{clip.name}.anim";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(clip, path);
            return clip;
        }

        static float Wave(float t) => Mathf.Sin(t * Mathf.PI * 2f);

        /// <summary>Holds a pose with a gentle ease from 0 to 1 and back (for one-shot actions).</summary>
        static float Pulse(float t, float peak) => t < peak ? Mathf.SmoothStep(0f, 1f, t / peak) : Mathf.SmoothStep(1f, 0f, (t - peak) / (1f - peak));

        static AnimationClip BuildIdle()
        {
            var b = new ClipBuilder("A_Placeholder_Idle", 3f, true);
            b.Move(RigParts.HipsPath, t => new Vector3(0f, Hips - 0.006f * (1f - Mathf.Cos(t * Mathf.PI * 2f)), 0f));
            b.Rotate(RigParts.SpinePath, t => new Vector3(1.5f * Wave(t), 0f, 0f));
            b.Rotate(RigParts.HeadPath, t => new Vector3(-1f * Wave(t), 2f * Wave(t * 0.5f), 0f));
            b.Rotate(RigParts.ShoulderLPath, t => new Vector3(0f, 0f, -8f - 1.5f * Wave(t)));
            b.Rotate(RigParts.ShoulderRPath, t => new Vector3(0f, 0f, 8f + 1.5f * Wave(t)));
            b.Rotate(RigParts.HipLPath, t => Vector3.zero);
            b.Rotate(RigParts.HipRPath, t => Vector3.zero);
            return b.Finish();
        }

        /// <param name="direction">Degrees clockwise from forward (0 = forward, 90 = strafe right).</param>
        static AnimationClip BuildWalk(string suffix, float direction)
        {
            const float stride = 28f;
            float forward = Mathf.Cos(direction * Mathf.Deg2Rad);
            float side = Mathf.Sin(direction * Mathf.Deg2Rad);
            var b = new ClipBuilder($"A_Placeholder_Walk_{suffix}", 0.9f, true);

            // Negative pitch swings a leg forward; positive roll swings it to the character's right.
            b.Rotate(RigParts.HipLPath, t => new Vector3(-stride * Wave(t) * forward, 0f, stride * 0.6f * Wave(t) * side));
            b.Rotate(RigParts.HipRPath, t => new Vector3(stride * Wave(t) * forward, 0f, -stride * 0.6f * Wave(t) * side));
            b.Rotate(RigParts.ShoulderLPath, t => new Vector3(stride * 0.7f * Wave(t) * forward, 0f, -8f));
            b.Rotate(RigParts.ShoulderRPath, t => new Vector3(-stride * 0.7f * Wave(t) * forward, 0f, 8f));
            b.Rotate(RigParts.SpinePath, t => new Vector3(5f * forward, 6f * Wave(t) * forward, -4f * side));
            b.Rotate(RigParts.HeadPath, t => new Vector3(-3f * forward, -5f * Wave(t) * forward, 3f * side));
            b.Move(RigParts.HipsPath, t => new Vector3(0f, Hips + 0.03f * Mathf.Cos(t * Mathf.PI * 4f), 0f));
            return b.Finish();
        }

        static AnimationClip BuildFall()
        {
            var b = new ClipBuilder("A_Placeholder_Fall", 1f, true);
            b.Rotate(RigParts.HipLPath, t => new Vector3(-28f + 3f * Wave(t), 0f, -4f));
            b.Rotate(RigParts.HipRPath, t => new Vector3(-10f - 3f * Wave(t), 0f, 4f));
            b.Rotate(RigParts.ShoulderLPath, t => new Vector3(-10f, 0f, -38f - 4f * Wave(t)));
            b.Rotate(RigParts.ShoulderRPath, t => new Vector3(-10f, 0f, 38f + 4f * Wave(t)));
            b.Rotate(RigParts.SpinePath, t => new Vector3(-4f, 0f, 0f));
            b.Move(RigParts.HipsPath, t => new Vector3(0f, Hips, 0f));
            return b.Finish();
        }

        static AnimationClip BuildDeath()
        {
            var b = new ClipBuilder("A_Placeholder_Death", 0.9f, false);
            b.Move(RigParts.HipsPath, t => new Vector3(0f, Mathf.Lerp(Hips, 0.25f, Mathf.SmoothStep(0f, 1f, t)), -0.3f * t));
            b.Rotate(RigParts.HipsPath, t => new Vector3(-85f * Mathf.SmoothStep(0f, 1f, t), 0f, 0f));
            b.Rotate(RigParts.ShoulderLPath, t => new Vector3(-20f * t, 0f, -60f * t));
            b.Rotate(RigParts.ShoulderRPath, t => new Vector3(-20f * t, 0f, 60f * t));
            b.Rotate(RigParts.HipLPath, t => new Vector3(-25f * t, 0f, -6f * t));
            b.Rotate(RigParts.HipRPath, t => new Vector3(-10f * t, 0f, 6f * t));
            b.Rotate(RigParts.HeadPath, t => new Vector3(-20f * t, 15f * t, 0f));
            return b.Finish();
        }

        /// <summary>Raise the right arm during the 0.55 s wind-up, strike down, recover.</summary>
        static AnimationClip BuildMeleeAttack()
        {
            var b = new ClipBuilder("A_Placeholder_AttackMelee", 0.75f, false);
            b.Rotate(RigParts.ShoulderRPath, t =>
            {
                float pitch = t < 0.73f ? Mathf.Lerp(0f, -150f, Mathf.SmoothStep(0f, 1f, t / 0.73f))
                    : t < 0.87f ? Mathf.Lerp(-150f, -40f, (t - 0.73f) / 0.14f)
                    : Mathf.Lerp(-40f, 0f, (t - 0.87f) / 0.13f);
                return new Vector3(pitch, 0f, 12f);
            });
            b.Rotate(RigParts.ShoulderLPath, t => new Vector3(-30f * Pulse(t, 0.7f), 0f, -20f * Pulse(t, 0.7f) - 8f));
            b.Rotate(RigParts.SpinePath, t => new Vector3(t < 0.73f ? -10f * (t / 0.73f) : Mathf.Lerp(-10f, 15f, Mathf.Clamp01((t - 0.73f) / 0.14f)) * (1f - Mathf.Clamp01((t - 0.87f) / 0.13f)), -15f * Pulse(t, 0.7f), 0f));
            b.Rotate(RigParts.HeadPath, t => Vector3.zero);
            return b.Finish();
        }

        /// <summary>Both arms forward during the wind-up, a kick when the shot leaves.</summary>
        static AnimationClip BuildShootAttack()
        {
            var b = new ClipBuilder("A_Placeholder_AttackShoot", 0.85f, false);
            b.Rotate(RigParts.ShoulderLPath, t => new Vector3(-85f * Pulse(t, 0.75f) - (t > 0.82f && t < 0.9f ? 15f : 0f), 0f, -8f));
            b.Rotate(RigParts.ShoulderRPath, t => new Vector3(-85f * Pulse(t, 0.75f) - (t > 0.82f && t < 0.9f ? 15f : 0f), 0f, 8f));
            b.Rotate(RigParts.SpinePath, t => new Vector3(-6f * Pulse(t, 0.85f), 0f, 0f));
            b.Rotate(RigParts.HeadPath, t => Vector3.zero);
            return b.Finish();
        }

        static AnimationClip BuildHit()
        {
            var b = new ClipBuilder("A_Placeholder_Hit", 0.3f, false);
            b.Rotate(RigParts.SpinePath, t => new Vector3(-14f * Pulse(t, 0.25f), 6f * Pulse(t, 0.25f), 0f));
            b.Rotate(RigParts.HeadPath, t => new Vector3(-10f * Pulse(t, 0.25f), 0f, 0f));
            b.Rotate(RigParts.ShoulderLPath, t => new Vector3(0f, 0f, -8f - 12f * Pulse(t, 0.25f)));
            b.Rotate(RigParts.ShoulderRPath, t => new Vector3(0f, 0f, 8f + 12f * Pulse(t, 0.25f)));
            return b.Finish();
        }

        static AnimationClip BuildFire()
        {
            var b = new ClipBuilder("A_Placeholder_Fire", 0.15f, false);
            b.Rotate(RigParts.SpinePath, t => new Vector3(-4f * Pulse(t, 0.3f), 0f, 0f));
            b.Rotate(RigParts.ShoulderRPath, t => new Vector3(-80f - 10f * Pulse(t, 0.3f), 0f, 8f));
            b.Rotate(RigParts.ShoulderLPath, t => new Vector3(-70f - 8f * Pulse(t, 0.3f), 30f, -8f));
            b.Rotate(RigParts.HeadPath, t => Vector3.zero);
            return b.Finish();
        }

        static AnimationClip BuildReload()
        {
            var b = new ClipBuilder("A_Placeholder_Reload", 1.4f, false);
            b.Rotate(RigParts.ShoulderRPath, t => new Vector3(-70f, 0f, 8f));
            b.Rotate(RigParts.ShoulderLPath, t => new Vector3(-70f + 55f * Pulse(t, 0.45f), 20f, -8f - 10f * Pulse(t, 0.45f)));
            b.Rotate(RigParts.SpinePath, t => new Vector3(6f * Pulse(t, 0.45f), -8f * Pulse(t, 0.45f), 0f));
            b.Rotate(RigParts.HeadPath, t => new Vector3(12f * Pulse(t, 0.45f), 0f, 0f));
            return b.Finish();
        }
    }
}
