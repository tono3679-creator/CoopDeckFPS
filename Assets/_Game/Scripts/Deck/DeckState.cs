using System;
using System.Collections.Generic;
using CoopDeck.Cards;
using UnityEngine;

namespace CoopDeck.Deck
{
    public enum CardUseFailure
    {
        None = 0,
        InvalidSlot = 1,
        EmptySlot = 2,
        OnCooldown = 3,
    }

    public readonly struct CardUseResult
    {
        public CardUseResult(int slot, CardRuntime card, float gaugeBefore, float gaugeAfter, bool depleted)
        {
            Slot = slot;
            Card = card;
            GaugeBefore = gaugeBefore;
            GaugeAfter = gaugeAfter;
            Depleted = depleted;
        }

        public int Slot { get; }
        public CardRuntime Card { get; }
        public float GaugeBefore { get; }
        public float GaugeAfter { get; }

        /// <summary>The card hit 0 and has already moved to the discard pile.</summary>
        public bool Depleted { get; }

        /// <summary>
        /// Rule R6: every use is at full power, including the last one made with less gauge than
        /// the cost. Kept as a property so upgrades or passives can change it later.
        /// </summary>
        public float PowerMultiplier => 1f;
    }

    /// <summary>
    /// The card cycle of one combatant (CARD_SYSTEM.md §1, §4): draw pile, fixed hand slots,
    /// discard pile and passives. Plain C# so the rules are unit-tested, can run on a host, and
    /// are shared by players and NPCs. Presentation and skill execution subscribe to its events.
    ///
    /// Event order for one use: CardUsed → GaugeChanged → CardDepleted → Reshuffled (if needed) → CardDrawn.
    /// </summary>
    public sealed class DeckState
    {
        readonly List<CardRuntime> drawPile = new List<CardRuntime>(); // the next card to draw is the last element
        readonly List<CardRuntime> discardPile = new List<CardRuntime>();
        readonly List<CardRuntime> passives = new List<CardRuntime>();
        readonly CardRuntime[] hand;
        readonly IRandom random;
        int nextInstanceId = 1;

        public event Action<int, CardRuntime> CardDrawn;
        public event Action<CardUseResult> CardUsed;

        /// <summary>slot, card, old gauge, new gauge.</summary>
        public event Action<int, CardRuntime, float, float> GaugeChanged;

        /// <summary>The card reached 0 gauge and moved from the slot to the discard pile.</summary>
        public event Action<int, CardRuntime> CardDepleted;

        /// <summary>Number of cards moved from the discard pile back into the draw pile.</summary>
        public event Action<int> Reshuffled;

        public DeckState(IEnumerable<CardDefinition> cards, int handSize, IRandom random, bool drawOpeningHand = true)
        {
            if (handSize < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(handSize), "A deck needs at least one hand slot.");
            }

            this.random = random ?? throw new ArgumentNullException(nameof(random));
            hand = new CardRuntime[handSize];

            if (cards != null)
            {
                foreach (CardDefinition definition in cards)
                {
                    if (definition == null)
                    {
                        continue;
                    }

                    CardRuntime card = CreateRuntime(definition);
                    if (card.IsActive)
                    {
                        drawPile.Add(card);
                    }
                    else
                    {
                        passives.Add(card); // R14: passives never enter the draw cycle
                    }
                }
            }

            ShuffleUtility.Shuffle(drawPile, random);
            if (drawOpeningHand)
            {
                FillEmptySlots();
            }
        }

        public int HandSize => hand.Length;
        public IReadOnlyList<CardRuntime> DrawPile => drawPile;
        public IReadOnlyList<CardRuntime> DiscardPile => discardPile;
        public IReadOnlyList<CardRuntime> Passives => passives;
        public int DrawPileCount => drawPile.Count;
        public int DiscardPileCount => discardPile.Count;
        public CardRuntime NextDraw => drawPile.Count > 0 ? drawPile[drawPile.Count - 1] : null;

        public int HandCount
        {
            get
            {
                int count = 0;
                foreach (CardRuntime card in hand)
                {
                    if (card != null)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        /// <summary>Active cards owned (draw + hand + discard). Only AddCard / RemoveCard change it.</summary>
        public int ActiveCardCount => drawPile.Count + discardPile.Count + HandCount;

        public CardRuntime GetHandCard(int slot) => slot >= 0 && slot < hand.Length ? hand[slot] : null;

        public int FindHandSlot(CardRuntime card)
        {
            for (int i = 0; i < hand.Length; i++)
            {
                if (hand[i] != null && hand[i] == card)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>Draws into every empty slot that can be filled (opening hand, small decks).</summary>
        public void FillEmptySlots()
        {
            for (int slot = 0; slot < hand.Length; slot++)
            {
                if (hand[slot] == null)
                {
                    DrawCard(slot);
                }
            }
        }

        /// <summary>
        /// Uses the card in <paramref name="slot"/>: spends its cost (the last use may spend less,
        /// R6), starts its cooldown, and if it hits 0 discards it and draws a replacement (R7, R8).
        /// </summary>
        public bool TryUseCard(int slot, out CardUseResult result, out CardUseFailure failure)
        {
            result = default;
            if (slot < 0 || slot >= hand.Length)
            {
                failure = CardUseFailure.InvalidSlot;
                return false;
            }

            CardRuntime card = hand[slot];
            if (card == null)
            {
                failure = CardUseFailure.EmptySlot;
                return false;
            }

            if (card.IsOnCooldown)
            {
                failure = CardUseFailure.OnCooldown;
                return false;
            }

            float before = card.Gauge;
            float after = before - card.GaugeCost;
            if (after < 0.0001f)
            {
                after = 0f;
            }

            card.Gauge = after;
            card.CooldownRemaining = card.Cooldown;
            bool depleted = after <= 0f;
            result = new CardUseResult(slot, card, before, after, depleted);
            failure = CardUseFailure.None;

            CardUsed?.Invoke(result);
            GaugeChanged?.Invoke(slot, card, before, after);
            if (depleted)
            {
                DiscardOnEmpty(slot);
            }

            return true;
        }

        /// <summary>
        /// Moves a card with 0 gauge from its slot to the discard pile and refills the slot.
        /// Refuses cards that still have gauge: there is no voluntary discard (R11).
        /// </summary>
        public bool DiscardOnEmpty(int slot)
        {
            CardRuntime card = GetHandCard(slot);
            if (card == null || card.Gauge > 0f)
            {
                return false;
            }

            hand[slot] = null;
            card.CooldownRemaining = 0f;
            discardPile.Add(card);
            CardDepleted?.Invoke(slot, card);
            DrawCard(slot);
            return true;
        }

        /// <summary>Draws the next card into an empty slot, reshuffling the discard pile first if the draw pile is empty (R9).</summary>
        public bool DrawCard(int slot)
        {
            if (slot < 0 || slot >= hand.Length || hand[slot] != null)
            {
                return false;
            }

            if (drawPile.Count == 0)
            {
                Reshuffle();
            }

            if (drawPile.Count == 0)
            {
                return false; // every card is already in hand: the slot stays empty
            }

            int last = drawPile.Count - 1;
            CardRuntime card = drawPile[last];
            drawPile.RemoveAt(last);
            card.Gauge = card.MaxGauge; // R10: a drawn card starts full
            card.CooldownRemaining = 0f;
            hand[slot] = card;
            CardDrawn?.Invoke(slot, card);
            return true;
        }

        /// <summary>Moves the discard pile into the draw pile and shuffles. Returns how many cards moved.</summary>
        public int Reshuffle()
        {
            if (discardPile.Count == 0)
            {
                return 0;
            }

            int count = discardPile.Count;
            drawPile.AddRange(discardPile);
            discardPile.Clear();
            ShuffleUtility.Shuffle(drawPile, random);
            Reshuffled?.Invoke(count);
            return count;
        }

        /// <summary>Advances cooldowns of the cards in hand.</summary>
        public void Tick(float deltaTime)
        {
            foreach (CardRuntime card in hand)
            {
                if (card != null && card.CooldownRemaining > 0f)
                {
                    card.CooldownRemaining = Mathf.Max(0f, card.CooldownRemaining - deltaTime);
                }
            }
        }

        /// <summary>
        /// Restores gauge on a card in hand, never above its maximum. Cards outside the hand
        /// (discard pile) cannot be targeted. Returns the amount actually restored.
        /// </summary>
        public float RestoreCharge(int slot, float amount)
        {
            CardRuntime card = GetHandCard(slot);
            if (card == null || amount <= 0f || card.Gauge <= 0f)
            {
                return 0f;
            }

            float before = card.Gauge;
            float after = Mathf.Min(card.MaxGauge, before + amount);
            float restored = after - before;
            if (restored <= 0f)
            {
                return 0f;
            }

            card.Gauge = after;
            GaugeChanged?.Invoke(slot, card, before, after);
            return restored;
        }

        /// <summary>
        /// Adds a new copy (card rewards). Active cards go to the discard pile and join the draw
        /// pile at the next reshuffle; passives go straight to <see cref="Passives"/>.
        /// </summary>
        public CardRuntime AddCard(CardDefinition definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            CardRuntime card = CreateRuntime(definition);
            if (!card.IsActive)
            {
                passives.Add(card);
                return card;
            }

            discardPile.Add(card);
            FillEmptySlots(); // a deck smaller than the hand picks the new card up right away
            return card;
        }

        /// <summary>Removes a card from the run (shop / event). A card in hand frees its slot, which draws a replacement.</summary>
        public bool RemoveCard(int instanceId)
        {
            for (int slot = 0; slot < hand.Length; slot++)
            {
                if (hand[slot] != null && hand[slot].InstanceId == instanceId)
                {
                    hand[slot] = null;
                    DrawCard(slot);
                    return true;
                }
            }

            return RemoveFrom(drawPile, instanceId) || RemoveFrom(discardPile, instanceId) || RemoveFrom(passives, instanceId);
        }

        /// <summary>Raises the upgrade level. Upgrade tables (cost / max changes) arrive with rewards and shops.</summary>
        public bool UpgradeCard(int instanceId)
        {
            foreach (CardRuntime card in AllCards())
            {
                if (card.InstanceId == instanceId)
                {
                    card.UpgradeLevel++;
                    return true;
                }
            }

            return false;
        }

        public IEnumerable<CardRuntime> AllCards()
        {
            foreach (CardRuntime card in hand)
            {
                if (card != null)
                {
                    yield return card;
                }
            }

            foreach (CardRuntime card in drawPile)
            {
                yield return card;
            }

            foreach (CardRuntime card in discardPile)
            {
                yield return card;
            }

            foreach (CardRuntime card in passives)
            {
                yield return card;
            }
        }

        CardRuntime CreateRuntime(CardDefinition definition) => new CardRuntime(nextInstanceId++, definition);

        static bool RemoveFrom(List<CardRuntime> list, int instanceId)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].InstanceId == instanceId)
                {
                    list.RemoveAt(i);
                    return true;
                }
            }

            return false;
        }
    }
}
