using CoopDeck.Core;
using UnityEngine;

namespace CoopDeck.Effects
{
    /// <summary>Short light pulse for muzzle flashes and explosions. Disabled on the Handheld tier.</summary>
    [RequireComponent(typeof(Light))]
    public class LightFlash : MonoBehaviour
    {
        [SerializeField, Min(0.01f)] float duration = 0.05f;
        [SerializeField, Min(0f)] float peakIntensity = 4f;

        Light flashLight;
        float elapsed;

        void Awake()
        {
            flashLight = GetComponent<Light>();
        }

        void OnEnable()
        {
            elapsed = 0f;
            flashLight.enabled = PerformanceSettings.EffectLightsEnabled;
            flashLight.intensity = peakIntensity;
        }

        void Update()
        {
            if (!flashLight.enabled)
            {
                return;
            }

            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            if (t >= 1f)
            {
                flashLight.enabled = false;
                return;
            }

            flashLight.intensity = peakIntensity * (1f - t) * (1f - t);
        }
    }
}
