using System.Collections.Generic;
using CoopDeck.Cards;
using CoopDeck.Core;
using UnityEngine;
using UnityEngine.UI;

namespace CoopDeck.UI
{
    /// <summary>
    /// F8 (Editor / development builds only): a read-only sheet of the shared HUD parts,
    /// including states the game cannot reach yet (原脈同調, legendary, mutation). Clearly
    /// labelled as a preview; nothing on it is interactive.
    /// </summary>
    public static class UiStyleGallery
    {
        public static bool Available => Application.isEditor || Debug.isDebugBuild;

        public static RectTransform Build(Transform parent, IReadOnlyList<CardDefinition> sampleCards)
        {
            RectTransform canvasFrame = HudKit.CreateCanvas(parent, "UI Preview Canvas", 40);
            RectTransform canvas = (RectTransform)canvasFrame.parent;
            Image dim = HudKit.CreateImage("Dim", canvas, UiTokens.WithAlpha(UiTokens.Background, 0.9f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            HudKit.Stretch(dim.rectTransform);
            dim.transform.SetAsFirstSibling();

            PanelView sheet = UiWidgets.Panel("Sheet", canvasFrame, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1780f, 1010f));
            RectTransform r = sheet.Root;
            var top = new Vector2(0.5f, 1f);
            Text title = UiWidgets.Label("Title", r, UiTokens.Heading, UiTokens.TextMain, TextAnchor.MiddleLeft, new Vector2(0f, 1f), new Vector2(560f, -42f),
                new Vector2(1060f, 40f), true);
            title.text = "UI プレビュー　共通部品と未実装の状態";
            Text note = UiWidgets.Label("Note", r, UiTokens.Body, UiTokens.Accent, TextAnchor.MiddleRight, new Vector2(1f, 1f), new Vector2(-380f, -42f),
                new Vector2(700f, 30f), true);
            note.text = "見た目の確認用（操作できません）　F8 で閉じる";

            Section(r, "戦闘HUDのスキル枠（右下）", 84f);
            string[] slotCaptions = { "通常・アイテム対象", "クールダウン中", "ラスト1回・強化 +1・変異", WorldTerms.Specialisation, WorldTerms.Specialisation + "・ゲージ0" };
            for (int i = 0; i < 5; i++)
            {
                CardDefinition def = sampleCards != null && sampleCards.Count > 0 ? sampleCards[i % sampleCards.Count] : null;
                float x = -680f + i * 340f;
                SkillSlotView slot = SkillSlotView.Create("Slot", r);
                slot.Root.anchorMin = slot.Root.anchorMax = top;
                slot.Root.pivot = top;
                slot.Root.anchoredPosition = new Vector2(x, -118f);
                slot.Apply(SampleCard(def, i));
                slot.Tick(0f);
                Text slotCaption = UiWidgets.Label("Caption", r, UiTokens.Caption, UiTokens.TextSub, TextAnchor.MiddleCenter, top, new Vector2(x, -236f),
                    new Vector2(330f, 22f));
                slotCaption.text = slotCaptions[i];
            }

            Section(r, "共通カードフレーム（報酬・交換・図鑑で使う予定）", 270f);
            string[] captions = { "通常", "クールダウン中", "ラスト1回・強化 +1", WorldTerms.Specialisation + "（特化・手札に固定）", WorldTerms.Specialisation + "・ゲージ0（使用不可）" };
            for (int i = 0; i < 5; i++)
            {
                CardDefinition def = sampleCards != null && sampleCards.Count > 0 ? sampleCards[i % sampleCards.Count] : null;
                float x = -680f + i * 340f;
                CardFrameView card = CardFrameView.Create("Card", r, top, top, new Vector2(x, -314f));
                card.FocusTag = "▼ アイテム対象";
                card.ShowSyncLabel = i >= 3;
                card.Apply(SampleCard(def, i));
                card.Tick(0f);
                Text caption = UiWidgets.Label("Caption", r, UiTokens.Caption, UiTokens.TextSub, TextAnchor.MiddleCenter, top, new Vector2(x, -500f),
                    new Vector2(330f, 24f));
                caption.text = captions[i];
            }

            Section(r, "属性アイコン（形でも区別）", 540f);
            for (int e = 0; e <= 4; e++)
            {
                var element = (Element)e;
                float x = -790f + e * 118f;
                UiWidgets.Icon("SocketFill", r, UiSprites.HexFill, UiTokens.WithAlpha(UiTokens.Background, 0.95f), top, new Vector2(x, -606f), 66f);
                UiWidgets.Icon("SocketRing", r, UiSprites.HexRing, UiTokens.ElementColor(element), top, new Vector2(x, -606f), 66f);
                UiWidgets.ElementIcon("Icon", r, element, top, new Vector2(x, -606f), 34f);
                Text label = UiWidgets.Label("Name", r, UiTokens.Caption, UiTokens.ElementText(element), TextAnchor.MiddleCenter, top, new Vector2(x, -654f),
                    new Vector2(110f, 24f), true);
                label.text = ElementStyle.DisplayName(element) + "属性";
            }

            Section(r, "レアリティ（色＋◆の数）", 540f, 120f);
            for (int i = 0; i < 4; i++)
            {
                var rarity = (CardRarity)i;
                float x = 160f + i * 170f;
                for (int p = 0; p < UiTokens.RarityPips(rarity); p++)
                {
                    UiWidgets.Icon("Pip", r, UiSprites.Diamond, UiTokens.RarityColor(rarity), top, new Vector2(x - 21f + p * 14f, -606f), 12f);
                }

                Text label = UiWidgets.Label("Name", r, UiTokens.Caption, UiTokens.RarityColor(rarity), TextAnchor.MiddleCenter, top, new Vector2(x, -636f),
                    new Vector2(160f, 24f), true);
                label.text = UiTokens.RarityName(rarity);
            }

            Section(r, "共通ボタン（通常 / フォーカス / 押下 / 無効）", 700f);
            string[] states = { "出撃", "術式図鑑", "恒久研究", "未実装" };
            for (int i = 0; i < 4; i++)
            {
                TerminalButton button = TerminalButton.Create("Button", r, top, top, new Vector2(-690f + i * 250f, -748f), new Vector2(220f, 44f), states[i], false);
                button.ForceState((TerminalButton.VisualState)i);
            }

            Section(r, "操作ランプ（消灯 / 点灯 / 点滅）", 700f, 120f);
            for (int i = 0; i < 3; i++)
            {
                LampView lamp = UiWidgets.Lamp("Lamp", r, top, new Vector2(160f + i * 90f, -760f), new Vector2(40f, 16f));
                lamp.Set(i > 0);
                if (i == 2)
                {
                    lamp.OnColor = UiTokens.Danger;
                    lamp.Set(true);
                }
            }

            Section(r, "戦闘通知", 820f);
            HudNoticeKind[] kinds = { HudNoticeKind.Objective, HudNoticeKind.Success, HudNoticeKind.Danger, HudNoticeKind.Revive, HudNoticeKind.Coop };
            string[] texts = { "WAVE 2 開始 — 敵の反応 4", "WAVE 2 突破", "ヴォルト がダウン — 近くに立って蘇生", "ヴォルト を蘇生しました", "協力コンボ：超伝導バースト" };
            for (int i = 0; i < kinds.Length; i++)
            {
                float x = -560f + (i % 3) * 600f;
                float y = -870f - (i / 3) * 48f;
                Color color = HudNotifications.KindColor(kinds[i]);
                PanelView row = UiWidgets.Panel("Notice", r, top, top, new Vector2(x, y), new Vector2(560f, 40f), UiSprites.FrameStyle.Small, false,
                    UiTokens.WithAlpha(UiTokens.Background, 0.86f), Color.Lerp(UiTokens.FrameSteel, color, 0.5f));
                HudKit.CreateImage("KindBar", row.Root, color, new Vector2(0f, 0.5f), new Vector2(7f, 0f), new Vector2(4f, 26f));
                Text t = UiWidgets.Label("Text", row.Root, UiTokens.Body, UiTokens.TextMain, TextAnchor.MiddleLeft, new Vector2(0f, 0.5f), new Vector2(290f, 0f),
                    new Vector2(520f, 30f), true);
                t.text = texts[i];
            }

            return canvas;
        }

        static void Section(RectTransform parent, string text, float y, float x = -820f)
        {
            Text label = UiWidgets.Label("Section", parent, UiTokens.Body, UiTokens.Accent, TextAnchor.MiddleLeft, new Vector2(0.5f, 1f),
                new Vector2(x + 300f, -y), new Vector2(600f, 28f), true);
            label.text = text;
        }

        static CardViewData SampleCard(CardDefinition def, int index)
        {
            Element[] elements = { Element.Fire, Element.Ice, Element.Lightning, Element.Gravity, Element.Gravity };
            CardRarity[] rarities = { CardRarity.Common, CardRarity.Rare, CardRarity.Epic, CardRarity.Legendary, CardRarity.Legendary };
            var data = new CardViewData
            {
                HasCard = true,
                Name = def != null ? def.displayName : "サンプル",
                Icon = def != null ? def.icon : null,
                Element = def != null ? def.element : elements[index],
                Rarity = rarities[index],
                MaxGauge = 100f,
                Cost = 25f,
                UsesFromFull = 4,
                Cooldown = 1f,
                KeyLabel = InputGlyphs.SkillLabel(index % 3, InputGlyphs.Active),
            };

            switch (index)
            {
                case 0:
                    data.Gauge = 100f;
                    data.UsesLeft = 4;
                    data.Selected = true;
                    break;
                case 1:
                    data.Gauge = 50f;
                    data.UsesLeft = 2;
                    data.CooldownRemaining = 0.6f;
                    break;
                case 2:
                    data.Gauge = 20f;
                    data.UsesLeft = 1;
                    data.UpgradeLevel = 1;
                    data.Mutated = true;
                    break;
                case 3:
                    data.Gauge = 75f;
                    data.UsesLeft = 3;
                    data.Specialized = true;
                    break;
                default:
                    data.Gauge = 0f;
                    data.UsesLeft = 0;
                    data.Specialized = true;
                    break;
            }

            return data;
        }
    }
}
