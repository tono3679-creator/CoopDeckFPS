using UnityEngine;

namespace CoopDeck.Core
{
    /// <summary>
    /// Anything that moves a character (FPS motor, NavMesh agent, network proxy). Animation reads
    /// only this, so the same Animator setup works for players, NPCs and enemies.
    /// </summary>
    public interface ILocomotionSource
    {
        /// <summary>World-space velocity in m/s.</summary>
        Vector3 Velocity { get; }

        bool IsGrounded { get; }
    }
}
