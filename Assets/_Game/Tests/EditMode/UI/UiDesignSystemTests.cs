using System.Collections.Generic;
using CoopDeck.Cards;
using CoopDeck.Core;
using CoopDeck.Deck;
using CoopDeck.UI;
using NUnit.Framework;
using UnityEngine;

namespace CoopDeck.Tests
{
    /// <summary>
    /// The HUD design system (Docs/UI_DESIGN.md): colour tokens and contrast, the layout at every
    /// supported aspect, card view states (incl. 原脈同調), notifications, glyphs and icon shapes.
    /// </summary>
    public class UiDesignSystemTests
    {
        static readonly Color BrightSky = Palette.SkyHorizon;
        static readonly Color Sand = Palette.Floor;

        // ------------------------------------------------------------ tokens

        [Test]
        public void Tokens_MatchTheSpecifiedPalette()
        {
            AssertHex(UiTokens.Background, 0x151E2B);
            AssertHex(UiTokens.Panel, 0x293848);
            AssertHex(UiTokens.TextMain, 0xF3F0E8);
            AssertHex(UiTokens.TextSub, 0xA7B8C7);
            AssertHex(UiTokens.Accent, 0xE8AC54);
            AssertHex(UiTokens.Fire, 0xF77B40);
            AssertHex(UiTokens.Ice, 0x9BDEFF);
            AssertHex(UiTokens.Lightning, 0x59D7F1);
            AssertHex(UiTokens.Gravity, 0xAC86EF);
        }

        [Test]
        public void TextTokens_ReachAA_OnPanels_EvenOverTheBrightestScenery()
        {
            Color[] texts = { UiTokens.TextMain, UiTokens.TextSub, UiTokens.Accent, UiTokens.DangerText };
            Color[] scenery = { BrightSky, Sand, Color.white, Color.black };
            foreach (Color text in texts)
            {
                Assert.That(UiTokens.ContrastRatio(text, UiTokens.Panel), Is.GreaterThanOrEqualTo(4.5f));
                Assert.That(UiTokens.ContrastRatio(text, UiTokens.Background), Is.GreaterThanOrEqualTo(4.5f));
                foreach (Color behind in scenery)
                {
                    // Panels are translucent: check the colour the player actually sees.
                    Color panel = UiTokens.Over(UiTokens.PanelTranslucent, behind);
                    Color deep = UiTokens.Over(UiTokens.PanelDeepTranslucent, behind);
                    Assert.That(UiTokens.ContrastRatio(text, panel), Is.GreaterThanOrEqualTo(4.5f), $"{text} over {behind}");
                    Assert.That(UiTokens.ContrastRatio(text, deep), Is.GreaterThanOrEqualTo(4.5f), $"{text} over {behind} (deep)");
                }
            }
        }

        [Test]
        public void ElementAndRarityText_ReachAA_OnCards()
        {
            Color card = UiTokens.Over(UiTokens.PanelDeepTranslucent, BrightSky);
            for (int e = 0; e <= 4; e++)
            {
                Assert.That(UiTokens.ContrastRatio(UiTokens.ElementText((Element)e), card), Is.GreaterThanOrEqualTo(4.5f), ((Element)e).ToString());
            }

            for (int r = 0; r <= 3; r++)
            {
                Assert.That(UiTokens.ContrastRatio(UiTokens.RarityColor((CardRarity)r), card), Is.GreaterThanOrEqualTo(4.5f), ((CardRarity)r).ToString());
            }

            Assert.That(UiTokens.ContrastRatio(UiTokens.SyncGold, card), Is.GreaterThanOrEqualTo(4.5f));
            Assert.That(UiTokens.ContrastRatio(UiTokens.DangerText, card), Is.GreaterThanOrEqualTo(4.5f));
        }

        [Test]
        public void RarityColours_AreDistinctFromElementColours()
        {
            Color[] elements = { UiTokens.Fire, UiTokens.Ice, UiTokens.Lightning, UiTokens.Gravity };
            Color[] saturated = { UiTokens.Rare, UiTokens.Epic, UiTokens.Legendary };
            foreach (Color rarity in saturated)
            {
                Color.RGBToHSV(rarity, out float rh, out _, out _);
                foreach (Color element in elements)
                {
                    Color.RGBToHSV(element, out float eh, out _, out _);
                    float distance = Mathf.Abs(Mathf.DeltaAngle(rh * 360f, eh * 360f));
                    Assert.That(distance, Is.GreaterThanOrEqualTo(18f), $"rarity {rarity} vs element {element}");
                }
            }

            // Common is a neutral grey: low saturation, so it never reads as an element.
            Color.RGBToHSV(UiTokens.Common, out _, out float cs, out _);
            Assert.That(cs, Is.LessThan(0.15f));
            // And rarity never relies on colour alone.
            Assert.That(UiTokens.RarityPips(CardRarity.Common), Is.EqualTo(1));
            Assert.That(UiTokens.RarityPips(CardRarity.Legendary), Is.EqualTo(4));
        }

        // ------------------------------------------------------------ layout

        /// <summary>Regions that are on screen all the time (the HUD's footprint).</summary>
        static IEnumerable<KeyValuePair<string, Rect>> PersistentRegions()
        {
            yield return new KeyValuePair<string, Rect>("Vitals", HudLayout.Vitals);
            yield return new KeyValuePair<string, Rect>("Squad", HudLayout.Squad);
            yield return new KeyValuePair<string, Rect>("Weapon", HudLayout.Weapon);
            yield return new KeyValuePair<string, Rect>("ItemSlot", HudLayout.ItemSlot);
            yield return new KeyValuePair<string, Rect>("Piles", HudLayout.Piles);
            yield return new KeyValuePair<string, Rect>("Objective", HudLayout.Objective);
            yield return new KeyValuePair<string, Rect>("GuideChip", HudLayout.GuideChip);
            for (int i = 0; i < 3; i++)
            {
                Rect slot = HudLayout.SkillSlot(i, 3);
                slot.yMax += HudLayout.SlotArrow; // the item-target arrow above the slot
                yield return new KeyValuePair<string, Rect>("Slot" + i, slot);
            }
        }

        static IEnumerable<KeyValuePair<string, Rect>> Regions()
        {
            foreach (KeyValuePair<string, Rect> region in PersistentRegions())
            {
                yield return region;
            }

            for (int ally = 1; ally < 3; ally++)
            {
                Rect squad = HudLayout.Squad;
                squad.y += ally * HudLayout.SquadStep;
                yield return new KeyValuePair<string, Rect>("Squad" + ally, squad);
            }

            yield return new KeyValuePair<string, Rect>("Log", HudLayout.Log);
            yield return new KeyValuePair<string, Rect>("DeckNotice", HudLayout.DeckNotice);
            yield return new KeyValuePair<string, Rect>("DrawToast", HudLayout.DrawToast);
            yield return new KeyValuePair<string, Rect>("Perf", HudLayout.Perf);
            yield return new KeyValuePair<string, Rect>("ComboBanner", HudLayout.ComboBanner);
        }

        [Test]
        public void Layout_IsCompact_TheHudCoversLittleOfTheScreen()
        {
            float area = 0f;
            foreach (KeyValuePair<string, Rect> region in PersistentRegions())
            {
                area += region.Value.width * region.Value.height;
            }

            float share = area / (HudLayout.Width * HudLayout.Height);
            Assert.That(share, Is.LessThan(0.08f), $"HUD covers {share:P1} of the screen");
            Assert.That(HudLayout.SlotSize, Is.LessThanOrEqualTo(80f));
            Assert.That(UiTokens.Body, Is.LessThanOrEqualTo(16));
        }

        [Test]
        public void Layout_RegionsFitTheFrame_DoNotOverlap_AndKeepTheCrosshairClear()
        {
            var frame = new Rect(0f, 0f, HudLayout.Width, HudLayout.Height);
            var list = new List<KeyValuePair<string, Rect>>(Regions());
            for (int i = 0; i < list.Count; i++)
            {
                Rect a = list[i].Value;
                Assert.That(frame.Contains(a.min) && frame.Contains(a.max), Is.True, $"{list[i].Key} leaves the frame");
                Assert.That(a.Overlaps(HudLayout.CrosshairClear), Is.False, $"{list[i].Key} covers the crosshair area");
                for (int j = i + 1; j < list.Count; j++)
                {
                    Assert.That(a.Overlaps(list[j].Value), Is.False, $"{list[i].Key} overlaps {list[j].Key}");
                }
            }
        }

        [TestCase(1920, 1080)]
        [TestCase(1280, 720)]
        [TestCase(1920, 1200)]
        [TestCase(1680, 1050)]
        [TestCase(2560, 1080)]
        [TestCase(3440, 1440)]
        [TestCase(2560, 1600)]
        public void Layout_EveryAspect_ShowsTheWholeFrame_AtALegibleSize(int width, int height)
        {
            // CanvasScaler "Expand" from 1920×1080: scale = min(w/1920, h/1080).
            float scale = Mathf.Min(width / HudLayout.Width, height / HudLayout.Height);
            var canvas = new Vector2(width / scale, height / scale);
            Assert.That(canvas.x, Is.GreaterThanOrEqualTo(HudLayout.Width - 0.01f));
            Assert.That(canvas.y, Is.GreaterThanOrEqualTo(HudLayout.Height - 0.01f));

            float frameWidth = HudSafeFrame.FrameWidth(canvas, HudSafeFrame.DefaultMaxAspect);
            Assert.That(frameWidth, Is.EqualTo(HudLayout.Width).Within(0.5f), "the safe frame is exactly the reference width");

            // The smallest HUD text on this screen, in physical pixels.
            Assert.That(UiTokens.MinimumFontSize * scale, Is.GreaterThanOrEqualTo(10f));
        }

        [Test]
        public void Layout_TopRegions_AnchorToTheTop_AndSidesToTheirSide()
        {
            Assert.That(HudLayout.AnchorFor(HudLayout.Objective), Is.EqualTo(new Vector2(1f, 1f)));
            Assert.That(HudLayout.AnchorFor(HudLayout.Perf), Is.EqualTo(new Vector2(0f, 1f)));
            Assert.That(HudLayout.AnchorFor(HudLayout.Vitals), Is.EqualTo(new Vector2(0f, 0f)));
            Assert.That(HudLayout.AnchorFor(HudLayout.Weapon), Is.EqualTo(new Vector2(1f, 0f)));
            Assert.That(HudLayout.AnchorFor(HudLayout.SkillSlot(1, 3)), Is.EqualTo(new Vector2(0.5f, 0f)));
            Assert.That(HudLayout.AnchorFor(HudLayout.ItemSlot), Is.EqualTo(new Vector2(0.5f, 0f)));
            Assert.That(HudLayout.AnchorFor(HudLayout.Piles), Is.EqualTo(new Vector2(0.5f, 0f)));
            Assert.That(HudLayout.AnchorFor(HudLayout.DrawToast), Is.EqualTo(new Vector2(0.5f, 0f)));
        }

        [TestCase(1)]
        [TestCase(3)]
        [TestCase(5)]
        public void Layout_CardHand_IsCentredAtTheBottom_AndLeavesTheLowerRightToTheRifle(int count)
        {
            // 2026-11-09 user: move the card row to the middle; at the bottom right it sat on the first-person rifle.
            Rect row = HudLayout.SkillRow(count);
            Assert.That(row.center.x, Is.EqualTo(HudLayout.Width * 0.5f).Within(0.5f), "the hand is centred under the crosshair");
            Assert.That(row.yMin, Is.EqualTo(HudLayout.M).Within(0.5f), "the hand sits on the bottom edge");
            for (int i = 1; i < count; i++)
            {
                Assert.That(HudLayout.SkillSlot(i, count).xMin - HudLayout.SkillSlot(i - 1, count).xMax, Is.EqualTo(HudLayout.SlotGap).Within(0.01f));
            }

            var lowerRight = new Rect(HudLayout.Width * 0.6f, 0f, HudLayout.Width * 0.4f, HudLayout.Height * 0.4f);
            foreach (Rect hand in new[] { HudLayout.SkillRow(3), HudLayout.ItemSlot, HudLayout.Piles })
            {
                Assert.That(hand.Overlaps(lowerRight), Is.False, "the card hand stays out of the rifle's corner");
            }

            Assert.That(HudLayout.DeckNotice.yMin, Is.GreaterThanOrEqualTo(HudLayout.HandTop), "the deck notice sits above the hand");
            Assert.That(HudLayout.DrawToast.yMin, Is.GreaterThanOrEqualTo(HudLayout.DeckNotice.yMax), "the draw toast sits above the notice");
        }

        // ------------------------------------------------------------ card view

        [Test]
        public void CardView_ShowsUsesLastUseAndDepletion_FromTheRuntimeCard()
        {
            var cards = new TestCards();
            try
            {
                CardDefinition def = cards.Card("card.ui_test", 25f, 0.5f);
                def.element = Element.Lightning;
                def.rarity = CardRarity.Epic;
                var deck = new DeckState(new List<CardDefinition> { def }, 3, new SeededRandom(7));
                CardRuntime card = deck.GetHandCard(0);
                Assert.That(card, Is.Not.Null);

                CardViewData full = CardViewData.From(card, "Q", false, false);
                Assert.That(full.HasCard && full.UsesLeft == 4 && full.UsesFromFull == 4, Is.True);
                Assert.That(full.UsesText, Is.EqualTo("あと 4 回"));
                Assert.That(full.Element, Is.EqualTo(Element.Lightning));
                Assert.That(full.Rarity, Is.EqualTo(CardRarity.Epic));
                Assert.That(full.Unusable, Is.False);

                deck.TryUseCard(0, out _, out _);
                deck.Tick(1f);
                deck.TryUseCard(0, out _, out _);
                deck.Tick(1f);
                deck.TryUseCard(0, out _, out _);
                CardViewData last = CardViewData.From(deck.GetHandCard(0), "Q", true, false);
                Assert.That(last.UsesText, Is.EqualTo("ラスト1回"));
                Assert.That(last.Selected, Is.True);

                CardViewData leaving = CardViewData.From(card, "Q", false, true);
                Assert.That(leaving.Gauge, Is.EqualTo(0f));
                Assert.That(leaving.UsesText, Is.EqualTo("使い切り → 捨て札"));

                CardViewData empty = CardViewData.From(null, "E", false, false);
                Assert.That(empty.HasCard, Is.False);
                Assert.That(empty.KeyLabel, Is.EqualTo("E"));
            }
            finally
            {
                cards.DestroyAll();
            }
        }

        [Test]
        public void CardView_Specialised_StaysAtZeroGauge_AndIsShownUnusable()
        {
            var data = new CardViewData { HasCard = true, Name = "x", MaxGauge = 100f, Cost = 25f, Gauge = 0f, Specialized = true };
            Assert.That(data.Unusable, Is.True);
            Assert.That(data.UsesText, Is.EqualTo("ゲージ0・使用不可"));

            data.Gauge = 30f;
            Assert.That(data.Unusable, Is.False);

            data.Gauge = 0f;
            data.Specialized = false; // a normal card at 0 is leaving the hand, not "unusable"
            Assert.That(data.Unusable, Is.False);
        }

        // ------------------------------------------------------------ notifications, glyphs, icons

        [Test]
        public void Notifications_KeepARecentHistory_AndIgnoreEmptyText()
        {
            var received = new List<string>();
            void OnPosted(HudNotice n) => received.Add(n.Text);
            HudNotifications.Posted += OnPosted;
            try
            {
                int before = HudNotifications.History.Count;
                HudNotifications.Post(HudNoticeKind.Info, "");
                Assert.That(HudNotifications.History.Count, Is.EqualTo(before));
                for (int i = 0; i < HudNotifications.HistorySize + 3; i++)
                {
                    HudNotifications.Post(HudNoticeKind.Objective, "n" + i, Element.Fire);
                }

                Assert.That(HudNotifications.History.Count, Is.EqualTo(HudNotifications.HistorySize));
                Assert.That(HudNotifications.History[HudNotifications.History.Count - 1].Text, Is.EqualTo("n" + (HudNotifications.HistorySize + 2)));
                Assert.That(received.Count, Is.EqualTo(HudNotifications.HistorySize + 3));
                Assert.That(HudNotifications.KindColor(HudNoticeKind.Danger), Is.EqualTo(UiTokens.Danger));
            }
            finally
            {
                HudNotifications.Posted -= OnPosted;
            }
        }

        [Test]
        public void Glyphs_FollowTheDefaultBindings()
        {
            Assert.That(InputGlyphs.SkillLabel(0, InputScheme.KeyboardMouse), Is.EqualTo("Q"));
            Assert.That(InputGlyphs.SkillLabel(1, InputScheme.KeyboardMouse), Is.EqualTo("E"));
            Assert.That(InputGlyphs.SkillLabel(2, InputScheme.KeyboardMouse), Is.EqualTo("F"));
            Assert.That(InputGlyphs.SkillLabel(0, InputScheme.Gamepad), Is.EqualTo("LB"));
            Assert.That(InputGlyphs.SkillLabel(1, InputScheme.Gamepad), Is.EqualTo("RB"));
            Assert.That(InputGlyphs.SkillLabel(2, InputScheme.Gamepad), Is.EqualTo("Y"));
            Assert.That(InputGlyphs.Label(HudAction.Reload, InputScheme.Gamepad), Is.EqualTo("X"));
            Assert.That(InputGlyphs.Label(HudAction.UseItem, InputScheme.KeyboardMouse), Is.EqualTo("G"));
        }

        [Test]
        public void ElementIcons_HaveDistinctSilhouettes()
        {
            const int n = 64;
            var masks = new bool[5][];
            for (int e = 0; e <= 4; e++)
            {
                masks[e] = new bool[n * n];
                int filled = 0;
                for (int y = 0; y < n; y++)
                {
                    for (int x = 0; x < n; x++)
                    {
                        bool inside = UiSprites.ElementSilhouette((Element)e, new Vector2(x + 0.5f, y + 0.5f));
                        masks[e][y * n + x] = inside;
                        filled += inside ? 1 : 0;
                    }
                }

                float coverage = filled / (float)(n * n);
                Assert.That(coverage, Is.InRange(0.08f, 0.6f), $"{(Element)e} icon coverage");
            }

            for (int a = 0; a <= 4; a++)
            {
                for (int b = a + 1; b <= 4; b++)
                {
                    int both = 0;
                    int either = 0;
                    for (int i = 0; i < n * n; i++)
                    {
                        both += masks[a][i] && masks[b][i] ? 1 : 0;
                        either += masks[a][i] || masks[b][i] ? 1 : 0;
                    }

                    Assert.That(both / (float)either, Is.LessThan(0.5f), $"{(Element)a} vs {(Element)b} look alike");
                }
            }
        }

        [Test]
        public void VitalsElement_ComesFromTheCharacterInUse()
        {
            var rabbit = ScriptableObject.CreateInstance<CoopDeck.Characters.CharacterDefinition>();
            var player = new GameObject("TestPlayer");
            var bare = new GameObject("TestBare");
            try
            {
                rabbit.Configure("LightningRabbit", "Lightning Rabbit", Element.Lightning, null);
                var swap = player.AddComponent<CoopDeck.Characters.PlayerCharacterSwap>();
                swap.Configure(new[] { rabbit }, null, new GameObject[0], null, null, null, null, 6.5f, null, null, null, null);
                Assert.That(HudController.CharacterElement(player, Element.Fire), Is.EqualTo(Element.Lightning));
                Assert.That(HudController.CharacterElement(bare, Element.Fire), Is.EqualTo(Element.Fire), "no character definition: the fallback");
                Assert.That(HudController.CharacterElement(null, Element.Ice), Is.EqualTo(Element.Ice));
            }
            finally
            {
                Object.DestroyImmediate(player);
                Object.DestroyImmediate(bare);
                Object.DestroyImmediate(rabbit);
            }
        }

        [Test]
        public void FrameSprites_AreNineSliced()
        {
            foreach (UiSprites.FrameStyle style in new[] { UiSprites.FrameStyle.Panel, UiSprites.FrameStyle.Card, UiSprites.FrameStyle.Small })
            {
                Sprite frame = UiSprites.Frame(style);
                Sprite fill = UiSprites.Fill(style);
                Assert.That(frame.border.x, Is.GreaterThan(0f));
                Assert.That(frame.border, Is.EqualTo(fill.border));
                Assert.That(frame.border.x * 2f, Is.LessThan(frame.rect.width));
            }

            Assert.That(UiSprites.FocusBrackets.border.x, Is.GreaterThan(0f));
            Assert.That(UiSprites.ElementIcon(Element.Fire), Is.Not.SameAs(UiSprites.ElementIcon(Element.Ice)));
        }

        static void AssertHex(Color color, uint hex)
        {
            Color expected = Palette.Hex(hex);
            Assert.That(color.r, Is.EqualTo(expected.r).Within(0.002f));
            Assert.That(color.g, Is.EqualTo(expected.g).Within(0.002f));
            Assert.That(color.b, Is.EqualTo(expected.b).Within(0.002f));
        }
    }
}
