using UnityEngine;

namespace CoopDeck.Effects
{
    /// <summary>
    /// An effect drawn from one point to another: weapon tracers, skill beams, chain arcs, a thrown spear.
    /// <c>WeaponFx</c> and <c>SkillCaster</c> spawn the prefab from the pool and call <see cref="Play"/>.
    /// </summary>
    public interface IBeamFx
    {
        void Play(Vector3 from, Vector3 to);
    }
}
