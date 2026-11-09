using System;
using UnityEngine;

namespace CoopDeck.Animation
{
    /// <summary>
    /// Procedural ears and cloth tails made of bone chains (the clips never key them), e.g. the Lightning Rabbit's
    /// three-joint ears and its two scarf tails. Each joint bends from its rest shape with one angle spring per axis;
    /// joints further out answer more slowly, so motion travels down the chain.
    ///   ears   twitch now and then, tip back with speed and acceleration, flatten on a hit, perk up while casting,
    ///          droop when downed, and lag behind turns
    ///   cloth  hangs a little lower than its sculpted shape, trails back with speed and while falling, swings out on
    ///          turns, flutters while moving and settles at rest
    /// Runs after the Animator and Animation Rigging (LateUpdate) and sleeps while the body is not rendered.
    /// </summary>
    [DisallowMultipleComponent]
    public class ChainSecondaryMotion : MonoBehaviour
    {
        public enum ChainKind
        {
            Ear,
            Cloth,
        }

        [Serializable]
        public class Chain
        {
            public string name;
            public ChainKind kind;
            [Tooltip("Root first.")]
            public Transform[] bones = new Transform[0];
            [Tooltip("-1 on the character's left, +1 on its right (mirrors yaw).")]
            public float side = 1f;
        }

        [SerializeField] CharacterAnimatorDriver driver;
        [SerializeField] Renderer visibilityRenderer;
        [SerializeField] Chain[] chains = new Chain[0];

        [Header("Ears")]
        [SerializeField] float earFrequency = 4.2f;
        [SerializeField, Range(0.1f, 1.5f)] float earDamping = 0.42f;
        [Tooltip("Degrees the ears tip back per m/s² of forward acceleration.")]
        [SerializeField] float earInertia = 1.2f;

        [Header("Cloth")]
        [SerializeField] float clothFrequency = 2.0f;
        [SerializeField, Range(0.1f, 1.5f)] float clothDamping = 0.32f;
        [Tooltip("Share of the way from the sculpted shape toward hanging straight down, at rest.")]
        [SerializeField, Range(0f, 1f)] float clothHang = 0.45f;
        [Tooltip("Degrees the cloth trails back per m/s of ground speed.")]
        [SerializeField] float clothTrail = 2.4f;
        [SerializeField] float maxClothTrail = 38f;
        [SerializeField] float clothFlutter = 5f;

        struct Joint
        {
            public Transform Transform;
            public Quaternion Rest;
            public Quaternion Hang;    // rest-pose rotation toward hanging (cloth root only)
            public Vector3 UpAxis;     // character up in bone-local space (yaw)
            public Vector3 RightAxis;  // character right in bone-local space (pitch)
            public AngleSpring Yaw;
            public AngleSpring Pitch;
        }

        Joint[][] joints = new Joint[0][];
        bool initialized;
        float lastYaw;
        Vector3 lastVelocity;
        Vector3 lastPosition;
        float hitAmount;
        float twitchTimer = 2f;
        float[] twitch = new float[0];
        float flutterPhase;

        public void Configure(CharacterAnimatorDriver owner, Renderer visible, Chain[] boneChains)
        {
            driver = owner;
            visibilityRenderer = visible;
            chains = boneChains;
            initialized = false;
        }

        public Chain[] Chains => chains;

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
                driver.Hurt += OnHurt;
            }
        }

        void OnDisable()
        {
            if (driver != null)
            {
                driver.Hurt -= OnHurt;
            }
        }

        void OnHurt(bool heavy) => hitAmount = heavy ? 1f : Mathf.Max(hitAmount, 0.6f);

        void Start()
        {
            if (!initialized)
            {
                Initialize();
            }
        }

        /// <summary>Records rest rotations and character axes. Call while the body is in its bind pose.</summary>
        void Initialize()
        {
            Transform root = transform;
            joints = new Joint[chains.Length][];
            twitch = new float[chains.Length];
            for (int c = 0; c < chains.Length; c++)
            {
                Transform[] bones = chains[c].bones ?? new Transform[0];
                joints[c] = new Joint[bones.Length];
                for (int i = 0; i < bones.Length; i++)
                {
                    Transform t = bones[i];
                    var j = new Joint { Transform = t, Hang = Quaternion.identity };
                    if (t != null)
                    {
                        j.Rest = t.localRotation;
                        Quaternion inv = Quaternion.Inverse(t.rotation);
                        j.UpAxis = (inv * root.up).normalized;
                        j.RightAxis = (inv * root.right).normalized;
                    }

                    joints[c][i] = j;
                }

                if (chains[c].kind == ChainKind.Cloth && bones.Length > 1 && bones[0] != null && bones[bones.Length - 1] != null)
                {
                    // the whole tail turns about its root toward hanging straight down
                    Transform first = bones[0], last = bones[bones.Length - 1];
                    Vector3 tip = last.childCount > 0 ? last.GetChild(0).position : last.position + (last.position - bones[bones.Length - 2].position);
                    Vector3 along = tip - first.position;
                    Vector3 hung = Vector3.Slerp(along.normalized, -root.up, clothHang) * along.magnitude;
                    Quaternion world = Quaternion.FromToRotation(along, hung);
                    joints[c][0].Hang = Quaternion.Inverse(first.rotation) * world * first.rotation;
                }
            }

            lastYaw = root.eulerAngles.y;
            lastPosition = root.position;
            initialized = true;
        }

        void LateUpdate()
        {
            if (!initialized || driver == null)
            {
                return;
            }

            float dt = Time.deltaTime;
            Animator animator = driver.Animator;
            bool alwaysAnimate = animator != null && animator.cullingMode == AnimatorCullingMode.AlwaysAnimate;
            if (dt <= 0f || (!alwaysAnimate && visibilityRenderer != null && !visibilityRenderer.isVisible))
            {
                return;
            }

            Transform root = transform;
            float speed = animator != null ? animator.GetFloat(AnimatorParams.SpeedId) : 0f;
            bool grounded = animator == null || animator.GetBool(AnimatorParams.IsGroundedId);
            float vertical = animator != null ? animator.GetFloat(AnimatorParams.VerticalSpeedId) : 0f;
            bool downed = driver.IsDowned || driver.IsDead;
            float cast = driver.CastHand;

            float yaw = root.eulerAngles.y;
            float yawRate = Mathf.DeltaAngle(lastYaw, yaw) / dt;
            lastYaw = yaw;
            Vector3 velocity = (root.position - lastPosition) / dt;
            lastPosition = root.position;
            float forwardAccel = Vector3.Dot(velocity - lastVelocity, root.forward) / dt;
            lastVelocity = velocity;
            hitAmount = Mathf.MoveTowards(hitAmount, 0f, dt / 0.6f);
            flutterPhase += dt * (2.5f + 0.35f * speed) * 2f * Mathf.PI;

            twitchTimer -= dt;
            if (twitchTimer <= 0f && twitch.Length > 0)
            {
                twitchTimer = UnityEngine.Random.Range(2f, 5f);
                int pick = UnityEngine.Random.Range(0, twitch.Length);
                if (chains[pick].kind == ChainKind.Ear)
                {
                    twitch[pick] = 1f;
                }
            }

            for (int c = 0; c < chains.Length; c++)
            {
                twitch[c] = Mathf.MoveTowards(twitch[c], 0f, dt * 5f);
                if (chains[c].kind == ChainKind.Ear)
                {
                    StepEar(c, speed, downed, cast, forwardAccel, yawRate, dt);
                }
                else
                {
                    StepCloth(c, speed, vertical, grounded, downed, yawRate, dt);
                }
            }
        }

        void StepEar(int c, float speed, bool downed, float cast, float forwardAccel, float yawRate, float dt)
        {
            Joint[] chain = joints[c];
            float side = chains[c].side;
            float pitch = SecondaryMotionMath.EarPitch(speed, downed, hitAmount, cast)
                          - Mathf.Clamp(forwardAccel * earInertia, -12f, 18f)
                          - 14f * twitch[c];
            float turn = Mathf.Clamp(-yawRate * 0.03f, -14f, 14f);
            for (int i = 0; i < chain.Length; i++)
            {
                float k = (i + 1f) / chain.Length;              // 0..1 along the ear
                float share = chain.Length > 1 ? Mathf.Lerp(0.45f, 0.25f, i / (chain.Length - 1f)) : 1f;
                float freq = earFrequency * (1.15f - 0.35f * k);
                ref Joint j = ref chain[i];
                j.Pitch.Step(pitch * share, dt, freq, earDamping);
                j.Yaw.Step((turn * (0.4f + 0.6f * k) + 5f * cast * side) * share, dt, freq, earDamping);
                Apply(ref j);
            }
        }

        void StepCloth(int c, float speed, float vertical, bool grounded, bool downed, float yawRate, float dt)
        {
            Joint[] chain = joints[c];
            float trail = downed ? 0f : Mathf.Min(maxClothTrail, speed * clothTrail);
            if (!grounded)
            {
                trail += Mathf.Clamp(-vertical * 3f, -8f, 24f); // rises while falling, drops a little on the way up
            }

            float turn = Mathf.Clamp(-yawRate * 0.05f, -30f, 30f);
            float flutterAmp = clothFlutter * Mathf.Clamp01(speed / 6f);
            for (int i = 0; i < chain.Length; i++)
            {
                float k = (i + 1f) / chain.Length;
                float share = 1f / chain.Length;
                float freq = clothFrequency * (1.2f - 0.4f * k);
                float flutter = flutterAmp * Mathf.Sin(flutterPhase - i * 1.1f) * (0.4f + 0.6f * k);
                ref Joint j = ref chain[i];
                j.Pitch.Step(trail * share * (1f + 0.5f * k) + flutter, dt, freq, clothDamping);
                j.Yaw.Step(turn * share * (0.6f + 0.8f * k) + 0.5f * flutter, dt, freq, clothDamping);
                Apply(ref j);
            }
        }

        static void Apply(ref Joint j)
        {
            if (j.Transform == null)
            {
                return;
            }

            // +pitch about character right swings a hanging tail back (and tips an upright ear forward)
            j.Transform.localRotation = j.Rest * j.Hang * Quaternion.AngleAxis(j.Yaw.Value, j.UpAxis) * Quaternion.AngleAxis(j.Pitch.Value, j.RightAxis);
        }
    }
}
