using System.Collections;
using System.Collections.Generic;
using CoopDeck.Combat;
using CoopDeck.Core;
using CoopDeck.Effects;
using UnityEngine;

namespace CoopDeck.Combos
{
    /// <summary>
    /// Applies elements and fires combos. Listens to <see cref="CombatEvents.ElementHitApplied"/>
    /// (raised by skills only), keeps elements on each target's <see cref="CombatStatus"/>, and
    /// resolves the three combo kinds. Order per combo: damage → <see cref="ComboEvents"/> →
    /// effects. Combo damage is flagged <see cref="DamageFlags.ComboDamage"/> and never raises an
    /// element hit, so combos cannot chain into further combos. Online, only the host would run this.
    /// </summary>
    public class ComboSystem : MonoBehaviour
    {
        const float HitHeight = 1.1f;

        [SerializeField] List<ComboDefinition> combos = new List<ComboDefinition>();
        [Tooltip("Safety net: combo hits one target may take per frame, however many combos fire.")]
        [SerializeField, Min(1)] int maxComboHitsPerTargetPerFrame = ComboDamageBudget.DefaultMaxHitsPerTargetPerFrame;
        [Tooltip("Safety net: combos that may start in one frame (readability and frame time).")]
        [SerializeField, Min(1)] int maxCombosPerFrame = 8;

        const float EffectMergeSeconds = 0.3f;

        struct RecentEffect
        {
            public GameObject Prefab;
            public Vector3 Position;
            public float Time;
        }

        readonly List<ComboRule> rules = new List<ComboRule>();
        readonly List<Health> found = new List<Health>();
        readonly List<RecentEffect> recentEffects = new List<RecentEffect>();
        ComboDamageBudget budget;
        int frame = -1;
        int combosThisFrame;

        /// <summary>Debug: forces every combo to one tier (F6). Null = from the cards' upgrade levels.</summary>
        public static ComboTier? TierOverride { get; set; }

        public IReadOnlyList<ComboDefinition> Combos => combos;

        /// <summary>The live rules (window, cooldown, co-op bonus). NPC planning and HUD markers read these.</summary>
        public IReadOnlyList<ComboRule> Rules => rules;

        public int TriggeredCount { get; private set; }

        /// <summary>Combos completed by a different ally than the one who applied the first element.</summary>
        public int CoopCount { get; private set; }

        public ComboDefinition DefinitionOf(in ComboRule rule) =>
            rule.Id >= 0 && rule.Id < combos.Count ? combos[rule.Id] : null;

        /// <summary>Big effects merged into one already playing nearby (see ClaimBigEffect).</summary>
        public int SkippedEffects { get; private set; }

        public void Configure(List<ComboDefinition> definitions)
        {
            combos = definitions ?? new List<ComboDefinition>();
            RebuildRules();
        }

        void Awake()
        {
            RebuildRules();
        }

        /// <summary>
        /// Instantiates each combo effect once up front (pooled), so the first combo of a run (and the
        /// first co-op overlay) does not hitch on instantiation in the middle of a fight.
        /// </summary>
        void Start()
        {
            var warmed = new HashSet<GameObject>();
            foreach (ComboDefinition combo in combos)
            {
                if (combo == null)
                {
                    continue;
                }

                Warm(warmed, combo.coopFx, 1);
                Warm(warmed, combo.arcFx, 3);
                if (combo.tiers == null)
                {
                    continue;
                }

                foreach (ComboTierSettings tier in combo.tiers)
                {
                    if (tier != null)
                    {
                        Warm(warmed, tier.fx, 1);
                        Warm(warmed, tier.hitFx, 2);
                    }
                }
            }
        }

        static void Warm(HashSet<GameObject> warmed, GameObject prefab, int count)
        {
            if (prefab != null && warmed.Add(prefab))
            {
                FxPool.Prewarm(prefab, count);
            }
        }

        void OnEnable()
        {
            CombatEvents.ElementHitApplied += OnElementHit;
        }

        void OnDisable()
        {
            CombatEvents.ElementHitApplied -= OnElementHit;
        }

        void RebuildRules()
        {
            rules.Clear();
            for (int i = 0; i < combos.Count; i++)
            {
                if (combos[i] != null)
                {
                    rules.Add(combos[i].ToRule(i));
                }
            }

            budget = new ComboDamageBudget(maxComboHitsPerTargetPerFrame);
        }

        void OnElementHit(ElementHit hit)
        {
            if (hit.Target == null)
            {
                return;
            }

            CombatStatus status = hit.Target.GetComponent<CombatStatus>();
            if (status == null)
            {
                return; // only targets with a status component can hold elements
            }

            if (frame != Time.frameCount)
            {
                frame = Time.frameCount;
                combosThisFrame = 0;
            }

            bool allow = combosThisFrame < maxCombosPerFrame;
            if (!ComboResolver.Resolve(status.Elements, rules, hit, out ComboTrigger trigger, allow))
            {
                return;
            }

            combosThisFrame++;
            ComboDefinition definition = combos[trigger.RuleId];
            if (TierOverride.HasValue)
            {
                trigger.Tier = TierOverride.Value;
            }

            Execute(definition, trigger, hit.Target, hit.Target.transform.position);
        }

        void Execute(ComboDefinition definition, ComboTrigger trigger, Health target, Vector3 position)
        {
            TriggeredCount++;
            if (trigger.IsCoop)
            {
                CoopCount++;
            }

            ComboTierSettings tier = definition.Tier(trigger.Tier);
            var report = new ComboReport
            {
                Definition = definition,
                Tier = trigger.Tier,
                Trigger = trigger,
                Target = target,
                Position = position,
            };

            switch (definition.kind)
            {
                case ComboKind.ChainBurst:
                    ResolveChainBurst(definition, tier, trigger, target, position, ref report);
                    Announce(definition, tier, position, ref report, finished: true);
                    break;

                case ComboKind.Explosion:
                    var hitSet = new HashSet<Health>();
                    AreaDamage(position, tier.radius, tier.damage, trigger, hitSet, ref report);
                    Announce(definition, tier, position, ref report, finished: true);
                    break;

                case ComboKind.Singularity:
                    PullIn(position, tier, trigger);
                    Announce(definition, tier, position, ref report, finished: false);
                    StartCoroutine(SingularityBlast(definition, tier, trigger, position, report));
                    break;
            }
        }

        /// <summary>Damage first, then the event, then the effects.</summary>
        void Announce(ComboDefinition definition, ComboTierSettings tier, Vector3 position, ref ComboReport report, bool finished)
        {
            ComboEvents.RaiseTriggered(report);
            if (finished)
            {
                ComboEvents.RaiseFinished(report);
            }

            float scale = definition.kind == ComboKind.Singularity ? Mathf.Max(tier.pullRadius, tier.radius) : Mathf.Max(1f, tier.radius);
            if (ClaimBigEffect(tier.fx, position, scale))
            {
                PlayFx(tier.fx, position + Vector3.up * 0.05f, scale);
                bool coop = report.IsCoop;
                CameraShaker.Shake(position, coop ? tier.shake * definition.coopShakeMultiplier : tier.shake);
                if (coop)
                {
                    // Co-op: the same combo effect plus a brighter overlay and a short camera punch.
                    // The overlay's optional layers are quality-gated like every other combo effect.
                    PlayFx(definition.coopFx, position + Vector3.up * 0.05f, Mathf.Max(1.5f, scale));
                    CameraShaker.Punch(position, definition.coopFovPunch);
                }
            }
        }

        /// <summary>
        /// Readability: when an area skill completes the same combo on a whole group at once, the
        /// big effect plays once for the group instead of stacking copies that hide the enemies.
        /// Damage, events and the banner count are unaffected.
        /// </summary>
        bool ClaimBigEffect(GameObject prefab, Vector3 position, float radius)
        {
            if (prefab == null)
            {
                return false;
            }

            float now = Time.time;
            recentEffects.RemoveAll(e => now - e.Time > EffectMergeSeconds);
            float mergeDistance = Mathf.Max(3.5f, radius * 0.6f);
            foreach (RecentEffect e in recentEffects)
            {
                if (e.Prefab == prefab && (e.Position - position).sqrMagnitude < mergeDistance * mergeDistance)
                {
                    SkippedEffects++;
                    return false;
                }
            }

            recentEffects.Add(new RecentEffect { Prefab = prefab, Position = position, Time = now });
            return true;
        }

        // ------------------------------------------------------------ superconductor

        void ResolveChainBurst(ComboDefinition definition, ComboTierSettings tier, in ComboTrigger trigger, Health target, Vector3 position, ref ComboReport report)
        {
            var hitSet = new HashSet<Health>();
            Vector3 center = position + Vector3.up * HitHeight;

            // The burst: the target (and, from Enhanced on, everyone in the radius).
            Hit(target, tier.damage, trigger, center, hitSet, ref report);
            if (tier.radius > 0f)
            {
                AreaDamage(position, tier.radius, tier.damage, trigger, hitSet, ref report);
            }

            // The chain: from the target to the nearest enemy not hit yet, each jump a bit weaker.
            Vector3 last = center;
            float damage = tier.damage;
            for (int i = 0; i < tier.chainCount; i++)
            {
                Health next = NearestHostile(last, tier.chainRange, trigger.CompleterTeam, hitSet);
                if (next == null)
                {
                    break;
                }

                damage *= tier.chainFalloff;
                Vector3 point = next.transform.position + Vector3.up * HitHeight;
                Hit(next, damage, trigger, last, hitSet, ref report);
                PlayArc(definition.arcFx, last, point, i);
                PlayFx(tier.hitFx, point, 1f);
                last = point;
            }
        }

        // ------------------------------------------------------------ burning singularity

        void PullIn(Vector3 center, ComboTierSettings tier, in ComboTrigger trigger)
        {
            if (tier.pullRadius <= 0f || tier.pullSpeed <= 0f || tier.pullDuration <= 0f)
            {
                return;
            }

            foreach (Health enemy in Hostiles(center, tier.pullRadius, trigger.CompleterTeam))
            {
                if (enemy.TryGetComponent(out ForceReceiver receiver))
                {
                    receiver.Pull(center, tier.pullSpeed, tier.pullDuration, 0.6f);
                }
            }
        }

        IEnumerator SingularityBlast(ComboDefinition definition, ComboTierSettings tier, ComboTrigger trigger, Vector3 center, ComboReport report)
        {
            if (tier.pullDuration > 0f)
            {
                yield return new WaitForSeconds(tier.pullDuration);
            }

            var hitSet = new HashSet<Health>();
            AreaDamage(center, tier.radius, tier.damage, trigger, hitSet, ref report);
            ComboEvents.RaiseFinished(report);
            if (!ClaimBigEffect(tier.hitFx, center, tier.radius))
            {
                yield break;
            }

            PlayFx(tier.hitFx, center + Vector3.up * 0.05f, Mathf.Max(1f, tier.radius));
            CameraShaker.Shake(center, tier.shake);
        }

        // ------------------------------------------------------------ shared

        void AreaDamage(Vector3 center, float radius, float damage, in ComboTrigger trigger, HashSet<Health> hitSet, ref ComboReport report)
        {
            if (radius <= 0f)
            {
                return;
            }

            Vector3 from = center + Vector3.up * HitHeight;
            foreach (Health enemy in Hostiles(center, radius, trigger.CompleterTeam))
            {
                Hit(enemy, damage, trigger, from, hitSet, ref report);
            }
        }

        /// <summary>One combo hit. Each enemy is hit at most once per combo, and the per-frame budget caps stacking.</summary>
        void Hit(Health target, float amount, in ComboTrigger trigger, Vector3 from, HashSet<Health> hitSet, ref ComboReport report)
        {
            if (target == null || !hitSet.Add(target) || !target.IsAlive || !IsHostile(target, trigger.CompleterTeam))
            {
                return;
            }

            if (!budget.TryHit(target.GetHashCode(), Time.frameCount))
            {
                return;
            }

            Vector3 point = target.transform.position + Vector3.up * HitHeight;
            Vector3 direction = point - from;
            DamageResult result = target.ApplyDamage(new DamageInfo
            {
                Amount = amount * trigger.DamageMultiplier,
                Element = trigger.IncomingElement,
                Kind = DamageKind.Combo,
                Flags = DamageFlags.ComboDamage | (trigger.IsCoop ? DamageFlags.Coop : DamageFlags.None),
                Source = trigger.Completer,
                SourceTeam = trigger.CompleterTeam,
                Point = point,
                Normal = Vector3.up,
                Direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.up,
                HeadshotMultiplier = 1f,
            });

            if (result.Applied)
            {
                report.EnemiesHit++;
                report.TotalDamage += result.Damage;
            }
        }

        static bool IsHostile(Health health, TeamId team) =>
            health != null && health.IsAlive && health.Team != team && DamageRules.CanDamage(team, health.Team);

        List<Health> Hostiles(Vector3 center, float radius, TeamId team)
        {
            found.Clear();
            foreach (Collider collider in Physics.OverlapSphere(center, radius, GameLayers.SkillQueryMask, QueryTriggerInteraction.Ignore))
            {
                Health health = collider.GetComponentInParent<Health>();
                if (IsHostile(health, team) && !found.Contains(health))
                {
                    found.Add(health);
                }
            }

            return new List<Health>(found);
        }

        Health NearestHostile(Vector3 from, float radius, TeamId team, HashSet<Health> exclude)
        {
            Health best = null;
            float bestDistance = float.MaxValue;
            foreach (Health candidate in Hostiles(from, radius, team))
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

        static void PlayFx(GameObject prefab, Vector3 position, float scale)
        {
            if (prefab == null)
            {
                return;
            }

            PooledFx fx = FxPool.Spawn(prefab, position, Quaternion.identity);
            if (fx == null)
            {
                return;
            }

            if (fx.TryGetComponent(out RadiusScaledFx sized))
            {
                sized.SetRadius(scale); // grows rings and areas only; sprites and heights stay as authored
            }
            else
            {
                fx.transform.localScale = Vector3.one * Mathf.Max(0.1f, scale);
            }
        }

        static void PlayArc(GameObject prefab, Vector3 from, Vector3 to, int index)
        {
            if (prefab == null)
            {
                return;
            }

            PooledFx fx = FxPool.Spawn(prefab, from, Quaternion.identity);
            if (fx != null && fx.TryGetComponent(out LightningArcFx arc))
            {
                fx.transform.localScale = Vector3.one;
                arc.Play(from, to, Time.frameCount * 31 + index * 7 + 1);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            TierOverride = null;
        }
    }
}
