using System;
using CoopDeck.Animation;
using CoopDeck.Combat;
using CoopDeck.Core;
using CoopDeck.Effects;
using CoopDeck.Enemies;
using UnityEngine;
using UnityEngine.AI;

namespace CoopDeck.AI
{
    /// <summary>
    /// Drives an enemy body from <see cref="EnemyStateMachine"/>: picks the nearest hostile from
    /// <see cref="CombatantRegistry"/>, checks line of sight, moves with a NavMeshAgent, turns to
    /// face the target during the telegraph, and performs the melee strike or projectile shot.
    /// Exposes its velocity as an <see cref="ILocomotionSource"/> for the animator.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent), typeof(EnemyActor))]
    public class EnemyBrain : MonoBehaviour, ILocomotionSource
    {
        const float TargetHeight = 1.1f;
        const float RepathInterval = 0.25f;

        [SerializeField] Transform eye;
        [SerializeField] Transform muzzle;
        [Tooltip("Moved to the target each frame; the head-aim rig constraint looks at it.")]
        [SerializeField] Transform lookTarget;
        [SerializeField] CharacterAnimatorDriver animatorDriver;

        NavMeshAgent agent;
        EnemyActor actor;
        CombatStatus status;
        readonly EnemyStateMachine machine = new EnemyStateMachine();
        ICombatActor target;
        float repathTimer;
        bool damagedThisFrame;

        /// <summary>Raised when an attack lands (melee strike resolved or projectile launched).</summary>
        public event Action<EnemyBrain> AttackReleased;

        /// <summary>Raised when the telegraph starts.</summary>
        public event Action<EnemyBrain> WindupStarted;

        public EnemyState State => machine.State;
        public ICombatActor Target => target;
        public EnemyStateMachine Machine => machine;
        public Vector3 Velocity => agent != null && agent.enabled ? agent.velocity : Vector3.zero;
        public bool IsGrounded => true;

        public void Configure(Transform eyeTransform, Transform muzzleTransform, Transform look, CharacterAnimatorDriver driver)
        {
            eye = eyeTransform;
            muzzle = muzzleTransform;
            lookTarget = look;
            animatorDriver = driver;
        }

        void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            actor = GetComponent<EnemyActor>();
            status = GetComponent<CombatStatus>();
            ApplyDefinition();
        }

        public void ApplyDefinition()
        {
            EnemyDefinition def = actor != null ? actor.Definition : null;
            if (def == null || agent == null)
            {
                return;
            }

            agent.speed = def.moveSpeed;
            agent.acceleration = def.acceleration;
            agent.angularSpeed = def.angularSpeed;
            agent.stoppingDistance = def.preferredRange;
            agent.autoBraking = true;
        }

        void Start()
        {
            if (actor.AlertOnSpawn)
            {
                damagedThisFrame = true; // same as being hit: aggro without line of sight
            }
        }

        void OnEnable()
        {
            actor.Health.Damaged += OnDamaged;
            actor.Health.Died += OnDied;
        }

        void OnDisable()
        {
            actor.Health.Damaged -= OnDamaged;
            actor.Health.Died -= OnDied;
        }

        void OnDamaged(DamageInfo info, DamageResult result)
        {
            damagedThisFrame = true;
            if (animatorDriver != null) animatorDriver.Trigger(AnimatorParams.HitId);
        }

        void OnDied(Health health, DamageInfo info)
        {
            machine.Kill();
            if (agent != null && agent.enabled && agent.isOnNavMesh)
            {
                agent.isStopped = true;
                agent.ResetPath();
            }

            if (animatorDriver != null) animatorDriver.SetDead(true);
        }

        void Update()
        {
            EnemyDefinition def = actor.Definition;
            if (def == null || machine.State == EnemyState.Dead)
            {
                return;
            }

            float dt = Time.deltaTime;
            target = CombatantRegistry.FindNearestHostile(actor.Team, transform.position, def.loseInterestRange);

            var perception = new EnemyPerception { WasDamaged = damagedThisFrame };
            damagedThisFrame = false;
            Vector3 targetPoint = transform.position + transform.forward * 5f + Vector3.up * TargetHeight;
            if (target != null)
            {
                targetPoint = target.Position + Vector3.up * TargetHeight;
                Vector3 flat = target.Position - transform.position;
                flat.y = 0f;
                perception.HasTarget = true;
                perception.Distance = flat.magnitude;
                perception.LineOfSight = HasLineOfSight(targetPoint);
            }

            var tuning = new EnemyTuning
            {
                DetectionRange = def.detectionRange,
                LoseInterestRange = def.loseInterestRange,
                AttackRange = def.attackRange,
                Windup = def.windup,
                Recover = def.recover,
                Cooldown = def.cooldown,
            };

            if (agent != null && status != null)
            {
                agent.speed = def.moveSpeed * status.MoveSpeedMultiplier; // ice / gravity slow
            }

            EnemyIntent intent = machine.Tick(dt, perception, tuning);
            ApplyMovement(intent, dt);

            if (lookTarget != null)
            {
                lookTarget.position = targetPoint;
            }

            if (intent.BeginWindup)
            {
                if (def.windupFx != null)
                {
                    FxPool.Spawn(def.windupFx, transform.position + Vector3.up * 1.3f + transform.forward * 0.4f, Quaternion.identity, transform);
                }

                if (animatorDriver != null) animatorDriver.Trigger(AnimatorParams.AttackId);
                WindupStarted?.Invoke(this);
            }

            if (intent.ReleaseAttack)
            {
                ReleaseAttack(def);
            }
        }

        bool HasLineOfSight(Vector3 targetPoint)
        {
            Vector3 from = eye != null ? eye.position : transform.position + Vector3.up * 1.6f;
            return !Physics.Linecast(from, targetPoint, GameLayers.SightBlockingMask, QueryTriggerInteraction.Ignore);
        }

        void ApplyMovement(EnemyIntent intent, float dt)
        {
            if (agent == null || !agent.enabled || !agent.isOnNavMesh)
            {
                return;
            }

            if (intent.Move && target != null)
            {
                agent.isStopped = false;
                repathTimer -= dt;
                if (repathTimer <= 0f)
                {
                    agent.SetDestination(target.Position);
                    repathTimer = RepathInterval;
                }
            }
            else
            {
                agent.isStopped = true;
            }

            // The agent steers while chasing; during the telegraph the brain turns to face the target.
            agent.updateRotation = !intent.FaceTarget;
            if (intent.FaceTarget && target != null)
            {
                Vector3 to = target.Position - transform.position;
                to.y = 0f;
                if (to.sqrMagnitude > 0.0001f)
                {
                    Quaternion wanted = Quaternion.LookRotation(to);
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, wanted, agent.angularSpeed * dt);
                }
            }
        }

        void ReleaseAttack(EnemyDefinition def)
        {
            if (target == null || !target.IsAlive)
            {
                AttackReleased?.Invoke(this);
                return;
            }

            if (def.attackKind == EnemyAttackKind.Melee)
            {
                Vector3 to = target.Position - transform.position;
                to.y = 0f;
                bool inReach = to.magnitude <= def.attackRange + 0.6f;
                bool inFront = Vector3.Angle(transform.forward, to) <= def.meleeArc;
                if (inReach && inFront && target.Health != null)
                {
                    target.Health.ApplyDamage(new DamageInfo
                    {
                        Amount = def.damage,
                        Kind = DamageKind.Weapon,
                        Source = actor,
                        SourceTeam = actor.Team,
                        Point = target.Position + Vector3.up * TargetHeight,
                        Normal = -to.normalized,
                        Direction = to.normalized,
                    });
                }
            }
            else if (def.projectilePrefab != null)
            {
                Vector3 origin = muzzle != null ? muzzle.position : transform.position + Vector3.up * 1.3f + transform.forward * 0.6f;
                Vector3 aimPoint = target.Position + Vector3.up * TargetHeight;
                Vector3 direction = (aimPoint - origin).normalized;
                PooledFx shot = FxPool.Spawn(def.projectilePrefab, origin, Quaternion.LookRotation(direction));
                if (shot != null && shot.TryGetComponent(out EnemyProjectile projectile))
                {
                    projectile.Launch(origin, direction, def.projectileSpeed, def.damage, def.projectileRadius,
                        actor, actor.Team, GameLayers.EnemyShotMask);
                }
            }

            AttackReleased?.Invoke(this);
        }
    }
}
