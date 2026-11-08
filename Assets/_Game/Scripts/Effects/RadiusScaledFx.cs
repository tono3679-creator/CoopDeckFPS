using UnityEngine;

namespace CoopDeck.Effects
{
    /// <summary>
    /// Fits one effect prefab to any area size without scaling the whole object (which would
    /// also move chest-height layers up and blow sprites up to screen size). Rings and emission
    /// areas listed in <see cref="full"/> scale with the radius, <see cref="partial"/> layers grow
    /// half as fast, and an inward spiral starts at the radius.
    /// </summary>
    public class RadiusScaledFx : MonoBehaviour, IRadiusFx
    {
        [Tooltip("Scaled to the radius (authored for 1 m): ground rings, pull areas.")]
        [SerializeField] Transform[] full = new Transform[0];

        [Tooltip("Scaled to 1 + (radius - 1) / 2: fireballs, debris areas.")]
        [SerializeField] Transform[] partial = new Transform[0];

        [SerializeField] SpiralTrailFx spiral;

        public float Radius { get; private set; } = 1f;

        public void Configure(Transform[] fullLayers, Transform[] partialLayers, SpiralTrailFx spiralTrails)
        {
            full = fullLayers ?? new Transform[0];
            partial = partialLayers ?? new Transform[0];
            spiral = spiralTrails;
        }

        public void SetRadius(float radius)
        {
            Radius = Mathf.Max(0.1f, radius);
            transform.localScale = Vector3.one;
            foreach (Transform t in full)
            {
                if (t != null)
                {
                    t.localScale = Vector3.one * Radius;
                }
            }

            float half = 1f + (Radius - 1f) * 0.5f;
            foreach (Transform t in partial)
            {
                if (t != null)
                {
                    t.localScale = Vector3.one * Mathf.Max(0.1f, half);
                }
            }

            if (spiral != null)
            {
                spiral.StartRadius = Radius;
                spiral.Restart();
            }
        }
    }
}
