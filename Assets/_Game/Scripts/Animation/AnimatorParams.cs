using UnityEngine;

namespace CoopDeck.Animation
{
    /// <summary>
    /// Animator parameter names shared by every full-body controller (ANIMATION_GUIDELINES.md §3).
    /// Final motion-capture controllers must keep these names so code never changes.
    /// </summary>
    public static class AnimatorParams
    {
        public const string MoveX = "MoveX";
        public const string MoveY = "MoveY";
        public const string Speed = "Speed";
        public const string LocomotionSpeed = "LocomotionSpeed";
        public const string IsGrounded = "IsGrounded";
        public const string VerticalSpeed = "VerticalSpeed";
        public const string Fire = "Fire";
        public const string Reload = "Reload";
        public const string Attack = "Attack";
        public const string Hit = "Hit";
        public const string Dead = "Dead";

        public static readonly int MoveXId = Animator.StringToHash(MoveX);
        public static readonly int MoveYId = Animator.StringToHash(MoveY);
        public static readonly int SpeedId = Animator.StringToHash(Speed);
        public static readonly int LocomotionSpeedId = Animator.StringToHash(LocomotionSpeed);
        public static readonly int IsGroundedId = Animator.StringToHash(IsGrounded);
        public static readonly int VerticalSpeedId = Animator.StringToHash(VerticalSpeed);
        public static readonly int FireId = Animator.StringToHash(Fire);
        public static readonly int ReloadId = Animator.StringToHash(Reload);
        public static readonly int AttackId = Animator.StringToHash(Attack);
        public static readonly int HitId = Animator.StringToHash(Hit);
        public static readonly int DeadId = Animator.StringToHash(Dead);
    }
}
