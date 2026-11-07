using System.Collections.Generic;
using CoopDeck.Core;
using UnityEngine;

namespace CoopDeck.Combat
{
    /// <summary>
    /// Every live combatant (players, NPCs, enemies) registers here while enabled, so AI can find
    /// targets without scene searches. Order is registration order; queries skip destroyed entries.
    /// </summary>
    public static class CombatantRegistry
    {
        static readonly List<ICombatActor> Actors = new List<ICombatActor>();
        static readonly Dictionary<ICombatActor, int> Ids = new Dictionary<ICombatActor, int>();
        static int nextId = 1;

        public static IReadOnlyList<ICombatActor> All => Actors;

        /// <summary>
        /// Stable id for an actor within the session (attacker / applier records, co-op credit).
        /// Assigned on first use and kept after the actor unregisters, so a dead applier still
        /// matches. 0 for null. Networking will map its object ids onto these.
        /// </summary>
        public static int IdOf(ICombatActor actor)
        {
            if (actor == null || actor is Object o && o == null)
            {
                return 0;
            }

            if (!Ids.TryGetValue(actor, out int id))
            {
                id = nextId++;
                Ids[actor] = id;
            }

            return id;
        }

        public static void Register(ICombatActor actor)
        {
            if (actor != null && !Actors.Contains(actor))
            {
                Actors.Add(actor);
            }
        }

        public static void Unregister(ICombatActor actor)
        {
            Actors.Remove(actor);
        }

        /// <summary>Nearest living actor hostile to <paramref name="team"/> within range, or null.</summary>
        public static ICombatActor FindNearestHostile(TeamId team, Vector3 from, float maxDistance)
        {
            ICombatActor best = null;
            float bestDistance = maxDistance * maxDistance;
            for (int i = Actors.Count - 1; i >= 0; i--)
            {
                ICombatActor actor = Actors[i];
                if (actor is Object unityObject && unityObject == null)
                {
                    Actors.RemoveAt(i); // destroyed without unregistering
                    continue;
                }

                if (!actor.IsAlive || actor.Team == team || actor.Team == TeamId.Neutral)
                {
                    continue;
                }

                float distance = (actor.Position - from).sqrMagnitude;
                if (distance <= bestDistance)
                {
                    bestDistance = distance;
                    best = actor;
                }
            }

            return best;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Actors.Clear();
            Ids.Clear();
            nextId = 1;
        }
    }
}
