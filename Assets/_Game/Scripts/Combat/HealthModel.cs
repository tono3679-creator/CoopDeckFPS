using UnityEngine;

namespace CoopDeck.Combat
{
    /// <summary>Hit points as plain data. The host will own this state once networking lands.</summary>
    public sealed class HealthModel
    {
        public HealthModel(float max)
        {
            Max = Mathf.Max(1f, max);
            Current = Max;
        }

        public float Max { get; private set; }
        public float Current { get; private set; }
        public bool IsAlive => Current > 0f;
        public float Normalized => Current / Max;

        /// <summary>Removes up to <paramref name="amount"/> HP and returns how much was actually removed.</summary>
        public float TakeDamage(float amount)
        {
            if (!IsAlive || amount <= 0f)
            {
                return 0f;
            }

            float dealt = Mathf.Min(Current, amount);
            Current -= dealt;
            if (Current < 0.0001f)
            {
                Current = 0f;
            }

            return dealt;
        }

        /// <summary>Restores up to <paramref name="amount"/> HP (never above max, never revives) and returns the amount healed.</summary>
        public float Heal(float amount)
        {
            if (!IsAlive || amount <= 0f)
            {
                return 0f;
            }

            float healed = Mathf.Min(Max - Current, amount);
            Current += healed;
            return healed;
        }

        public void ResetToFull()
        {
            Current = Max;
        }

        public void SetMax(float max, bool refill)
        {
            Max = Mathf.Max(1f, max);
            Current = refill ? Max : Mathf.Min(Current, Max);
        }
    }
}
