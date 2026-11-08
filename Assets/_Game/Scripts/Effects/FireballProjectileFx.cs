using UnityEngine;

namespace CoopDeck.Effects
{
    /// <summary>
    /// The flying fireball: a white-hot core inside swirling flame shells (meshes with Shader Graph fire), a
    /// fire trail, flames and embers left behind in world space (so the fire streams backward), a launch puff
    /// and a glow light (High quality only). Gameplay moves the shot through <see cref="IProjectileFx"/>; this
    /// never decides hits. On impact the core and shells vanish under the blast while the trail, flames and
    /// embers fade out; at the end of its range the shot burns away (dissolve) with a small puff.
    /// </summary>
    [RequireComponent(typeof(PooledFx))]
    public class FireballProjectileFx : MonoBehaviour, IProjectileFx
    {
        [Tooltip("Core and flame shells: scaled by the launch growth and the projectile's visual scale.")]
        [SerializeField] Transform body;
        [Tooltip("Mesh layers that burn away (_Dissolve) when the shot fizzles and hide on impact.")]
        [SerializeField] Renderer[] meshes = new Renderer[0];
        [Tooltip("Emitters that follow the shot: they stop when it ends and their particles finish.")]
        [SerializeField] ParticleSystem[] emitters = new ParticleSystem[0];
        [SerializeField] TrailRenderer[] trails = new TrailRenderer[0];
        [Tooltip("Played when the shot runs out of range.")]
        [SerializeField] ParticleSystem fizzle;
        [SerializeField] Light glow;
        [Tooltip("Body scale when it leaves the hand: it grows from here so it never fills the view.")]
        [SerializeField, Range(0.1f, 1f)] float launchScale = 0.4f;
        [SerializeField, Min(0.01f)] float growTime = 0.12f;
        [Tooltip("Flames and embers left behind start only after this (about 3 m out): none rise in front of the player's own eyes.")]
        [SerializeField, Min(0f)] float emitDelay = 0.07f;
        [Tooltip("Seconds the trail, flames and embers keep fading after the shot ends.")]
        [SerializeField, Min(0.05f)] float linger = 0.65f;
        [SerializeField, Min(0.05f)] float fizzleTime = 0.25f;
        [SerializeField, Min(0f)] float glowIntensity = 2.4f;

        static readonly int DissolveId = Shader.PropertyToID("_Dissolve");
        static readonly int SeedId = Shader.PropertyToID("_Seed");

        PooledFx pooled;
        MaterialPropertyBlock block;
        float[] trailWidths;
        float[] baseDissolve;
        float scale = 1f;
        float age;
        float endAge;
        float seed;
        bool ending;
        bool impacted;
        bool emitting;

        public bool IsEnding => ending;

        /// <summary>Current body scale (launch growth × visual scale).</summary>
        public float BodyScale { get; private set; }

        void Awake()
        {
            pooled = GetComponent<PooledFx>();
            block = new MaterialPropertyBlock();
            trailWidths = new float[trails.Length];
            for (int i = 0; i < trails.Length; i++)
            {
                trailWidths[i] = trails[i] != null ? trails[i].widthMultiplier : 0f;
            }

            // A layer's own material dissolve (the outer flames are cut into licks) is kept under the fizzle.
            baseDissolve = new float[meshes.Length];
            for (int i = 0; i < meshes.Length; i++)
            {
                Material m = meshes[i] != null ? meshes[i].sharedMaterial : null;
                baseDissolve[i] = m != null && m.HasProperty(DissolveId) ? m.GetFloat(DissolveId) : 0f;
            }
        }

        public void Launch(Vector3 position, Vector3 direction, float visualScale)
        {
            if (block == null)
            {
                Awake();
            }

            transform.SetPositionAndRotation(position, Rotation(direction, transform.rotation));
            scale = Mathf.Max(0.1f, visualScale);
            age = 0f;
            endAge = 0f;
            ending = false;
            impacted = false;
            seed = Random.value * 50f;

            foreach (Renderer mesh in meshes)
            {
                if (mesh != null)
                {
                    mesh.enabled = true;
                }
            }

            SetDissolve(0f);
            foreach (TrailRenderer trail in trails)
            {
                if (trail != null)
                {
                    trail.Clear();
                    trail.emitting = true;
                }
            }

            emitting = false;
            foreach (ParticleSystem ps in emitters)
            {
                if (ps != null)
                {
                    ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear); // the pool played them on spawn
                }
            }

            if (fizzle != null)
            {
                fizzle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); // the pool plays every system on spawn
            }

            ApplyBody(launchScale);
        }

        public void Fly(Vector3 position, Vector3 direction)
        {
            if (!ending)
            {
                transform.SetPositionAndRotation(position, Rotation(direction, transform.rotation));
            }
        }

        public void End(bool impact)
        {
            if (ending)
            {
                return;
            }

            ending = true;
            impacted = impact;
            endAge = 0f;
            foreach (ParticleSystem ps in emitters)
            {
                if (ps != null)
                {
                    ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
                }
            }

            foreach (TrailRenderer trail in trails)
            {
                if (trail != null)
                {
                    trail.emitting = false;
                }
            }

            if (impact)
            {
                foreach (Renderer mesh in meshes)
                {
                    if (mesh != null)
                    {
                        mesh.enabled = false; // the blast's own fireball takes over at the same spot
                    }
                }
            }
            else if (fizzle != null && fizzle.gameObject.activeInHierarchy)
            {
                fizzle.Play(true);
            }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            age += dt;
            if (!ending)
            {
                if (!emitting && age >= emitDelay)
                {
                    emitting = true;
                    foreach (ParticleSystem ps in emitters)
                    {
                        if (ps != null && ps.gameObject.activeInHierarchy)
                        {
                            ps.Play(false);
                        }
                    }
                }

                ApplyBody(Mathf.Lerp(launchScale, 1f, Mathf.SmoothStep(0f, 1f, age / growTime)));
                if (glow != null && glow.isActiveAndEnabled)
                {
                    glow.intensity = glowIntensity * (0.8f + 0.2f * Mathf.PerlinNoise(age * 14f, seed)) * Mathf.Min(1f, age / growTime + 0.3f);
                }

                return;
            }

            endAge += dt;
            if (!impacted)
            {
                SetDissolve(Mathf.Clamp01(endAge / fizzleTime));
            }

            if (glow != null && glow.isActiveAndEnabled)
            {
                glow.intensity = impacted ? 0f : glowIntensity * Mathf.Clamp01(1f - endAge / fizzleTime);
            }

            if (endAge >= linger && pooled != null)
            {
                pooled.ReleaseNow();
            }
        }

        void ApplyBody(float growth)
        {
            BodyScale = growth * scale;
            if (body != null)
            {
                body.localScale = Vector3.one * BodyScale;
            }

            for (int i = 0; i < trails.Length; i++)
            {
                if (trails[i] != null)
                {
                    trails[i].widthMultiplier = trailWidths[i] * BodyScale;
                }
            }
        }

        void SetDissolve(float amount)
        {
            for (int i = 0; i < meshes.Length; i++)
            {
                Renderer mesh = meshes[i];
                if (mesh == null)
                {
                    continue;
                }

                mesh.GetPropertyBlock(block);
                block.SetFloat(DissolveId, Mathf.Lerp(baseDissolve[i], 1f, amount));
                block.SetFloat(SeedId, seed);
                mesh.SetPropertyBlock(block);
            }
        }

        static Quaternion Rotation(Vector3 direction, Quaternion fallback)
        {
            return direction.sqrMagnitude > 1e-8f ? Quaternion.LookRotation(direction) : fallback;
        }
    }
}
