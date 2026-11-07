using CoopDeck.Core;
using UnityEngine;

namespace CoopDeck.Effects
{
    /// <summary>
    /// Root component of an effect prefab: plays its particle systems, scales their particle
    /// counts by <see cref="PerformanceSettings.EffectDensity"/>, and returns itself to the pool.
    /// </summary>
    [DisallowMultipleComponent]
    public class PooledFx : MonoBehaviour
    {
        [Tooltip("Seconds until the effect returns to the pool. 0 = derive from the particle systems.")]
        [SerializeField, Min(0f)] float lifetime;
        [SerializeField] bool scaleWithEffectDensity = true;

        ParticleSystem[] systems;
        float[] baseRates;
        short[][] baseBurstMin;
        short[][] baseBurstMax;
        float remaining;
        float appliedDensity = -1f;

        internal GameObject SourcePrefab { get; set; }

        public float Lifetime
        {
            get => lifetime;
            set => lifetime = Mathf.Max(0f, value);
        }

        void Awake()
        {
            CacheSystems();
        }

        void CacheSystems()
        {
            if (systems != null)
            {
                return;
            }

            systems = GetComponentsInChildren<ParticleSystem>(true);
            baseRates = new float[systems.Length];
            baseBurstMin = new short[systems.Length][];
            baseBurstMax = new short[systems.Length][];

            for (int i = 0; i < systems.Length; i++)
            {
                ParticleSystem.EmissionModule emission = systems[i].emission;
                baseRates[i] = emission.rateOverTimeMultiplier;
                int count = emission.burstCount;
                baseBurstMin[i] = new short[count];
                baseBurstMax[i] = new short[count];
                for (int b = 0; b < count; b++)
                {
                    ParticleSystem.Burst burst = emission.GetBurst(b);
                    baseBurstMin[i][b] = burst.minCount;
                    baseBurstMax[i][b] = burst.maxCount;
                }
            }

            if (lifetime <= 0f)
            {
                lifetime = DeriveLifetime();
            }
        }

        float DeriveLifetime()
        {
            float longest = 0.1f;
            foreach (ParticleSystem ps in systems)
            {
                ParticleSystem.MainModule main = ps.main;
                longest = Mathf.Max(longest, main.duration + main.startLifetime.constantMax);
            }

            return longest;
        }

        public void Play()
        {
            CacheSystems();
            ApplyDensity(scaleWithEffectDensity ? PerformanceSettings.EffectDensity : 1f);
            remaining = lifetime;

            foreach (ParticleSystem ps in systems)
            {
                ps.Clear(true);
                ps.Play(true);
            }
        }

        void ApplyDensity(float density)
        {
            if (Mathf.Approximately(density, appliedDensity))
            {
                return;
            }

            appliedDensity = density;
            for (int i = 0; i < systems.Length; i++)
            {
                ParticleSystem.EmissionModule emission = systems[i].emission;
                emission.rateOverTimeMultiplier = baseRates[i] * density;
                for (int b = 0; b < baseBurstMin[i].Length; b++)
                {
                    ParticleSystem.Burst burst = emission.GetBurst(b);
                    burst.minCount = ScaleCount(baseBurstMin[i][b], density);
                    burst.maxCount = ScaleCount(baseBurstMax[i][b], density);
                    emission.SetBurst(b, burst);
                }
            }
        }

        static short ScaleCount(short count, float density)
        {
            if (count <= 0)
            {
                return count;
            }

            return (short)Mathf.Max(1, Mathf.RoundToInt(count * density));
        }

        void Update()
        {
            remaining -= Time.deltaTime;
            if (remaining <= 0f)
            {
                FxPool.Release(this);
            }
        }
    }
}
