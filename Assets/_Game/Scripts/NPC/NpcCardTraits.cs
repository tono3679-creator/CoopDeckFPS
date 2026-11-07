using CoopDeck.Cards;
using CoopDeck.Core;
using UnityEngine;

namespace CoopDeck.NPC
{
    /// <summary>
    /// What a card does, summarised for the AI (computed once per definition, no Unity objects):
    /// how it reaches enemies, its element, area, rough damage and support effects.
    /// </summary>
    public struct NpcCardTraits
    {
        public Element Element;
        public CardTargeting Targeting;
        public float Range;
        public float ImpactDelay;

        /// <summary>Largest radius of an effect that touches enemies (0 = single target).</summary>
        public float EnemyRadius;

        /// <summary>Radius of heal / shield / buff (0 = the caster only).</summary>
        public float AllyRadius;

        /// <summary>Total damage one use deals to one enemy (direct + area + over time).</summary>
        public float Damage;

        public float HealAmount;
        public float ShieldAmount;
        public float BuffAmount;
        public bool Marks;
        public bool Pulls;

        /// <summary>The card reaches enemies at all (so it can apply its element / finish a combo).</summary>
        public bool HitsEnemies;

        public bool IsArea => HitsEnemies && EnemyRadius > 0f;
        public bool IsSupport => HealAmount > 0f || ShieldAmount > 0f || BuffAmount > 0f;
        public bool NeedsAim => Targeting == CardTargeting.Aim || Targeting == CardTargeting.AimPoint;

        /// <summary>How far from the caster an enemy can be and still be reached.</summary>
        public float Reach => Targeting == CardTargeting.AreaAroundSelf ? EnemyRadius : Targeting == CardTargeting.Self ? 0f : Range;

        public static NpcCardTraits From(CardDefinition card)
        {
            var t = new NpcCardTraits();
            if (card == null)
            {
                return t;
            }

            t.Element = card.element;
            t.Targeting = card.targeting;
            t.Range = card.range;
            t.ImpactDelay = card.impactDelay;
            foreach (SkillEffectSpec effect in card.effects)
            {
                switch (effect.kind)
                {
                    case SkillEffectKind.Damage:
                        t.Damage += effect.amount;
                        t.HitsEnemies = true;
                        break;
                    case SkillEffectKind.AreaDamage:
                        t.Damage += effect.amount;
                        t.EnemyRadius = Mathf.Max(t.EnemyRadius, effect.radius);
                        t.HitsEnemies = true;
                        break;
                    case SkillEffectKind.DamageOverTime:
                        t.Damage += effect.amount * effect.duration;
                        t.EnemyRadius = Mathf.Max(t.EnemyRadius, effect.radius);
                        t.HitsEnemies = true;
                        break;
                    case SkillEffectKind.Vulnerability:
                        t.Marks = true;
                        t.EnemyRadius = Mathf.Max(t.EnemyRadius, effect.radius);
                        t.HitsEnemies = true;
                        break;
                    case SkillEffectKind.Pull:
                        t.Pulls = true;
                        t.EnemyRadius = Mathf.Max(t.EnemyRadius, effect.radius);
                        t.HitsEnemies = true;
                        break;
                    case SkillEffectKind.Heal:
                        t.HealAmount += effect.amount;
                        t.AllyRadius = Mathf.Max(t.AllyRadius, effect.radius);
                        break;
                    case SkillEffectKind.Shield:
                        t.ShieldAmount += effect.amount;
                        t.AllyRadius = Mathf.Max(t.AllyRadius, effect.radius);
                        break;
                    case SkillEffectKind.DamageBuff:
                        t.BuffAmount += effect.amount;
                        t.AllyRadius = Mathf.Max(t.AllyRadius, effect.radius);
                        break;
                }
            }

            // Self-centred and aimed-only cards cannot reach enemies through an area they do not have.
            if (t.Targeting == CardTargeting.Self && t.EnemyRadius <= 0f)
            {
                t.HitsEnemies = false;
            }

            return t;
        }
    }
}
