using System.Collections.Generic;
using CoopDeck.Cards;
using CoopDeck.Combat;
using CoopDeck.Combos;
using CoopDeck.Core;
using UnityEngine;

namespace CoopDeck.NPC
{
    /// <summary>The action the utility layer picked, with what it needs to carry it out.</summary>
    public struct NpcDecision
    {
        public NpcActionKind Action;
        public float Score;

        /// <summary>Enemy index to aim the skill at (or shoot), -1 none.</summary>
        public int Target;

        /// <summary>Hand slot to use, -1 none.</summary>
        public int Slot;

        /// <summary>Enemy to shoot while not casting, -1 none.</summary>
        public int ShootTarget;

        /// <summary>Combo rule index (finish / setup), -1 none.</summary>
        public int RuleIndex;

        /// <summary>Finishing an element an ally applied (co-op bonus).</summary>
        public bool Coop;

        public Vector3 EvadeDirection;
        public bool EvadeDash;

        /// <summary>Hand slots saved for a combo this think (bit per slot).</summary>
        public int ReservedMask;

        public static NpcDecision None => new NpcDecision { Action = NpcActionKind.None, Target = -1, Slot = -1, ShootTarget = -1, RuleIndex = -1 };
    }

    /// <summary>An enemy the NPC can finish a combo on right now (for the HUD "follow-up ready" marker).</summary>
    public struct NpcFollowUp
    {
        public int Enemy;
        public int Slot;
        public int RuleIndex;
        public bool Coop;
    }

    /// <summary>
    /// Utility AI: scores every possible action from an <see cref="NpcSituation"/> and returns the
    /// best one. Bands keep the design priorities (1 dodge a dangerous attack, 2 help a downed or
    /// dying ally, 3 complete a co-op combo, 4 heal / protect, 5 area skill when it hits several,
    /// 6 single-target skill / shooting, 7 follow); inside a band the situation (distance, line of
    /// sight, cooldown, gauge, hand, tactic) moves the score. Pure C#: same input, same answer.
    /// </summary>
    public static class NpcDecisionMaker
    {
        public const float EvadeBand = 900f;
        public const float ReviveBand = 820f;
        public const float RescueHealBand = 810f;
        public const float ComboFinishBand = 700f;
        public const float ComboSetupBand = 600f;
        public const float SupportBand = 500f;
        public const float AreaBand = 400f;
        public const float SingleBand = 300f;
        public const float ShootBand = 200f;
        public const float FollowBand = 100f;

        /// <summary>Seconds of travel before a cast lands, added to the combo margin for aimed cards.</summary>
        const float AimLead = 0.2f;

        public static NpcDecision Decide(NpcSituation s, NpcProfile p, List<NpcFollowUp> followUps = null)
        {
            followUps?.Clear();
            NpcDecision best = NpcDecision.None;
            if (s == null || p == null || !s.Alive)
            {
                return best;
            }

            int shoot = PickShootTarget(s);
            best.ShootTarget = shoot;
            best.ReservedMask = ReservedMask(s, p);

            Consider(ref best, EvaluateEvade(s, p));
            Consider(ref best, EvaluateRevive(s));
            bool paced = s.SinceLastCast >= p.castInterval; // ordinary skills keep a human-like rhythm
            if (s.SinceLastCast >= 0.25f)
            {
                EvaluateCombos(s, p, ref best, followUps, allowSetup: paced);
                EvaluateSupport(s, ref best, urgentOnly: !paced);
            }

            if (paced)
            {
                EvaluateAreaAndSingle(s, p, shoot, ref best);
            }

            if (shoot >= 0)
            {
                NpcEnemyInfo e = s.Enemies[shoot];
                float score = ShootBand + 40f - Mathf.Min(40f, e.Distance);
                Consider(ref best, new NpcDecision { Action = NpcActionKind.Shoot, Score = score, Target = shoot, Slot = -1, RuleIndex = -1 });
            }

            if (best.Action == NpcActionKind.None)
            {
                best.Action = NpcActionKind.Follow;
                best.Score = FollowBand;
                best.Target = -1;
                best.Slot = -1;
                best.RuleIndex = -1;
            }

            best.ShootTarget = shoot;
            return best;
        }

        static void Consider(ref NpcDecision best, in NpcDecision candidate)
        {
            if (candidate.Action == NpcActionKind.None || candidate.Score <= best.Score)
            {
                return;
            }

            int reserved = best.ReservedMask;
            int shoot = best.ShootTarget;
            best = candidate;
            best.ReservedMask = reserved;
            best.ShootTarget = shoot;
        }

        // ---------------------------------------------------------------- 1. evade

        public static NpcDecision EvaluateEvade(NpcSituation s, NpcProfile p)
        {
            NpcDecision result = NpcDecision.None;
            for (int i = 0; i < s.ThreatCount; i++)
            {
                NpcThreat t = s.Threats[i];
                if (t.TimeLeft <= 0f || t.TimeLeft > 1.1f)
                {
                    continue;
                }

                Vector3 away = Flat(s.Position - t.Origin);
                float distance = away.magnitude;
                if (t.Kind == ThreatKind.Melee && distance > t.Reach + 1f)
                {
                    continue; // already out of reach
                }

                bool dangerous = t.Damage >= s.Health * p.lethalShare || s.Health01 < 0.35f;
                if (!dangerous && t.Roll >= p.dodgeChance)
                {
                    continue;
                }

                float score = EvadeBand + (dangerous ? 60f : 0f) + (1.1f - t.TimeLeft) * 10f;
                if (score <= result.Score)
                {
                    continue;
                }

                Vector3 travel = t.Kind == ThreatKind.Melee ? (distance > 0.01f ? away / distance : -Flat(t.Direction).normalized) : Flat(t.Direction).normalized;
                if (travel.sqrMagnitude < 0.01f)
                {
                    travel = Vector3.forward;
                }

                Vector3 side = Vector3.Cross(Vector3.up, travel).normalized * (t.Roll < 0.5f ? 1f : -1f);
                Vector3 dir = t.Kind == ThreatKind.Melee ? (travel * 0.7f + side * 0.7f).normalized : side;

                result = new NpcDecision
                {
                    Action = NpcActionKind.Evade,
                    Score = score,
                    Target = -1,
                    Slot = -1,
                    RuleIndex = -1,
                    EvadeDirection = dir,
                    EvadeDash = s.DashCharges >= 1f,
                };
            }

            return result;
        }

        // ---------------------------------------------------------------- 2. help allies

        static NpcDecision EvaluateRevive(NpcSituation s)
        {
            if (!s.HasLeader || !s.LeaderDowned)
            {
                return NpcDecision.None;
            }

            return new NpcDecision { Action = NpcActionKind.Revive, Score = ReviveBand, Target = -1, Slot = -1, RuleIndex = -1 };
        }

        // ---------------------------------------------------------------- 3. combos

        static void EvaluateCombos(NpcSituation s, NpcProfile p, ref NpcDecision best, List<NpcFollowUp> followUps, bool allowSetup = true)
        {
            if (s.Rules == null || s.Rules.Count == 0)
            {
                return;
            }

            for (int slot = 0; slot < s.Hand.Length; slot++)
            {
                NpcHandCard card = s.Hand[slot];
                if (!card.Ready || !card.Traits.HitsEnemies || card.Traits.Element == Element.None)
                {
                    continue;
                }

                float margin = p.comboMargin + card.Traits.ImpactDelay + (card.Traits.NeedsAim ? AimLead : 0f);
                float costPenalty = card.GaugeCost / 100f * 12f;

                // Finish: an element already on an enemy pairs with this card.
                for (int e = 0; e < s.EnemyCount; e++)
                {
                    NpcEnemyInfo enemy = s.Enemies[e];
                    if (!enemy.Engaged || enemy.Auras == null || !CanReach(card.Traits, enemy))
                    {
                        continue;
                    }

                    if (!ComboPlanner.TryFindFollowUp(enemy.Auras, s.Rules, card.Traits.Element, margin, out ComboOpportunity chance))
                    {
                        continue;
                    }

                    bool coop = chance.ApplierId != 0 && chance.ApplierId != s.SelfId && chance.ApplierTeam == s.Team;
                    followUps?.Add(new NpcFollowUp { Enemy = e, Slot = slot, RuleIndex = chance.RuleIndex, Coop = coop });

                    float score = ComboFinishBand + (coop ? 60f : -60f) - costPenalty;
                    score += Mathf.Clamp01(1f - chance.TimeLeft / 4f) * 20f; // about to expire: now or never
                    score -= enemy.Distance * 0.4f;
                    if (card.Traits.IsArea)
                    {
                        score += 6f * Mathf.Min(4, s.EngagedWithin(enemy.Position, card.Traits.EnemyRadius) - 1);
                    }

                    score += TacticBias(s, e, comboFirstBonus: 50f);
                    Consider(ref best, new NpcDecision
                    {
                        Action = NpcActionKind.ComboFinish,
                        Score = score,
                        Target = e,
                        Slot = slot,
                        RuleIndex = chance.RuleIndex,
                        Coop = coop,
                    });
                }

                if (allowSetup && (best.ReservedMask & (1 << slot)) == 0)
                {
                    EvaluateSetup(s, p, slot, card, costPenalty, ref best); // a saved finisher is not spent on starting combos
                }
            }
        }

        /// <summary>Start a combo for an ally: apply the element that pairs with a card the leader holds.</summary>
        static void EvaluateSetup(NpcSituation s, NpcProfile p, int slot, in NpcHandCard card, float costPenalty, ref NpcDecision best)
        {
            if (!s.SetupAllowed)
            {
                return; // just started one: give the ally time to finish it
            }

            Element mine = card.Traits.Element;
            int ruleIndex = -1;
            bool forLeader = false;
            if (s.HasLeader && s.LeaderAlive && s.LeaderElementMask != 0)
            {
                for (int e = 1; e <= 4 && ruleIndex < 0; e++)
                {
                    if ((s.LeaderElementMask & (1 << e)) != 0 && ComboPlanner.Pairs(s.Rules, mine, (Element)e, out int r))
                    {
                        ruleIndex = r;
                        forLeader = true;
                    }
                }
            }

            if (ruleIndex < 0)
            {
                // Own follow-up: another ready card in hand pairs with this one (a solo combo).
                for (int other = 0; other < s.Hand.Length && ruleIndex < 0; other++)
                {
                    if (other != slot && s.Hand[other].Ready && s.Hand[other].Traits.HitsEnemies &&
                        ComboPlanner.Pairs(s.Rules, mine, s.Hand[other].Traits.Element, out int r))
                    {
                        ruleIndex = r;
                    }
                }
            }

            if (ruleIndex < 0)
            {
                return;
            }

            ComboRule rule = s.Rules[ruleIndex];
            Element partner = rule.PartnerOf(mine);
            int target = -1;
            float bestValue = float.MinValue;
            for (int e = 0; e < s.EnemyCount; e++)
            {
                NpcEnemyInfo enemy = s.Enemies[e];
                if (!enemy.Engaged || enemy.Health01 < 0.2f || !CanReach(card.Traits, enemy))
                {
                    continue;
                }

                if (enemy.Auras != null && (enemy.Auras.Has(mine) || enemy.Auras.Has(partner) || enemy.Auras.CooldownRemaining(rule.Id) > 0f))
                {
                    continue; // already primed (or a finish is possible: handled above) or on cooldown
                }

                float value = -enemy.Distance;
                if (e == s.LeaderTarget)
                {
                    value += 40f; // the one the leader is shooting: they will hit it next
                }

                if (e == s.FocusTarget)
                {
                    value += 25f;
                }

                if (s.HasLeader)
                {
                    value -= (enemy.Position - s.LeaderPosition).magnitude * 0.5f;
                }

                if (value > bestValue)
                {
                    bestValue = value;
                    target = e;
                }
            }

            if (target < 0)
            {
                return;
            }

            float score = (forLeader ? ComboSetupBand : ComboSetupBand - 60f) - costPenalty;
            score += TacticBias(s, target, comboFirstBonus: 60f);
            Consider(ref best, new NpcDecision
            {
                Action = NpcActionKind.ComboSetup,
                Score = score,
                Target = target,
                Slot = slot,
                RuleIndex = ruleIndex,
                Coop = forLeader,
            });
        }

        /// <summary>Focus: the marked enemy first. Combo first: combos score higher.</summary>
        static float TacticBias(NpcSituation s, int enemy, float comboFirstBonus)
        {
            switch (s.Tactic)
            {
                case NpcTactic.ComboFirst: return comboFirstBonus;
                case NpcTactic.Focus: return s.FocusTarget < 0 ? 0f : enemy == s.FocusTarget ? 30f : -150f;
                default: return 0f;
            }
        }

        // ---------------------------------------------------------------- 4. support

        static void EvaluateSupport(NpcSituation s, ref NpcDecision best, bool urgentOnly = false)
        {
            bool underPressure = s.ThreatCount > 0 || s.EngagedWithin(s.Position, 15f) > 0;
            for (int slot = 0; slot < s.Hand.Length; slot++)
            {
                NpcHandCard card = s.Hand[slot];
                if (!card.Ready || !card.Traits.IsSupport)
                {
                    continue;
                }

                NpcCardTraits t = card.Traits;
                float score = 0f;
                if (t.HealAmount > 0f)
                {
                    float selfMissing = 1f - s.Health01;
                    bool leaderInRange = s.HasLeader && s.LeaderAlive && s.LeaderDistance <= Mathf.Max(0.5f, t.AllyRadius - 0.5f);
                    float leaderMissing = leaderInRange ? 1f - s.LeaderHealth01 : 0f;
                    if (leaderInRange && s.LeaderHealth01 < 0.35f)
                    {
                        score = RescueHealBand; // 2. the leader is about to go down
                    }
                    else if (Mathf.Max(selfMissing, leaderMissing) >= 0.35f)
                    {
                        score = SupportBand + Mathf.Max(selfMissing, leaderMissing) * 80f;
                    }
                }

                if (t.ShieldAmount > 0f && s.ShieldAmount <= 0f && s.Health01 < 0.7f && underPressure)
                {
                    score = Mathf.Max(score, SupportBand + (1f - s.Health01) * 60f);
                }

                if (t.BuffAmount > 0f && s.DamageBuff <= 0f && s.HasEngagedEnemy && underPressure)
                {
                    score = Mathf.Max(score, SupportBand - 30f);
                }

                if (score > 0f && (!urgentOnly || score >= RescueHealBand))
                {
                    Consider(ref best, new NpcDecision { Action = NpcActionKind.Support, Score = score, Target = -1, Slot = slot, RuleIndex = -1 });
                }
            }
        }

        // ---------------------------------------------------------------- 5–6. damage skills

        static void EvaluateAreaAndSingle(NpcSituation s, NpcProfile p, int shoot, ref NpcDecision best)
        {
            for (int slot = 0; slot < s.Hand.Length; slot++)
            {
                NpcHandCard card = s.Hand[slot];
                if (!card.Ready || !card.Traits.HitsEnemies || (best.ReservedMask & (1 << slot)) != 0)
                {
                    continue;
                }

                NpcCardTraits t = card.Traits;
                float costPenalty = card.GaugeCost / 100f * 15f;
                if (t.IsArea)
                {
                    int target = -1;
                    int count = 0;
                    if (t.Targeting == CardTargeting.AreaAroundSelf || t.Targeting == CardTargeting.Self)
                    {
                        count = s.EngagedWithin(s.Position, t.EnemyRadius - 0.3f);
                    }
                    else
                    {
                        for (int e = 0; e < s.EnemyCount; e++)
                        {
                            NpcEnemyInfo enemy = s.Enemies[e];
                            if (!enemy.Engaged || !CanReach(t, enemy))
                            {
                                continue;
                            }

                            int n = s.EngagedWithin(enemy.Position, t.EnemyRadius * 0.9f);
                            if (n > count || n == count && e == shoot)
                            {
                                count = n;
                                target = e;
                            }
                        }
                    }

                    if (count >= 2)
                    {
                        float score = AreaBand + Mathf.Min(5, count) * 15f - costPenalty;
                        if (target >= 0)
                        {
                            score += TacticBias(s, target, 0f) * 0.3f;
                        }

                        Consider(ref best, new NpcDecision { Action = NpcActionKind.AreaSkill, Score = score, Target = target, Slot = slot, RuleIndex = -1 });
                        continue;
                    }
                }

                // Single target (or an area card on a lone enemy): the shooting target, or the focus.
                int single = s.Tactic == NpcTactic.Focus && s.FocusTarget >= 0 && CanReach(t, s.Enemies[s.FocusTarget]) ? s.FocusTarget : shoot;
                if (single < 0 || !CanReach(t, s.Enemies[single]))
                {
                    continue;
                }

                NpcEnemyInfo victim = s.Enemies[single];
                if (victim.IsTraining && t.Damage >= 80f || victim.Health01 < 0.15f)
                {
                    continue; // no meteors on practice dummies, no cards on an enemy the gun is about to finish
                }

                float value = SingleBand + Mathf.Min(40f, t.Damage * 0.4f) - costPenalty + (t.Marks && victim.Health01 > 0.5f ? 15f : 0f);
                if (t.IsArea)
                {
                    value -= 40f; // an area card on one enemy is a waste unless nothing better comes up
                }

                Consider(ref best, new NpcDecision { Action = NpcActionKind.SingleSkill, Score = value, Target = single, Slot = slot, RuleIndex = -1 });
            }
        }

        // ---------------------------------------------------------------- holding cards for combos

        /// <summary>
        /// Cards saved for an ally's element: a finisher whose partner the leader holds is kept out of
        /// plain damage use for up to <see cref="NpcProfile.HoldFor"/> seconds (never forever: once the
        /// hold runs out the card is used normally for a while).
        /// </summary>
        public static int ReservedMask(NpcSituation s, NpcProfile p)
        {
            if (s.Tactic == NpcTactic.Focus || s.Rules == null || !s.HasLeader || s.LeaderElementMask == 0)
            {
                return 0;
            }

            float limit = p.HoldFor(s.Tactic);
            int mask = 0;
            for (int slot = 0; slot < s.Hand.Length; slot++)
            {
                NpcHandCard card = s.Hand[slot];
                if (!card.Present || card.HoldBlocked || card.HeldFor >= limit || !card.Traits.HitsEnemies || card.Traits.Element == Element.None)
                {
                    continue;
                }

                if (s.FavoredElement != Element.None && card.Traits.Element != s.FavoredElement)
                {
                    continue; // not a finisher for this NPC: it is spent starting combos instead of waiting
                }

                for (int e = 1; e <= 4; e++)
                {
                    if ((s.LeaderElementMask & (1 << e)) != 0 && ComboPlanner.Pairs(s.Rules, card.Traits.Element, (Element)e, out _))
                    {
                        mask |= 1 << slot;
                        break;
                    }
                }
            }

            return mask;
        }

        // ---------------------------------------------------------------- targeting

        public static int PickShootTarget(NpcSituation s)
        {
            int best = -1;
            float bestScore = float.MinValue;
            for (int i = 0; i < s.EnemyCount; i++)
            {
                NpcEnemyInfo e = s.Enemies[i];
                if (!e.Engaged || !e.LineOfSight)
                {
                    continue;
                }

                float score = 100f - e.Distance * 1.5f + (1f - e.Health01) * 10f;
                if (i == s.CurrentTarget) score += 15f;
                if (i == s.LeaderTarget) score += 20f;
                if (i == s.FocusTarget) score += s.Tactic == NpcTactic.Focus ? 400f : 30f;
                if (e.IsMelee && e.Distance < 8f) score += 15f;
                if (e.IsTraining) score -= 20f;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }

            return best;
        }

        /// <summary>Can this card reach that enemy from where the NPC stands?</summary>
        public static bool CanReach(in NpcCardTraits t, in NpcEnemyInfo enemy)
        {
            switch (t.Targeting)
            {
                case CardTargeting.AreaAroundSelf:
                case CardTargeting.Self:
                    return t.EnemyRadius > 0f && enemy.Distance <= t.EnemyRadius - 0.3f;
                default:
                    return enemy.LineOfSight && enemy.Distance <= t.Range;
            }
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
