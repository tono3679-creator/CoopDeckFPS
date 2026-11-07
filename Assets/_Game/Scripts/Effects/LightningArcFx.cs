using UnityEngine;

namespace CoopDeck.Effects
{
    /// <summary>
    /// A jagged electric arc between two points on a <see cref="LineRenderer"/>. The zig-zag is
    /// re-rolled a few times per second so it flickers, and the arc thins out over its life.
    /// Pooled through <see cref="FxPool"/> (the root's <see cref="PooledFx"/> lifetime ends it).
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public class LightningArcFx : MonoBehaviour
    {
        [SerializeField, Range(2, 24)] int segments = 10;
        [Tooltip("Sideways jitter as a fraction of the arc length (capped by Max Jitter).")]
        [SerializeField, Range(0f, 0.5f)] float jitter = 0.12f;
        [SerializeField, Min(0f)] float maxJitter = 0.9f;
        [SerializeField, Min(0.01f)] float rerollInterval = 0.045f;
        [SerializeField, Min(0.001f)] float width = 0.12f;
        [SerializeField, Min(0.05f)] float duration = 0.35f;

        LineRenderer line;
        Vector3 from;
        Vector3 to;
        float age;
        float rerollTimer;
        bool playing;
        System.Random random = new System.Random(1);

        public float Duration => duration;

        void Awake()
        {
            line = GetComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.enabled = false;
        }

        public void Play(Vector3 start, Vector3 end, int seed = 0)
        {
            if (line == null)
            {
                Awake();
            }

            from = start;
            to = end;
            age = 0f;
            rerollTimer = 0f;
            playing = true;
            random = new System.Random(seed != 0 ? seed : Time.frameCount * 7919 + GetHashCode());
            line.positionCount = segments + 1;
            line.enabled = true;
            Reroll();
            line.widthMultiplier = width;
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
                line.enabled = false;
                return;
            }

            rerollTimer -= Time.deltaTime;
            if (rerollTimer <= 0f)
            {
                Reroll();
            }

            line.widthMultiplier = width * (1f - age / duration);
        }

        void Reroll()
        {
            rerollTimer = rerollInterval;
            Vector3 delta = to - from;
            float length = delta.magnitude;
            Vector3 axis = length > 0.0001f ? delta / length : Vector3.forward;
            Vector3 side = Vector3.Cross(axis, Vector3.up);
            if (side.sqrMagnitude < 0.001f)
            {
                side = Vector3.Cross(axis, Vector3.right);
            }

            side.Normalize();
            Vector3 up = Vector3.Cross(side, axis);
            float amplitude = Mathf.Min(maxJitter, length * jitter);

            for (int i = 0; i <= segments; i++)
            {
                float t = (float)i / segments;
                Vector3 point = from + delta * t;
                if (i > 0 && i < segments)
                {
                    // Strongest in the middle, pinned at both ends.
                    float envelope = Mathf.Sin(t * Mathf.PI);
                    float a = (float)(random.NextDouble() * 2.0 - 1.0) * amplitude * envelope;
                    float b = (float)(random.NextDouble() * 2.0 - 1.0) * amplitude * envelope;
                    point += side * a + up * b;
                }

                line.SetPosition(i, point);
            }
        }

        void OnDisable()
        {
            playing = false;
            if (line != null)
            {
                line.enabled = false;
            }
        }
    }
}
