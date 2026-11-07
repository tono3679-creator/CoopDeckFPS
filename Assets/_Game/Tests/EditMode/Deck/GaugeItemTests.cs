using CoopDeck.Deck;
using CoopDeck.Items;
using NUnit.Framework;

namespace CoopDeck.Tests
{
    /// <summary>Gauge items (CARD_SYSTEM.md §5): Charge Potion +40 to one card, Resonance Crystal +25 to each hand card.</summary>
    public class GaugeItemTests
    {
        TestCards cards;
        GaugeItemDefinition potion;
        GaugeItemDefinition crystal;

        [SetUp]
        public void SetUp()
        {
            cards = new TestCards();
            potion = cards.Item("potion", GaugeItemTarget.SelectedCard, 40f);
            crystal = cards.Item("crystal", GaugeItemTarget.AllHandCards, 25f);
        }

        [TearDown]
        public void TearDown() => cards.DestroyAll();

        DeckState Deck(float cost) => new DeckState(cards.Many(12, cost), 3, new SeededRandom(5));

        [Test]
        public void ChargePotion_AddsFortyToTheSelectedCard_NeverAboveMax()
        {
            DeckState deck = Deck(50f);
            var items = new GaugeItemInventory(new[] { potion, crystal });
            deck.TryUseCard(1, out _, out _); // 50

            Assert.That(items.TryUse(0, deck, 1, out ItemUseResult first, out _), Is.True);
            Assert.That(deck.GetHandCard(1).Gauge, Is.EqualTo(90f));
            Assert.That(first.TotalRestored, Is.EqualTo(40f));
            Assert.That(deck.GetHandCard(0).Gauge, Is.EqualTo(100f));
            Assert.That(items.GetCount(potion), Is.EqualTo(1));

            Assert.That(items.TryUse(0, deck, 1, out ItemUseResult second, out _), Is.True);
            Assert.That(second.TotalRestored, Is.EqualTo(10f), "capped at the maximum");
            Assert.That(deck.GetHandCard(1).Gauge, Is.EqualTo(100f));
            Assert.That(items.GetCount(potion), Is.EqualTo(0));
        }

        [Test]
        public void ResonanceCrystal_AddsTwentyFiveToEveryHandCard_Clamped()
        {
            DeckState deck = Deck(40f);
            var items = new GaugeItemInventory(new[] { potion, crystal });
            deck.TryUseCard(0, out _, out _); // 60
            deck.TryUseCard(0, out _, out _); // 20
            deck.TryUseCard(1, out _, out _); // 60

            Assert.That(items.TryUse(1, deck, 0, out ItemUseResult result, out _), Is.True);
            Assert.That(deck.GetHandCard(0).Gauge, Is.EqualTo(45f));
            Assert.That(deck.GetHandCard(1).Gauge, Is.EqualTo(85f));
            Assert.That(deck.GetHandCard(2).Gauge, Is.EqualTo(100f), "already full: unchanged");
            Assert.That(result.CardsAffected, Is.EqualTo(2));
            Assert.That(items.GetCount(crystal), Is.EqualTo(1));
        }

        [Test]
        public void FullTarget_IsRefused_AndTheItemIsKept()
        {
            DeckState deck = Deck(50f);
            var items = new GaugeItemInventory(new[] { potion, crystal });

            Assert.That(items.TryUse(0, deck, 0, out _, out ItemUseFailure failure), Is.False);
            Assert.That(failure, Is.EqualTo(ItemUseFailure.AlreadyFull));
            Assert.That(items.TryUse(1, deck, 0, out _, out failure), Is.False);
            Assert.That(failure, Is.EqualTo(ItemUseFailure.AlreadyFull));
            Assert.That(items.GetCount(potion), Is.EqualTo(2));
            Assert.That(items.GetCount(crystal), Is.EqualTo(2));
        }

        [Test]
        public void DiscardedCard_CannotBeRestored()
        {
            DeckState deck = Deck(100f);
            var items = new GaugeItemInventory(new[] { potion });
            CardRuntime used = deck.GetHandCard(0);
            deck.TryUseCard(0, out _, out _); // 0 → discard pile, a fresh full card takes the slot

            Assert.That(deck.DiscardPile, Does.Contain(used));
            Assert.That(items.TryUse(0, deck, 0, out _, out ItemUseFailure failure), Is.False);
            Assert.That(failure, Is.EqualTo(ItemUseFailure.AlreadyFull), "the slot now holds a full card");
            Assert.That(used.Gauge, Is.EqualTo(0f), "the discarded card was not touched");
        }

        [Test]
        public void EmptySlot_IsRefused()
        {
            var deck = new DeckState(cards.Many(2, 50f), 3, new SeededRandom(1));
            var items = new GaugeItemInventory(new[] { potion });
            Assert.That(items.TryUse(0, deck, 2, out _, out ItemUseFailure failure), Is.False);
            Assert.That(failure, Is.EqualTo(ItemUseFailure.NoCardInSlot));
            Assert.That(items.GetCount(potion), Is.EqualTo(2));
        }

        [Test]
        public void CarryLimit_StockRunsOut_AndSelectionCycles()
        {
            DeckState deck = Deck(10f);
            var items = new GaugeItemInventory(new[] { potion, crystal });
            Assert.That(items.Add(potion, 5), Is.EqualTo(0), "already at the limit of 2");

            for (int i = 0; i < 2; i++)
            {
                deck.TryUseCard(0, out _, out _);
                deck.Tick(1f);
                Assert.That(items.TryUse(0, deck, 0, out _, out _), Is.True);
            }

            deck.TryUseCard(0, out _, out _);
            Assert.That(items.TryUse(0, deck, 0, out _, out ItemUseFailure failure), Is.False);
            Assert.That(failure, Is.EqualTo(ItemUseFailure.OutOfStock));
            Assert.That(items.Add(potion, 5), Is.EqualTo(2));

            Assert.That(items.Selected, Is.SameAs(potion));
            items.CycleSelection();
            Assert.That(items.Selected, Is.SameAs(crystal));
            items.CycleSelection();
            Assert.That(items.Selected, Is.SameAs(potion));
        }
    }
}
