using CoopDeck.Core;
using CoopDeck.Player;
using CoopDeck.Weapons;
using UnityEngine;
using UnityEngine.UI;

namespace CoopDeck.UI
{
    /// <summary>
    /// Weapon readout in the bottom-right corner (the card hand is at the bottom centre), compact: weapon name, reserve ("∞": the game has no
    /// reserve ammo, reloading is always possible), the ammo count with the magazine size and a
    /// thin bar that empties as you fire and fills while reloading (amber, with a blinking lamp), and
    /// for a weapon with an on-hit ability (Thunderhop 帯電連射) a segmented charge gauge on top.
    /// No panel: a soft dark scrim keeps it readable.
    /// </summary>
    public sealed class WeaponPanel
    {
        RectTransform root;
        Image elementIcon;
        Text nameText;
        Text ammoText;
        Text magazineText;
        Text stateText;
        LampView lamp;
        HudBar bar;
        int lastAmmo = int.MinValue;
        int lastMagazine = int.MinValue;
        int lastState = -1;
        WeaponDefinition lastDefinition;
        InputScheme lastScheme = (InputScheme)(-1);
        RectTransform chargeRow;
        Image chargeIcon;
        Text chargeName;
        HudBar chargeBar;
        ChargeBurstModel shownCharge;
        int lastDischarges;

        public RectTransform Root => root;

        /// <summary>The ability charge shown (0–1), or -1 when the weapon has none (tests, debugging).</summary>
        public float ChargeShown => shownCharge != null ? chargeBar.Value : -1f;

        /// <summary>The ammo count shown (tests, debugging).</summary>
        public string AmmoLine => ammoText != null ? ammoText.text : "";

        public static WeaponPanel Create(Transform parent)
        {
            var view = new WeaponPanel();
            view.Build(parent);
            return view;
        }

        void Build(Transform parent)
        {
            Rect layout = HudLayout.Weapon;
            root = HudKit.CreateRect("Weapon", parent, Vector2.zero, Vector2.zero, Vector2.zero, layout.size);
            HudLayout.Place(root, layout);
            var bl = Vector2.zero;

            Image scrim = UiWidgets.Icon("Scrim", root, UiSprites.Glow, UiTokens.WithAlpha(UiTokens.Background, 0.4f), new Vector2(0.5f, 0.5f), Vector2.zero, 1f);
            scrim.preserveAspect = false;
            scrim.rectTransform.sizeDelta = new Vector2(layout.width + 120f, 130f);

            elementIcon = UiWidgets.ElementIcon("Element", root, Element.None, bl, new Vector2(8f, 42f), 16f);
            nameText = UiWidgets.Label("Name", root, UiTokens.Body, UiTokens.TextMain, TextAnchor.MiddleLeft, bl, new Vector2(76f, 42f), new Vector2(144f, 22f), true, false);
            HudKit.AddOutline(nameText, 0.85f);
            stateText = UiWidgets.Label("State", root, UiTokens.Caption, UiTokens.TextSub, TextAnchor.MiddleLeft, bl, new Vector2(78f, 18f), new Vector2(130f, 20f), true, false);
            HudKit.AddOutline(stateText, 0.85f);
            lamp = UiWidgets.Lamp("StateLamp", root, bl, new Vector2(7f, 18f), new Vector2(14f, 7f));

            ammoText = UiWidgets.Label("Ammo", root, UiTokens.NumberLarge, UiTokens.TextMain, TextAnchor.MiddleRight, bl, new Vector2(204f, 34f), new Vector2(110f, 48f),
                true, false);
            HudKit.AddOutline(ammoText, 0.9f);
            magazineText = UiWidgets.Label("Magazine", root, UiTokens.Body, UiTokens.TextSub, TextAnchor.MiddleLeft, bl, new Vector2(284f, 28f), new Vector2(44f, 22f),
                true, false);
            HudKit.AddOutline(magazineText, 0.85f);
            bar = UiWidgets.Bar("AmmoBar", root, bl, new Vector2(1f, 0.5f), new Vector2(layout.width, 4f), new Vector2(150f, 4f), UiTokens.TextMain);

            // On-hit ability charge (Thunderhop 帯電連射): icon, name, segmented gauge. Hidden for weapons without one.
            chargeRow = HudKit.CreateRect("Charge", root, bl, bl, new Vector2(0f, 60f), new Vector2(layout.width, 20f));
            chargeIcon = UiWidgets.ElementIcon("Icon", chargeRow, Element.Lightning, new Vector2(0f, 0.5f), new Vector2(8f, 0f), 15f);
            HudKit.AddOutline(chargeIcon, 0.6f);
            chargeName = UiWidgets.Label("Name", chargeRow, UiTokens.Caption, UiTokens.ElementText(Element.Lightning), TextAnchor.MiddleLeft, new Vector2(0f, 0.5f),
                new Vector2(66f, 0f), new Vector2(96f, 20f), true, false);
            HudKit.AddOutline(chargeName, 0.85f);
            chargeBar = UiWidgets.Bar("ChargeBar", chargeRow, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(176f, 7f), UiTokens.Lightning);
            chargeRow.gameObject.SetActive(false);
        }

        void UpdateCharge(HitscanWeapon weapon, float dt)
        {
            ChargeBurstModel charge = weapon != null ? weapon.ChargeBurst : null;
            if (charge != shownCharge)
            {
                shownCharge = charge;
                chargeRow.gameObject.SetActive(charge != null);
                if (charge != null)
                {
                    Element element = charge.Spec.element;
                    chargeIcon.sprite = UiSprites.ElementIcon(element);
                    chargeIcon.color = UiTokens.ElementColor(element);
                    chargeName.text = string.IsNullOrEmpty(charge.Spec.displayName) ? "帯電" : charge.Spec.displayName;
                    chargeName.color = UiTokens.ElementText(element);
                    chargeBar.FillColor = UiTokens.ElementColor(element);
                    UiWidgets.SetTicks(chargeBar, Mathf.Clamp(charge.RequiredStacks, 0, 16));
                    chargeBar.Set(0f, true);
                    lastDischarges = charge.Discharges;
                }
            }

            if (charge == null)
            {
                return;
            }

            // A discharge empties the gauge: the bar's drop glow is the "fired" flash.
            if (charge.Discharges != lastDischarges)
            {
                lastDischarges = charge.Discharges;
                chargeIcon.rectTransform.localScale = Vector3.one * 1.6f;
            }

            chargeIcon.rectTransform.localScale = Vector3.Lerp(chargeIcon.rectTransform.localScale, Vector3.one, 1f - Mathf.Exp(-12f * dt));
            bool waiting = charge.WaitingForCooldown;
            Color chargeColor = UiTokens.ElementColor(charge.Spec.element);
            chargeBar.FillColor = waiting ? Color.Lerp(chargeColor, UiTokens.TextMain, 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 14f)) : chargeColor;
            chargeBar.Set(charge.Progress);
            chargeBar.Tick(dt);
        }

        public void Update(FpsCharacter player, float dt)
        {
            HitscanWeapon weapon = player != null ? player.Weapon : null;
            WeaponState state = weapon != null ? weapon.State : null;
            UpdateCharge(weapon, dt);
            if (state == null)
            {
                if (lastAmmo != int.MinValue)
                {
                    ammoText.text = "--";
                    magazineText.text = "";
                    lastAmmo = int.MinValue;
                }

                return;
            }

            if (weapon.Definition != lastDefinition)
            {
                lastDefinition = weapon.Definition;
                nameText.text = lastDefinition != null ? lastDefinition.displayName : "";
                Element element = lastDefinition != null ? lastDefinition.element : Element.None;
                elementIcon.enabled = element != Element.None;
                elementIcon.sprite = UiSprites.ElementIcon(element);
                elementIcon.color = UiTokens.ElementColor(element);
                nameText.rectTransform.anchoredPosition = new Vector2(element != Element.None ? 94f : 76f, 42f);
            }

            if (state.MagazineSize != lastMagazine)
            {
                lastMagazine = state.MagazineSize;
                magazineText.text = "/ " + lastMagazine;
                lastAmmo = int.MinValue;
            }

            bool low = state.Ammo <= Mathf.CeilToInt(state.MagazineSize * 0.25f);
            if (state.Ammo != lastAmmo)
            {
                lastAmmo = state.Ammo;
                ammoText.text = state.Ammo.ToString();
            }

            ammoText.color = low && !state.IsReloading ? UiTokens.DangerText : UiTokens.TextMain;

            int stateKey = state.IsReloading ? 2 : state.Ammo == 0 ? 3 : low ? 1 : 0;
            if (stateKey != lastState || InputGlyphs.Active != lastScheme)
            {
                lastState = stateKey;
                lastScheme = InputGlyphs.Active;
                string reload = InputGlyphs.Label(HudAction.Reload);
                stateText.text = stateKey == 2 ? "装填中" : stateKey == 3 ? $"{reload} で装填" : stateKey == 1 ? "残弾わずか" : "予備 ∞";
                stateText.color = stateKey == 2 ? UiTokens.Accent : stateKey == 0 ? UiTokens.TextSub : UiTokens.DangerText;
            }

            lamp.OnColor = state.IsReloading ? UiTokens.Accent : UiTokens.Danger;
            lamp.Tick(dt);
            lamp.Set(state.IsReloading || low, state.IsReloading ? 3f : state.Ammo == 0 ? 2f : 0f);

            bar.FillColor = state.IsReloading ? UiTokens.Accent : low ? UiTokens.Danger : UiTokens.TextMain;
            bar.Set(state.IsReloading ? state.ReloadProgress : state.Ammo / (float)Mathf.Max(1, state.MagazineSize), true);
            bar.Tick(dt);
        }
    }
}
