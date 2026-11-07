using UnityEngine;

namespace CoopDeck.Combat
{
    /// <summary>
    /// Lets skills displace an actor (gravity pull). Tracks the total displacement so a
    /// respawn can undo it. When another component drives the position (a patrol path, an AI
    /// agent), it sets <see cref="ExternallyDriven"/> and adds <see cref="Offset"/> itself.
    /// </summary>
    [DisallowMultipleComponent]
    public class ForceReceiver : MonoBehaviour
    {
        [Tooltip("Upper bound on total displacement so repeated pulls cannot fling a target across the map.")]
        [SerializeField, Min(0f)] float maxDisplacement = 12f;

        Vector3 offset;
        Vector3 pullCenter;
        float pullSpeed;
        float pullRemaining;
        float stopDistance;

        public bool ExternallyDriven { get; set; }
        public Vector3 Offset => offset;
        public bool IsBeingPulled => pullRemaining > 0f;

        /// <summary>Drags the actor horizontally toward <paramref name="center"/> for a while.</summary>
        public void Pull(Vector3 center, float speed, float duration, float stopAt = 1.2f)
        {
            if (speed <= 0f || duration <= 0f)
            {
                return;
            }

            pullCenter = center;
            pullSpeed = speed;
            pullRemaining = Mathf.Max(pullRemaining, duration);
            stopDistance = Mathf.Max(0f, stopAt);
        }

        void Update()
        {
            if (pullRemaining <= 0f)
            {
                return;
            }

            float dt = Time.deltaTime;
            pullRemaining -= dt;

            Vector3 toCenter = pullCenter - transform.position;
            toCenter.y = 0f;
            float distance = toCenter.magnitude;
            if (distance <= stopDistance)
            {
                return;
            }

            Vector3 step = toCenter / distance * Mathf.Min(pullSpeed * dt, distance - stopDistance);
            Vector3 clamped = Vector3.ClampMagnitude(offset + step, maxDisplacement);
            step = clamped - offset;
            offset = clamped;

            if (ExternallyDriven)
            {
                return;
            }

            if (TryGetComponent(out UnityEngine.AI.NavMeshAgent agent) && agent.enabled && agent.isOnNavMesh)
            {
                agent.Move(step); // stays on the NavMesh instead of being pushed into walls
            }
            else if (TryGetComponent(out CharacterController controller) && controller.enabled)
            {
                controller.Move(step);
            }
            else
            {
                transform.position += step;
            }
        }

        /// <summary>Stops any pull and moves the actor back by the accumulated displacement.</summary>
        public void ResetOffset()
        {
            if (!ExternallyDriven)
            {
                transform.position -= offset;
            }

            offset = Vector3.zero;
            pullRemaining = 0f;
        }
    }
}
