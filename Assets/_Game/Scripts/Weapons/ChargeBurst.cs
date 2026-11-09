using System;
using System.Collections.Generic;
using CoopDeck.Core;
using UnityEngine;

namespace CoopDeck.Weapons
{
    /// <summary>How a <see cref="ChargeBurstSpec"/> discharge reaches past its primary target.</summary>
    public enum ChargeBurstShape
    {
        /// <summary>Jumps to the nearest hostiles one after another (VOLT-9 帯電連射).</summary>
        Chain = 0,

        /// <summary>A small explosion on the target: the nearest hostiles within a radius (IGNIS-27 火種刻印).</summary>
        Splash = 1,
    }

    /// <summary>
    /// A weapon's "consecutive hits on the same target" ability (the Thunderhop's 帯電連射,
    /// Docs/THUNDERHOP_SMG.md; the IGNIS-27's 火種刻印, Docs/IGNIS27_RIFLE.md). Disabled by default, so weapons
    /// without it are unchanged; the shape defaults to Chain and the status to none, so older data keeps its behaviour.
    /// </summary>
    [Serializable]
    public struct ChargeBurstSpec
    {
        public bool enabled;
        [Tooltip("Shown on the HUD (e.g. 帯電連射).")]
        public string displayName;

        [Header("Charge")]
        [Min(1)] public int requiredStacks;
        [Tooltip("Stacks reset when the same target is not hit again within this many seconds.")]
        [Min(0.05f)] public float maxSecondsBetweenHits;
        [Min(1)] public int stacksPerHit;

        [Header("Discharge")]
        [Min(0f)] public float primaryDamage;
        [Min(0)] public int chainTargets;
        [Min(0f)] public float chainRange;
        [Min(0f)] public float chainDamage;
        [Tooltip("Element applied through the existing element / combo system (aura length and combo cooldown are the combo system's).")]
        public Element element;
        [Tooltip("Seconds before the same target can be discharged on again. A full charge waits for it.")]
        [Min(0f)] public float perTargetCooldown;

        [Tooltip("Chain: jumps from the target to the nearest hostiles one after another (VOLT-9). Splash: a small explosion on the target that reaches the nearest hostiles within a radius (IGNIS-27).")]
        public ChargeBurstShape shape;

        [Header("Splash (shape Splash)")]
        [Tooltip("Hostiles whose colliders come this close to the burst point take the splash.")]
        [Min(0f)] public float splashRadius;
        [Tooltip("Most hostiles the splash reaches besides the target (nearest first).")]
        [Min(0)] public int splashTargets;
        [Min(0f)] public float splashDamage;

        [Header("Status on every target of the discharge")]
        [Tooltip("Damage per second of the existing damage over time (CombatStatus, a burn for fire), on the target and every chain / splash target. 0 = none. Ticks are status damage, never bullets, so they add no stacks.")]
        [Min(0f)] public float statusDamagePerSecond;
        [Min(0f)] public float statusDuration;

        [Header("Presentation")]
        [Tooltip("Hit on the primary target (flash, sparks, surface arcs).")]
        public GameObject burstFx;
        [Tooltip("Bolt (LightningBoltFx, or an older LightningArcFx) from the muzzle to the primary target and on to each chained target.")]
        public GameObject arcFx;
        [Tooltip("Smaller hit on each chained target. Empty: the primary hit.")]
        public GameObject chainBurstFx;
        [Tooltip("Small crackle at the muzzle while the charge is half full or more.")]
        public GameObject chargeFx;
    }

    /// <summary>
    /// The rules of <see cref="ChargeBurstSpec"/> as plain data (EditMode-tested): bullets that
    /// hit add stacks on their target; switching target or pausing longer than the gap resets
    /// them; at the required count the discharge fires unless that target is cooling down, in
    /// which case the charge stays full and fires on the first hit after the cooldown. Only the
    /// owning weapon's bullets call <see cref="RegisterHit"/>, so the discharge's own damage (and
    /// any combo it starts) can never add stacks.
    /// </summary>
    public sealed class ChargeBurstModel
    {
        const int PruneThreshold = 32;

        readonly ChargeBurstSpec spec;
        readonly Dictionary<object, float> readyAt = new Dictionary<object, float>();
        readonly List<object> pruneBuffer = new List<object>();
        object target;
        int stacks;
        float clock;
        float lastHit = float.NegativeInfinity;

        public ChargeBurstModel(ChargeBurstSpec spec)
        {
            this.spec = spec;
        }

        public ChargeBurstSpec Spec => spec;
        public bool Enabled => spec.enabled;
        public int Stacks => stacks;
        public int RequiredStacks => Mathf.Max(1, spec.requiredStacks);
        public float Progress => stacks / (float)RequiredStacks;
        public object Target => target;

        /// <summary>Discharges so far (tests, HUD).</summary>
        public int Discharges { get; private set; }

        /// <summary>Full, but the current target is still cooling down.</summary>
        public bool WaitingForCooldown => target != null && stacks >= RequiredStacks && CooldownRemaining(target) > 0f;

        public float CooldownRemaining(object t)
        {
            return t != null && readyAt.TryGetValue(t, out float ready) ? Mathf.Max(0f, ready - clock) : 0f;
        }

        /// <summary>Advances time: a target not hit again in time loses its stacks.</summary>
        public void Tick(float deltaTime)
        {
            clock += Mathf.Max(0f, deltaTime);
            if (target != null && clock - lastHit > spec.maxSecondsBetweenHits)
            {
                Reset();
            }
        }

        /// <summary>A bullet from the owning weapon hit <paramref name="hitTarget"/>. Returns true when the discharge fires on it.</summary>
        public bool RegisterHit(object hitTarget)
        {
            if (!spec.enabled || hitTarget == null)
            {
                return false;
            }

            if (!ReferenceEquals(hitTarget, target) || clock - lastHit > spec.maxSecondsBetweenHits)
            {
                target = hitTarget;
                stacks = 0;
            }

            lastHit = clock;
            stacks = Mathf.Min(RequiredStacks, stacks + Mathf.Max(1, spec.stacksPerHit));
            if (stacks < RequiredStacks || CooldownRemaining(hitTarget) > 0f)
            {
                return false;
            }

            stacks = 0;
            Discharges++;
            Prune();
            readyAt[hitTarget] = clock + spec.perTargetCooldown;
            return true;
        }

        /// <summary>The target died or left: its stacks are gone.</summary>
        public void ForgetTarget(object t)
        {
            if (ReferenceEquals(t, target))
            {
                Reset();
            }
        }

        public void Reset()
        {
            target = null;
            stacks = 0;
        }

        void Prune()
        {
            if (readyAt.Count < PruneThreshold)
            {
                return;
            }

            pruneBuffer.Clear();
            foreach (KeyValuePair<object, float> pair in readyAt)
            {
                if (pair.Value <= clock)
                {
                    pruneBuffer.Add(pair.Key);
                }
            }

            foreach (object key in pruneBuffer)
            {
                readyAt.Remove(key);
            }
        }
    }
}
