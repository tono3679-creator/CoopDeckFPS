using System.Linq;
using CoopDeck.Core;
using CoopDeck.Input;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CoopDeck.Tests
{
    public class InputAndPlatformTests
    {
        [Test]
        public void DefaultControls_ContainEveryPlayerAction()
        {
            InputActionAsset asset = GameInputActions.CreateDefaultAsset();
            try
            {
                InputActionMap map = asset.FindActionMap(GameInputActions.PlayerMap, true);
                foreach (string action in GameInputActions.AllPlayerActions)
                {
                    Assert.That(map.FindAction(action), Is.Not.Null, action);
                }

                Assert.That(map.actions.Count, Is.EqualTo(GameInputActions.AllPlayerActions.Length));
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void CoreActions_HaveKeyboardMouseAndGamepadBindings()
        {
            InputActionAsset asset = GameInputActions.CreateDefaultAsset();
            try
            {
                string[] both = { GameInputActions.Move, GameInputActions.Jump, GameInputActions.Dash, GameInputActions.Fire,
                    GameInputActions.Reload, GameInputActions.Skill1, GameInputActions.Skill2, GameInputActions.Skill3,
                    GameInputActions.UseItem, GameInputActions.Pause };
                foreach (string name in both)
                {
                    InputAction action = asset.FindAction($"{GameInputActions.PlayerMap}/{name}", true);
                    Assert.That(action.bindings.Any(b => b.groups != null && b.groups.Contains(GameInputActions.KeyboardMouseScheme)), Is.True, name + " keyboard/mouse");
                    Assert.That(action.bindings.Any(b => b.groups != null && b.groups.Contains(GameInputActions.GamepadScheme)), Is.True, name + " gamepad");
                }

                Assert.That(asset.controlSchemes.Count, Is.EqualTo(2));
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        [TestCase(GameInputActions.Skill1, "<Keyboard>/q", "<Gamepad>/leftShoulder")]
        [TestCase(GameInputActions.Skill2, "<Keyboard>/e", "<Gamepad>/rightShoulder")]
        [TestCase(GameInputActions.Skill3, "<Keyboard>/f", "<Gamepad>/buttonNorth")]
        [TestCase(GameInputActions.UseItem, "<Keyboard>/g", "<Gamepad>/dpad/up")]
        [TestCase(GameInputActions.CycleItem, "<Keyboard>/t", "<Gamepad>/dpad/down")]
        [TestCase(GameInputActions.SelectCard1, "<Keyboard>/1", null)]
        [TestCase(GameInputActions.SelectCard2, "<Keyboard>/2", null)]
        [TestCase(GameInputActions.SelectCard3, "<Keyboard>/3", null)]
        [TestCase(GameInputActions.SelectCardPrev, null, "<Gamepad>/dpad/left")]
        [TestCase(GameInputActions.SelectCardNext, null, "<Gamepad>/dpad/right")]
        [TestCase(GameInputActions.Fire, "<Mouse>/leftButton", "<Gamepad>/rightTrigger")]
        public void DeckControls_AreBoundAsDocumented(string actionName, string keyboardPath, string gamepadPath)
        {
            InputActionAsset asset = GameInputActions.CreateDefaultAsset();
            try
            {
                InputAction action = asset.FindAction($"{GameInputActions.PlayerMap}/{actionName}", true);
                string[] paths = action.bindings.Select(b => b.path).ToArray();
                if (keyboardPath != null) Assert.That(paths, Does.Contain(keyboardPath));
                if (gamepadPath != null) Assert.That(paths, Does.Contain(gamepadPath));
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void CharacterCommands_CardSelect_DefaultsToNone()
        {
            var commands = new CharacterCommands();
            Assert.That(commands.TryGetCardSelect(out _), Is.False);
            commands.RequestCardSelect(0);
            Assert.That(commands.TryGetCardSelect(out int slot), Is.True);
            Assert.That(slot, Is.EqualTo(0));
        }

        [Test]
        public void DefaultControls_RoundTripThroughJson()
        {
            InputActionAsset asset = GameInputActions.CreateDefaultAsset();
            InputActionAsset copy = null;
            try
            {
                copy = InputActionAsset.FromJson(asset.ToJson());
                Assert.That(copy.FindAction($"{GameInputActions.PlayerMap}/{GameInputActions.Move}"), Is.Not.Null);
                Assert.That(copy.FindAction($"{GameInputActions.PlayerMap}/{GameInputActions.Move}").bindings.Count,
                    Is.EqualTo(asset.FindAction($"{GameInputActions.PlayerMap}/{GameInputActions.Move}").bindings.Count));
            }
            finally
            {
                Object.DestroyImmediate(asset);
                if (copy != null)
                {
                    Object.DestroyImmediate(copy);
                }
            }
        }

        [TestCase("ROG Ally X RC72LA_RC72LA (ASUSTeK COMPUTER INC.)", "AMD Radeon Graphics", PerformanceTier.Handheld)]
        [TestCase("RC71L", "AMD Radeon(TM) Graphics", PerformanceTier.Handheld)]
        [TestCase("System Product Name", "AMD Radeon(TM) Graphics", PerformanceTier.Handheld)]
        [TestCase("System Product Name", "NVIDIA GeForce RTX 4070", PerformanceTier.High)]
        [TestCase("", "", PerformanceTier.High)]
        public void TierDetection(string deviceModel, string gpu, PerformanceTier expected)
        {
            Assert.That(PerformanceBootstrap.DetectTier(deviceModel, gpu, out _), Is.EqualTo(expected));
        }

        [Test]
        public void TierParsing()
        {
            Assert.That(PerformanceBootstrap.TryParseTier("Handheld", out PerformanceTier a) && a == PerformanceTier.Handheld);
            Assert.That(PerformanceBootstrap.TryParseTier("high", out PerformanceTier b) && b == PerformanceTier.High);
            Assert.That(PerformanceBootstrap.TryParseTier("ultra", out _), Is.False);
        }

        [Test]
        public void CharacterCommands_SkillMask()
        {
            var commands = new CharacterCommands();
            commands.SetSkillPressed(0);
            commands.SetSkillPressed(2);
            Assert.That(commands.IsSkillPressed(0), Is.True);
            Assert.That(commands.IsSkillPressed(1), Is.False);
            Assert.That(commands.IsSkillPressed(2), Is.True);
        }
    }
}
