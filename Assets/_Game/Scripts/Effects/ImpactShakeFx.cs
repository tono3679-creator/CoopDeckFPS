using UnityEngine;

namespace CoopDeck.Effects
{
    /// <summary>
    /// Shakes the nearby cameras when a big effect lands (<see cref="CameraShaker.Shake"/>, falling off with distance,
    /// halved on low effects and off with -noshake), optionally with a short FOV punch, after an optional delay
    /// (a collapse that lands at the end of its pull).
    /// </summary>
    public class ImpactShakeFx : MonoBehaviour
    {
        [SerializeField, Range(0f, 1f)] float strength = 0.2f;
        [SerializeField, Min(0f)] float punchDegrees;
        [SerializeField, Min(0f)] float delay;

        float age;
        bool done;

        public void Configure(float shake, float punch, float after)
        {
            strength = shake;
            punchDegrees = punch;
            delay = after;
        }

        // Fired from Update (not OnEnable): the pool may enable a new instance before moving it to the impact.
        void OnEnable()
        {
            age = 0f;
            done = false;
        }

        void Update()
        {
            if (done)
            {
                return;
            }

            if (age >= delay)
            {
                Fire();
            }
        }

        void LateUpdate()
        {
            age += Time.deltaTime;
        }

        void Fire()
        {
            done = true;
            CameraShaker.Shake(transform.position, strength);
            if (punchDegrees > 0f)
            {
                CameraShaker.Punch(transform.position, punchDegrees);
            }
        }
    }
}
