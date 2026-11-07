using System.Collections;
using System.Collections.Generic;
using CoopDeck.Combat;
using CoopDeck.Core;
using CoopDeck.Player;
using CoopDeck.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CoopDeck.Tests
{
    /// <summary>
    /// Physics-level checks of the FPS core, driven through CharacterCommands exactly like an
    /// NPC or network proxy would drive a character. No input devices are involved.
    /// </summary>
    public class FpsPlayModeTests
    {
        const float Dt = 1f / 60f;
        readonly List<GameObject> spawned = new List<GameObject>();

        sealed class ScriptedCommands : ICharacterCommandSource
        {
            public CharacterCommands Next;

            public CharacterCommands GetCommands(float deltaTime)
            {
                CharacterCommands c = Next;
                // Pressed flags last one step, held values persist.
                Next.JumpPressed = false;
                Next.DashPressed = false;
                Next.FirePressed = false;
                Next.ReloadPressed = false;
                Next.SkillPressedMask = 0;
                return c;
            }
        }

        sealed class FixedAim : IAimProvider
        {
            public Ray Ray;
            public Vector2 TotalRecoil;
            public Ray GetAimRay() => Ray;
            public void AddRecoil(Vector2 kickDegrees) => TotalRecoil += kickDegrees;
        }

        [SetUp]
        public void SetUp()
        {
            // Batch-mode frames are much shorter than real ones; pin delta time so frame-driven
            // tests move the same distance everywhere.
            Time.captureDeltaTime = Dt;
        }

        [TearDown]
        public void TearDown()
        {
            Time.captureDeltaTime = 0f;
            foreach (GameObject go in spawned)
            {
                if (go != null)
                {
                    Object.Destroy(go);
                }
            }

            spawned.Clear();
        }

        GameObject Spawn(GameObject go)
        {
            spawned.Add(go);
            return go;
        }

        GameObject Box(Vector3 position, Vector3 size, Vector3 euler = default)
        {
            GameObject box = Spawn(GameObject.CreatePrimitive(PrimitiveType.Cube));
            box.transform.SetPositionAndRotation(position, Quaternion.Euler(euler));
            box.transform.localScale = size;
            Physics.SyncTransforms();
            return box;
        }

        FpsMotor CreateMotor(Vector3 position)
        {
            var go = Spawn(new GameObject("TestMotor"));
            go.transform.position = position;
            var controller = go.AddComponent<CharacterController>();
            controller.height = 1.8f;
            controller.radius = 0.38f;
            controller.center = new Vector3(0f, 0.9f, 0f);
            controller.stepOffset = 0.4f;
            controller.slopeLimit = 50f;
            controller.skinWidth = 0.06f;
            controller.minMoveDistance = 0f;
            return go.AddComponent<FpsMotor>();
        }

        static IEnumerator Simulate(FpsMotor motor, Vector2 move, int steps, bool sprint = false)
        {
            for (int i = 0; i < steps; i++)
            {
                motor.Simulate(move, sprint, false, false, Dt);
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator Motor_SettlesOnGround_AndWalksAtWalkSpeed()
        {
            Box(new Vector3(0f, -0.5f, 0f), new Vector3(60f, 1f, 60f));
            FpsMotor motor = CreateMotor(new Vector3(0f, 0.2f, 0f));
            yield return Simulate(motor, Vector2.zero, 30);
            Assert.That(motor.IsGrounded, Is.True);

            Vector3 start = motor.transform.position;
            yield return Simulate(motor, Vector2.up, 60);
            float distance = (motor.transform.position - start).z;

            // 1 s at 6.5 m/s, minus ~0.05 s of acceleration.
            Assert.That(distance, Is.InRange(5.9f, 6.6f));
            Assert.That(motor.HorizontalVelocity.magnitude, Is.EqualTo(motor.Profile.walkSpeed).Within(0.05f));
        }

        [UnityTest]
        public IEnumerator Motor_SprintIsFaster_AndStopsQuickly()
        {
            Box(new Vector3(0f, -0.5f, 0f), new Vector3(80f, 1f, 80f));
            FpsMotor motor = CreateMotor(new Vector3(0f, 0.2f, -20f));
            yield return Simulate(motor, Vector2.zero, 20);
            yield return Simulate(motor, Vector2.up, 40, sprint: true);
            Assert.That(motor.IsSprinting, Is.True);
            Assert.That(motor.HorizontalVelocity.magnitude, Is.EqualTo(motor.Profile.sprintSpeed).Within(0.1f));

            yield return Simulate(motor, Vector2.zero, 15);
            Assert.That(motor.HorizontalVelocity.magnitude, Is.LessThan(0.05f), "should stop within 0.25 s");
        }

        [UnityTest]
        public IEnumerator Motor_JumpReachesConfiguredHeight_AndLands()
        {
            Box(new Vector3(0f, -0.5f, 0f), new Vector3(20f, 1f, 20f));
            FpsMotor motor = CreateMotor(new Vector3(0f, 0.2f, 0f));
            yield return Simulate(motor, Vector2.zero, 30);
            float groundY = motor.transform.position.y;

            bool landed = false;
            motor.Landed += _ => landed = true;
            motor.Simulate(Vector2.zero, false, true, false, Dt);
            yield return null;

            float peak = groundY;
            for (int i = 0; i < 90; i++)
            {
                motor.Simulate(Vector2.zero, false, false, false, Dt);
                peak = Mathf.Max(peak, motor.transform.position.y);
                yield return null;
            }

            Assert.That(peak - groundY, Is.EqualTo(motor.Profile.jumpHeight).Within(0.12f));
            Assert.That(landed, Is.True);
            Assert.That(motor.IsGrounded, Is.True);
        }

        [UnityTest]
        public IEnumerator Motor_ClimbsStepsBelowStepOffset()
        {
            Box(new Vector3(0f, -0.5f, 0f), new Vector3(20f, 1f, 30f));
            for (int i = 0; i < 6; i++)
            {
                float h = 0.25f * (i + 1);
                Box(new Vector3(0f, h * 0.5f, 2f + i * 0.5f), new Vector3(3f, h, 0.5f));
            }

            // Landing at the top so the walk ends up on the 1.5 m level.
            Box(new Vector3(0f, 0.75f, 9.75f), new Vector3(3f, 1.5f, 10f));

            FpsMotor motor = CreateMotor(new Vector3(0f, 0.2f, -2f));
            yield return Simulate(motor, Vector2.zero, 20);
            yield return Simulate(motor, Vector2.up, 120);

            Assert.That(motor.transform.position.y, Is.GreaterThan(1.3f), "should have climbed to the top step (1.5 m)");
            Assert.That(motor.transform.position.z, Is.GreaterThan(4.5f));
        }

        [UnityTest]
        public IEnumerator Motor_SlidesAlongWall_InsteadOfSticking()
        {
            Box(new Vector3(0f, -0.5f, 0f), new Vector3(40f, 1f, 40f));
            Box(new Vector3(0f, 1.5f, 2f), new Vector3(30f, 3f, 0.5f));
            FpsMotor motor = CreateMotor(new Vector3(0f, 0.2f, 0f));
            yield return Simulate(motor, Vector2.zero, 20);

            // Push diagonally into the wall (forward-right).
            Vector3 start = motor.transform.position;
            yield return Simulate(motor, new Vector2(0.7071f, 0.7071f), 90);
            Vector3 moved = motor.transform.position - start;

            Assert.That(moved.z, Is.LessThan(1.6f), "wall blocks forward motion");
            Assert.That(moved.x, Is.GreaterThan(3f), "keeps sliding sideways along the wall");
        }

        [UnityTest]
        public IEnumerator Motor_DashMovesFast_AndConsumesCharge()
        {
            Box(new Vector3(0f, -0.5f, 0f), new Vector3(60f, 1f, 60f));
            FpsMotor motor = CreateMotor(new Vector3(0f, 0.2f, 0f));
            yield return Simulate(motor, Vector2.zero, 20);
            float chargesBefore = motor.DashCharge;
            Vector3 start = motor.transform.position;

            motor.Simulate(Vector2.right, false, false, true, Dt);
            yield return null;
            yield return Simulate(motor, Vector2.right, 9);

            Assert.That(motor.DashCharge, Is.LessThan(chargesBefore - 0.8f));
            Assert.That((motor.transform.position - start).x, Is.GreaterThan(2f), "dash covers ground fast");
        }

        GameObject CreateTarget(Vector3 position, out Health health)
        {
            var target = Spawn(new GameObject("Target"));
            target.transform.position = position;
            health = target.AddComponent<Health>();
            health.Team = TeamId.Enemies;

            var body = Spawn(GameObject.CreatePrimitive(PrimitiveType.Cube));
            body.transform.SetParent(target.transform, false);
            body.transform.localScale = new Vector3(1f, 2f, 1f);
            body.AddComponent<HitZone>().Configure(health, HitZoneType.Body, 1f);

            var head = Spawn(GameObject.CreatePrimitive(PrimitiveType.Sphere));
            head.transform.SetParent(target.transform, false);
            head.transform.localPosition = new Vector3(0f, 1.4f, 0f);
            head.transform.localScale = Vector3.one * 0.6f;
            head.AddComponent<HitZone>().Configure(health, HitZoneType.Head, 1f);
            Physics.SyncTransforms();
            return target;
        }

        HitscanWeapon CreateWeapon(FixedAim aim, out WeaponDefinition definition)
        {
            definition = ScriptableObject.CreateInstance<WeaponDefinition>();
            definition.damage = 10f;
            definition.headshotMultiplier = 2f;
            definition.baseSpread = 0f;
            definition.spreadPerShot = 0f;
            definition.movingSpread = 0f;
            definition.recoilPitch = 0.5f;
            definition.recoilYaw = 0f;
            definition.falloffStart = 100f;
            definition.magazineSize = 3;
            definition.reloadTime = 0.2f;

            var go = Spawn(new GameObject("Weapon"));
            var weapon = go.AddComponent<HitscanWeapon>();
            weapon.SetDefinition(definition);
            weapon.Initialize(null, aim, TeamId.Players, Physics.DefaultRaycastLayers);
            return weapon;
        }

        [UnityTest]
        public IEnumerator Weapon_BodyAndHeadshotDamage_RecoilAndReload()
        {
            CreateTarget(new Vector3(0f, 1f, 10f), out Health health);
            yield return new WaitForFixedUpdate();

            var aim = new FixedAim { Ray = new Ray(new Vector3(0f, 1f, 0f), Vector3.forward) };
            HitscanWeapon weapon = CreateWeapon(aim, out WeaponDefinition definition);
            var shots = new List<ShotEvent>();
            weapon.Fired += shots.Add;

            weapon.SetTrigger(true, true);
            weapon.Tick(Dt);
            weapon.SetTrigger(false, false);
            weapon.Tick(Dt);

            Assert.That(shots.Count, Is.EqualTo(1));
            Assert.That(shots[0].HitDamageable, Is.True);
            Assert.That(health.Current, Is.EqualTo(health.Max - 10f).Within(1e-3f));
            Assert.That(aim.TotalRecoil.y, Is.GreaterThan(0f), "recoil kicks the view up");

            // Let the fire cooldown run out with the trigger released.
            for (int i = 0; i < 12; i++)
            {
                weapon.Tick(Dt);
            }

            // Aim at the head: exactly one round per trigger pull.
            aim.Ray = new Ray(new Vector3(0f, 2.4f, 0f), Vector3.forward);
            weapon.SetTrigger(true, true);
            weapon.Tick(Dt);
            Assert.That(shots.Count, Is.EqualTo(2));
            Assert.That(shots[1].Result.IsHeadshot, Is.True);
            Assert.That(health.Current, Is.EqualTo(health.Max - 30f).Within(1e-3f));

            // Holding the trigger for one fire interval fires the third round and empties the magazine.
            weapon.SetTrigger(true, false);
            weapon.Tick(0.1f);
            Assert.That(shots.Count, Is.EqualTo(3));
            Assert.That(weapon.State.Ammo, Is.EqualTo(0));
            weapon.SetTrigger(false, false);
            for (int i = 0; i < 30; i++)
            {
                weapon.Tick(Dt);
            }

            Assert.That(weapon.State.Ammo, Is.EqualTo(3), "auto reload after emptying");
            Object.Destroy(definition);
        }

        [UnityTest]
        public IEnumerator Weapon_MissesWhenAimedAway()
        {
            CreateTarget(new Vector3(0f, 1f, 10f), out Health health);
            yield return new WaitForFixedUpdate();

            var aim = new FixedAim { Ray = new Ray(new Vector3(0f, 1f, 0f), Vector3.back) };
            HitscanWeapon weapon = CreateWeapon(aim, out WeaponDefinition definition);
            weapon.SetTrigger(true, true);
            weapon.Tick(Dt);

            Assert.That(health.Current, Is.EqualTo(health.Max));
            Object.Destroy(definition);
        }

        [UnityTest]
        public IEnumerator Character_DrivenByCommandSource_MovesTurnsAndShoots()
        {
            Box(new Vector3(0f, -0.5f, 0f), new Vector3(60f, 1f, 60f));
            CreateTarget(new Vector3(0f, 1f, 12f), out Health targetHealth);

            var root = Spawn(new GameObject("TestCharacter"));
            root.layer = GameLayers.Player >= 0 ? GameLayers.Player : 0;
            root.transform.position = new Vector3(0f, 0.2f, 0f);
            var controller = root.AddComponent<CharacterController>();
            controller.height = 1.8f;
            controller.radius = 0.38f;
            controller.center = new Vector3(0f, 0.9f, 0f);
            root.AddComponent<FpsMotor>();
            root.AddComponent<Health>().Team = TeamId.Players;

            var pivot = new GameObject("Pivot");
            pivot.transform.SetParent(root.transform, false);
            pivot.transform.localPosition = new Vector3(0f, 1.0f, 0f);
            var look = root.AddComponent<FpsLook>();
            look.Configure(root.transform, pivot.transform, null);

            HitscanWeapon weapon = root.AddComponent<HitscanWeapon>();
            var definition = ScriptableObject.CreateInstance<WeaponDefinition>();
            definition.damage = 10f;
            definition.baseSpread = 0f;
            definition.spreadPerShot = 0f;
            definition.movingSpread = 0f;
            definition.airborneSpread = 0f;
            weapon.SetDefinition(definition);

            var character = root.AddComponent<FpsCharacter>();
            var commands = new ScriptedCommands();
            character.CommandSource = commands;
            yield return null; // Start() wires the weapon to the character.

            for (int i = 0; i < 20; i++)
            {
                yield return null;
            }

            // Walk forward for half a second.
            Vector3 start = root.transform.position;
            commands.Next.Move = Vector2.up;
            for (int i = 0; i < 30; i++)
            {
                yield return null;
            }

            commands.Next.Move = Vector2.zero;
            Assert.That(root.transform.position.z - start.z, Is.GreaterThan(1.5f));

            int skillPressed = -1;
            character.SkillSlotRequested += slot => skillPressed = slot;
            commands.Next.SetSkillPressed(1);
            yield return null;
            Assert.That(skillPressed, Is.EqualTo(1), "skill slot presses are forwarded for the deck system");

            // Shoot the target straight ahead.
            commands.Next.FireHeld = true;
            commands.Next.FirePressed = true;
            for (int i = 0; i < 10; i++)
            {
                yield return null;
            }

            commands.Next.FireHeld = false;
            Assert.That(targetHealth.Current, Is.LessThan(targetHealth.Max), "the character's weapon hit the target");

            // Turn 90 degrees right through LookDelta.
            float yawBefore = look.Yaw;
            commands.Next.LookDelta = new Vector2(90f, 0f);
            yield return null;
            commands.Next.LookDelta = Vector2.zero;
            Assert.That(Mathf.DeltaAngle(yawBefore, look.Yaw), Is.EqualTo(90f).Within(2f));

            Object.Destroy(definition);
        }
    }
}
