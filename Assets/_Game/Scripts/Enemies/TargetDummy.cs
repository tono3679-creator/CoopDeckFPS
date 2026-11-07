using System.Collections;
using CoopDeck.Combat;
using CoopDeck.Effects;
using UnityEngine;

namespace CoopDeck.Enemies
{
    /// <summary>
    /// Shooting-range target: takes damage, dies (hides, disables colliders, plays a death
    /// effect) and respawns after a delay. Real enemy AI arrives in Phase 4.
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class TargetDummy : MonoBehaviour
    {
        [SerializeField] GameObject visualRoot;
        [SerializeField] GameObject deathFxPrefab;
        [SerializeField, Min(0f)] float respawnDelay = 3f;
        [SerializeField] Vector3 deathFxOffset = new Vector3(0f, 1f, 0f);

        Health health;
        Collider[] colliders;

        public bool IsDead => health != null && !health.IsAlive;

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
        }

        void OnDisable()
        {
            health.Died -= OnDied;
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
