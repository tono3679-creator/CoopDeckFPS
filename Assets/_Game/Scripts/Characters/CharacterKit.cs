using CoopDeck.Animation;
using UnityEngine;

namespace CoopDeck.Characters
{
    /// <summary>
    /// A character's visual parts for the player, built by the editor (CharacterPrefabBuilder.BuildCharacterKit) and
    /// put into the player by <see cref="PlayerCharacterSwap"/>:
    ///   Body       the full-body model (Animator, hand IK, look-at, secondary motion) with its held weapon on the
    ///              Weapon bone; others see it, the local player only its shadow
    ///   ViewModel  the first-person weapon and arms, laid out relative to the player's view-model root
    /// plus what the player's animator driver, weapon and caster need to know about them.
    /// </summary>
    public class CharacterKit : MonoBehaviour
    {
        [SerializeField] GameObject body;
        [SerializeField] Animator bodyAnimator;
        [SerializeField] Transform viewModel;
        [SerializeField] Transform muzzle;
        [SerializeField] FirstPersonArms arms;

        [Header("Animator driver")]
        [SerializeField, Min(0.1f)] float clipStrideSpeed = 4f;
        [SerializeField] LocomotionRing[] rings = new LocomotionRing[0];
        [SerializeField, Min(1f)] float maxBlendSpeed = 9.5f;
        [SerializeField, Min(0.1f)] float reloadClipSeconds = 1.7f;

        [Header("First-person shoulders (relative to the eyes)")]
        [SerializeField] Vector3 leftShoulderFromEye;
        [SerializeField] Vector3 rightShoulderFromEye;
        [SerializeField] float torsoYaw = 40f;
        [SerializeField] float torsoLean = 33f;

        public GameObject Body => body;
        public Animator BodyAnimator => bodyAnimator;
        public Transform ViewModel => viewModel;
        public Transform Muzzle => muzzle;
        public FirstPersonArms Arms => arms;
        public float ClipStrideSpeed => clipStrideSpeed;
        public LocomotionRing[] Rings => rings;
        public float MaxBlendSpeed => maxBlendSpeed;
        public float ReloadClipSeconds => reloadClipSeconds;
        public Vector3 LeftShoulderFromEye => leftShoulderFromEye;
        public Vector3 RightShoulderFromEye => rightShoulderFromEye;
        public float TorsoYaw => torsoYaw;
        public float TorsoLean => torsoLean;

        public void Configure(GameObject bodyRoot, Animator animator, Transform viewModelRoot, Transform viewMuzzle,
            FirstPersonArms firstPersonArms)
        {
            body = bodyRoot;
            bodyAnimator = animator;
            viewModel = viewModelRoot;
            muzzle = viewMuzzle;
            arms = firstPersonArms;
        }

        public void ConfigureDriver(float stride, LocomotionRing[] speedRings, float maxSpeed, float reloadSeconds)
        {
            clipStrideSpeed = stride;
            rings = speedRings;
            maxBlendSpeed = maxSpeed;
            reloadClipSeconds = reloadSeconds;
        }

        public void ConfigureShoulders(Vector3 left, Vector3 right, float yaw, float lean)
        {
            leftShoulderFromEye = left;
            rightShoulderFromEye = right;
            torsoYaw = yaw;
            torsoLean = lean;
        }
    }
}
