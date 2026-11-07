using System;
using CoopDeck.Combat;
using CoopDeck.Core;
using CoopDeck.Effects;
using UnityEngine;

namespace CoopDeck.Enemies
{
    /// <summary>
    /// The combat identity of an enemy: health, status, team, registry membership and death.
    /// Behaviour lives in the AI brain; this class is what damage sources and combos see.
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class EnemyActor : MonoBehaviour, ICombatActor
    {
        [SerializeField] EnemyDefinition definition;
        [Tooltip("Seconds the body stays after death (death animation) before it is removed.")]
        [SerializeField, Min(0f)] float despawnDelay = 2.2f;

        Health health;
        CombatStatus status;
        bool defeated;

        /// <summary>Raised once, the moment this enemy dies.</summary>
        public event Action<EnemyActor> Defeated;

        public EnemyDefinition Definition => definition;
        public string DisplayName => definition != null ? definition.displayName : name;
        public TeamId Team => Health.Team;
        public Health Health => health != null ? health : health = GetComponent<Health>();
        public CombatStatus Status => status != null ? status : status = GetComponent<CombatStatus>();
        public Vector3 Position => transform.position;
        public bool IsAlive => Health.IsAlive;

        /// <summary>Set by spawners: the enemy starts hunting immediately instead of waiting to see a target.</summary>
        public bool AlertOnSpawn { get; set; }

        void Awake()
        {
            health = GetComponent<Health>();
            status = GetComponent<CombatStatus>();
            ApplyDefinition();
        }

        public void Initialize(EnemyDefinition enemyDefinition)
        {
            definition = enemyDefinition;
            ApplyDefinition();
        }

        void ApplyDefinition()
        {
            if (definition != null && Health != null)
            {
                Health.Team = TeamId.Enemies;
                Health.SetMaxHealth(definition.maxHealth, true);
            }
        }

        void OnEnable()
        {
            CombatantRegistry.Register(this);
            Health.Died += OnDied;
        }

        void OnDisable()
        {
            CombatantRegistry.Unregister(this);
            Health.Died -= OnDied;
        }

        void OnDied(Health dead, DamageInfo info)
        {
            if (defeated)
            {
                return;
            }

            defeated = true;
            CombatantRegistry.Unregister(this);
            foreach (Collider c in GetComponentsInChildren<Collider>())
            {
                c.enabled = false; // the corpse no longer blocks shots or movement
            }

            if (definition != null && definition.deathFx != null)
            {
                FxPool.Spawn(definition.deathFx, transform.position + Vector3.up, Quaternion.identity);
            }

            Defeated?.Invoke(this);
            Destroy(gameObject, despawnDelay);
        }
    }
}
