using CoopDeck.Cards;
using CoopDeck.Core;
using CoopDeck.Deck;
using UnityEngine;
using UnityEngine.UI;

namespace CoopDeck.UI
{
    /// <summary>
    /// What one skill card shows. Built from runtime state (<see cref="From"/>) or by hand for
    /// previews. <see cref="Specialized"/> is the 原脈同調 state; the run logic for it does not
    /// exist yet, so the combat HUD always passes false (Docs/UI_DESIGN.md §4).
    /// </summary>
    public struct CardViewData
    {
        public bool HasCard;
        public string Name;
        public Sprite Icon;
        public Element Element;
        public CardRarity Rarity;
        public float Gauge;
        public float MaxGauge;
        public float Cost;
        public int UsesLeft;
        public int UsesFromFull;
        public float Cooldown;
        public float CooldownRemaining;
        public int UpgradeLevel;
        public bool Mutated;
        public bool Specialized;
        public bool Selected;

        /// <summary>The card just hit 0 and is leaving for the discard pile.</summary>
        public bool Depleting;

        public string KeyLabel;

        /// <summary>A specialised card stays in hand at 0 gauge but cannot be used.</summary>
        public bool Unusable => HasCard && Specialized && Gauge <= 0f;

        public bool CoolingDown => HasCard && !Depleting && CooldownRemaining > 0f;

        public static CardViewData Empty(string keyLabel, bool selected = false) => new CardViewData { KeyLabel = keyLabel, Selected = selected };

        public static CardViewData From(CardRuntime card, string keyLabel, bool selected, bool depleting, bool specialized = false)
        {
            if (card == null)
            {
                return Empty(keyLabel, selected);
            }

            CardDefinition def = card.Definition;
            return new CardViewData
            {
                HasCard = true,
                Name = def.displayName,
                Icon = def.icon,
                Element = def.element,
                Rarity = def.rarity,
                Gauge = depleting ? 0f : card.Gauge,
                MaxGauge = card.MaxGauge,
                Cost = card.GaugeCost,
                UsesLeft = depleting ? 0 : card.RemainingUses,
                UsesFromFull = Mathf.CeilToInt(card.MaxGauge / card.GaugeCost - 0.0001f),
                Cooldown = card.Cooldown,
                CooldownRemaining = depleting ? 0f : card.CooldownRemaining,
                UpgradeLevel = card.UpgradeLevel,
                Specialized = specialized,
                Selected = selected,
                Depleting = depleting,
                KeyLabel = keyLabel,
            };
        }

        /// <summary>The line under the gauge: uses left, last use, depleted, unusable.</summary>
        public string UsesText
        {
            get
            {
                if (!HasCard) return "";
                if (Unusable) return "ゲージ0・使用不可";
                if (Depleting) return "使い切り → 捨て札";
                return UsesLeft == 1 ? "ラスト1回" : $"あと {UsesLeft} 回";
            }
        }
    }

    /// <summary>
    /// The shared skill-card frame (combat hand now; rewards, deck swap and codex later): metal
    /// frame, hexagonal icon socket, element icon + name, rarity pips, key badge, segmented gauge
    /// with drop glow, cooldown shade, upgrade badge, the 原脈同調 gold frame and sync mark, and the
    /// item-target brackets. Strings are rebuilt only when a shown value changes.
    /// </summary>
    public sealed class CardFrameView
    {
        public const float Width = 220f;
        public const float Height = 168f;

        static readonly Vector2 Centre = new Vector2(0.5f, 0.5f);

        public RectTransform Root;
        public CanvasGroup Group;

        Image glow;
        PanelView panel;
        Image headerTint;
        Image innerGold;
        Image socketFill;
        Image socketRing;
        Image icon;
        Text nameText;
        Image elementIcon;
        Text elementText;
        readonly Image[] pips = new Image[4];
        PanelView keyPlate;
        Text keyText;
        RectTransform upgradeBadge;
        Text upgradeText;
        RectTransform mutationBadge;
        Text gaugeText;
        Text costText;
        HudBar bar;
        Text usesText;
        Text cooldownText;
        Image cooldownShade;
        Image unusableShade;
        Text unusableText;
        Image syncDisc;
        Image syncMark;
        Text syncLabel;
        Image focus;
        Text focusTag;
        Text emptyText;

        float useFlash;
        bool built;
        CardViewData last;
        int lastGaugeKey = int.MinValue;
        int lastCostKey = int.MinValue;
        int lastCooldownKey = int.MinValue;
        int lastTicks = -1;
        string lastName;
        string lastKey;
        int lastUsesKey = int.MinValue;

        public static CardFrameView Create(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 position)
        {
            var view = new CardFrameView();
            view.Build(name, parent, anchor, pivot, position);
            return view;
        }

        void Build(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 position)
        {
            Root = HudKit.CreateRect(name, parent, anchor, pivot, position, new Vector2(Width, Height));
            Group = Root.gameObject.AddComponent<CanvasGroup>();
            Group.interactable = false;
            Group.blocksRaycasts = false;

            glow = UiWidgets.Icon("UseGlow", Root, UiSprites.Glow, new Color(1f, 1f, 1f, 0f), Centre, Vector2.zero, Width);
            glow.preserveAspect = false;
            glow.rectTransform.sizeDelta = new Vector2(Width * 1.5f, Height * 1.6f);

            panel = UiWidgets.Panel("Card", Root, Centre, Centre, Vector2.zero, new Vector2(Width, Height), UiSprites.FrameStyle.Card, true,
                UiTokens.PanelDeepTranslucent);
            HudKit.Stretch(panel.Root);
            headerTint = HudKit.CreateImage("ElementHeader", panel.Root, Color.clear, new Vector2(0.5f, 1f), new Vector2(0f, -6f), new Vector2(Width - 16f, 54f));
            headerTint.sprite = UiSprites.Gradient;
            headerTint.rectTransform.pivot = new Vector2(0.5f, 1f);
            panel.Frame.transform.SetAsLastSibling();
            innerGold = UiWidgets.SlicedImage("SyncInnerLine", Root, UiSprites.Frame(UiSprites.FrameStyle.Small), UiTokens.WithAlpha(UiTokens.SyncGold, 0.8f));
            HudKit.Stretch(innerGold.rectTransform, 7f);
            innerGold.fillCenter = false;

            // Icon socket.
            socketFill = UiWidgets.Icon("SocketFill", Root, UiSprites.HexFill, UiTokens.WithAlpha(UiTokens.Background, 0.95f), Centre, new Vector2(-70f, 30f), 64f);
            socketRing = UiWidgets.Icon("SocketRing", Root, UiSprites.HexRing, UiTokens.FrameSteel, Centre, new Vector2(-70f, 30f), 64f);
            icon = UiWidgets.Icon("Icon", Root, HudKit.WhiteSprite, Color.white, Centre, new Vector2(-70f, 30f), 42f);

            nameText = UiWidgets.Label("Name", Root, UiTokens.Label, UiTokens.TextMain, TextAnchor.MiddleLeft, Centre, new Vector2(31f, 52f),
                new Vector2(130f, 30f), true);
            UiWidgets.FitText(nameText, UiTokens.Label);

            elementIcon = UiWidgets.ElementIcon("ElementIcon", Root, Element.None, Centre, new Vector2(-24f, 24f), 20f);
            elementText = UiWidgets.Label("Element", Root, UiTokens.Caption, UiTokens.TextSub, TextAnchor.MiddleLeft, Centre, new Vector2(34f, 24f),
                new Vector2(96f, 24f));
            for (int i = 0; i < pips.Length; i++)
            {
                pips[i] = UiWidgets.Icon("RarityPip", Root, UiSprites.Diamond, UiTokens.Common, Centre, new Vector2(-26f + i * 14f, 4f), 12f);
            }

            keyText = UiWidgets.KeyBadge("Key", Root, Centre, new Vector2(Width * 0.5f - 36f, Height * 0.5f - 2f), new Vector2(48f, 34f), out keyPlate);

            upgradeBadge = UiWidgets.Panel("Upgrade", Root, Centre, Centre, new Vector2(-40f, 2f), new Vector2(34f, 22f), UiSprites.FrameStyle.Small, false,
                UiTokens.WithAlpha(UiTokens.Background, 0.95f), UiTokens.Accent).Root;
            upgradeText = UiWidgets.Label("Value", upgradeBadge, UiTokens.Caption - 2, UiTokens.Accent, TextAnchor.MiddleCenter, Centre, Vector2.zero,
                new Vector2(34f, 22f), true);
            mutationBadge = UiWidgets.Panel("Mutation", Root, Centre, Centre, new Vector2(-92f, 2f), new Vector2(34f, 22f), UiSprites.FrameStyle.Small, false,
                UiTokens.WithAlpha(UiTokens.Background, 0.95f), UiTokens.Energy).Root;
            UiWidgets.Label("Value", mutationBadge, UiTokens.Caption - 2, UiTokens.Energy, TextAnchor.MiddleCenter, Centre, Vector2.zero,
                new Vector2(34f, 22f), true).text = "変";

            gaugeText = UiWidgets.Label("Gauge", Root, UiTokens.Body, UiTokens.TextMain, TextAnchor.MiddleLeft, Centre, new Vector2(-42f, -22f),
                new Vector2(120f, 26f), true);
            costText = UiWidgets.Label("Cost", Root, UiTokens.Caption, UiTokens.TextSub, TextAnchor.MiddleRight, Centre, new Vector2(48f, -22f),
                new Vector2(100f, 24f));
            bar = UiWidgets.Bar("GaugeBar", Root, Centre, Centre, new Vector2(0f, -45f), new Vector2(Width - 28f, 14f), Color.white);
            usesText = UiWidgets.Label("Uses", Root, UiTokens.Caption, UiTokens.TextSub, TextAnchor.MiddleLeft, Centre, new Vector2(-42f, -67f),
                new Vector2(120f, 24f));
            cooldownText = UiWidgets.Label("Cooldown", Root, UiTokens.Caption, UiTokens.TextSub, TextAnchor.MiddleRight, Centre, new Vector2(48f, -67f),
                new Vector2(100f, 24f));

            cooldownShade = HudKit.CreateImage("CooldownShade", Root, UiTokens.WithAlpha(UiTokens.Background, 0.6f), new Vector2(0.5f, 1f), new Vector2(0f, -5f),
                new Vector2(Width - 10f, Height - 10f));
            cooldownShade.rectTransform.pivot = new Vector2(0.5f, 1f);

            unusableShade = HudKit.CreateImage("UnusableShade", Root, UiTokens.WithAlpha(UiTokens.Background, 0.62f), Centre, Vector2.zero,
                new Vector2(Width - 10f, Height - 10f));
            unusableText = UiWidgets.Label("Unusable", Root, UiTokens.Body, UiTokens.TextMain, TextAnchor.MiddleCenter, Centre, new Vector2(0f, -2f),
                new Vector2(Width, 30f), true);
            unusableText.text = "使用不可";

            syncDisc = UiWidgets.Icon("SyncDisc", Root, UiSprites.Glow, UiTokens.WithAlpha(UiTokens.SyncGold, 0.55f), Centre, new Vector2(0f, Height * 0.5f), 58f);
            syncMark = UiWidgets.Icon("SyncMark", Root, UiSprites.SyncMark, UiTokens.SyncGold, Centre, new Vector2(0f, Height * 0.5f), 34f);
            syncLabel = UiWidgets.Label("SyncLabel", Root, UiTokens.Caption - 2, UiTokens.SyncGold, TextAnchor.MiddleCenter, Centre,
                new Vector2(0f, Height * 0.5f + 26f), new Vector2(160f, 20f), true);
            syncLabel.text = WorldTerms.Specialisation;
            syncLabel.gameObject.SetActive(false); // the mark alone on the HUD; label shown in previews / detail views

            focus = UiWidgets.FocusBrackets("ItemTarget", Root, UiTokens.Accent, 9f);
            focusTag = UiWidgets.Label("ItemTargetTag", Root, UiTokens.Caption, UiTokens.Accent, TextAnchor.MiddleLeft, new Vector2(0.5f, 1f),
                new Vector2(-30f, 30f), new Vector2(160f, 24f), true);
            HudKit.AddOutline(focusTag, 0.7f);

            emptyText = UiWidgets.Label("Empty", Root, UiTokens.Body, UiTokens.TextMuted, TextAnchor.MiddleCenter, Centre, Vector2.zero, new Vector2(Width, 30f));
            emptyText.text = "空き枠";
            built = true;
        }

        /// <summary>Shows the specialisation label under the sync mark (previews, detail screens).</summary>
        public bool ShowSyncLabel
        {
            set => syncLabel.gameObject.SetActive(value);
        }

        /// <summary>Text above the card while it is the item target (localised by the caller).</summary>
        public string FocusTag
        {
            set => focusTag.text = value;
        }

        /// <summary>Brief bright pulse when the card is used.</summary>
        public void FlashUse() => useFlash = 1f;

        public void Tick(float dt)
        {
            if (!built)
            {
                return;
            }

            bar.Tick(dt);
            useFlash = Mathf.Max(0f, useFlash - dt * 3.2f);
            Color glowColor = last.Specialized ? UiTokens.SyncGold : UiTokens.ElementColor(last.Element);
            float idle = last.Specialized && !last.Unusable ? 0.12f + 0.06f * Mathf.Sin(Time.unscaledTime * 2.4f) : 0f;
            glow.color = UiTokens.WithAlpha(glowColor, Mathf.Max(idle, useFlash * 0.5f));
            if (last.Specialized)
            {
                syncDisc.color = UiTokens.WithAlpha(UiTokens.SyncGold, last.Unusable ? 0.15f : 0.4f + 0.2f * Mathf.Sin(Time.unscaledTime * 2.4f));
            }
        }

        public void Apply(in CardViewData data)
        {
            bool has = data.HasCard;
            bool newCard = !ReferenceEquals(data.Name, last.Name) || data.Element != last.Element || has != last.HasCard;
            SetContentActive(has);
            emptyText.enabled = !has;
            focus.enabled = data.Selected;
            focusTag.enabled = data.Selected && has;
            panel.Fill.color = has ? UiTokens.PanelDeepTranslucent : UiTokens.WithAlpha(UiTokens.PanelDeep, 0.45f);

            bool spec = has && data.Specialized;
            Color frame = spec ? UiTokens.SyncGold : has && data.Rarity == CardRarity.Legendary ? UiTokens.Legendary : has ? UiTokens.FrameSteel : UiTokens.WithAlpha(UiTokens.FrameSteel, 0.5f);
            panel.Frame.color = frame;
            innerGold.enabled = spec;
            syncDisc.enabled = spec;
            syncMark.enabled = spec;
            syncMark.color = data.Unusable ? UiTokens.WithAlpha(UiTokens.SyncGold, 0.55f) : UiTokens.SyncGold;
            if (syncLabel.gameObject.activeSelf)
            {
                syncLabel.enabled = spec;
            }

            if (lastKey != data.KeyLabel)
            {
                lastKey = data.KeyLabel;
                keyText.text = data.KeyLabel ?? "";
            }

            if (!has)
            {
                cooldownShade.enabled = false;
                unusableShade.enabled = unusableText.enabled = false;
                last = data;
                lastName = null;
                return;
            }

            Color element = UiTokens.ElementColor(data.Element);
            if (newCard || lastName != data.Name)
            {
                lastName = data.Name;
                nameText.text = data.Name;
                icon.sprite = data.Icon != null ? data.Icon : UiSprites.ElementIcon(data.Element);
                icon.color = data.Icon != null ? Color.white : element;
                elementIcon.sprite = UiSprites.ElementIcon(data.Element);
                elementIcon.color = element;
                elementText.text = ElementStyle.DisplayName(data.Element) + "属性";
                elementText.color = UiTokens.ElementText(data.Element);
                socketRing.color = Color.Lerp(UiTokens.FrameSteel, element, 0.65f);
                headerTint.color = UiTokens.WithAlpha(element, 0.2f);
                bar.FillColor = element;
                bar.Set(data.MaxGauge > 0f ? data.Gauge / data.MaxGauge : 0f, true);
                int pipCount = UiTokens.RarityPips(data.Rarity);
                Color rarity = UiTokens.RarityColor(data.Rarity);
                for (int i = 0; i < pips.Length; i++)
                {
                    pips[i].color = i < pipCount ? rarity : UiTokens.WithAlpha(UiTokens.FrameSteel, 0.35f);
                }
            }

            int ticks = data.UsesFromFull >= 2 && data.UsesFromFull <= 8 ? data.UsesFromFull : 0;
            if (ticks != lastTicks)
            {
                lastTicks = ticks;
                UiWidgets.SetTicks(bar, ticks);
            }

            int gaugeKey = Mathf.CeilToInt(data.Gauge) * 10000 + Mathf.CeilToInt(data.MaxGauge);
            if (gaugeKey != lastGaugeKey)
            {
                lastGaugeKey = gaugeKey;
                gaugeText.text = $"{Mathf.CeilToInt(data.Gauge)} / {Mathf.CeilToInt(data.MaxGauge)}";
            }

            bar.Set(data.MaxGauge > 0f ? data.Gauge / data.MaxGauge : 0f);
            bar.FillColor = data.Depleting || data.Unusable ? UiTokens.Danger : element;
            gaugeText.color = data.Depleting || data.Unusable ? UiTokens.DangerText : UiTokens.TextMain;

            int costKey = Mathf.CeilToInt(data.Cost);
            if (costKey != lastCostKey)
            {
                lastCostKey = costKey;
                costText.text = $"消費 {costKey}";
            }

            int usesKey = data.Unusable ? -3 : data.Depleting ? -2 : data.UsesLeft;
            if (usesKey != lastUsesKey)
            {
                lastUsesKey = usesKey;
                usesText.text = data.UsesText;
            }

            usesText.color = data.Depleting || data.Unusable ? UiTokens.DangerText : data.UsesLeft == 1 ? UiTokens.Accent : UiTokens.TextSub;

            bool cooling = data.CoolingDown;
            int cooldownKey = cooling ? Mathf.CeilToInt(data.CooldownRemaining * 10f) : -1 - Mathf.RoundToInt(data.Cooldown * 10f);
            if (cooldownKey != lastCooldownKey)
            {
                lastCooldownKey = cooldownKey;
                cooldownText.text = cooling ? $"CD {data.CooldownRemaining:0.0}s" : data.Cooldown > 0f ? $"CD {data.Cooldown:0.#}s" : "";
            }

            cooldownText.color = cooling ? UiTokens.Energy : UiTokens.TextSub;
            cooldownShade.enabled = cooling;
            if (cooling)
            {
                float fraction = data.Cooldown > 0f ? Mathf.Clamp01(data.CooldownRemaining / data.Cooldown) : 0f;
                cooldownShade.rectTransform.localScale = new Vector3(1f, fraction, 1f);
            }

            bool unusable = data.Unusable;
            unusableShade.enabled = unusableText.enabled = unusable;

            upgradeBadge.gameObject.SetActive(data.UpgradeLevel > 0);
            if (data.UpgradeLevel > 0 && data.UpgradeLevel != last.UpgradeLevel)
            {
                upgradeText.text = "+" + data.UpgradeLevel;
            }

            mutationBadge.gameObject.SetActive(data.Mutated);
            last = data;
        }

        void SetContentActive(bool active)
        {
            headerTint.enabled = active;
            socketFill.enabled = active;
            socketRing.enabled = active;
            icon.enabled = active;
            nameText.enabled = active;
            elementIcon.enabled = active;
            elementText.enabled = active;
            foreach (Image pip in pips)
            {
                pip.enabled = active;
            }

            gaugeText.enabled = active;
            costText.enabled = active;
            bar.Root.gameObject.SetActive(active);
            usesText.enabled = active;
            cooldownText.enabled = active;
            if (!active)
            {
                upgradeBadge.gameObject.SetActive(false);
                mutationBadge.gameObject.SetActive(false);
            }
        }
    }
}
