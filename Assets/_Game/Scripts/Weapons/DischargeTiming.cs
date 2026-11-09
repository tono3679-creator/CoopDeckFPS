using System;
using UnityEngine;

namespace CoopDeck.Weapons
{
    /// <summary>
    /// When a lightning discharge shows each beat on screen (Docs/LIGHTNING_VFX.md): the first strike in the discharge
    /// frame, then each chained hop's arc after a short pause, its tip reaching the next enemy <see cref="chainTravel"/>
    /// later (the second burst plays on arrival). Display only: the damage, the targets and the element hits are all
    /// applied in the discharge frame by <see cref="HitscanWeapon"/>.
    /// </summary>
    [Serializable]
    public struct DischargeTiming
    {
        [Tooltip("The first chained arc starts this long after the first strike (65–85 ms reads as a second beat).")]
        [Min(0f)] public float chainDelay;

        [Tooltip("Seconds the arc's tip takes to reach the next enemy; its burst plays on arrival.")]
        [Min(0f)] public float chainTravel;

        [Tooltip("Pause after an arrival before the next hop's arc starts (a third target on).")]
        [Min(0f)] public float hopGap;

        public static DischargeTiming Default => new DischargeTiming { chainDelay = 0.075f, chainTravel = 0.035f, hopGap = 0.03f };

        /// <summary>Seconds after the discharge when hop <paramref name="hop"/>'s arc starts (0 = the first chained target).</summary>
        public float ArcStart(int hop) => chainDelay + Mathf.Max(0, hop) * (chainTravel + hopGap);

        /// <summary>Seconds after the discharge when hop <paramref name="hop"/>'s arc reaches its enemy (its burst and flash).</summary>
        public float Arrival(int hop) => ArcStart(hop) + chainTravel;
    }
}
