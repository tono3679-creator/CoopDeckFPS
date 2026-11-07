using System.Collections.Generic;
using System.Linq;
using CoopDeck.Cards;
using CoopDeck.Deck;
using NUnit.Framework;

namespace CoopDeck.Tests
{
    /// <summary>Card cycle rules from CARD_SYSTEM.md §1 / §4 / §6.</summary>
    public class DeckStateTests
    {
        TestCards cards;

        [SetUp]
        public void SetUp() => cards = new TestCards();

        [TearDown]
        public void TearDown() => cards.DestroyAll();

        static DeckState NewDeck(IEnumerable<CardDefinition> defs, int handSize = 3, int seed = 42) =>
            new DeckState(defs, handSize, new SeededRandom(seed));

        static void AssertInvariant(DeckState deck, int expectedTotal)
        {
            Assert.That(deck.ActiveCardCount, Is.EqualTo(expectedTotal), "card total changed");
            var ids = new List<int>();
            for (int slot = 0; slot < deck.HandSize; slot++)
            {
                if (deck.GetHandCard(slot) != null) ids.Add(deck.GetHandCard(slot).InstanceId);
            }

            ids.AddRange(deck.DrawPile.Select(c => c.InstanceId));
            ids.AddRange(deck.DiscardPile.Select(c => c.InstanceId));
            Assert.That(ids.Count, Is.EqualTo(ids.Distinct().Count()), "a card is in two places at once");
        }

        [Test]
        public void Opening_TwelveCards_DrawsThreeAtFullGauge()
        {
            DeckState deck = NewDeck(cards.Many(12, 25f));

            Assert.That(deck.HandCount, Is.EqualTo(3));
            Assert.That(deck.DrawPileCount, Is.EqualTo(9));
            Assert.That(deck.DiscardPileCount, Is.EqualTo(0));
            for (int slot = 0; slot < 3; slot++)
            {
                Assert.That(deck.GetHandCard(slot).Gauge, Is.EqualTo(100f));
            }

            AssertInvariant(deck, 12);
        }

        [Test]
        public void Use_SpendsEachCardsOwnCost_AndLeavesOtherSlotsAlone()
        {
            var defs = new List<CardDefinition> { cards.Card("a", 25f), cards.Card("b", 40f), cards.Card("c", 10f) };
            DeckState deck = NewDeck(defs);

            for (int slot = 0; slot < 3; slot++)
            {
                CardRuntime card = deck.GetHandCard(slot);
                Assert.That(deck.TryUseCard(slot, out CardUseResult result, out _), Is.True);
                Assert.That(result.GaugeBefore, Is.EqualTo(100f));
                Assert.That(card.Gauge, Is.EqualTo(100f - card.GaugeCost));
                for (int other = slot + 1; other < 3; other++)
                {
                    Assert.That(deck.GetHandCard(other).Gauge, Is.EqualTo(100f), "untouched cards keep their gauge");
                }
            }
        }

        [Test]
        public void ChainLightningExample_ThreeUses_LastOneAtFullPower()
        {
            var defs = new List<CardDefinition> { cards.Card("chain", 40f) };
            defs.AddRange(cards.Many(11, 25f));
            DeckState deck = NewDeck(defs, seed: 1);

            // Find the chain lightning wherever it is and bring it into a slot.
            int slot = FindSlot(deck, "chain");
            while (slot < 0)
            {
                deck.TryUseCard(0, out _, out _);
                slot = FindSlot(deck, "chain");
            }

            CardRuntime chain = deck.GetHandCard(slot);
            float[] expected = { 60f, 20f, 0f };
            for (int use = 0; use < 3; use++)
            {
                Assert.That(deck.TryUseCard(slot, out CardUseResult result, out _), Is.True);
                Assert.That(result.GaugeAfter, Is.EqualTo(expected[use]));
                Assert.That(result.PowerMultiplier, Is.EqualTo(1f), "every use, the last one included, is at full power");
                Assert.That(result.Depleted, Is.EqualTo(use == 2));
            }

            Assert.That(deck.DiscardPile, Does.Contain(chain));
            Assert.That(deck.GetHandCard(slot), Is.Not.SameAs(chain));
        }

        [Test]
        public void Depleted_MovesToDiscard_AndRefillsSlotFromDrawPile()
        {
            DeckState deck = NewDeck(cards.Many(12, 100f));
            CardRuntime first = deck.GetHandCard(0);
            CardRuntime next = deck.NextDraw;
            var depleted = new List<CardRuntime>();
            deck.CardDepleted += (s, c) => depleted.Add(c);

            Assert.That(deck.TryUseCard(0, out CardUseResult result, out _), Is.True);

            Assert.That(result.Depleted, Is.True);
            Assert.That(depleted, Is.EquivalentTo(new[] { first }));
            Assert.That(deck.DiscardPile, Is.EquivalentTo(new[] { first }));
            Assert.That(deck.GetHandCard(0), Is.SameAs(next));
            Assert.That(next.Gauge, Is.EqualTo(100f));
            Assert.That(deck.DrawPileCount, Is.EqualTo(8));
            AssertInvariant(deck, 12);
        }

        [Test]
        public void EmptyDrawPile_ReshufflesDiscardPile()
        {
            DeckState deck = NewDeck(cards.Many(4, 100f));
            int reshuffled = 0;
            deck.Reshuffled += count => reshuffled = count;

            deck.TryUseCard(0, out _, out _); // draws the 4th card, draw pile now empty
            Assert.That(deck.DrawPileCount, Is.EqualTo(0));
            Assert.That(reshuffled, Is.EqualTo(0));

            deck.TryUseCard(1, out _, out _); // no draw pile: the 2 discarded cards are reshuffled
            Assert.That(reshuffled, Is.EqualTo(2));
            Assert.That(deck.HandCount, Is.EqualTo(3));
            Assert.That(deck.DrawPileCount + deck.DiscardPileCount, Is.EqualTo(1));
            AssertInvariant(deck, 4);
        }

        [Test]
        public void RedrawnCard_ComesBackAtFullGauge()
        {
            DeckState deck = NewDeck(cards.Many(3, 60f));
            CardRuntime card = deck.GetHandCard(0);

            deck.TryUseCard(0, out _, out _);
            Assert.That(card.Gauge, Is.EqualTo(40f));
            deck.TryUseCard(0, out _, out _); // 40 < 60: last use, gauge 0 → discard → reshuffle → redraw

            Assert.That(deck.GetHandCard(0), Is.SameAs(card), "the only card left is drawn again");
            Assert.That(card.Gauge, Is.EqualTo(100f));
        }

        [Test]
        public void Cooldown_BlocksReuse_UntilTicked()
        {
            DeckState deck = NewDeck(cards.Many(12, 10f, cooldown: 1f));
            Assert.That(deck.TryUseCard(0, out _, out _), Is.True);
            Assert.That(deck.TryUseCard(0, out _, out CardUseFailure failure), Is.False);
            Assert.That(failure, Is.EqualTo(CardUseFailure.OnCooldown));
            Assert.That(deck.GetHandCard(0).Gauge, Is.EqualTo(90f), "a refused use spends nothing");

            deck.Tick(0.6f);
            Assert.That(deck.TryUseCard(0, out _, out _), Is.False);
            deck.Tick(0.5f);
            Assert.That(deck.TryUseCard(0, out _, out _), Is.True);
            Assert.That(deck.TryUseCard(1, out _, out _), Is.True, "cooldowns are per card");
        }

        [Test]
        public void CopiesOfTheSameCard_KeepSeparateGauges()
        {
            CardDefinition lance = cards.Card("lance", 25f);
            DeckState deck = NewDeck(Enumerable.Repeat(lance, 12));

            deck.TryUseCard(0, out _, out _);
            deck.TryUseCard(0, out _, out _);
            deck.TryUseCard(1, out _, out _);

            Assert.That(deck.GetHandCard(0).Gauge, Is.EqualTo(50f));
            Assert.That(deck.GetHandCard(1).Gauge, Is.EqualTo(75f));
            Assert.That(deck.GetHandCard(2).Gauge, Is.EqualTo(100f));
            Assert.That(deck.AllCards().Select(c => c.InstanceId).Distinct().Count(), Is.EqualTo(12));
            Assert.That(deck.AllCards().All(c => c.Definition == lance));
        }

        [Test]
        public void NoVoluntaryDiscard()
        {
            DeckState deck = NewDeck(cards.Many(12, 25f));
            CardRuntime card = deck.GetHandCard(1);
            deck.TryUseCard(1, out _, out _);

            Assert.That(deck.DiscardOnEmpty(1), Is.False, "a card with gauge left cannot be discarded");
            Assert.That(deck.GetHandCard(1), Is.SameAs(card));
            Assert.That(deck.DiscardPileCount, Is.EqualTo(0));
        }

        [Test]
        public void EventOrder_UsedGaugeDepletedReshuffledDrawn()
        {
            DeckState deck = NewDeck(cards.Many(3, 100f));
            var events = new List<string>();
            deck.CardUsed += r => events.Add("Used");
            deck.GaugeChanged += (s, c, a, b) => events.Add("Gauge");
            deck.CardDepleted += (s, c) => events.Add("Depleted");
            deck.Reshuffled += n => events.Add("Reshuffled");
            deck.CardDrawn += (s, c) => events.Add("Drawn");

            deck.TryUseCard(2, out _, out _);

            Assert.That(events, Is.EqualTo(new[] { "Used", "Gauge", "Depleted", "Reshuffled", "Drawn" }));
        }

        [Test]
        public void RandomPlay_NeverChangesTheCardTotal()
        {
            var defs = new List<CardDefinition>();
            float[] costs = { 25f, 34f, 40f, 50f, 100f, 10f };
            for (int i = 0; i < 12; i++)
            {
                defs.Add(cards.Card($"mix_{i}", costs[i % costs.Length], cooldown: i % 3 == 0 ? 0.5f : 0f));
            }

            DeckState deck = NewDeck(defs, seed: 7);
            var random = new System.Random(99);
            int uses = 0;
            for (int step = 0; step < 4000; step++)
            {
                switch (random.Next(4))
                {
                    case 0:
                    case 1:
                        if (deck.TryUseCard(random.Next(3), out _, out _)) uses++;
                        break;
                    case 2:
                        deck.Tick(0.2f);
                        break;
                    default:
                        deck.RestoreCharge(random.Next(3), 25f);
                        break;
                }

                AssertInvariant(deck, 12);
                Assert.That(deck.HandCount, Is.EqualTo(3));
            }

            Assert.That(uses, Is.GreaterThan(500), "the scenario must actually cycle the deck");
        }

        [Test]
        public void SameSeedAndActions_GiveTheSameDeckOrder()
        {
            List<CardDefinition> defs = cards.Many(12, 50f);
            List<string> RunOnce()
            {
                DeckState deck = NewDeck(defs, seed: 2026);
                var drawn = new List<string>();
                deck.CardDrawn += (s, c) => drawn.Add(c.Definition.cardId);
                for (int i = 0; i < 60; i++)
                {
                    deck.TryUseCard(i % 3, out _, out _);
                }

                return drawn;
            }

            Assert.That(RunOnce(), Is.EqualTo(RunOnce()));
        }

        [Test]
        public void DeckSmallerThanHand_LeavesSlotEmptyWithoutErrors()
        {
            DeckState deck = NewDeck(cards.Many(2, 100f));
            Assert.That(deck.HandCount, Is.EqualTo(2));
            Assert.That(deck.GetHandCard(2), Is.Null);

            Assert.That(deck.TryUseCard(2, out _, out CardUseFailure failure), Is.False);
            Assert.That(failure, Is.EqualTo(CardUseFailure.EmptySlot));
            Assert.That(deck.TryUseCard(0, out _, out _), Is.True);
            Assert.That(deck.HandCount, Is.EqualTo(2), "the used card is reshuffled and drawn again");
            AssertInvariant(deck, 2);
        }

        [Test]
        public void PassiveCards_NeverEnterTheDrawCycle()
        {
            var defs = cards.Many(3, 50f);
            defs.Add(cards.Card("passive_a", 1f, type: CardType.Passive));
            defs.Add(cards.Card("passive_b", 1f, type: CardType.Passive));
            DeckState deck = NewDeck(defs);

            Assert.That(deck.Passives.Count, Is.EqualTo(2));
            Assert.That(deck.ActiveCardCount, Is.EqualTo(3));
            for (int i = 0; i < 20; i++)
            {
                deck.TryUseCard(i % 3, out _, out _);
                for (int slot = 0; slot < 3; slot++)
                {
                    Assert.That(deck.GetHandCard(slot)?.IsActive ?? true, Is.True);
                }
            }
        }

        [Test]
        public void AddCard_GoesToDiscard_RemoveCard_FromHandRefillsSlot()
        {
            DeckState deck = NewDeck(cards.Many(12, 50f));
            CardRuntime added = deck.AddCard(cards.Card("reward", 25f));
            Assert.That(deck.DiscardPile, Does.Contain(added));
            Assert.That(deck.ActiveCardCount, Is.EqualTo(13));

            CardRuntime inHand = deck.GetHandCard(1);
            Assert.That(deck.RemoveCard(inHand.InstanceId), Is.True);
            Assert.That(deck.GetHandCard(1), Is.Not.Null.And.Not.SameAs(inHand));
            Assert.That(deck.ActiveCardCount, Is.EqualTo(12));
            Assert.That(deck.AllCards(), Has.No.Member(inHand));
        }

        [Test]
        public void RestoreCharge_ClampsToMax_AndIgnoresEmptySlots()
        {
            DeckState deck = NewDeck(cards.Many(12, 30f));
            deck.TryUseCard(0, out _, out _); // 70
            Assert.That(deck.RestoreCharge(0, 50f), Is.EqualTo(30f));
            Assert.That(deck.GetHandCard(0).Gauge, Is.EqualTo(100f));
            Assert.That(deck.RestoreCharge(0, 10f), Is.EqualTo(0f));
            Assert.That(deck.RestoreCharge(5, 10f), Is.EqualTo(0f), "invalid slot");
        }

        static int FindSlot(DeckState deck, string cardId)
        {
            for (int slot = 0; slot < deck.HandSize; slot++)
            {
                if (deck.GetHandCard(slot)?.Definition.cardId == cardId)
                {
                    return slot;
                }
            }

            return -1;
        }
    }
}
