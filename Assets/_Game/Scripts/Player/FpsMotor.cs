using System;
using CoopDeck.Core;
using UnityEngine;

namespace CoopDeck.Player
{
    /// <summary>
    /// Kinematic first-person movement on a CharacterController: acceleration/deceleration,
    /// sprint, dash with charges, jump with coyote time and input buffering, air control,
    /// ground snapping on slopes/stairs, and wall sliding without sticking.
    /// It is driven by <see cref="Simulate"/> only, never by input directly.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class FpsMotor : MonoBehaviour, ILocomotionSource
    {
        [SerializeField] MovementProfile profile;

        CharacterController controller;
        Vector3 horizontalVelocity;
        float verticalVelocity;
        bool grounded;
        float simTime;
        float lastGroundedTime = float.NegativeInfinity;
        float lastJumpPressTime = float.NegativeInfinity;
        float lastJumpTime = float.NegativeInfinity;
        float dashRemaining;
        float dashCooldownRemaining;
        float dashCharge;
        Vector3 dashDirection;
        int movementMask;

        public event Action Jumped;
        public event Action Dashed;

        /// <summary>Raised on touchdown with the downward speed at impact (m/s).</summary>
        public event Action<float> Landed;

        public MovementProfile Profile => profile;
        public bool IsGrounded => grounded;
        public bool IsSprinting { get; private set; }
        public bool IsDashing => dashRemaining > 0f;

        /// <summary>Direction of the current (or last) dash, flat, world space.</summary>
        public Vector3 DashDirection => dashDirection;
        public Vector3 Velocity => horizontalVelocity + Vector3.up * verticalVelocity;
        public Vector3 HorizontalVelocity => horizontalVelocity;

        /// <summary>Available dash charges, fractional while recharging.</summary>
        public float DashCharge => dashCharge;

        /// <summary>0 at rest, 1 at walk speed, above 1 when sprinting or dashing.</summary>
        public float SpeedFraction => profile.walkSpeed > 0f ? horizontalVelocity.magnitude / profile.walkSpeed : 0f;

        void Awake()
        {
            controller = GetComponent<CharacterController>();
            if (profile == null)
            {
                profile = MovementProfile.CreateRuntimeDefault();
            }

            dashCharge = profile.dashCharges;
            movementMask = GameLayers.MovementBlockingMask;
        }

        public void SetProfile(MovementProfile newProfile)
        {
            profile = newProfile != null ? newProfile : MovementProfile.CreateRuntimeDefault();
            dashCharge = Mathf.Min(dashCharge, profile.dashCharges);
        }

        /// <param name="move">Local planar input (x = right, y = forward), magnitude 0..1.</param>
        public void Simulate(Vector2 move, bool sprintHeld, bool jumpPressed, bool dashPressed, float deltaTime)
        {
            if (deltaTime <= 0f || !controller.enabled)
            {
                return;
            }

            simTime += deltaTime;
            if (grounded)
            {
                lastGroundedTime = simTime;
            }

            if (jumpPressed)
            {
                lastJumpPressTime = simTime;
            }

            UpdateDashCharges(deltaTime);

            Vector3 wish = transform.right * move.x + transform.forward * move.y;
            wish.y = 0f;
            wish = Vector3.ClampMagnitude(wish, 1f);

            IsSprinting = sprintHeld && move.y > 0.1f && !IsDashing;
            float targetSpeed = IsSprinting ? profile.sprintSpeed : profile.walkSpeed;

            if (dashPressed)
            {
                TryStartDash(wish);
            }

            bool jumpedThisStep = false;
            if (IsDashing)
            {
                dashRemaining -= deltaTime;
                horizontalVelocity = dashDirection * profile.dashSpeed;
                if (dashRemaining <= 0f)
                {
                    // Leave the dash at run speed so it blends into normal movement instead of stopping dead.
                    horizontalVelocity = dashDirection * targetSpeed;
                }
            }
            else if (grounded)
            {
                horizontalVelocity = MotorMath.StepGroundVelocity(horizontalVelocity, wish * targetSpeed,
                    profile.groundAcceleration, profile.groundDeceleration, profile.reverseAccelerationMultiplier, deltaTime);
            }
            else
            {
                horizontalVelocity = MotorMath.StepAirVelocity(horizontalVelocity, wish,
                    targetSpeed * profile.airSpeedFactor, profile.airAcceleration, deltaTime);
            }

            bool jumpBuffered = simTime - lastJumpPressTime <= profile.jumpBufferTime;
            bool canUseCoyote = simTime - lastGroundedTime <= profile.coyoteTime && simTime - lastJumpTime > profile.coyoteTime;
            if (jumpBuffered && (grounded || canUseCoyote))
            {
                verticalVelocity = MotorMath.JumpVelocity(profile.jumpHeight, profile.gravity);
                lastJumpTime = simTime;
                lastJumpPressTime = float.NegativeInfinity;
                grounded = false;
                jumpedThisStep = true;
                Jumped?.Invoke();
            }

            if (IsDashing)
            {
                // Dashes are flat: no gravity while dashing, but never cancel an upward jump.
                verticalVelocity = grounded ? -profile.groundStickSpeed : Mathf.Max(verticalVelocity, 0f);
            }
            else if (grounded && verticalVelocity <= 0f)
            {
                verticalVelocity = -profile.groundStickSpeed;
            }
            else
            {
                verticalVelocity = MotorMath.StepGravity(verticalVelocity, profile.gravity,
                    profile.fallGravityMultiplier, profile.maxFallSpeed, deltaTime);
            }

            bool wasGrounded = grounded;
            float fallSpeedBeforeMove = -verticalVelocity;

            CollisionFlags flags = controller.Move((horizontalVelocity + Vector3.up * verticalVelocity) * deltaTime);

            if ((flags & CollisionFlags.Above) != 0 && verticalVelocity > 0f)
            {
                verticalVelocity = 0f;
            }

            bool nowGrounded = controller.isGrounded;
            if (!nowGrounded && wasGrounded && !jumpedThisStep && verticalVelocity <= 0f)
            {
                nowGrounded = SnapToGround();
            }

            if (nowGrounded && !wasGrounded && !jumpedThisStep)
            {
                Landed?.Invoke(Mathf.Max(0f, fallSpeedBeforeMove));
            }

            grounded = nowGrounded;
        }

        void UpdateDashCharges(float deltaTime)
        {
            if (dashCooldownRemaining > 0f)
            {
                dashCooldownRemaining -= deltaTime;
            }

            if (dashCharge < profile.dashCharges)
            {
                dashCharge = Mathf.Min(profile.dashCharges, dashCharge + deltaTime / profile.dashRechargeTime);
            }
        }

        void TryStartDash(Vector3 wish)
        {
            if (IsDashing || dashCooldownRemaining > 0f || dashCharge < 1f || profile.dashCharges <= 0)
            {
                return;
            }

            Vector3 forward = transform.forward;
            forward.y = 0f;
            dashDirection = wish.sqrMagnitude > 0.01f ? wish.normalized : forward.normalized;
            dashCharge -= 1f;
            dashCooldownRemaining = profile.dashCooldown;
            dashRemaining = profile.dashDuration;
            Dashed?.Invoke();
        }

        /// <summary>Keeps the character on the ground when walking down slopes or off small steps.</summary>
        bool SnapToGround()
        {
            float radius = controller.radius * 0.95f;
            Vector3 center = transform.TransformPoint(controller.center);
            Vector3 bottomSphere = center + Vector3.down * (controller.height * 0.5f - controller.radius);
            Vector3 origin = bottomSphere + Vector3.up * 0.05f;
            float maxDistance = profile.groundSnapDistance + controller.skinWidth + 0.05f;

            if (!Physics.SphereCast(origin, radius, Vector3.down, out RaycastHit hit, maxDistance, movementMask, QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            if (Vector3.Angle(hit.normal, Vector3.up) > controller.slopeLimit)
            {
                return false;
            }

            controller.Move(Vector3.down * (hit.distance + controller.skinWidth));
            return controller.isGrounded;
        }

        void OnControllerColliderHit(ControllerColliderHit hit)
        {
            // Only contacts above step height are walls. Lower contacts are steps the controller climbs.
            float feetY = transform.TransformPoint(controller.center).y - controller.height * 0.5f;
            if (hit.point.y <= feetY + controller.stepOffset)
            {
                return;
            }

            Vector3 normal = hit.normal;
            if (normal.y > 0.7f || normal.y < -0.7f)
            {
                return;
            }

            // Remove the velocity component pushing into the wall so the player slides along it.
            var planar = new Vector3(normal.x, 0f, normal.z);
            if (planar.sqrMagnitude < 0.0001f)
            {
                return;
            }

            planar.Normalize();
            float into = Vector3.Dot(horizontalVelocity, planar);
            if (into < 0f)
            {
                horizontalVelocity -= planar * into;
            }
        }

        /// <summary>Moves the character instantly (spawn, respawn, tests).</summary>
        public void Teleport(Vector3 position)
        {
            bool wasEnabled = controller.enabled;
            controller.enabled = false;
            transform.position = position;
            controller.enabled = wasEnabled;
            horizontalVelocity = Vector3.zero;
            verticalVelocity = 0f;
            dashRemaining = 0f;
            grounded = false;
        }
    }
}
