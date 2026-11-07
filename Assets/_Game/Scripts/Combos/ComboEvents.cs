using System;
using CoopDeck.Combat;
using UnityEngine;

namespace CoopDeck.Combos
{
    /// <summary>One combo, as seen by HUD, tests and analytics.</summary>
    public struct ComboReport
    {
        public ComboDefinition Definition;
        public ComboTier Tier;
        public ComboTrigger Trigger;

        /// <summary>The enemy the combo was completed on (may already be dead).</summary>
        public Health Target;

        public Vector3 Position;

        /// <summary>Filled in <see cref="ComboEvents.Finished"/> (the singularity's blast comes later).</summary>
        public int EnemiesHit;

        public float TotalDamage;

        public bool IsCoop => Trigger.IsCoop;
    }

    /// <summary>
    /// Combo notifications. <see cref="Triggered"/> fires the moment a combo starts (name display,
    /// sound); <see cref="Finished"/> once all of its damage is applied.
    /// </summary>
    public static class ComboEvents
    {
        public static event Action<ComboReport> Triggered;
        public static event Action<ComboReport> Finished;

        public static void RaiseTriggered(in ComboReport report) => Triggered?.Invoke(report);

        public static void RaiseFinished(in ComboReport report) => Finished?.Invoke(report);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Triggered = null;
            Finished = null;
        }
    }
}
