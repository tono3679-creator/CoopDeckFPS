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

        Vector3 basePosition;
        Quaternion baseRotation;
        float trauma;
        float seed;

        public float Trauma => trauma;

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
        }

        void OnEnable()
        {
            Active.Add(this);
        }

        void OnDisable()
        {
            Active.Remove(this);
            trauma = 0f;
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
            if (trauma <= 0f)
            {
                return;
            }

            trauma = Mathf.Max(0f, trauma - decay * Time.deltaTime);
            float amount = trauma * trauma; // squared: small impacts barely move the view
            float t = Time.time * frequency;
            float pitch = (Mathf.PerlinNoise(seed, t) * 2f - 1f) * maxAngle * amount;
            float yaw = (Mathf.PerlinNoise(seed + 10f, t) * 2f - 1f) * maxAngle * amount;
            float roll = (Mathf.PerlinNoise(seed + 20f, t) * 2f - 1f) * maxAngle * 0.5f * amount;
            Vector3 offset = new Vector3(Mathf.PerlinNoise(seed + 30f, t) * 2f - 1f, Mathf.PerlinNoise(seed + 40f, t) * 2f - 1f, 0f) * (maxOffset * amount);

            transform.localPosition = basePosition + offset;
            transform.localRotation = baseRotation * Quaternion.Euler(pitch, yaw, roll);
            if (trauma <= 0f)
            {
                transform.localPosition = basePosition;
                transform.localRotation = baseRotation;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Active.Clear();
        }
    }
}
