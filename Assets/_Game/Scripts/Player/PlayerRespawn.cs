using System.Collections;
using CoopDeck.Combat;
using UnityEngine;

namespace CoopDeck.Player
{
    /// <summary>
    /// Placeholder down/revive: after death the player respawns at the start position with
    /// full health. Replaced by the down → revive flow later (ANIMATION_GUIDELINES / Phase 4).
    /// </summary>
    public class PlayerRespawn : MonoBehaviour
    {
        [SerializeField, Min(0f)] float respawnDelay = 2.5f;

        Health health;
        FpsMotor motor;
        FpsLook look;
        Vector3 spawnPosition;
        float spawnYaw;

        public bool IsWaitingToRespawn { get; private set; }

        void Awake()
        {
            health = GetComponent<Health>();
            motor = GetComponent<FpsMotor>();
            look = GetComponent<FpsLook>();
        }

        void Start()
        {
            spawnPosition = transform.position;
            spawnYaw = transform.eulerAngles.y;
        }

        void OnEnable()
        {
            if (health != null)
            {
                health.Died += OnDied;
            }
        }

        void OnDisable()
        {
            if (health != null)
            {
                health.Died -= OnDied;
            }
        }

        void OnDied(Health dead, DamageInfo info)
        {
            if (!IsWaitingToRespawn)
            {
                StartCoroutine(RespawnAfterDelay());
            }
        }

        IEnumerator RespawnAfterDelay()
        {
            IsWaitingToRespawn = true;
            yield return new WaitForSeconds(respawnDelay);
            if (motor != null)
            {
                motor.Teleport(spawnPosition);
            }

            if (look != null)
            {
                look.SetRotation(spawnYaw, 0f);
            }

            health.ResetHealth();
            IsWaitingToRespawn = false;
        }
    }
}
