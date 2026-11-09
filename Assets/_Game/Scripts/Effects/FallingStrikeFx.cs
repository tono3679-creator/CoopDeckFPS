using UnityEngine;

namespace CoopDeck.Effects
{
    /// <summary>
    /// Something falling onto the effect's origin over a set time (the meteor in the strike warning): it starts
    /// <see cref="height"/> above and <see cref="lean"/> to the side, accelerates down, faces its path and disappears on
    /// touching down (the blast is the card's area effect, played by the caster at the same moment).
    /// </summary>
    public class FallingStrikeFx : MonoBehaviour
    {
        [SerializeField] Transform body;
        [SerializeField, Min(0.05f)] float duration = 0.9f;
        [SerializeField, Min(0f)] float height = 26f;
        [SerializeField] Vector3 lean = new Vector3(9f, 0f, 6f);
        [Tooltip("Time 0..1 to path 0..1 (accelerating).")]
        [SerializeField] AnimationCurve ease = new AnimationCurve(new Keyframe(0f, 0f, 0f, 0.4f), new Keyframe(1f, 1f, 2.2f, 0f));

        float age;
        Vector3 start;
        bool pending;

        public Transform Body => body;
        public float Duration => duration;

        public void Configure(Transform falling, float seconds, float fallHeight, Vector3 sideways)
        {
            body = falling;
            duration = seconds;
            height = fallHeight;
            lean = sideways;
        }

        void OnEnable()
        {
            // The pool may enable a new instance before moving it into place: read the position on the first frame.
            age = 0f;
            pending = true;
            if (body != null)
            {
                body.gameObject.SetActive(true);
            }
        }

        void LateUpdate()
        {
            if (body == null || !body.gameObject.activeSelf)
            {
                return;
            }

            if (pending)
            {
                pending = false;
                start = transform.position + Vector3.up * height + lean;
                Place(0f);
                return;
            }

            age += Time.deltaTime;
            float t = age / duration;
            if (t >= 1f)
            {
                body.gameObject.SetActive(false);
                return;
            }

            Place(t);
        }

        void Place(float t)
        {
            Vector3 end = transform.position;
            body.position = Vector3.LerpUnclamped(start, end, ease.Evaluate(t));
            Vector3 dir = end - start;
            if (dir.sqrMagnitude > 1e-4f)
            {
                body.rotation = Quaternion.LookRotation(dir.normalized);
            }
        }
    }
}
