using UnityEngine;
using UnityEngine.InputSystem;

namespace CoopDeck.Input
{
    /// <summary>
    /// Default control layout. The editor bootstrap writes this to
    /// Assets/_Game/Settings/Input/GameControls.inputactions once; after that the asset is the
    /// source of truth and can be edited in the Input Actions editor.
    /// </summary>
    public static class GameInputActions
    {
        public const string KeyboardMouseScheme = "Keyboard&Mouse";
        public const string GamepadScheme = "Gamepad";

        public const string PlayerMap = "Player";

        public const string Move = "Move";
        public const string Look = "Look";
        public const string LookStick = "LookStick";
        public const string Jump = "Jump";
        public const string Sprint = "Sprint";
        public const string SprintToggle = "SprintToggle";
        public const string Dash = "Dash";
        public const string Fire = "Fire";
        public const string Reload = "Reload";
        public const string Skill1 = "Skill1";
        public const string Skill2 = "Skill2";
        public const string Skill3 = "Skill3";
        public const string UseItem = "UseItem";
        public const string CycleItem = "CycleItem";
        public const string SelectCard1 = "SelectCard1";
        public const string SelectCard2 = "SelectCard2";
        public const string SelectCard3 = "SelectCard3";
        public const string SelectCardPrev = "SelectCardPrev";
        public const string SelectCardNext = "SelectCardNext";
        public const string Pause = "Pause";

        public static readonly string[] SkillActions = { Skill1, Skill2, Skill3 };
        public static readonly string[] SelectCardActions = { SelectCard1, SelectCard2, SelectCard3 };

        public static readonly string[] AllPlayerActions =
        {
            Move, Look, LookStick, Jump, Sprint, SprintToggle, Dash, Fire, Reload,
            Skill1, Skill2, Skill3, UseItem, CycleItem,
            SelectCard1, SelectCard2, SelectCard3, SelectCardPrev, SelectCardNext, Pause,
        };

        public static InputActionAsset CreateDefaultAsset()
        {
            var asset = ScriptableObject.CreateInstance<InputActionAsset>();
            asset.name = "GameControls";

            asset.AddControlScheme(KeyboardMouseScheme)
                .WithRequiredDevice("<Keyboard>")
                .WithRequiredDevice("<Mouse>");
            asset.AddControlScheme(GamepadScheme)
                .WithRequiredDevice("<Gamepad>");

            InputActionMap map = asset.AddActionMap(PlayerMap);

            InputAction move = map.AddAction(Move, InputActionType.Value, expectedControlLayout: "Vector2");
            move.AddCompositeBinding("2DVector")
                .With("up", "<Keyboard>/w", KeyboardMouseScheme)
                .With("down", "<Keyboard>/s", KeyboardMouseScheme)
                .With("left", "<Keyboard>/a", KeyboardMouseScheme)
                .With("right", "<Keyboard>/d", KeyboardMouseScheme);
            move.AddBinding("<Gamepad>/leftStick", groups: GamepadScheme);

            InputAction look = map.AddAction(Look, InputActionType.Value, expectedControlLayout: "Vector2");
            look.AddBinding("<Mouse>/delta", groups: KeyboardMouseScheme);

            InputAction lookStick = map.AddAction(LookStick, InputActionType.Value, expectedControlLayout: "Vector2");
            lookStick.AddBinding("<Gamepad>/rightStick", groups: GamepadScheme);

            AddButton(map, Jump, "<Keyboard>/space", "<Gamepad>/buttonSouth");
            AddButton(map, Sprint, "<Keyboard>/leftShift", null);
            AddButton(map, SprintToggle, null, "<Gamepad>/leftStickPress");
            AddButton(map, Dash, "<Keyboard>/leftCtrl", "<Gamepad>/buttonEast");
            AddButton(map, Fire, "<Mouse>/leftButton", "<Gamepad>/rightTrigger");
            AddButton(map, Reload, "<Keyboard>/r", "<Gamepad>/buttonWest");
            AddButton(map, Skill1, "<Keyboard>/q", "<Gamepad>/leftShoulder");
            AddButton(map, Skill2, "<Keyboard>/e", "<Gamepad>/rightShoulder");
            AddButton(map, Skill3, "<Keyboard>/f", "<Gamepad>/buttonNorth");
            AddButton(map, UseItem, "<Keyboard>/g", "<Gamepad>/dpad/up");
            AddButton(map, CycleItem, "<Keyboard>/t", "<Gamepad>/dpad/down");
            AddButton(map, SelectCard1, "<Keyboard>/1", null);
            AddButton(map, SelectCard2, "<Keyboard>/2", null);
            AddButton(map, SelectCard3, "<Keyboard>/3", null);
            AddButton(map, SelectCardPrev, null, "<Gamepad>/dpad/left");
            AddButton(map, SelectCardNext, null, "<Gamepad>/dpad/right");
            AddButton(map, Pause, "<Keyboard>/escape", "<Gamepad>/start");

            return asset;
        }

        static void AddButton(InputActionMap map, string name, string keyboardMousePath, string gamepadPath)
        {
            InputAction action = map.AddAction(name, InputActionType.Button);
            if (!string.IsNullOrEmpty(keyboardMousePath))
            {
                action.AddBinding(keyboardMousePath, groups: KeyboardMouseScheme);
            }

            if (!string.IsNullOrEmpty(gamepadPath))
            {
                action.AddBinding(gamepadPath, groups: GamepadScheme);
            }
        }
    }
}
