using System;
using CoopDeck.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CoopDeck.UI
{
    public enum InputScheme
    {
        KeyboardMouse = 0,
        Gamepad = 1,
    }

    /// <summary>Actions whose key / button the HUD shows.</summary>
    public enum HudAction
    {
        Skill1,
        Skill2,
        Skill3,
        SelectCard,
        UseItem,
        CycleItem,
        Reload,
        Dash,
        Guide,
        Tactic,
        Focus,
        NpcDeck,
        Pause,
    }

    /// <summary>
    /// Which device the player is using right now, and the key / button label for HUD actions on
    /// it (Q / LB …). The HUD only reads devices to pick labels; gameplay input still flows
    /// through <c>ICharacterCommandSource</c>. Labels match <c>GameInputActions</c>' bindings.
    /// </summary>
    public static class InputGlyphs
    {
        static readonly string[] Keyboard =
        {
            "Q", "E", "F", "1/2/3", "G", "T", "R", "Ctrl", "F1", "V", "中クリック/X", "Tab", "Esc",
        };

        static readonly string[] Pad =
        {
            "LB", "RB", "Y", "十字←→", "十字↑", "十字↓", "X", "B", "Start", "R3", "LT", "ポーズ中 Y", "Start",
        };

        static int lastFrame = -1;
        static bool initialised;

        public static InputScheme Active { get; private set; }

        public static event Action<InputScheme> Changed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            lastFrame = -1;
            initialised = false;
            Active = InputScheme.KeyboardMouse;
            Changed = null;
        }

        public static string Label(HudAction action) => Label(action, Active);

        public static string Label(HudAction action, InputScheme scheme)
        {
            int i = (int)action;
            return scheme == InputScheme.Gamepad ? Pad[i] : Keyboard[i];
        }

        public static string SkillLabel(int slot, InputScheme scheme)
        {
            return slot >= 0 && slot < 3 ? Label(HudAction.Skill1 + slot, scheme) : (slot + 1).ToString();
        }

        /// <summary>Forces a scheme (tests, previews).</summary>
        public static void SetActive(InputScheme scheme)
        {
            initialised = true;
            if (Active == scheme)
            {
                return;
            }

            Active = scheme;
            Changed?.Invoke(scheme);
        }

        /// <summary>Once per frame: switch to the device that was just used. Handhelds start on the pad.</summary>
        public static void Poll()
        {
            if (lastFrame == Time.frameCount)
            {
                return;
            }

            lastFrame = Time.frameCount;
            if (!initialised)
            {
                initialised = true;
                Active = Gamepad.current != null && PerformanceSettings.Tier == PerformanceTier.Handheld ? InputScheme.Gamepad : InputScheme.KeyboardMouse;
            }

            if (Active == InputScheme.KeyboardMouse && GamepadUsed())
            {
                SetActive(InputScheme.Gamepad);
            }
            else if (Active == InputScheme.Gamepad && KeyboardMouseUsed())
            {
                SetActive(InputScheme.KeyboardMouse);
            }
        }

        static bool GamepadUsed()
        {
            Gamepad pad = Gamepad.current;
            if (pad == null || !pad.wasUpdatedThisFrame)
            {
                return false;
            }

            return pad.leftStick.ReadValue().sqrMagnitude > 0.25f || pad.rightStick.ReadValue().sqrMagnitude > 0.25f ||
                   pad.leftTrigger.ReadValue() > 0.4f || pad.rightTrigger.ReadValue() > 0.4f ||
                   pad.buttonSouth.isPressed || pad.buttonEast.isPressed || pad.buttonWest.isPressed || pad.buttonNorth.isPressed ||
                   pad.leftShoulder.isPressed || pad.rightShoulder.isPressed || pad.startButton.isPressed || pad.selectButton.isPressed ||
                   pad.dpad.ReadValue().sqrMagnitude > 0.25f || pad.leftStickButton.isPressed || pad.rightStickButton.isPressed;
        }

        static bool KeyboardMouseUsed()
        {
            Keyboard keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null && keyboard.anyKey.wasPressedThisFrame)
            {
                return true;
            }

            Mouse mouse = Mouse.current;
            return mouse != null && (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame || mouse.delta.ReadValue().sqrMagnitude > 16f);
        }
    }
}
