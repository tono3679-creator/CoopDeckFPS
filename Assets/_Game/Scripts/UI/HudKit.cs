using System;
using UnityEngine;
using UnityEngine.UI;

namespace CoopDeck.UI
{
    /// <summary>Shared helpers for the code-built HUD (font with Japanese glyphs, canvases, element factories).</summary>
    public static class HudKit
    {
        public static readonly Vector2 ReferenceResolution = new Vector2(1920f, 1080f);

        static Font font;
        static Sprite whiteSprite;

        /// <summary>Default panel colour (UI tokens: panel slate, nearly opaque).</summary>
        public static Color PanelColor => UiTokens.PanelTranslucent;

        /// <summary>OS fonts with Japanese glyphs first (Windows), then Unity's built-in font.</summary>
        public static Font Font
        {
            get
            {
                if (font != null)
                {
                    return font;
                }

                try
                {
                    font = Font.CreateDynamicFontFromOSFont(new[] { "Yu Gothic UI", "Meiryo UI", "Meiryo", "MS Gothic", "Arial" }, 32);
                }
                catch (Exception)
                {
                    font = null;
                }

                if (font == null)
                {
                    font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                }

                return font;
            }
        }

        /// <summary>A white 4×4 sprite for filled/tinted images that need a real sprite.</summary>
        public static Sprite WhiteSprite
        {
            get
            {
                if (whiteSprite == null)
                {
                    var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = "HudWhite", hideFlags = HideFlags.DontSave };
                    var pixels = new Color32[16];
                    for (int i = 0; i < pixels.Length; i++)
                    {
                        pixels[i] = new Color32(255, 255, 255, 255);
                    }

                    texture.SetPixels32(pixels);
                    texture.Apply();
                    whiteSprite = Sprite.Create(texture, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f), 100f);
                    whiteSprite.hideFlags = HideFlags.DontSave;
                }

                return whiteSprite;
            }
        }

        /// <summary>
        /// A screen-space overlay canvas. The scaler "expands" from 1920×1080, so the whole
        /// reference layout is visible at any aspect (720p = 1080p scaled, 16:10 gets extra
        /// height, ultrawide extra width). With <paramref name="safeFrame"/> the returned rect is a
        /// child clamped to 16:9 and centred, so corner panels stay near the centre on ultrawide
        /// screens; full-screen effects (vignettes, world markers) pass false.
        /// </summary>
        public static RectTransform CreateCanvas(Transform parent, string name, int sortingOrder, bool safeFrame = true)
        {
            var canvasGo = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(parent, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            canvas.pixelPerfect = false;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = ReferenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            var root = (RectTransform)canvasGo.transform;
            return safeFrame ? CreateSafeFrame(root) : root;
        }

        /// <summary>A full-height child of a canvas whose width is clamped to the HUD aspect (see <see cref="HudSafeFrame"/>).</summary>
        public static RectTransform CreateSafeFrame(RectTransform canvasRoot)
        {
            var go = new GameObject("SafeFrame", typeof(RectTransform), typeof(HudSafeFrame));
            go.transform.SetParent(canvasRoot, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(ReferenceResolution.x, 0f);
            go.GetComponent<HudSafeFrame>().Refresh();
            return rect;
        }

        public static RectTransform CreateRect(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        /// <summary>A rect placed by its pivot (e.g. pivot (0,0) + anchor (0,0) = bottom-left corner at <paramref name="position"/>).</summary>
        public static RectTransform CreateRect(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            RectTransform rect = CreateRect(name, parent, anchor, position, size);
            rect.pivot = pivot;
            return rect;
        }

        /// <summary>Stretches a rect over its parent with an inset (negative = outset).</summary>
        public static RectTransform Stretch(RectTransform rect, float inset = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(-2f * inset, -2f * inset);
            return rect;
        }

        public static Text CreateText(string name, Transform parent, int fontSize, TextAnchor alignment, Vector2 anchor, Vector2 position, Vector2 size)
        {
            RectTransform rect = CreateRect(name, parent, anchor, position, size);
            var text = rect.gameObject.AddComponent<Text>();
            text.font = Font;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = UiTokens.TextMain;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        public static Image CreateImage(string name, Transform parent, Color color, Vector2 anchor, Vector2 position, Vector2 size)
        {
            RectTransform rect = CreateRect(name, parent, anchor, position, size);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = WhiteSprite;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>Four-way outline: for text drawn straight over the 3D scene.</summary>
        public static void AddOutline(Graphic graphic, float alpha = 0.6f)
        {
            graphic.gameObject.AddComponent<Outline>().effectColor = new Color(UiTokens.FrameDark.r, UiTokens.FrameDark.g, UiTokens.FrameDark.b, alpha);
        }

        /// <summary>A soft drop shadow: for text on panels (half the vertices of an outline).</summary>
        public static void AddShadow(Graphic graphic, float alpha = 0.7f, float distance = 1.6f)
        {
            var shadow = graphic.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(UiTokens.FrameDark.r, UiTokens.FrameDark.g, UiTokens.FrameDark.b, alpha);
            shadow.effectDistance = new Vector2(distance, -distance);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            font = null;
            whiteSprite = null;
        }
    }
}
