using System.Collections.Generic;
using CoopDeck.Core;
using UnityEngine;

namespace CoopDeck.Effects
{
    /// <summary>
    /// Current left crawling on the enemy's surface after a lightning hit (Docs/LIGHTNING_VFX.md): a few short arcs
    /// between random points around the hit, re-rolled every few hundredths of a second, gone after 0.1–0.2 s.
    /// Starts on its own when the pooled hit effect is spawned (OnEnable); <see cref="Restart"/> starts it again (the hit
    /// was resized). The radius follows the hit's scale. 1–3 arcs by effect quality.
    /// </summary>
    public class SurfaceArcFx : MonoBehaviour
    {
        [SerializeField] LineRenderer[] arcs = new LineRenderer[0];
        [SerializeField, Min(0.05f)] float radius = 0.45f;
        [SerializeField, Min(0.02f)] float duration = 0.16f;
        [SerializeField, Min(0.01f)] float rerollInterval = 0.03f;
        [SerializeField, Min(0.005f)] float width = 0.07f;

        readonly List<Vector3> points = new List<Vector3>(8);
        readonly List<Vector2> offsets = new List<Vector2>(8);
        float age;
        float rerollTimer;
        int active;
        bool playing;
        System.Random random = new System.Random(3);

        public bool Playing => playing;
        public int ActiveArcs => active;

        public void Configure(LineRenderer[] lines, float arcRadius, float lifetime)
        {
            arcs = lines;
            radius = arcRadius;
            duration = lifetime;
        }

        void OnEnable() => Restart();

        public void Restart()
        {
            age = 0f;
            rerollTimer = 0f;
            playing = true;
            random = new System.Random(Random.Range(1, int.MaxValue));
            active = Mathf.Min(arcs.Length, LightningPath.ResidualArcCount(PerformanceSettings.Vfx));
            Reroll();
            Apply();
        }

        void OnDisable()
        {
            playing = false;
        }

        void Update()
        {
            if (!playing)
            {
                return;
            }

            age += Time.deltaTime;
            if (age >= duration)
            {
                playing = false;
                foreach (LineRenderer arc in arcs)
                {
                    if (arc != null)
                    {
                        arc.enabled = false;
                    }
                }

                return;
            }

            rerollTimer += Time.deltaTime;
            if (rerollTimer >= rerollInterval)
            {
                rerollTimer = 0f;
                Reroll();
            }

            Apply();
        }

        void Reroll()
        {
            Vector3 centre = transform.position;
            float r = radius * transform.lossyScale.x;
            for (int i = 0; i < arcs.Length; i++)
            {
                LineRenderer arc = arcs[i];
                if (arc == null)
                {
                    continue;
                }

                arc.enabled = i < active;
                if (i >= active)
                {
                    continue;
                }

                Vector3 a = centre + RandomOnSphere() * r;
                Vector3 b = centre + RandomOnSphere() * r;
                LightningPath.Build(a, b, 4, Vector3.Distance(a, b) * 0.3f, random, offsets, true, 0f, points);
                arc.positionCount = points.Count;
                for (int p = 0; p < points.Count; p++)
                {
                    arc.SetPosition(p, points[p]);
                }
            }
        }

        void Apply()
        {
            float t = age / duration;
            float alpha = 1f - t * t;
            for (int i = 0; i < active && i < arcs.Length; i++)
            {
                LineRenderer arc = arcs[i];
                if (arc == null)
                {
                    continue;
                }

                arc.widthMultiplier = width * (1f - 0.5f * t);
                arc.startColor = arc.endColor = new Color(1f, 1f, 1f, alpha);
            }
        }

        Vector3 RandomOnSphere()
        {
            float z = (float)random.NextDouble() * 2f - 1f;
            float a = (float)random.NextDouble() * Mathf.PI * 2f;
            float r = Mathf.Sqrt(1f - z * z);
            return new Vector3(r * Mathf.Cos(a), z, r * Mathf.Sin(a));
        }
    }
}
