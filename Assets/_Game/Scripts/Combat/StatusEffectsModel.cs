using System.Collections.Generic;
using CoopDeck.Core;
using UnityEngine;

namespace CoopDeck.Combat
{
    /// <summary>One damage-over-time tick produced by <see cref="StatusEffectsModel.Tick"/>.</summary>
    public readonly struct DotTick
    {
        public DotTick(ICombatActor source, TeamId sourceTeam, Element element, float amount)
        {
            Source = source;
            SourceTeam = sourceTeam;
            Element = element;
            Amount = amount;
        }

        public ICombatActor Source { get; }
        public TeamId SourceTeam { get; }
        public Element Element { get; }
        public float Amount { get; }
    }

    /// <summary>
    /// Timed combat modifiers as plain data: shield, outgoing damage buff, incoming damage
    /// debuff (vulnerability / marking) and damage over time. Non-stacking buffs keep the
    /// stronger value and the longer duration; damage over time stacks up to a cap.
    /// </summary>
    public sealed class StatusEffectsModel
    {
        public const int MaxDamageOverTimeStacks = 6;
        public const float DotTickInterval = 0.5f;

        struct DotInstance
        {
            public ICombatActor Source;
            public TeamId SourceTeam;
            public Element Element;
            public float AmountPerTick;
            public int TicksLeft;
            public float Timer;
        }

        readonly List<DotInstance> dots = new List<DotInstance>();

        float shield;
        float shieldRemaining;
        float buffValue;
        float buffRemaining;
        float vulnerabilityValue;
        float vulnerabilityRemaining;

        public float Shield => shieldRemaining > 0f ? shield : 0f;
        public float ShieldTimeRemaining => Shield > 0f ? shieldRemaining : 0f;

        /// <summary>Bonus to outgoing damage, e.g. 0.3 = +30 %.</summary>
        public float DamageBuff => buffRemaining > 0f ? buffValue : 0f;
        public float DamageBuffTimeRemaining => buffRemaining > 0f ? buffRemaining : 0f;

        /// <summary>Bonus to incoming damage, e.g. 0.3 = takes 30 % more.</summary>
        public float Vulnerability => vulnerabilityRemaining > 0f ? vulnerabilityValue : 0f;
        public float VulnerabilityTimeRemaining => vulnerabilityRemaining > 0f ? vulnerabilityRemaining : 0f;

        public float OutgoingDamageMultiplier => 1f + DamageBuff;
        public float IncomingDamageMultiplier => 1f + Vulnerability;
        public int DamageOverTimeCount => dots.Count;

        /// <summary>Most recent element applied to this actor (kept for the Phase 4 combo system).</summary>
        public Element LastElement { get; private set; }

        /// <summary>Seconds since <see cref="LastElement"/> was applied.</summary>
        public float LastElementAge { get; private set; } = float.PositiveInfinity;

        public void AddShield(float amount, float duration)
        {
            if (amount <= 0f || duration <= 0f)
            {
                return;
            }

            shield = Shield > 0f ? Mathf.Max(shield, amount) : amount;
            shieldRemaining = Mathf.Max(shieldRemaining, duration);
        }

        /// <summary>Lets the shield soak up damage. Returns the damage that gets through.</summary>
        public float AbsorbDamage(float amount, out float absorbed)
        {
            absorbed = 0f;
            if (amount <= 0f || Shield <= 0f)
            {
                return Mathf.Max(0f, amount);
            }

            absorbed = Mathf.Min(shield, amount);
            shield -= absorbed;
            if (shield <= 0.0001f)
            {
                shield = 0f;
                shieldRemaining = 0f;
            }

            return amount - absorbed;
        }

        public void AddDamageBuff(float bonus, float duration)
        {
            if (bonus <= 0f || duration <= 0f)
            {
                return;
            }

            buffValue = DamageBuff > 0f ? Mathf.Max(buffValue, bonus) : bonus;
            buffRemaining = Mathf.Max(buffRemaining, duration);
        }

        public void AddVulnerability(float bonus, float duration)
        {
            if (bonus <= 0f || duration <= 0f)
            {
                return;
            }

            vulnerabilityValue = Vulnerability > 0f ? Mathf.Max(vulnerabilityValue, bonus) : bonus;
            vulnerabilityRemaining = Mathf.Max(vulnerabilityRemaining, duration);
        }

        /// <summary>
        /// Adds damage over time. Total damage is exactly <paramref name="damagePerSecond"/> ×
        /// duration (rounded to whole ticks). When the cap is reached the weakest-remaining stack is replaced.
        /// </summary>
        public void AddDamageOverTime(ICombatActor source, TeamId sourceTeam, Element element, float damagePerSecond, float duration)
        {
            int ticks = Mathf.RoundToInt(duration / DotTickInterval);
            if (damagePerSecond <= 0f || ticks <= 0)
            {
                return;
            }

            if (dots.Count >= MaxDamageOverTimeStacks)
            {
                int weakest = 0;
                for (int i = 1; i < dots.Count; i++)
                {
                    if (dots[i].TicksLeft < dots[weakest].TicksLeft)
                    {
                        weakest = i;
                    }
                }

                dots.RemoveAt(weakest);
            }

            dots.Add(new DotInstance
            {
                Source = source,
                SourceTeam = sourceTeam,
                Element = element,
                AmountPerTick = damagePerSecond * DotTickInterval,
                TicksLeft = ticks,
            });
            MarkElement(element);
        }

        public void MarkElement(Element element)
        {
            if (element == Element.None)
            {
                return;
            }

            LastElement = element;
            LastElementAge = 0f;
        }

        /// <summary>Advances timers and appends due damage-over-time ticks to <paramref name="ticksOut"/>.</summary>
        public void Tick(float deltaTime, List<DotTick> ticksOut)
        {
            shieldRemaining = Mathf.Max(0f, shieldRemaining - deltaTime);
            buffRemaining = Mathf.Max(0f, buffRemaining - deltaTime);
            vulnerabilityRemaining = Mathf.Max(0f, vulnerabilityRemaining - deltaTime);
            LastElementAge += deltaTime;

            for (int i = dots.Count - 1; i >= 0; i--)
            {
                DotInstance dot = dots[i];
                dot.Timer += deltaTime;
                while (dot.Timer >= DotTickInterval && dot.TicksLeft > 0)
                {
                    dot.Timer -= DotTickInterval;
                    dot.TicksLeft--;
                    ticksOut?.Add(new DotTick(dot.Source, dot.SourceTeam, dot.Element, dot.AmountPerTick));
                }

                if (dot.TicksLeft <= 0)
                {
                    dots.RemoveAt(i);
                }
                else
                {
                    dots[i] = dot;
                }
            }
        }

        public void Clear()
        {
            dots.Clear();
            shield = shieldRemaining = 0f;
            buffValue = buffRemaining = 0f;
            vulnerabilityValue = vulnerabilityRemaining = 0f;
            LastElement = Element.None;
            LastElementAge = float.PositiveInfinity;
        }
    }
}
