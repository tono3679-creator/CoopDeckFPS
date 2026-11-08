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

        /// <summary>The element's main colour from the art bible palette.</summary>
        public static Color ColorOf(Element element) => Palette.Of(element).Main;
    }
}
