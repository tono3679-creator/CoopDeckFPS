using UnityEngine;

namespace CoopDeck.Animation
{
    /// <summary>
    /// Lives on the Fox Gunner body's Animator. Bends the spine and head toward the aim direction with the
    /// Humanoid look-at (IK pass of the base layer), on top of whatever clip plays, so the rifle follows the
    /// player's pitch for everyone watching. Hand IK (Animation Rigging) runs after this and keeps both hands on
    /// the rifle, which rides the chest. Look-at fades out while downed or dead and mostly while sprinting.
    ///
    /// The look-at aims the head, not the rifle, so a slow correction (only while both hands hold the rifle
    /// normally) tilts the look target until the muzzle lines up with the aim direction.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    [DisallowMultipleComponent]
    public class FoxBodyAnimator : MonoBehaviour
    {
        [SerializeField] CharacterAnimatorDriver driver;
        [SerializeField, Range(0f, 1f)] float bodyWeight = 0.65f;
        [SerializeField, Range(0f, 1f)] float headWeight = 0.85f;
        [SerializeField, Range(0f, 1f)] float clampWeight = 0.5f;
        [SerializeField, Min(0.1f)] float fadeSpeed = 4f;
        [SerializeField, Range(0f, 1f)] float sprintWeight = 0.2f;
        [SerializeField, Min(0.5f)] float eyeHeight = 1.3f;
        [Tooltip("Degrees per second per degree of muzzle error.")]
        [SerializeField, Min(0f)] float muzzleCorrection = 3f;
        [SerializeField, Range(0f, 45f)] float maxCorrection = 30f;
        [Tooltip("Where cast VFX start (the left hand).")]
        [SerializeField] Transform castOrigin;
        [Tooltip("The rifle's grip frame (Weapon bone).")]
        [SerializeField] Transform weapon;

        Animator animator;
        float weight;
        float pitchBias;
        Quaternion muzzleInWeapon = Quaternion.identity;
        bool hasMuzzle;

        public Transform CastOrigin => castOrigin;
        public float LookWeight => weight;
        public float PitchBias => pitchBias;

        public void Configure(CharacterAnimatorDriver owner, Transform castHand, Transform weaponBone)
        {
            driver = owner;
            castOrigin = castHand;
            weapon = weaponBone;
        }

        void Awake()
        {
            animator = GetComponent<Animator>();
            if (driver == null)
            {
                driver = GetComponentInParent<CharacterAnimatorDriver>();
            }

            if (castOrigin == null && animator.isHuman)
            {
                castOrigin = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            }

            if (weapon != null)
            {
                // bind pose: the Weapon bone is the rifle frame facing the body's forward
                muzzleInWeapon = Quaternion.Inverse(weapon.rotation) * transform.rotation;
                hasMuzzle = true;
            }
        }

        /// <summary>World direction of the muzzle (bind-pose relation to the Weapon bone).</summary>
        public Vector3 MuzzleForward => hasMuzzle ? (weapon.rotation * muzzleInWeapon) * Vector3.forward : transform.forward;

        void OnAnimatorIK(int layerIndex)
        {
            if (layerIndex != 0 || animator == null)
            {
                return;
            }

            bool aim = driver != null && driver.HasAim && !driver.IsDowned && !driver.IsDead;
            // sprinting carries the rifle low across the body (no shooting), so the spine follows the aim only a little
            float goal = !aim ? 0f : driver.IsSprinting ? sprintWeight : 1f;
            weight = Mathf.MoveTowards(weight, goal, fadeSpeed * Time.deltaTime);
            if (weight <= 0.001f)
            {
                animator.SetLookAtWeight(0f);
                return;
            }

            Vector3 eye = transform.position + transform.up * eyeHeight;
            Vector3 dir = Quaternion.AngleAxis(-pitchBias, transform.right) * driver.AimDirection;
            animator.SetLookAtPosition(eye + dir * 10f);
            animator.SetLookAtWeight(weight, bodyWeight, headWeight, 0f, clampWeight);
        }

        void LateUpdate()
        {
            if (!hasMuzzle || driver == null || weight < 0.9f || driver.IsSprinting || muzzleCorrection <= 0f)
            {
                return;
            }

            // only while the rifle is in the plain two-handed hold (not reloading, casting or staggering)
            if (driver.LeftHandOnRifle < 0.99f || driver.CastHand > 0.01f)
            {
                return;
            }

            Vector3 right = transform.right;
            Vector3 muzzle = Vector3.ProjectOnPlane(MuzzleForward, right);
            Vector3 aimDir = Vector3.ProjectOnPlane(driver.AimDirection, right);
            if (muzzle.sqrMagnitude < 1e-4f || aimDir.sqrMagnitude < 1e-4f)
            {
                return;
            }

            float error = Vector3.SignedAngle(muzzle, aimDir, right); // < 0: the muzzle is below the aim
            pitchBias = Mathf.Clamp(pitchBias - error * muzzleCorrection * Time.deltaTime, -maxCorrection, maxCorrection);
        }
    }
}
