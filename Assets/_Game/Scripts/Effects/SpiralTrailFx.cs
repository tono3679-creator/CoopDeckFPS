using UnityEngine;

namespace CoopDeck.Effects
{
    /// <summary>
    /// Moves its child trail emitters (objects with a <see cref="TrailRenderer"/>) along an
    /// inward spiral around the effect centre: embers being sucked into a singularity. The radius
    /// follows the root scale, so one prefab works for every combo size.
    /// </summary>
    public class SpiralTrailFx : MonoBehaviour
    {
        [SerializeField, Min(0.01f)] float duration = 0.9f;
        [SerializeField, Min(0f)] float startRadius = 1f;
        [SerializeField, Min(0f)] float endRadius = 0.08f;
        [SerializeField] float turnsPerSecond = 1.4f;
        [SerializeField] float height = 0.6f;

        Transform[] emitters = new Transform[0];
        TrailRenderer[] trails = new TrailRenderer[0];
        float age;

        /// <summary>Where the spiral starts (metres); <see cref="RadiusScaledFx"/> sets it to the pull radius.</summary>
        public float StartRadius
        {
            get => startRadius;
            set => startRadius = Mathf.Max(0f, value);
        }

        void Awake()
        {
            trails = GetComponentsInChildren<TrailRenderer>(true);
            emitters = new Transform[trails.Length];
            for (int i = 0; i < trails.Length; i++)
            {
                emitters[i] = trails[i].transform;
            }
        }

        void OnEnable()
        {
            Restart();
        }

        /// <summary>Back to the start of the spiral with clean trails (after the radius changed).</summary>
        public void Restart()
        {
            age = 0f;
            Place(0f);
            foreach (TrailRenderer trail in trails)
            {
                if (trail != null)
                {
                    trail.Clear();
                    trail.emitting = true;
                }
            }
        }

        void Update()
        {
            age += Time.deltaTime;
            float t = Mathf.Clamp01(age / duration);
            Place(t);
            if (t >= 1f)
            {
                foreach (TrailRenderer trail in trails)
                {
                    if (trail != null)
                    {
                        trail.emitting = false;
                    }
                }
            }
        }

        void Place(float t)
        {
            int count = emitters.Length;
            float eased = t * t;
            float radius = Mathf.Lerp(startRadius, endRadius, eased);
            for (int i = 0; i < count; i++)
            {
                if (emitters[i] == null)
                {
                    continue;
                }

                float phase = (float)i / Mathf.Max(1, count);
                float angle = (phase + age * turnsPerSecond) * Mathf.PI * 2f;
                float y = height * (1f - t) * (0.5f + 0.5f * Mathf.Sin(phase * 6.28f + age * 3f));
                emitters[i].localPosition = new Vector3(Mathf.Cos(angle) * radius, y, Mathf.Sin(angle) * radius);
            }
        }
    }
}
