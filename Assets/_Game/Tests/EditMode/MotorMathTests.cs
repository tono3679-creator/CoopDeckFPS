using CoopDeck.Player;
using NUnit.Framework;
using UnityEngine;

namespace CoopDeck.Tests
{
    public class MotorMathTests
    {
        const float Dt = 1f / 60f;

        [Test]
        public void Ground_ReachesTargetSpeed_InExpectedTime()
        {
            Vector3 velocity = Vector3.zero;
            var target = new Vector3(0f, 0f, 6.5f);
            int frames = 0;
            while (velocity.magnitude < 6.5f - 0.0001f && frames < 1000)
            {
                velocity = MotorMath.StepGroundVelocity(velocity, target, 65f, 50f, 1.6f, Dt);
                frames++;
            }

            // 6.5 m/s at 65 m/s² = 0.1 s = 6 frames.
            Assert.That(frames, Is.InRange(6, 7));
            Assert.That(velocity.z, Is.EqualTo(6.5f).Within(1e-4f));
        }

        [Test]
        public void Ground_DeceleratesToZero_WithoutOvershoot()
        {
            var velocity = new Vector3(5f, 0f, 0f);
            for (int i = 0; i < 30; i++)
            {
                velocity = MotorMath.StepGroundVelocity(velocity, Vector3.zero, 65f, 50f, 1.6f, Dt);
                Assert.That(velocity.x, Is.GreaterThanOrEqualTo(0f), "must not reverse while stopping");
            }

            Assert.That(velocity.magnitude, Is.EqualTo(0f).Within(1e-5f));
        }

        [Test]
        public void Ground_Reversing_UsesHigherAcceleration()
        {
            var moving = new Vector3(6f, 0f, 0f);
            Vector3 reversed = MotorMath.StepGroundVelocity(moving, new Vector3(-6f, 0f, 0f), 60f, 50f, 1.5f, Dt);
            Vector3 normal = MotorMath.StepGroundVelocity(Vector3.zero, new Vector3(-6f, 0f, 0f), 60f, 50f, 1.5f, Dt);

            float reversedChange = (moving - reversed).magnitude;
            float normalChange = normal.magnitude;
            Assert.That(reversedChange, Is.EqualTo(normalChange * 1.5f).Within(1e-4f));
        }

        [Test]
        public void Ground_IgnoresVerticalComponents()
        {
            Vector3 result = MotorMath.StepGroundVelocity(new Vector3(0f, 3f, 0f), new Vector3(0f, 5f, 2f), 100f, 100f, 1f, 1f);
            Assert.That(result.y, Is.EqualTo(0f));
        }

        [Test]
        public void Air_NeverGainsSpeedBeyondWishOrCurrent()
        {
            // Fast after a dash: steering must not add speed.
            var fast = new Vector3(0f, 0f, 19f);
            Vector3 steered = MotorMath.StepAirVelocity(fast, Vector3.right, 6.5f, 20f, Dt);
            Assert.That(steered.magnitude, Is.LessThanOrEqualTo(19f + 1e-4f));
            Assert.That(steered.x, Is.GreaterThan(0f), "air control still steers");

            // Slow: can accelerate up to the wish speed but not past it.
            Vector3 slow = Vector3.zero;
            for (int i = 0; i < 600; i++)
            {
                slow = MotorMath.StepAirVelocity(slow, Vector3.forward, 6.5f, 20f, Dt);
            }

            Assert.That(slow.magnitude, Is.EqualTo(6.5f).Within(1e-3f));
        }

        [Test]
        public void Air_NoInput_KeepsMomentum()
        {
            var velocity = new Vector3(3f, 0f, 4f);
            Assert.That(MotorMath.StepAirVelocity(velocity, Vector3.zero, 6.5f, 20f, Dt), Is.EqualTo(velocity));
        }

        [Test]
        public void JumpVelocity_ReachesRequestedHeight()
        {
            const float height = 1.35f;
            const float gravity = 26f;
            float v = MotorMath.JumpVelocity(height, gravity);

            // Integrate the rise with the same gravity step the motor uses.
            float y = 0f;
            float peak = 0f;
            const float step = 1f / 600f;
            while (v > 0f)
            {
                y += v * step;
                v = MotorMath.StepGravity(v, gravity, 1.25f, 40f, step);
                peak = Mathf.Max(peak, y);
            }

            Assert.That(peak, Is.EqualTo(height).Within(0.02f));
        }

        [Test]
        public void Gravity_FallsFaster_AndClampsToTerminalSpeed()
        {
            float rising = MotorMath.StepGravity(1f, 20f, 2f, 30f, 0.01f);
            float falling = MotorMath.StepGravity(-1f, 20f, 2f, 30f, 0.01f);
            Assert.That(1f - rising, Is.EqualTo(0.2f).Within(1e-5f));
            Assert.That(-1f - falling, Is.EqualTo(0.4f).Within(1e-5f));
            Assert.That(MotorMath.StepGravity(-29.9f, 20f, 2f, 30f, 1f), Is.EqualTo(-30f));
        }
    }
}
