using System.Collections.Generic;
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

        /// <summary>
        /// Playback rate for a directional blend tree whose clips sit on speed rings (walk 1.5 m/s, run 6.5 m/s...).
        /// Between two rings the tree blends both stride length and cycle length linearly, so the natural ground
        /// speed is lerp(stride) / lerp(cycle), not the plain speed: this returns speed / natural speed so the planted
        /// foot keeps pace. Inside the first ring the clips blend with a still pose of the same cycle length at the
        /// centre, so the stride shrinks with the speed and the rate stays 1. Above the last ring the cycle speeds up
        /// (clamped to <paramref name="maxRate"/>).
        /// </summary>
        public static float RingPlaybackRate(float planarSpeed, IReadOnlyList<LocomotionRing> rings, float maxRate = 1.6f)
        {
            if (rings == null || rings.Count == 0 || planarSpeed <= rings[0].speed)
            {
                return 1f;
            }

            for (int i = 0; i < rings.Count - 1; i++)
            {
                LocomotionRing a = rings[i];
                LocomotionRing b = rings[i + 1];
                if (planarSpeed <= b.speed)
                {
                    float w = (planarSpeed - a.speed) / Mathf.Max(1e-4f, b.speed - a.speed);
                    float stride = Mathf.Lerp(a.speed * a.cycle, b.speed * b.cycle, w);
                    float cycle = Mathf.Lerp(a.cycle, b.cycle, w);
                    float natural = stride / Mathf.Max(1e-4f, cycle);
                    return Mathf.Clamp(planarSpeed / Mathf.Max(1e-4f, natural), 0.5f, maxRate);
                }
            }

            LocomotionRing last = rings[rings.Count - 1];
            return Mathf.Clamp(planarSpeed / Mathf.Max(1e-4f, last.speed), 1f, maxRate);
        }

        /// <summary>
        /// Light vs heavy hit from the share of max health one hit took (heavy plays a full upper-body stagger).
        /// </summary>
        public static bool IsHeavyHit(float damage, float maxHealth, float heavyShare = 0.2f)
        {
            return maxHealth > 0f && damage >= maxHealth * heavyShare - 1e-3f;
        }
    }
}
