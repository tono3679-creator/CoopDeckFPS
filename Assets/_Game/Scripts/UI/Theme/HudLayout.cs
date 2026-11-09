using UnityEngine;

namespace CoopDeck.UI
{
    /// <summary>
    /// Where each combat-HUD region sits, in the 1920×1080 safe frame (origin bottom-left, like
    /// the screen). Compact layout (2026-11-08 user feedback "UI が大きすぎる"): status at the
    /// bottom-left corner, the card hand as small slots at the bottom centre (2026-11-09: at the
    /// bottom right it sat on the first-person rifle, which runs out of that corner), the weapon
    /// readout in the bottom-right corner, objective at the top-right, the centre left open. The canvas scaler expands from this frame and
    /// <see cref="HudSafeFrame"/> clamps it to 16:9, so the rects hold at 720p, 1080p, 16:10 and
    /// ultrawide. EditMode tests check that no two regions overlap and the crosshair area stays clear.
    /// </summary>
    public static class HudLayout
    {
        public const float Width = 1920f;
        public const float Height = 1080f;
        public const float M = UiTokens.Margin;

        // ------------------------------------------------------------ card hand (bottom centre)
        public const float SlotSize = 76f;
        public const float SlotGap = 8f;

        /// <summary>Height of the key badge row under each slot.</summary>
        public const float KeyRow = 24f;

        /// <summary>Room above a slot for the item-target arrow.</summary>
        public const float SlotArrow = 18f;

        /// <summary>
        /// Slot <paramref name="slot"/> of a hand of <paramref name="count"/> (the square plus its key row). The hand is
        /// centred under the crosshair; the item slot and the pile counts sit to its left.
        /// </summary>
        public static Rect SkillSlot(int slot, int count)
        {
            float row = count * SlotSize + (count - 1) * SlotGap;
            float x = Width * 0.5f - row * 0.5f + slot * (SlotSize + SlotGap);
            return new Rect(x, M, SlotSize, SlotSize + KeyRow + 2f);
        }

        public static Rect SkillRow(int count) => Rect.MinMaxRect(SkillSlot(0, count).xMin, M, SkillSlot(count - 1, count).xMax, M + SlotSize + KeyRow + 2f);

        public static readonly Rect ItemSlot = new Rect(SkillSlot(0, 3).xMin - 16f - SlotSize, M, SlotSize, SlotSize + KeyRow + 2f);
        public static readonly Rect Piles = new Rect(ItemSlot.xMin - 10f - 86f, M + KeyRow + 2f, 86f, SlotSize);

        /// <summary>Top of the card hand, the item-target arrow included.</summary>
        public static readonly float HandTop = SkillRow(3).yMax + SlotArrow;

        // ------------------------------------------------------------ bottom right
        public static readonly Rect Weapon = new Rect(Width - M - 300f, M, 300f, 80f);

        // ------------------------------------------------------------ bottom left
        public static readonly Rect Vitals = new Rect(M, M, 420f, 60f);
        public const int LogRows = 5;
        public const float LogRowHeight = 22f;
        public static readonly Rect Log = new Rect(M, Vitals.yMax + 14f, 520f, LogRows * LogRowHeight);

        /// <summary>Ally row slot 0; further allies stack upward by <see cref="SquadStep"/> (up to three allies).</summary>
        public static readonly Rect Squad = new Rect(M, Log.yMax + 16f, 300f, 46f);
        public const float SquadStep = 54f;

        // ------------------------------------------------------------ bottom centre, above the hand (kept light)
        public static readonly Rect DeckNotice = new Rect(Width * 0.5f - 300f, HandTop + 6f, 600f, 26f);

        /// <summary>A card entering the hand: icon, name and one line of description, then it fades.</summary>
        public static readonly Rect DrawToast = new Rect(Width * 0.5f - 260f, DeckNotice.yMax + 8f, 520f, 52f);

        // ------------------------------------------------------------ top
        public static readonly Rect Objective = new Rect(Width - M - 380f, Height - M - 82f, 380f, 82f);
        public static readonly Rect Perf = new Rect(M, Height - M - 70f, 300f, 70f);
        public static readonly Rect GuideChip = new Rect(M, Perf.yMin - 8f - 24f, 220f, 24f);

        // ------------------------------------------------------------ centre
        /// <summary>Kept free of HUD panels: the aim point and the enemy under it.</summary>
        public static readonly Rect CrosshairClear = new Rect(Width * 0.5f - 200f, Height * 0.5f - 150f, 400f, 300f);

        /// <summary>Combo name banner, just above the clear zone.</summary>
        public static readonly Rect ComboBanner = new Rect(Width * 0.5f - 420f, CrosshairClear.yMax + 8f, 840f, 90f);

        /// <summary>
        /// Puts a rect at a layout rect, anchored to the nearest side of the frame (left / centre /
        /// right, bottom / top), so on 16:10 the top regions stay at the top edge.
        /// </summary>
        public static RectTransform Place(RectTransform rect, Rect layout)
        {
            Vector2 anchor = AnchorFor(layout);
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = Vector2.zero;
            rect.anchoredPosition = new Vector2(layout.x - anchor.x * Width, layout.y - anchor.y * Height);
            rect.sizeDelta = layout.size;
            return rect;
        }

        public static Vector2 AnchorFor(Rect layout)
        {
            float cx = layout.center.x / Width;
            float ax = cx < 0.35f ? 0f : cx > 0.65f ? 1f : 0.5f;
            float ay = layout.center.y / Height < 0.5f ? 0f : 1f;
            return new Vector2(ax, ay);
        }
    }
}
