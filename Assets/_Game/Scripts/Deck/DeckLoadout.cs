using System;
using System.Collections.Generic;
using CoopDeck.Cards;
using CoopDeck.Core;

namespace CoopDeck.Deck
{
    /// <summary>
    /// Cards a combatant owns outside a run's draw cycle: how many copies of each definition.
    /// Stage rewards, shops and unlocks add here (for players and NPC allies alike); a deck list
    /// (<see cref="DeckLoadout"/>) can only use what the collection holds.
    /// </summary>
    public sealed class CardCollection
    {
        readonly List<CardDefinition> order = new List<CardDefinition>();
        readonly Dictionary<CardDefinition, int> counts = new Dictionary<CardDefinition, int>();

        public event Action Changed;

        /// <summary>Distinct definitions in the order they were first added.</summary>
        public IReadOnlyList<CardDefinition> Definitions => order;

        public int CountOf(CardDefinition definition) =>
            definition != null && counts.TryGetValue(definition, out int count) ? count : 0;

        public void Add(CardDefinition definition, int copies = 1)
        {
            if (definition == null || copies <= 0)
            {
                return;
            }

            if (!counts.ContainsKey(definition))
            {
                order.Add(definition);
                counts[definition] = 0;
            }

            counts[definition] += copies;
            Changed?.Invoke();
        }

        public bool Remove(CardDefinition definition, int copies = 1)
        {
            int have = CountOf(definition);
            if (have <= 0 || copies <= 0)
            {
                return false;
            }

            int left = Math.Max(0, have - copies);
            counts[definition] = left;
            if (left == 0)
            {
                counts.Remove(definition);
                order.Remove(definition);
            }

            Changed?.Invoke();
            return true;
        }
    }

    public enum LoadoutEditResult
    {
        Ok = 0,
        DeckFull = 1,
        NoCopiesLeft = 2,
        BelowMinimum = 3,
        InvalidCard = 4,
        InvalidIndex = 5,

        /// <summary>The owner is fighting: decks are locked until the fight ends.</summary>
        Locked = 6,
    }

    /// <summary>
    /// A deck list being built (before a run, or between fights): which cards, within a size range
    /// and the copies owned. The live draw cycle (<see cref="DeckState"/>) is built from it, so
    /// editing never changes cards mid-fight. Plain C# for tests and a future host / save file.
    /// </summary>
    public sealed class DeckLoadout
    {
        readonly List<CardDefinition> cards = new List<CardDefinition>();
        readonly List<CardDefinition> defaults = new List<CardDefinition>();

        public DeckLoadout(IEnumerable<CardDefinition> initialCards, int handSize, int minSize, int maxSize, CardCollection collection)
        {
            HandSize = Math.Max(1, handSize);
            MinSize = Math.Max(1, minSize);
            MaxSize = Math.Max(MinSize, maxSize);
            Collection = collection ?? new CardCollection();
            if (initialCards != null)
            {
                foreach (CardDefinition card in initialCards)
                {
                    if (card != null && card.type == CardType.Active)
                    {
                        defaults.Add(card);
                    }
                }
            }

            cards.AddRange(defaults);
        }

        public event Action Changed;

        public IReadOnlyList<CardDefinition> Cards => cards;
        public IReadOnlyList<CardDefinition> DefaultCards => defaults;
        public CardCollection Collection { get; }
        public int HandSize { get; }
        public int MinSize { get; }
        public int MaxSize { get; }
        public int Count => cards.Count;

        /// <summary>Copies of <paramref name="definition"/> in the list.</summary>
        public int CopiesInDeck(CardDefinition definition)
        {
            int n = 0;
            foreach (CardDefinition card in cards)
            {
                if (card == definition)
                {
                    n++;
                }
            }

            return n;
        }

        /// <summary>Copies still free to add (owned minus already in the list).</summary>
        public int CopiesAvailable(CardDefinition definition) => Math.Max(0, Collection.CountOf(definition) - CopiesInDeck(definition));

        public LoadoutEditResult CanAdd(CardDefinition definition)
        {
            if (definition == null || definition.type != CardType.Active)
            {
                return LoadoutEditResult.InvalidCard;
            }

            if (cards.Count >= MaxSize)
            {
                return LoadoutEditResult.DeckFull;
            }

            return CopiesAvailable(definition) > 0 ? LoadoutEditResult.Ok : LoadoutEditResult.NoCopiesLeft;
        }

        public LoadoutEditResult TryAdd(CardDefinition definition)
        {
            LoadoutEditResult result = CanAdd(definition);
            if (result == LoadoutEditResult.Ok)
            {
                cards.Add(definition);
                Changed?.Invoke();
            }

            return result;
        }

        public LoadoutEditResult TryRemoveAt(int index)
        {
            if (index < 0 || index >= cards.Count)
            {
                return LoadoutEditResult.InvalidIndex;
            }

            if (cards.Count <= MinSize)
            {
                return LoadoutEditResult.BelowMinimum;
            }

            cards.RemoveAt(index);
            Changed?.Invoke();
            return LoadoutEditResult.Ok;
        }

        /// <summary>Removes one copy of <paramref name="definition"/> (the last one in the list).</summary>
        public LoadoutEditResult TryRemove(CardDefinition definition)
        {
            int index = cards.LastIndexOf(definition);
            return index < 0 ? LoadoutEditResult.InvalidCard : TryRemoveAt(index);
        }

        /// <summary>Back to the starting list (the cards the loadout was created with).</summary>
        public void ResetToDefault()
        {
            cards.Clear();
            cards.AddRange(defaults);
            Changed?.Invoke();
        }

        /// <summary>Replaces the list (presets). Cards beyond the owned copies or the size limit are skipped.</summary>
        public int SetCards(IEnumerable<CardDefinition> list)
        {
            cards.Clear();
            if (list != null)
            {
                foreach (CardDefinition card in list)
                {
                    if (CanAdd(card) == LoadoutEditResult.Ok)
                    {
                        cards.Add(card);
                    }
                }
            }

            Changed?.Invoke();
            return cards.Count;
        }

        public bool IsValid => cards.Count >= MinSize && cards.Count <= MaxSize;

        /// <summary>Cards per element, indexed by (int)Element (None = no element).</summary>
        public int[] ElementCounts()
        {
            var result = new int[5];
            foreach (CardDefinition card in cards)
            {
                int index = (int)card.element;
                if (index >= 0 && index < result.Length)
                {
                    result[index]++;
                }
            }

            return result;
        }

        /// <summary>The element with the most cards (None if the deck has no elemental card).</summary>
        public Element DominantElement()
        {
            int[] counts = ElementCounts();
            Element best = Element.None;
            int bestCount = 0;
            for (int i = 1; i < counts.Length; i++)
            {
                if (counts[i] > bestCount)
                {
                    bestCount = counts[i];
                    best = (Element)i;
                }
            }

            return best;
        }
    }
}
