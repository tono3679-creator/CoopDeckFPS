using System.Collections.Generic;
using CoopDeck.Core;
using UnityEngine;

namespace CoopDeck.Effects
{
    /// <summary>
    /// A thick stylized lightning bolt (Docs/LIGHTNING_VFX.md): one main strip (Line Renderer with the SG_FX_LightningBolt
    /// material: thin white-hot core, wide cyan body, azure edge, flowing noise) plus short thin branches gathered at the
    /// impact end. The first strike (strength 1) shows whole in one frame, punches in at a little over full width and is
    /// gone ~0.1 s later, its far end fading first, so the hit's burst carries what follows. A chained hop (strength below
    /// 1, with a travel time) runs from one enemy to the next: its tip travels for a few hundredths of a second, then it
    /// flashes and fades the same way. A hop's white core and cyan edge are set apart from the first strike's
    /// (<see cref="chainCoreWidth"/>, <see cref="chainGlowFalloff"/>) so it keeps an outline against a bright wall.
    /// Pooled through <see cref="FxPool"/>; the root's <see cref="PooledFx"/> lifetime ends it.
    /// </summary>
    public class LightningBoltFx : MonoBehaviour
    {
        static readonly int CoreWidthId = Shader.PropertyToID("_CoreWidth");
        static readonly int GlowFalloffId = Shader.PropertyToID("_GlowFalloff");
        static readonly int HaloOpacityId = Shader.PropertyToID("_HaloOpacity");

        [SerializeField] LineRenderer main;
        [SerializeField] LineRenderer[] branches = new LineRenderer[0];

        [Header("Shape")]
        [Tooltip("Main strip width of the first strike (the shader draws the narrow core inside it).")]
        [SerializeField, Min(0.01f)] float width = 0.6f;
        [SerializeField, Min(0.05f)] float segmentLength = 0.6f;
        [Tooltip("Zig-zag amplitude as a fraction of the bolt length (capped by Max Amplitude).")]
        [SerializeField, Range(0f, 0.4f)] float amplitude = 0.07f;
        [SerializeField, Min(0f)] float maxAmplitude = 0.55f;
        [Tooltip("Metres skipped in front of the start (a weapon muzzle right in front of the camera) so the bolt never fills the screen.")]
        [SerializeField, Min(0f)] float startClearance = 0.7f;
        [Tooltip("Closer to the camera than this, the bolt gets thinner (down to Near Width Scale) so it never covers the screen.")]
        [SerializeField, Min(0.1f)] float fullWidthDistance = 7f;
        [SerializeField, Range(0.2f, 1f)] float nearWidthScale = 0.45f;

        [Header("Branches")]
        [SerializeField, Range(0.1f, 1f)] float branchWidth = 0.45f;
        [SerializeField] Vector2 branchLength = new Vector2(0.3f, 0.8f);
        [Tooltip("Where along the bolt branches grow (0 = its start, 1 = the impact): gathered at the impact end.")]
        [SerializeField] Vector2 branchSpan = new Vector2(0.62f, 0.95f);

        [Header("Timing")]
        [Tooltip("First strike: seconds from the punch until it is gone (the white core retracts in 80–120 ms).")]
        [SerializeField, Min(0.03f)] float tail = 0.1f;
        [Tooltip("A chained hop: seconds from its arrival until it is gone.")]
        [SerializeField, Min(0.03f)] float chainTail = 0.09f;
        [Tooltip("Flashes per bolt (1 = one strike; more re-flash every Stroke Interval).")]
        [SerializeField, Range(1, 4)] int strokes = 1;
        [SerializeField, Min(0.03f)] float strokeInterval = 0.06f;
        [Tooltip("Detail re-roll (the flicker).")]
        [SerializeField, Min(0.01f)] float rerollInterval = 0.04f;

        [Header("Chained hop look")]
        [Tooltip("Width of a chained hop relative to the first strike.")]
        [SerializeField, Range(0.3f, 1.5f)] float chainWidthScale = 0.95f;
        [Tooltip("White core width of a chained hop (material units; the first strike's material uses 0.11).")]
        [SerializeField, Range(0.02f, 0.6f)] float chainCoreWidth = 0.2f;
        [Tooltip("Cyan edge falloff of a chained hop (higher = a narrower, crisper cyan edge; the first strike's material uses 1.5).")]
        [SerializeField, Range(0.2f, 10f)] float chainGlowFalloff = 2.2f;
        [SerializeField, Range(0f, 1f)] float chainHaloOpacity = 0.95f;

        readonly List<Vector3> path = new List<Vector3>(32);
        readonly List<Vector2> baseOffsets = new List<Vector2>(32);
        readonly List<Vector3> branchPath = new List<Vector3>(8);
        readonly List<Vector2> branchOffsets = new List<Vector2>(8);
        readonly float[] branchEnd = new float[8];
        readonly GradientColorKey[] colorKeys = new GradientColorKey[2];
        readonly GradientAlphaKey[] alphaKeys = new GradientAlphaKey[3];
        Gradient gradient;
        MaterialPropertyBlock block;
        Vector3 from;
        Vector3 to;
        float strength = 1f;
        float nearScale = 1f;
        float age;
        float rerollTimer;
        float travel;
        float playDuration;
        int stroke;
        int activeBranches;
        bool chained;
        bool playing;
        System.Random random = new System.Random(1);

        /// <summary>How long this play lasts (the first strike's before any play).</summary>
        public float Duration => playDuration > 0f ? playDuration : DurationFor(strokes, strokeInterval, tail);
        public float Strength => strength;
        public bool Playing => playing;
        public int ActiveBranches => activeBranches;
        public int Strokes => strokes;
        public float Travel => travel;

        /// <summary>True once a chained hop's tip has reached its end (always for the first strike).</summary>
        public bool Arrived => age >= travel;

        public int MainPointCount => main != null ? main.positionCount : 0;

        public void Configure(LineRenderer mainStrip, LineRenderer[] branchStrips)
        {
            main = mainStrip;
            branches = branchStrips;
        }

        void OnEnable()
        {
            if (!playing)
            {
                Hide();
            }
        }

        void OnDisable()
        {
            playing = false;
        }

        /// <param name="boltStrength">1 = the first strike; about 0.65 for a chained hop (its own look, fewer branches).</param>
        /// <param name="travelTime">Seconds for the tip to run from start to end (a chained hop); 0 = whole at once.</param>
        public void Play(Vector3 start, Vector3 end, float boltStrength = 1f, int seed = 0, float travelTime = 0f)
        {
            strength = Mathf.Clamp01(boltStrength);
            chained = strength < 0.9f;
            Vector3 axis = end - start;
            float length = axis.magnitude;
            if (startClearance > 0f && length > startClearance * 2f)
            {
                start += axis / length * startClearance;
            }

            from = start;
            to = end;
            random = new System.Random(seed != 0 ? seed : Random.Range(1, int.MaxValue));
            age = 0f;
            rerollTimer = 0f;
            stroke = 0;
            travel = Mathf.Max(0f, travelTime);
            playDuration = travel + DurationFor(strokes, strokeInterval, chained ? chainTail : tail);
            playing = true;
            activeBranches = Mathf.Min(branches.Length, LightningPath.BranchCount(PerformanceSettings.Vfx, strength));
            Camera view = Camera.main;
            nearScale = view != null ? NearScale(DistanceToSegment(view.transform.position, from, to), fullWidthDistance, nearWidthScale) : 1f;
            ApplyLook();
            Rebuild(true, true);
            Apply();
        }

        /// <summary>A chained hop gets its own core and edge widths (per renderer); the first strike uses the material as is.</summary>
        void ApplyLook()
        {
            if (main == null)
            {
                return;
            }

            if (!chained)
            {
                main.SetPropertyBlock(null);
                return;
            }

            block ??= new MaterialPropertyBlock();
            block.Clear();
            block.SetFloat(CoreWidthId, chainCoreWidth);
            block.SetFloat(GlowFalloffId, chainGlowFalloff);
            block.SetFloat(HaloOpacityId, chainHaloOpacity);
            main.SetPropertyBlock(block);
        }

        void Update()
        {
            if (!playing)
            {
                return;
            }

            float dt = Time.deltaTime;
            bool wasTravelling = age < travel;
            age += dt;
            if (age >= playDuration)
            {
                playing = false;
                Hide();
                return;
            }

            if (age >= travel)
            {
                int current = Mathf.Min(strokes - 1, Mathf.FloorToInt((age - travel) / strokeInterval));
                if (current != stroke || wasTravelling)
                {
                    // Arrival, or a re-flash: fresh detail and branches.
                    stroke = current;
                    rerollTimer = 0f;
                    Rebuild(false, true);
                }
                else
                {
                    rerollTimer += dt;
                    if (rerollTimer >= rerollInterval)
                    {
                        rerollTimer = 0f;
                        Rebuild(false, false);
                    }
                }
            }

            Apply();
        }

        void Rebuild(bool newShape, bool newStroke)
        {
            float length = Vector3.Distance(from, to);
            float amp = Mathf.Min(length * amplitude, maxAmplitude) * Mathf.Lerp(0.75f, 1f, strength);
            float flicker = newShape ? 0f : newStroke ? 0.55f : 0.3f;
            LightningPath.Build(from, to, LightningPath.Segments(length, segmentLength), amp, random, baseOffsets, newShape, flicker, path);
            if (age >= travel)
            {
                SetPositions(main, path);
            }

            float strokeStart = travel + stroke * strokeInterval;
            bool last = stroke >= strokes - 1;
            for (int i = 0; i < branches.Length; i++)
            {
                LineRenderer branch = branches[i];
                if (branch == null)
                {
                    continue;
                }

                bool on = i < activeBranches;
                branch.enabled = on && age >= travel;
                if (!on)
                {
                    continue;
                }

                // Short branches gathered near the impact, moving at every re-roll; each flash brings them back.
                int lo = Mathf.Clamp(Mathf.RoundToInt((path.Count - 1) * branchSpan.x), 1, path.Count - 2);
                int hi = Mathf.Clamp(Mathf.RoundToInt((path.Count - 1) * branchSpan.y), lo + 1, path.Count - 1);
                Vector3 origin = path[random.Next(lo, hi)];
                Vector3 dir = (to - from).normalized;
                LightningPath.Perpendiculars(dir, out Vector3 side, out Vector3 up);
                float angle = (float)random.NextDouble() * Mathf.PI * 2f;
                Vector3 outward = (side * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * Mathf.Lerp(0.7f, 1.4f, (float)random.NextDouble());
                float reach = Mathf.Lerp(branchLength.x, branchLength.y, (float)random.NextDouble()) * Mathf.Lerp(0.7f, 1f, strength);
                reach = Mathf.Min(reach, length * 0.3f);
                Vector3 tip = origin + (dir + outward).normalized * reach;
                LightningPath.Build(origin, tip, 4, reach * 0.22f, random, branchOffsets, true, 0f, branchPath);
                SetPositions(branch, branchPath);
                if (newStroke)
                {
                    float span = last ? (chained ? chainTail : tail) : strokeInterval;
                    branchEnd[i] = strokeStart + span * Mathf.Lerp(0.5f, 0.85f, (float)random.NextDouble());
                }
            }
        }

        void Apply()
        {
            float tint = Mathf.Lerp(0.8f, 1f, strength);
            if (age < travel)
            {
                // The hop's tip runs toward the next enemy at full width; branches come with the arrival.
                WritePartial(Mathf.Clamp01(age / travel));
                main.enabled = true;
                main.widthMultiplier = BaseWidth();
                SetColours(tint, 1f, 1f);
                foreach (LineRenderer branch in branches)
                {
                    if (branch != null)
                    {
                        branch.enabled = false;
                    }
                }

                return;
            }

            float local = age - travel;
            bool last = stroke >= strokes - 1;
            float since = local - stroke * strokeInterval;
            float t = Mathf.Clamp01(since / (last ? (chained ? chainTail : tail) : strokeInterval));
            // Punch in over ~20 ms at a little over full width, then thin out; the last flash fades in its second half.
            float start = stroke == 0 ? (travel > 0f ? 0.9f : 0.6f) : 0.8f;
            float punch = since < 0.02f ? Mathf.Lerp(start, 1.15f, since / 0.02f) : Mathf.Lerp(1.15f, last ? 0.35f : 0.6f, Mathf.SmoothStep(0f, 1f, t));
            punch *= 1f - 0.1f * stroke;
            float alpha = last ? (t < 0.45f ? 1f : 1f - (t - 0.45f) / 0.55f) : Mathf.Lerp(1f, 0.6f, t);
            // The far end (the sky for the first strike, the previous enemy for a hop) goes first.
            float far = last ? alpha * (1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.6f))) : alpha;
            tint *= Mathf.Lerp(1f, 0.75f, t);

            main.enabled = true;
            main.widthMultiplier = BaseWidth() * punch;
            SetColours(tint, far, alpha);

            float branchScale = width * branchWidth * nearScale * (chained ? chainWidthScale : 1f);
            for (int i = 0; i < activeBranches && i < branches.Length; i++)
            {
                LineRenderer branch = branches[i];
                if (branch == null)
                {
                    continue;
                }

                bool alive = age < branchEnd[i];
                branch.enabled = alive;
                if (!alive)
                {
                    continue;
                }

                branch.widthMultiplier = branchScale * punch;
                branch.startColor = branch.endColor = new Color(tint, tint, tint, alpha);
            }
        }

        float BaseWidth()
        {
            if (!chained)
            {
                return width * nearScale;
            }

            // Later hops (strength 0.65 x 0.85 ...) get a little thinner.
            return width * chainWidthScale * Mathf.Lerp(0.85f, 1f, Mathf.InverseLerp(0.4f, 0.65f, strength)) * nearScale;
        }

        void SetColours(float tint, float farAlpha, float nearAlpha)
        {
            gradient ??= new Gradient();
            var c = new Color(tint, tint, tint);
            colorKeys[0] = new GradientColorKey(c, 0f);
            colorKeys[1] = new GradientColorKey(c, 1f);
            alphaKeys[0] = new GradientAlphaKey(farAlpha, 0f);
            alphaKeys[1] = new GradientAlphaKey(Mathf.Lerp(farAlpha, nearAlpha, 0.6f), 0.5f);
            alphaKeys[2] = new GradientAlphaKey(nearAlpha, 1f);
            gradient.SetKeys(colorKeys, alphaKeys);
            main.colorGradient = gradient;
        }

        /// <summary>The path up to <paramref name="progress"/> (0..1), its last point interpolated: the travelling tip.</summary>
        void WritePartial(float progress)
        {
            int n = path.Count;
            if (n < 2 || progress >= 1f)
            {
                SetPositions(main, path);
                return;
            }

            float f = progress * (n - 1);
            int whole = Mathf.FloorToInt(f);
            main.positionCount = whole + 2;
            for (int i = 0; i <= whole; i++)
            {
                main.SetPosition(i, path[i]);
            }

            main.SetPosition(whole + 1, Vector3.Lerp(path[whole], path[whole + 1], f - whole));
        }

        void Hide()
        {
            if (main != null)
            {
                main.enabled = false;
            }

            foreach (LineRenderer branch in branches)
            {
                if (branch != null)
                {
                    branch.enabled = false;
                }
            }
        }

        /// <summary>Length of a bolt with this many flashes: the flashes, then the last one's fade (its travel comes on top).</summary>
        public static float DurationFor(int strokeCount, float interval, float fade) => Mathf.Max(0, strokeCount - 1) * interval + fade;

        /// <summary>Width factor for a bolt this far from the camera: 1 from <paramref name="fullDistance"/> out, down to <paramref name="nearScale"/> at 0.</summary>
        public static float NearScale(float distance, float fullDistance, float nearScale) => Mathf.Lerp(nearScale, 1f, Mathf.Clamp01(distance / fullDistance));

        public static float DistanceToSegment(Vector3 point, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float t = Mathf.Clamp01(Vector3.Dot(point - a, ab) / Mathf.Max(1e-5f, ab.sqrMagnitude));
            return Vector3.Distance(point, a + ab * t);
        }

        static void SetPositions(LineRenderer line, List<Vector3> points)
        {
            line.positionCount = points.Count;
            for (int i = 0; i < points.Count; i++)
            {
                line.SetPosition(i, points[i]);
            }
        }
    }
}
