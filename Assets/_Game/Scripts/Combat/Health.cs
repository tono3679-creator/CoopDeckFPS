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
            float amount = DamageRules.Compute(info.Amount, multiplier);

            // Vulnerability (marking) raises incoming damage first, then a shield soaks it up.
            float absorbed = 0f;
            if (TryGetComponent(out CombatStatus status))
            {
                amount *= status.Model.IncomingDamageMultiplier;
                amount = status.Model.AbsorbDamage(amount, out absorbed);
                status.Model.MarkElement(info.Element);
            }

            float dealt = Model.TakeDamage(amount);

            if (zone == HitZoneType.Head)
            {
                result.Flags |= DamageFlags.Headshot;
            }
            else if (zone == HitZoneType.Weakpoint)
            {
                result.Flags |= DamageFlags.Critical;
            }

            result.Applied = dealt > 0f || absorbed > 0f;
            result.Damage = dealt;
            result.Absorbed = absorbed;
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

        /// <summary>Revive from 0 HP with a fraction of max health (ally revive). False if not down.</summary>
        public bool Revive(float fraction)
        {
            if (!Model.Revive(Max * Mathf.Clamp01(fraction)))
            {
                return false;
            }

            Revived?.Invoke(this);
            Changed?.Invoke(this);
            return true;
        }

        public void SetMaxHealth(float max, bool refill)
        {
            maxHealth = max;
            Model.SetMax(max, refill);
            Changed?.Invoke(this);
        }
    }
}
