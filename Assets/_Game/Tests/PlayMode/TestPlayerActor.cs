using CoopDeck.Combat;
using CoopDeck.Core;
using UnityEngine;

namespace CoopDeck.Tests
{
    /// <summary>
    /// Stand-in for a player in AI tests: a Players-team <see cref="Health"/> that registers in the
    /// <see cref="CombatantRegistry"/> like FpsCharacter does, without the FPS controller.
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class TestPlayerActor : MonoBehaviour, ICombatActor
    {
        Health health;

        public string DisplayName => name;
        public TeamId Team => Health.Team;
        public Health Health => health != null ? health : health = GetComponent<Health>();
        public CombatStatus Status => null;
        public Vector3 Position => transform.position;
        public bool IsAlive => Health.IsAlive;

        void Awake()
        {
            Health.Team = TeamId.Players;
        }

        void OnEnable()
        {
            CombatantRegistry.Register(this);
        }

        void OnDisable()
        {
            CombatantRegistry.Unregister(this);
        }
    }
}
