using UnityEngine;

namespace CoopDeck.UI
{
    /// <summary>
    /// Keeps the HUD inside a centred frame no wider than <see cref="MaxAspect"/> (16:9), so on
    /// ultrawide screens the HP / weapon panels sit near the player's focus instead of the far
    /// screen edges. Height always follows the canvas (16:10 simply gets more room).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HudSafeFrame : MonoBehaviour
    {
        public const float DefaultMaxAspect = 16f / 9f;

        [SerializeField, Min(1f)] float maxAspect = DefaultMaxAspect;

        RectTransform rect;
        Vector2 lastParentSize;

        public float MaxAspect
        {
            get => maxAspect;
            set
            {
                maxAspect = Mathf.Max(1f, value);
                lastParentSize = Vector2.zero;
            }
        }

        /// <summary>Frame width for a canvas size (canvas units).</summary>
        public static float FrameWidth(Vector2 canvasSize, float maxAspect) => Mathf.Min(canvasSize.x, canvasSize.y * maxAspect);

        void LateUpdate() => Refresh();

        public void Refresh()
        {
            if (rect == null)
            {
                rect = (RectTransform)transform;
            }

            var parent = rect.parent as RectTransform;
            if (parent == null)
            {
                return;
            }

            Vector2 size = parent.rect.size;
            if (size == lastParentSize)
            {
                return;
            }

            lastParentSize = size;
            rect.sizeDelta = new Vector2(FrameWidth(size, maxAspect), 0f);
        }
    }
}
