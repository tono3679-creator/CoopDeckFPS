using System;
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

        ICharacterCommandSource commandSource;

        /// <summary>Skill slot index pressed (0..2). The deck system subscribes in Phase 3.</summary>
        public event Action<int> SkillSlotRequested;

        public event Action ItemUseRequested;
        public event Action ItemCycleRequested;

        public string DisplayName => displayName;
        public TeamId Team => health != null ? health.Team : TeamId.Players;
        public Health Health => health;
        public Vector3 Position => transform.position;
        public bool IsAlive => health == null || health.IsAlive;
        public FpsMotor Motor => motor;
        public FpsLook Look => look;
        public HitscanWeapon Weapon => weapon;
        public CharacterCommands LastCommands { get; private set; }

        public ICharacterCommandSource CommandSource
        {
            get => commandSource;
            set => commandSource = value;
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
            }
        }

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

            if (commands.CycleItemPressed)
            {
                ItemCycleRequested?.Invoke();
            }
        }
    }
}
