using System.Collections.Generic;
using CoopDeck.Core;
using UnityEngine;

namespace CoopDeck.Effects
{
    /// <summary>
    /// Short, small camera shake for big impacts (combos). Put on the view camera, below the
    /// pitch pivot: it only offsets the camera's own local pose, so the aim ray (from the pivot)
    /// is never moved. Strength falls off with distance and is capped, and is scaled by
    /// <see cref="PerformanceSettings.ScreenShakeScale"/> and the effect quality.
    /// </summary>
    public class CameraShaker : MonoBehaviour
    {
        static readonly List<CameraShaker> Active = new List<CameraShaker>();

        [Tooltip("Maximum rotation at full trauma, degrees.")]
        [SerializeField, Min(0f)] float maxAngle = 0.9f;
        [Tooltip("Maximum offset at full trauma, metres.")]
        [SerializeField, Min(0f)] float maxOffset = 0.035f;
        [Tooltip("Trauma lost per second (1 = a full shake lasts one second).")]
        [SerializeField, Min(0.1f)] float decay = 3.2f;
        [SerializeField, Min(1f)] float frequency = 22f;
        [Tooltip("Beyond this distance an impact does not shake the camera.")]
        [SerializeField, Min(1f)] float falloffDistance = 30f;

        [Tooltip("Seconds of a field-of-view punch (co-op combos): quick widen, ease back.")]
        [SerializeField, Min(0.05f)] float punchDuration = 0.32f;

        [Tooltip("Seconds of a kick (this camera's own jolt, e.g. a cast leaving the hand): snaps in, eases back.")]
        [SerializeField, Min(0.05f)] float kickDuration = 0.22f;

        Vector3 basePosition;
        Quaternion baseRotation;
        float trauma;
        float seed;
        Camera viewCamera;
        float punchStrength;
        float punchAge = float.PositiveInfinity;
        float lastWrittenFov = -1f;
        float lastPunchOffset;
        Vector3 kickEuler;
        Vector3 kickOffset;
        float kickAge = float.PositiveInfinity;
        bool posed;

        public float Trauma => trauma;

        /// <summary>Current field-of-view offset from a punch, degrees (0 when idle).</summary>
        public float PunchOffset { get; private set; }

        /// <summary>Current share (0..1) of the last kick being applied (0 when idle).</summary>
        public float KickAmount { get; private set; }

        /// <summary>
        /// A short directional jolt of this camera only (the caster's own view as a cast leaves the hand), unlike
        /// <see cref="Shake"/> which reaches every camera near an impact. Euler in degrees (x &lt; 0 tips the view up),
        /// offset in metres in the camera's space. Scaled like the shake; the aim ray is unaffected.
        /// </summary>
        public void Kick(Vector3 eulerDegrees, Vector3 localOffset)
        {
            float scale = PerformanceSettings.ScreenShakeScale * PerformanceSettings.ShakeFor(PerformanceSettings.Vfx);
            if (scale <= 0f)
            {
                return;
            }

            kickEuler = eulerDegrees * scale;
            kickOffset = localOffset * scale;
            kickAge = 0f;
        }

        /// <summary>
        /// A short field-of-view kick for a big moment (co-op combo), falling off with distance and
        /// scaled like the shake (none with -noshake, half at low effect quality). The aim ray is
        /// unaffected; the base FOV set by the look code each frame is preserved.
        /// </summary>
        public static void Punch(Vector3 source, float degrees)
        {
            float scale = PerformanceSettings.ScreenShakeScale * PerformanceSettings.ShakeFor(PerformanceSettings.Vfx);
            if (degrees <= 0f || scale <= 0f)
            {
                return;
            }

            foreach (CameraShaker shaker in Active)
            {
                float distance = Vector3.Distance(source, shaker.transform.position);
                float attenuation = 1f - Mathf.Clamp01(distance / shaker.falloffDistance);
                float strength = degrees * scale * Mathf.Max(0.35f, attenuation);
                if (strength > shaker.punchStrength * (1f - Mathf.Clamp01(shaker.punchAge / shaker.punchDuration)))
                {
                    shaker.punchStrength = strength;
                    shaker.punchAge = 0f;
                }
            }
        }

        /// <summary>Asks every active camera to shake for an impact at <paramref name="source"/> (strength 0..1).</summary>
        public static void Shake(Vector3 source, float strength)
        {
            float scale = PerformanceSettings.ScreenShakeScale * PerformanceSettings.ShakeFor(PerformanceSettings.Vfx);
            if (strength <= 0f || scale <= 0f)
            {
                return;
            }

            foreach (CameraShaker shaker in Active)
            {
                shaker.AddImpact(source, strength * scale);
            }
        }

        void Awake()
        {
            basePosition = transform.localPosition;
            baseRotation = transform.localRotation;
            seed = Random.value * 100f;
            viewCamera = GetComponent<Camera>();
        }

        void OnEnable()
        {
            Active.Add(this);
        }

        void OnDisable()
        {
            Active.Remove(this);
            trauma = 0f;
            kickAge = float.PositiveInfinity;
            KickAmount = 0f;
            posed = false;
            transform.localPosition = basePosition;
            transform.localRotation = baseRotation;
        }

        public void AddImpact(Vector3 source, float strength)
        {
            float distance = Vector3.Distance(source, transform.position);
            float attenuation = 1f - Mathf.Clamp01(distance / falloffDistance);
            trauma = Mathf.Clamp01(trauma + strength * attenuation * attenuation);
        }

        void LateUpdate()
        {
            ApplyPunch();
            float kick = KickShape();
            if (trauma <= 0f && kick <= 0f)
            {
                if (posed)
                {
                    transform.localPosition = basePosition;
                    transform.localRotation = baseRotation;
                    posed = false;
                }

                return;
            }

            Vector3 offset = Vector3.zero;
            Quaternion shake = Quaternion.identity;
            if (trauma > 0f)
            {
                trauma = Mathf.Max(0f, trauma - decay * Time.deltaTime);
                float amount = trauma * trauma; // squared: small impacts barely move the view
                float t = Time.time * frequency;
                float pitch = (Mathf.PerlinNoise(seed, t) * 2f - 1f) * maxAngle * amount;
                float yaw = (Mathf.PerlinNoise(seed + 10f, t) * 2f - 1f) * maxAngle * amount;
                float roll = (Mathf.PerlinNoise(seed + 20f, t) * 2f - 1f) * maxAngle * 0.5f * amount;
                offset = new Vector3(Mathf.PerlinNoise(seed + 30f, t) * 2f - 1f, Mathf.PerlinNoise(seed + 40f, t) * 2f - 1f, 0f) * (maxOffset * amount);
                shake = Quaternion.Euler(pitch, yaw, roll);
            }

            transform.localPosition = basePosition + offset + kickOffset * kick;
            transform.localRotation = baseRotation * shake * Quaternion.Euler(kickEuler * kick);
            posed = true;
        }

        /// <summary>Share of the kick this frame: in within the first 15 % of its time, then eased back to 0.</summary>
        float KickShape()
        {
            KickAmount = 0f;
            if (kickAge >= kickDuration)
            {
                return 0f;
            }

            kickAge += Time.deltaTime;
            float t = Mathf.Clamp01(kickAge / kickDuration);
            KickAmount = t < 0.15f ? Mathf.SmoothStep(0f, 1f, t / 0.15f) : 1f - Mathf.SmoothStep(0f, 1f, (t - 0.15f) / 0.85f);
            return KickAmount;
        }

        void ApplyPunch()
        {
            if (viewCamera == null)
            {
                return;
            }

            float offset = 0f;
            if (punchAge < punchDuration)
            {
                punchAge += Time.deltaTime;
                float t = Mathf.Clamp01(punchAge / punchDuration);
                // Fast attack (first 20 %), smooth release.
                float shape = t < 0.2f ? t / 0.2f : 1f - Mathf.SmoothStep(0f, 1f, (t - 0.2f) / 0.8f);
                offset = punchStrength * shape;
            }

            if (Mathf.Approximately(offset, 0f) && Mathf.Approximately(lastPunchOffset, 0f))
            {
                PunchOffset = 0f;
                return;
            }

            // The look code writes the base FOV each frame; if it did not this frame, undo our last offset first.
            float current = viewCamera.fieldOfView;
            float baseFov = Mathf.Approximately(current, lastWrittenFov) ? current - lastPunchOffset : current;
            lastWrittenFov = baseFov + offset;
            lastPunchOffset = offset;
            PunchOffset = offset;
            viewCamera.fieldOfView = lastWrittenFov;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Active.Clear();
        }
    }
}
