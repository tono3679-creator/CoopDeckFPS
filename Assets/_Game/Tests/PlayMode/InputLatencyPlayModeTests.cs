using System.Collections;
using System.Collections.Generic;
using CoopDeck.Combat;
using CoopDeck.Core;
using CoopDeck.Input;
using CoopDeck.Player;
using CoopDeck.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace CoopDeck.Tests
{
    /// <summary>
    /// Input latency of the full local chain: device event → PlayerInputReader → CharacterCommands
    /// → FpsCharacter → HitscanWeapon → Health. A virtual gamepad is added for the test and removed
    /// afterwards; the real devices are not touched.
    /// </summary>
    public class InputLatencyPlayModeTests
    {
        const float Dt = 1f / 60f;
        readonly List<Object> spawned = new List<Object>();
        Gamepad pad;
        InputSettings originalSettings;
        InputSettings testSettings;

        [SetUp]
        public void SetUp()
        {
            Time.captureDeltaTime = Dt;

            // Keep the virtual pad working even if the Game view is not focused.
            originalSettings = InputSystem.settings;
            testSettings = Object.Instantiate(originalSettings);
            testSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings = testSettings;
            pad = InputSystem.AddDevice<Gamepad>("CoopDeckLatencyTestPad");
        }

        [TearDown]
        public void TearDown()
        {
            Time.captureDeltaTime = 0f;
            foreach (Object o in spawned)
            {
                if (o != null)
                {
                    Object.Destroy(o);
                }
            }

            spawned.Clear();
            if (pad != null && pad.added)
            {
                InputSystem.RemoveDevice(pad);
            }

            InputSystem.settings = originalSettings;
            if (testSettings != null)
            {
                Object.Destroy(testSettings);
            }
        }

        T Track<T>(T o) where T : Object
        {
            spawned.Add(o);
            return o;
        }

        [UnityTest]
        public IEnumerator GamepadTrigger_ShootsAndHits_InTheFrameTheInputArrives()
        {
            GameObject floor = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            floor.transform.position = new Vector3(0f, -0.5f, 0f);
            floor.transform.localScale = new Vector3(40f, 1f, 40f);

            var target = Track(new GameObject("LatencyTarget"));
            target.transform.position = new Vector3(0f, 1f, 12f);
            var targetHealth = target.AddComponent<Health>();
            targetHealth.Team = TeamId.Enemies;
            GameObject body = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            body.transform.SetParent(target.transform, false);
            body.transform.localScale = new Vector3(2f, 3f, 1f);
            body.AddComponent<HitZone>().Configure(targetHealth, HitZoneType.Body, 1f);
            Physics.SyncTransforms();

            var root = Track(new GameObject("LatencyPlayer"));
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
            root.AddComponent<FpsLook>().Configure(root.transform, pivot.transform, null);

            HitscanWeapon weapon = root.AddComponent<HitscanWeapon>();
            var definition = Track(ScriptableObject.CreateInstance<WeaponDefinition>());
            definition.damage = 10f;
            definition.baseSpread = 0f;
            definition.spreadPerShot = 0f;
            definition.movingSpread = 0f;
            definition.airborneSpread = 0f;
            weapon.SetDefinition(definition);

            var reader = root.AddComponent<PlayerInputReader>();
            var character = root.AddComponent<FpsCharacter>();
            character.CommandSource = reader;

            for (int i = 0; i < 20; i++)
            {
                yield return null; // settle on the floor
            }

            Assert.That(reader.IsPaused, Is.False);
            int firedFrame = -1;
            int hitFrame = -1;
            weapon.Fired += _ => { if (firedFrame < 0) firedFrame = Time.frameCount; };
            targetHealth.Damaged += (info, result) => { if (hitFrame < 0) hitFrame = Time.frameCount; };

            // The event is queued now and processed at the start of the next frame, before Update.
            InputSystem.QueueStateEvent(pad, new GamepadState { rightTrigger = 1f });
            yield return null;
            int inputFrame = Time.frameCount;
            Assert.That(pad.rightTrigger.isPressed, Is.True, "the virtual trigger press arrived");

            Assert.That(firedFrame, Is.EqualTo(inputFrame), "shot fired in the same frame the press was read (0 frames added)");
            Assert.That(hitFrame, Is.EqualTo(inputFrame), "hitscan damage lands in that frame too");

            InputSystem.QueueStateEvent(pad, new GamepadState());
            yield return null;
            Assert.That(pad.rightTrigger.isPressed, Is.False);
        }
    }
}
