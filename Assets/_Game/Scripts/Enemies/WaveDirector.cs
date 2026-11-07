using System;
using System.Collections.Generic;
using CoopDeck.Combat;
using CoopDeck.Core;
using UnityEngine;

namespace CoopDeck.Enemies
{
    /// <summary>
    /// Runs the arena: when a player enters the zone the waves start (and pause when every player
    /// has left). Spawns enemy prefabs round-robin at the spawn points and feeds deaths back into
    /// the <see cref="WaveSchedule"/>.
    /// </summary>
    public class WaveDirector : MonoBehaviour
    {
        [Serializable]
        public class WaveCounts
        {
            [Tooltip("How many of each enemy type, in the order of Enemy Prefabs.")]
            public List<int> counts = new List<int>();
        }

        [SerializeField] List<GameObject> enemyPrefabs = new List<GameObject>();
        [SerializeField] List<WaveCounts> waves = new List<WaveCounts>();
        [SerializeField] Transform[] spawnPoints = Array.Empty<Transform>();
        [Tooltip("Arena volume, centred on this transform (bottom at its position).")]
        [SerializeField] Vector3 zoneSize = new Vector3(26f, 6f, 18f);
        [SerializeField, Min(0f)] float spawnInterval = 0.6f;
        [SerializeField, Min(0f)] float intermission = 3.5f;
        [SerializeField, Min(1)] int maxAlive = 8;
        [Tooltip("Seconds without any player in the zone before spawning pauses.")]
        [SerializeField, Min(0f)] float leaveGrace = 6f;

        readonly List<EnemyActor> alive = new List<EnemyActor>();
        WaveSchedule schedule;
        int nextSpawnPoint;
        float outsideTimer;

        public WaveSchedule Schedule => schedule ??= BuildSchedule();
        public bool PlayerInside { get; private set; }
        public IReadOnlyList<EnemyActor> AliveEnemies => alive;

        public event Action<EnemyActor> EnemySpawned;

        public void Configure(List<GameObject> prefabs, List<WaveCounts> waveList, Transform[] points, Vector3 size)
        {
            enemyPrefabs = prefabs;
            waves = waveList;
            spawnPoints = points;
            zoneSize = size;
            schedule = null;
        }

        WaveSchedule BuildSchedule()
        {
            var counts = new List<int[]>();
            foreach (WaveCounts wave in waves)
            {
                counts.Add(wave.counts.ToArray());
            }

            return new WaveSchedule(counts, spawnInterval, intermission, maxAlive);
        }

        public Bounds ZoneBounds => new Bounds(transform.position + Vector3.up * (zoneSize.y * 0.5f), zoneSize);

        void Update()
        {
            PlayerInside = AnyPlayerInside();
            if (PlayerInside)
            {
                outsideTimer = 0f;
                Schedule.Start();
            }
            else if (Schedule.Phase != WavePhase.Idle)
            {
                outsideTimer += Time.deltaTime;
                if (outsideTimer >= leaveGrace)
                {
                    Schedule.Stop();
                }
            }

            if (Schedule.Tick(Time.deltaTime, out int type))
            {
                Spawn(type);
            }
        }

        bool AnyPlayerInside()
        {
            Bounds zone = ZoneBounds;
            foreach (ICombatActor actor in CombatantRegistry.All)
            {
                if (actor is UnityEngine.Object o && o == null)
                {
                    continue;
                }

                if (actor.Team == TeamId.Players && actor.IsAlive && zone.Contains(actor.Position + Vector3.up * 0.5f))
                {
                    return true;
                }
            }

            return false;
        }

        void Spawn(int type)
        {
            if (type < 0 || type >= enemyPrefabs.Count || enemyPrefabs[type] == null)
            {
                return;
            }

            Transform point = spawnPoints.Length > 0 ? spawnPoints[nextSpawnPoint++ % spawnPoints.Length] : transform;
            Vector3 toCenter = transform.position - point.position;
            toCenter.y = 0f;
            Quaternion facing = toCenter.sqrMagnitude > 0.01f ? Quaternion.LookRotation(toCenter) : point.rotation;

            GameObject instance = Instantiate(enemyPrefabs[type], point.position, facing);
            var actor = instance.GetComponent<EnemyActor>();
            if (actor == null)
            {
                return;
            }

            actor.AlertOnSpawn = true; // arena enemies know where the players are
            alive.Add(actor);
            actor.Defeated += OnDefeated;
            Schedule.NotifySpawned();
            EnemySpawned?.Invoke(actor);
        }

        void OnDefeated(EnemyActor actor)
        {
            actor.Defeated -= OnDefeated;
            alive.Remove(actor);
            Schedule.NotifyDefeated();
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.3f, 0.9f, 1f, 0.25f);
            Bounds zone = ZoneBounds;
            Gizmos.DrawWireCube(zone.center, zone.size);
        }
    }
}
