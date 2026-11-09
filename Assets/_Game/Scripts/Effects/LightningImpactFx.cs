using UnityEngine;

namespace CoopDeck.Effects
{
    /// <summary>
    /// Root of a lightning hit prefab (FX_Lightning_Hit / _Chain, Docs/LIGHTNING_VFX.md): the star flash, glow, shards
    /// and sparks burst at the contact on the chest, and a cyan shock ring spreads on the floor under the enemy, so the
    /// hit reads as an upright burst from afar. <see cref="Play"/> sizes the burst to the enemy's body and puts the ring
    /// on the ground (none for an enemy in the air). The impact sound plays as the pooled hit is spawned, i.e. on the
    /// frame the hit shows.
    /// </summary>
    [DisallowMultipleComponent]
    public class LightningImpactFx : MonoBehaviour
    {
        [SerializeField] Transform groundRing;
        [SerializeField] AudioSource sound;
        [Tooltip("Body width the prefab is built for (metres); a wider body scales the burst up.")]
        [SerializeField, Min(0.1f)] float referenceWidth = 0.8f;
        [SerializeField] Vector2 scaleRange = new Vector2(0.6f, 1.8f);

        PooledFx pooled;
        SurfaceArcFx residual;

        /// <summary>Scale applied for the current body (1 = <see cref="referenceWidth"/>).</summary>
        public float Scale { get; private set; } = 1f;

        /// <summary>True when the floor ring is out for this hit (grounded enemy, quality Medium or higher).</summary>
        public bool GroundRingShown => groundRing != null && groundRing.gameObject.activeInHierarchy;

        public Transform GroundRing => groundRing;

        public void Configure(Transform ring, AudioSource impactSound, float bodyWidth)
        {
            groundRing = ring;
            sound = impactSound;
            referenceWidth = bodyWidth;
        }

        void Awake()
        {
            pooled = GetComponent<PooledFx>();
            residual = GetComponentInChildren<SurfaceArcFx>(true);
        }

        void OnEnable()
        {
            // A spawn without Play keeps the prefab's size and the ring under the contact.
            Scale = 1f;
            transform.localScale = Vector3.one;
            if (groundRing != null)
            {
                groundRing.localPosition = Vector3.zero;
            }

            if (sound != null && sound.clip != null)
            {
                sound.Play();
            }
        }

        /// <param name="bodyWidth">The enemy's body width in metres (its collider); the burst scales with it.</param>
        /// <param name="ground">The floor under the enemy, or null when it is in the air (no ring; the chest burst carries the hit).</param>
        public void Play(float bodyWidth, Vector3? ground)
        {
            Scale = Mathf.Clamp(bodyWidth / referenceWidth, scaleRange.x, scaleRange.y);
            transform.localScale = Vector3.one * Scale;
            if (groundRing != null && ground.HasValue)
            {
                groundRing.SetPositionAndRotation(ground.Value, Quaternion.identity);
            }

            // Restart at the new size and place (same frame as the spawn), then drop the ring in the air.
            if (pooled != null)
            {
                pooled.Play();
            }

            if (residual != null)
            {
                residual.Restart();
            }

            if (groundRing != null && !ground.HasValue)
            {
                groundRing.gameObject.SetActive(false);
            }
        }
    }
}
