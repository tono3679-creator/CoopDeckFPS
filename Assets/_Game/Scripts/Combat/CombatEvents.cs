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

    public enum ThreatKind : byte
    {
        /// <summary>A melee strike is winding up (it lands after <see cref="ThreatWarning.TimeToImpact"/> if still in reach).</summary>
        Melee = 0,

        /// <summary>A projectile is winding up or already flying toward the target.</summary>
        Projectile = 1,
    }

    /// <summary>
    /// "An attack is coming at you": raised by enemy AI at the start of a telegraph and when a
    /// projectile launches, so allies (NPC brains) can dodge the way a human reads the telegraph.
    /// </summary>
    public readonly struct ThreatWarning
    {
        public ThreatWarning(ICombatActor attacker, ICombatActor target, ThreatKind kind, Vector3 origin, Vector3 direction,
            float timeToImpact, float damage, float reach)
        {
            Attacker = attacker;
            Target = target;
            Kind = kind;
            Origin = origin;
            Direction = direction;
            TimeToImpact = timeToImpact;
            Damage = damage;
            Reach = reach;
        }

        public ICombatActor Attacker { get; }
        public ICombatActor Target { get; }
        public ThreatKind Kind { get; }
        public Vector3 Origin { get; }

        /// <summary>Normalized direction of travel (projectile) or from attacker to target (melee).</summary>
        public Vector3 Direction { get; }

        public float TimeToImpact { get; }
        public float Damage { get; }

        /// <summary>Melee reach in metres (0 for projectiles).</summary>
        public float Reach { get; }
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

        /// <summary>An enemy telegraphed an attack at a target (see <see cref="ThreatWarning"/>).</summary>
        public static event Action<ThreatWarning> ThreatRaised;

        public static void RaiseThreat(in ThreatWarning warning)
        {
            ThreatRaised?.Invoke(warning);
        }

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
            ThreatRaised = null;
        }
    }
}
