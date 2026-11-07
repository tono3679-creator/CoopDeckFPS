using System;
using UnityEngine;

namespace CoopDeck.Cards
{
    public enum CardType
    {
        Active = 0,

        /// <summary>Stays in effect for the whole run; never enters the draw pile.</summary>
        Passive = 1,
    }

    public enum CardRarity
    {
        Common = 0,
        Rare = 1,
        Epic = 2,
        Legendary = 3,
    }

    /// <summary>Where a card's effects are centred.</summary>
    public enum CardTargeting
    {
        /// <summary>The caster.</summary>
        Self = 0,

        /// <summary>Instant ray along the crosshair; effects use the enemy hit (if any) and the hit point.</summary>
        Aim = 1,

        /// <summary>The point under the crosshair (ground-targeted area skills).</summary>
        AimPoint = 2,

        /// <summary>An area centred on the caster.</summary>
        AreaAroundSelf = 3,
    }

    /// <summary>
    /// Shared effect building blocks. A new card is a new combination of these; only a genuinely
    /// new kind of effect needs code (in CoopDeck.Skills).
    /// </summary>
    public enum SkillEffectKind
    {
        /// <summary>Damage to the aimed enemy, optionally chaining to nearby enemies.</summary>
        Damage = 0,

        /// <summary>Damage to every enemy within <see cref="SkillEffectSpec.radius"/>.</summary>
        AreaDamage = 1,

        /// <summary>Damage per second for a duration (aimed enemy, or every enemy in the radius).</summary>
        DamageOverTime = 2,

        /// <summary>Restores HP to allies in the radius (caster included).</summary>
        Heal = 3,

        /// <summary>Absorbs incoming damage for a duration (caster, or allies in the radius).</summary>
        Shield = 4,

        /// <summary>Raises outgoing damage (weapon and skills) for a duration.</summary>
        DamageBuff = 5,

        /// <summary>Marks enemies so they take more damage for a duration.</summary>
        Vulnerability = 6,

        /// <summary>Drags enemies in the radius toward the effect centre.</summary>
        Pull = 7,
    }

    /// <summary>One effect of a card. Field meaning depends on <see cref="kind"/> (see tooltips).</summary>
    [Serializable]
    public struct SkillEffectSpec
    {
        public SkillEffectKind kind;

        [Tooltip("Damage / heal / shield amount; damage per second for DamageOverTime; bonus fraction for DamageBuff and Vulnerability (0.3 = +30%).")]
        public float amount;

        [Tooltip("Area radius in metres. 0 = only the aimed target (or only the caster).")]
        public float radius;

        [Tooltip("Seconds for DamageOverTime, Shield, DamageBuff, Vulnerability and Pull.")]
        public float duration;

        [Tooltip("Damage only: how many extra enemies the hit jumps to.")]
        public int chainCount;

        [Tooltip("Damage only: how far each chain jump reaches.")]
        public float chainRadius;

        [Tooltip("Pull only: metres per second toward the centre.")]
        public float pullSpeed;

        public static SkillEffectSpec Damage(float amount, int chainCount = 0, float chainRadius = 0f) =>
            new SkillEffectSpec { kind = SkillEffectKind.Damage, amount = amount, chainCount = chainCount, chainRadius = chainRadius };

        public static SkillEffectSpec Area(float amount, float radius) =>
            new SkillEffectSpec { kind = SkillEffectKind.AreaDamage, amount = amount, radius = radius };

        public static SkillEffectSpec DamageOverTime(float damagePerSecond, float duration, float radius = 0f) =>
            new SkillEffectSpec { kind = SkillEffectKind.DamageOverTime, amount = damagePerSecond, duration = duration, radius = radius };

        public static SkillEffectSpec Heal(float amount, float radius) =>
            new SkillEffectSpec { kind = SkillEffectKind.Heal, amount = amount, radius = radius };

        public static SkillEffectSpec Shield(float amount, float duration, float radius = 0f) =>
            new SkillEffectSpec { kind = SkillEffectKind.Shield, amount = amount, duration = duration, radius = radius };

        public static SkillEffectSpec Buff(float bonus, float duration, float radius = 0f) =>
            new SkillEffectSpec { kind = SkillEffectKind.DamageBuff, amount = bonus, duration = duration, radius = radius };

        public static SkillEffectSpec Mark(float bonus, float duration, float radius = 0f) =>
            new SkillEffectSpec { kind = SkillEffectKind.Vulnerability, amount = bonus, duration = duration, radius = radius };

        public static SkillEffectSpec PullTo(float radius, float speed, float duration) =>
            new SkillEffectSpec { kind = SkillEffectKind.Pull, radius = radius, pullSpeed = speed, duration = duration };
    }
}
