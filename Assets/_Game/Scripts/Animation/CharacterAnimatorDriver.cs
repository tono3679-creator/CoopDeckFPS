using CoopDeck.Core;
using UnityEngine;

namespace CoopDeck.Animation
{
    /// <summary>
    /// Feeds a full-body Animator from an <see cref="ILocomotionSource"/>: 8-direction blend
    /// (MoveX/MoveY), speed, grounded state, plus action triggers (fire, reload, attack, hit, dead).
    /// Locomotion is motor/agent driven (no root motion); the animation follows the movement.
    /// </summary>
    [DisallowMultipleComponent]
    public class CharacterAnimatorDriver : MonoBehaviour
    {
        [SerializeField] Animator animator;
        [Tooltip("Component implementing ILocomotionSource. Defaults to one on this object or a parent.")]
        [SerializeField] MonoBehaviour locomotionSourceComponent;
        [Tooltip("Ground speed that maps to the walk clips (blend radius 1).")]
        [SerializeField, Min(0.1f)] float walkSpeed = 4f;
        [Tooltip("Ground speed the walk cycle was authored for; faster movement plays it faster.")]
        [SerializeField, Min(0.1f)] float clipStrideSpeed = 4f;
        [SerializeField, Min(0f)] float dampTime = 0.1f;

        ILocomotionSource source;

        public Animator Animator => animator;

        public void Configure(Animator targetAnimator, MonoBehaviour locomotionSource, float walk, float stride)
        {
            animator = targetAnimator;
            locomotionSourceComponent = locomotionSource;
            walkSpeed = walk;
            clipStrideSpeed = stride;
            source = locomotionSource as ILocomotionSource;
        }

        void Awake()
        {
            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>();
            }

            source = locomotionSourceComponent as ILocomotionSource ?? GetComponentInParent<ILocomotionSource>();
        }

        void Update()
        {
            if (animator == null || source == null || !animator.isActiveAndEnabled || animator.runtimeAnimatorController == null)
            {
                return;
            }

            float dt = Time.deltaTime;
            Vector3 velocity = source.Velocity;
            Vector2 blend = LocomotionMath.LocalBlend(velocity, transform.rotation, walkSpeed);
            float planarSpeed = new Vector2(velocity.x, velocity.z).magnitude;

            animator.SetFloat(AnimatorParams.MoveXId, blend.x, dampTime, dt);
            animator.SetFloat(AnimatorParams.MoveYId, blend.y, dampTime, dt);
            animator.SetFloat(AnimatorParams.SpeedId, planarSpeed);
            animator.SetFloat(AnimatorParams.LocomotionSpeedId, LocomotionMath.PlaybackRate(planarSpeed, clipStrideSpeed));
            animator.SetBool(AnimatorParams.IsGroundedId, source.IsGrounded);
            animator.SetFloat(AnimatorParams.VerticalSpeedId, velocity.y);
        }

        public void Trigger(int parameterId)
        {
            if (animator != null && animator.isActiveAndEnabled && animator.runtimeAnimatorController != null)
            {
                animator.SetTrigger(parameterId);
            }
        }

        public void SetDead(bool dead)
        {
            if (animator != null && animator.isActiveAndEnabled && animator.runtimeAnimatorController != null)
            {
                animator.SetBool(AnimatorParams.DeadId, dead);
            }
        }
    }
}
