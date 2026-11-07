using CoopDeck.Combat;
using UnityEngine;

namespace CoopDeck.UI
{
    /// <summary>Billboarded HP bar above a target, built from two quads. Hidden at full health.</summary>
    public class WorldHealthBar : MonoBehaviour
    {
        [SerializeField] Health health;
        [SerializeField] Transform fill;
        [SerializeField] Transform background;
        [SerializeField] bool hideWhenFull = true;

        Camera cachedCamera;

        public void Configure(Health target, Transform fillQuad, Transform backgroundQuad)
        {
            health = target;
            fill = fillQuad;
            background = backgroundQuad;
        }

        void Awake()
        {
            if (health == null)
            {
                health = GetComponentInParent<Health>();
            }
        }

        void LateUpdate()
        {
            if (health == null || fill == null)
            {
                return;
            }

            float normalized = health.Normalized;
            bool visible = health.IsAlive && (!hideWhenFull || normalized < 0.999f);
            fill.gameObject.SetActive(visible);
            if (background != null)
            {
                background.gameObject.SetActive(visible);
            }

            if (!visible)
            {
                return;
            }

            // Fill quad is 1 unit wide; shrink it from the right edge.
            Vector3 scale = fill.localScale;
            scale.x = Mathf.Max(0.0001f, normalized);
            fill.localScale = scale;
            fill.localPosition = new Vector3((normalized - 1f) * 0.5f, 0f, -0.001f);

            if (cachedCamera == null)
            {
                cachedCamera = Camera.main;
            }

            if (cachedCamera != null)
            {
                transform.rotation = Quaternion.LookRotation(transform.position - cachedCamera.transform.position);
            }
        }
    }
}
