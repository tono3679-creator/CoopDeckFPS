using UnityEngine;

namespace CoopDeck.Effects
{
    /// <summary>Turns a layer at a steady rate (a tornado's flame tubes).</summary>
    public class SpinFx : MonoBehaviour
    {
        [SerializeField] Vector3 degreesPerSecond = new Vector3(0f, 0f, 180f);

        public void Configure(Vector3 rate) => degreesPerSecond = rate;

        void Update() => transform.Rotate(degreesPerSecond * Time.deltaTime, Space.Self);
    }
}
