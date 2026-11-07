using System;
using UnityEngine;

namespace CoopDeck.Combat
{
    public readonly struct DamageEvent
    {
        public DamageEvent(Health target, DamageInfo info, DamageResult result)
        {
            Target = target;
            Info = info;
            Result = result;
        }

        public Health Target { get; }
        public DamageInfo Info { get; }
        public DamageResult Result { get; }
    }

    /// <summary>
    /// Global combat notifications. HUD feedback, the future combo system and analytics listen
    /// here so damage code never needs to know about them.
    /// </summary>
    public static class CombatEvents
    {
        public static event Action<DamageEvent> DamageApplied;
        public static event Action<Health, DamageInfo> Died;

        public static void RaiseDamageApplied(in DamageEvent damageEvent)
        {
            DamageApplied?.Invoke(damageEvent);
        }

        public static void RaiseDied(Health target, in DamageInfo info)
        {
            Died?.Invoke(target, info);
        }

        // Domain reload may be disabled in the editor, so static events are cleared explicitly.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            DamageApplied = null;
            Died = null;
        }
    }
}
