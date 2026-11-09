using CoopDeck.Core;
using UnityEngine;
using UnityEngine.UI;

namespace CoopDeck.UI
{
    /// <summary>A framed terminal panel: chamfered fill, faint circuit pattern, bevelled metal frame.</summary>
    public sealed class PanelView
    {
        public RectTransform Root;
        public Image Fill;
        public Image Pattern;
        public Image Frame;

        public void SetFrameColor(Color color) => Frame.color = color;
    }

    /// <summary>An amber operation lamp with a soft glow; lit / unlit / blinking.</summary>
    public sealed class LampView
    {
        public RectTransform Root;
        public Image Lamp;
        public Image Glow;
        public Color OnColor = UiTokens.Accent;
        float flash;

        public void Set(bool lit, float blinkRate = 0f)
        {
            bool on = lit && (blinkRate <= 0f || Mathf.Repeat(Time.unscaledTime * blinkRate, 1f) < 0.6f);
            float boost = Mathf.Clamp01(flash);
            Lamp.color = on || boost > 0f ? Color.Lerp(OnColor, Color.white, boost * 0.5f) : UiTokens.LampOff;
            Glow.color = UiTokens.WithAlpha(OnColor, on ? 0.35f + 0.4f * boost : 0.4f * boost);
        }

        /// <summary>Short bright flash (button press, notification).</summary>
        public void Flash() => flash = 1f;

        public void Tick(float dt) => flash = Mathf.Max(0f, flash - dt * 4f);
    }

    /// <summary>
    /// A status bar with a delayed "ghost" segment and a short glow when it drops
    /// (HP, shield, card gauge). Plain object: its owner calls <see cref="Tick"/>.
    /// </summary>
    public sealed class HudBar
    {
        const float GhostDelay = 0.28f;
        const float GhostSpeed = 1.6f;
        const float GlowTime = 0.4f;

        public RectTransform Root;
        public Image Back;
        public Image Ghost;
        public Image Fill;
        public Image Glow;
        public Color FillColor = Color.white;
        public Color GhostColor = UiTokens.WithAlpha(UiTokens.TextMain, 0.75f);

        float shown = -1f;
        float ghost;
        float ghostWait;
        float glow;

        public float Value => Mathf.Max(0f, shown);

        /// <summary>Sets the fill (0–1). A drop leaves a ghost that catches up and flashes the bar.</summary>
        public void Set(float normalized, bool instant = false)
        {
            normalized = Mathf.Clamp01(normalized);
            if (shown < 0f || instant)
            {
                shown = ghost = normalized;
                Apply();
                return;
            }

            if (normalized < shown - 0.0001f)
            {
                ghost = Mathf.Max(ghost, shown);
                ghostWait = GhostDelay;
                glow = 1f;
            }
            else if (normalized > ghost)
            {
                ghost = normalized;
            }

            shown = normalized;
            Apply();
        }

        public void Tick(float dt)
        {
            if (ghostWait > 0f)
            {
                ghostWait -= dt;
            }
            else if (ghost > shown)
            {
                ghost = Mathf.Max(shown, ghost - GhostSpeed * dt);
            }

            glow = Mathf.Max(0f, glow - dt / GlowTime);
            Apply();
        }

        void Apply()
        {
            Fill.rectTransform.localScale = new Vector3(Mathf.Max(0f, shown), 1f, 1f);
            Fill.color = glow > 0f ? Color.Lerp(FillColor, Color.white, glow * 0.45f) : FillColor;
            if (Ghost != null)
            {
                Ghost.rectTransform.localScale = new Vector3(Mathf.Max(0f, ghost), 1f, 1f);
                Ghost.enabled = ghost > shown + 0.001f;
                Ghost.color = GhostColor;
            }

            if (Glow != null)
            {
                Glow.color = UiTokens.WithAlpha(FillColor, glow * 0.55f);
                Glow.enabled = glow > 0f;
            }
        }
    }

    /// <summary>Factories for the shared HUD parts. All colours and sizes come from <see cref="UiTokens"/>.</summary>
    public static class UiWidgets
    {
        static readonly Vector2 Centre = new Vector2(0.5f, 0.5f);

        public static PanelView Panel(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size,
            UiSprites.FrameStyle style = UiSprites.FrameStyle.Panel, bool pattern = true, Color? fill = null, Color? frame = null)
        {
            var view = new PanelView { Root = HudKit.CreateRect(name, parent, anchor, pivot, position, size) };
            view.Fill = SlicedImage("Fill", view.Root, UiSprites.Fill(style), fill ?? UiTokens.PanelTranslucent);
            HudKit.Stretch(view.Fill.rectTransform);
            if (pattern)
            {
                RectTransform patternRect = HudKit.CreateRect("Pattern", view.Root, Centre, Vector2.zero, Vector2.zero);
                HudKit.Stretch(patternRect, UiSprites.FrameBorder(style) * 0.45f);
                view.Pattern = patternRect.gameObject.AddComponent<Image>();
                view.Pattern.sprite = UiSprites.Pattern;
                view.Pattern.type = Image.Type.Tiled;
                view.Pattern.color = UiTokens.WithAlpha(UiTokens.Energy, 0.05f);
                view.Pattern.raycastTarget = false;
            }

            view.Frame = SlicedImage("Frame", view.Root, UiSprites.Frame(style), frame ?? UiTokens.FrameSteel);
            HudKit.Stretch(view.Frame.rectTransform);
            return view;
        }

        public static Image SlicedImage(string name, Transform parent, Sprite sprite, Color color)
        {
            RectTransform rect = HudKit.CreateRect(name, parent, Centre, Vector2.zero, Vector2.zero);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.fillCenter = true;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        public static Image Icon(string name, Transform parent, Sprite sprite, Color color, Vector2 anchor, Vector2 position, float size)
        {
            Image image = HudKit.CreateImage(name, parent, color, anchor, position, new Vector2(size, size));
            image.sprite = sprite;
            image.preserveAspect = true;
            return image;
        }

        public static Image ElementIcon(string name, Transform parent, Element element, Vector2 anchor, Vector2 position, float size)
        {
            return Icon(name, parent, UiSprites.ElementIcon(element), UiTokens.ElementColor(element), anchor, position, size);
        }

        /// <summary>Text on a panel: token colour, soft shadow, optional bold.</summary>
        public static Text Label(string name, Transform parent, int fontSize, Color color, TextAnchor alignment, Vector2 anchor, Vector2 position,
            Vector2 size, bool bold = false, bool shadow = true)
        {
            Text text = HudKit.CreateText(name, parent, Mathf.Max(UiTokens.MinimumFontSize - 2, fontSize), alignment, anchor, position, size);
            text.color = color;
            text.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            if (shadow)
            {
                HudKit.AddShadow(text);
            }

            return text;
        }

        /// <summary>Text that auto-shrinks to its box (long card names) down to the minimum size.</summary>
        public static void FitText(Text text, int maxSize)
        {
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.resizeTextForBestFit = true;
            text.resizeTextMaxSize = maxSize;
            text.resizeTextMinSize = UiTokens.MinimumFontSize - 2;
        }

        public static LampView Lamp(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size)
        {
            var view = new LampView { Root = HudKit.CreateRect(name, parent, anchor, position, size) };
            view.Glow = Icon("Glow", view.Root, UiSprites.Glow, UiTokens.WithAlpha(UiTokens.Accent, 0f), Centre, Vector2.zero, Mathf.Max(size.x, size.y) * 2.6f);
            view.Glow.preserveAspect = false;
            view.Glow.rectTransform.sizeDelta = size * 2.6f;
            view.Lamp = SlicedImage("Lamp", view.Root, UiSprites.Lamp, UiTokens.LampOff);
            HudKit.Stretch(view.Lamp.rectTransform);
            view.Set(false);
            return view;
        }

        /// <summary>
        /// A horizontal bar: dark back with a 1 px rim, ghost, fill and glow. <paramref name="segments"/>
        /// draws evenly spaced ticks (HP chunks, card uses).
        /// </summary>
        public static HudBar Bar(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size, Color fill, int segments = 0)
        {
            var bar = new HudBar { FillColor = fill };
            bar.Root = HudKit.CreateRect(name, parent, anchor, pivot, position, size);
            Image rim = HudKit.CreateImage("Rim", bar.Root, UiTokens.WithAlpha(UiTokens.FrameDark, 0.95f), Centre, Vector2.zero, Vector2.zero);
            HudKit.Stretch(rim.rectTransform, -1.5f);
            bar.Back = HudKit.CreateImage("Back", bar.Root, UiTokens.WithAlpha(UiTokens.PanelDeep, 0.95f), Centre, Vector2.zero, Vector2.zero);
            HudKit.Stretch(bar.Back.rectTransform);
            bar.Glow = HudKit.CreateImage("Glow", bar.Root, UiTokens.WithAlpha(fill, 0f), Centre, Vector2.zero, Vector2.zero);
            bar.Glow.sprite = UiSprites.Glow;
            HudKit.Stretch(bar.Glow.rectTransform, -8f);
            bar.Ghost = LeftFill("Ghost", bar.Root, bar.GhostColor);
            bar.Fill = LeftFill("Fill", bar.Root, fill);
            Image sheen = HudKit.CreateImage("Sheen", bar.Fill.rectTransform, UiTokens.WithAlpha(Color.white, 0.22f), new Vector2(0.5f, 1f), Vector2.zero,
                new Vector2(0f, Mathf.Max(1f, size.y * 0.3f)));
            sheen.rectTransform.anchorMin = new Vector2(0f, 1f);
            sheen.rectTransform.anchorMax = new Vector2(1f, 1f);
            sheen.rectTransform.pivot = new Vector2(0.5f, 1f);
            SetTicks(bar, segments);
            return bar;
        }

        /// <summary>Replaces the tick marks of a bar (e.g. when a card with another cost is drawn).</summary>
        public static void SetTicks(HudBar bar, int segments)
        {
            Transform old = bar.Root.Find("Ticks");
            if (old != null)
            {
                Object.Destroy(old.gameObject);
            }

            if (segments < 2)
            {
                return;
            }

            RectTransform ticks = HudKit.CreateRect("Ticks", bar.Root, Centre, Vector2.zero, Vector2.zero);
            HudKit.Stretch(ticks);
            for (int i = 1; i < segments; i++)
            {
                Image tick = HudKit.CreateImage("Tick", ticks, UiTokens.WithAlpha(UiTokens.FrameDark, 0.8f), new Vector2(i / (float)segments, 0.5f), Vector2.zero, new Vector2(2f, 0f));
                tick.rectTransform.anchorMin = new Vector2(i / (float)segments, 0f);
                tick.rectTransform.anchorMax = new Vector2(i / (float)segments, 1f);
            }
        }

        static Image LeftFill(string name, Transform parent, Color color)
        {
            Image image = HudKit.CreateImage(name, parent, color, Centre, Vector2.zero, Vector2.zero);
            RectTransform rect = image.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0f, 0.5f);
            rect.sizeDelta = Vector2.zero;
            rect.anchoredPosition = Vector2.zero;
            return image;
        }

        /// <summary>A small framed plate holding a key / button glyph.</summary>
        public static Text KeyBadge(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size, out PanelView plate)
        {
            plate = Panel(name, parent, anchor, Centre, position, size, UiSprites.FrameStyle.Small, false, UiTokens.WithAlpha(UiTokens.Background, 0.92f),
                UiTokens.FrameLight);
            Text text = Label("Glyph", plate.Root, UiTokens.Title, UiTokens.TextMain, TextAnchor.MiddleCenter, Centre, new Vector2(0f, 1f), size, true);
            FitText(text, UiTokens.Title);
            return text;
        }

        /// <summary>Corner brackets around a rect: focus and selection (works without colour: it is a shape).</summary>
        public static Image FocusBrackets(string name, Transform parent, Color color, float outset = 8f)
        {
            Image image = SlicedImage(name, parent, UiSprites.FocusBrackets, color);
            image.fillCenter = false;
            HudKit.Stretch(image.rectTransform, -outset);
            return image;
        }

        /// <summary>A status chip: small plate, icon and a short value (buff timer, debuff, element aura).</summary>
        public static Text Chip(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 position, Sprite icon, Color color, out RectTransform root, out Image iconImage)
        {
            PanelView plate = Panel(name, parent, anchor, pivot, position, new Vector2(96f, 32f), UiSprites.FrameStyle.Small, false,
                UiTokens.WithAlpha(UiTokens.Background, 0.88f), UiTokens.WithAlpha(color, 0.9f));
            root = plate.Root;
            iconImage = Icon("Icon", root, icon, color, new Vector2(0f, 0.5f), new Vector2(18f, 0f), 20f);
            Text value = Label("Value", root, UiTokens.Caption, UiTokens.TextMain, TextAnchor.MiddleLeft, new Vector2(0f, 0.5f), new Vector2(60f, 0f),
                new Vector2(64f, 28f), true);
            return value;
        }
    }
}
