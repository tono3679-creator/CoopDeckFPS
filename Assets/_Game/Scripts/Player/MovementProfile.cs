using UnityEngine;

namespace CoopDeck.Player
{
    /// <summary>
    /// Movement tuning for one character. Characters (Phase 8) reference one of these, so a
    /// fast character is a data change, not a code change.
    /// </summary>
    [CreateAssetMenu(menuName = "CoopDeck/Movement Profile", fileName = "MP_NewProfile")]
    public class MovementProfile : ScriptableObject
    {
        [Header("Ground")]
        [Min(0f)] public float walkSpeed = 6.5f;
        [Min(0f)] public float sprintSpeed = 9.5f;
        [Tooltip("m/s² toward the target velocity while there is input.")]
        [Min(0f)] public float groundAcceleration = 65f;
        [Tooltip("m/s² toward zero when there is no input.")]
        [Min(0f)] public float groundDeceleration = 50f;
        [Tooltip("Acceleration multiplier when reversing direction, for snappy strafing.")]
        [Min(1f)] public float reverseAccelerationMultiplier = 1.6f;

        [Header("Air")]
        [Min(0f)] public float airAcceleration = 20f;
        [Tooltip("Air steering target speed as a fraction of the ground speed.")]
        [Range(0f, 1.5f)] public float airSpeedFactor = 1f;

        [Header("Jump & gravity")]
        [Min(0f)] public float jumpHeight = 1.35f;
        [Min(0.1f)] public float gravity = 26f;
        [Tooltip("Gravity multiplier while falling, for a snappier arc.")]
        [Min(1f)] public float fallGravityMultiplier = 1.25f;
        [Min(1f)] public float maxFallSpeed = 40f;
        [Tooltip("Seconds after leaving a ledge during which a jump still counts as grounded.")]
        [Min(0f)] public float coyoteTime = 0.12f;
        [Tooltip("Seconds a jump press is remembered before landing.")]
        [Min(0f)] public float jumpBufferTime = 0.12f;

        [Header("Dash")]
        [Min(0f)] public float dashSpeed = 19f;
        [Min(0.01f)] public float dashDuration = 0.16f;
        [Min(0f)] public float dashCooldown = 0.3f;
        [Min(0)] public int dashCharges = 2;
        [Tooltip("Seconds to recharge one dash charge.")]
        [Min(0.1f)] public float dashRechargeTime = 2.2f;

        [Header("Grounding")]
        [Tooltip("How far below the feet the motor looks for ground to stay glued to slopes and stairs.")]
        [Min(0f)] public float groundSnapDistance = 0.35f;
        [Tooltip("Small downward speed kept while grounded so the controller stays in contact.")]
        [Min(0f)] public float groundStickSpeed = 2f;

        public static MovementProfile CreateRuntimeDefault()
        {
            var profile = CreateInstance<MovementProfile>();
            profile.name = "MP_RuntimeDefault";
            return profile;
        }
    }
}
