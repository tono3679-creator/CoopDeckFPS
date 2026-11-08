using System.Collections.Generic;
using CoopDeck.Core;
using UnityEngine;

namespace CoopDeck.Cards
{
    /// <summary>
    /// Immutable, shared data for one kind of card. Per-copy state (gauge, cooldown, upgrade
    /// level) lives in the runtime card (CoopDeck.Deck.CardRuntime), so a deck can hold several
    /// copies of the same definition without their gauges mixing.
    /// </summary>
    [CreateAssetMenu(menuName = "CoopDeck/Card Definition", fileName = "CD_NewCard")]
    public class CardDefinition : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Stable unique id used by saves and the network, e.g. card.chain_lightning.")]
        public string cardId = "card.new";
        public string displayName = "New Card";
        [TextArea(2, 4)] public string description = "";
        public CardType type = CardType.Active;
        public Element element = Element.None;
        public CardRarity rarity = CardRarity.Common;

        [Header("Gauge")]
        [Min(1f)] public float maxGauge = 100f;
        [Tooltip("Gauge spent per use. The last use is allowed at full power even if less remains.")]
        [Min(1f)] public float gaugeCost = 25f;
        [Tooltip("Seconds before this card can be used again (anti-spam; separate from the gauge).")]
        [Min(0f)] public float cooldown = 0.5f;

        [Header("Casting")]
        public CardTargeting targeting = CardTargeting.Aim;
        [Min(1f)] public float range = 40f;
        [Tooltip("Seconds between casting and the effects landing (meteor-style). 0 = instant.")]
        [Min(0f)] public float impactDelay;
        public List<SkillEffectSpec> effects = new List<SkillEffectSpec>();

        [Header("Presentation")]
        public Sprite icon;
        [Tooltip("Line from the hand to the target point (Aim / AimPoint).")]
        public GameObject beamFx;
        [Tooltip("Burst on each enemy hit.")]
        public GameObject impactFx;
        [Tooltip("Burst sized to the effect radius at the effect centre.")]
        public GameObject areaFx;
        [Tooltip("Played on each ally that receives a heal / shield / buff.")]
        public GameObject auraFx;
        [Tooltip("Shown at the target point during impactDelay.")]
        public GameObject telegraphFx;
        [Tooltip("The impact effect clings to the enemy it hit (follows it) instead of staying where it hit.")]
        public bool attachImpactFx;

        [Header("Projectile")]
        [Tooltip("Speed above 0 makes the skill fly from the casting hand and resolve where it lands.")]
        public ProjectileSpec projectile;
        [Tooltip("Charge in the casting hand until the projectile launches.")]
        public GameObject castFx;
        [Tooltip("The flying projectile; follows the simulated shot (Area Fx is the blast where it lands).")]
        public GameObject projectileFx;

        /// <summary>Uses from a full gauge, counting the final partial use.</summary>
        public int UsesFromFull => Mathf.CeilToInt(maxGauge / Mathf.Max(1f, gaugeCost) - 0.0001f);

        /// <summary>Largest effect radius, used to size area effects.</summary>
        public float MaxRadius
        {
            get
            {
                float radius = 0f;
                foreach (SkillEffectSpec effect in effects)
                {
                    radius = Mathf.Max(radius, effect.radius);
                }

                return radius;
            }
        }

        public bool HasEffect(SkillEffectKind kind)
        {
            foreach (SkillEffectSpec effect in effects)
            {
                if (effect.kind == kind)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
