using CoopDeck.Core;
using UnityEngine;
using UnityEngine.UI;

namespace CoopDeck.UI
{
    /// <summary>
    /// Shows <see cref="HudNotifications"/> as a small log above the bottom-left status: newest at
    /// the bottom, five lines at most, plain outlined text with a coloured mark, each line fades
    /// after a few seconds. Rows are pooled (no allocation per notice beyond its text).
    /// </summary>
    public sealed class NotificationFeedView
    {
        const float SlideTime = 0.15f;
        const float HoldTime = 4f;
        const float FadeTime = 0.6f;

        sealed class Row
        {
            public RectTransform Root;
            public CanvasGroup Group;
            public Image Icon;
            public Text Text;
            public float Age = float.PositiveInfinity;
            public float Y;
        }

        readonly Row[] rows = new Row[HudLayout.LogRows];
        RectTransform root;

        /// <summary>Text of the newest visible notice (tests, debugging).</summary>
        public string Newest { get; private set; } = "";

        public static NotificationFeedView Create(Transform parent)
        {
            var view = new NotificationFeedView();
            view.Build(parent);
            return view;
        }

        void Build(Transform parent)
        {
            Rect layout = HudLayout.Log;
            root = HudKit.CreateRect("NotificationFeed", parent, Vector2.zero, Vector2.zero, Vector2.zero, layout.size);
            HudLayout.Place(root, layout);
            for (int i = 0; i < rows.Length; i++)
            {
                var row = new Row
                {
                    Root = HudKit.CreateRect("Notice", root, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(layout.width, HudLayout.LogRowHeight)),
                };
                row.Group = row.Root.gameObject.AddComponent<CanvasGroup>();
                row.Group.interactable = false;
                row.Group.blocksRaycasts = false;
                row.Icon = UiWidgets.Icon("Icon", row.Root, UiSprites.Diamond, UiTokens.Accent, new Vector2(0f, 0.5f), new Vector2(8f, 0f), 12f);
                HudKit.AddOutline(row.Icon, 0.6f);
                row.Text = UiWidgets.Label("Text", row.Root, UiTokens.Body, UiTokens.TextMain, TextAnchor.MiddleLeft, new Vector2(0f, 0.5f),
                    new Vector2(20f + (layout.width - 20f) * 0.5f, 0f), new Vector2(layout.width - 20f, HudLayout.LogRowHeight), false, false);
                HudKit.AddOutline(row.Text, 0.9f);
                row.Root.gameObject.SetActive(false);
                rows[i] = row;
            }

            HudNotifications.Posted += OnPosted;
        }

        public void Dispose()
        {
            HudNotifications.Posted -= OnPosted;
        }

        void OnPosted(HudNotice notice)
        {
            // Rows[0] is the newest (bottom); the oldest row is recycled.
            Row recycled = rows[rows.Length - 1];
            for (int i = rows.Length - 1; i > 0; i--)
            {
                rows[i] = rows[i - 1];
            }

            rows[0] = recycled;
            Color color = HudNotifications.KindColor(notice.Kind);
            recycled.Text.text = notice.Text;
            recycled.Text.color = notice.Kind == HudNoticeKind.Info ? UiTokens.TextSub : UiTokens.TextMain;
            bool element = notice.Element != Element.None;
            recycled.Icon.sprite = element ? UiSprites.ElementIcon(notice.Element) : notice.Kind == HudNoticeKind.Revive ? UiSprites.CrossIcon : UiSprites.Diamond;
            recycled.Icon.color = element ? UiTokens.ElementColor(notice.Element) : color;
            recycled.Age = 0f;
            recycled.Y = -HudLayout.LogRowHeight;
            recycled.Root.gameObject.SetActive(true);
            Newest = notice.Text;
        }

        public void Tick(float dt)
        {
            for (int i = 0; i < rows.Length; i++)
            {
                Row row = rows[i];
                if (!row.Root.gameObject.activeSelf)
                {
                    continue;
                }

                row.Age += dt;
                if (row.Age >= HoldTime + FadeTime)
                {
                    row.Root.gameObject.SetActive(false);
                    continue;
                }

                float targetY = i * HudLayout.LogRowHeight;
                row.Y = Mathf.Lerp(row.Y, targetY, 1f - Mathf.Exp(-20f * dt));
                row.Root.anchoredPosition = new Vector2(0f, row.Y);
                row.Group.alpha = row.Age < SlideTime ? row.Age / SlideTime : row.Age > HoldTime ? 1f - (row.Age - HoldTime) / FadeTime : 1f;
            }
        }
    }
}
