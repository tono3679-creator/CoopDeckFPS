using UnityEngine;

namespace CoopDeck.Effects
{
    /// <summary>
    /// The fireball's blast, authored for the base blast radius. Its up axis follows the surface it hit
    /// (flames and the shockwave spread along a wall or the floor); on a character it faces back toward the shot
    /// (a vertical impact ring) and skips the ground-only layers (the scorch mark). A larger radius (a passive)
    /// grows the billows, shockwave and the spread of flames and sparks; particle sprites keep their size.
    /// </summary>
    public class FireballExplosionFx : MonoBehaviour, IRadiusFx, ISurfaceFx
    {
        [SerializeField, Min(0.1f)] float authoredRadius = 2.5f;
        [Tooltip("Layers that grow with the blast radius.")]
        [SerializeField] Transform[] radiusLayers = new Transform[0];
        [Tooltip("Only on the ground and walls: skipped when the blast is on a character.")]
        [SerializeField] ParticleSystem[] surfaceOnly = new ParticleSystem[0];
        [Tooltip("Shockwave rings: smaller on a character, whose ring stands upright facing the shot (a full-size one would cut into the floor).")]
        [SerializeField] Transform[] rings = new Transform[0];
        [SerializeField, Range(0.1f, 1f)] float characterRingScale = 0.45f;

        public float Radius { get; private set; } = 1f;
        public bool OnCharacter { get; private set; }
        public float AuthoredRadius => authoredRadius;

        public void SetRadius(float radius)
        {
            Radius = Mathf.Max(0.1f, radius);
            float k = Radius / authoredRadius;
            foreach (Transform layer in radiusLayers)
            {
                if (layer != null)
                {
                    layer.localScale = Vector3.one * k;
                }
            }
        }

        public void SetSurface(Vector3 normal, bool onCharacter)
        {
            OnCharacter = onCharacter;
            Vector3 up = normal.sqrMagnitude < 1e-6f ? Vector3.up : normal.normalized;
            transform.rotation = Quaternion.FromToRotation(Vector3.up, up);
            foreach (Transform ring in rings)
            {
                if (ring != null)
                {
                    ring.localScale = Vector3.one * (onCharacter ? characterRingScale : 1f);
                }
            }

            if (!onCharacter)
            {
                return;
            }

            foreach (ParticleSystem ps in surfaceOnly)
            {
                if (ps != null)
                {
                    ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                }
            }
        }
    }
}
