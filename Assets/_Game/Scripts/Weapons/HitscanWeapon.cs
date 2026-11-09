using System;
using System.Collections.Generic;
using CoopDeck.Combat;
using CoopDeck.Core;
using UnityEngine;

namespace CoopDeck.Weapons
{
    public readonly struct ShotEvent
    {
        public ShotEvent(Vector3 muzzle, Vector3 end, bool hitSomething, Vector3 normal, bool hitDamageable, DamageResult result)
        {
            Muzzle = muzzle;
            End = end;
            HitSomething = hitSomething;
            Normal = normal;
            HitDamageable = hitDamageable;
            Result = result;
        }

        public Vector3 Muzzle { get; }
        public Vector3 End { get; }
        public bool HitSomething { get; }
        public Vector3 Normal { get; }
        public bool HitDamageable { get; }
        public DamageResult Result { get; }
    }

    /// <summary>A <see cref="ChargeBurstSpec"/> discharge: the primary target, where it was hit, and the chained targets.</summary>
    public readonly struct ChargeBurstReport
    {
        public ChargeBurstReport(Health primary, Vector3 point, IReadOnlyList<Health> chained, IReadOnlyList<Vector3> chainPoints, float damage)
        {
            Primary = primary;
            Point = point;
            Chained = chained;
            ChainPoints = chainPoints;
            Damage = damage;
        }

        public Health Primary { get; }
        public Vector3 Point { get; }

        /// <summary>Chained targets (valid during the event only; the list is reused).</summary>
        public IReadOnlyList<Health> Chained { get; }

        public IReadOnlyList<Vector3> ChainPoints { get; }

        /// <summary>Total damage the discharge dealt (primary + chain).</summary>
        public float Damage { get; }
    }

    /// <summary>
    /// Instant-hit gun. Aims along the owner's <see cref="IAimProvider"/> ray (the crosshair),
    /// applies damage through <see cref="IDamageable"/>, and reports every shot as an event for
    /// effects, HUD and (later) network replication. It draws nothing itself.
    /// </summary>
    public class HitscanWeapon : MonoBehaviour
    {
        const int MaxShotsPerFrame = 4;

        [SerializeField] WeaponDefinition definition;
        [Tooltip("Where tracers start. Usually the view-model muzzle for the local player.")]
        [SerializeField] Transform muzzle;
        [SerializeField] bool autoReloadWhenEmpty = true;

        WeaponState state;
        IAimProvider aim;
        ICombatActor owner;
        TeamId ownerTeam = TeamId.Players;
        int hitMask = Physics.DefaultRaycastLayers;
        bool triggerHeld;
        bool triggerPressed;
        float movementSpread;
        bool airborne;
        System.Random random = new System.Random(1234);
        ChargeBurstModel chargeBurst;
        bool chargeBurstBuilt;
        readonly List<Health> burstChain = new List<Health>(4);
        readonly List<Vector3> burstChainPoints = new List<Vector3>(4);
        readonly Collider[] overlapBuffer = new Collider[32];
        readonly List<SplashCandidate> splashCandidates = new List<SplashCandidate>(8);

        public event Action<ShotEvent> Fired;
        public event Action ReloadStarted;
        public event Action ReloadFinished;
        public event Action AmmoChanged;

        /// <summary>The on-hit ability discharged (FX, HUD).</summary>
        public event Action<ChargeBurstReport> ChargeBurstTriggered;

        public WeaponDefinition Definition => definition;
        public WeaponState State => state ??= definition != null ? WeaponState.FromDefinition(definition) : null;

        /// <summary>The weapon's consecutive-hit ability, or null when its definition has none.</summary>
        public ChargeBurstModel ChargeBurst
        {
            get
            {
                if (!chargeBurstBuilt)
                {
                    chargeBurstBuilt = true;
                    chargeBurst = definition != null && definition.chargeBurst.enabled ? new ChargeBurstModel(definition.chargeBurst) : null;
                }

                return chargeBurst;
            }
        }
        public Transform Muzzle => muzzle;
        public bool IsReloading => State != null && State.IsReloading;

        /// <summary>Current cone radius in degrees, including bloom and movement penalties.</summary>
        public float CurrentSpread
        {
            get
            {
                if (definition == null || State == null)
                {
                    return 0f;
                }

                float spread = definition.baseSpread + State.Bloom + definition.movingSpread * movementSpread;
                if (airborne)
                {
                    spread += definition.airborneSpread;
                }

                return spread;
            }
        }

        public void Initialize(ICombatActor ownerActor, IAimProvider aimProvider, TeamId team, int shotMask)
        {
            owner = ownerActor;
            aim = aimProvider;
            ownerTeam = team;
            hitMask = shotMask;
            ApplyRecoilRecovery();
        }

        public void SetDefinition(WeaponDefinition newDefinition)
        {
            definition = newDefinition;
            state = null;
            chargeBurst = null;
            chargeBurstBuilt = false;
            ApplyRecoilRecovery();
            AmmoChanged?.Invoke();
        }

        void ApplyRecoilRecovery()
        {
            aim?.SetRecoilRecovery(definition != null ? definition.recoilRecoverySeconds : 0f);
        }

        public void SetMuzzle(Transform newMuzzle)
        {
            muzzle = newMuzzle;
        }

        /// <summary>Seeds spread randomness. The host will seed this per shot sequence for replication.</summary>
        public void SetRandomSeed(int seed)
        {
            random = new System.Random(seed);
        }

        public void SetTrigger(bool held, bool pressedThisFrame)
        {
            triggerHeld = held;
            triggerPressed = pressedThisFrame;
        }

        /// <param name="speedFraction">0 = standing still, 1 = full run speed.</param>
        public void SetMovementState(float speedFraction, bool isAirborne)
        {
            movementSpread = Mathf.Clamp01(speedFraction);
            airborne = isAirborne;
        }

        public bool RequestReload()
        {
            if (State == null || !State.StartReload())
            {
                return false;
            }

            ReloadStarted?.Invoke();
            return true;
        }

        /// <summary>Advances the weapon one step. Call once per frame from the owning character.</summary>
        public void Tick(float deltaTime)
        {
            WeaponState s = State;
            if (s == null || aim == null)
            {
                return;
            }

            if (s.Tick(deltaTime, triggerHeld))
            {
                ReloadFinished?.Invoke();
                AmmoChanged?.Invoke();
            }

            ChargeBurst?.Tick(deltaTime);

            bool wantsFire = definition.automatic ? triggerHeld : triggerPressed;
            int shots = 0;
            while (wantsFire && shots < MaxShotsPerFrame && s.TryFire())
            {
                FireOnce(s.ShotsSinceTriggerDown == 1);
                shots++;
                if (!definition.automatic)
                {
                    break;
                }
            }

            if (shots > 0)
            {
                AmmoChanged?.Invoke();
            }

            if (autoReloadWhenEmpty && s.NeedsReload && !s.IsCoolingDown)
            {
                RequestReload();
            }

            triggerPressed = false;
        }

        void FireOnce(bool firstShot)
        {
            Ray aimRay = aim.GetAimRay();
            Vector3 muzzlePosition = muzzle != null ? muzzle.position : aimRay.origin;
            float spread = CurrentSpread;

            // Attack buffs (skill cards) raise weapon damage too.
            CombatStatus ownerStatus = owner != null ? owner.Status : null;
            float damageMultiplier = ownerStatus != null ? ownerStatus.Model.OutgoingDamageMultiplier : 1f;

            for (int pellet = 0; pellet < definition.pelletsPerShot; pellet++)
            {
                Vector3 direction = ApplySpread(aimRay.direction, spread);
                var ray = new Ray(aimRay.origin, direction);
                bool hitSomething = Physics.Raycast(ray, out RaycastHit hit, definition.range, hitMask, QueryTriggerInteraction.Ignore);

                Vector3 end = hitSomething ? hit.point : ray.origin + direction * definition.range;
                Vector3 normal = hitSomething ? hit.normal : -direction;
                DamageResult result = default;
                bool hitDamageable = false;

                if (hitSomething)
                {
                    IDamageable target = hit.collider.GetComponentInParent<IDamageable>();
                    if (target != null)
                    {
                        hitDamageable = true;
                        var info = new DamageInfo
                        {
                            Amount = definition.damage * damageMultiplier * definition.FalloffAt(hit.distance),
                            Element = definition.element,
                            Kind = DamageKind.Weapon,
                            Source = owner,
                            SourceTeam = ownerTeam,
                            Point = hit.point,
                            Normal = hit.normal,
                            Direction = direction,
                            HeadshotMultiplier = definition.headshotMultiplier,
                        };
                        result = target.ApplyDamage(info);
                    }
                }

                Fired?.Invoke(new ShotEvent(muzzlePosition, end, hitSomething, normal, hitDamageable, result));

                if (hitDamageable && result.Applied && ChargeBurst != null)
                {
                    RegisterChargeHit(hit, direction, damageMultiplier, result.Killed);
                }
            }

            float kickScale = firstShot ? definition.firstShotRecoilMultiplier : 1f;
            float yaw = ((float)random.NextDouble() * 2f - 1f) * definition.recoilYaw;
            aim.AddRecoil(new Vector2(yaw, definition.recoilPitch) * kickScale);
        }

        // ---------------------------------------------------------------- on-hit ability (帯電連射)

        void RegisterChargeHit(RaycastHit hit, Vector3 direction, float damageMultiplier, bool killed)
        {
            Health health = hit.collider.GetComponentInParent<Health>();
            if (health == null)
            {
                return;
            }

            if (killed || !health.IsAlive)
            {
                chargeBurst.ForgetTarget(health); // the charge was on this target: it is gone with it
                return;
            }

            if (chargeBurst.RegisterHit(health))
            {
                ReleaseChargeBurst(health, hit.point, direction, damageMultiplier);
            }
        }

        /// <summary>
        /// The discharge: bonus damage to the primary target and, for a Chain, the nearest hostile(s) in range one
        /// after another, for a Splash, the nearest hostiles within the radius of the hit (IGNIS-27 火種刻印); all
        /// through <see cref="Health.ApplyDamage"/> with <see cref="DamageFlags.WeaponProc"/>; then one
        /// element hit per target into the existing combo system (aura length and the 3 s combo
        /// cooldown stay the combo system's). Never builds stacks itself: only bullets do.
        /// </summary>
        void ReleaseChargeBurst(Health primary, Vector3 point, Vector3 direction, float damageMultiplier)
        {
            ChargeBurstSpec spec = definition.chargeBurst;
            burstChain.Clear();
            burstChainPoints.Clear();
            float dealt = Proc(primary, spec.primaryDamage * damageMultiplier, point, direction, spec.element);

            if (spec.shape == ChargeBurstShape.Splash)
            {
                // IGNIS-27 火種刻印: a small explosion on the target, the nearest hostiles within the radius.
                CollectSplashTargets(point, spec.splashRadius, spec.splashTargets, primary);
                for (int i = 0; i < burstChain.Count; i++)
                {
                    dealt += Proc(burstChain[i], spec.splashDamage * damageMultiplier, burstChainPoints[i], (burstChainPoints[i] - point).normalized, spec.element);
                }
            }
            else
            {
                Vector3 from = primary.transform.position;
                for (int i = 0; i < spec.chainTargets; i++)
                {
                    if (!NearestChainTarget(from, spec.chainRange, primary, out Health next, out Vector3 nextPoint))
                    {
                        break;
                    }

                    dealt += Proc(next, spec.chainDamage * damageMultiplier, nextPoint, (nextPoint - point).normalized, spec.element);
                    burstChain.Add(next);
                    burstChainPoints.Add(nextPoint);
                    from = next.transform.position;
                }
            }

            // The existing damage over time (a burn, for fire) on every target: CombatStatus ticks it as status damage,
            // which is not a bullet, so it never adds stacks.
            ApplyStatus(primary, spec, damageMultiplier);
            for (int i = 0; i < burstChain.Count; i++)
            {
                ApplyStatus(burstChain[i], spec, damageMultiplier);
            }

            if (spec.element != Element.None)
            {
                int ownerId = owner != null ? CombatantRegistry.IdOf(owner) : 0;
                CombatEvents.RaiseElementHit(new ElementHit(primary, spec.element, owner, ownerId, ownerTeam, point, 0));
                for (int i = 0; i < burstChain.Count; i++)
                {
                    if (burstChain[i] != null)
                    {
                        CombatEvents.RaiseElementHit(new ElementHit(burstChain[i], spec.element, owner, ownerId, ownerTeam, burstChainPoints[i], 0));
                    }
                }
            }

            ChargeBurstTriggered?.Invoke(new ChargeBurstReport(primary, point, burstChain, burstChainPoints, dealt));
        }

        void ApplyStatus(Health target, ChargeBurstSpec spec, float damageMultiplier)
        {
            if (target == null || !target.IsAlive || spec.statusDamagePerSecond <= 0f || spec.statusDuration <= 0f)
            {
                return;
            }

            CombatStatus status = target.GetComponent<CombatStatus>();
            if (status != null)
            {
                Element element = spec.element != Element.None ? spec.element : Element.Fire;
                status.AddDamageOverTime(owner, ownerTeam, element, spec.statusDamagePerSecond * damageMultiplier, spec.statusDuration);
            }
        }

        /// <summary>
        /// Splash targets: living hostiles whose colliders come within <paramref name="radius"/> of the burst point,
        /// nearest first, at most <paramref name="max"/>, never the primary target twice. Fills burstChain / burstChainPoints.
        /// </summary>
        void CollectSplashTargets(Vector3 point, float radius, int max, Health primary)
        {
            splashCandidates.Clear();
            if (radius <= 0f || max <= 0)
            {
                return;
            }

            int count = Physics.OverlapSphereNonAlloc(point, radius, overlapBuffer, hitMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider c = overlapBuffer[i];
                Health health = c != null ? c.GetComponentInParent<Health>() : null;
                if (health == null || health == primary || !health.IsAlive || !DamageRules.CanDamage(ownerTeam, health.Team))
                {
                    continue;
                }

                float distance = (c.ClosestPoint(point) - point).sqrMagnitude;
                int existing = -1;
                for (int k = 0; k < splashCandidates.Count; k++)
                {
                    if (splashCandidates[k].Target == health)
                    {
                        existing = k;
                        break;
                    }
                }

                if (existing >= 0)
                {
                    if (distance < splashCandidates[existing].Distance)
                    {
                        splashCandidates[existing] = new SplashCandidate(health, distance, c.bounds.center);
                    }

                    continue;
                }

                splashCandidates.Add(new SplashCandidate(health, distance, c.bounds.center));
            }

            splashCandidates.Sort(ByDistance);
            for (int i = 0; i < splashCandidates.Count && burstChain.Count < max; i++)
            {
                burstChain.Add(splashCandidates[i].Target);
                burstChainPoints.Add(splashCandidates[i].Center);
            }
        }

        static readonly Comparison<SplashCandidate> ByDistance = (a, b) => a.Distance.CompareTo(b.Distance);

        readonly struct SplashCandidate
        {
            public SplashCandidate(Health target, float distance, Vector3 center)
            {
                Target = target;
                Distance = distance;
                Center = center;
            }

            public Health Target { get; }
            public float Distance { get; }
            public Vector3 Center { get; }
        }

        float Proc(Health target, float amount, Vector3 point, Vector3 direction, Element element)
        {
            if (target == null || amount <= 0f)
            {
                return 0f;
            }

            DamageResult result = target.ApplyDamage(new DamageInfo
            {
                Amount = amount,
                Element = element,
                Kind = DamageKind.Weapon,
                Flags = DamageFlags.WeaponProc,
                Source = owner,
                SourceTeam = ownerTeam,
                Point = point,
                Normal = -direction,
                Direction = direction,
                HeadshotMultiplier = 1f,
            });
            return result.Damage;
        }

        bool NearestChainTarget(Vector3 from, float range, Health primary, out Health best, out Vector3 bestPoint)
        {
            best = null;
            bestPoint = default;
            if (range <= 0f)
            {
                return false;
            }

            float bestDistance = float.MaxValue;
            int count = Physics.OverlapSphereNonAlloc(from, range, overlapBuffer, hitMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider c = overlapBuffer[i];
                Health health = c != null ? c.GetComponentInParent<Health>() : null;
                if (health == null || health == primary || burstChain.Contains(health) || !health.IsAlive || !DamageRules.CanDamage(ownerTeam, health.Team))
                {
                    continue;
                }

                Vector3 center = c.bounds.center;
                float distance = (center - from).sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = health;
                    bestPoint = center;
                }
            }

            return best != null;
        }

        Vector3 ApplySpread(Vector3 direction, float spreadDegrees)
        {
            if (spreadDegrees <= 0f)
            {
                return direction;
            }

            // Uniform point in a disc, projected onto a cone of the given radius.
            double angle = random.NextDouble() * Math.PI * 2.0;
            float radius = Mathf.Sqrt((float)random.NextDouble()) * Mathf.Tan(spreadDegrees * Mathf.Deg2Rad);
            var offset = new Vector3((float)Math.Cos(angle) * radius, (float)Math.Sin(angle) * radius, 1f);
            return (Quaternion.LookRotation(direction) * offset).normalized;
        }
    }
}
