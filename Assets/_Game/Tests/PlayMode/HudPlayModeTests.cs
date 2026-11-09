using System.Collections;
using System.Collections.Generic;
using CoopDeck.Cards;
using CoopDeck.Core;
using CoopDeck.Items;
using CoopDeck.Skills;
using CoopDeck.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace CoopDeck.Tests
{
    /// <summary>
    /// The redesigned combat HUD in play: every region is built, the hand follows the deck
    /// (gauge, depletion slide, the new card), the key labels follow the device in use, and the
    /// notification feed shows posted notices. Uses a bare DeckController (deck rules only).
    /// </summary>
    public class HudPlayModeTests
    {
        readonly List<Object> spawned = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in spawned)
            {
                if (o != null)
                {
                    Object.Destroy(o);
                }
            }

            spawned.Clear();
            InputGlyphs.SetActive(InputScheme.KeyboardMouse);
        }

        CardDefinition Card(string name, Element element, float cost)
        {
            var card = ScriptableObject.CreateInstance<CardDefinition>();
            card.name = name;
            card.cardId = "card.hud_test_" + name;
            card.displayName = name;
            card.element = element;
            card.maxGauge = 100f;
            card.gaugeCost = cost;
            card.cooldown = 0.05f;
            card.effects = new List<SkillEffectSpec>();
            spawned.Add(card);
            return card;
        }

        static Text FindText(Transform root, string cardName, string child)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == cardName)
                {
                    Transform target = t.Find(child);
                    return target != null ? target.GetComponent<Text>() : null;
                }
            }

            return null;
        }

        static bool Has(Transform root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == name)
                {
                    return true;
                }
            }

            return false;
        }

        [UnityTest]
        public IEnumerator CombatHud_BuildsEveryRegion_AndTheHandFollowsTheDeck()
        {
            var cards = new List<CardDefinition>
            {
                Card("火炎テスト", Element.Fire, 50f),
                Card("氷結テスト", Element.Ice, 50f),
                Card("雷光テスト", Element.Lightning, 50f),
                Card("重力テスト", Element.Gravity, 50f),
            };
            var item = ScriptableObject.CreateInstance<GaugeItemDefinition>();
            item.displayName = "テスト薬";
            item.amount = 40f;
            spawned.Add(item);

            var deckGo = new GameObject("TestDeck");
            spawned.Add(deckGo);
            var deck = deckGo.AddComponent<DeckController>();
            deck.Rebuild(cards, 3, new[] { item }, 11);

            var hudGo = new GameObject("TestHud");
            spawned.Add(hudGo);
            var hud = hudGo.AddComponent<HudController>();
            var deckHud = hudGo.AddComponent<DeckHud>();
            deckHud.Bind(deck);
            InputGlyphs.SetActive(InputScheme.KeyboardMouse);

            yield return new WaitForSecondsRealtime(0.6f); // opening deal animation

            foreach (string region in new[] { "Vitals", "Weapon", "NotificationFeed", "Perf", "GuideChip", "Slot1", "Slot2", "Slot3", "ItemSlot", "Piles", "DrawToast" })
            {
                Assert.That(Has(hudGo.transform, region), Is.True, region + " missing");
            }

            Assert.That(deckHud.SlotCount, Is.EqualTo(3));
            Text uses = FindText(hudGo.transform, "Slot1", "Square/Uses");
            Assert.That(uses, Is.Not.Null);
            Text key = FindText(hudGo.transform, "Slot1", "Key/Glyph");
            Assert.That(deckHud.SlotCardName(0), Is.EqualTo(deck.Deck.GetHandCard(0).Definition.displayName));
            Assert.That(uses.text, Is.EqualTo("2"), "50-cost card: two uses");
            Assert.That(key.text, Is.EqualTo("Q"));

            // The labels follow the device in use.
            InputGlyphs.SetActive(InputScheme.Gamepad);
            yield return null;
            Assert.That(key.text, Is.EqualTo("LB"));

            // Use: the gauge text follows the deck.
            Assert.That(deck.UseSlot(0), Is.True);
            yield return null;
            Assert.That(uses.text, Is.EqualTo("1"));

            // Deplete: the card slides away and the drawn card takes the slot.
            yield return new WaitForSecondsRealtime(0.1f);
            Assert.That(deck.UseSlot(0), Is.True);
            yield return new WaitForSecondsRealtime(0.8f);
            Assert.That(deckHud.SlotCardName(0), Is.EqualTo(deck.Deck.GetHandCard(0).Definition.displayName));
            Assert.That(deckHud.ToastName, Is.EqualTo(deck.Deck.GetHandCard(0).Definition.displayName), "the drawn card is announced");
            Assert.That(uses.text, Is.EqualTo("2"));
            Assert.That(deckHud.PilesLine, Does.Contain("捨て札").And.EndWith("<b>1</b></color>"));

            // Notices reach the feed.
            HudNotifications.Post(HudNoticeKind.Revive, "テスト通知");
            yield return null;
            Assert.That(hud.NewestNotice, Is.EqualTo("テスト通知"));
        }

        [UnityTest]
        public IEnumerator KeyLabels_SwitchToTheDeviceThePlayerJustUsed()
        {
            var hudGo = new GameObject("TestHud");
            spawned.Add(hudGo);
            hudGo.AddComponent<HudController>();
            var keyboard = InputSystem.AddDevice<Keyboard>("HudTestKeyboard");
            var gamepad = InputSystem.AddDevice<Gamepad>("HudTestGamepad");
            try
            {
                InputGlyphs.SetActive(InputScheme.KeyboardMouse);
                yield return null;

                InputSystem.QueueStateEvent(gamepad, new GamepadState().WithButton(GamepadButton.South));
                yield return null;
                InputSystem.QueueStateEvent(gamepad, new GamepadState());
                yield return null;
                Assert.That(InputGlyphs.Active, Is.EqualTo(InputScheme.Gamepad));
                Assert.That(InputGlyphs.SkillLabel(0, InputGlyphs.Active), Is.EqualTo("LB"));

                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Q));
                yield return null;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                yield return null;
                Assert.That(InputGlyphs.Active, Is.EqualTo(InputScheme.KeyboardMouse));
                Assert.That(InputGlyphs.SkillLabel(0, InputGlyphs.Active), Is.EqualTo("Q"));
            }
            finally
            {
                InputSystem.RemoveDevice(keyboard);
                InputSystem.RemoveDevice(gamepad);
            }
        }
    }
}
