using CoopDeck.Weapons;
using UnityEngine;

namespace CoopDeck.Player
{
    /// <summary>
    /// Procedural first-person weapon motion (placeholder until real arm animations exist):
    /// walk bob, look sway, firing kick, reload dip, sprint tilt and landing dip.
    /// Only the view model moves; the camera itself never bobs.
    /// </summary>
    public class ViewModelAnimator : MonoBehaviour
    {
        [SerializeField] FpsCharacter character;
        [SerializeField] Transform viewModelRoot;

        [Header("Bob")]
        [SerializeField] float bobFrequency = 1.9f;
        [SerializeField] Vector2 bobAmplitude = new Vector2(0.012f, 0.009f);

        [Header("Sway")]
        [SerializeField] float swayPerDegree = 0.0016f;
        [SerializeField] float swayMax = 0.035f;
        [SerializeField] float swaySmoothing = 12f;

        [Header("Kick")]
        [SerializeField] float kickBack = 0.045f;
        [SerializeField] float kickPitch = 2.4f;
        [SerializeField] float kickReturn = 18f;

        [Header("Poses")]
        [SerializeField] Vector3 sprintOffset = new Vector3(0.04f, -0.05f, -0.03f);
        [SerializeField] Vector3 sprintEuler = new Vector3(8f, -22f, 12f);
        [SerializeField] Vector3 reloadOffset = new Vector3(0f, -0.12f, 0f);
        [SerializeField] Vector3 reloadEuler = new Vector3(28f, 0f, -18f);
        [SerializeField] float poseSmoothing = 10f;

        Vector3 restPosition;
        Quaternion restRotation;
        float bobPhase;
        Vector2 sway;
        float kick;
        float landingDip;
        float sprintBlend;
        HitscanWeapon weapon;

        void Awake()
        {
            if (character == null)
            {
                character = GetComponentInParent<FpsCharacter>();
            }

            if (viewModelRoot == null)
            {
                viewModelRoot = transform;
            }

            restPosition = viewModelRoot.localPosition;
            restRotation = viewModelRoot.localRotation;
        }

        public void Configure(FpsCharacter owner, Transform root)
        {
            character = owner;
            viewModelRoot = root;
            restPosition = root.localPosition;
            restRotation = root.localRotation;
        }

        bool subscribed;
        FpsMotor subscribedMotor;

        // Subscribing in Start as well as OnEnable makes this independent of component init order.
        void OnEnable() => Subscribe();
        void Start() => Subscribe();

        void Subscribe()
        {
            if (subscribed || character == null)
            {
                return;
            }

            weapon = character.Weapon;
            subscribedMotor = character.Motor;
            if (weapon != null)
            {
                weapon.Fired += OnFired;
            }

            if (subscribedMotor != null)
            {
                subscribedMotor.Landed += OnLanded;
            }

            subscribed = weapon != null || subscribedMotor != null;
        }

        void OnDisable()
        {
            if (!subscribed)
            {
                return;
            }

            if (weapon != null)
            {
                weapon.Fired -= OnFired;
            }

            if (subscribedMotor != null)
            {
                subscribedMotor.Landed -= OnLanded;
            }

            subscribed = false;
        }

        void OnFired(ShotEvent shot)
        {
            kick = Mathf.Min(kick + 1f, 2.5f);
        }

        void OnLanded(float impactSpeed)
        {
            landingDip = Mathf.Clamp(impactSpeed * 0.006f, 0f, 0.06f);
        }

        void LateUpdate()
        {
            if (character == null || viewModelRoot == null)
            {
                return;
            }

            float dt = Time.deltaTime;
            FpsMotor motor = character.Motor;
            bool grounded = motor == null || motor.IsGrounded;
            float speed = motor != null ? Mathf.Clamp01(motor.SpeedFraction) : 0f;

            // Bob only while walking on the ground.
            if (grounded && speed > 0.05f)
            {
                bobPhase += dt * bobFrequency * Mathf.PI * 2f * Mathf.Lerp(0.6f, 1.25f, speed);
            }

            float bobWeight = grounded ? speed : 0f;
            Vector3 bob = new Vector3(Mathf.Cos(bobPhase) * bobAmplitude.x, Mathf.Abs(Mathf.Sin(bobPhase)) * bobAmplitude.y, 0f) * bobWeight;

            // Sway lags behind view rotation.
            Vector2 look = character.Look != null ? character.Look.LastLookDelta : Vector2.zero;
            Vector2 targetSway = Vector2.ClampMagnitude(-look * swayPerDegree, swayMax);
            sway = Vector2.Lerp(sway, targetSway, 1f - Mathf.Exp(-swaySmoothing * dt));

            kick = Mathf.Lerp(kick, 0f, 1f - Mathf.Exp(-kickReturn * dt));
            landingDip = Mathf.Lerp(landingDip, 0f, 1f - Mathf.Exp(-8f * dt));

            float poseBlend = 1f - Mathf.Exp(-poseSmoothing * dt);
            sprintBlend = Mathf.Lerp(sprintBlend, motor != null && motor.IsSprinting ? 1f : 0f, poseBlend);
            float reloadWeight = ReloadWeight();

            Vector3 position = restPosition + bob + new Vector3(sway.x, sway.y - landingDip, -kick * kickBack)
                               + sprintOffset * sprintBlend + reloadOffset * reloadWeight;
            Quaternion rotation = restRotation
                                  * Quaternion.Euler(sprintEuler * sprintBlend)
                                  * Quaternion.Euler(reloadEuler * reloadWeight)
                                  * Quaternion.Euler(-kick * kickPitch, 0f, 0f);

            viewModelRoot.localPosition = position;
            viewModelRoot.localRotation = rotation;
        }

        float ReloadWeight()
        {
            if (weapon == null || !weapon.IsReloading)
            {
                return 0f;
            }

            // Down quickly, hold, then come back up before the reload completes.
            float p = weapon.State.ReloadProgress;
            if (p < 0.2f) return Mathf.SmoothStep(0f, 1f, p / 0.2f);
            if (p > 0.8f) return Mathf.SmoothStep(1f, 0f, (p - 0.8f) / 0.2f);
            return 1f;
        }
    }
}
