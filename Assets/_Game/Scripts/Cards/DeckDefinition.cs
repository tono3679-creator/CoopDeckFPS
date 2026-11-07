using System.Collections.Generic;
using UnityEngine;

namespace CoopDeck.Cards
{
    /// <summary>A starting deck: the list may contain the same card several times.</summary>
    [CreateAssetMenu(menuName = "CoopDeck/Deck Definition", fileName = "DD_NewDeck")]
    public class DeckDefinition : ScriptableObject
    {
        public string deckId = "deck.new";
        public List<CardDefinition> cards = new List<CardDefinition>();
        [Min(1)] public int handSize = 3;
    }
}
