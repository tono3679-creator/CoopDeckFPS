using CoopDeck.Combat;
using CoopDeck.Core;
using CoopDeck.Player;
using UnityEngine;
using UnityEngine.UI;

namespace CoopDeck.UI
{
    /// <summary>
    /// Bottom-left status, compact: one wide HP bar with the numbers inside (25-HP segments, ghost
    /// on damage, red pulse when low), a thin shield line on top, and a row above with the element
    /// mark, name, small status icons with timers (shield, attack buff, vulnerability, burning,
    /// element auras) and the dash charges as lamps. No panel: a soft dark scrim keeps it readable.
    /// </summary>
    public sealed class VitalsPanel
    {
        const int MaxDash = 3;
        const int MaxChips = 3;
        const float LowHealth = 0.3f;
        const float BarWidth = 420f;
        const float BarHeight = 22f;

        RectTransform root;
        Image elementIcon;
        Text nameText;
        Text hpText;
        HudBar hpBar;
        HudBar shieldBar;
        readonly LampView[] dashLamps = new LampView[MaxDash];
        readonly Chip[] chips = new Chip[MaxChips];
        int lastHp = int.MinValue;
        int lastMax = int.MinValue;
        int lastSegments = -1;
        bool lastAlive = true;
        float lastCharge = -1f;

        sealed class Chip
        {
            public RectTransform Root;
            public Image Icon;
            public Text Value;
            public int Key = int.MinValue;
        }

        public RectTransform Root => root;

        /// <summary>The numbers inside the HP bar (tests, debugging).</summary>
        public string HpLine => hpText != null ? hpText.text : "";

        public static VitalsPanel Create(Transform parent, Element element, string name)
        {
            var view = new VitalsPanel();
            view.Build(parent, element, name);
            return view;
        }

        void Build(Transform parent, Element element, string name)
        {
            Rect layout = HudLayout.Vitals;
            root = HudKit.CreateRect("Vitals", parent, Vector2.zero, Vector2.zero, Vector2.zero, layout.size);
            HudLayout.Place(root, layout);
            var bl = Vector2.zero;

            Image scrim = UiWidgets.Icon("Scrim", root, UiSprites.Glow, UiTokens.WithAlpha(UiTokens.Background, 0.45f), new Vector2(0.5f, 0.5f), Vector2.zero, 1f);
            scrim.preserveAspect = false;
            scrim.rectTransform.sizeDelta = new Vector2(BarWidth + 160f, 150f);

            shieldBar = UiWidgets.Bar("Shield", root, bl, bl, new Vector2(0f, BarHeight + 3f), new Vector2(BarWidth, 4f), UiTokens.Shield);
            hpBar = UiWidgets.Bar("Hp", root, bl, bl, Vector2.zero, new Vector2(BarWidth, BarHeight), UiTokens.Health);
            hpText = UiWidgets.Label("HpText", hpBar.Root, UiTokens.Body, UiTokens.TextMain, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0f, 1f),
                new Vector2(BarWidth, BarHeight), true, false);
            HudKit.AddOutline(hpText, 0.9f);

            float rowY = BarHeight + 22f;
            elementIcon = UiWidgets.ElementIcon("Element", root, element, bl, new Vector2(9f, rowY), 18f);
            HudKit.AddOutline(elementIcon, 0.6f);
            nameText = UiWidgets.Label("Name", root, UiTokens.Body, UiTokens.TextMain, TextAnchor.MiddleLeft, bl, new Vector2(66f, rowY), new Vector2(92f, 22f), true, false);
            HudKit.AddOutline(nameText, 0.85f);
            nameText.text = name;

            for (int i = 0; i < MaxChips; i++)
            {
                var chip = new Chip { Root = HudKit.CreateRect("Status", root, bl, new Vector2(0f, 0.5f), new Vector2(118f + i * 66f, rowY), new Vector2(62f, 22f)) };
                chip.Icon = UiWidgets.Icon("Icon", chip.Root, UiSprites.ShieldIcon, UiTokens.Shield, new Vector2(0f, 0.5f), new Vector2(9f, 0f), 17f);
                HudKit.AddOutline(chip.Icon, 0.6f);
                chip.Value = UiWidgets.Label("Value", chip.Root, UiTokens.Caption, UiTokens.TextMain, TextAnchor.MiddleLeft, new Vector2(0f, 0.5f), new Vector2(42f, 0f),
                    new Vector2(44f, 20f), true, false);
                HudKit.AddOutline(chip.Value, 0.85f);
                chip.Root.gameObject.SetActive(false);
                chips[i] = chip;
            }

            Text dashLabel = UiWidgets.Label("DashLabel", root, UiTokens.Caption, UiTokens.TextSub, TextAnchor.MiddleRight, bl, new Vector2(BarWidth - 82f, rowY),
                new Vector2(50f, 20f), false, false);
            HudKit.AddOutline(dashLabel, 0.8f);
            dashLabel.text = "回避";
            for (int i = 0; i < MaxDash; i++)
            {
                dashLamps[i] = UiWidgets.Lamp("Dash", root, bl, new Vector2(BarWidth - 48f + i * 20f, rowY), new Vector2(16f, 8f));
                dashLamps[i].OnColor = UiTokens.Energy;
            }
        }

        public void SetElement(Element element)
        {
            elementIcon.sprite = UiSprites.ElementIcon(element);
            elementIcon.color = UiTokens.ElementColor(element);
        }

        public void Update(FpsCharacter player, float dt)
        {
            Health health = player != null ? player.Health : null;
            CombatStatus status = player != null ? player.Status : null;
            float shield = status != null ? status.Model.Shield : 0f;

            if (health != null)
            {
                int hp = Mathf.CeilToInt(health.Current);
                int max = Mathf.CeilToInt(health.Max);
                if (hp != lastHp || max != lastMax || health.IsAlive != lastAlive)
                {
                    lastHp = hp;
                    lastMax = max;
                    lastAlive = health.IsAlive;
                    hpText.text = health.IsAlive ? $"{hp} / {max}" : "ダウン";
                }

                int segments = Mathf.Clamp(Mathf.RoundToInt(health.Max / 25f), 0, 12);
                if (segments != lastSegments)
                {
                    lastSegments = segments;
                    UiWidgets.SetTicks(hpBar, segments);
                }

                float normalized = health.Normalized;
                bool low = health.IsAlive && normalized <= LowHealth;
                float pulse = low ? 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 7f) : 0f;
                hpBar.FillColor = low ? Color.Lerp(UiTokens.Danger, UiTokens.TextMain, pulse * 0.25f) : UiTokens.Health;
                hpBar.Set(normalized);
                hpText.color = !health.IsAlive ? UiTokens.DangerText : UiTokens.TextMain;
                shieldBar.Set(Mathf.Clamp01(shield / Mathf.Max(1f, health.Max)));
                shieldBar.Root.gameObject.SetActive(shield > 0f);
            }

            hpBar.Tick(dt);
            shieldBar.Tick(dt);
            UpdateDash(player, dt);
            UpdateChips(status, shield);
        }

        void UpdateDash(FpsCharacter player, float dt)
        {
            FpsMotor motor = player != null ? player.Motor : null;
            int maxCharges = motor != null && motor.Profile != null ? motor.Profile.dashCharges : 0;
            float charge = motor != null ? motor.DashCharge : 0f;
            for (int i = 0; i < MaxDash; i++)
            {
                LampView lamp = dashLamps[i];
                bool exists = i < maxCharges;
                lamp.Root.gameObject.SetActive(exists);
                if (!exists)
                {
                    continue;
                }

                if (lastCharge >= 0f && lastCharge < i + 1 && charge >= i + 1)
                {
                    lamp.Flash();
                }

                lamp.Tick(dt);
                lamp.Set(charge >= i + 1);
            }

            lastCharge = charge;
        }

        void UpdateChips(CombatStatus status, float shield)
        {
            int used = 0;
            if (status != null)
            {
                StatusEffectsModel m = status.Model;
                if (shield > 0f)
                {
                    SetChip(ref used, 1, UiSprites.ShieldIcon, UiTokens.Shield, m.ShieldTimeRemaining);
                }

                if (m.DamageBuff > 0f)
                {
                    SetChip(ref used, 2, UiSprites.ChevronUp, UiTokens.Accent, m.DamageBuffTimeRemaining);
                }

                if (m.Vulnerability > 0f)
                {
                    SetChip(ref used, 3, UiSprites.ChevronDown, UiTokens.Danger, m.VulnerabilityTimeRemaining);
                }

                if (m.DamageOverTimeCount > 0)
                {
                    SetChip(ref used, 4, UiSprites.ElementIcon(Element.Fire), UiTokens.Danger, -1f);
                }

                ElementAuraModel auras = status.Elements;
                for (int e = 1; e <= 4 && auras != null; e++)
                {
                    var element = (Element)e;
                    if (auras.Has(element))
                    {
                        SetChip(ref used, 10 + e, UiSprites.ElementIcon(element), UiTokens.ElementColor(element), -1f);
                    }
                }
            }

            for (int i = used; i < MaxChips; i++)
            {
                if (chips[i].Root.gameObject.activeSelf)
                {
                    chips[i].Root.gameObject.SetActive(false);
                }

                chips[i].Key = int.MinValue;
            }
        }

        void SetChip(ref int used, int kind, Sprite icon, Color color, float seconds)
        {
            if (used >= MaxChips)
            {
                return;
            }

            Chip chip = chips[used++];
            int key = kind * 1000 + (seconds >= 0f ? Mathf.CeilToInt(seconds) : 999);
            if (chip.Key == key)
            {
                return;
            }

            bool newKind = chip.Key / 1000 != kind;
            chip.Key = key;
            chip.Root.gameObject.SetActive(true);
            chip.Value.text = seconds >= 0f ? Mathf.CeilToInt(seconds) + "s" : "";
            if (newKind)
            {
                chip.Icon.sprite = icon;
                chip.Icon.color = color;
            }
        }
    }
}
