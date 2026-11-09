using CoopDeck.Cards;
using UnityEngine;
using UnityEngine.UI;

namespace CoopDeck.UI
{
    /// <summary>
    /// Compact skill slot for the combat HUD (bottom right): a square with the card icon, a thin
    /// element-coloured frame, uses left (top-left), rarity pips (top-right), the gauge as a thin
    /// bar along the bottom (drop glow), the cooldown shade with seconds, and the key badge
    /// below. 原脈同調 = gold frame + sync mark; at 0 gauge it stays and shows "使用不可". The full
    /// name and description appear in the draw toast and on the larger <see cref="CardFrameView"/>.
    /// </summary>
    public sealed class SkillSlotView
    {
        public const float Size = HudLayout.SlotSize;

        static readonly Vector2 Centre = new Vector2(0.5f, 0.5f);

        public RectTransform Root;
        public CanvasGroup Group;

        RectTransform square;
        Image glow;
        Image fill;
        Image icon;
        Image frame;
        Text usesText;
        Text upgradeText;
        readonly Image[] pips = new Image[4];
        HudBar bar;
        Image cooldownShade;
        Text cooldownText;
        Image unusableShade;
        Text unusableText;
        Image syncMark;
        Image focus;
        Text focusArrow;
        PanelView keyPlate;
        Text keyText;
        Text emptyText;

        CardViewData last;
        bool hasLast;
        int lastUsesKey = int.MinValue;
        int lastCooldownKey = int.MinValue;
        int lastTicks = -1;
        string lastKey;
        float useFlash;

        public static SkillSlotView Create(string name, Transform parent)
        {
            var view = new SkillSlotView();
            view.Build(name, parent);
            return view;
        }

        void Build(string name, Transform parent)
        {
            Root = HudKit.CreateRect(name, parent, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(Size, Size + HudLayout.KeyRow + 2f));
            Group = Root.gameObject.AddComponent<CanvasGroup>();
            Group.interactable = false;
            Group.blocksRaycasts = false;

            square = HudKit.CreateRect("Square", Root, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(Size, Size));
            glow = UiWidgets.Icon("Glow", square, UiSprites.Glow, Color.clear, Centre, Vector2.zero, Size * 1.8f);
            fill = UiWidgets.SlicedImage("Fill", square, UiSprites.Fill(UiSprites.FrameStyle.Small), UiTokens.WithAlpha(UiTokens.PanelDeep, 0.88f));
            HudKit.Stretch(fill.rectTransform);
            icon = UiWidgets.Icon("Icon", square, HudKit.WhiteSprite, Color.white, Centre, new Vector2(0f, 3f), Size - 20f);
            bar = UiWidgets.Bar("GaugeBar", square, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 5f), new Vector2(Size - 12f, 6f), Color.white);
            cooldownShade = HudKit.CreateImage("CooldownShade", square, UiTokens.WithAlpha(UiTokens.Background, 0.62f), new Vector2(0.5f, 1f), new Vector2(0f, -3f),
                new Vector2(Size - 6f, Size - 6f));
            cooldownShade.rectTransform.pivot = new Vector2(0.5f, 1f);
            cooldownText = UiWidgets.Label("Cooldown", square, UiTokens.Title, UiTokens.TextMain, TextAnchor.MiddleCenter, Centre, new Vector2(0f, 3f),
                new Vector2(Size, 30f), true, false);
            HudKit.AddOutline(cooldownText, 0.85f);
            unusableShade = HudKit.CreateImage("UnusableShade", square, UiTokens.WithAlpha(UiTokens.Background, 0.7f), Centre, Vector2.zero, new Vector2(Size - 6f, Size - 6f));
            unusableText = UiWidgets.Label("Unusable", square, UiTokens.Caption - 2, UiTokens.DangerText, TextAnchor.MiddleCenter, Centre, new Vector2(0f, 3f),
                new Vector2(Size, 22f), true, false);
            HudKit.AddOutline(unusableText, 0.85f);
            unusableText.text = "使用不可";
            frame = UiWidgets.SlicedImage("Frame", square, UiSprites.Frame(UiSprites.FrameStyle.Small), UiTokens.FrameSteel);
            HudKit.Stretch(frame.rectTransform);

            usesText = UiWidgets.Label("Uses", square, UiTokens.Body, UiTokens.TextMain, TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(17f, -14f),
                new Vector2(30f, 22f), true, false);
            HudKit.AddOutline(usesText, 0.9f);
            upgradeText = UiWidgets.Label("Upgrade", square, UiTokens.Caption - 2, UiTokens.Accent, TextAnchor.MiddleRight, new Vector2(1f, 0f), new Vector2(-18f, 20f),
                new Vector2(30f, 18f), true, false);
            HudKit.AddOutline(upgradeText, 0.9f);
            for (int i = 0; i < pips.Length; i++)
            {
                pips[i] = UiWidgets.Icon("RarityPip", square, UiSprites.Diamond, UiTokens.Common, new Vector2(1f, 1f), new Vector2(-9f - i * 8f, -9f), 7f);
            }

            syncMark = UiWidgets.Icon("SyncMark", square, UiSprites.SyncMark, UiTokens.SyncGold, new Vector2(0.5f, 1f), new Vector2(0f, 1f), 20f);
            focus = UiWidgets.FocusBrackets("ItemTarget", square, UiTokens.Accent, 5f);
            focusArrow = UiWidgets.Label("ItemTargetArrow", square, UiTokens.Caption, UiTokens.Accent, TextAnchor.MiddleCenter, new Vector2(0.5f, 1f),
                new Vector2(0f, 16f), new Vector2(Size, 18f), true, false);
            focusArrow.text = "▼";
            HudKit.AddOutline(focusArrow, 0.8f);

            keyText = UiWidgets.KeyBadge("Key", Root, new Vector2(0.5f, 0f), new Vector2(0f, HudLayout.KeyRow * 0.5f), new Vector2(Size - 24f, HudLayout.KeyRow - 2f),
                out keyPlate);
            keyText.fontSize = UiTokens.Caption;
            keyText.resizeTextMaxSize = UiTokens.Caption;

            emptyText = UiWidgets.Label("Empty", square, UiTokens.Caption, UiTokens.TextMuted, TextAnchor.MiddleCenter, Centre, Vector2.zero, new Vector2(Size, 22f), false, false);
            emptyText.text = "空き";
        }

        /// <summary>Brief bright pulse when the card is used.</summary>
        public void FlashUse() => useFlash = 1f;

        public void Tick(float dt)
        {
            bar.Tick(dt);
            useFlash = Mathf.Max(0f, useFlash - dt * 3.5f);
            Color glowColor = last.Specialized ? UiTokens.SyncGold : UiTokens.ElementColor(last.Element);
            float idle = last.HasCard && last.Specialized && !last.Unusable ? 0.18f + 0.08f * Mathf.Sin(Time.unscaledTime * 2.4f) : 0f;
            glow.color = UiTokens.WithAlpha(glowColor, Mathf.Max(idle, useFlash * 0.55f));
        }

        public void Apply(in CardViewData data)
        {
            bool has = data.HasCard;
            bool newCard = !hasLast || has != last.HasCard || !ReferenceEquals(data.Name, last.Name) || data.Element != last.Element;
            hasLast = true;

            if (lastKey != data.KeyLabel)
            {
                lastKey = data.KeyLabel;
                keyText.text = data.KeyLabel ?? "";
            }

            focus.enabled = data.Selected;
            focusArrow.enabled = data.Selected && has;
            emptyText.enabled = !has;
            icon.enabled = has;
            usesText.enabled = has;
            bar.Root.gameObject.SetActive(has);
            foreach (Image pip in pips)
            {
                pip.enabled = has;
            }

            if (!has)
            {
                fill.color = UiTokens.WithAlpha(UiTokens.PanelDeep, 0.45f);
                frame.color = UiTokens.WithAlpha(UiTokens.FrameSteel, 0.5f);
                cooldownShade.enabled = cooldownText.enabled = false;
                unusableShade.enabled = unusableText.enabled = false;
                syncMark.enabled = false;
                upgradeText.enabled = false;
                last = data;
                return;
            }

            Color element = UiTokens.ElementColor(data.Element);
            fill.color = UiTokens.WithAlpha(UiTokens.PanelDeep, 0.88f);
            if (newCard)
            {
                icon.sprite = data.Icon != null ? data.Icon : UiSprites.ElementIcon(data.Element);
                icon.color = data.Icon != null ? Color.white : element;
                bar.FillColor = element;
                bar.Set(data.MaxGauge > 0f ? data.Gauge / data.MaxGauge : 0f, true);
                int pipCount = UiTokens.RarityPips(data.Rarity);
                Color rarity = UiTokens.RarityColor(data.Rarity);
                for (int i = 0; i < pips.Length; i++)
                {
                    pips[i].color = i < pipCount ? rarity : Color.clear;
                }
            }

            bool spec = data.Specialized;
            frame.color = spec ? UiTokens.SyncGold : data.Rarity == CardRarity.Legendary ? UiTokens.Legendary : Color.Lerp(UiTokens.FrameSteel, element, 0.7f);
            syncMark.enabled = spec;
            syncMark.color = data.Unusable ? UiTokens.WithAlpha(UiTokens.SyncGold, 0.5f) : UiTokens.SyncGold;

            int ticks = data.UsesFromFull >= 2 && data.UsesFromFull <= 8 ? data.UsesFromFull : 0;
            if (ticks != lastTicks)
            {
                lastTicks = ticks;
                UiWidgets.SetTicks(bar, ticks);
            }

            bar.Set(data.MaxGauge > 0f ? data.Gauge / data.MaxGauge : 0f);
            bar.FillColor = data.Depleting || data.Unusable ? UiTokens.Danger : element;

            int usesKey = data.Unusable || data.Depleting ? 0 : data.UsesLeft;
            if (usesKey != lastUsesKey)
            {
                lastUsesKey = usesKey;
                usesText.text = usesKey.ToString();
            }

            usesText.color = usesKey <= 0 ? UiTokens.DangerText : usesKey == 1 ? UiTokens.Accent : UiTokens.TextMain;

            bool cooling = data.CoolingDown;
            cooldownShade.enabled = cooling;
            cooldownText.enabled = cooling;
            if (cooling)
            {
                float fraction = data.Cooldown > 0f ? Mathf.Clamp01(data.CooldownRemaining / data.Cooldown) : 0f;
                cooldownShade.rectTransform.localScale = new Vector3(1f, fraction, 1f);
                int key = Mathf.CeilToInt(data.CooldownRemaining * 10f);
                if (key != lastCooldownKey)
                {
                    lastCooldownKey = key;
                    cooldownText.text = (key / 10f).ToString("0.0");
                }
            }

            unusableShade.enabled = unusableText.enabled = data.Unusable;
            upgradeText.enabled = data.UpgradeLevel > 0 || data.Mutated;
            if (upgradeText.enabled && (data.UpgradeLevel != last.UpgradeLevel || data.Mutated != last.Mutated || newCard))
            {
                upgradeText.text = (data.Mutated ? "変" : "") + (data.UpgradeLevel > 0 ? "+" + data.UpgradeLevel : "");
            }

            last = data;
        }
    }
}
