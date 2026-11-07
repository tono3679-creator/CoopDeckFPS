using CoopDeck.Core;
using UnityEngine;

namespace CoopDeck.Effects
{
    /// <summary>
    /// Marks an optional layer of an effect (extra sparks, secondary rings, trails) that only
    /// plays at or above a given <see cref="VfxQuality"/>. <see cref="PooledFx"/> switches these on
    /// or off each time the effect plays, so the setting can change during play.
    /// </summary>
    public class VfxQualityGate : MonoBehaviour
    {
        [SerializeField] VfxQuality minimumQuality = VfxQuality.Medium;

        public VfxQuality MinimumQuality
        {
            get => minimumQuality;
            set => minimumQuality = value;
        }

        public bool AllowedNow => PerformanceSettings.Vfx >= minimumQuality;
    }
}
