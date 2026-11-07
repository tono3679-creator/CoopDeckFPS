using System;
using CoopDeck.Core;
using UnityEngine;

namespace CoopDeck.Combos
{
    /// <summary>How a combo hurts: chained lightning, a pull then a blast, or one big explosion.</summary>
    public enum ComboKind
    {
        /// <summary>Burst at the target (radius) and jumps to nearby enemies (superconductor).</summary>
        ChainBurst = 0,

        /// <summary>Pulls enemies in, then explodes after the pull (burning singularity).</summary>
        Singularity = 1,

        /// <summary>One explosion around the target (overload).</summary>
        Explosion = 2,
    }

    /// <summary>Growth stage of a combo, from the upgrade level of the cards involved.</summary>
    public enum ComboTier
    {
        /// <summary>Small effect.</summary>
        Normal = 0,

        /// <summary>More chains, bigger area.</summary>
        Enhanced = 1,

        /// <summary>Large fused effect.</summary>
        Ultimate = 2,
    }

    /// <summary>Numbers and effects for one tier of a combo. Unused fields are ignored by the kind.</summary>
    [Serializable]
    public class ComboTierSettings
    {
        [Tooltip("Damage to each enemy hit (before the co-op bonus). Chain jumps lose Chain Falloff per jump.")]
        [Min(0f)] public float damage = 40f;

        [Tooltip("ChainBurst: burst radius around the target. Explosion / Singularity: blast radius.")]
        [Min(0f)] public float radius = 4f;

        [Tooltip("ChainBurst: how many extra enemies the lightning jumps to.")]
        [Min(0)] public int chainCount = 3;

        [Tooltip("ChainBurst: reach of each jump.")]
        [Min(0f)] public float chainRange = 8f;

        [Tooltip("ChainBurst: damage factor per jump.")]
        [Range(0.1f, 1f)] public float chainFalloff = 0.85f;

        [Tooltip("Singularity: pull radius.")]
        [Min(0f)] public float pullRadius = 7f;

        [Tooltip("Singularity: metres per second toward the centre.")]
        [Min(0f)] public float pullSpeed = 9f;

        [Tooltip("Singularity: seconds of pull before the blast.")]
        [Min(0f)] public float pullDuration = 0.9f;

        [Tooltip("Camera shake strength 0..1 (small, capped by the camera).")]
        [Range(0f, 1f)] public float shake = 0.25f;

        [Tooltip("Main effect at the target, authored for a 1 m radius and scaled to Radius.")]
        public GameObject fx;

        [Tooltip("Per-hit burst (chain targets) or the singularity's blast.")]
        public GameObject hitFx;
    }

    /// <summary>
    /// One element combo: which two elements react, how long the first one stays usable, the
    /// re-trigger cooldown per target, the co-op bonus, and the three growth tiers. Data only:
    /// effects are referenced, so swapping them never changes damage.
    /// </summary>
    [CreateAssetMenu(menuName = "CoopDeck/Combo", fileName = "CB_NewCombo")]
    public class ComboDefinition : ScriptableObject
    {
        public const int TierCount = 3;

        public string comboId = "combo.new";
        public string displayName = "New Combo";
        public Color color = Color.white;
        public Element elementA = Element.Ice;
        public Element elementB = Element.Lightning;
        public ComboKind kind = ComboKind.ChainBurst;

        [Tooltip("Seconds after the first element was applied during which the second completes the combo.")]
        [Min(0.1f)] public float window = 4f;

        [Tooltip("Seconds before this combo can fire on the same target again.")]
        [Min(0f)] public float cooldown = 3f;

        [Tooltip("Damage multiplier when a different ally applied the first element.")]
        [Min(1f)] public float coopMultiplier = 1.3f;

        [Tooltip("Normal / Enhanced / Ultimate.")]
        public ComboTierSettings[] tiers = { new ComboTierSettings(), new ComboTierSettings(), new ComboTierSettings() };

        [Tooltip("ChainBurst: arc drawn between chained enemies.")]
        public GameObject arcFx;

        public ComboTierSettings Tier(ComboTier tier)
        {
            if (tiers == null || tiers.Length == 0)
            {
                return new ComboTierSettings();
            }

            int index = Mathf.Clamp((int)tier, 0, tiers.Length - 1);
            return tiers[index] ?? tiers[0] ?? new ComboTierSettings();
        }

        public ComboRule ToRule(int id) => new ComboRule(id, elementA, elementB, window, cooldown, coopMultiplier);

        public static string TierLabel(ComboTier tier)
        {
            switch (tier)
            {
                case ComboTier.Enhanced: return "強化";
                case ComboTier.Ultimate: return "究極";
                default: return "";
            }
        }
    }
}
