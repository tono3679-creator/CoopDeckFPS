using System;
using System.Collections.Generic;
using CoopDeck.Core;
using UnityEngine;

namespace CoopDeck.UI
{
    public enum HudNoticeKind
    {
        Info = 0,
        Objective = 1,
        Success = 2,
        Warning = 3,
        Danger = 4,
        Coop = 5,
        Revive = 6,
    }

    public readonly struct HudNotice
    {
        public HudNotice(HudNoticeKind kind, string text, Element element)
        {
            Kind = kind;
            Text = text;
            Element = element;
        }

        public HudNoticeKind Kind { get; }
        public string Text { get; }

        /// <summary>Optional element (shows its icon); None = the kind's lamp only.</summary>
        public Element Element { get; }
    }

    /// <summary>
    /// The combat notification bus (wave start / clear, ally down / revived …). HUD components
    /// post here; the feed under the objective shows the latest few. Gameplay never posts: the
    /// HUD translates gameplay events into notices.
    /// </summary>
    public static class HudNotifications
    {
        public const int HistorySize = 8;

        static readonly List<HudNotice> history = new List<HudNotice>(HistorySize);

        public static event Action<HudNotice> Posted;

        /// <summary>Most recent last.</summary>
        public static IReadOnlyList<HudNotice> History => history;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            history.Clear();
            Posted = null;
        }

        public static void Post(HudNoticeKind kind, string text, Element element = Element.None)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            var notice = new HudNotice(kind, text, element);
            if (history.Count == HistorySize)
            {
                history.RemoveAt(0);
            }

            history.Add(notice);
            Posted?.Invoke(notice);
        }

        public static Color KindColor(HudNoticeKind kind)
        {
            switch (kind)
            {
                case HudNoticeKind.Objective: return UiTokens.Accent;
                case HudNoticeKind.Success: return UiTokens.Health;
                case HudNoticeKind.Warning: return UiTokens.Accent;
                case HudNoticeKind.Danger: return UiTokens.Danger;
                case HudNoticeKind.Coop: return UiTokens.Coop;
                case HudNoticeKind.Revive: return UiTokens.Energy;
                default: return UiTokens.TextSub;
            }
        }
    }
}
