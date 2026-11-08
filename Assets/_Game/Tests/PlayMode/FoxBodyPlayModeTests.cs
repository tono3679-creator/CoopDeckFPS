#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CoopDeck.Animation;
using CoopDeck.Combat;
using CoopDeck.Core;
using CoopDeck.Effects;
using CoopDeck.Player;
using CoopDeck.Skills;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace CoopDeck.Tests
{
    /// <summary>
    /// The generated PF_Player's Fox Gunner body in play: gameplay events reach the right layer and state without
    /// delaying gameplay, both hands stay on the rifle while moving and strafing, the spine follows the aim pitch,
    /// and the tail and ears move on their own.
    /// </summary>
    public class FoxBodyPlayModeTests
    {
        const float Dt = 1f / 60f;
        const string PlayerPath = "Assets/_Game/Prefabs/Characters/PF_Player.prefab";
        readonly List<Object> spawned = new List<Object>();

        sealed class ScriptedCommands : ICharacterCommandSource
        {
            public CharacterCommands Next;

            public CharacterCommands GetCommands(float deltaTime)
            {
                CharacterCommands c = Next;
                Next.JumpPressed = false;
                Next.DashPressed = false;
                Next.FirePressed = false;
                Next.ReloadPressed = false;
                Next.SkillPressedMask = 0;
                return c;
            }
        }

        [SetUp]
        public void SetUp() => Time.captureDeltaTime = Dt;

        [TearDown]
        public void TearDown()
        {
            Time.captureDeltaTime = 0f;
            foreach (Object o in spawned.Where(o => o != null))
            {
                Object.Destroy(o);
            }

            spawned.Clear();
        }

        GameObject player;
        ScriptedCommands commands;
        Animator body;

        IEnumerator Spawn()
        {
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.position = new Vector3(0f, -0.5f, 0f);
            floor.transform.localScale = new Vector3(80f, 1f, 80f);
            spawned.Add(floor);
            // a sun, so the local player's shadow-only body is actually drawn (tail/ears sleep while it is not)
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.Hard;
            sun.transform.rotation = Quaternion.Euler(50f, 30f, 0f);
            spawned.Add(sun.gameObject);
            Physics.SyncTransforms();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath);
            Assert.That(prefab, Is.Not.Null, PlayerPath + " missing: run the bootstrap");
            player = Object.Instantiate(prefab, new Vector3(0f, 0.05f, 0f), Quaternion.identity);
            spawned.Add(player);
            commands = new ScriptedCommands();
            player.GetComponent<FpsCharacter>().CommandSource = commands;
            body = player.transform.Find("BodyVisual").GetComponent<Animator>();
            yield return Frames(30);
        }

        static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++)
            {
                yield return null;
            }
        }

        bool InState(string layer, string state)
        {
            int index = body.GetLayerIndex(layer);
            return body.GetCurrentAnimatorStateInfo(index).IsName(state) || (body.IsInTransition(index) && body.GetNextAnimatorStateInfo(index).IsName(state));
        }

        IEnumerator WaitForState(string layer, string state, int maxFrames, string because)
        {
            for (int i = 0; i < maxFrames && !InState(layer, state); i++)
            {
                yield return null;
            }

            Assert.That(InState(layer, state), Is.True, $"{because}: {layer}/{state}");
        }

        [UnityTest]
        public IEnumerator GameplayEvents_DriveTheRightLayerAndState()
        {
            yield return Spawn();
            var character = player.GetComponent<FpsCharacter>();
            var health = player.GetComponent<Health>();
            Assert.That(InState("Base Layer", "Idle"), Is.True, "standing still");

            commands.Next.Move = Vector2.up;
            yield return WaitForState("Base Layer", "Locomotion", 20, "moving");
            commands.Next.Move = Vector2.zero;
            yield return Frames(30);

            commands.Next.FireHeld = true;
            commands.Next.FirePressed = true;
            yield return WaitForState("Additive", "Fire", 6, "a shot restarts the recoil");
            commands.Next.FireHeld = false;
            yield return Frames(10);

            commands.Next.ReloadPressed = true;
            yield return WaitForState("UpperBody", "Reload", 6, "reload");
            Assert.That(body.GetFloat(AnimatorParams.ReloadSpeed), Is.EqualTo(1f).Within(0.05f), "1.7 s clip for a 1.7 s reload");
            yield return Frames(130);
            Assert.That(InState("UpperBody", "Empty"), Is.True, "back to the shared pose after the reload");

            health.ApplyDamage(new DamageInfo { Amount = 5f, SourceTeam = TeamId.Enemies });
            yield return WaitForState("Additive", "Hit_Light", 4, "small hit = additive flinch");
            yield return Frames(30);
            health.ApplyDamage(new DamageInfo { Amount = health.Max * 0.3f, SourceTeam = TeamId.Enemies });
            yield return WaitForState("UpperBody", "Hit_Heavy", 4, "big hit = upper-body stagger");
            yield return Frames(40);

            var deck = player.GetComponent<DeckController>();
            CoopDeck.Cards.CardDefinition card = deck.Deck.GetHandCard(0).Definition;
            SkillMotion motion = SkillMotionRules.For(card);
            string castState = motion == SkillMotion.Charged ? "Skill_Charged_Start" : "Skill_" + motion;
            float gaugeBefore = deck.Deck.GetHandCard(0).Gauge;
            commands.Next.SkillPressedMask = 1;
            yield return null;
            Assert.That(deck.Deck.GetHandCard(0) == null || deck.Deck.GetHandCard(0).Gauge < gaugeBefore || deck.Deck.GetHandCard(0).Definition != card,
                Is.True, "the card is spent at once: the cast motion never delays the effect");
            yield return WaitForState("UpperBody", castState, 5, card.displayName + " plays its cast motion");
            yield return Frames(60);

            commands.Next.JumpPressed = true;
            yield return WaitForState("Base Layer", "JumpStart", 5, "jump");
            yield return Frames(60);

            commands.Next.Move = Vector2.right;
            commands.Next.DashPressed = true;
            yield return WaitForState("Base Layer", "Dash", 4, "dash, even while another blend runs");
            commands.Next.Move = Vector2.zero;
            yield return Frames(40);

            health.ApplyDamage(new DamageInfo { Amount = 9999f, SourceTeam = TeamId.Enemies });
            yield return WaitForState("Reaction", "Downed_Start", 6, "0 HP = downed (waiting for a revive)");
            Assert.That(character.BodyAnimator.IsDowned, Is.True);
            yield return WaitForState("Reaction", "Downed_Loop", 90, "downed loop");
        }

        [UnityTest]
        public IEnumerator Downed_BleedOut_Death_ThenRespawn_ClearsTheReaction()
        {
            yield return Spawn();
            var revivable = player.GetComponent<Revivable>();
            revivable.Configure(2.5f, 0.6f, 2.4f, 0.45f); // short bleed-out for the test
            var health = player.GetComponent<Health>();
            health.ApplyDamage(new DamageInfo { Amount = 9999f, SourceTeam = TeamId.Enemies });
            yield return WaitForState("Reaction", "Downed_Start", 6, "downed");
            yield return WaitForState("Reaction", "Death", 90, "bled out = death");
            health.Revive(1f); // what PlayerRespawn does
            yield return WaitForState("Reaction", "Empty", 30, "respawn clears the reaction");
            yield return WaitForState("UpperBody", "Equip", 10, "and brings the rifle up");
        }

        [UnityTest]
        public IEnumerator BothHands_StayOnTheRifle_WhileMovingAndStrafing()
        {
            yield return Spawn();
            Transform rightHand = body.GetBoneTransform(HumanBodyBones.RightHand);
            Transform leftHand = body.GetBoneTransform(HumanBodyBones.LeftHand);
            Transform[] all = body.GetComponentsInChildren<Transform>(true);
            Transform rightGoal = all.First(t => t.name == "IK_Hand_R");
            Transform leftGoal = all.First(t => t.name == "IK_Hand_L");
            Transform weapon = all.First(t => t.name == "Weapon");
            Transform rifle = weapon.Find("HeldRifle");
            Assert.That(rifle, Is.Not.Null, "the rifle rides the Weapon bone");

            float worstRight = 0f, worstLeft = 0f;
            foreach (Vector2 move in new[] { Vector2.zero, Vector2.up, Vector2.right, Vector2.left, Vector2.down, new Vector2(0.7f, 0.7f), new Vector2(0f, 0.25f) })
            {
                commands.Next.Move = move;
                for (int i = 0; i < 40; i++)
                {
                    yield return null;
                    if (i < 15)
                    {
                        continue; // let the blend settle
                    }

                    worstRight = Mathf.Max(worstRight, Vector3.Distance(rightHand.position, rightGoal.position));
                    if (body.GetFloat(AnimatorParams.LeftHandIK) > 0.99f)
                    {
                        worstLeft = Mathf.Max(worstLeft, Vector3.Distance(leftHand.position, leftGoal.position));
                    }
                }
            }

            Debug.Log($"[FoxBody] hand-to-grip error while moving: right {worstRight * 100:0.0} cm, left {worstLeft * 100:0.0} cm");
            Assert.That(worstRight, Is.LessThan(0.03f), "right hand on the grip");
            Assert.That(worstLeft, Is.LessThan(0.03f), "left hand on the handguard");
        }

        [UnityTest]
        public IEnumerator FirstPersonHands_HoldTheRifle_KeepTheWristUntwisted_AndOpenToCast()
        {
            yield return Spawn();
            var arms = player.GetComponentInChildren<FirstPersonArms>(true);
            Assert.That(arms, Is.Not.Null);
            Transform foreL = arms.ForeArm(true);
            Transform foreR = arms.ForeArm(false);
            Quaternion restL = arms.RestHandInForearm(true);
            Quaternion restR = arms.RestHandInForearm(false);


            float worst = 0f, worstTwist = 0f;
            foreach (Vector2 move in new[] { Vector2.zero, Vector2.up, Vector2.right })
            {
                commands.Next.Move = move;
                for (int i = 0; i < 30; i++)
                {
                    yield return null;
                    worst = Mathf.Max(worst, Vector3.Distance(arms.Hand(true).position, arms.GoalPosition(true)),
                        Vector3.Distance(arms.Hand(false).position, arms.GoalPosition(false)));
                    foreach (var (fore, hand, rest) in new[] { (foreL, arms.Hand(true), restL), (foreR, arms.Hand(false), restR) })
                    {
                        Quaternion wrist = Quaternion.Inverse(fore.rotation * rest) * hand.rotation;
                        Vector3 axisLocal = Quaternion.Inverse(fore.rotation * rest) * (hand.position - fore.position).normalized;
                        worstTwist = Mathf.Max(worstTwist, Mathf.Abs(TwoBoneIK.TwistAngle(wrist, axisLocal)));
                    }
                }
            }

            commands.Next.Move = Vector2.zero;
            Debug.Log($"[FoxBody] first-person hands: worst goal error {worst * 100:0.0} cm, worst wrist twist {worstTwist:0} deg");
            Assert.That(worst, Is.LessThan(0.015f), "both first-person hands reach their goals on the rifle");
            Assert.That(worstTwist, Is.LessThan(75f), "the forearm takes its share of the twist (no pinched wrist)");

            Assert.That(arms.OpenAmount, Is.LessThan(0.05f), "the left hand grips the handguard");
            player.GetComponent<FpsCharacter>().BodyAnimator.Skill(SkillMotion.QuickCast);
            float peak = 0f;
            for (int i = 0; i < 20; i++)
            {
                yield return null;
                peak = Mathf.Max(peak, arms.OpenAmount);
            }

            Assert.That(peak, Is.GreaterThan(0.6f), "the left hand opens to cast");
        }

        /// <summary>
        /// The quick cast is a palm strike, not a sideways wave: the hand is pulled back first, then driven well past
        /// its hold on the handguard in the body and in first person (the shoulder follows it out there), the shot
        /// kicks the caster's own view, and everything is back in place afterwards.
        /// </summary>
        [UnityTest]
        public IEnumerator QuickCast_StrikesThePalmOut_InBothViews_AndKicksTheCastersView()
        {
            yield return Spawn();
            var arms = player.GetComponentInChildren<FirstPersonArms>(true);
            Transform eyes = player.GetComponent<FpsLook>().PitchPivot;
            Transform leftGoal = body.GetComponentsInChildren<Transform>(true).First(t => t.name == "IK_Hand_L");
            var shaker = player.GetComponentInChildren<CameraShaker>(true);
            var card = AssetDatabase.LoadAssetAtPath<CoopDeck.Cards.CardDefinition>("Assets/_Game/ScriptableObjects/Cards/CD_Fireball.asset");
            Assert.That(card, Is.Not.Null, "run the bootstrap");
            Assert.That(SkillMotionRules.For(card), Is.EqualTo(SkillMotion.QuickCast));

            float BodyForward() => body.transform.InverseTransformPoint(leftGoal.position).z;
            float ViewForward() => eyes.InverseTransformPoint(arms.Hand(true).position).z;
            float bodyHold = BodyForward(), viewHold = ViewForward();
            Vector3 cameraRest = shaker.transform.localPosition;

            player.GetComponent<FpsCharacter>().BodyAnimator.Skill(SkillMotion.QuickCast);
            player.GetComponent<SkillCaster>().CastCard(card);
            float bodyMax = float.MinValue, viewMax = float.MinValue, viewMin = float.MaxValue, kick = 0f, reach = 0f;
            int viewMinFrame = 0, viewMaxFrame = 0;
            for (int i = 1; i <= 30; i++)
            {
                yield return null;
                bodyMax = Mathf.Max(bodyMax, BodyForward());
                float view = ViewForward();
                if (view < viewMin)
                {
                    viewMin = view;
                    viewMinFrame = i;
                }

                if (view > viewMax)
                {
                    viewMax = view;
                    viewMaxFrame = i;
                }

                kick = Mathf.Max(kick, shaker.KickAmount);
                reach = Mathf.Max(reach, arms.CastReach);
            }

            Debug.Log($"[FoxBody] quick cast: body palm {bodyHold:0.000} -> {bodyMax:0.000} m forward; first-person wrist {viewHold:0.000} -> " +
                      $"{viewMin:0.000} (frame {viewMinFrame}) -> {viewMax:0.000} (frame {viewMaxFrame}); shoulder reach {reach * 100f:0.0} cm; kick {kick:0.00}");
            Assert.That(bodyMax - bodyHold, Is.GreaterThan(0.12f), "the body's palm strikes well past its hold on the handguard");
            Assert.That(viewMin, Is.LessThan(viewHold - 0.08f), "first person: the hand is pulled back before the strike");
            Assert.That(viewMinFrame, Is.LessThan(viewMaxFrame));
            Assert.That(viewMax - viewHold, Is.GreaterThan(0.12f), "first person: the palm is driven out past the handguard");
            Assert.That(reach, Is.GreaterThan(0.02f), "the first-person shoulder follows the strike out");
            Assert.That(kick, Is.GreaterThan(0.9f), "the shot kicks the caster's own view");

            yield return Frames(40);
            Assert.That(arms.CastReach, Is.EqualTo(0f), "the shoulder is back under the eyes");
            Assert.That(Vector3.Distance(shaker.transform.localPosition, cameraRest), Is.LessThan(1e-4f), "the view is back in place");
            Assert.That(Mathf.Abs(ViewForward() - viewHold), Is.LessThan(0.02f), "the hand is back on the handguard");
        }

        [UnityTest]
        public IEnumerator FirstPersonArms_ReachInFromTheLowerCorners_WithStraightWrists()
        {
            yield return Spawn();
            for (int i = 0; i < 30; i++)
            {
                yield return null;
            }

            var arms = player.GetComponentInChildren<FirstPersonArms>(true);
            Camera view = player.GetComponentsInChildren<Camera>(true).First(c => c.name == "ViewModelCamera");
            float bottom = -Mathf.Tan(view.fieldOfView * 0.5f * Mathf.Deg2Rad); // y/z of the lower screen edge
            Vector3 Cam(Transform t) => view.transform.InverseTransformPoint(t.position);
            Vector3 wristL = Cam(arms.Hand(true)), elbowL = Cam(arms.ForeArm(true)), wristR = Cam(arms.Hand(false));

            // how far each hand is bent off its forearm line (twist excluded)
            float Bend(bool left)
            {
                Transform lower = arms.ForeArm(left), hand = arms.Hand(left);
                Vector3 axis = (hand.position - lower.position).normalized;
                Quaternion off = hand.rotation * Quaternion.Inverse(lower.rotation * arms.RestHandInForearm(left));
                return Vector3.Angle(axis, off * axis);
            }

            Debug.Log($"[FoxBody] first-person layout: left wrist {wristL} elbow {elbowL} right wrist {wristR} (screen bottom y/z {bottom:0.000}); " +
                      $"wrist bend left {Bend(true):0} right {Bend(false):0} deg");
            Assert.That(wristL.y / wristL.z, Is.GreaterThan(bottom + 0.1f), "the left hand is well inside the view");
            Assert.That(elbowL.y / elbowL.z, Is.LessThan(bottom), "the left elbow stays under the lower edge");
            Assert.That(elbowL.x, Is.LessThan(wristL.x - 0.05f), "the left forearm reaches in from the lower left, not straight up");
            Assert.That(wristR.y / wristR.z, Is.GreaterThan(bottom * 1.15f), "the right hand on the grip reaches the lower edge");
            Assert.That(Bend(true), Is.LessThan(30f), "the left hand continues its forearm (no hand folded back over the bracer)");
            Assert.That(Bend(false), Is.LessThan(40f), "the right hand continues its forearm");
        }

        [UnityTest]
        public IEnumerator Spine_FollowsTheAimPitch_AndTheTailAndEarsMove()
        {
            yield return Spawn();
            var look = player.GetComponent<FpsLook>();
            Transform weapon = body.GetComponentsInChildren<Transform>(true).First(t => t.name == "Weapon");
            Transform rifle = weapon.Find("HeldRifle");

            Vector3 right = player.transform.right;
            float MuzzleError() => Vector3.SignedAngle(Vector3.ProjectOnPlane(rifle.forward, right),
                Vector3.ProjectOnPlane(look.GetAimRay().direction, right), right);
            float Pitch() => Vector3.SignedAngle(Vector3.ProjectOnPlane(rifle.forward, right), player.transform.forward, right);

            look.SetRotation(look.Yaw, 0f);
            yield return Frames(90);
            float level = Pitch();
            float levelError = MuzzleError();
            look.SetRotation(look.Yaw, -35f); // look up (FpsLook pitch is negative upward)
            yield return Frames(90);
            float up = Pitch();
            float upError = MuzzleError();
            Debug.Log($"[FoxBody] rifle pitch: level aim {level:0.0} (error {levelError:0.0}) deg, aiming up {up:0.0} (error {upError:0.0}) deg");
            Assert.That(Mathf.Abs(up - level), Is.GreaterThan(20f), "the body bends the rifle toward the aim");
            Assert.That(Mathf.Abs(levelError), Is.LessThan(4f), "aiming level: the muzzle points where the player aims");
            Assert.That(Mathf.Abs(upError), Is.LessThan(8f), "aiming up: the muzzle follows");
            look.SetRotation(look.Yaw, 0f);

            Transform tail = body.GetComponentsInChildren<Transform>(true).First(t => t.name == "Tail03");
            Transform ear = body.GetComponentsInChildren<Transform>(true).First(t => t.name == "Ear_L");
            var tailRotations = new List<Quaternion>();
            commands.Next.Move = Vector2.up;
            for (int i = 0; i < 60; i++)
            {
                yield return null;
                tailRotations.Add(tail.localRotation);
            }

            float swing = tailRotations.Max(q => Quaternion.Angle(q, tailRotations[0]));
            Assert.That(swing, Is.GreaterThan(4f), "the tail sways while running");
            Quaternion earBefore = ear.localRotation;
            player.GetComponent<Health>().ApplyDamage(new DamageInfo { Amount = player.GetComponent<Health>().Max * 0.3f, SourceTeam = TeamId.Enemies });
            yield return Frames(8);
            Assert.That(Quaternion.Angle(ear.localRotation, earBefore), Is.GreaterThan(8f), "a hit flattens the ears");
        }
    }
}
#endif
