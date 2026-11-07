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
    /// It never touches the weapon, so skills and shooting run side by side.
    /// </summary>
    public class SkillCaster : MonoBehaviour
    {
        const float ChainDamageFactor = 0.75f;
        const float FxHeight = 1.1f;

        [Tooltip("Where beams start (the view-model muzzle for the local player).")]
        [SerializeField] Transform castOrigin;

        ICombatActor owner;
        IAimProvider aim;
        int rayMask = Physics.DefaultRaycastLayers;
        int areaMask = Physics.DefaultRaycastLayers;
        readonly List<Health> targets = new List<Health>();
        readonly HashSet<Health> visited = new HashSet<Health>();
        readonly List<Health> affectedEnemies = new List<Health>();

        /// <summary>Raised when a cast's effects have been applied (immediately, or after impactDelay).</summary>
        public event Action<SkillCastReport> EffectsApplied;

        public bool IsInitialized => owner != null && aim != null;

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

        /// <param name="upgradeLevel">The card copy's upgrade level; carried to the element hits (combo tier).</param>
        public SkillCastReport CastCard(CardDefinition card, int upgradeLevel = 0)
        {
            if (card == null || !IsInitialized)
            {
                return default;
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

        SkillCastReport Resolve(CardDefinition card, Vector3 center, Health primary, Vector3 origin, int upgradeLevel)
        {
            var report = new SkillCastReport { Card = card, Point = center, PrimaryTarget = primary };
            CombatStatus ownerStatus = owner.Status;
            float damageMultiplier = ownerStatus != null ? ownerStatus.Model.OutgoingDamageMultiplier : 1f;
            visited.Clear();
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
            if (radius > 0f)
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
            PlayFx(card.impactFx, point, Quaternion.identity, 0f);
            return result.Damage;
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

        /// <param name="radius">When above 0, the prefab (authored for a 1 m radius) is scaled to match.</param>
        static void PlayFx(GameObject prefab, Vector3 position, Quaternion rotation, float radius)
        {
            if (prefab == null)
            {
                return;
            }

            PooledFx fx = FxPool.Spawn(prefab, position, rotation);
            if (fx != null)
            {
                fx.transform.localScale = radius > 0f ? Vector3.one * radius : Vector3.one;
            }
        }
    }
}
