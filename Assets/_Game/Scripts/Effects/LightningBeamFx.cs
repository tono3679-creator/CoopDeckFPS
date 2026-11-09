using UnityEngine;

namespace CoopDeck.Effects
{
    /// <summary>A lightning bolt from one point to another as a skill beam (chain lightning): <see cref="LightningBoltFx"/> on the same object.</summary>
    [RequireComponent(typeof(LightningBoltFx))]
    public class LightningBeamFx : MonoBehaviour, IBeamFx
    {
        [SerializeField, Min(0.1f)] float strength = 1f;
        [Tooltip("Seconds for the bolt's tip to reach the end (0 = at once).")]
        [SerializeField, Min(0f)] float travel = 0.03f;

        LightningBoltFx bolt;
        int seed;

        public void Configure(float boltStrength, float travelSeconds)
        {
            strength = boltStrength;
            travel = travelSeconds;
        }

        public void Play(Vector3 from, Vector3 to)
        {
            if (bolt == null)
            {
                bolt = GetComponent<LightningBoltFx>();
            }

            bolt.Play(from, to, strength, ++seed, travel);
        }
    }
}
