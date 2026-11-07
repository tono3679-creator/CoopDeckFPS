using CoopDeck.Core;
using UnityEngine;

namespace CoopDeck.Cards
{
    /// <summary>Display names and colors per element, shared by UI, icons and effects.</summary>
    public static class ElementStyle
    {
        public static string DisplayName(Element element)
        {
            switch (element)
            {
                case Element.Fire: return "火";
                case Element.Ice: return "氷";
                case Element.Lightning: return "雷";
                case Element.Gravity: return "重力";
                default: return "無";
            }
        }

        public static Color ColorOf(Element element)
        {
            switch (element)
            {
                case Element.Fire: return new Color(1f, 0.45f, 0.18f);
                case Element.Ice: return new Color(0.42f, 0.85f, 1f);
                case Element.Lightning: return new Color(1f, 0.88f, 0.25f);
                case Element.Gravity: return new Color(0.65f, 0.4f, 1f);
                default: return new Color(0.45f, 1f, 0.7f);
            }
        }
    }
}
