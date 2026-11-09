using System.Collections.Generic;
using CoopDeck.Core;
using UnityEngine;

namespace CoopDeck.Animation
{
    /// <summary>
    /// Feeds a full-body Animator: locomotion from an <see cref="ILocomotionSource"/> (8-direction blend,
    /// speed, grounded state) plus action calls from gameplay events (fire, reload, skills, hits, down/revive,
    /// interactions). Locomotion is motor/agent driven (no root motion); the animation follows the movement.
    ///
    /// Every call is safe on any controller: parameters and states a controller lacks are skipped, so the
    /// placeholder enemy/NPC controllers and the Fox Gunner controller share this component. Nothing here waits
    /// for an animation: gameplay never depends on the Animator.
    /// </summary>
    [DisallowMultipleComponent]
    public class CharacterAnimatorDriver : MonoBehaviour
    {
        [SerializeField] Animator animator;
        [Tooltip("Component implementing ILocomotionSource. Defaults to one on this object or a parent.")]
        [SerializeField] MonoBehaviour locomotionSourceComponent;
        [Tooltip("Ground speed that maps to the walk clips (blend radius 1). Ignored with speed rings.")]
        [SerializeField, Min(0.1f)] float walkSpeed = 4f;
        [Tooltip("Ground speed the walk cycle was authored for; faster movement plays it faster. Ignored with speed rings.")]
        [SerializeField, Min(0.1f)] float clipStrideSpeed = 4f;
        [SerializeField, Min(0f)] float dampTime = 0.1f;

        [Header("Speed rings (blend tree positions in m/s)")]
        [Tooltip("When set, MoveX/MoveY are local velocity in m/s and LocomotionSpeed keeps the planted foot in step.")]
        [SerializeField] LocomotionRing[] rings;
        [SerializeField, Min(1f)] float maxBlendSpeed = 9.5f;

        [Header("Layers / states")]
        [SerializeField] string additiveLayer = "Additive";
        [SerializeField] string fireState = "Fire";
        [Tooltip("Seconds standing still before the idle flourish plays (0 = never).")]
        [SerializeField, Min(0f)] float idleFlourishDelay = 14f;
        [Tooltip("Length of the reload clip; ReloadSpeed = this / the weapon's reload time.")]
        [SerializeField, Min(0.1f)] float reloadClipSeconds = 1.7f;

        ILocomotionSource source;
        RuntimeAnimatorController cachedController;
        readonly HashSet<int> parameters = new HashSet<int>();
        int additiveLayerIndex = -1;
        int fireStateHash;
        bool hasFireState;
        float idleTime;

        /// <summary>A cast started (procedural extras such as the fox's tail fan listen to this).</summary>
        public event System.Action<SkillMotion> SkillStarted;

        /// <summary>A hit landed (true = heavy).</summary>
        public event System.Action<bool> Hurt;

        /// <summary>A dash started, toward this world direction (electric afterimages and similar extras listen).</summary>
        public event System.Action<Vector3> Dashed;

        public Animator Animator => animator;
        public Vector3 AimDirection { get; private set; } = Vector3.forward;
        public bool HasAim { get; private set; }
        public bool IsDowned { get; private set; }
        public bool IsDead { get; private set; }
        public IReadOnlyList<LocomotionRing> Rings => rings;

        /// <summary>1 while the clip has the left hand on the rifle (curve written by the clips); 1 without the curve.</summary>
        public float LeftHandOnRifle => Ready && parameters.Contains(AnimatorParams.LeftHandIKId) ? animator.GetFloat(AnimatorParams.LeftHandIKId) : 1f;

        /// <summary>1 while the left hand is out casting (curve written by the clips).</summary>
        public float CastHand => Ready && parameters.Contains(AnimatorParams.CastHandId) ? animator.GetFloat(AnimatorParams.CastHandId) : 0f;

        public void Configure(Animator targetAnimator, MonoBehaviour locomotionSource, float walk, float stride)
        {
            animator = targetAnimator;
            locomotionSourceComponent = locomotionSource;
            walkSpeed = walk;
            clipStrideSpeed = stride;
            source = locomotionSource as ILocomotionSource;
            cachedController = null;
        }

        /// <summary>
        /// Swaps only where the locomotion comes from. A character simulated on another machine is moved by received
        /// poses, not by its motor (CoopDeck.Networking).
        /// </summary>
        public void SetLocomotionSource(ILocomotionSource locomotion)
        {
            source = locomotion;
            locomotionSourceComponent = locomotion as MonoBehaviour;
        }

        /// <summary>Blend in m/s with speed rings (Fox Gunner controller).</summary>
        public void ConfigureRings(LocomotionRing[] speedRings, float maxSpeed, float reloadClipLength)
        {
            rings = speedRings;
            maxBlendSpeed = maxSpeed;
            reloadClipSeconds = reloadClipLength;
        }

        void Awake()
        {
            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>();
            }

            source = locomotionSourceComponent as ILocomotionSource ?? GetComponentInParent<ILocomotionSource>();
        }

        bool Ready => animator != null && animator.isActiveAndEnabled && animator.runtimeAnimatorController != null && Cache();

        bool Cache()
        {
            if (cachedController == animator.runtimeAnimatorController)
            {
                return true;
            }

            cachedController = animator.runtimeAnimatorController;
            parameters.Clear();
            foreach (AnimatorControllerParameter p in animator.parameters)
            {
                parameters.Add(p.nameHash);
            }

            additiveLayerIndex = animator.GetLayerIndex(additiveLayer);
            fireStateHash = Animator.StringToHash(fireState);
            hasFireState = additiveLayerIndex >= 0 && animator.HasState(additiveLayerIndex, fireStateHash);
            return true;
        }

        public bool HasParameter(int id) => Ready && parameters.Contains(id);

        void Update()
        {
            if (!Ready || source == null)
            {
                return;
            }

            float dt = Time.deltaTime;
            Vector3 velocity = source.Velocity;
            float planarSpeed = new Vector2(velocity.x, velocity.z).magnitude;
            bool useRings = rings != null && rings.Length > 0;
            Vector2 blend = useRings
                ? LocomotionMath.LocalBlend(velocity, transform.rotation, 1f, maxBlendSpeed)
                : LocomotionMath.LocalBlend(velocity, transform.rotation, walkSpeed);
            float rate = useRings
                ? LocomotionMath.RingPlaybackRate(planarSpeed, rings)
                : LocomotionMath.PlaybackRate(planarSpeed, clipStrideSpeed);

            animator.SetFloat(AnimatorParams.MoveXId, blend.x, dampTime, dt);
            animator.SetFloat(AnimatorParams.MoveYId, blend.y, dampTime, dt);
            animator.SetFloat(AnimatorParams.SpeedId, planarSpeed);
            animator.SetFloat(AnimatorParams.LocomotionSpeedId, rate);
            animator.SetBool(AnimatorParams.IsGroundedId, source.IsGrounded);
            animator.SetFloat(AnimatorParams.VerticalSpeedId, velocity.y);

            if (idleFlourishDelay > 0f && parameters.Contains(AnimatorParams.InspectId))
            {
                bool still = planarSpeed < 0.2f && source.IsGrounded && !IsDowned && !IsDead;
                idleTime = still ? idleTime + dt : 0f;
                if (idleTime >= idleFlourishDelay)
                {
                    idleTime = -idleFlourishDelay; // wait twice as long before the next one
                    animator.SetTrigger(AnimatorParams.InspectId);
                }
            }
        }

        // ------------------------------------------------------------------ generic

        public void Trigger(int parameterId)
        {
            if (Ready && parameters.Contains(parameterId))
            {
                animator.SetTrigger(parameterId);
                idleTime = 0f;
            }
        }

        void SetBool(int id, bool value)
        {
            if (Ready && parameters.Contains(id))
            {
                animator.SetBool(id, value);
            }
        }

        void SetInt(int id, int value)
        {
            if (Ready && parameters.Contains(id))
            {
                animator.SetInteger(id, value);
            }
        }

        void SetFloat(int id, float value)
        {
            if (Ready && parameters.Contains(id))
            {
                animator.SetFloat(id, value);
            }
        }

        // ------------------------------------------------------------------ movement

        public void SetSprinting(bool sprinting)
        {
            IsSprinting = sprinting;
            SetBool(AnimatorParams.IsSprintingId, sprinting);
        }

        public bool IsSprinting { get; private set; }

        public void Jump() => Trigger(AnimatorParams.JumpId);

        /// <summary>Dash toward a world direction (the body leans into it).</summary>
        public void Dash(Vector3 worldDirection)
        {
            Vector3 local = Quaternion.Inverse(transform.rotation) * worldDirection;
            var dir = new Vector2(local.x, local.z);
            if (dir.sqrMagnitude < 1e-4f)
            {
                dir = Vector2.up;
            }

            dir.Normalize();
            SetFloat(AnimatorParams.DashXId, dir.x);
            SetFloat(AnimatorParams.DashYId, dir.y);
            Trigger(AnimatorParams.DashId);
            Dashed?.Invoke(worldDirection);
        }

        public void SetSliding(bool sliding) => SetBool(AnimatorParams.SlideId, sliding);

        /// <summary>Where the character aims (world direction); the body's spine and head follow it.</summary>
        public void SetAim(Vector3 worldDirection)
        {
            if (worldDirection.sqrMagnitude > 1e-6f)
            {
                AimDirection = worldDirection.normalized;
                HasAim = true;
            }
        }

        // ------------------------------------------------------------------ weapon

        /// <summary>One shot: restarts the additive recoil (or fires the trigger on simpler controllers).</summary>
        public void Fire()
        {
            if (!Ready)
            {
                return;
            }

            idleTime = 0f;
            if (hasFireState)
            {
                animator.Play(fireStateHash, additiveLayerIndex, 0f);
            }
            else if (parameters.Contains(AnimatorParams.FireId))
            {
                animator.SetTrigger(AnimatorParams.FireId);
            }
        }

        /// <summary>Reload that lasts <paramref name="seconds"/> (the clip is time-scaled to match).</summary>
        public void Reload(float seconds)
        {
            SetFloat(AnimatorParams.ReloadSpeedId, seconds > 0.05f ? reloadClipSeconds / seconds : 1f);
            Trigger(AnimatorParams.ReloadId);
        }

        public void SetAiming(bool aiming) => SetBool(AnimatorParams.AimingId, aiming);

        public void Equip() => Trigger(AnimatorParams.EquipId);

        public void Inspect() => Trigger(AnimatorParams.InspectId);

        // ------------------------------------------------------------------ skills

        public void Skill(SkillMotion motion)
        {
            SetInt(AnimatorParams.SkillMotionId, (int)motion);
            SetBool(AnimatorParams.ChargingId, false);
            Trigger(AnimatorParams.SkillId);
            SkillStarted?.Invoke(motion);
        }

        /// <summary>Hold-to-charge casts (none in the game yet): Charging keeps the charge loop playing.</summary>
        public void BeginCharge()
        {
            SetInt(AnimatorParams.SkillMotionId, (int)SkillMotion.Charged);
            SetBool(AnimatorParams.ChargingId, true);
            Trigger(AnimatorParams.SkillId);
        }

        public void ReleaseCharge() => SetBool(AnimatorParams.ChargingId, false);

        // ------------------------------------------------------------------ reactions

        /// <summary>A hit; <paramref name="heavy"/> plays the upper-body stagger instead of the additive flinch.</summary>
        public void Hit(bool heavy)
        {
            Hurt?.Invoke(heavy);
            if (heavy && HasParameter(AnimatorParams.HitHeavyId))
            {
                Trigger(AnimatorParams.HitHeavyId);
            }
            else
            {
                Trigger(AnimatorParams.HitId);
            }
        }

        public void Knockback() => Trigger(AnimatorParams.KnockbackId);

        /// <summary>Downed (waiting for a revive). Controllers without IsDown show their death pose instead.</summary>
        public void SetDowned(bool downed)
        {
            IsDowned = downed;
            if (HasParameter(AnimatorParams.IsDownId))
            {
                SetBool(AnimatorParams.IsDownId, downed);
            }
            else
            {
                SetDead(downed || IsDead);
            }
        }

        /// <summary>Revived by an ally: get up from the downed pose.</summary>
        public void Revive()
        {
            IsDowned = false;
            SetBool(AnimatorParams.IsDownId, false);
            Trigger(AnimatorParams.ReviveId);
            if (!HasParameter(AnimatorParams.IsDownId))
            {
                SetDead(false);
            }
        }

        public void SetDead(bool dead)
        {
            IsDead = dead;
            SetBool(AnimatorParams.DeadId, dead);
        }

        /// <summary>Back in play after a respawn: clear down/death and bring the rifle up.</summary>
        public void Respawned()
        {
            IsDowned = false;
            IsDead = false;
            SetBool(AnimatorParams.IsDownId, false);
            SetBool(AnimatorParams.DeadId, false);
            Trigger(AnimatorParams.EquipId);
        }

        // ------------------------------------------------------------------ interactions

        public void Interact(InteractionMotion motion)
        {
            SetInt(AnimatorParams.InteractKindId, (int)motion);
            Trigger(AnimatorParams.InteractId);
        }

        /// <summary>While this character keeps a downed ally's revive bar filling.</summary>
        public void SetReviving(bool reviving) => SetBool(AnimatorParams.RevivingId, reviving);
    }
}
