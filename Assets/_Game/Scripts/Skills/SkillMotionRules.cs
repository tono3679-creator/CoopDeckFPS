using CoopDeck.Animation;
using CoopDeck.Cards;

namespace CoopDeck.Skills
{
    /// <summary>
    /// Which shared cast motion a card plays (no per-card clips): legendary cards use the ultimate, area-on-the-floor
    /// cards slam a field, cards that only help the caster or nearby allies are a self buff, slow (delayed impact)
    /// cards are a charged cast, everything else is a quick palm thrust. Passives that split, home or explode reuse
    /// the base card's motion.
    /// </summary>
    public static class SkillMotionRules
    {
        public const float ChargedImpactDelay = 0.3f;

        public static SkillMotion For(CardDefinition card)
        {
            if (card == null)
            {
                return SkillMotion.QuickCast;
            }

            if (card.rarity == CardRarity.Legendary)
            {
                return SkillMotion.Ultimate;
            }

            bool hurts = card.HasEffect(SkillEffectKind.Damage) || card.HasEffect(SkillEffectKind.AreaDamage)
                         || card.HasEffect(SkillEffectKind.DamageOverTime) || card.HasEffect(SkillEffectKind.Pull)
                         || card.HasEffect(SkillEffectKind.Vulnerability);
            if (!hurts && (card.targeting == CardTargeting.Self || card.targeting == CardTargeting.AreaAroundSelf))
            {
                return SkillMotion.SelfBuff;
            }

            if (card.targeting == CardTargeting.AimPoint || card.targeting == CardTargeting.AreaAroundSelf)
            {
                return SkillMotion.AreaCast;
            }

            return card.impactDelay >= ChargedImpactDelay ? SkillMotion.Charged : SkillMotion.QuickCast;
        }
    }
}
