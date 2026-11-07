using System.Collections;
using CoopDeck.Combat;
using CoopDeck.Core;
using CoopDeck.Effects;
using UnityEngine;

namespace CoopDeck.Enemies
{
    /// <summary>
    /// Shooting-range target: takes damage, dies (hides, disables colliders, plays a death
    /// effect) and respawns after a delay. It registers as a passive combatant
    /// (<see cref="ITrainingTarget"/>), so NPC allies join in when the player shoots it but it never
    /// starts a fight on its own.
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class TargetDummy : MonoBehaviour, ICombatActor, ITrainingTarget
    {
        [SerializeField] GameObject visualRoot;
        [SerializeField] GameObject deathFxPrefab;
        [SerializeField, Min(0f)] float respawnDelay = 3f;
        [SerializeField] Vector3 deathFxOffset = new Vector3(0f, 1f, 0f);

        Health health;
        CombatStatus status;
        Collider[] colliders;

        public bool IsDead => health != null && !health.IsAlive;

        public string DisplayName => "的";
        public TeamId Team => Health.Team;
        public Health Health => health != null ? health : health = GetComponent<Health>();
        public CombatStatus Status => status != null ? status : status = GetComponent<CombatStatus>();
        public Vector3 Position => transform.position;
        public bool IsAlive => Health.IsAlive;

        void Awake()
        {
            health = GetComponent<Health>();
            colliders = GetComponentsInChildren<Collider>(true);
        }

        public void Configure(GameObject visuals, GameObject deathFx, float delay)
        {
            visualRoot = visuals;
            deathFxPrefab = deathFx;
            respawnDelay = delay;
        }

        void OnEnable()
        {
            health.Died += OnDied;
            CombatantRegistry.Register(this);
        }

        void OnDisable()
        {
            health.Died -= OnDied;
            CombatantRegistry.Unregister(this);
        }

        void OnDied(Health dead, DamageInfo info)
        {
            SetAlive(false);
            if (deathFxPrefab != null)
            {
                FxPool.Spawn(deathFxPrefab, transform.TransformPoint(deathFxOffset), Quaternion.identity);
            }

            StartCoroutine(RespawnAfterDelay());
        }

        IEnumerator RespawnAfterDelay()
        {
            yield return new WaitForSeconds(respawnDelay);
            if (TryGetComponent(out ForceReceiver receiver))
            {
                receiver.ResetOffset(); // come back where it stood before any gravity pull
            }

            health.ResetHealth();
            SetAlive(true);
        }

        void SetAlive(bool alive)
        {
            foreach (Collider c in colliders)
            {
                c.enabled = alive;
            }

            if (visualRoot != null)
            {
                visualRoot.SetActive(alive);
            }
        }
    }
}
