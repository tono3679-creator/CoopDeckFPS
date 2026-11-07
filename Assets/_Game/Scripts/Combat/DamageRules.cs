using CoopDeck.Core;
using UnityEngine;

namespace CoopDeck.Combat
{
    /// <summary>Pure damage math, kept free of MonoBehaviours so it is easy to test and to run on a host.</summary>
    public static class DamageRules
    {
        public static bool CanDamage(TeamId attacker, TeamId target)
        {
            return target == TeamId.Neutral || attacker == TeamId.Neutral || attacker != target;
        }

        /// <summary>Final multiplier for a hit: the zone's own multiplier, times the weapon's headshot bonus on heads.</summary>
        public static float ZoneMultiplier(HitZoneType zone, float zoneMultiplier, float headshotMultiplier)
        {
            float multiplier = Mathf.Max(0f, zoneMultiplier);
            if (zone == HitZoneType.Head)
            {
                multiplier *= headshotMultiplier > 0f ? headshotMultiplier : 1f;
            }

            return multiplier;
        }

        public static float Compute(float baseAmount, float multiplier)
        {
            return Mathf.Max(0f, baseAmount * multiplier);
        }

        /// <summary>
        /// Linear falloff: full damage up to <paramref name="falloffStart"/>, then down to
        /// <paramref name="minFactor"/> at <paramref name="maxRange"/>.
        /// </summary>
        public static float DistanceFalloff(float distance, float falloffStart, float maxRange, float minFactor)
        {
            if (distance <= falloffStart || maxRange <= falloffStart)
            {
                return 1f;
            }

            float t = Mathf.InverseLerp(falloffStart, maxRange, distance);
            return Mathf.Lerp(1f, Mathf.Clamp01(minFactor), t);
        }
    }
}
