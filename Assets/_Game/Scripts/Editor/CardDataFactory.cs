using System.Collections.Generic;
using CoopDeck.Cards;
using CoopDeck.Core;
using CoopDeck.Items;
using UnityEditor;
using UnityEngine;

namespace CoopDeck.EditorTools
{
    /// <summary>
    /// Creates the 12 starting card definitions, the 12-card starter deck and the two gauge items.
    /// Existing assets keep their tuning: only missing icon / effect references are filled in.
    /// The table below is the single source for the initial numbers (mirrored in CARD_SYSTEM.md).
    /// </summary>
    public static class CardDataFactory
    {
        public const string CardFolder = GamePaths.Root + "/ScriptableObjects/Cards";
        public const string DeckPath = CardFolder + "/Decks/DD_Starter.asset";
        public const string ItemFolder = GamePaths.Root + "/ScriptableObjects/Items";
        public const string IconFolder = GamePaths.Root + "/Art/UI/CardIcons";

        sealed class CardSpec
        {
            public string AssetName;
            public string Id;
            public string Name;
            public string Description;
            public Element Element;
            public CardRarity Rarity;
            public float Cost;
            public float Cooldown;
            public CardTargeting Targeting;
            public float Range = 50f;
            public float Delay;
            public CardGlyph Glyph;
            public SkillEffectSpec[] Effects;
        }

        static readonly CardSpec[] Specs =
        {
            new CardSpec
            {
                AssetName = "CD_IceLance", Id = "card.ice_lance", Name = "氷の槍", Element = Element.Ice, Glyph = CardGlyph.Spear,
                Cost = 25f, Cooldown = 0.6f, Targeting = CardTargeting.Aim, Range = 60f,
                Description = "照準先の敵1体に氷の槍を放つ（55ダメージ）。",
                Effects = new[] { SkillEffectSpec.Damage(55f) },
            },
            new CardSpec
            {
                AssetName = "CD_ChainLightning", Id = "card.chain_lightning", Name = "連鎖雷撃", Element = Element.Lightning, Glyph = CardGlyph.Bolt,
                Rarity = CardRarity.Rare, Cost = 40f, Cooldown = 1f, Targeting = CardTargeting.Aim, Range = 50f,
                Description = "敵1体に40ダメージ。近くの敵へ最大3回連鎖する（連鎖先は75%）。",
                Effects = new[] { SkillEffectSpec.Damage(40f, chainCount: 3, chainRadius: 9f) },
            },
            new CardSpec
            {
                AssetName = "CD_FlameVortex", Id = "card.flame_vortex", Name = "火炎旋風", Element = Element.Fire, Glyph = CardGlyph.Vortex,
                Cost = 34f, Cooldown = 1.5f, Targeting = CardTargeting.AimPoint, Range = 35f,
                Description = "着弾点の半径4mに30ダメージ＋炎上（毎秒8、3秒）。",
                Effects = new[] { SkillEffectSpec.Area(30f, 4f), SkillEffectSpec.DamageOverTime(8f, 3f, 4f) },
            },
            new CardSpec
            {
                AssetName = "CD_GravityCollapse", Id = "card.gravity_collapse", Name = "重力崩壊", Element = Element.Gravity, Glyph = CardGlyph.Collapse,
                Rarity = CardRarity.Rare, Cost = 50f, Cooldown = 2.5f, Targeting = CardTargeting.AimPoint, Range = 35f,
                Description = "半径8mの敵を中心へ引き寄せ（1.2秒）、20ダメージ。",
                Effects = new[] { SkillEffectSpec.PullTo(8f, 7f, 1.2f), SkillEffectSpec.Area(20f, 8f) },
            },
            new CardSpec
            {
                AssetName = "CD_MeteorStrike", Id = "card.meteor_strike", Name = "メテオストライク", Element = Element.Fire, Glyph = CardGlyph.Meteor,
                Rarity = CardRarity.Rare, Cost = 100f, Cooldown = 0f, Targeting = CardTargeting.AimPoint, Range = 45f, Delay = 0.9f,
                Description = "0.9秒後、着弾点の半径5mに130ダメージ。1回で使い切る大技。",
                Effects = new[] { SkillEffectSpec.Area(130f, 5f) },
            },
            new CardSpec
            {
                AssetName = "CD_Shield", Id = "card.shield", Name = "シールド", Element = Element.None, Glyph = CardGlyph.Shield,
                Cost = 50f, Cooldown = 4f, Targeting = CardTargeting.Self,
                Description = "自分に60のシールドを8秒間張る（ダメージを先に吸収）。",
                Effects = new[] { SkillEffectSpec.Shield(60f, 8f) },
            },
            new CardSpec
            {
                AssetName = "CD_AreaHeal", Id = "card.area_heal", Name = "範囲回復", Element = Element.None, Glyph = CardGlyph.Cross,
                Cost = 50f, Cooldown = 3f, Targeting = CardTargeting.AreaAroundSelf,
                Description = "自分と半径8mの味方のHPを45回復。",
                Effects = new[] { SkillEffectSpec.Heal(45f, 8f) },
            },
            new CardSpec
            {
                AssetName = "CD_MarkTarget", Id = "card.mark_target", Name = "敵のマーキング", Element = Element.None, Glyph = CardGlyph.Crosshair,
                Cost = 34f, Cooldown = 1f, Targeting = CardTargeting.Aim, Range = 60f,
                Description = "照準先の敵1体をマーク：8秒間、受けるダメージ+30%。",
                Effects = new[] { SkillEffectSpec.Mark(0.3f, 8f) },
            },
            new CardSpec
            {
                AssetName = "CD_FrostNova", Id = "card.frost_nova", Name = "フロストノヴァ", Element = Element.Ice, Glyph = CardGlyph.Nova,
                Cost = 34f, Cooldown = 1.2f, Targeting = CardTargeting.AreaAroundSelf,
                Description = "自分の周囲6mに冷気を放ち35ダメージ。",
                Effects = new[] { SkillEffectSpec.Area(35f, 6f) },
            },
            new CardSpec
            {
                AssetName = "CD_ThunderCharge", Id = "card.thunder_charge", Name = "サンダーチャージ", Element = Element.Lightning, Glyph = CardGlyph.ArrowUp,
                Cost = 50f, Cooldown = 6f, Targeting = CardTargeting.Self,
                Description = "8秒間、射撃とスキルのダメージ+30%。",
                Effects = new[] { SkillEffectSpec.Buff(0.3f, 8f) },
            },
            new CardSpec
            {
                AssetName = "CD_Incendiary", Id = "card.incendiary", Name = "焼夷グレネード", Element = Element.Fire, Glyph = CardGlyph.Grenade,
                Cost = 34f, Cooldown = 1.5f, Targeting = CardTargeting.AimPoint, Range = 30f,
                Description = "着弾点の半径4mの敵を炎上させる（毎秒12、4秒）。",
                Effects = new[] { SkillEffectSpec.DamageOverTime(12f, 4f, 4f) },
            },
            new CardSpec
            {
                AssetName = "CD_GravitySpike", Id = "card.gravity_spike", Name = "グラビティスパイク", Element = Element.Gravity, Glyph = CardGlyph.Spike,
                Cost = 25f, Cooldown = 0.8f, Targeting = CardTargeting.Aim, Range = 50f,
                Description = "敵1体に35ダメージ。周囲6mの敵をその地点へ引き寄せる。",
                Effects = new[] { SkillEffectSpec.Damage(35f), SkillEffectSpec.PullTo(6f, 6f, 0.6f) },
            },
        };

        public static List<CardDefinition> EnsureCards(SkillFxSet fx, bool overwriteIcons)
        {
            EditorUtil.EnsureFolder(CardFolder);
            var cards = new List<CardDefinition>();
            foreach (CardSpec spec in Specs)
            {
                string path = $"{CardFolder}/{spec.AssetName}.asset";
                var card = AssetDatabase.LoadAssetAtPath<CardDefinition>(path);
                if (card == null)
                {
                    card = ScriptableObject.CreateInstance<CardDefinition>();
                    card.cardId = spec.Id;
                    card.displayName = spec.Name;
                    card.description = spec.Description;
                    card.type = CardType.Active;
                    card.element = spec.Element;
                    card.rarity = spec.Rarity;
                    card.maxGauge = 100f;
                    card.gaugeCost = spec.Cost;
                    card.cooldown = spec.Cooldown;
                    card.targeting = spec.Targeting;
                    card.range = spec.Range;
                    card.impactDelay = spec.Delay;
                    card.effects = new List<SkillEffectSpec>(spec.Effects);
                    AssetDatabase.CreateAsset(card, path);
                }

                string iconPath = $"{IconFolder}/ICON_{spec.AssetName.Substring(3)}.png";
                Sprite icon = CardIconPainter.EnsureIcon(iconPath, spec.Glyph, ElementStyle.ColorOf(spec.Element), overwriteIcons);
                if (card.icon == null || overwriteIcons) card.icon = icon;

                Element element = card.element;
                bool aimed = card.targeting == CardTargeting.Aim || card.targeting == CardTargeting.AimPoint;
                bool support = card.HasEffect(SkillEffectKind.Heal) || card.HasEffect(SkillEffectKind.Shield) || card.HasEffect(SkillEffectKind.DamageBuff);
                if (card.beamFx == null && aimed) card.beamFx = fx.Beam(element);
                if (card.impactFx == null) card.impactFx = fx.Burst(element);
                if (card.areaFx == null && card.MaxRadius > 0f) card.areaFx = fx.Area(element);
                if (card.auraFx == null && support) card.auraFx = fx.Aura(element);
                if (card.telegraphFx == null && card.impactDelay > 0f) card.telegraphFx = fx.Telegraph;

                EditorUtility.SetDirty(card);
                cards.Add(card);
            }

            AssetDatabase.SaveAssets();
            return cards;
        }

        /// <summary>The 12-card starter deck: one of each starting card. Kept as-is once it exists.</summary>
        public static DeckDefinition EnsureStarterDeck(List<CardDefinition> cards)
        {
            EditorUtil.EnsureFolder(System.IO.Path.GetDirectoryName(DeckPath)?.Replace('\\', '/'));
            var deck = AssetDatabase.LoadAssetAtPath<DeckDefinition>(DeckPath);
            if (deck == null)
            {
                deck = ScriptableObject.CreateInstance<DeckDefinition>();
                deck.deckId = "deck.starter";
                deck.handSize = 3;
                deck.cards = new List<CardDefinition>(cards);
                AssetDatabase.CreateAsset(deck, DeckPath);
                AssetDatabase.SaveAssets();
            }

            return deck;
        }

        public static List<GaugeItemDefinition> EnsureGaugeItems()
        {
            EditorUtil.EnsureFolder(ItemFolder);
            return new List<GaugeItemDefinition>
            {
                EnsureItem("GI_ChargePotion", "item.charge_potion", "チャージポーション", "選択中のカードのゲージを40回復。",
                    GaugeItemTarget.SelectedCard, 40f, new Color(0.45f, 0.9f, 1f)),
                EnsureItem("GI_ResonanceCrystal", "item.resonance_crystal", "共鳴結晶", "手札すべてのカードのゲージを25ずつ回復。",
                    GaugeItemTarget.AllHandCards, 25f, new Color(0.85f, 0.6f, 1f)),
            };
        }

        static GaugeItemDefinition EnsureItem(string assetName, string id, string name, string description, GaugeItemTarget target, float amount, Color color)
        {
            string path = $"{ItemFolder}/{assetName}.asset";
            var item = AssetDatabase.LoadAssetAtPath<GaugeItemDefinition>(path);
            if (item == null)
            {
                item = ScriptableObject.CreateInstance<GaugeItemDefinition>();
                item.itemId = id;
                item.displayName = name;
                item.description = description;
                item.target = target;
                item.amount = amount;
                item.maxCarry = 2;
                item.startCount = 2;
                item.color = color;
                AssetDatabase.CreateAsset(item, path);
                AssetDatabase.SaveAssets();
            }

            return item;
        }
    }
}
