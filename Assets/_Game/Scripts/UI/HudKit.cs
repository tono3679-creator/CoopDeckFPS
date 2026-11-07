using System;
using UnityEngine;
using UnityEngine.UI;

namespace CoopDeck.UI
{
    /// <summary>Shared helpers for the code-built HUD (font with Japanese glyphs, element factories).</summary>
    public static class HudKit
    {
        static Font font;
        static Sprite whiteSprite;

        public static readonly Color PanelColor = new Color(0.08f, 0.06f, 0.16f, 0.72f);

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
                    var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = "HudWhite" };
                    var pixels = new Color32[16];
                    for (int i = 0; i < pixels.Length; i++)
                    {
                        pixels[i] = new Color32(255, 255, 255, 255);
                    }

                    texture.SetPixels32(pixels);
                    texture.Apply();
                    whiteSprite = Sprite.Create(texture, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f), 100f);
                }

                return whiteSprite;
            }
        }

        public static RectTransform CreateCanvas(Transform parent, string name, int sortingOrder)
        {
            var canvasGo = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(parent, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            return (RectTransform)canvasGo.transform;
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

        public static Text CreateText(string name, Transform parent, int fontSize, TextAnchor alignment, Vector2 anchor, Vector2 position, Vector2 size)
        {
            RectTransform rect = CreateRect(name, parent, anchor, position, size);
            var text = rect.gameObject.AddComponent<Text>();
            text.font = Font;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = Color.white;
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

        public static void AddOutline(Graphic graphic, float alpha = 0.6f)
        {
            graphic.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, alpha);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            font = null;
            whiteSprite = null;
        }
    }
}
