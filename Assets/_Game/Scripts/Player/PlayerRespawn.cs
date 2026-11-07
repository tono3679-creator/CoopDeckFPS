using System.Collections;
using CoopDeck.Combat;
using UnityEngine;

namespace CoopDeck.Player
{
    /// <summary>
    /// What happens when the player goes down. With a living ally nearby in the run (an NPC or
    /// another player) and a <see cref="Revivable"/>, the player stays downed and waits to be
    /// revived; if nobody can help, or the bleed-out timer runs out, the player respawns at the
    /// start position with full health after <see cref="respawnDelay"/> (the Phase 1 behaviour).
    /// </summary>
    public class PlayerRespawn : MonoBehaviour
    {
        [SerializeField, Min(0f)] float respawnDelay = 2.5f;

        Health health;
        FpsMotor motor;
        FpsLook look;
        Revivable revivable;
        Vector3 spawnPosition;
        float spawnYaw;

        public bool IsWaitingToRespawn { get; private set; }

        /// <summary>Downed and waiting for an ally instead of respawning right away.</summary>
        public bool IsWaitingForRevive { get; private set; }

        void Awake()
        {
            health = GetComponent<Health>();
            motor = GetComponent<FpsMotor>();
            look = GetComponent<FpsLook>();
            revivable = GetComponent<Revivable>();
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
            float waited = 0f;
            while (true)
            {
                yield return null;
                waited += Time.deltaTime;
                if (health.IsAlive)
                {
                    // Revived by an ally: carry on where we fell.
                    IsWaitingToRespawn = false;
                    IsWaitingForRevive = false;
                    yield break;
                }

                bool canBeRevived = revivable != null && !revivable.HasBledOut && revivable.HasLivingAlly();
                IsWaitingForRevive = canBeRevived;
                if (!canBeRevived && waited >= respawnDelay)
                {
                    break;
                }
            }

            IsWaitingForRevive = false;
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
