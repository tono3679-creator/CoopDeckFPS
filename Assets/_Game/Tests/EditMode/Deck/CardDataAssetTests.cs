using System.Linq;
using CoopDeck.Cards;
using CoopDeck.Items;
using CoopDeck.Skills;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CoopDeck.Tests
{
    /// <summary>Checks the generated card data (run CoopDeck > Setup > Run Phase 1 Bootstrap first).</summary>
    public class CardDataAssetTests
    {
        const string DeckPath = "Assets/_Game/ScriptableObjects/Cards/Decks/DD_Starter.asset";
        const string PlayerPrefabPath = "Assets/_Game/Prefabs/Characters/PF_Player.prefab";

        static DeckDefinition LoadDeck()
        {
            var deck = AssetDatabase.LoadAssetAtPath<DeckDefinition>(DeckPath);
            Assert.That(deck, Is.Not.Null, "starter deck missing: run the bootstrap");
            return deck;
        }

        [Test]
        public void StarterDeck_HasThirteenDistinctActiveCards_AndThreeSlots()
        {
            DeckDefinition deck = LoadDeck();
            Assert.That(deck.cards.Count, Is.EqualTo(13), "the 12 starting cards and the fireball");
            Assert.That(deck.handSize, Is.EqualTo(3));
            Assert.That(deck.cards.All(c => c != null && c.type == CardType.Active));
            Assert.That(deck.cards.Select(c => c.cardId).Distinct().Count(), Is.EqualTo(13));
            Assert.That(deck.cards.Select(c => c.cardId), Does.Contain("card.fireball"));
        }

        [Test]
        public void StarterDeck_ContainsTheEightCardsFromTheDesignDoc()
        {
            string[] specified =
            {
                "card.ice_lance", "card.chain_lightning", "card.flame_vortex", "card.gravity_collapse",
                "card.meteor_strike", "card.shield", "card.area_heal", "card.mark_target",
            };
            var ids = LoadDeck().cards.Select(c => c.cardId).ToList();
            foreach (string id in specified)
            {
                Assert.That(ids, Does.Contain(id));
            }
        }

        [Test]
        public void EveryCard_IsPlayable_AndHasPresentation()
        {
            foreach (CardDefinition card in LoadDeck().cards)
            {
                string label = card.cardId;
                Assert.That(card.maxGauge, Is.EqualTo(100f), label);
                Assert.That(card.gaugeCost, Is.InRange(1f, 100f), label);
                Assert.That(card.UsesFromFull, Is.GreaterThanOrEqualTo(1), label);
                Assert.That(card.effects, Is.Not.Empty, label);
                Assert.That(card.icon, Is.Not.Null, label + " icon");
                Assert.That(card.impactFx, Is.Not.Null, label + " impact fx");
                Assert.That(string.IsNullOrWhiteSpace(card.displayName), Is.False, label);
                Assert.That(string.IsNullOrWhiteSpace(card.description), Is.False, label);

                if (card.HasEffect(SkillEffectKind.Damage))
                {
                    Assert.That(card.targeting, Is.EqualTo(CardTargeting.Aim), label + ": single-target damage needs Aim");
                }

                if (card.MaxRadius > 0f)
                {
                    Assert.That(card.areaFx, Is.Not.Null, label + " area fx");
                }

                if (card.impactDelay > 0f)
                {
                    Assert.That(card.telegraphFx, Is.Not.Null, label + " telegraph");
                }
            }
        }

        [Test]
        public void ChainLightning_MatchesTheDesignDocExample()
        {
            CardDefinition chain = LoadDeck().cards.First(c => c.cardId == "card.chain_lightning");
            Assert.That(chain.gaugeCost, Is.EqualTo(40f));
            Assert.That(chain.UsesFromFull, Is.EqualTo(3), "100 → 60 → 20 → 0");
        }

        [Test]
        public void AllSevenCommonEffectKinds_AreUsedByTheStarterDeck()
        {
            var kinds = LoadDeck().cards.SelectMany(c => c.effects).Select(e => e.kind).Distinct().ToList();
            SkillEffectKind[] required =
            {
                SkillEffectKind.Damage, SkillEffectKind.AreaDamage, SkillEffectKind.DamageOverTime, SkillEffectKind.Heal,
                SkillEffectKind.Shield, SkillEffectKind.DamageBuff, SkillEffectKind.Pull,
            };
            foreach (SkillEffectKind kind in required)
            {
                Assert.That(kinds, Does.Contain(kind));
            }
        }

        [Test]
        public void PlayerPrefab_HasDeckWithStarterDeckAndBothItems()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            Assert.That(prefab, Is.Not.Null);
            var controller = prefab.GetComponent<DeckController>();
            Assert.That(controller, Is.Not.Null);
            Assert.That(prefab.GetComponent<SkillCaster>(), Is.Not.Null);

            var so = new SerializedObject(controller);
            Assert.That(so.FindProperty("startingDeck").objectReferenceValue, Is.SameAs(LoadDeck()));
            SerializedProperty items = so.FindProperty("startingItems");
            Assert.That(items.arraySize, Is.EqualTo(2));
            var first = (GaugeItemDefinition)items.GetArrayElementAtIndex(0).objectReferenceValue;
            var second = (GaugeItemDefinition)items.GetArrayElementAtIndex(1).objectReferenceValue;
            Assert.That(first.target, Is.EqualTo(GaugeItemTarget.SelectedCard));
            Assert.That(first.amount, Is.EqualTo(40f));
            Assert.That(second.target, Is.EqualTo(GaugeItemTarget.AllHandCards));
            Assert.That(second.amount, Is.EqualTo(25f));
            Assert.That(first.maxCarry, Is.EqualTo(2));
            Assert.That(second.maxCarry, Is.EqualTo(2));
        }
    }
}
