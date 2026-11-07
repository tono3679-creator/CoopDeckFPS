using System;
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

        public event Action<ShotEvent> Fired;
        public event Action ReloadStarted;
        public event Action ReloadFinished;
        public event Action AmmoChanged;

        public WeaponDefinition Definition => definition;
        public WeaponState State => state ??= definition != null ? WeaponState.FromDefinition(definition) : null;
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
        }

        public void SetDefinition(WeaponDefinition newDefinition)
        {
            definition = newDefinition;
            state = null;
            AmmoChanged?.Invoke();
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
                            Amount = definition.damage * damageMultiplier *
                                     DamageRules.DistanceFalloff(hit.distance, definition.falloffStart, definition.range, definition.minDamageFactor),
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
            }

            float kickScale = firstShot ? definition.firstShotRecoilMultiplier : 1f;
            float yaw = ((float)random.NextDouble() * 2f - 1f) * definition.recoilYaw;
            aim.AddRecoil(new Vector2(yaw, definition.recoilPitch) * kickScale);
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
