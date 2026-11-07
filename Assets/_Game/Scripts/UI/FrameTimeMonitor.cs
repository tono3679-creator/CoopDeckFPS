using UnityEngine;

namespace CoopDeck.UI
{
    /// <summary>
    /// Rolling frame-time statistics (average FPS, 1% low, worst frame) for performance work on
    /// the ROG Ally X. Uses unscaled time so pausing does not skew it.
    /// </summary>
    public class FrameTimeMonitor : MonoBehaviour
    {
        [SerializeField, Range(60, 2000)] int sampleCount = 600;

        float[] samples;
        int head;
        int filled;
        float[] scratch;

        public float AverageFps { get; private set; }
        public float OnePercentLowFps { get; private set; }
        public float AverageMs { get; private set; }
        public float WorstMs { get; private set; }
        public int SampleCount => filled;

        void Awake()
        {
            samples = new float[sampleCount];
            scratch = new float[sampleCount];
        }

        /// <summary>When false, stats are only computed on <see cref="Recompute"/> (used for long captures).</summary>
        public bool AutoRecompute { get; set; } = true;

        /// <summary>Resizes the sample buffer and clears it.</summary>
        public void SetCapacity(int count)
        {
            count = Mathf.Max(60, count);
            samples = new float[count];
            scratch = new float[count];
            ResetStats();
        }

        public void ResetStats()
        {
            head = 0;
            filled = 0;
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (dt <= 0f)
            {
                return;
            }

            samples[head] = dt;
            head = (head + 1) % samples.Length;
            filled = Mathf.Min(filled + 1, samples.Length);

            // Recompute a few times per second; sorting 600 floats is cheap but not free.
            if (AutoRecompute && Time.frameCount % 15 == 0)
            {
                Recompute();
            }
        }

        public void Recompute()
        {
            if (filled == 0)
            {
                return;
            }

            float sum = 0f;
            for (int i = 0; i < filled; i++)
            {
                sum += samples[i];
                scratch[i] = samples[i];
            }

            System.Array.Sort(scratch, 0, filled);
            float worst = scratch[filled - 1];

            // 1% low = average FPS over the slowest 1% of frames.
            int slowCount = Mathf.Max(1, filled / 100);
            float slowSum = 0f;
            for (int i = filled - slowCount; i < filled; i++)
            {
                slowSum += scratch[i];
            }

            float average = sum / filled;
            AverageMs = average * 1000f;
            AverageFps = 1f / average;
            OnePercentLowFps = slowCount / slowSum;
            WorstMs = worst * 1000f;
        }
    }
}
