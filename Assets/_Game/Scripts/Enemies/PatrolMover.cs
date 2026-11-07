using CoopDeck.Combat;
using UnityEngine;

namespace CoopDeck.Enemies
{
    /// <summary>Moves a target back and forth along a local axis (moving-target practice). Stops while dead.</summary>
    public class PatrolMover : MonoBehaviour
    {
        [SerializeField] Vector3 localTravel = new Vector3(6f, 0f, 0f);
        [SerializeField, Min(0.1f)] float speed = 3f;
        [SerializeField, Range(0f, 1f)] float startPhase;

        Vector3 origin;
        float phase;
        Health health;
        Rigidbody body;
        ForceReceiver receiver;

        public void Configure(Vector3 travel, float moveSpeed, float phaseOffset)
        {
            localTravel = travel;
            speed = moveSpeed;
            startPhase = phaseOffset;
        }

        void Awake()
        {
            origin = transform.position;
            health = GetComponent<Health>();
            body = GetComponent<Rigidbody>();
            receiver = GetComponent<ForceReceiver>();
            if (receiver != null)
            {
                // The patrol owns the position; skill pulls are added as an offset.
                receiver.ExternallyDriven = true;
            }

            float length = localTravel.magnitude;
            phase = startPhase * (length > 0f ? 2f * length / speed : 0f);
        }

        void FixedUpdate()
        {
            if (health != null && !health.IsAlive)
            {
                return;
            }

            float length = localTravel.magnitude;
            if (length < 0.001f)
            {
                return;
            }

            phase += Time.fixedDeltaTime;
            float t = Mathf.PingPong(phase * speed / length, 1f);
            float eased = t * t * (3f - 2f * t);
            Vector3 target = origin + transform.rotation * localTravel * (eased - 0.5f);
            if (receiver != null)
            {
                target += receiver.Offset;
            }

            if (body != null && body.isKinematic)
            {
                body.MovePosition(target);
            }
            else
            {
                transform.position = target;
            }
        }
    }
}
