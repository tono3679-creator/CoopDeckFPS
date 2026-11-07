using System.Collections.Generic;
using CoopDeck.Combat;
using CoopDeck.Core;

namespace CoopDeck.Combos
{
    /// <summary>A combo that one more element hit would fire on a target right now.</summary>
    public struct ComboOpportunity
    {
        public int RuleIndex;

        /// <summary>The element already on the target (it gets used up).</summary>
        public Element PrimedElement;

        /// <summary>The element a hit must carry to fire the combo.</summary>
        public Element FinisherElement;

        /// <summary>Seconds left in the combo window.</summary>
        public float TimeLeft;

        public int ApplierId;
        public TeamId ApplierTeam;
    }

    /// <summary>
    /// Read-only questions about combos, answered with exactly the rules <see cref="ComboResolver"/>
    /// uses (window, per-target cooldown) but without changing anything: "would an Ice hit fire a
    /// combo here?", "what can finish this target?". NPC allies plan follow-ups with it and the HUD
    /// marks targets with it. Online, clients can run it on replicated auras for markers only.
    /// </summary>
    public static class ComboPlanner
    {
        /// <summary>
        /// The combo a hit of <paramref name="incoming"/> would fire on a target holding
        /// <paramref name="auras"/>, requiring at least <paramref name="minTimeLeft"/> seconds of window
        /// (time for the shot to land). Picks the most recently applied partner, like the resolver.
        /// </summary>
        public static bool TryFindFollowUp(ElementAuraModel auras, IReadOnlyList<ComboRule> rules, Element incoming, float minTimeLeft,
            out ComboOpportunity opportunity)
        {
            opportunity = default;
            if (auras == null || rules == null || incoming == Element.None)
            {
                return false;
            }

            bool found = false;
            float bestAge = float.MaxValue;
            for (int i = 0; i < rules.Count; i++)
            {
                ComboRule rule = rules[i];
                if (!rule.Involves(incoming))
                {
                    continue;
                }

                Element partner = rule.PartnerOf(incoming);
                if (partner == incoming || !auras.TryGet(partner, out ElementAura aura))
                {
                    continue;
                }

                float timeLeft = UnityEngine.Mathf.Min(rule.Window - aura.Age, aura.Remaining);
                if (timeLeft < minTimeLeft || auras.CooldownRemaining(rule.Id) > 0f)
                {
                    continue;
                }

                if (!found || aura.Age < bestAge)
                {
                    found = true;
                    bestAge = aura.Age;
                    opportunity = new ComboOpportunity
                    {
                        RuleIndex = i,
                        PrimedElement = partner,
                        FinisherElement = incoming,
                        TimeLeft = timeLeft,
                        ApplierId = aura.ApplierId,
                        ApplierTeam = aura.ApplierTeam,
                    };
                }
            }

            return found;
        }

        /// <summary>
        /// Bit mask of the elements (bit = (int)Element) that would fire a combo on this target now.
        /// 0 = nothing primed. Used for "combo ready" markers.
        /// </summary>
        public static int FinisherMask(ElementAuraModel auras, IReadOnlyList<ComboRule> rules, float minTimeLeft)
        {
            int mask = 0;
            for (int e = 1; e <= 4; e++)
            {
                if (TryFindFollowUp(auras, rules, (Element)e, minTimeLeft, out _))
                {
                    mask |= 1 << e;
                }
            }

            return mask;
        }

        /// <summary>True when some rule pairs the two elements (e.g. Ice + Lightning).</summary>
        public static bool Pairs(IReadOnlyList<ComboRule> rules, Element a, Element b, out int ruleIndex)
        {
            ruleIndex = -1;
            if (rules == null || a == Element.None || b == Element.None || a == b)
            {
                return false;
            }

            for (int i = 0; i < rules.Count; i++)
            {
                if (rules[i].Involves(a) && rules[i].PartnerOf(a) == b)
                {
                    ruleIndex = i;
                    return true;
                }
            }

            return false;
        }
    }
}
