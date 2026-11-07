using System;
using CoopDeck.Core;
using UnityEngine;

namespace CoopDeck.Combat
{
    /// <summary>
    /// Hit points for anything that can be damaged. Hits normally arrive through a
    /// <see cref="HitZone"/> on a collider; a collider without one counts as Body.
    /// </summary>
    [DisallowMultipleComponent]
    public class Health : MonoBehaviour, IDamageable
    {
        [SerializeField, Min(1f)] float maxHealth = 100f;
        [SerializeField] TeamId team = TeamId.Enemies;

        HealthModel model;

        /// <summary>Raised after damage was applied (not for blocked hits).</summary>
        public event Action<DamageInfo, DamageResult> Damaged;

        public event Action<Health, DamageInfo> Died;
        public event Action<Health> Revived;
        public event Action<Health> Changed;

        HealthModel Model => model ??= new HealthModel(maxHealth);

        public float Current => Model.Current;
        public float Max => Model.Max;
        public float Normalized => Model.Normalized;
        public bool IsAlive => Model.IsAlive;

        public TeamId Team
        {
            get => team;
            set => team = value;
        }

        public DamageResult ApplyDamage(in DamageInfo info)
        {
            return ApplyDamage(info, HitZoneType.Body, 1f);
        }

        public DamageResult ApplyDamage(in DamageInfo info, HitZoneType zone, float zoneMultiplier)
        {
            var result = new DamageResult
            {
                Zone = zone,
                Flags = info.Flags,
                RemainingHealth = Current,
            };

            if (!IsAlive || !DamageRules.CanDamage(info.SourceTeam, team))
            {
                return result;
            }

            float multiplier = DamageRules.ZoneMultiplier(zone, zoneMultiplier, info.HeadshotMultiplier);
            float dealt = Model.TakeDamage(DamageRules.Compute(info.Amount, multiplier));

            if (zone == HitZoneType.Head)
            {
                result.Flags |= DamageFlags.Headshot;
            }

            result.Applied = dealt > 0f;
            result.Damage = dealt;
            result.Killed = !Model.IsAlive;
            result.RemainingHealth = Current;

            if (result.Applied)
            {
                Damaged?.Invoke(info, result);
                Changed?.Invoke(this);
                CombatEvents.RaiseDamageApplied(new DamageEvent(this, info, result));
            }

            if (result.Killed)
            {
                Died?.Invoke(this, info);
                CombatEvents.RaiseDied(this, info);
            }

            return result;
        }

        public float Heal(float amount)
        {
            float healed = Model.Heal(amount);
            if (healed > 0f)
            {
                Changed?.Invoke(this);
            }

            return healed;
        }

        /// <summary>Back to full health (respawn, revive, test reset).</summary>
        public void ResetHealth()
        {
            Model.ResetToFull();
            Revived?.Invoke(this);
            Changed?.Invoke(this);
        }

        public void SetMaxHealth(float max, bool refill)
        {
            maxHealth = max;
            Model.SetMax(max, refill);
            Changed?.Invoke(this);
        }
    }
}
