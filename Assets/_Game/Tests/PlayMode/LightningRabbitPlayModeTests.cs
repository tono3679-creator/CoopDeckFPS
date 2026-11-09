#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CoopDeck.Animation;
using CoopDeck.Characters;
using CoopDeck.Combat;
using CoopDeck.Core;
using CoopDeck.Player;
using CoopDeck.Skills;
using CoopDeck.Weapons;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace CoopDeck.Tests
{
    /// <summary>
    /// The Lightning Rabbit in play (Docs/LIGHTNING_RABBIT.md): selecting it puts its body, Lightning SMG and
    /// first-person arms into PF_Player in place of the fox's; both hands stay on the SMG in the body and in first
    /// person; the SMG follows the aim; the ears, scarf and tail move on their own; and the fox stays the default.
    /// </summary>
    public class LightningRabbitPlayModeTests
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

        GameObject player;
        ScriptedCommands commands;
        Animator body;

        [SetUp]
        public void SetUp() => Time.captureDeltaTime = Dt;

        [TearDown]
        public void TearDown()
        {
            Time.captureDeltaTime = 0f;
            CharacterSelection.Override = "FoxGunner"; // back to the assembly-wide default (CharacterSelectionPin)
            foreach (Object o in spawned.Where(o => o != null))
            {
                Object.Destroy(o);
            }

            spawned.Clear();
        }

        IEnumerator Spawn(string character)
        {
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.position = new Vector3(0f, -0.5f, 0f);
            floor.transform.localScale = new Vector3(80f, 1f, 80f);
            spawned.Add(floor);
            var sun = new GameObject("Sun").AddComponent<Light>(); // the shadow-only body is drawn (springs sleep otherwise)
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.Hard;
            sun.transform.rotation = Quaternion.Euler(50f, 30f, 0f);
            spawned.Add(sun.gameObject);
            Physics.SyncTransforms();
            CharacterSelection.Override = character;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath);
            Assert.That(prefab, Is.Not.Null, PlayerPath + " missing: run the bootstrap");
            player = Object.Instantiate(prefab, new Vector3(0f, 0.05f, 0f), Quaternion.identity);
            spawned.Add(player);
            commands = new ScriptedCommands();
            player.GetComponent<FpsCharacter>().CommandSource = commands;
            yield return Frames(30);
            body = player.transform.Find("BodyVisual").GetComponent<Animator>();
        }

        static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++)
            {
                yield return null;
            }
        }

        Transform BodyBone(string name) => body.GetComponentsInChildren<Transform>(true).First(t => t.name == name);

        [UnityTest]
        public IEnumerator Selecting_TheRabbit_PutsItsBodySmgAndArmsInThePlayer()
        {
            yield return Spawn("LightningRabbit");
            var swap = player.GetComponent<PlayerCharacterSwap>();
            Assert.That(swap.Current.Id, Is.EqualTo("LightningRabbit"));
            Assert.That(swap.Current.Element, Is.EqualTo(Element.Lightning));
            Assert.That(player.transform.Cast<Transform>().Count(t => t.name == "BodyVisual"), Is.EqualTo(1), "only the rabbit's body is left");
            Assert.That(body.runtimeAnimatorController.name, Is.EqualTo("AC_LightningRabbit_TP"));
            Assert.That(body.avatar.isHuman, Is.True);
            Assert.That(BodyBone("Weapon").Find("HeldSMG"), Is.Not.Null, "the SMG rides the Weapon bone");
            Assert.That(player.GetComponentsInChildren<Transform>(true).Any(t => t.name == "HeldRifle"), Is.False, "no fox rifle left");

            Transform viewModel = player.GetComponentsInChildren<ViewModelAnimator>(true).First().transform;
            Assert.That(viewModel.GetComponentsInChildren<Transform>(true).Any(t => t.name == "LightningSMG"), Is.True, "first-person SMG");
            Assert.That(viewModel.GetComponentsInChildren<Transform>(true).Any(t => t.name == "FireRifle"), Is.False, "no first-person rifle");
            var arms = viewModel.GetComponentInChildren<FirstPersonArms>(true);
            Assert.That(arms, Is.Not.Null);
            Assert.That(arms.GetComponentInChildren<SkinnedMeshRenderer>().sharedMaterial.name, Is.EqualTo("M_LightningRabbit_SoftToon_FirstPerson"));

            var weapon = player.GetComponent<HitscanWeapon>();
            Assert.That(weapon.Muzzle.parent.name, Is.EqualTo("LightningSMG"), "shots leave the first-person SMG's muzzle");
            var caster = player.GetComponent<SkillCaster>();
            Assert.That(caster.CastHand, Is.SameAs(arms.Hand(true)), "projectiles charge in the rabbit's left palm");
            Assert.That(player.GetComponent<CharacterAnimatorDriver>().Animator, Is.SameAs(body), "the driver plays the rabbit's controller");
        }

        [UnityTest]
        public IEnumerator TheFox_StaysTheDefault_AndUnknownIdsKeepIt()
        {
            yield return Spawn("NoSuchCharacter");
            Assert.That(body.runtimeAnimatorController.name, Is.EqualTo("AC_FoxGunner_TP"));
            Assert.That(player.GetComponent<PlayerCharacterSwap>().Current.Id, Is.EqualTo("FoxGunner"));
            Assert.That(player.GetComponentInChildren<CharacterKit>(true), Is.Null, "no kit was put in");
        }

        [UnityTest]
        public IEnumerator BothHands_StayOnTheSmg_WhileMovingAndStrafing()
        {
            yield return Spawn("LightningRabbit");
            Transform rightHand = body.GetBoneTransform(HumanBodyBones.RightHand);
            Transform leftHand = body.GetBoneTransform(HumanBodyBones.LeftHand);
            Transform rightGoal = BodyBone("IK_Hand_R");
            Transform leftGoal = BodyBone("IK_Hand_L");

            float worstRight = 0f, worstLeft = 0f;
            foreach (Vector2 move in new[] { Vector2.zero, Vector2.up, Vector2.right, Vector2.left, Vector2.down, new Vector2(0.7f, 0.7f) })
            {
                commands.Next.Move = move;
                for (int i = 0; i < 40; i++)
                {
                    yield return null;
                    if (i < 15)
                    {
                        continue;
                    }

                    worstRight = Mathf.Max(worstRight, Vector3.Distance(rightHand.position, rightGoal.position));
                    if (body.GetFloat(AnimatorParams.LeftHandIK) > 0.99f)
                    {
                        worstLeft = Mathf.Max(worstLeft, Vector3.Distance(leftHand.position, leftGoal.position));
                    }
                }
            }

            Debug.Log($"[Rabbit] hand-to-grip error while moving: right {worstRight * 100:0.0} cm, left {worstLeft * 100:0.0} cm");
            Assert.That(worstRight, Is.LessThan(0.03f), "right hand on the pistol grip");
            Assert.That(worstLeft, Is.LessThan(0.03f), "left hand on the foregrip");
        }

        [UnityTest]
        public IEnumerator FirstPersonHands_HoldTheSmg_InsideTheView()
        {
            yield return Spawn("LightningRabbit");
            var arms = player.GetComponentInChildren<FirstPersonArms>(true);
            float worst = 0f;
            foreach (Vector2 move in new[] { Vector2.zero, Vector2.up, Vector2.right })
            {
                commands.Next.Move = move;
                for (int i = 0; i < 30; i++)
                {
                    yield return null;
                    worst = Mathf.Max(worst, Vector3.Distance(arms.Hand(true).position, arms.GoalPosition(true)),
                        Vector3.Distance(arms.Hand(false).position, arms.GoalPosition(false)));
                }
            }

            commands.Next.Move = Vector2.zero;
            yield return Frames(20);
            Camera view = player.GetComponentsInChildren<Camera>(true).First(c => c.name == "ViewModelCamera");
            float bottom = -Mathf.Tan(view.fieldOfView * 0.5f * Mathf.Deg2Rad);
            Vector3 Cam(Transform t) => view.transform.InverseTransformPoint(t.position);
            Vector3 wristL = Cam(arms.Hand(true)), wristR = Cam(arms.Hand(false)), elbowL = Cam(arms.ForeArm(true));
            Debug.Log($"[Rabbit] first-person hands: worst goal error {worst * 100:0.0} cm; left wrist {wristL} elbow {elbowL} " +
                      $"right wrist {wristR} (screen bottom y/z {bottom:0.000})");
            Assert.That(worst, Is.LessThan(0.02f), "both first-person hands reach their goals on the SMG");
            Assert.That(wristL.y / wristL.z, Is.GreaterThan(bottom + 0.05f), "the left hand on the foregrip is inside the view");
            Assert.That(elbowL.y / elbowL.z, Is.LessThan(bottom), "the left elbow stays under the lower edge");
        }

        [UnityTest]
        public IEnumerator Smg_FollowsTheAim_AndTheEarsScarfAndTailMove()
        {
            yield return Spawn("LightningRabbit");
            var look = player.GetComponent<FpsLook>();
            Transform smg = BodyBone("Weapon").Find("HeldSMG");
            Vector3 right = player.transform.right;
            float Pitch() => Vector3.SignedAngle(Vector3.ProjectOnPlane(smg.forward, right), player.transform.forward, right);

            look.SetRotation(look.Yaw, 0f);
            yield return Frames(90);
            float level = Pitch();
            look.SetRotation(look.Yaw, -35f);
            yield return Frames(90);
            float up = Pitch();
            Debug.Log($"[Rabbit] SMG pitch: level aim {level:0.0} deg, aiming up {up:0.0} deg");
            Assert.That(Mathf.Abs(up - level), Is.GreaterThan(20f), "the body bends the SMG toward the aim");
            look.SetRotation(look.Yaw, 0f);
            yield return Frames(30);

            Transform scarf = BodyBone("Scarf_F3");
            Transform tail = BodyBone("Tail02");
            Transform ear = BodyBone("Ear_L2");
            Quaternion scarfRest = scarf.localRotation, tailRest = tail.localRotation;
            commands.Next.Move = Vector2.up;
            float scarfSwing = 0f, tailSwing = 0f;
            for (int i = 0; i < 60; i++)
            {
                yield return null;
                scarfSwing = Mathf.Max(scarfSwing, Quaternion.Angle(scarf.localRotation, scarfRest));
                tailSwing = Mathf.Max(tailSwing, Quaternion.Angle(tail.localRotation, tailRest));
            }

            commands.Next.Move = Vector2.zero;
            Debug.Log($"[Rabbit] running: scarf swings {scarfSwing:0} deg, tail {tailSwing:0} deg");
            Assert.That(scarfSwing, Is.GreaterThan(4f), "the scarf trails while running");
            Assert.That(tailSwing, Is.GreaterThan(2f), "the tail moves while running");
            yield return Frames(60);

            Quaternion earBefore = ear.localRotation;
            player.GetComponent<Health>().ApplyDamage(new DamageInfo { Amount = player.GetComponent<Health>().Max * 0.3f, SourceTeam = TeamId.Enemies });
            yield return Frames(8);
            Assert.That(Quaternion.Angle(ear.localRotation, earBefore), Is.GreaterThan(3f), "a hit flattens the ears");
        }

        static float GlowBoost(Renderer r)
        {
            var block = new MaterialPropertyBlock();
            r.GetPropertyBlock(block);
            return block.GetFloat("_GlowBoost");
        }

        [UnityTest]
        public IEnumerator Glow_FlaresOnADash_AndSparksLeaveTheGloves()
        {
            yield return Spawn("LightningRabbit");
            var fx = player.GetComponentInChildren<ElectricBodyFx>(true);
            Assert.That(fx, Is.Not.Null, "the kit's lightning came along");
            Assert.That(fx.Driver, Is.SameAs(player.GetComponent<CharacterAnimatorDriver>()), "it follows the player's driver");
            Renderer viewGlow = fx.ViewGlow[0];
            yield return Frames(60);
            Assert.That(fx.ViewDrawn(), Is.True, "the first-person arms' glow is drawn");
            Assert.That(fx.BodyDrawn(), Is.False, "the local player's own body is only a shadow");
            Assert.That(fx.BodyGlow.All(r => !r.enabled), Is.True, "its glow copies are off (not skinned for nothing)");
            float rest = GlowBoost(viewGlow);
            int sparksBefore = fx.SparksPlayed;

            commands.Next.Move = Vector2.up;
            commands.Next.DashPressed = true;
            float peak = 0f;
            for (int i = 0; i < 12; i++)
            {
                yield return null;
                peak = Mathf.Max(peak, GlowBoost(viewGlow));
            }

            commands.Next.Move = Vector2.zero;
            yield return Frames(90);
            float after = GlowBoost(viewGlow);
            Debug.Log($"[Rabbit] glow boost: rest {rest:0.00}, dash peak {peak:0.00}, 1.5 s later {after:0.00}; " +
                      $"sparks {fx.SparksPlayed - sparksBefore}, afterimages {fx.GhostsShown}");
            Assert.That(rest, Is.LessThan(0.3f), "a quiet breath at rest");
            Assert.That(peak, Is.GreaterThan(1.5f), "the marks flare on a dash");
            Assert.That(after, Is.LessThan(0.6f), "and settle again");
            Assert.That(fx.SparksPlayed, Is.GreaterThan(sparksBefore), "sparks jump from the first-person gloves");
            Assert.That(fx.GhostsShown, Is.EqualTo(0), "no afterimages of the local player's own hidden body");
        }

        [UnityTest]
        public IEnumerator Glow_BuildsWithTheThunderhopCharge_AndFlaresOnItsDischarge()
        {
            yield return Spawn("LightningRabbit");
            var fx = player.GetComponentInChildren<ElectricBodyFx>(true);
            var weapon = player.GetComponent<HitscanWeapon>();
            Assert.That(fx.Weapon, Is.SameAs(weapon), "the lightning follows the player's gun");
            Assert.That(weapon.ChargeBurst != null && weapon.ChargeBurst.Enabled, Is.True, "the rabbit starts with a charging gun (VOLT-9)");

            // A wide target straight ahead: every bullet lands, it never dies.
            var target = new GameObject("ChargeTarget");
            spawned.Add(target);
            target.transform.position = player.transform.position + player.transform.forward * 7f;
            var health = target.AddComponent<Health>();
            health.Team = TeamId.Enemies;
            health.SetMaxHealth(100000f, true);
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.SetParent(target.transform, false);
            wall.transform.localPosition = new Vector3(0f, 1.5f, 0f);
            wall.transform.localScale = new Vector3(5f, 3f, 0.5f);
            wall.AddComponent<HitZone>().Configure(health, HitZoneType.Body, 1f);
            Physics.SyncTransforms();
            yield return Frames(20);

            int bursts = 0;
            weapon.ChargeBurstTriggered += _ => bursts++;
            Renderer viewGlow = fx.ViewGlow[0];
            float rest = GlowBoost(viewGlow), charged = 0f, peak = 0f;
            int sparksBefore = fx.SparksPlayed, sparksAtBurst = 0;
            commands.Next.FireHeld = true;
            for (int i = 0; i < 240 && bursts == 0; i++)
            {
                sparksAtBurst = fx.SparksPlayed; // before the frame that may discharge
                charged = Mathf.Max(charged, GlowBoost(viewGlow));
                yield return null;
            }

            commands.Next.FireHeld = false;
            Assert.That(bursts, Is.EqualTo(1), "12 hits in a row discharge");
            for (int i = 0; i < 10; i++)
            {
                yield return null;
                peak = Mathf.Max(peak, GlowBoost(viewGlow));
            }

            yield return Frames(90);
            float after = GlowBoost(viewGlow);
            Debug.Log($"[Rabbit] Thunderhop charge glow: rest {rest:0.00}, charging {charged:0.00}, discharge {peak:0.00}, 1.5 s later {after:0.00}; " +
                      $"sparks while charging {sparksAtBurst - sparksBefore}, at the discharge {fx.SparksPlayed - sparksAtBurst}");
            Assert.That(fx.BurstsSeen, Is.EqualTo(1));
            Assert.That(charged, Is.GreaterThan(rest + ElectricPulse.ChargeGlow * 0.5f), "the marks brighten while the charge builds");
            Assert.That(peak, Is.GreaterThan(ElectricPulse.DashSpike), "the discharge is the biggest flare");
            Assert.That(after, Is.LessThan(0.6f), "and it settles");
            Assert.That(fx.SparksPlayed - sparksAtBurst, Is.GreaterThanOrEqualTo(3), "the gloves spark at the discharge");
        }

        [UnityTest]
        public IEnumerator Afterimages_FollowADash_WhenTheBodyIsSeen()
        {
            yield return Spawn("LightningRabbit");
            var fx = player.GetComponentInChildren<ElectricBodyFx>(true);
            // As another player would see it: the body drawn, a camera in front of it.
            foreach (Renderer r in body.GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }

            var watcher = new GameObject("Watcher").AddComponent<Camera>();
            spawned.Add(watcher.gameObject);
            watcher.transform.SetPositionAndRotation(player.transform.position + player.transform.forward * 4f + Vector3.up * 1.2f,
                Quaternion.LookRotation(-player.transform.forward));
            yield return Frames(10);
            Assert.That(fx.BodyDrawn(), Is.True);
            Assert.That(fx.BodyGlow.All(r => r.enabled), Is.True, "the body's glowing marks are on");

            commands.Next.Move = Vector2.up;
            commands.Next.DashPressed = true;
            int most = 0;
            for (int i = 0; i < 20; i++)
            {
                yield return null;
                most = Mathf.Max(most, fx.ActiveGhosts);
            }

            commands.Next.Move = Vector2.zero;
            yield return Frames(40);
            Debug.Log($"[Rabbit] afterimages: {fx.GhostsShown} shown, at most {most} at once, {fx.ActiveGhosts} left after 0.7 s");
            int expected = PerformanceSettings.Vfx >= VfxQuality.High ? 3 : PerformanceSettings.Vfx >= VfxQuality.Medium ? 2 : 0;
            Assert.That(fx.GhostsShown, Is.EqualTo(expected), "afterimages for the quality setting");
            Assert.That(fx.ActiveGhosts, Is.EqualTo(0), "they fade out");
        }
    }
}
#endif
