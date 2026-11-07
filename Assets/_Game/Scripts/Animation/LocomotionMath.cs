using UnityEngine;

namespace CoopDeck.Animation
{
    /// <summary>Pure helpers that turn motion into blend-tree inputs (unit-tested).</summary>
    public static class LocomotionMath
    {
        /// <summary>
        /// World velocity → 2D blend position relative to where the character faces
        /// (x = strafe right, y = forward), scaled so walking speed is radius 1.
        /// </summary>
        public static Vector2 LocalBlend(Vector3 worldVelocity, Quaternion facing, float walkSpeed, float maxRadius = 1f)
        {
            Vector3 local = Quaternion.Inverse(facing) * worldVelocity;
            var blend = new Vector2(local.x, local.z) / Mathf.Max(0.01f, walkSpeed);
            return Vector2.ClampMagnitude(blend, maxRadius);
        }

        /// <summary>
        /// Playback rate for the walk cycle so stride roughly matches ground speed. Standing still
        /// plays idle at normal speed.
        /// </summary>
        public static float PlaybackRate(float planarSpeed, float clipStrideSpeed, float min = 0.75f, float max = 1.8f)
        {
            if (planarSpeed < 0.05f || clipStrideSpeed <= 0f)
            {
                return 1f;
            }

            return Mathf.Clamp(planarSpeed / clipStrideSpeed, min, max);
        }
    }
}
