using UnityEngine;

namespace CoopDeck.Effects
{
    /// <summary>
    /// An effect that fits its own layers to an area radius (rings and spreads grow, sprites keep their size)
    /// instead of being scaled as a whole. Callers keep the root at scale 1 and call this after spawning.
    /// </summary>
    public interface IRadiusFx
    {
        void SetRadius(float radius);
    }

    /// <summary>An impact effect that adapts to what it hit: the surface normal, or a character.</summary>
    public interface ISurfaceFx
    {
        /// <param name="normal">World normal of the surface (on a character: horizontal, back toward the shot).</param>
        /// <param name="onCharacter">True when it hit a character: no ground decals in the air.</param>
        void SetSurface(Vector3 normal, bool onCharacter);
    }

    /// <summary>
    /// The visible part of a simulated projectile. Gameplay moves the shot and tells the visual where to be;
    /// the visual never decides hits.
    /// </summary>
    public interface IProjectileFx
    {
        /// <param name="scale">1 = as authored; passives that enlarge the projectile raise it.</param>
        void Launch(Vector3 position, Vector3 direction, float scale);

        void Fly(Vector3 position, Vector3 direction);

        /// <param name="impact">True when it hit something (the blast takes over), false when it ran out of range.</param>
        void End(bool impact);
    }
}
