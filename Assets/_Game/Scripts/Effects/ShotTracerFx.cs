using UnityEngine;

namespace CoopDeck.Effects
{
    /// <summary>
    /// A shot's light from the muzzle to the hit, in two layers:
    /// <list type="bullet">
    /// <item>the <b>streak</b>: a bright, tapered bolt (thick hot head, thin tail) that flies to the hit point;</item>
    /// <item>the <b>wake</b>: a thin line left behind from the muzzle to the streak's tail that fades out quickly, so
    /// sustained fire reads as a stream of shots rather than isolated dots.</item>
    /// </list>
    /// With <see cref="jitter"/> both lines crackle sideways (electric rounds), re-rolled every <see cref="jitterInterval"/>.
    /// Colours and shapes come from the materials (the shot shaders read the line's UV: x along, 0 = tail).
    /// </summary>
    public class ShotTracerFx : MonoBehaviour, IBeamFx
    {
        [SerializeField] LineRenderer streak;
        [SerializeField] LineRenderer wake;
        [SerializeField, Min(1f)] float speed = 260f;
        [SerializeField, Min(0.05f)] float streakLength = 3.2f;
        [SerializeField, Min(0.001f)] float streakWidth = 0.06f;
        [SerializeField, Min(0.001f)] float wakeWidth = 0.022f;
        [Tooltip("Seconds the wake takes to fade after the streak arrives.")]
        [SerializeField, Min(0.01f)] float wakeFade = 0.12f;
        [Tooltip("Sideways crackle in metres (0 = straight).")]
        [SerializeField, Min(0f)] float jitter;
        [SerializeField, Min(0.005f)] float jitterInterval = 0.03f;

        const int StreakPoints = 6;
        const int WakePoints = 10;

        Vector3 from;
        Vector3 direction;
        Vector3 side;
        Vector3 up;
        float distance;
        float travelled;
        float sinceArrival = -1f;
        float rerollTimer;
        int seed;
        bool playing;
        readonly Vector2[] offsets = new Vector2[WakePoints];

        public bool Playing => playing;
        public float Speed => speed;

        public void Configure(LineRenderer streakLine, LineRenderer wakeLine, float travelSpeed, float length, float width, float trailWidth,
            float fade, float crackle)
        {
            streak = streakLine;
            wake = wakeLine;
            speed = travelSpeed;
            streakLength = length;
            streakWidth = width;
            wakeWidth = trailWidth;
            wakeFade = fade;
            jitter = crackle;
        }

        void Awake()
        {
            Prepare(streak, StreakPoints);
            Prepare(wake, WakePoints);
        }

        static void Prepare(LineRenderer line, int points)
        {
            if (line == null)
            {
                return;
            }

            line.useWorldSpace = true;
            line.positionCount = points;
            line.enabled = false;
        }

        public void Play(Vector3 start, Vector3 end)
        {
            from = start;
            Vector3 delta = end - start;
            distance = delta.magnitude;
            direction = distance > 0.0001f ? delta / distance : Vector3.forward;
            side = Vector3.Cross(direction, Mathf.Abs(direction.y) > 0.95f ? Vector3.right : Vector3.up).normalized;
            up = Vector3.Cross(side, direction);
            travelled = 0f;
            sinceArrival = -1f;
            rerollTimer = 0f;
            seed = Random.Range(0, 1 << 16);
            playing = distance > 0.05f;
            Reroll();
            if (streak != null)
            {
                streak.widthMultiplier = streakWidth;
                streak.enabled = playing;
            }

            if (wake != null)
            {
                wake.widthMultiplier = wakeWidth;
                wake.enabled = playing;
            }

            UpdateLines(0f);
        }

        void Update()
        {
            if (!playing)
            {
                return;
            }

            float dt = Time.deltaTime;
            travelled += speed * dt;
            if (jitter > 0f && (rerollTimer += dt) >= jitterInterval)
            {
                rerollTimer = 0f;
                Reroll();
            }

            if (travelled - streakLength >= distance)
            {
                sinceArrival = sinceArrival < 0f ? 0f : sinceArrival + dt;
                if (streak != null)
                {
                    streak.enabled = false;
                }

                if (sinceArrival >= wakeFade)
                {
                    Stop();
                    return;
                }
            }

            UpdateLines(sinceArrival);
        }

        void Reroll()
        {
            var random = new System.Random(seed++);
            for (int i = 0; i < offsets.Length; i++)
            {
                offsets[i] = new Vector2((float)random.NextDouble() * 2f - 1f, (float)random.NextDouble() * 2f - 1f);
            }
        }

        Vector3 Along(float d, int index, int count)
        {
            Vector3 p = from + direction * d;
            if (jitter <= 0f || index == 0 || index == count - 1)
            {
                return p;
            }

            Vector2 o = offsets[index % offsets.Length] * jitter;
            return p + side * o.x + up * o.y;
        }

        void UpdateLines(float fading)
        {
            float head = Mathf.Min(travelled, distance);
            float tail = Mathf.Clamp(travelled - streakLength, 0f, distance);
            if (streak != null && streak.enabled)
            {
                for (int i = 0; i < StreakPoints; i++)
                {
                    float t = i / (float)(StreakPoints - 1);
                    streak.SetPosition(i, Along(Mathf.Lerp(tail, head, t), i, StreakPoints));
                }
            }

            if (wake != null)
            {
                float end = fading >= 0f ? distance : tail;
                float start = Mathf.Min(0.15f, end); // a gap at the muzzle: the flash covers it
                for (int i = 0; i < WakePoints; i++)
                {
                    float t = i / (float)(WakePoints - 1);
                    wake.SetPosition(i, Along(Mathf.Lerp(start, end, t), i, WakePoints));
                }

                float alpha = fading >= 0f ? 1f - fading / wakeFade : 1f;
                wake.widthMultiplier = wakeWidth * Mathf.Max(0.05f, alpha);
                Color c = Color.white;
                c.a = alpha * alpha;
                wake.startColor = c;
                wake.endColor = c;
            }
        }

        void Stop()
        {
            playing = false;
            if (streak != null)
            {
                streak.enabled = false;
            }

            if (wake != null)
            {
                wake.enabled = false;
            }
        }

        void OnDisable() => Stop();
    }
}
