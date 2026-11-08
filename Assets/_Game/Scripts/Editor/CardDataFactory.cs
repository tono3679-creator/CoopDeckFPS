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

        /// <summary>
        /// Extra cards that are not in the 12-card starter deck: the NPC ally's cheap lightning finisher.
        /// Same CardDefinition / rules as every other card, so a player could equip it too.
        /// </summary>
        static readonly CardSpec[] NpcSpecs =
        {
            new CardSpec
            {
                AssetName = "CD_Thunderbolt", Id = "card.thunderbolt", Name = "雷撃", Element = Element.Lightning, Glyph = CardGlyph.Bolt,
                Cost = 25f, Cooldown = 0.7f, Targeting = CardTargeting.Aim, Range = 55f,
                Description = "照準先の敵1体に雷を落とす（42ダメージ）。凍結・炎上した敵への追撃に向く。",
                Effects = new[] { SkillEffectSpec.Damage(42f) },
            },
        };

        public static List<CardDefinition> EnsureCards(SkillFxSet fx, bool overwriteIcons)
        {
            EditorUtil.EnsureFolder(CardFolder);
            var cards = new List<CardDefinition>();
            foreach (CardSpec spec in Specs)
            {
                cards.Add(EnsureCard(spec, fx, overwriteIcons));
            }

            AssetDatabase.SaveAssets();
            return cards;
        }

        /// <summary>Cards outside the starter set (NPC finisher). Kept as-is once they exist.</summary>
        public static List<CardDefinition> EnsureNpcCards(SkillFxSet fx, bool overwriteIcons)
        {
            EditorUtil.EnsureFolder(CardFolder);
            var cards = new List<CardDefinition>();
            foreach (CardSpec spec in NpcSpecs)
            {
                cards.Add(EnsureCard(spec, fx, overwriteIcons));
            }

            AssetDatabase.SaveAssets();
            return cards;
        }

        static CardDefinition EnsureCard(CardSpec spec, SkillFxSet fx, bool overwriteIcons)
        {
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
                return card;
            }
        }

        public const string FireballCardPath = CardFolder + "/CD_Fireball.asset";

        /// <summary>
        /// The fox's basic fire skill: a fireball thrown from the left palm (the quick-cast motion launches it at
        /// 0.12 s) that flies at 45 m/s and bursts where it lands: 45 to the enemy hit, 20 within 2.5 m and a burn
        /// (6 per second for 3 s), all through SkillCaster's usual effects (element hit, combos). Numbers are kept
        /// once the asset exists; the effects are refreshed from <paramref name="vfx"/> on every bootstrap.
        /// </summary>
        public static CardDefinition EnsureFireball(FireballVfxSet vfx, bool overwriteIcons)
        {
            EditorUtil.EnsureFolder(CardFolder);
            var card = AssetDatabase.LoadAssetAtPath<CardDefinition>(FireballCardPath);
            if (card == null)
            {
                card = ScriptableObject.CreateInstance<CardDefinition>();
                card.cardId = "card.fireball";
                card.displayName = "火球";
                card.description = "左手から火球を放つ。命中で45ダメージ、半径2.5mに20ダメージ＋炎上（毎秒6、3秒）。";
                card.type = CardType.Active;
                card.element = Element.Fire;
                card.rarity = CardRarity.Common;
                card.maxGauge = 100f;
                card.gaugeCost = 25f;
                card.cooldown = 0.5f;
                card.targeting = CardTargeting.Aim;
                card.range = 60f;
                card.effects = new List<SkillEffectSpec>
                {
                    SkillEffectSpec.Damage(45f),
                    SkillEffectSpec.Area(20f, FireballVfxFactory.BlastRadius),
                    SkillEffectSpec.DamageOverTime(6f, 3f, FireballVfxFactory.BlastRadius),
                };
                AssetDatabase.CreateAsset(card, FireballCardPath);
            }

            if (!card.projectile.IsProjectile)
            {
                card.projectile = new ProjectileSpec { speed = 45f, radius = 0.2f, launchDelay = 0.12f, visualScale = 1f };
            }

            Sprite icon = CardIconPainter.EnsureIcon($"{IconFolder}/ICON_Fireball.png", CardGlyph.Fireball, ElementStyle.ColorOf(Element.Fire), overwriteIcons);
            if (card.icon == null || overwriteIcons) card.icon = icon;

            card.castFx = vfx.Cast;
            card.projectileFx = vfx.Projectile;
            card.areaFx = vfx.Explosion;
            card.impactFx = vfx.TargetFlare;
            card.attachImpactFx = true;
            card.beamFx = null;
            card.auraFx = null;
            card.telegraphFx = null;
            EditorUtility.SetDirty(card);
            AssetDatabase.SaveAssets();
            return card;
        }

        /// <summary>Adds a card to the existing starter deck once (the deck keeps its other cards and order).</summary>
        public static void EnsureInStarterDeck(DeckDefinition deck, CardDefinition card)
        {
            if (deck == null || card == null || deck.cards.Contains(card))
            {
                return;
            }

            deck.cards.Add(card);
            EditorUtility.SetDirty(deck);
            AssetDatabase.SaveAssets();
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

        public const string PracticeDeckPath = CardFolder + "/Decks/DD_ComboPractice.asset";

        /// <summary>
        /// Combo practice deck (F7 in play): 12 copies of the existing elemental attack cards, so
        /// the pairs for all three combos (ice + lightning, fire + gravity, fire + lightning) show
        /// up in hand together often. Same rules as any deck. Kept as-is once it exists.
        /// </summary>
        public static DeckDefinition EnsurePracticeDeck(List<CardDefinition> cards)
        {
            var deck = AssetDatabase.LoadAssetAtPath<DeckDefinition>(PracticeDeckPath);
            if (deck != null)
            {
                return deck;
            }

            (string id, int copies)[] recipe =
            {
                ("card.ice_lance", 2), ("card.frost_nova", 1), ("card.chain_lightning", 3),
                ("card.flame_vortex", 2), ("card.incendiary", 1), ("card.gravity_spike", 2), ("card.gravity_collapse", 1),
            };

            var list = new List<CardDefinition>();
            foreach ((string id, int copies) in recipe)
            {
                CardDefinition card = cards.Find(c => c != null && c.cardId == id);
                if (card == null)
                {
                    throw new System.InvalidOperationException($"Practice deck: card '{id}' is missing");
                }

                for (int i = 0; i < copies; i++)
                {
                    list.Add(card);
                }
            }

            deck = ScriptableObject.CreateInstance<DeckDefinition>();
            deck.deckId = "deck.combo_practice";
            deck.handSize = 3;
            deck.cards = list;
            AssetDatabase.CreateAsset(deck, PracticeDeckPath);
            AssetDatabase.SaveAssets();
            return deck;
        }

        public const string NpcVoltDeckPath = CardFolder + "/Decks/DD_Npc_Volt.asset";
        public const string NpcBlazeDeckPath = CardFolder + "/Decks/DD_Npc_Blaze.asset";
        public const string NpcGuardianDeckPath = CardFolder + "/Decks/DD_Npc_Guardian.asset";

        /// <summary>
        /// NPC ally decks: 8 cards and 2 hand slots, same rules as the player. Volt (lightning) is the
        /// starting deck; Blaze (fire + gravity) and Guardian (support) are presets for the deck editor.
        /// Kept as-is once they exist.
        /// </summary>
        public static List<DeckDefinition> EnsureNpcDecks(List<CardDefinition> allCards)
        {
            return new List<DeckDefinition>
            {
                EnsureDeck(NpcVoltDeckPath, "deck.npc_volt", 2, allCards,
                    ("card.thunderbolt", 2), ("card.chain_lightning", 2), ("card.thunder_charge", 1), ("card.incendiary", 1),
                    ("card.area_heal", 1), ("card.shield", 1)),
                EnsureDeck(NpcBlazeDeckPath, "deck.npc_blaze", 2, allCards,
                    ("card.flame_vortex", 2), ("card.incendiary", 2), ("card.gravity_spike", 2), ("card.gravity_collapse", 1), ("card.area_heal", 1)),
                EnsureDeck(NpcGuardianDeckPath, "deck.npc_guardian", 2, allCards,
                    ("card.area_heal", 2), ("card.shield", 2), ("card.mark_target", 2), ("card.frost_nova", 1), ("card.thunderbolt", 1)),
            };
        }

        static DeckDefinition EnsureDeck(string path, string id, int handSize, List<CardDefinition> cards, params (string id, int copies)[] recipe)
        {
            var deck = AssetDatabase.LoadAssetAtPath<DeckDefinition>(path);
            if (deck != null)
            {
                return deck;
            }

            var list = new List<CardDefinition>();
            foreach ((string cardId, int copies) in recipe)
            {
                CardDefinition card = cards.Find(c => c != null && c.cardId == cardId);
                if (card == null)
                {
                    throw new System.InvalidOperationException($"Deck {id}: card '{cardId}' is missing");
                }

                for (int i = 0; i < copies; i++)
                {
                    list.Add(card);
                }
            }

            deck = ScriptableObject.CreateInstance<DeckDefinition>();
            deck.deckId = id;
            deck.handSize = handSize;
            deck.cards = list;
            AssetDatabase.CreateAsset(deck, path);
            AssetDatabase.SaveAssets();
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
