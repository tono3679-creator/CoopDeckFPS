using System;
using CoopDeck.Animation;
using CoopDeck.Combat;
using CoopDeck.Core;
using CoopDeck.Weapons;
using UnityEngine;

namespace CoopDeck.Player
{
    /// <summary>
    /// Composition root of a first-person combatant. Each frame it pulls one
    /// <see cref="CharacterCommands"/> from its command source and hands the pieces to look,
    /// motor and weapon. Skill and item presses are forwarded as events for the deck system
    /// (Phase 3); nothing here knows whether a human, an NPC or the network is in control.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class FpsCharacter : MonoBehaviour, ICombatActor
    {
        [SerializeField] string displayName = "Player";
        [SerializeField] FpsMotor motor;
        [SerializeField] FpsLook look;
        [SerializeField] Health health;
        [SerializeField] HitscanWeapon weapon;

        [Tooltip("Component implementing ICharacterCommandSource. Defaults to one on this GameObject.")]
        [SerializeField] MonoBehaviour commandSourceComponent;

        [Tooltip("Full-body animation (seen by others, and as the local player's shadow). Optional.")]
        [SerializeField] CharacterAnimatorDriver bodyAnimator;

        ICharacterCommandSource commandSource;
        CombatStatus status;

        /// <summary>Skill slot index pressed (0..2). The deck system subscribes.</summary>
        public event Action<int> SkillSlotRequested;

        public event Action ItemUseRequested;
        public event Action ItemCycleRequested;

        /// <summary>A hand slot was picked directly (keys 1/2/3).</summary>
        public event Action<int> CardSelectRequested;

        /// <summary>Card selection moved by -1 / +1 (gamepad D-pad left/right).</summary>
        public event Action<int> CardSelectStepRequested;

        /// <summary>Squad order: cycle the allies' tactic. NPC allies following this character listen.</summary>
        public event Action<FpsCharacter> OrderTacticRequested;

        /// <summary>Squad order: mark what this character is aiming at as the focus target.</summary>
        public event Action<FpsCharacter> OrderMarkRequested;

        /// <summary>The command source was swapped (human ⇄ AI ⇄ network).</summary>
        public event Action<FpsCharacter> ControllerChanged;

        public string DisplayName => displayName;
        public TeamId Team => health != null ? health.Team : TeamId.Players;
        public Health Health => health;
        public CombatStatus Status => status != null ? status : status = GetComponent<CombatStatus>();
        public Vector3 Position => transform.position;
        public bool IsAlive => health == null || health.IsAlive;
        public FpsMotor Motor => motor;
        public FpsLook Look => look;
        public HitscanWeapon Weapon => weapon;
        public CharacterCommands LastCommands { get; private set; }

        public ICharacterCommandSource CommandSource
        {
            get => commandSource;
            set => SetCommandSource(value);
        }

        /// <summary>What kind of controller drives this character right now.</summary>
        public ControllerKind ControllerKind =>
            commandSource is IControllerIdentity identity ? identity.Kind : commandSource != null ? ControllerKind.Scripted : ControllerKind.None;

        public bool IsHumanControlled => ControllerKind == ControllerKind.Human;

        /// <summary>
        /// Swaps who drives this body. The body, deck, weapon and health stay as they are, so a human
        /// can take over an NPC slot (or hand it back) without touching any other system.
        /// </summary>
        public void SetCommandSource(ICharacterCommandSource source)
        {
            if (ReferenceEquals(commandSource, source))
            {
                return;
            }

            commandSource = source;
            ControllerChanged?.Invoke(this);
        }

        /// <summary>The character driven by the local human, or null (HUD, tools, diagnostics).</summary>
        public static FpsCharacter FindLocalHuman()
        {
            FpsCharacter fallback = null;
            var actors = CombatantRegistry.All; // indexed: no boxed enumerator (GC) in per-frame / per-think loops
            for (int ai = 0; ai < actors.Count; ai++)
            {
                ICombatActor actor = actors[ai];
                if (actor is FpsCharacter character && character != null)
                {
                    if (character.IsHumanControlled)
                    {
                        return character;
                    }

                    if (fallback == null && character.ControllerKind != ControllerKind.AI)
                    {
                        fallback = character;
                    }
                }
            }

            return fallback;
        }

        void Awake()
        {
            if (motor == null) motor = GetComponent<FpsMotor>();
            if (look == null) look = GetComponent<FpsLook>();
            if (health == null) health = GetComponent<Health>();
            if (weapon == null) weapon = GetComponentInChildren<HitscanWeapon>();

            commandSource = commandSourceComponent as ICharacterCommandSource ?? GetComponent<ICharacterCommandSource>();
        }

        void Start()
        {
            if (weapon != null)
            {
                weapon.Initialize(this, look, Team, GameLayers.PlayerShotMask);
                if (bodyAnimator != null)
                {
                    weapon.Fired += _ => bodyAnimator.Trigger(AnimatorParams.FireId);
                    weapon.ReloadStarted += () => bodyAnimator.Trigger(AnimatorParams.ReloadId);
                }
            }

            if (bodyAnimator != null && health != null)
            {
                health.Damaged += (info, result) => bodyAnimator.Trigger(AnimatorParams.HitId);
                health.Died += (h, info) => bodyAnimator.SetDead(true);
                health.Revived += h => bodyAnimator.SetDead(false);
            }
        }

        // Registered while enabled so enemies (and later NPC allies) can find this character.
        void OnEnable() => CombatantRegistry.Register(this);

        void OnDisable() => CombatantRegistry.Unregister(this);

        void Update()
        {
            Step(Time.deltaTime);
        }

        /// <summary>One simulation step. Public so tests (and later a fixed network tick) can drive it.</summary>
        public void Step(float deltaTime)
        {
            CharacterCommands commands = IsAlive && commandSource != null
                ? commandSource.GetCommands(deltaTime)
                : default;
            LastCommands = commands;

            if (look != null)
            {
                look.ApplyLook(commands.LookDelta);
            }

            // Shooting cancels sprint so the gun is always ready when the trigger is pulled.
            bool sprint = commands.SprintHeld && !commands.FireHeld;
            if (motor != null)
            {
                motor.Simulate(commands.Move, sprint, commands.JumpPressed, commands.DashPressed, deltaTime);
            }

            if (look != null)
            {
                look.Tick(deltaTime, motor != null && motor.IsSprinting);
            }

            if (weapon != null)
            {
                bool canShoot = motor == null || !motor.IsSprinting;
                weapon.SetTrigger(canShoot && commands.FireHeld, canShoot && commands.FirePressed);
                if (motor != null)
                {
                    weapon.SetMovementState(motor.SpeedFraction, !motor.IsGrounded);
                }

                if (commands.ReloadPressed)
                {
                    weapon.RequestReload();
                }

                weapon.Tick(deltaTime);
            }

            // Selection first, so "pick card 2 + use potion" in the same frame targets card 2.
            if (commands.TryGetCardSelect(out int selectedSlot))
            {
                CardSelectRequested?.Invoke(selectedSlot);
            }

            if (commands.CardSelectStep != 0)
            {
                CardSelectStepRequested?.Invoke(commands.CardSelectStep);
            }

            // Skills run alongside shooting: the weapon already ticked above and is not touched here.
            for (int slot = 0; slot < CharacterCommands.SkillSlotCount; slot++)
            {
                if (commands.IsSkillPressed(slot))
                {
                    SkillSlotRequested?.Invoke(slot);
                }
            }

            if (commands.UseItemPressed)
            {
                ItemUseRequested?.Invoke();
            }

            if (commands.OrderTacticPressed)
            {
                OrderTacticRequested?.Invoke(this);
            }

            if (commands.OrderMarkPressed)
            {
                OrderMarkRequested?.Invoke(this);
            }

            if (commands.CycleItemPressed)
            {
                ItemCycleRequested?.Invoke();
            }
        }
    }
}
