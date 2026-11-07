using System;
using CoopDeck.Core;
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
    /// An elemental skill reached an enemy: raised once per affected enemy per cast, after the
    /// cast's damage was applied (also for effects without damage, e.g. a burn or a pull).
    /// The combo system applies elements and finds combos from this. Combo damage never raises it.
    /// </summary>
    public readonly struct ElementHit
    {
        public ElementHit(Health target, Element element, ICombatActor source, int sourceId, TeamId sourceTeam, Vector3 point, int power)
        {
            Target = target;
            Element = element;
            Source = source;
            SourceId = sourceId;
            SourceTeam = sourceTeam;
            Point = point;
            Power = power;
        }

        public Health Target { get; }
        public Element Element { get; }

        /// <summary>Who cast it (the combo "completer" or element applier). May be null.</summary>
        public ICombatActor Source { get; }

        /// <summary>Stable actor id (<see cref="CombatantRegistry.IdOf"/>); 0 = unknown.</summary>
        public int SourceId { get; }

        public TeamId SourceTeam { get; }
        public Vector3 Point { get; }

        /// <summary>Upgrade level of the card used (0 = not upgraded).</summary>
        public int Power { get; }
    }

    /// <summary>
    /// Global combat notifications. HUD feedback, the combo system and analytics listen here so
    /// damage code never needs to know about them.
    /// </summary>
    public static class CombatEvents
    {
        public static event Action<DamageEvent> DamageApplied;
        public static event Action<Health, DamageInfo> Died;
        public static event Action<ElementHit> ElementHitApplied;

        public static void RaiseDamageApplied(in DamageEvent damageEvent)
        {
            DamageApplied?.Invoke(damageEvent);
        }

        public static void RaiseElementHit(in ElementHit hit)
        {
            ElementHitApplied?.Invoke(hit);
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
            ElementHitApplied = null;
        }
    }
}
