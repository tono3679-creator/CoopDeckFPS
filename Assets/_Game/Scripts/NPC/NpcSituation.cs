using System.Collections.Generic;
using CoopDeck.Combat;
using CoopDeck.Combos;
using CoopDeck.Core;
using UnityEngine;

namespace CoopDeck.NPC
{
    /// <summary>One enemy as the NPC perceives it this think.</summary>
    public struct NpcEnemyInfo
    {
        public int Id;
        public Vector3 Position;
        public float Distance;
        public bool LineOfSight;
        public float Health01;

        /// <summary>A shooting-range dummy: only worth attacking when the team is already on it.</summary>
        public bool IsTraining;

        /// <summary>The team is fighting it (seen and hostile, or a dummy the team just hit).</summary>
        public bool Engaged;

        /// <summary>Melee attacker (the NPC keeps its distance).</summary>
        public bool IsMelee;

        /// <summary>Elements on it (combo partners). May be null.</summary>
        public ElementAuraModel Auras;
    }

    /// <summary>One hand slot as the NPC sees it.</summary>
    public struct NpcHandCard
    {
        public bool Present;
        public int Slot;
        public int InstanceId;
        public string Name;
        public NpcCardTraits Traits;
        public float Gauge;
        public float GaugeCost;
        public float Cooldown;

        /// <summary>Seconds this card has been saved for a combo so far.</summary>
        public float HeldFor;

        /// <summary>A recent hold ran out: this card may not be saved again yet.</summary>
        public bool HoldBlocked;

        public bool Ready => Present && Cooldown <= 0f && Gauge > 0f;
    }

    /// <summary>A telegraphed attack aimed at the NPC (after its reaction time).</summary>
    public struct NpcThreat
    {
        public ThreatKind Kind;
        public Vector3 Origin;
        public Vector3 Direction;
        public float TimeLeft;
        public float Damage;
        public float Reach;

        /// <summary>Random roll 0..1 fixed when the threat arrived (dodge chance), so re-thinks agree.</summary>
        public float Roll;
    }

    /// <summary>
    /// Everything one NPC decision reads, as plain data. The controller fills it from the scene
    /// a few times per second (reused buffers, no allocation); tests fill it by hand.
    /// </summary>
    public sealed class NpcSituation
    {
        public const int MaxEnemies = 16;
        public const int MaxThreats = 4;
        public const int MaxHand = CharacterCommands.SkillSlotCount;

        public readonly NpcEnemyInfo[] Enemies = new NpcEnemyInfo[MaxEnemies];
        public readonly NpcHandCard[] Hand = new NpcHandCard[MaxHand];
        public readonly NpcThreat[] Threats = new NpcThreat[MaxThreats];

        public int EnemyCount;
        public int ThreatCount;

        public float Time;
        public NpcTactic Tactic;

        // ---------------------------------------------------------------- self
        public int SelfId;
        public TeamId Team = TeamId.Players;
        public Vector3 Position;
        public Vector3 Forward = Vector3.forward;
        public bool Alive = true;
        public float Health = 100f;
        public float Health01 = 1f;
        public float DashCharges = 2f;
        public float ShieldAmount;
        public float DamageBuff;

        // ---------------------------------------------------------------- leader (the player it follows)
        public bool HasLeader;
        public int LeaderId;
        public Vector3 LeaderPosition;
        public Vector3 LeaderAimOrigin;
        public Vector3 LeaderAimDirection = Vector3.forward;
        public bool LeaderAlive = true;
        public bool LeaderDowned;
        public float LeaderHealth01 = 1f;
        public float LeaderDistance;

        /// <summary>Bit (1 &lt;&lt; (int)Element) per element the leader can apply with a ready card in hand.</summary>
        public int LeaderElementMask;

        /// <summary>Enemy the leader is aiming at (index into <see cref="Enemies"/>, -1 none).</summary>
        public int LeaderTarget = -1;

        /// <summary>Enemy the leader marked for focus fire (-1 none).</summary>
        public int FocusTarget = -1;

        /// <summary>Enemy the NPC shot at last (keeps it from flicking between targets), -1 none.</summary>
        public int CurrentTarget = -1;

        public IReadOnlyList<ComboRule> Rules;

        /// <summary>
        /// The NPC's specialty (Volt: lightning). Cards of this element are its finishers and may be saved
        /// for an ally's element; other elements are used to start combos for allies. None = any card may be saved.
        /// </summary>
        public Element FavoredElement = Element.None;

        /// <summary>False for a few seconds after the NPC started a combo, so it does not keep re-applying elements.</summary>
        public bool SetupAllowed = true;

        /// <summary>Seconds since the NPC last used a card (ordinary skills wait for <c>castInterval</c>).</summary>
        public float SinceLastCast = float.PositiveInfinity;

        public void Clear()
        {
            EnemyCount = 0;
            ThreatCount = 0;
            LeaderTarget = -1;
            FocusTarget = -1;
            CurrentTarget = -1;
            LeaderElementMask = 0;
            for (int i = 0; i < Hand.Length; i++)
            {
                Hand[i] = default;
            }
        }

        public int AddEnemy(in NpcEnemyInfo info)
        {
            if (EnemyCount >= MaxEnemies)
            {
                return -1;
            }

            Enemies[EnemyCount] = info;
            return EnemyCount++;
        }

        public void AddThreat(in NpcThreat threat)
        {
            if (ThreatCount < MaxThreats)
            {
                Threats[ThreatCount++] = threat;
            }
        }

        public bool HasEngagedEnemy
        {
            get
            {
                for (int i = 0; i < EnemyCount; i++)
                {
                    if (Enemies[i].Engaged)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>Engaged enemies within <paramref name="radius"/> of <paramref name="center"/>.</summary>
        public int EngagedWithin(Vector3 center, float radius)
        {
            float r2 = radius * radius;
            int count = 0;
            for (int i = 0; i < EnemyCount; i++)
            {
                if (Enemies[i].Engaged && (Enemies[i].Position - center).sqrMagnitude <= r2)
                {
                    count++;
                }
            }

            return count;
        }
    }
}
