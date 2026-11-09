using CoopDeck.Cards;
using CoopDeck.Core;
using UnityEngine;

namespace CoopDeck.UI
{
    /// <summary>
    /// Design tokens of the HUD ("灯守隊の探索端末": ancient industrial metal frames, amber
    /// operation lamps, blue-white 原脈 energy). Every UI colour and size comes from here, so the
    /// look can be tuned in one place. Effects keep using <see cref="Palette"/>; the UI element
    /// colours below are tuned for text and icons on dark panels (Docs/UI_DESIGN.md §2).
    /// </summary>
    public static class UiTokens
    {
        // ------------------------------------------------------------ base
        public static readonly Color Background = Palette.Hex(0x151E2B);
        public static readonly Color Panel = Palette.Hex(0x293848);
        public static readonly Color PanelDeep = Palette.Hex(0x1B2634);
        public static readonly Color TextMain = Palette.Hex(0xF3F0E8);
        public static readonly Color TextSub = Palette.Hex(0xA7B8C7);
        public static readonly Color Accent = Palette.Hex(0xE8AC54);

        /// <summary>Disabled / unlit text. Decorative only: never the sole carrier of information.</summary>
        public static readonly Color TextMuted = Palette.Hex(0x7D8D9C);

        // ------------------------------------------------------------ metal frame
        public static readonly Color FrameSteel = Palette.Hex(0x6A7C8E);
        public static readonly Color FrameLight = Palette.Hex(0x9DB0C2);
        public static readonly Color FrameDark = Palette.Hex(0x0D141D);

        /// <summary>Unlit amber lamp.</summary>
        public static readonly Color LampOff = Palette.Hex(0x4A3C28);

        /// <summary>Blue-white 原脈 energy: circuit lines, holograms, revive progress.</summary>
        public static readonly Color Energy = Palette.Hex(0xCDEEFF);

        // ------------------------------------------------------------ elements (UI)
        public static readonly Color Fire = Palette.Hex(0xF77B40);
        public static readonly Color Ice = Palette.Hex(0x9BDEFF);
        public static readonly Color Lightning = Palette.Hex(0x59D7F1);
        public static readonly Color Gravity = Palette.Hex(0xAC86EF);

        /// <summary>No element: warm stone, so it never reads as one of the four.</summary>
        public static readonly Color Neutral = Palette.Hex(0xD8CCB2);

        // ------------------------------------------------------------ rarity (hues kept away from the elements)
        public static readonly Color Common = Palette.Hex(0xB9C3CD);
        public static readonly Color Rare = Palette.Hex(0x5FD39B);
        public static readonly Color Epic = Palette.Hex(0xE97FB4);
        public static readonly Color Legendary = Palette.Hex(0xFFD466);

        /// <summary>原脈同調 (specialised skill): gold frame and sync mark.</summary>
        public static readonly Color SyncGold = Palette.Hex(0xF6C85F);

        // ------------------------------------------------------------ states
        public static readonly Color Health = Palette.Hex(0x8EDB8A);
        public static readonly Color Shield = Palette.Hex(0xDDF3FF);
        public static readonly Color Danger = Palette.Hex(0xFF5A4A);

        /// <summary>Danger for text (bars, lamps and vignettes use <see cref="Danger"/>): keeps 4.5:1 on panels.</summary>
        public static readonly Color DangerText = Palette.Hex(0xFF9A8C);
        public static readonly Color Coop = Palette.Coop.Main;

        // ------------------------------------------------------------ type scale (1920×1080 reference)
        public const int Caption = 15;
        public const int Body = 16;
        public const int Label = 17;
        public const int Title = 20;
        public const int Heading = 24;
        public const int Display = 30;
        public const int NumberLarge = 40;

        /// <summary>Nothing on the combat HUD is smaller than this (720p → 10 px, Ally X 1080p → 15 px).</summary>
        public const int MinimumFontSize = Caption;

        // ------------------------------------------------------------ spacing
        public const float Margin = 24f;
        public const float Gap = 8f;

        /// <summary>Panels are nearly opaque: text contrast never depends on the scene behind.</summary>
        public const float PanelAlpha = 0.94f;

        public static Color PanelTranslucent => WithAlpha(Panel, PanelAlpha);
        public static Color PanelDeepTranslucent => WithAlpha(PanelDeep, 0.92f);

        public static Color ElementColor(Element element)
        {
            switch (element)
            {
                case Element.Fire: return Fire;
                case Element.Ice: return Ice;
                case Element.Lightning: return Lightning;
                case Element.Gravity: return Gravity;
                default: return Neutral;
            }
        }

        /// <summary>Element colour lifted for small text on a panel (keeps ≥ 4.5:1 contrast).</summary>
        public static Color ElementText(Element element) => Color.Lerp(ElementColor(element), TextMain, 0.3f);

        public static Color RarityColor(CardRarity rarity)
        {
            switch (rarity)
            {
                case CardRarity.Rare: return Rare;
                case CardRarity.Epic: return Epic;
                case CardRarity.Legendary: return Legendary;
                default: return Common;
            }
        }

        public static string RarityName(CardRarity rarity)
        {
            switch (rarity)
            {
                case CardRarity.Rare: return "レア";
                case CardRarity.Epic: return "エピック";
                case CardRarity.Legendary: return "レジェンダリー";
                default: return "コモン";
            }
        }

        /// <summary>Rarity is also shown as 1–4 pips, so it never relies on colour alone.</summary>
        public static int RarityPips(CardRarity rarity) => (int)rarity + 1;

        public static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        // ------------------------------------------------------------ contrast (WCAG 2.x)

        /// <summary>Relative luminance of an sRGB colour (alpha ignored).</summary>
        public static float RelativeLuminance(Color srgb)
        {
            return 0.2126f * Linear(srgb.r) + 0.7152f * Linear(srgb.g) + 0.0722f * Linear(srgb.b);
        }

        public static float ContrastRatio(Color a, Color b)
        {
            float la = RelativeLuminance(a);
            float lb = RelativeLuminance(b);
            if (la < lb)
            {
                (la, lb) = (lb, la);
            }

            return (la + 0.05f) / (lb + 0.05f);
        }

        /// <summary>A translucent colour composited over an opaque one (sRGB, like the screen).</summary>
        public static Color Over(Color top, Color bottom)
        {
            Color c = Color.Lerp(bottom, top, top.a);
            c.a = 1f;
            return c;
        }

        static float Linear(float c) => c <= 0.04045f ? c / 12.92f : Mathf.Pow((c + 0.055f) / 1.055f, 2.4f);
    }
}
