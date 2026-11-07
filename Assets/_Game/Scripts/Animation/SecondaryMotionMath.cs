using UnityEngine;

namespace CoopDeck.Animation
{
    /// <summary>
    /// A damped angular spring (degrees). Used for the fox's tail and ears: it follows a target with a little
    /// lag and overshoot, settles instead of wobbling forever, and stays stable at low frame rates (sub-steps).
    /// </summary>
    public struct AngleSpring
    {
        public float Value;
        public float Velocity;

        /// <param name="frequency">Natural frequency in Hz.</param>
        /// <param name="damping">1 = critically damped (no overshoot), &lt; 1 = some overshoot.</param>
        public void Step(float target, float deltaTime, float frequency, float damping)
        {
            if (deltaTime <= 0f)
            {
                return;
            }

            float w = 2f * Mathf.PI * Mathf.Max(0.01f, frequency);
            int steps = Mathf.Clamp(Mathf.CeilToInt(deltaTime / (1f / 120f)), 1, 8);
            float h = deltaTime / steps;
            for (int i = 0; i < steps; i++)
            {
                // semi-implicit Euler on x'' = w^2 (target - x) - 2 zeta w x'
                float accel = w * w * (target - Value) - 2f * damping * w * Velocity;
                Velocity += accel * h;
                Value += Velocity * h;
            }
        }

        public void Reset(float value)
        {
            Value = value;
            Velocity = 0f;
        }
    }

    /// <summary>Pure rules for the tail and ear targets (degrees), kept apart from the transforms so they can be tested.</summary>
    public static class SecondaryMotionMath
    {
        /// <summary>Tail lift (pitch up &gt; 0) from ground speed (m/s), vertical speed and state.</summary>
        public static float TailPitch(float planarSpeed, float verticalSpeed, bool grounded, bool downed, float ultimate)
        {
            if (downed)
            {
                return -32f;
            }

            float lift = Mathf.Lerp(0f, 8f, Mathf.InverseLerp(0.5f, 9.5f, planarSpeed)); // a little lift: more reads as a stretched tail
            if (!grounded)
            {
                lift += Mathf.Clamp(-verticalSpeed * 2.2f, -18f, 22f); // falling lifts the tail, rising lets it trail down
            }

            return Mathf.Lerp(lift, 48f, Mathf.Clamp01(ultimate));
        }

        /// <summary>Side-to-side sway amplitude (deg): slow in idle, larger and faster with speed.</summary>
        public static float TailSwayAmplitude(float planarSpeed, bool downed)
        {
            if (downed)
            {
                return 2f;
            }

            return Mathf.Lerp(7f, 12f, Mathf.InverseLerp(0f, 9.5f, planarSpeed));
        }

        public static float TailSwayFrequency(float planarSpeed)
        {
            return Mathf.Lerp(0.35f, 2.2f, Mathf.InverseLerp(0f, 6.5f, planarSpeed));
        }

        /// <summary>Ears tilt back (negative) when running, droop when downed, flatten on a hit, perk up when casting.</summary>
        public static float EarPitch(float planarSpeed, bool downed, float hit, float cast)
        {
            float pitch = -Mathf.Lerp(0f, 24f, Mathf.InverseLerp(1f, 9.5f, planarSpeed));
            if (downed)
            {
                pitch = -38f;
            }

            pitch = Mathf.Lerp(pitch, -48f, Mathf.Clamp01(hit));
            pitch += 9f * Mathf.Clamp01(cast) * (1f - Mathf.Clamp01(hit));
            return pitch;
        }
    }
}
