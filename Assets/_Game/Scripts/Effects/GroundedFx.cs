using UnityEngine;

namespace CoopDeck.Effects
{
    /// <summary>
    /// Marks an effect built to stand on the ground at its origin (rings, scorch, a tornado growing out of the floor, a
    /// warning sigil). Whoever spawns it drops it onto the ground under the point that was hit (the skill caster looks
    /// straight down, skipping characters), so a blast that met an enemy's chest does not leave its ground layers
    /// floating at chest height. Visual only: gameplay keeps the real hit point.
    /// </summary>
    public class GroundedFx : MonoBehaviour
    {
        /// <summary>The downward look starts this far above the point, so a point lying on the floor still finds it.</summary>
        public const float RayLift = 0.3f;

        [Tooltip("How far below the hit point the ground may be (metres). Farther than that (a wall high up), the effect stays where it hit.")]
        [SerializeField, Min(0f)] float maxDrop = 3f;

        public float MaxDrop => maxDrop;

        public void Configure(float drop) => maxDrop = Mathf.Max(0f, drop);
    }
}
