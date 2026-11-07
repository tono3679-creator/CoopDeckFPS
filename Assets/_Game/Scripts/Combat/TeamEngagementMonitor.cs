using CoopDeck.Core;
using UnityEngine;

namespace CoopDeck.Combat
{
    /// <summary>
    /// "Is the team in a fight?" as plain rules: a fight is on while a hostile (not a training
    /// target) is within range of a living team member, and for a short while after the team last
    /// traded damage with one. Deck edits and other out-of-combat actions check this.
    /// </summary>
    public sealed class EngagementState
    {
        public EngagementState(float memorySeconds)
        {
            MemorySeconds = Mathf.Max(0f, memorySeconds);
        }

        public float MemorySeconds { get; }
        public float LastHostileDamageTime { get; private set; } = float.NegativeInfinity;

        public void NotifyHostileDamage(float time)
        {
            LastHostileDamageTime = Mathf.Max(LastHostileDamageTime, time);
        }

        public bool IsEngaged(float time, bool hostileNearby) => hostileNearby || time - LastHostileDamageTime < MemorySeconds;

        /// <summary>Any living non-training hostile within <paramref name="radius"/> of a living member of <paramref name="team"/>.</summary>
        public static bool HostileNear(TeamId team, float radius)
        {
            float radiusSq = radius * radius;
            var actors = CombatantRegistry.All;
            for (int i = 0; i < actors.Count; i++)
            {
                ICombatActor member = actors[i];
                if (!Valid(member) || member.Team != team || !member.IsAlive || member is ITrainingTarget)
                {
                    continue;
                }

                for (int j = 0; j < actors.Count; j++)
                {
                    ICombatActor other = actors[j];
                    if (!Valid(other) || !other.IsAlive || other is ITrainingTarget || other.Team == team || other.Team == TeamId.Neutral)
                    {
                        continue;
                    }

                    if ((other.Position - member.Position).sqrMagnitude <= radiusSq)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        static bool Valid(ICombatActor actor) => actor != null && !(actor is Object o && o == null);
    }

    /// <summary>Scene host for <see cref="EngagementState"/> (one per team; the player team by default).</summary>
    public class TeamEngagementMonitor : MonoBehaviour
    {
        [SerializeField] TeamId team = TeamId.Players;
        [Tooltip("A hostile this close to any team member means the team is in combat.")]
        [SerializeField, Min(1f)] float engageRadius = 28f;
        [Tooltip("Seconds the fight lasts after the last damage traded with a hostile.")]
        [SerializeField, Min(0f)] float damageMemory = 5f;

        EngagementState state;
        float nextScan;
        bool hostileNear;

        public TeamId Team => team;
        public EngagementState State => state ??= new EngagementState(damageMemory);
        public bool IsEngaged => State.IsEngaged(Time.time, hostileNear);

        void OnEnable()
        {
            CombatEvents.DamageApplied += OnDamage;
        }

        void OnDisable()
        {
            CombatEvents.DamageApplied -= OnDamage;
        }

        void Update()
        {
            if (Time.time >= nextScan)
            {
                nextScan = Time.time + 0.25f;
                hostileNear = EngagementState.HostileNear(team, engageRadius);
            }
        }

        void OnDamage(DamageEvent e)
        {
            if (e.Target == null || e.Info.Kind == DamageKind.Environment)
            {
                return;
            }

            bool targetIsTeam = e.Target.Team == team;
            bool sourceIsTeam = e.Info.SourceTeam == team;
            bool training = e.Target.GetComponent<ITrainingTarget>() != null || e.Info.Source is ITrainingTarget;
            if (training || targetIsTeam == sourceIsTeam || e.Info.SourceTeam == TeamId.Neutral)
            {
                return;
            }

            State.NotifyHostileDamage(Time.time);
        }

        /// <summary>Forces a scan now (tests, menus opening).</summary>
        public void Refresh()
        {
            hostileNear = EngagementState.HostileNear(team, engageRadius);
            nextScan = Time.time + 0.25f;
        }
    }
}
