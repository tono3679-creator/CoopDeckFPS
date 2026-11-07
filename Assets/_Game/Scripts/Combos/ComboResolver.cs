using System.Collections.Generic;
using CoopDeck.Combat;
using CoopDeck.Core;
using UnityEngine;

namespace CoopDeck.Combos
{
    /// <summary>The rule part of a <see cref="ComboDefinition"/> as plain data (tests build these directly).</summary>
    public readonly struct ComboRule
    {
        public ComboRule(int id, Element elementA, Element elementB, float window, float cooldown, float coopMultiplier)
        {
            Id = id;
            ElementA = elementA;
            ElementB = elementB;
            Window = window;
            Cooldown = cooldown;
            CoopMultiplier = coopMultiplier;
        }

        public int Id { get; }
        public Element ElementA { get; }
        public Element ElementB { get; }
        public float Window { get; }
        public float Cooldown { get; }
        public float CoopMultiplier { get; }

        public bool Involves(Element element) => element != Element.None && (element == ElementA || element == ElementB);

        public Element PartnerOf(Element element) => element == ElementA ? ElementB : ElementA;
    }

    /// <summary>A combo that just fired: which rule, who set it up, who finished it, how strong.</summary>
    public struct ComboTrigger
    {
        public int RuleId;

        /// <summary>The element that was on the target and got used up.</summary>
        public Element PartnerElement;

        /// <summary>The element of the hit that completed the combo.</summary>
        public Element IncomingElement;

        public ICombatActor Applier;
        public int ApplierId;
        public TeamId ApplierTeam;

        public ICombatActor Completer;
        public int CompleterId;
        public TeamId CompleterTeam;

        /// <summary>A different ally applied the first element.</summary>
        public bool IsCoop;

        /// <summary>1, or the co-op multiplier.</summary>
        public float DamageMultiplier;

        public ComboTier Tier;
    }

    /// <summary>
    /// Element and combo rules, without Unity objects:
    /// - a hit whose element pairs with an element already on the target (applied within the
    ///   rule's window) fires that combo: the partner element is used up, the hit's own element is
    ///   not applied, and the combo goes on cooldown for that target;
    /// - otherwise the hit's element is applied (or refreshed);
    /// - combo damage never reaches this code (only skill element hits do), so a combo cannot
    ///   start another combo.
    /// </summary>
    public static class ComboResolver
    {
        public const int MaxTier = (int)ComboTier.Ultimate;

        /// <summary>The stronger card decides the tier: upgrade level 0 → Normal, 1 → Enhanced, 2+ → Ultimate.</summary>
        public static ComboTier TierFor(int applierPower, int completerPower) =>
            (ComboTier)Mathf.Clamp(Mathf.Max(applierPower, completerPower), 0, MaxTier);

        public static bool IsCoop(int applierId, TeamId applierTeam, int completerId, TeamId completerTeam) =>
            applierId != 0 && completerId != 0 && applierId != completerId && applierTeam == completerTeam;

        /// <param name="allowCombo">False (e.g. the per-frame combo cap was reached): only applies the element.</param>
        /// <returns>True when the hit completed a combo.</returns>
        public static bool Resolve(ElementAuraModel auras, IReadOnlyList<ComboRule> rules, in ElementHit hit, out ComboTrigger trigger, bool allowCombo = true)
        {
            trigger = default;
            if (auras == null || hit.Element == Element.None)
            {
                return false;
            }

            int best = -1;
            ElementAura partner = default;
            if (allowCombo && rules != null)
            {
                for (int i = 0; i < rules.Count; i++)
                {
                    ComboRule rule = rules[i];
                    if (!rule.Involves(hit.Element))
                    {
                        continue;
                    }

                    Element partnerElement = rule.PartnerOf(hit.Element);
                    if (partnerElement == hit.Element || !auras.TryGet(partnerElement, out ElementAura aura))
                    {
                        continue;
                    }

                    if (aura.Age > rule.Window || auras.CooldownRemaining(rule.Id) > 0f)
                    {
                        continue;
                    }

                    // Several partners present: the most recently applied one reacts.
                    if (best < 0 || aura.Age < partner.Age)
                    {
                        best = i;
                        partner = aura;
                    }
                }
            }

            if (best < 0)
            {
                auras.Apply(hit.Element, hit.Source, hit.SourceId, hit.SourceTeam, hit.Power);
                return false;
            }

            ComboRule chosen = rules[best];
            auras.Consume(partner.Element);
            auras.StartCooldown(chosen.Id, chosen.Cooldown);
            bool coop = IsCoop(partner.ApplierId, partner.ApplierTeam, hit.SourceId, hit.SourceTeam);
            trigger = new ComboTrigger
            {
                RuleId = chosen.Id,
                PartnerElement = partner.Element,
                IncomingElement = hit.Element,
                Applier = partner.Applier,
                ApplierId = partner.ApplierId,
                ApplierTeam = partner.ApplierTeam,
                Completer = hit.Source,
                CompleterId = hit.SourceId,
                CompleterTeam = hit.SourceTeam,
                IsCoop = coop,
                DamageMultiplier = coop ? Mathf.Max(1f, chosen.CoopMultiplier) : 1f,
                Tier = TierFor(partner.Power, hit.Power),
            };
            return true;
        }
    }

    /// <summary>
    /// Second safety net against runaway multi-hits: at most N combo hits per target per frame,
    /// however many combos fire at once.
    /// </summary>
    public sealed class ComboDamageBudget
    {
        public const int DefaultMaxHitsPerTargetPerFrame = 2;

        readonly Dictionary<int, int> hits = new Dictionary<int, int>();
        readonly int maxHits;
        int frame = int.MinValue;

        public ComboDamageBudget(int maxHitsPerTargetPerFrame = DefaultMaxHitsPerTargetPerFrame)
        {
            maxHits = Mathf.Max(1, maxHitsPerTargetPerFrame);
        }

        public int MaxHitsPerTargetPerFrame => maxHits;

        public bool TryHit(int targetKey, int currentFrame)
        {
            if (currentFrame != frame)
            {
                hits.Clear();
                frame = currentFrame;
            }

            hits.TryGetValue(targetKey, out int count);
            if (count >= maxHits)
            {
                return false;
            }

            hits[targetKey] = count + 1;
            return true;
        }
    }
}
