using CoopDeck.Core;
using UnityEngine;

namespace CoopDeck.Combat
{
    /// <summary>
    /// Marks a collider as a body part of a <see cref="Health"/> owner (body, head, weak point,
    /// armor) so weapons and skills can scale damage per part.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class HitZone : MonoBehaviour, IDamageable
    {
        [SerializeField] Health owner;
        [SerializeField] HitZoneType zone = HitZoneType.Body;

        [Tooltip("Target-specific multiplier, e.g. 0.5 for an armored plate. Weapon headshot bonuses are applied on top for Head zones.")]
        [SerializeField, Min(0f)] float multiplier = 1f;

        public HitZoneType Zone => zone;
        public Health Owner => owner != null ? owner : owner = GetComponentInParent<Health>();
        public TeamId Team => Owner != null ? Owner.Team : TeamId.Neutral;
        public bool IsAlive => Owner != null && Owner.IsAlive;

        public void Configure(Health health, HitZoneType zoneType, float zoneMultiplier)
        {
            owner = health;
            zone = zoneType;
            multiplier = zoneMultiplier;
        }

        public DamageResult ApplyDamage(in DamageInfo info)
        {
            Health target = Owner;
            return target != null ? target.ApplyDamage(info, zone, multiplier) : default;
        }
    }
}
