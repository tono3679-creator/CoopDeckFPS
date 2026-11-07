using UnityEngine;

namespace CoopDeck.Items
{
    public enum GaugeItemTarget
    {
        /// <summary>The currently selected hand card.</summary>
        SelectedCard = 0,

        /// <summary>Every card in hand.</summary>
        AllHandCards = 1,
    }

    /// <summary>A gauge-restoring consumable (CARD_SYSTEM.md §5).</summary>
    [CreateAssetMenu(menuName = "CoopDeck/Gauge Item Definition", fileName = "GI_NewItem")]
    public class GaugeItemDefinition : ScriptableObject
    {
        public string itemId = "item.new";
        public string displayName = "New Item";
        [TextArea(1, 3)] public string description = "";
        public GaugeItemTarget target = GaugeItemTarget.SelectedCard;
        [Tooltip("Gauge restored per targeted card (never above the card's maximum).")]
        [Min(1f)] public float amount = 40f;
        [Min(1)] public int maxCarry = 2;
        [Min(0)] public int startCount = 2;
        public Sprite icon;
        public Color color = Color.white;
    }
}
