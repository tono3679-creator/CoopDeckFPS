using System;
using System.Collections.Generic;
using CoopDeck.Core;
using UnityEngine;

namespace CoopDeck.Combat
{
    /// <summary>Why an element left a target (VFX and UI react differently).</summary>
    public enum ElementRemoveReason
    {
        Expired = 0,

        /// <summary>Used up by a combo.</summary>
        Consumed = 1,

        /// <summary>Death, respawn or a reset.</summary>
        Cleared = 2,
    }

    /// <summary>One element sitting on a target, with who put it there (for combos and co-op credit).</summary>
    public struct ElementAura
    {
        public Element Element;

        /// <summary>Seconds until it wears off.</summary>
        public float Remaining;

        /// <summary>Seconds since it was applied (or last refreshed).</summary>
        public float Age;

        /// <summary>Who applied it. May be null (destroyed source); <see cref="ApplierId"/> stays valid.</summary>
        public ICombatActor Applier;

        /// <summary>Stable actor id (<see cref="CombatantRegistry.IdOf"/>); 0 = unknown.</summary>
        public int ApplierId;

        public TeamId ApplierTeam;

        /// <summary>Upgrade level of the card that applied it (decides the combo tier).</summary>
        public int Power;

        public bool IsActive => Element != Element.None && Remaining > 0f;
    }

    /// <summary>
    /// Elements applied to one target as plain data: apply / refresh, duration, consume (combo),
    /// expiry and clearing, plus per-reaction cooldowns so the same combo cannot fire again on the
    /// same target too soon. Several different elements can sit on a target at once.
    /// Element side effects: Ice slows movement, Gravity weighs it down a little.
    /// </summary>
    public sealed class ElementAuraModel
    {
        /// <summary>How long an element stays (the combo window): 4 s.</summary>
        public const float DefaultDuration = 4f;

        public const float IceMoveMultiplier = 0.55f;
        public const float GravityMoveMultiplier = 0.8f;

        // Indexed by (int)Element; slot 0 (None) is never used.
        readonly ElementAura[] auras = new ElementAura[5];
        readonly Dictionary<int, float> cooldowns = new Dictionary<int, float>();
        readonly List<int> expiredCooldowns = new List<int>();
        readonly List<int> keyBuffer = new List<int>();

        public event Action<ElementAura> Applied;
        public event Action<Element, ElementRemoveReason> Removed;

        public int ActiveCount
        {
            get
            {
                int count = 0;
                for (int i = 1; i < auras.Length; i++)
                {
                    if (auras[i].IsActive)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        /// <summary>Movement speed factor from the current elements (1 = unaffected).</summary>
        public float MoveSpeedMultiplier =>
            (Has(Element.Ice) ? IceMoveMultiplier : 1f) * (Has(Element.Gravity) ? GravityMoveMultiplier : 1f);

        public bool Has(Element element) => Valid(element) && auras[(int)element].IsActive;

        public bool TryGet(Element element, out ElementAura aura)
        {
            if (Has(element))
            {
                aura = auras[(int)element];
                return true;
            }

            aura = default;
            return false;
        }

        /// <summary>Adds the element, or refreshes its duration, applier and power if already present.</summary>
        public void Apply(Element element, ICombatActor applier, int applierId, TeamId applierTeam, int power, float duration = DefaultDuration)
        {
            if (!Valid(element) || duration <= 0f)
            {
                return;
            }

            var aura = new ElementAura
            {
                Element = element,
                Remaining = duration,
                Age = 0f,
                Applier = applier,
                ApplierId = applierId,
                ApplierTeam = applierTeam,
                Power = Mathf.Max(0, power),
            };
            auras[(int)element] = aura;
            Applied?.Invoke(aura);
        }

        /// <summary>Removes the element because a combo used it. Returns false if it was not present.</summary>
        public bool Consume(Element element)
        {
            if (!Has(element))
            {
                return false;
            }

            auras[(int)element] = default;
            Removed?.Invoke(element, ElementRemoveReason.Consumed);
            return true;
        }

        /// <summary>Removes every element (death, respawn). Cooldowns are kept unless <paramref name="includeCooldowns"/>.</summary>
        public void Clear(bool includeCooldowns = false)
        {
            for (int i = 1; i < auras.Length; i++)
            {
                if (auras[i].IsActive)
                {
                    auras[i] = default;
                    Removed?.Invoke((Element)i, ElementRemoveReason.Cleared);
                }
            }

            if (includeCooldowns)
            {
                cooldowns.Clear();
            }
        }

        public void Tick(float deltaTime)
        {
            for (int i = 1; i < auras.Length; i++)
            {
                if (!auras[i].IsActive)
                {
                    continue;
                }

                auras[i].Remaining -= deltaTime;
                auras[i].Age += deltaTime;
                if (auras[i].Remaining <= 0f)
                {
                    auras[i] = default;
                    Removed?.Invoke((Element)i, ElementRemoveReason.Expired);
                }
            }

            if (cooldowns.Count == 0)
            {
                return;
            }

            expiredCooldowns.Clear();
            keyBuffer.Clear();
            keyBuffer.AddRange(cooldowns.Keys);
            foreach (int key in keyBuffer)
            {
                float left = cooldowns[key] - deltaTime;
                if (left <= 0f)
                {
                    expiredCooldowns.Add(key);
                }
                else
                {
                    cooldowns[key] = left;
                }
            }

            foreach (int key in expiredCooldowns)
            {
                cooldowns.Remove(key);
            }
        }

        /// <summary>Seconds before reaction <paramref name="reactionId"/> may fire on this target again.</summary>
        public float CooldownRemaining(int reactionId) => cooldowns.TryGetValue(reactionId, out float left) ? left : 0f;

        public void StartCooldown(int reactionId, float seconds)
        {
            if (seconds > 0f)
            {
                cooldowns[reactionId] = seconds;
            }
        }

        public IEnumerable<ElementAura> ActiveAuras()
        {
            for (int i = 1; i < auras.Length; i++)
            {
                if (auras[i].IsActive)
                {
                    yield return auras[i];
                }
            }
        }

        static bool Valid(Element element) => element > Element.None && (int)element < 5;
    }
}
