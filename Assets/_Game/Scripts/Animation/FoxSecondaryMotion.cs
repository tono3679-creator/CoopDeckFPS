using UnityEngine;

namespace CoopDeck.Animation
{
    /// <summary>
    /// Procedural tail and ears for the Fox Gunner (the clips never key them):
    ///   tail  - slow idle sway, lags behind turns, swings wider and lifts with speed, trails up when falling,
    ///           bounces on landing, lies down when downed, fans up for an ultimate (Fox_Tail_Ultimate)
    ///   ears  - small idle twitches (Fox_Idle_Expression), tilt back when running, flatten on a hit
    ///           (Fox_Ear_Reaction), perk up while casting, droop when downed
    /// Springs settle instead of wobbling forever. Runs after the Animator and Animation Rigging (LateUpdate)
    /// and costs a handful of rotations per frame; it sleeps while the body is not rendered.
    /// </summary>
    [DisallowMultipleComponent]
    public class FoxSecondaryMotion : MonoBehaviour
    {
        [SerializeField] CharacterAnimatorDriver driver;
        [SerializeField] Transform hips;
        [SerializeField] Transform[] tail = new Transform[0];
        [SerializeField] Transform earLeft;
        [SerializeField] Transform earRight;
        [SerializeField] Renderer visibilityRenderer;

        [Header("Tail")]
        [SerializeField] float tailFrequency = 2.4f;
        [SerializeField, Range(0.1f, 1.5f)] float tailDamping = 0.55f;
        [SerializeField] float turnLag = 0.06f;          // degrees of tail yaw per degree/second of body turn
        [SerializeField] float maxTailYaw = 38f;
        [Tooltip("Share of the pelvis' forward lean the tail takes back (keeps it hanging while running).")]
        [SerializeField, Range(0f, 1f)] float leanCompensation = 0.8f;

        [Header("Ears")]
        [SerializeField] float earFrequency = 4.5f;
        [SerializeField, Range(0.1f, 1.5f)] float earDamping = 0.5f;

        struct Bone
        {
            public Transform Transform;
            public Quaternion Rest;
            public Vector3 UpAxis;     // character up in bone-local space (yaw)
            public Vector3 RightAxis;  // character right in bone-local space (pitch)
            public AngleSpring Yaw;
            public AngleSpring Pitch;
        }

        Bone[] tailBones = new Bone[0];
        Bone earL;
        Bone earR;
        bool initialized;
        float swayPhase;
        float lastYaw;
        Vector3 lastHipsPos;
        float hitAmount;
        float ultimate;
        float twitchTimer = 2f;
        float twitchLeft;
        float twitchRight;
        float landKick;
        bool wasGrounded = true;
        Vector3 hipsUpLocal = Vector3.up;
        float restLean;

        public float UltimateAmount => ultimate;
        public float HitAmount => hitAmount;

        public void Configure(CharacterAnimatorDriver owner, Transform hipsBone, Transform[] tailChain, Transform left, Transform right, Renderer visible)
        {
            driver = owner;
            hips = hipsBone;
            tail = tailChain;
            earLeft = left;
            earRight = right;
            visibilityRenderer = visible;
            initialized = false;
        }

        void Awake()
        {
            if (driver == null)
            {
                driver = GetComponentInParent<CharacterAnimatorDriver>();
            }
        }

        void OnEnable()
        {
            if (driver != null)
            {
                driver.SkillStarted += OnSkill;
                driver.Hurt += OnHurt;
            }
        }

        void OnDisable()
        {
            if (driver != null)
            {
                driver.SkillStarted -= OnSkill;
                driver.Hurt -= OnHurt;
            }
        }

        void OnSkill(SkillMotion motion)
        {
            if (motion == SkillMotion.Ultimate)
            {
                ultimate = 1.6f; // seconds of fan, eased out below
            }
        }

        void OnHurt(bool heavy) => hitAmount = heavy ? 1f : Mathf.Max(hitAmount, 0.6f);

        /// <summary>Records rest rotations and character axes. Call while the body is in its bind pose.</summary>
        void Initialize()
        {
            Transform root = transform;
            tailBones = new Bone[tail != null ? tail.Length : 0];
            for (int i = 0; i < tailBones.Length; i++)
            {
                tailBones[i] = MakeBone(tail[i], root);
            }

            earL = MakeBone(earLeft, root);
            earR = MakeBone(earRight, root);
            if (hips != null)
            {
                hipsUpLocal = Quaternion.Inverse(hips.rotation) * root.up; // the hips' "up" in the bind pose
                restLean = 0f;
            }

            lastYaw = root.eulerAngles.y;
            lastHipsPos = hips != null ? hips.position : root.position;
            initialized = true;
        }

        static Bone MakeBone(Transform t, Transform root)
        {
            var b = new Bone { Transform = t };
            if (t != null)
            {
                b.Rest = t.localRotation;
                Quaternion inv = Quaternion.Inverse(t.rotation);
                b.UpAxis = (inv * root.up).normalized;
                b.RightAxis = (inv * root.right).normalized;
            }

            return b;
        }

        void Start()
        {
            if (!initialized)
            {
                Initialize();
            }
        }

        void LateUpdate()
        {
            if (!initialized || driver == null)
            {
                return;
            }

            float dt = Time.deltaTime;
            Animator animator = driver.Animator;
            // Sleep off screen, like the Animator itself; bodies that always animate (the local player's, whose shadow
            // is drawn) keep their tail and ears going.
            bool alwaysAnimate = animator != null && animator.cullingMode == AnimatorCullingMode.AlwaysAnimate;
            if (dt <= 0f || (!alwaysAnimate && visibilityRenderer != null && !visibilityRenderer.isVisible))
            {
                return;
            }

            float speed = animator != null ? animator.GetFloat(AnimatorParams.SpeedId) : 0f;
            bool grounded = animator == null || animator.GetBool(AnimatorParams.IsGroundedId);
            float vertical = animator != null ? animator.GetFloat(AnimatorParams.VerticalSpeedId) : 0f;
            bool downed = driver.IsDowned || driver.IsDead;
            float cast = driver.CastHand;

            float yaw = transform.eulerAngles.y;
            float yawRate = Mathf.DeltaAngle(lastYaw, yaw) / dt;
            lastYaw = yaw;
            if (grounded && !wasGrounded)
            {
                landKick = Mathf.Clamp(-vertical * 1.5f, 4f, 18f);
            }

            wasGrounded = grounded;
            landKick = Mathf.MoveTowards(landKick, 0f, 40f * dt);
            hitAmount = Mathf.MoveTowards(hitAmount, 0f, dt / 0.6f);
            ultimate = Mathf.Max(0f, ultimate - dt);
            float ultimateWeight = Mathf.Clamp01(ultimate / 0.4f);

            // ---- tail: a travelling wave along the chain plus inertia from turning
            swayPhase += dt * SecondaryMotionMath.TailSwayFrequency(speed) * 2f * Mathf.PI;
            float amplitude = SecondaryMotionMath.TailSwayAmplitude(speed, downed) * (1f - 0.7f * ultimateWeight);
            float pitchTarget = SecondaryMotionMath.TailPitch(speed, vertical, grounded, downed, ultimateWeight) - landKick;
            if (hips != null && !downed)
            {
                // The tail rides the pelvis: a forward lean (running, sprinting) would swing it up level and make it
                // read as stretched. Take most of the lean back so it keeps hanging behind.
                Vector3 hipsUp = Vector3.ProjectOnPlane(hips.rotation * hipsUpLocal, transform.right);
                float lean = Vector3.SignedAngle(transform.up, hipsUp, transform.right) - restLean; // > 0: leaning forward
                pitchTarget -= Mathf.Clamp(lean, -10f, 35f) * leanCompensation;
            }
            float turn = Mathf.Clamp(-yawRate * turnLag, -maxTailYaw, maxTailYaw);
            for (int i = 0; i < tailBones.Length; i++)
            {
                float k = (i + 1f) / tailBones.Length;
                float wave = amplitude * Mathf.Sin(swayPhase - i * 0.9f) * (0.5f + 0.5f * k);
                float yawTarget = Mathf.Clamp(wave + turn * (0.6f + 0.4f * k), -maxTailYaw, maxTailYaw);
                float pitchShare = i == 0 ? 0.55f : 0.45f / Mathf.Max(1, tailBones.Length - 1);
                ref Bone b = ref tailBones[i];
                b.Yaw.Step(yawTarget, dt, tailFrequency * (1.2f - 0.3f * k), tailDamping);
                b.Pitch.Step(pitchTarget * pitchShare * (ultimateWeight > 0f ? 1.6f : 1f), dt, tailFrequency, tailDamping);
                Apply(ref b);
            }

            // ---- ears: twitch now and then, react to state
            twitchTimer -= dt;
            if (twitchTimer <= 0f)
            {
                twitchTimer = Random.Range(2.2f, 5.5f);
                if (Random.value < 0.5f) twitchLeft = 1f; else twitchRight = 1f;
            }

            twitchLeft = Mathf.MoveTowards(twitchLeft, 0f, dt * 6f);
            twitchRight = Mathf.MoveTowards(twitchRight, 0f, dt * 6f);
            float earPitch = SecondaryMotionMath.EarPitch(speed, downed, hitAmount, cast);
            float headTurn = Mathf.Clamp(yawRate * 0.02f, -10f, 10f);
            StepEar(ref earL, earPitch - 14f * twitchLeft, 6f * cast + headTurn, dt);
            StepEar(ref earR, earPitch - 14f * twitchRight, -6f * cast + headTurn, dt);
        }

        void StepEar(ref Bone b, float pitch, float yaw, float dt)
        {
            if (b.Transform == null)
            {
                return;
            }

            b.Pitch.Step(pitch, dt, earFrequency, earDamping);
            b.Yaw.Step(yaw, dt, earFrequency, earDamping);
            Apply(ref b);
        }

        static void Apply(ref Bone b)
        {
            if (b.Transform == null)
            {
                return;
            }

            // +pitch about character right lifts a backward-pointing tail and tips upward-pointing ears forward
            b.Transform.localRotation = b.Rest * Quaternion.AngleAxis(b.Yaw.Value, b.UpAxis) * Quaternion.AngleAxis(b.Pitch.Value, b.RightAxis);
        }
    }
}
