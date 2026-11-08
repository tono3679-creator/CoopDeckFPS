using UnityEngine;

namespace CoopDeck.Core
{
    /// <summary>One element's effect tones: the hot core, the element colour itself, the deep outer edge.</summary>
    public readonly struct ElementTones
    {
        public readonly Color Core;
        public readonly Color Main;
        public readonly Color Deep;

        public ElementTones(Color core, Color main, Color deep)
        {
            Core = core;
            Main = main;
            Deep = deep;
        }
    }

    /// <summary>
    /// The art bible's palette (Docs/ART_BIBLE.md, "夕方の空中遺跡"): the single source for every
    /// colour in materials, effects and UI. Terrain stays calm and mid-toned; elements, enemies and
    /// effects carry the saturation. Add a colour here (and to the bible) instead of writing a
    /// literal elsewhere.
    /// </summary>
    public static class Palette
    {
        // ------------------------------------------------------------ environment
        public static readonly Color Floor = Hex(0xB9AE97);      // 砂岩
        public static readonly Color Wall = Hex(0x6F7F95);       // 青灰の石
        public static readonly Color Trim = Hex(0x4E5D6E);       // 深いスレート（縁・小物）
        public static readonly Color SkyTop = Hex(0x5C8FD6);
        public static readonly Color SkyHorizon = Hex(0xF3D9B1); // 暖かい霞
        public static readonly Color Shadow = Hex(0x46507A);     // 藍
        public static readonly Color Accent = Hex(0x2FE0C8);     // 闘技場シアン

        // ------------------------------------------------------------ effects
        public static readonly ElementTones Fire = new ElementTones(Hex(0xFFE9A8), Hex(0xFF732E), Hex(0xC9301E));
        public static readonly ElementTones Ice = new ElementTones(Hex(0xF0FCFF), Hex(0x6BD9FF), Hex(0x2F7BD6));
        public static readonly ElementTones Lightning = new ElementTones(Hex(0xFFFBE6), Hex(0xFFE040), Hex(0x8E5CFF));
        public static readonly ElementTones Gravity = new ElementTones(Hex(0xEBDDFF), Hex(0xA666FF), Hex(0x2B0F57));
        public static readonly ElementTones Neutral = new ElementTones(Hex(0xEBFFF4), Hex(0x73FFB3), Hex(0x1E9E76));

        /// <summary>Co-op combos (two allies made it): gold.</summary>
        public static readonly ElementTones Coop = new ElementTones(Hex(0xFFF7DB), Hex(0xFFD14D), Hex(0xE0861F));

        /// <summary>Incoming harm: enemy wind-ups and the meteor's landing zone.</summary>
        public static readonly Color Danger = Hex(0xFF4733);

        /// <summary>Smoke and soot: the shadow indigo, darkened and greyed (reads as smoke, not lilac, over sand).</summary>
        public static readonly Color Smoke = Hex(0x3C3D48);

        public static ElementTones Of(Element element)
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

        /// <summary>0xRRGGBB (sRGB) to an opaque colour.</summary>
        public static Color Hex(uint rgb)
        {
            return new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
        }
    }
}
