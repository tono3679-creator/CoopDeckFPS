using System;
using System.Collections.Generic;
using CoopDeck.Cards;
using CoopDeck.Combat;
using CoopDeck.Deck;
using UnityEngine;

namespace CoopDeck.Skills
{
    /// <summary>
    /// Out-of-combat deck building for one combatant (the NPC ally today, players later): owns the
    /// <see cref="CardCollection"/> and the <see cref="DeckLoadout"/>, and rebuilds the
    /// <see cref="DeckController"/> from it after each edit. Edits are refused while the team is
    /// fighting, so cards never change mid-fight. Stage rewards call <see cref="GrantCard"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public class DeckLoadoutController : MonoBehaviour
    {
        [Serializable]
        public class OwnedCard
        {
            public CardDefinition card;
            [Min(1)] public int copies = 1;
        }

        [SerializeField] DeckController deckController;
        [Tooltip("The deck the loadout starts from and returns to on reset. Defaults to the DeckController's starting deck.")]
        [SerializeField] DeckDefinition initialDeck;
        [Tooltip("Cards available for building (copies owned).")]
        [SerializeField] List<OwnedCard> ownedCards = new List<OwnedCard>();
        [Tooltip("Ready-made decks for quick testing (e.g. fire & gravity, support).")]
        [SerializeField] List<DeckDefinition> presets = new List<DeckDefinition>();
        [SerializeField, Min(1)] int minDeckSize = 4;
        [SerializeField, Min(1)] int maxDeckSize = 12;
        [Tooltip("Edits are locked while this reports a fight. None = never locked.")]
        [SerializeField] TeamEngagementMonitor engagement;

        CardCollection collection;
        DeckLoadout loadout;

        /// <summary>The loadout was applied to the live deck (new shuffle, full gauges).</summary>
        public event Action<DeckLoadoutController> Applied;

        public DeckController DeckController => deckController;
        public CardCollection Collection { get { EnsureBuilt(); return collection; } }
        public DeckLoadout Loadout { get { EnsureBuilt(); return loadout; } }
        public IReadOnlyList<DeckDefinition> Presets => presets;
        public int AppliedCount { get; private set; }

        /// <summary>True while edits are refused (the team is in combat).</summary>
        public bool IsLocked => engagement != null && engagement.IsEngaged;

        public void Configure(DeckController controller, DeckDefinition initial, List<OwnedCard> owned, List<DeckDefinition> presetDecks,
            TeamEngagementMonitor monitor, int minSize = 4, int maxSize = 12)
        {
            deckController = controller;
            initialDeck = initial;
            ownedCards = owned ?? new List<OwnedCard>();
            presets = presetDecks ?? new List<DeckDefinition>();
            engagement = monitor;
            minDeckSize = minSize;
            maxDeckSize = maxSize;
            collection = null;
            loadout = null;
        }

        public void SetEngagementMonitor(TeamEngagementMonitor monitor) => engagement = monitor;

        void Awake()
        {
            if (deckController == null)
            {
                deckController = GetComponent<DeckController>();
            }
        }

        void EnsureBuilt()
        {
            if (loadout != null)
            {
                return;
            }

            DeckDefinition start = initialDeck != null ? initialDeck : deckController != null ? deckController.StartingDeck : null;
            collection = new CardCollection();
            foreach (OwnedCard owned in ownedCards)
            {
                if (owned != null && owned.card != null)
                {
                    collection.Add(owned.card, owned.copies);
                }
            }

            // The starting deck is always buildable: make sure every card in it is owned.
            if (start != null)
            {
                var needed = new Dictionary<CardDefinition, int>();
                foreach (CardDefinition card in start.cards)
                {
                    if (card != null)
                    {
                        needed.TryGetValue(card, out int n);
                        needed[card] = n + 1;
                    }
                }

                foreach (KeyValuePair<CardDefinition, int> pair in needed)
                {
                    int missing = pair.Value - collection.CountOf(pair.Key);
                    if (missing > 0)
                    {
                        collection.Add(pair.Key, missing);
                    }
                }
            }

            int handSize = start != null ? start.handSize : deckController != null && deckController.Deck != null ? deckController.Deck.HandSize : 2;
            loadout = new DeckLoadout(start != null ? start.cards : null, handSize, minDeckSize, maxDeckSize, collection);
        }

        public LoadoutEditResult TryAdd(CardDefinition card)
        {
            if (IsLocked)
            {
                return LoadoutEditResult.Locked;
            }

            LoadoutEditResult result = Loadout.TryAdd(card);
            if (result == LoadoutEditResult.Ok)
            {
                Apply();
            }

            return result;
        }

        public LoadoutEditResult TryRemoveAt(int index)
        {
            if (IsLocked)
            {
                return LoadoutEditResult.Locked;
            }

            LoadoutEditResult result = Loadout.TryRemoveAt(index);
            if (result == LoadoutEditResult.Ok)
            {
                Apply();
            }

            return result;
        }

        public LoadoutEditResult ResetToDefault()
        {
            if (IsLocked)
            {
                return LoadoutEditResult.Locked;
            }

            Loadout.ResetToDefault();
            Apply();
            return LoadoutEditResult.Ok;
        }

        public LoadoutEditResult ApplyPreset(int index)
        {
            if (IsLocked)
            {
                return LoadoutEditResult.Locked;
            }

            if (index < 0 || index >= presets.Count || presets[index] == null)
            {
                return LoadoutEditResult.InvalidIndex;
            }

            foreach (CardDefinition card in presets[index].cards)
            {
                int missing = 0;
                foreach (CardDefinition c in presets[index].cards)
                {
                    if (c == card)
                    {
                        missing++;
                    }
                }

                missing -= Collection.CountOf(card);
                if (missing > 0)
                {
                    Collection.Add(card, missing); // presets are test helpers: they bring their cards along
                }
            }

            Loadout.SetCards(presets[index].cards);
            Apply();
            return LoadoutEditResult.Ok;
        }

        /// <summary>Reward hook: adds owned copies (allowed any time; the deck list itself changes only out of combat).</summary>
        public void GrantCard(CardDefinition card, int copies = 1)
        {
            Collection.Add(card, copies);
        }

        /// <summary>Rebuilds the live deck from the list: a fresh shuffle with full gauges.</summary>
        void Apply()
        {
            if (deckController == null || !Loadout.IsValid)
            {
                return;
            }

            deckController.Rebuild(Loadout.Cards, Loadout.HandSize, deckController.StartingItems, 0);
            AppliedCount++;
            Applied?.Invoke(this);
        }
    }
}
