using System;
using System.Collections.Generic;
using UnityEngine;

namespace CoopDeck.Enemies
{
    public enum WavePhase
    {
        Idle = 0,
        Spawning = 1,
        Fighting = 2,
        Intermission = 3,
    }

    /// <summary>
    /// Wave progression as plain data: which enemy type to spawn when, how many may be alive at
    /// once, and when the next wave starts. After the last authored wave the list loops with one
    /// extra enemy of each type per loop, so the arena keeps escalating.
    /// </summary>
    public sealed class WaveSchedule
    {
        readonly List<int[]> waves;
        readonly float spawnInterval;
        readonly float intermission;
        readonly int maxAlive;
        readonly Queue<int> pending = new Queue<int>();
        float timer;

        /// <param name="waveCounts">Per wave: how many of each enemy type (index = type).</param>
        public WaveSchedule(IEnumerable<int[]> waveCounts, float spawnInterval, float intermission, int maxAlive)
        {
            waves = new List<int[]>(waveCounts ?? Array.Empty<int[]>());
            this.spawnInterval = Mathf.Max(0f, spawnInterval);
            this.intermission = Mathf.Max(0f, intermission);
            this.maxAlive = Mathf.Max(1, maxAlive);
        }

        public WavePhase Phase { get; private set; } = WavePhase.Idle;

        /// <summary>1-based wave number, 0 before the first wave.</summary>
        public int WaveNumber { get; private set; }

        public int Alive { get; private set; }
        public int PendingSpawns => pending.Count;
        public int RemainingInWave => pending.Count + Alive;
        public float IntermissionRemaining => Phase == WavePhase.Intermission ? Mathf.Max(0f, timer) : 0f;
        public bool HasWaves => waves.Count > 0;

        public event Action<int> WaveStarted;
        public event Action<int> WaveCleared;

        /// <summary>Starts (or resumes) the waves. Resuming continues the current wave.</summary>
        public void Start()
        {
            if (!HasWaves || Phase != WavePhase.Idle)
            {
                return;
            }

            if (WaveNumber == 0 || RemainingInWave == 0)
            {
                BeginWave(WaveNumber + 1);
            }
            else
            {
                Phase = pending.Count > 0 ? WavePhase.Spawning : WavePhase.Fighting;
            }
        }

        /// <summary>Stops spawning. Enemies already alive keep fighting.</summary>
        public void Stop()
        {
            Phase = WavePhase.Idle;
        }

        /// <summary>Advances time. Returns true with the enemy type when one should spawn now.</summary>
        public bool Tick(float deltaTime, out int spawnType)
        {
            spawnType = -1;
            switch (Phase)
            {
                case WavePhase.Spawning:
                    timer -= deltaTime;
                    if (pending.Count == 0)
                    {
                        Phase = WavePhase.Fighting;
                        break;
                    }

                    if (timer <= 0f && Alive < maxAlive)
                    {
                        spawnType = pending.Dequeue();
                        timer = spawnInterval;
                        if (pending.Count == 0)
                        {
                            Phase = WavePhase.Fighting;
                        }

                        return true;
                    }

                    break;

                case WavePhase.Fighting:
                    if (Alive <= 0)
                    {
                        Phase = WavePhase.Intermission;
                        timer = intermission;
                        WaveCleared?.Invoke(WaveNumber);
                    }

                    break;

                case WavePhase.Intermission:
                    timer -= deltaTime;
                    if (timer <= 0f)
                    {
                        BeginWave(WaveNumber + 1);
                    }

                    break;
            }

            return false;
        }

        public void NotifySpawned()
        {
            Alive++;
        }

        public void NotifyDefeated()
        {
            Alive = Mathf.Max(0, Alive - 1);
        }

        /// <summary>Enemy counts for a wave number, including the loop bonus.</summary>
        public int[] CountsFor(int waveNumber)
        {
            if (!HasWaves || waveNumber < 1)
            {
                return Array.Empty<int>();
            }

            int index = (waveNumber - 1) % waves.Count;
            int loop = (waveNumber - 1) / waves.Count;
            int[] counts = (int[])waves[index].Clone();
            for (int i = 0; i < counts.Length; i++)
            {
                if (counts[i] > 0 || loop > 0)
                {
                    counts[i] += loop;
                }
            }

            return counts;
        }

        void BeginWave(int number)
        {
            WaveNumber = number;
            pending.Clear();
            int[] counts = CountsFor(number);

            // Interleave types (A, B, A, B, A …) so a wave never arrives as one block of the same enemy.
            int[] left = (int[])counts.Clone();
            bool any = true;
            while (any)
            {
                any = false;
                for (int type = 0; type < left.Length; type++)
                {
                    if (left[type] > 0)
                    {
                        pending.Enqueue(type);
                        left[type]--;
                        any = true;
                    }
                }
            }

            timer = 0f;
            Phase = pending.Count > 0 ? WavePhase.Spawning : WavePhase.Fighting;
            WaveStarted?.Invoke(number);
        }
    }
}
