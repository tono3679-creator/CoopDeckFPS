using UnityEngine;

namespace CoopDeck.Animation
{
    /// <summary>
    /// Animator parameter names shared by every full-body controller (ANIMATION_GUIDELINES.md §3).
    /// Final motion-capture controllers must keep these names so code never changes. Controllers may
    /// omit parameters; <see cref="CharacterAnimatorDriver"/> only sets the ones a controller has.
    /// </summary>
    public static class AnimatorParams
    {
        // Locomotion
        public const string MoveX = "MoveX";
        public const string MoveY = "MoveY";
        public const string Speed = "Speed";
        public const string LocomotionSpeed = "LocomotionSpeed";
        public const string IsGrounded = "IsGrounded";
        public const string VerticalSpeed = "VerticalSpeed";
        public const string IsSprinting = "IsSprinting";
        public const string Jump = "Jump";
        public const string Dash = "Dash";
        public const string DashX = "DashX";
        public const string DashY = "DashY";
        public const string Slide = "Slide";

        // Weapon
        public const string Fire = "Fire";
        public const string Reload = "Reload";
        public const string ReloadSpeed = "ReloadSpeed";
        public const string Aiming = "Aiming";
        public const string Equip = "Equip";
        public const string Inspect = "Inspect";

        // Skills (SkillMotion values)
        public const string Skill = "Skill";
        public const string SkillMotion = "SkillMotion";
        public const string Charging = "Charging";

        // Reactions
        public const string Attack = "Attack";
        public const string Hit = "Hit";
        public const string HitHeavy = "HitHeavy";
        public const string Knockback = "Knockback";
        public const string IsDown = "IsDown";
        public const string Revive = "Revive";
        public const string Dead = "Dead";

        // Interactions (InteractionMotion values)
        public const string Interact = "Interact";
        public const string InteractKind = "InteractKind";
        public const string Reviving = "Reviving";

        // Written by clip curves (A_FoxGunner_Clips.json): 1 = left hand on the rifle / left hand casting.
        public const string LeftHandIK = "LeftHandIK";
        public const string CastHand = "CastHand";

        public static readonly int MoveXId = Animator.StringToHash(MoveX);
        public static readonly int MoveYId = Animator.StringToHash(MoveY);
        public static readonly int SpeedId = Animator.StringToHash(Speed);
        public static readonly int LocomotionSpeedId = Animator.StringToHash(LocomotionSpeed);
        public static readonly int IsGroundedId = Animator.StringToHash(IsGrounded);
        public static readonly int VerticalSpeedId = Animator.StringToHash(VerticalSpeed);
        public static readonly int IsSprintingId = Animator.StringToHash(IsSprinting);
        public static readonly int JumpId = Animator.StringToHash(Jump);
        public static readonly int DashId = Animator.StringToHash(Dash);
        public static readonly int DashXId = Animator.StringToHash(DashX);
        public static readonly int DashYId = Animator.StringToHash(DashY);
        public static readonly int SlideId = Animator.StringToHash(Slide);
        public static readonly int FireId = Animator.StringToHash(Fire);
        public static readonly int ReloadId = Animator.StringToHash(Reload);
        public static readonly int ReloadSpeedId = Animator.StringToHash(ReloadSpeed);
        public static readonly int AimingId = Animator.StringToHash(Aiming);
        public static readonly int EquipId = Animator.StringToHash(Equip);
        public static readonly int InspectId = Animator.StringToHash(Inspect);
        public static readonly int SkillId = Animator.StringToHash(Skill);
        public static readonly int SkillMotionId = Animator.StringToHash(SkillMotion);
        public static readonly int ChargingId = Animator.StringToHash(Charging);
        public static readonly int AttackId = Animator.StringToHash(Attack);
        public static readonly int HitId = Animator.StringToHash(Hit);
        public static readonly int HitHeavyId = Animator.StringToHash(HitHeavy);
        public static readonly int KnockbackId = Animator.StringToHash(Knockback);
        public static readonly int IsDownId = Animator.StringToHash(IsDown);
        public static readonly int ReviveId = Animator.StringToHash(Revive);
        public static readonly int DeadId = Animator.StringToHash(Dead);
        public static readonly int InteractId = Animator.StringToHash(Interact);
        public static readonly int InteractKindId = Animator.StringToHash(InteractKind);
        public static readonly int RevivingId = Animator.StringToHash(Reviving);
        public static readonly int LeftHandIKId = Animator.StringToHash(LeftHandIK);
        public static readonly int CastHandId = Animator.StringToHash(CastHand);
    }

    /// <summary>
    /// Shared cast motions (ANIMATION_GUIDELINES.md): a card picks one by how it is cast, not one clip per card,
    /// so split / homing / exploding passives reuse the same body motion.
    /// </summary>
    public enum SkillMotion
    {
        QuickCast = 0,
        Charged = 1,
        AreaCast = 2,
        SelfBuff = 3,
        Ultimate = 4,
    }

    /// <summary>Upper-body interaction gestures (InteractKind parameter).</summary>
    public enum InteractionMotion
    {
        Pickup = 0,
        Interact = 1,
        UseItem = 2,
        Emote = 3,
    }

    /// <summary>One ring of a directional locomotion blend tree: ground speed and the clip's cycle length.</summary>
    [System.Serializable]
    public struct LocomotionRing
    {
        public float speed;
        public float cycle;

        public LocomotionRing(float speed, float cycle)
        {
            this.speed = speed;
            this.cycle = cycle;
        }
    }
}
