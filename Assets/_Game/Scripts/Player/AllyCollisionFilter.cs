using System.Collections.Generic;
using CoopDeck.Combat;
using UnityEngine;

namespace CoopDeck.Player
{
    /// <summary>
    /// Allies (players and NPCs of the same team) walk through each other, so an NPC can never
    /// body-block the player in a doorway or push them off a ledge. Enemies still collide.
    /// </summary>
    [DisallowMultipleComponent]
    public class AllyCollisionFilter : MonoBehaviour
    {
        static readonly List<AllyCollisionFilter> Active = new List<AllyCollisionFilter>();

        Collider[] colliders;
        Health health;

        void Awake()
        {
            colliders = GetComponentsInChildren<Collider>(true);
            health = GetComponent<Health>();
        }

        void OnEnable()
        {
            foreach (AllyCollisionFilter other in Active)
            {
                if (other != null && other != this && SameTeam(other))
                {
                    SetIgnored(other, true);
                }
            }

            Active.Add(this);
        }

        void OnDisable()
        {
            Active.Remove(this);
        }

        bool SameTeam(AllyCollisionFilter other) =>
            health == null || other.health == null || health.Team == other.health.Team;

        void SetIgnored(AllyCollisionFilter other, bool ignore)
        {
            foreach (Collider a in colliders)
            {
                foreach (Collider b in other.colliders)
                {
                    if (a != null && b != null)
                    {
                        Physics.IgnoreCollision(a, b, ignore);
                    }
                }
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Active.Clear();
        }
    }
}
