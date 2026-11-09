using UnityEngine;

namespace CoopDeck.Effects
{
    /// <summary>
    /// Re-strikes a <see cref="SurfaceArcFx"/> at random intervals while its effect is still running (a charged body
    /// crackling for the whole buff); stops once the effect stops emitting.
    /// </summary>
    [RequireComponent(typeof(SurfaceArcFx))]
    public class RepeatArcsFx : MonoBehaviour
    {
        [SerializeField, Min(0.05f)] float minInterval = 0.35f;
        [SerializeField, Min(0.05f)] float maxInterval = 0.9f;

        SurfaceArcFx arcs;
        PooledFx pooled;
        float next;

        public void Configure(float shortest, float longest)
        {
            minInterval = shortest;
            maxInterval = Mathf.Max(shortest, longest);
        }

        void OnEnable()
        {
            arcs = GetComponent<SurfaceArcFx>();
            pooled = GetComponentInParent<PooledFx>();
            next = Random.Range(minInterval, maxInterval);
        }

        void Update()
        {
            if (pooled != null && pooled.Stopping)
            {
                return;
            }

            next -= Time.deltaTime;
            if (next <= 0f)
            {
                next = Random.Range(minInterval, maxInterval);
                arcs.Restart();
            }
        }
    }
}
