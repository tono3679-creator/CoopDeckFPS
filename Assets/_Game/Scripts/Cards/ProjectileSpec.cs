using System;
using UnityEngine;

namespace CoopDeck.Cards
{
    /// <summary>
    /// Makes a card's skill fly: the cast launches a projectile along the crosshair and the card's effects
    /// (damage, area, burn, element hit) resolve where it lands, through the same code as instant skills.
    /// Speed 0 (the default) keeps the skill instant.
    /// Passive hooks: a bigger projectile raises <see cref="visualScale"/> (and the card's area radius for
    /// the blast); split shots, homing or extra launches belong here as new fields read by SkillCaster.
    /// </summary>
    [Serializable]
    public struct ProjectileSpec
    {
        [Tooltip("Metres per second. 0 = not a projectile: the skill lands instantly on the aim point.")]
        [Min(0f)] public float speed;

        [Tooltip("Collision radius of the flying projectile (metres).")]
        [Min(0f)] public float radius;

        [Tooltip("Seconds from the press to the launch, matched to the cast motion (the palm reaches forward). " +
                 "The charge effect plays in the hand meanwhile.")]
        [Min(0f)] public float launchDelay;

        [Tooltip("Size of the flying effect, 1 = as authored.")]
        [Min(0f)] public float visualScale;

        public bool IsProjectile => speed > 0f;

        public float Radius => radius > 0f ? radius : 0.2f;

        public float VisualScale => visualScale > 0f ? visualScale : 1f;
    }
}
