using CoopDeck.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CoopDeck.Input
{
    /// <summary>
    /// Turns the local player's devices into <see cref="CharacterCommands"/>. It never moves or
    /// shoots anything itself, so an NPC brain or a network proxy can replace it.
    /// Also owns pause / cursor lock for the local player.
    /// </summary>
    public class PlayerInputReader : MonoBehaviour, ICharacterCommandSource
    {
        [SerializeField] InputActionAsset actions;

        [Header("Mouse")]
        [Tooltip("Degrees of rotation per mouse count.")]
        [SerializeField, Min(0.001f)] float mouseSensitivity = 0.07f;
        [SerializeField] bool invertY;

        [Header("Gamepad")]
        [SerializeField, Min(1f)] float stickYawSpeed = 230f;
        [SerializeField, Min(1f)] float stickPitchSpeed = 160f;
        [Tooltip("Response curve exponent. 1 = linear, higher = finer aim near the center.")]
        [SerializeField, Range(1f, 3f)] float stickResponseExponent = 1.7f;
        [Tooltip("Seconds for stick turning to ramp from 40% to full speed.")]
        [SerializeField, Min(0f)] float stickRampTime = 0.15f;

        [Header("Cursor")]
        [SerializeField] bool lockCursorOnStart = true;

        InputActionMap map;
        InputAction move;
        InputAction look;
        InputAction lookStick;
        InputAction jump;
        InputAction sprint;
        InputAction sprintToggle;
        InputAction dash;
        InputAction fire;
        InputAction reload;
        InputAction useItem;
        InputAction cycleItem;
        InputAction pause;
        InputAction selectPrev;
        InputAction selectNext;
        readonly InputAction[] skills = new InputAction[CharacterCommands.SkillSlotCount];
        readonly InputAction[] selectCard = new InputAction[CharacterCommands.SkillSlotCount];

        InputActionAsset runtimeActions;
        bool sprintToggled;
        float stickRamp;
        bool suppressFireUntilRelease;

        public bool IsPaused { get; private set; }
        public InputActionAsset RuntimeActions => runtimeActions;

        public float MouseSensitivity
        {
            get => mouseSensitivity;
            set => mouseSensitivity = Mathf.Max(0.001f, value);
        }

        void Awake()
        {
            // Each reader gets its own copy so split-screen / multiple local players never share action state.
            runtimeActions = actions != null ? Instantiate(actions) : GameInputActions.CreateDefaultAsset();

            map = runtimeActions.FindActionMap(GameInputActions.PlayerMap, throwIfNotFound: true);
            move = map.FindAction(GameInputActions.Move, true);
            look = map.FindAction(GameInputActions.Look, true);
            lookStick = map.FindAction(GameInputActions.LookStick, true);
            jump = map.FindAction(GameInputActions.Jump, true);
            sprint = map.FindAction(GameInputActions.Sprint, true);
            sprintToggle = map.FindAction(GameInputActions.SprintToggle, true);
            dash = map.FindAction(GameInputActions.Dash, true);
            fire = map.FindAction(GameInputActions.Fire, true);
            reload = map.FindAction(GameInputActions.Reload, true);
            useItem = map.FindAction(GameInputActions.UseItem, true);
            cycleItem = map.FindAction(GameInputActions.CycleItem, true);
            pause = map.FindAction(GameInputActions.Pause, true);
            selectPrev = map.FindAction(GameInputActions.SelectCardPrev, true);
            selectNext = map.FindAction(GameInputActions.SelectCardNext, true);
            for (int i = 0; i < skills.Length; i++)
            {
                skills[i] = map.FindAction(GameInputActions.SkillActions[i], true);
                selectCard[i] = map.FindAction(GameInputActions.SelectCardActions[i], true);
            }
        }

        void OnEnable()
        {
            map?.Enable();
            SetPaused(!lockCursorOnStart);
        }

        void OnDisable()
        {
            map?.Disable();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        void OnDestroy()
        {
            if (runtimeActions != null)
            {
                Destroy(runtimeActions);
            }
        }

        void Update()
        {
            if (pause.WasPressedThisFrame())
            {
                SetPaused(!IsPaused);
            }
            else if (IsPaused && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                // Clicking back into the game resumes, but that click must not shoot.
                SetPaused(false);
                suppressFireUntilRelease = true;
            }

            if (suppressFireUntilRelease && !fire.IsPressed())
            {
                suppressFireUntilRelease = false;
            }
        }

        void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus && !IsPaused && enabled)
            {
                ApplyCursorState();
            }
        }

        public void SetPaused(bool paused)
        {
            IsPaused = paused;
            ApplyCursorState();
        }

        void ApplyCursorState()
        {
            Cursor.lockState = IsPaused ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = IsPaused;
        }

        public CharacterCommands GetCommands(float deltaTime)
        {
            var commands = new CharacterCommands();
            if (map == null || IsPaused)
            {
                stickRamp = 0f;
                return commands;
            }

            commands.Move = Vector2.ClampMagnitude(move.ReadValue<Vector2>(), 1f);
            commands.LookDelta = ReadLook(deltaTime);

            if (sprintToggle.WasPressedThisFrame())
            {
                sprintToggled = !sprintToggled;
            }

            if (commands.Move.sqrMagnitude < 0.01f)
            {
                sprintToggled = false;
            }

            commands.SprintHeld = sprint.IsPressed() || sprintToggled;
            commands.JumpPressed = jump.WasPressedThisFrame();
            commands.DashPressed = dash.WasPressedThisFrame();

            bool fireAllowed = !suppressFireUntilRelease;
            commands.FireHeld = fireAllowed && fire.IsPressed();
            commands.FirePressed = fireAllowed && fire.WasPressedThisFrame();
            commands.ReloadPressed = reload.WasPressedThisFrame();
            commands.UseItemPressed = useItem.WasPressedThisFrame();
            commands.CycleItemPressed = cycleItem.WasPressedThisFrame();

            for (int i = 0; i < skills.Length; i++)
            {
                if (skills[i].WasPressedThisFrame())
                {
                    commands.SetSkillPressed(i);
                }

                if (selectCard[i].WasPressedThisFrame())
                {
                    commands.RequestCardSelect(i);
                }
            }

            if (selectPrev.WasPressedThisFrame())
            {
                commands.CardSelectStep -= 1;
            }

            if (selectNext.WasPressedThisFrame())
            {
                commands.CardSelectStep += 1;
            }

            return commands;
        }

        Vector2 ReadLook(float deltaTime)
        {
            // Mouse delta is already "per frame", so it is not scaled by time.
            Vector2 mouseDegrees = look.ReadValue<Vector2>() * mouseSensitivity;

            Vector2 stick = lookStick.ReadValue<Vector2>();
            Vector2 stickDegrees = Vector2.zero;
            float magnitude = stick.magnitude;
            if (magnitude > 0.001f)
            {
                float curved = Mathf.Pow(Mathf.Clamp01(magnitude), stickResponseExponent);
                Vector2 shaped = stick / magnitude * curved;
                stickRamp = stickRampTime <= 0f ? 1f : Mathf.MoveTowards(stickRamp, 1f, deltaTime / stickRampTime);
                float ramp = Mathf.Lerp(0.4f, 1f, stickRamp);
                stickDegrees = new Vector2(shaped.x * stickYawSpeed, shaped.y * stickPitchSpeed) * (deltaTime * ramp);
            }
            else
            {
                stickRamp = 0f;
            }

            Vector2 total = mouseDegrees + stickDegrees;
            if (invertY)
            {
                total.y = -total.y;
            }

            return total;
        }
    }
}
