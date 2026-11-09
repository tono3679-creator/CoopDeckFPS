using System.Collections.Generic;
using CoopDeck.Core;
using UnityEngine;

namespace CoopDeck.Effects
{
    /// <summary>
    /// The stylized bolt shape and the per-quality amounts (Docs/LIGHTNING_VFX.md), as plain functions so they are
    /// EditMode-tested. A bolt is a zig-zag between two points: offsets alternate side most of the time (a readable
    /// "lightning" silhouette instead of noise), shrink to zero at both ends, and never exceed the amplitude.
    /// </summary>
    public static class LightningPath
    {
        /// <summary>
        /// Fills <paramref name="points"/> with a zig-zag from <paramref name="start"/> to <paramref name="end"/>.
        /// <paramref name="baseOffsets"/> (one per inner point, reused between calls) keeps the big shape;
        /// <paramref name="flicker"/> (0–1) adds fresh small offsets on top so a re-roll changes the detail only.
        /// </summary>
        public static void Build(Vector3 start, Vector3 end, int segments, float amplitude, System.Random random, List<Vector2> baseOffsets,
            bool newShape, float flicker, List<Vector3> points)
        {
            segments = Mathf.Max(2, segments);
            points.Clear();
            Vector3 axis = end - start;
            float length = axis.magnitude;
            Vector3 dir = length > 1e-4f ? axis / length : Vector3.forward;
            Perpendiculars(dir, out Vector3 side, out Vector3 up);

            if (newShape || baseOffsets.Count != segments - 1)
            {
                baseOffsets.Clear();
                float sign = random.NextDouble() < 0.5 ? -1f : 1f;
                for (int i = 1; i < segments; i++)
                {
                    // Mostly alternate sides: the stylized zig-zag. Sometimes repeat a side for variety.
                    if (random.NextDouble() < 0.75)
                    {
                        sign = -sign;
                    }

                    float x = sign * Mathf.Lerp(0.45f, 1f, (float)random.NextDouble());
                    float y = ((float)random.NextDouble() * 2f - 1f) * 0.6f;
                    baseOffsets.Add(new Vector2(x, y));
                }
            }

            points.Add(start);
            for (int i = 1; i < segments; i++)
            {
                float t = i / (float)segments;
                float envelope = Mathf.Pow(Mathf.Sin(t * Mathf.PI), 0.6f);
                Vector2 o = baseOffsets[i - 1];
                if (flicker > 0f)
                {
                    o += new Vector2((float)random.NextDouble() * 2f - 1f, (float)random.NextDouble() * 2f - 1f) * flicker;
                }

                o = Vector2.ClampMagnitude(o, 1f);
                points.Add(start + axis * t + (side * o.x + up * o.y) * (amplitude * envelope));
            }

            points.Add(end);
        }

        public static void Perpendiculars(Vector3 dir, out Vector3 side, out Vector3 up)
        {
            Vector3 reference = Mathf.Abs(Vector3.Dot(dir, Vector3.up)) > 0.95f ? Vector3.right : Vector3.up;
            side = Vector3.Normalize(Vector3.Cross(reference, dir));
            up = Vector3.Cross(dir, side);
        }

        /// <summary>Segments for a bolt of a length: about one kink per <paramref name="segmentLength"/> metres, 6–24.</summary>
        public static int Segments(float length, float segmentLength) => Mathf.Clamp(Mathf.RoundToInt(length / Mathf.Max(0.05f, segmentLength)), 6, 24);

        /// <summary>Thin branches off a bolt: 2–4 on the first strike at high quality, fewer on chained strikes and lower quality.</summary>
        public static int BranchCount(VfxQuality quality, float strength)
        {
            bool primary = strength >= 0.9f;
            switch (quality)
            {
                case VfxQuality.High: return primary ? 4 : 2;
                case VfxQuality.Medium: return primary ? 2 : 1;
                default: return 0;
            }
        }

        /// <summary>Little arcs left crawling on the enemy after a hit.</summary>
        public static int ResidualArcCount(VfxQuality quality)
        {
            switch (quality)
            {
                case VfxQuality.High: return 3;
                case VfxQuality.Medium: return 2;
                default: return 1;
            }
        }
    }
}
