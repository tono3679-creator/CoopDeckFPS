using System;
using System.Collections;
using System.Collections.Generic;
using CoopDeck.Cards;
using CoopDeck.Combat;
using CoopDeck.Core;
using CoopDeck.Effects;
using UnityEngine;

namespace CoopDeck.Skills
{
    /// <summary>Where a projectile skill landed (tests, analytics).</summary>
    public struct ProjectileImpact
    {
        public CardDefinition Card;
        public Vector3 Point;
        public Vector3 Normal;

        /// <summary>The enemy it hit directly, or null for the world.</summary>
        public Health Target;

        /// <summary>False when it ran out of range without hitting anything (no damage).</summary>
        public bool Hit;

        public float TimeInFlight;
    }

    /// <summary>What one cast did (tests, HUD feedback, analytics).</summary>
    public struct SkillCastReport
    {
        public CardDefinition Card;
        public Vector3 Point;
        public Health PrimaryTarget;
        public int EnemiesAffected;
        public int AlliesAffected;
        public float DamageDealt;
        public float Healed;

        /// <summary>True when the effects land later (impactDelay); the numbers above are then empty.</summary>
        public bool Delayed;
    }

    /// <summary>
    /// Executes a card's effects in the world. Targeting: a ray along the crosshair (Aim /
    /// AimPoint) or the caster (Self / AreaAroundSelf). Damage and effects are resolved first,
    /// then purely visual prefabs are played through <see cref="FxPool"/>.
    /// Projectile cards (<see cref="ProjectileSpec"/>) charge in the casting hand, launch along the
    /// crosshair after the cast motion's delay, fly as a swept sphere and resolve the same effects where
    /// they land. The flight is simulated here; the visual only follows it.
    /// It never touches the weapon, so skills and shooting run side by side.
    /// </summary>
    public class SkillCaster : MonoBehaviour
    {
        const float ChainDamageFactor = 0.75f;
        const float FxHeight = 1.1f;

        /// <summary>The visible projectile leaves the hand and joins the crosshair path within this distance.</summary>
        public const float ConvergeDistance = 6f;

        [Tooltip("Where beams start (the view-model muzzle for the local player).")]
        [SerializeField] Transform castOrigin;

        [Tooltip("The casting hand (the first-person left hand for the local player): hand charges and projectiles start at its palm.")]
        [SerializeField] Transform castHand;

        [Tooltip("With World Camera: the hand is drawn by this view-model camera, so its position is converted to the world view.")]
        [SerializeField] Camera viewModelCamera;

        [SerializeField] Camera worldCamera;

        [Header("Caster's view")]
        [Tooltip("The caster's own camera jolts as a projectile leaves the hand (CameraShaker.Kick): degrees, x < 0 tips the view up. " +
                 "Only the local player's caster has a world camera; other players see nothing.")]
        [SerializeField] Vector3 launchKickEuler = new Vector3(-0.9f, 0f, -0.6f);
        [Tooltip("The same jolt as an offset in the camera's space (metres; z < 0 = back toward the caster).")]
        [SerializeField] Vector3 launchKickOffset = new Vector3(0f, -0.004f, -0.018f);

        CameraShaker casterView;
        ICombatActor owner;
        IAimProvider aim;
        int rayMask = Physics.DefaultRaycastLayers;
        int areaMask = Physics.DefaultRaycastLayers;
        readonly List<Health> targets = new List<Health>();
        readonly HashSet<Health> visited = new HashSet<Health>();
        readonly HashSet<Health> impactShown = new HashSet<Health>();
        readonly List<Health> affectedEnemies = new List<Health>();

        struct PendingLaunch
        {
            public CardDefinition Card;
            public int UpgradeLevel;
            public float LaunchTime;
        }

        struct Flight
        {
            public CardDefinition Card;
            public int UpgradeLevel;
            public Vector3 Position;
            public Vector3 Direction;
            public float Speed;
            public float Radius;
            public float Travelled;
            public float MaxDistance;
            public float Age;
            public Vector3 VisualOffset;
            public Vector3 LastVisual;
            public IProjectileFx Visual;
        }

        readonly List<PendingLaunch> pending = new List<PendingLaunch>();
        readonly List<Flight> flights = new List<Flight>();
        static readonly RaycastHit[] SweepHits = new RaycastHit[16];

        /// <summary>Raised when a cast's effects have been applied (immediately, or after impactDelay).</summary>
        public event Action<SkillCastReport> EffectsApplied;

        /// <summary>A projectile left the hand (position = where the visible shot starts).</summary>
        public event Action<CardDefinition, Vector3> ProjectileLaunched;

        /// <summary>A projectile landed (or ran out of range).</summary>
        public event Action<ProjectileImpact> ProjectileEnded;

        public bool IsInitialized => owner != null && aim != null;

        /// <summary>Projectiles in the air.</summary>
        public int FlyingCount => flights.Count;

        /// <summary>Projectile casts waiting for the cast motion to launch them.</summary>
        public int PendingCount => pending.Count;

        public Transform CastHand => castHand;

        public void Initialize(ICombatActor ownerActor, IAimProvider aimProvider, int raycastMask, int areaQueryMask)
        {
            owner = ownerActor;
            aim = aimProvider;
            rayMask = raycastMask;
            areaMask = areaQueryMask;
        }

        public void SetCastOrigin(Transform origin)
        {
            castOrigin = origin;
        }

        /// <summary>The hand that charges and throws projectiles; with both cameras, a first-person hand is converted to the world view.</summary>
        public void SetCastHand(Transform hand, Camera viewModel = null, Camera world = null)
        {
            castHand = hand;
            viewModelCamera = viewModel;
            worldCamera = world;
        }

        /// <param name="upgradeLevel">The card copy's upgrade level; carried to the element hits (combo tier).</param>
        public SkillCastReport CastCard(CardDefinition card, int upgradeLevel = 0)
        {
            if (card == null || !IsInitialized)
            {
                return default;
            }

            if (card.projectile.IsProjectile)
            {
                return BeginProjectile(card, upgradeLevel);
            }

            Ray ray = aim.GetAimRay();
            Vector3 origin = castOrigin != null ? castOrigin.position : ray.origin;
            Health primary = null;
            Vector3 point;

            switch (card.targeting)
            {
                case CardTargeting.Aim:
                case CardTargeting.AimPoint:
                    point = ResolveAimPoint(card, ray, out primary);
                    PlayBeam(card.beamFx, origin, point);
                    break;
                default:
                    point = owner.Position;
                    break;
            }

            if (card.impactDelay > 0f)
            {
                PlayFx(card.telegraphFx, point + Vector3.up * 0.05f, Quaternion.identity, card.MaxRadius);
                StartCoroutine(ResolveLater(card, point, primary, upgradeLevel));
                return new SkillCastReport { Card = card, Point = point, PrimaryTarget = primary, Delayed = true };
            }

            return Resolve(card, point, primary, origin, upgradeLevel);
        }

        Vector3 ResolveAimPoint(CardDefinition card, Ray ray, out Health primary)
        {
            primary = null;
            if (Physics.Raycast(ray, out RaycastHit hit, card.range, rayMask, QueryTriggerInteraction.Ignore))
            {
                Health health = hit.collider.GetComponentInParent<Health>();
                if (IsEnemy(health))
                {
                    primary = health;
                }

                return hit.point;
            }

            Vector3 end = ray.origin + ray.direction * card.range;
            if (card.targeting == CardTargeting.AimPoint &&
                Physics.Raycast(end + Vector3.up * 0.5f, Vector3.down, out RaycastHit ground, 40f, rayMask, QueryTriggerInteraction.Ignore))
            {
                return ground.point; // aimed at the sky: drop the area onto the ground below
            }

            return end;
        }

        IEnumerator ResolveLater(CardDefinition card, Vector3 point, Health primary, int upgradeLevel)
        {
            yield return new WaitForSeconds(card.impactDelay);
            if (IsInitialized)
            {
                Resolve(card, point, primary, point + Vector3.up * 8f, upgradeLevel);
            }
        }

        // ------------------------------------------------------------------ projectiles

        readonly HashSet<CardDefinition> prewarmed = new HashSet<CardDefinition>();

        /// <summary>Instantiates a projectile card's effects into the pool up front (several shots and blasts can overlap).</summary>
        public void Prewarm(CardDefinition card)
        {
            if (card == null || !card.projectile.IsProjectile || !prewarmed.Add(card))
            {
                return;
            }

            FxPool.Prewarm(card.castFx, 2);
            FxPool.Prewarm(card.projectileFx, 4);
            FxPool.Prewarm(card.areaFx, 3);
            FxPool.Prewarm(card.impactFx, 3);
        }

        SkillCastReport BeginProjectile(CardDefinition card, int upgradeLevel)
        {
            PlayCastFx(card);
            pending.Add(new PendingLaunch { Card = card, UpgradeLevel = upgradeLevel, LaunchTime = Time.time + card.projectile.launchDelay });
            if (card.projectile.launchDelay <= 0f)
            {
                LaunchDue();
            }

            return new SkillCastReport { Card = card, Delayed = true };
        }

        /// <summary>The charge gathering in the casting hand while the cast motion brings the palm forward.</summary>
        void PlayCastFx(CardDefinition card)
        {
            if (card.castFx == null)
            {
                return;
            }

            Transform hand = castHand != null ? castHand : castOrigin;
            if (hand == null)
            {
                return;
            }

            Vector3 palm = PalmPoint(hand, AimDirectionNow(), viewModelCamera);
            Vector3 local = hand.InverseTransformPoint(palm);
            PooledFx fx = FxPool.Spawn(card.castFx, palm, hand.rotation, hand, hand.gameObject.layer);
            if (fx != null)
            {
                fx.transform.localPosition = local;
                fx.transform.localScale = Vector3.one;
            }
        }

        void Update()
        {
            if (pending.Count > 0)
            {
                LaunchDue();
            }

            if (flights.Count > 0)
            {
                StepProjectiles(Time.deltaTime);
            }
        }

        void LaunchDue()
        {
            float now = Time.time;
            for (int i = 0; i < pending.Count; i++)
            {
                if (pending[i].LaunchTime > now)
                {
                    continue;
                }

                PendingLaunch launch = pending[i];
                pending.RemoveAt(i);
                i--;
                Launch(launch.Card, launch.UpgradeLevel);
            }
        }

        void Launch(CardDefinition card, int upgradeLevel)
        {
            if (!IsInitialized)
            {
                return;
            }

            // The shot flies along the crosshair (exact aim); the visible fireball starts at the palm and joins
            // that path within ConvergeDistance, so it leaves the hand yet lands where the player aimed.
            Ray ray = aim.GetAimRay();
            Vector3 direction = ray.direction.normalized;
            Vector3 visualStart = VisibleCastPoint(direction, ray.origin);
            var flight = new Flight
            {
                Card = card,
                UpgradeLevel = upgradeLevel,
                Position = ray.origin,
                Direction = direction,
                Speed = card.projectile.speed,
                Radius = card.projectile.Radius,
                MaxDistance = Mathf.Max(1f, card.range),
                VisualOffset = visualStart - ray.origin,
                LastVisual = visualStart,
            };

            if (card.projectileFx != null)
            {
                PooledFx fx = FxPool.Spawn(card.projectileFx, visualStart, Quaternion.LookRotation(direction));
                if (fx != null)
                {
                    fx.transform.localScale = Vector3.one;
                    flight.Visual = fx.GetComponent<IProjectileFx>();
                    flight.Visual?.Launch(visualStart, VisualDirection(flight, 0f), card.projectile.VisualScale);
                }
            }

            flights.Add(flight);
            KickCasterView();
            ProjectileLaunched?.Invoke(card, visualStart);
        }

        /// <summary>The shot's recoil in the caster's own view (the strike's palm comes forward as it leaves).</summary>
        void KickCasterView()
        {
            if (worldCamera == null)
            {
                return;
            }

            if (casterView == null)
            {
                casterView = worldCamera.GetComponent<CameraShaker>();
            }

            if (casterView != null)
            {
                casterView.Kick(launchKickEuler, launchKickOffset);
            }
        }

        /// <summary>Moves every projectile by <paramref name="deltaTime"/> (public so a fixed network tick or a test can drive it).</summary>
        public void StepProjectiles(float deltaTime)
        {
            for (int i = flights.Count - 1; i >= 0; i--)
            {
                Flight f = flights[i];
                float step = f.Speed * deltaTime;
                f.Age += deltaTime;
                if (Sweep(f.Position, f.Direction, f.Radius, step, out Vector3 point, out Vector3 normal, out Health target))
                {
                    flights.RemoveAt(i);
                    Land(f, point, normal, target);
                    continue;
                }

                f.Position += f.Direction * step;
                f.Travelled += step;
                if (f.Travelled >= f.MaxDistance)
                {
                    flights.RemoveAt(i);
                    f.Visual?.End(false);
                    ProjectileEnded?.Invoke(new ProjectileImpact { Card = f.Card, Point = f.Position, Normal = -f.Direction, TimeInFlight = f.Age });
                    continue;
                }

                if (f.Visual != null)
                {
                    Vector3 visual = f.Position + f.VisualOffset * VisualOffsetWeight(f.Travelled);
                    Vector3 moved = visual - f.LastVisual;
                    f.Visual.Fly(visual, moved.sqrMagnitude > 1e-8f ? moved : f.Direction);
                    f.LastVisual = visual;
                }

                flights[i] = f;
            }
        }

        /// <summary>1 at the hand .. 0 once the visible shot has joined the crosshair path.</summary>
        public static float VisualOffsetWeight(float travelled)
        {
            return 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(travelled / ConvergeDistance));
        }

        static Vector3 VisualDirection(in Flight f, float travelled)
        {
            // Direction of the visible path just after `travelled` (it bends from the hand onto the crosshair line).
            float ahead = travelled + 0.5f;
            Vector3 now = f.Position + f.Direction * travelled + f.VisualOffset * VisualOffsetWeight(travelled);
            Vector3 next = f.Position + f.Direction * ahead + f.VisualOffset * VisualOffsetWeight(ahead);
            Vector3 d = next - now;
            return d.sqrMagnitude > 1e-8f ? d.normalized : f.Direction;
        }

        /// <summary>
        /// Nearest thing the sphere touches along the step: the world, or a living enemy. Allies, the caster
        /// and the dead are passed through. Uses a shared buffer (no allocation).
        /// </summary>
        bool Sweep(Vector3 from, Vector3 direction, float radius, float distance, out Vector3 point, out Vector3 normal, out Health target)
        {
            point = default;
            normal = default;
            target = null;
            int count = Physics.SphereCastNonAlloc(from, radius, direction, SweepHits, distance, rayMask, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = SweepHits[i];
                Health health = hit.collider.GetComponentInParent<Health>();
                if (health != null && !IsEnemy(health))
                {
                    continue; // allies, the caster and dead characters do not stop the shot
                }

                if (hit.distance >= best)
                {
                    continue;
                }

                best = hit.distance;
                found = true;
                target = health;
                if (hit.distance <= 0f && hit.point == Vector3.zero)
                {
                    point = from; // already touching at the start of the step
                    normal = -direction;
                }
                else
                {
                    point = hit.point;
                    normal = hit.normal;
                }
            }

            return found;
        }

        void Land(in Flight f, Vector3 point, Vector3 normal, Health target)
        {
            f.Visual?.End(true);
            bool onCharacter = target != null;
            Vector3 center = onCharacter ? point : point + normal * 0.05f;
            Resolve(f.Card, center, target, point - f.Direction, f.UpgradeLevel, normal, onCharacter);
            ProjectileEnded?.Invoke(new ProjectileImpact
            {
                Card = f.Card,
                Point = point,
                Normal = normal,
                Target = target,
                Hit = true,
                TimeInFlight = f.Age,
            });
        }

        Vector3 AimDirectionNow()
        {
            return aim != null ? aim.GetAimRay().direction.normalized : transform.forward;
        }

        /// <summary>Where the visible projectile starts, in the world view: the palm, converted from the view-model camera.</summary>
        Vector3 VisibleCastPoint(Vector3 aimDirection, Vector3 fallback)
        {
            Transform hand = castHand != null ? castHand : castOrigin;
            if (hand == null)
            {
                return fallback;
            }

            return ToWorldView(PalmPoint(hand, aimDirection, viewModelCamera));
        }

        /// <summary>
        /// In front of the palm: along the hand (wrist to fingers) and toward the aim. Seen from the player's own eye
        /// (a first-person hand) it also sits a little toward the screen centre, so the hand does not hide it.
        /// </summary>
        static Vector3 PalmPoint(Transform hand, Vector3 aimDirection, Camera eye)
        {
            Vector3 along = hand.parent != null ? hand.position - hand.parent.position : Vector3.zero;
            along = along.sqrMagnitude > 1e-6f ? along.normalized : aimDirection;
            Vector3 point = hand.position + along * 0.05f + aimDirection * 0.06f;
            if (eye != null)
            {
                point += aimDirection * 0.04f + eye.transform.right * 0.045f + eye.transform.up * 0.01f;
            }

            return point;
        }

        /// <summary>
        /// The view model is drawn by its own camera (a narrower field of view from the same eye). A point there
        /// is moved to the world-camera position that shows up at the same spot on screen, at the same depth.
        /// </summary>
        Vector3 ToWorldView(Vector3 point)
        {
            if (viewModelCamera == null || worldCamera == null)
            {
                return point;
            }

            Vector3 viewport = viewModelCamera.WorldToViewportPoint(point);
            return worldCamera.ViewportToWorldPoint(viewport);
        }

        void OnDisable()
        {
            foreach (Flight f in flights)
            {
                f.Visual?.End(false);
            }

            flights.Clear();
            pending.Clear();
        }

        // ------------------------------------------------------------------ resolution

        SkillCastReport Resolve(CardDefinition card, Vector3 center, Health primary, Vector3 origin, int upgradeLevel)
        {
            return Resolve(card, center, primary, origin, upgradeLevel, null, false);
        }

        /// <param name="surfaceNormal">Set for projectiles: the blast is turned to the surface it hit (up on characters).</param>
        SkillCastReport Resolve(CardDefinition card, Vector3 center, Health primary, Vector3 origin, int upgradeLevel,
            Vector3? surfaceNormal, bool onCharacter)
        {
            var report = new SkillCastReport { Card = card, Point = center, PrimaryTarget = primary };
            CombatStatus ownerStatus = owner.Status;
            float damageMultiplier = ownerStatus != null ? ownerStatus.Model.OutgoingDamageMultiplier : 1f;
            visited.Clear();
            impactShown.Clear();
            affectedEnemies.Clear();

            foreach (SkillEffectSpec effect in card.effects)
            {
                switch (effect.kind)
                {
                    case SkillEffectKind.Damage:
                        ApplyDamage(card, effect, primary, damageMultiplier, ref report);
                        break;

                    case SkillEffectKind.AreaDamage:
                        foreach (Health enemy in Enemies(center, effect.radius))
                        {
                            report.DamageDealt += Hit(card, enemy, effect.amount * damageMultiplier, origin);
                            Count(enemy, ref report, isEnemy: true);
                        }

                        break;

                    case SkillEffectKind.DamageOverTime:
                        foreach (Health enemy in EnemiesOrPrimary(center, effect.radius, primary))
                        {
                            CombatStatus status = enemy.GetComponent<CombatStatus>();
                            if (status != null)
                            {
                                status.AddDamageOverTime(owner, owner.Team, card.element, effect.amount * damageMultiplier, effect.duration);
                                Count(enemy, ref report, isEnemy: true);
                            }
                        }

                        break;

                    case SkillEffectKind.Heal:
                        foreach (Health ally in Allies(center, effect.radius))
                        {
                            report.Healed += ally.Heal(effect.amount);
                            PlayAura(card, ally);
                            Count(ally, ref report, isEnemy: false);
                        }

                        break;

                    case SkillEffectKind.Shield:
                    case SkillEffectKind.DamageBuff:
                        foreach (Health ally in Allies(center, effect.radius))
                        {
                            CombatStatus status = ally.GetComponent<CombatStatus>();
                            if (status == null)
                            {
                                continue;
                            }

                            if (effect.kind == SkillEffectKind.Shield)
                            {
                                status.AddShield(effect.amount, effect.duration);
                            }
                            else
                            {
                                status.AddDamageBuff(effect.amount, effect.duration);
                            }

                            PlayAura(card, ally);
                            Count(ally, ref report, isEnemy: false);
                        }

                        break;

                    case SkillEffectKind.Vulnerability:
                        foreach (Health enemy in EnemiesOrPrimary(center, effect.radius, primary))
                        {
                            CombatStatus status = enemy.GetComponent<CombatStatus>();
                            if (status != null)
                            {
                                status.AddVulnerability(effect.amount, effect.duration);
                                PlayFx(card.impactFx, enemy.transform.position + Vector3.up * FxHeight, Quaternion.identity, 0f);
                                Count(enemy, ref report, isEnemy: true);
                            }
                        }

                        break;

                    case SkillEffectKind.Pull:
                        foreach (Health enemy in Enemies(center, effect.radius))
                        {
                            if (enemy.TryGetComponent(out ForceReceiver receiver))
                            {
                                receiver.Pull(center, effect.pullSpeed, effect.duration);
                                Count(enemy, ref report, isEnemy: true);
                            }
                        }

                        break;
                }
            }

            float radius = card.MaxRadius;
            if (surfaceNormal.HasValue)
            {
                // Projectile: the blast sits exactly where it hit, turned to the surface; on a character it stands
                // upright and faces back toward the shot (its ring is a vertical impact ring, not a halo at chest height).
                Vector3 up = onCharacter ? FacingBack(origin - center) : surfaceNormal.Value.sqrMagnitude < 1e-6f ? Vector3.up : surfaceNormal.Value.normalized;
                GameObject blast = radius > 0f ? card.areaFx : primary == null ? card.impactFx : null;
                PooledFx fx = PlayFx(blast, center, Quaternion.FromToRotation(Vector3.up, up), radius);
                if (fx != null && fx.TryGetComponent(out ISurfaceFx surface))
                {
                    surface.SetSurface(up, onCharacter);
                }
            }
            else if (radius > 0f)
            {
                PlayFx(card.areaFx, center + Vector3.up * 0.1f, Quaternion.identity, radius);
            }
            else if (primary == null && card.targeting != CardTargeting.Self)
            {
                PlayFx(card.impactFx, center, Quaternion.identity, 0f);
            }

            RaiseElementHits(card, upgradeLevel);
            EffectsApplied?.Invoke(report);
            return report;
        }

        /// <summary>
        /// One element hit per enemy this cast reached (damage, burn, mark or pull), after all
        /// of the card's damage was applied. The combo system listens; the caster does not know it.
        /// </summary>
        void RaiseElementHits(CardDefinition card, int upgradeLevel)
        {
            if (card.element == Element.None || affectedEnemies.Count == 0)
            {
                return;
            }

            int ownerId = CombatantRegistry.IdOf(owner);
            Health[] hit = affectedEnemies.ToArray(); // a combo may cause further skill-free damage
            affectedEnemies.Clear();
            foreach (Health enemy in hit)
            {
                if (enemy == null)
                {
                    continue;
                }

                CombatEvents.RaiseElementHit(new ElementHit(enemy, card.element, owner, ownerId, owner.Team,
                    enemy.transform.position + Vector3.up * FxHeight, upgradeLevel));
            }
        }

        void ApplyDamage(CardDefinition card, SkillEffectSpec effect, Health primary, float multiplier, ref SkillCastReport report)
        {
            if (primary == null || !primary.IsAlive)
            {
                return;
            }

            Vector3 from = castOrigin != null ? castOrigin.position : owner.Position;
            report.DamageDealt += Hit(card, primary, effect.amount * multiplier, from);
            Count(primary, ref report, isEnemy: true); // counted even if this hit killed it: the killing blow can finish a combo

            // Chain: jump to the nearest enemy not hit yet, with reduced damage.
            var chained = new HashSet<Health> { primary };
            Health last = primary;
            for (int i = 0; i < effect.chainCount; i++)
            {
                Health next = NearestEnemy(last.transform.position, effect.chainRadius, chained);
                if (next == null)
                {
                    break;
                }

                PlayBeam(card.beamFx, last.transform.position + Vector3.up * FxHeight, next.transform.position + Vector3.up * FxHeight);
                report.DamageDealt += Hit(card, next, effect.amount * multiplier * ChainDamageFactor, last.transform.position);
                Count(next, ref report, isEnemy: true);
                chained.Add(next);
                last = next;
            }
        }

        float Hit(CardDefinition card, Health target, float amount, Vector3 from)
        {
            Vector3 point = target.transform.position + Vector3.up * FxHeight;
            DamageResult result = target.ApplyDamage(new DamageInfo
            {
                Amount = amount,
                Element = card.element,
                Kind = DamageKind.Skill,
                Source = owner,
                SourceTeam = owner.Team,
                Point = point,
                Normal = Vector3.up,
                Direction = (point - from).normalized,
                HeadshotMultiplier = 1f,
            });
            if (impactShown.Add(target))
            {
                PlayImpact(card, target, point); // once per enemy per cast, even when two effects hit it
            }

            return result.Damage;
        }

        static void PlayImpact(CardDefinition card, Health target, Vector3 point)
        {
            if (card.impactFx == null)
            {
                return;
            }

            Transform parent = card.attachImpactFx && target != null ? target.transform : null;
            PooledFx fx = FxPool.Spawn(card.impactFx, point, Quaternion.identity, parent);
            if (fx != null)
            {
                fx.transform.localScale = Vector3.one;
            }
        }

        void Count(Health target, ref SkillCastReport report, bool isEnemy)
        {
            if (!visited.Add(target))
            {
                return;
            }

            if (isEnemy)
            {
                report.EnemiesAffected++;
                affectedEnemies.Add(target);
            }
            else
            {
                report.AlliesAffected++;
            }
        }

        bool IsEnemy(Health health)
        {
            return health != null && health.IsAlive && health.Team != owner.Team && DamageRules.CanDamage(owner.Team, health.Team);
        }

        List<Health> Enemies(Vector3 center, float radius)
        {
            Collect(center, radius, enemies: true);
            return new List<Health>(targets);
        }

        List<Health> EnemiesOrPrimary(Vector3 center, float radius, Health primary)
        {
            if (radius > 0f)
            {
                return Enemies(center, radius);
            }

            return primary != null && primary.IsAlive ? new List<Health> { primary } : new List<Health>();
        }

        /// <summary>Allies in the radius; radius 0 means the caster alone. The caster is always included.</summary>
        List<Health> Allies(Vector3 center, float radius)
        {
            targets.Clear();
            if (radius > 0f)
            {
                Collect(center, radius, enemies: false);
            }

            Health self = owner.Health;
            if (self != null && self.IsAlive && !targets.Contains(self))
            {
                targets.Add(self);
            }

            return new List<Health>(targets);
        }

        void Collect(Vector3 center, float radius, bool enemies)
        {
            targets.Clear();
            if (radius <= 0f)
            {
                return;
            }

            foreach (Collider collider in Physics.OverlapSphere(center, radius, areaMask, QueryTriggerInteraction.Ignore))
            {
                Health health = collider.GetComponentInParent<Health>();
                if (health == null || !health.IsAlive || targets.Contains(health))
                {
                    continue;
                }

                bool isEnemy = IsEnemy(health);
                bool isAlly = health.Team == owner.Team;
                if (enemies ? isEnemy : isAlly)
                {
                    targets.Add(health);
                }
            }
        }

        Health NearestEnemy(Vector3 from, float radius, HashSet<Health> exclude)
        {
            Collect(from, radius, enemies: true);
            Health best = null;
            float bestDistance = float.MaxValue;
            foreach (Health candidate in targets)
            {
                if (exclude.Contains(candidate))
                {
                    continue;
                }

                float distance = (candidate.transform.position - from).sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = candidate;
                }
            }

            return best;
        }

        void PlayAura(CardDefinition card, Health ally)
        {
            PlayFx(card.auraFx, ally.transform.position + Vector3.up * 0.05f, Quaternion.identity, 0f);
        }

        static void PlayBeam(GameObject prefab, Vector3 from, Vector3 to)
        {
            if (prefab == null)
            {
                return;
            }

            PooledFx beam = FxPool.Spawn(prefab, from, Quaternion.identity);
            if (beam != null && beam.TryGetComponent(out TracerFx tracer))
            {
                tracer.Play(from, to);
            }
        }

        /// <param name="radius">
        /// When above 0, the prefab is fitted to it: an <see cref="IRadiusFx"/> sizes its own layers, any other
        /// prefab (authored for a 1 m radius) is scaled as a whole.
        /// </param>
        /// <summary>The horizontal direction back toward where a shot came from (up when it came straight down).</summary>
        static Vector3 FacingBack(Vector3 towardShooter)
        {
            Vector3 flat = Vector3.ProjectOnPlane(towardShooter, Vector3.up);
            return flat.sqrMagnitude > 1e-6f ? flat.normalized : Vector3.up;
        }

        static PooledFx PlayFx(GameObject prefab, Vector3 position, Quaternion rotation, float radius)
        {
            if (prefab == null)
            {
                return null;
            }

            PooledFx fx = FxPool.Spawn(prefab, position, rotation);
            if (fx == null)
            {
                return null;
            }

            if (fx.TryGetComponent(out IRadiusFx sized))
            {
                fx.transform.localScale = Vector3.one;
                if (radius > 0f)
                {
                    sized.SetRadius(radius);
                }
            }
            else
            {
                fx.transform.localScale = radius > 0f ? Vector3.one * radius : Vector3.one;
            }

            return fx;
        }
    }
}
