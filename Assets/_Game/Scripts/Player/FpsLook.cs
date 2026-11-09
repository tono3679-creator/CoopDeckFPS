using CoopDeck.Core;
using UnityEngine;

namespace CoopDeck.Player
{
    /// <summary>
    /// First-person view: yaw turns the body, pitch tilts the camera pivot. Weapon recoil adds a
    /// kick that mostly springs back on its own (so sustained fire climbs a little, but the view
    /// never drifts far), plus a sprint FOV boost. No camera shake on movement.
    /// </summary>
    public class FpsLook : MonoBehaviour, IAimProvider
    {
        [SerializeField] Transform yawRoot;
        [SerializeField] Transform pitchPivot;
        [SerializeField] Camera viewCamera;
        [SerializeField] float minPitch = -85f;
        [SerializeField] float maxPitch = 85f;

        [Header("Recoil")]
        [Tooltip("How fast the recoverable part of the kick returns (1/s).")]
        [SerializeField, Min(0f)] float recoilReturnSpeed = 10f;
        [Tooltip("Share of each kick that springs back automatically. The rest stays as aim climb.")]
        [SerializeField, Range(0f, 1f)] float recoilRecoverableShare = 0.7f;

        [Header("Field of view")]
        [SerializeField, Range(40f, 110f)] float baseFieldOfView = 68f;
        [SerializeField, Range(0f, 20f)] float sprintFieldOfViewBoost = 5f;
        [SerializeField, Min(0f)] float fieldOfViewSmoothing = 8f;

        float yaw;
        float pitch;
        Vector2 recoilOffset;
        float weaponRecoilReturnSpeed;
        float sprintBlend;

        public float Yaw => yaw;
        public float Pitch => pitch;
        public Transform PitchPivot => pitchPivot;
        public Camera ViewCamera => viewCamera;
        public Vector2 LastLookDelta { get; private set; }

        public float BaseFieldOfView
        {
            get => baseFieldOfView;
            set => baseFieldOfView = Mathf.Clamp(value, 40f, 110f);
        }

        void Awake()
        {
            if (yawRoot == null)
            {
                yawRoot = transform;
            }

            if (pitchPivot == null)
            {
                pitchPivot = transform;
            }

            yaw = yawRoot.eulerAngles.y;
        }

        public void Configure(Transform yawTransform, Transform pitchTransform, Camera cameraForFov)
        {
            yawRoot = yawTransform;
            pitchPivot = pitchTransform;
            viewCamera = cameraForFov;
            yaw = yawRoot.eulerAngles.y;
        }

        public void ApplyLook(Vector2 deltaDegrees)
        {
            LastLookDelta = deltaDegrees;
            yaw = Mathf.Repeat(yaw + deltaDegrees.x, 360f);
            pitch = Mathf.Clamp(pitch + deltaDegrees.y, minPitch, maxPitch);
        }

        public void SetRotation(float newYaw, float newPitch)
        {
            yaw = newYaw;
            pitch = Mathf.Clamp(newPitch, minPitch, maxPitch);
            recoilOffset = Vector2.zero;
            Apply();
        }

        public void AddRecoil(Vector2 kickDegrees)
        {
            float permanent = 1f - recoilRecoverableShare;
            yaw = Mathf.Repeat(yaw + kickDegrees.x * permanent, 360f);
            pitch = Mathf.Clamp(pitch + kickDegrees.y * permanent, minPitch, maxPitch);
            recoilOffset += kickDegrees * recoilRecoverableShare;
        }

        /// <summary>The held weapon's recovery time (95 % settled after <paramref name="seconds"/>); 0 = this look's default.</summary>
        public void SetRecoilRecovery(float seconds)
        {
            weaponRecoilReturnSpeed = seconds > 0f ? 3f / seconds : 0f;
        }

        /// <summary>Recoil return rate in use (1/s).</summary>
        public float RecoilReturnSpeed => weaponRecoilReturnSpeed > 0f ? weaponRecoilReturnSpeed : recoilReturnSpeed;

        /// <param name="sprinting">Drives the sprint FOV boost.</param>
        public void Tick(float deltaTime, bool sprinting)
        {
            float decay = 1f - Mathf.Exp(-RecoilReturnSpeed * deltaTime);
            recoilOffset = Vector2.Lerp(recoilOffset, Vector2.zero, decay);

            float fovBlend = 1f - Mathf.Exp(-fieldOfViewSmoothing * deltaTime);
            sprintBlend = Mathf.Lerp(sprintBlend, sprinting ? 1f : 0f, fovBlend);
            if (viewCamera != null)
            {
                viewCamera.fieldOfView = baseFieldOfView + sprintFieldOfViewBoost * sprintBlend;
            }

            Apply();
        }

        void Apply()
        {
            float finalPitch = Mathf.Clamp(pitch + recoilOffset.y, minPitch, maxPitch);
            yawRoot.rotation = Quaternion.Euler(0f, yaw, 0f);
            pitchPivot.localRotation = Quaternion.Euler(-finalPitch, recoilOffset.x, 0f);
        }

        public Ray GetAimRay()
        {
            return new Ray(pitchPivot.position, pitchPivot.forward);
        }
    }
}
