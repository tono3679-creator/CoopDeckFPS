using UnityEngine;

namespace CoopDeck.Effects
{
    /// <summary>A short bright streak that travels from the muzzle to the impact point.</summary>
    [RequireComponent(typeof(LineRenderer))]
    public class TracerFx : MonoBehaviour
    {
        [SerializeField, Min(1f)] float speed = 320f;
        [SerializeField, Min(0.1f)] float streakLength = 5f;
        [SerializeField, Min(0.001f)] float width = 0.035f;

        LineRenderer line;
        Vector3 from;
        Vector3 direction;
        float distance;
        float travelled;
        bool playing;

        void Awake()
        {
            line = GetComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.enabled = false;
        }

        public void Play(Vector3 start, Vector3 end)
        {
            if (line == null)
            {
                Awake();
            }

            from = start;
            Vector3 delta = end - start;
            distance = delta.magnitude;
            direction = distance > 0.0001f ? delta / distance : Vector3.forward;
            travelled = 0f;
            playing = distance > 0.05f;
            line.widthMultiplier = width;
            line.enabled = playing;
            UpdateLine();
        }

        void Update()
        {
            if (!playing)
            {
                return;
            }

            travelled += speed * Time.deltaTime;
            if (travelled - streakLength >= distance)
            {
                playing = false;
                line.enabled = false;
                return;
            }

            UpdateLine();
        }

        void UpdateLine()
        {
            float head = Mathf.Min(travelled + streakLength * 0.5f, distance);
            float tail = Mathf.Clamp(travelled - streakLength * 0.5f, 0f, distance);
            line.SetPosition(0, from + direction * tail);
            line.SetPosition(1, from + direction * head);
        }

        void OnDisable()
        {
            playing = false;
            if (line != null)
            {
                line.enabled = false;
            }
        }
    }
}
