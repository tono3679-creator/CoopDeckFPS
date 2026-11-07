using System;
using CoopDeck.Core;
using UnityEngine;

namespace CoopDeck.Combat
{
    public enum DamageKind : byte
    {
        Weapon = 0,
        Skill = 1,
        Combo = 2,
        Status = 3,
        Environment = 4,
    }

    public enum HitZoneType : byte
    {
        Body = 0,
        Head = 1,
        Weakpoint = 2,
        Armor = 3,
    }

    [Flags]
    public enum DamageFlags : byte
    {
        None = 0,
        Headshot = 1 << 0,
        Critical = 1 << 1,

        /// <summary>Damage produced by an element combo. It must never trigger another combo.</summary>
        ComboDamage = 1 << 2,

        /// <summary>The combo was completed by two different actors.</summary>
        Coop = 1 << 3,
    }

    /// <summary>
    /// Everything a target needs to resolve one hit. Plain data so it can later be sent over the
    /// network or recorded, and so the combo system can inspect it.
    /// </summary>
    public struct DamageInfo
    {
        /// <summary>Damage before hit-zone multipliers.</summary>
        public float Amount;

        public Element Element;
        public DamageKind Kind;
        public DamageFlags Flags;

        /// <summary>Who caused it. May be null (environment, destroyed source).</summary>
        public ICombatActor Source;

        public TeamId SourceTeam;
        public Vector3 Point;
        public Vector3 Normal;
        public Vector3 Direction;

        /// <summary>Extra multiplier when the hit lands on a Head zone. 0 means "use 1".</summary>
        public float HeadshotMultiplier;
    }

    public struct DamageResult
    {
        public bool Applied;

        /// <summary>HP actually removed.</summary>
        public float Damage;

        /// <summary>Damage soaked up by a shield before reaching HP.</summary>
        public float Absorbed;

        public bool Killed;
        public HitZoneType Zone;
        public DamageFlags Flags;
        public float RemainingHealth;

        public bool IsHeadshot => (Flags & DamageFlags.Headshot) != 0;
    }

    public interface IDamageable
    {
        TeamId Team { get; }
        bool IsAlive { get; }
        DamageResult ApplyDamage(in DamageInfo info);
    }

    /// <summary>
    /// The combat-facing view of any fighter: local human, NPC ally, remote player or enemy.
    /// Systems that need "who did this" (combos, assists, AI targeting) only depend on this.
    /// </summary>
    public interface ICombatActor
    {
        string DisplayName { get; }
        TeamId Team { get; }
        Health Health { get; }

        /// <summary>Buffs, shields and debuffs on this actor. May be null.</summary>
        CombatStatus Status { get; }

        Vector3 Position { get; }
        bool IsAlive { get; }
    }
}
