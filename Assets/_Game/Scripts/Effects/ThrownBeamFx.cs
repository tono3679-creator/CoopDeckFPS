using UnityEngine;

namespace CoopDeck.Effects
{
    /// <summary>
    /// A thrown object flying from the caster to the hit (the ice lance): the spear faces its flight, its trail and
    /// particles follow, it vanishes on arrival (the caster plays the shatter at the hit).
    /// </summary>
    public class ThrownBeamFx : MonoBehaviour, IBeamFx
    {
        [SerializeField] Transform projectile;
        [SerializeField, Min(1f)] float speed = 110f;
        [Tooltip("Metres along the path the thrown object appears at: a long spear starting at the muzzle would reach back past the camera.")]
        [SerializeField, Min(0f)] float startOffset = 2.4f;

        Vector3 from;
        Vector3 to;
        float distance;
        float travelled;
        bool flying;

        public bool Flying => flying;

        public void Configure(Transform thrown, float travelSpeed)
        {
            projectile = thrown;
            speed = travelSpeed;
        }

        public void Play(Vector3 start, Vector3 end)
        {
            Vector3 path = end - start;
            float full = path.magnitude;
            float skip = Mathf.Min(startOffset, full * 0.5f);
            from = full > 1e-4f ? start + path / full * skip : start;
            to = end;
            distance = Vector3.Distance(from, end);
            travelled = 0f;
            flying = distance > 0.05f && projectile != null;
            if (projectile == null)
            {
                return;
            }

            projectile.gameObject.SetActive(flying);
            projectile.position = from;
            if (flying)
            {
                projectile.rotation = Quaternion.LookRotation((end - from) / distance);
            }

            foreach (TrailRenderer trail in projectile.GetComponentsInChildren<TrailRenderer>(true))
            {
                trail.Clear();
            }

            // The spear was inactive when the pool played the effect: start its particles now that it flies.
            foreach (ParticleSystem ps in projectile.GetComponentsInChildren<ParticleSystem>())
            {
                ps.Clear(true);
                ps.Play(true);
            }
        }

        void Update()
        {
            if (!flying)
            {
                return;
            }

            travelled += speed * Time.deltaTime;
            if (travelled >= distance)
            {
                flying = false;
                projectile.position = to;
                projectile.gameObject.SetActive(false);
                return;
            }

            projectile.position = Vector3.Lerp(from, to, travelled / distance);
        }

        void OnDisable() => flying = false;
    }
}
