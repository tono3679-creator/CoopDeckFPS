using UnityEngine;

namespace CoopDeck.Player
{
    /// <summary>Movement integration steps as pure functions (unit-tested, reusable by NPC and network prediction).</summary>
    public static class MotorMath
    {
        /// <summary>
        /// Ground movement: move the planar velocity toward the target at a constant rate.
        /// Reversing direction uses a higher rate so strafe changes feel responsive.
        /// </summary>
        public static Vector3 StepGroundVelocity(Vector3 current, Vector3 target, float acceleration,
            float deceleration, float reverseMultiplier, float deltaTime)
        {
            current.y = 0f;
            target.y = 0f;

            float rate;
            if (target.sqrMagnitude < 0.0001f)
            {
                rate = deceleration;
            }
            else
            {
                bool reversing = current.sqrMagnitude > 0.0001f && Vector3.Dot(current, target) < 0f;
                rate = acceleration * (reversing ? reverseMultiplier : 1f);
            }

            return Vector3.MoveTowards(current, target, rate * deltaTime);
        }

        /// <summary>
        /// Air control: steer toward the input direction without ever gaining speed beyond
        /// max(target speed, current speed). Momentum from dashes and jumps is kept.
        /// </summary>
        public static Vector3 StepAirVelocity(Vector3 current, Vector3 wishDirection, float wishSpeed,
            float airAcceleration, float deltaTime)
        {
            current.y = 0f;
            wishDirection.y = 0f;
            if (wishDirection.sqrMagnitude < 0.0001f)
            {
                return current;
            }

            float limit = Mathf.Max(wishSpeed, current.magnitude);
            Vector3 next = current + wishDirection.normalized * (airAcceleration * deltaTime);
            return Vector3.ClampMagnitude(next, limit);
        }

        public static float JumpVelocity(float jumpHeight, float gravity)
        {
            return Mathf.Sqrt(2f * Mathf.Max(0f, gravity) * Mathf.Max(0f, jumpHeight));
        }

        public static float StepGravity(float verticalVelocity, float gravity, float fallMultiplier,
            float maxFallSpeed, float deltaTime)
        {
            float g = verticalVelocity < 0f ? gravity * fallMultiplier : gravity;
            return Mathf.Max(verticalVelocity - g * deltaTime, -maxFallSpeed);
        }
    }
}
