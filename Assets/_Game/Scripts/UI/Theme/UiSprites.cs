using System;
using System.Collections.Generic;
using CoopDeck.Core;
using UnityEngine;

namespace CoopDeck.UI
{
    /// <summary>
    /// The HUD's shapes, drawn in code (no external art, no import settings to keep in sync):
    /// chamfered metal frames with bevels, armour seams and rivets; panel fills; a faint
    /// 術式 circuit pattern; amber lamps; element icons whose silhouettes differ (never colour
    /// alone); rarity pips; the 原脈同調 sync mark; focus brackets. Sprites are white/grey so a
    /// single <c>Image.color</c> tints them. Built once on first use (a few ms) and cached.
    /// </summary>
    public static class UiSprites
    {
        public enum FrameStyle
        {
            /// <summary>Panels: chamfer 12, band 5.</summary>
            Panel = 0,

            /// <summary>Cards: chamfer 10, heavier band 6.</summary>
            Card = 1,

            /// <summary>Chips, key badges, small plates: chamfer 5, band 3.</summary>
            Small = 2,
        }

        static readonly Dictionary<int, Sprite> cache = new Dictionary<int, Sprite>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            cache.Clear();
        }

        static readonly FrameSpec[] FrameSpecs =
        {
            new FrameSpec(64, 12f, 5f),
            new FrameSpec(64, 10f, 6f),
            new FrameSpec(32, 5f, 3f),
        };

        readonly struct FrameSpec
        {
            public readonly int Size;
            public readonly float Cut;
            public readonly float Band;

            public FrameSpec(int size, float cut, float band)
            {
                Size = size;
                Cut = cut;
                Band = band;
            }

            public int Border => Mathf.CeilToInt(Cut + Band) + 2;
        }

        // ------------------------------------------------------------ public sprites

        /// <summary>Chamfered fill (9-sliced). Tint with a panel colour.</summary>
        public static Sprite Fill(FrameStyle style) => Get(100 + (int)style, () => BuildFill(FrameSpecs[(int)style]));

        /// <summary>Chamfered bevelled metal band (9-sliced). Tint with a frame colour.</summary>
        public static Sprite Frame(FrameStyle style) => Get(200 + (int)style, () => BuildFrame(FrameSpecs[(int)style]));

        /// <summary>Four corner brackets (9-sliced): focus / selection.</summary>
        public static Sprite FocusBrackets => Get(300, BuildBrackets);

        /// <summary>Tiled faint circuit / 術式 pattern for panel interiors.</summary>
        public static Sprite Pattern => Get(301, BuildPattern);

        /// <summary>Soft radial glow.</summary>
        public static Sprite Glow => Get(302, BuildGlow);

        /// <summary>Rounded lamp (9-sliced horizontally) with a top highlight.</summary>
        public static Sprite Lamp => Get(303, BuildLamp);

        /// <summary>Diamond: rarity pips, bullets.</summary>
        public static Sprite Diamond => Get(304, BuildDiamond);

        /// <summary>原脈同調 mark: ring, ticks and a four-point star.</summary>
        public static Sprite SyncMark => Get(305, BuildSyncMark);

        /// <summary>Hexagonal socket fill (behind card icons and portraits).</summary>
        public static Sprite HexFill => Get(306, () => BuildHex(false));

        /// <summary>Hexagonal socket ring.</summary>
        public static Sprite HexRing => Get(307, () => BuildHex(true));

        /// <summary>Vertical gradient (transparent bottom → white top) for light falloff on panels.</summary>
        public static Sprite Gradient => Get(308, BuildGradient);

        public static Sprite ShieldIcon => Get(320, () => BuildIcon(ShieldShape));
        public static Sprite ChevronUp => Get(321, () => BuildIcon(p => Chevron(p, true)));
        public static Sprite ChevronDown => Get(322, () => BuildIcon(p => Chevron(p, false)));
        public static Sprite CrossIcon => Get(323, () => BuildIcon(Cross));

        /// <summary>Silhouette icon per element: flame, snowflake, bolt, orbit, diamond.</summary>
        public static Sprite ElementIcon(Element element)
        {
            switch (element)
            {
                case Element.Fire: return Get(400, () => BuildIcon(FlameShape));
                case Element.Ice: return Get(401, () => BuildIcon(SnowflakeShape));
                case Element.Lightning: return Get(402, () => BuildIcon(BoltShape));
                case Element.Gravity: return Get(403, () => BuildIcon(OrbitShape));
                default: return Get(404, () => BuildIcon(NeutralShape));
            }
        }

        /// <summary>The icon shape of an element as an inside test on a 64×64 grid (tests: shapes must differ).</summary>
        public static bool ElementSilhouette(Element element, Vector2 point)
        {
            switch (element)
            {
                case Element.Fire: return FlameShape(point);
                case Element.Ice: return SnowflakeShape(point);
                case Element.Lightning: return BoltShape(point);
                case Element.Gravity: return OrbitShape(point);
                default: return NeutralShape(point);
            }
        }

        /// <summary>9-slice border (pixels) of a frame style, for layout maths.</summary>
        public static float FrameBorder(FrameStyle style) => FrameSpecs[(int)style].Border;

        static Sprite Get(int key, Func<Sprite> build)
        {
            if (!cache.TryGetValue(key, out Sprite sprite) || sprite == null)
            {
                sprite = build();
                cache[key] = sprite;
            }

            return sprite;
        }

        // ------------------------------------------------------------ signed distances (pixels)

        static float Cover(float distance) => Mathf.Clamp01(0.5f - distance);

        static float ChamferBox(Vector2 p, Vector2 centre, Vector2 half, float cut)
        {
            float ax = Mathf.Abs(p.x - centre.x);
            float ay = Mathf.Abs(p.y - centre.y);
            float qx = ax - half.x;
            float qy = ay - half.y;
            float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude;
            float box = outside + Mathf.Min(Mathf.Max(qx, qy), 0f);
            float chamfer = (ax + ay - (half.x + half.y - cut)) * 0.70710678f;
            return Mathf.Max(box, chamfer);
        }

        static float RoundBox(Vector2 p, Vector2 centre, Vector2 half, float radius)
        {
            float qx = Mathf.Abs(p.x - centre.x) - half.x + radius;
            float qy = Mathf.Abs(p.y - centre.y) - half.y + radius;
            return new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
        }

        static float Circle(Vector2 p, Vector2 centre, float radius) => (p - centre).magnitude - radius;

        static float Segment(Vector2 p, Vector2 a, Vector2 b, float halfWidth)
        {
            Vector2 pa = p - a;
            Vector2 ba = b - a;
            float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Mathf.Max(1e-5f, Vector2.Dot(ba, ba)));
            return (pa - ba * h).magnitude - halfWidth;
        }

        static float Hexagon(Vector2 p, Vector2 centre, float radius)
        {
            // Pointy-top hexagon (Inigo Quilez's sdHexagon, rotated).
            Vector2 q = new Vector2(Mathf.Abs(p.y - centre.y), Mathf.Abs(p.x - centre.x));
            Vector2 k = new Vector2(-0.866025404f, 0.5f);
            q -= 2f * Mathf.Min(Vector2.Dot(k, q), 0f) * k;
            q -= new Vector2(Mathf.Clamp(q.x, -0.577350269f * radius, 0.577350269f * radius), radius);
            return q.magnitude * Mathf.Sign(q.y);
        }

        static bool InPolygon(Vector2 p, Vector2[] polygon)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            {
                Vector2 a = polygon[i];
                Vector2 b = polygon[j];
                if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x)
                {
                    inside = !inside;
                }
            }

            return inside;
        }

        // ------------------------------------------------------------ builders

        static Texture2D NewTexture(int width, int height, string name, TextureWrapMode wrap = TextureWrapMode.Clamp)
        {
            return new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = name,
                wrapMode = wrap,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave,
            };
        }

        static Sprite Finish(Texture2D texture, Color32[] pixels, Vector4 border = default)
        {
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            var sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, border);
            sprite.name = texture.name;
            sprite.hideFlags = HideFlags.DontSave;
            return sprite;
        }

        static Color32 Grey(float luminance, float alpha)
        {
            byte l = (byte)(Mathf.Clamp01(luminance) * 255f);
            return new Color32(l, l, l, (byte)(Mathf.Clamp01(alpha) * 255f));
        }

        static Sprite BuildFill(FrameSpec spec)
        {
            int s = spec.Size;
            Texture2D texture = NewTexture(s, s, "UI_Fill_" + s);
            var pixels = new Color32[s * s];
            Vector2 centre = new Vector2(s * 0.5f, s * 0.5f);
            Vector2 half = centre - new Vector2(1.5f, 1.5f); // tucked under the frame's outline
            for (int y = 0; y < s; y++)
            {
                for (int x = 0; x < s; x++)
                {
                    float d = ChamferBox(new Vector2(x + 0.5f, y + 0.5f), centre, half, spec.Cut - 0.6f);
                    pixels[y * s + x] = Grey(1f, Cover(d));
                }
            }

            int b = spec.Border;
            return Finish(texture, pixels, new Vector4(b, b, b, b));
        }

        static Sprite BuildFrame(FrameSpec spec)
        {
            int s = spec.Size;
            Texture2D texture = NewTexture(s, s, "UI_Frame_" + s);
            var pixels = new Color32[s * s];
            Vector2 centre = new Vector2(s * 0.5f, s * 0.5f);
            Vector2 outerHalf = centre - new Vector2(0.25f, 0.25f);
            Vector2 innerHalf = outerHalf - new Vector2(spec.Band, spec.Band);
            float innerCut = Mathf.Max(1f, spec.Cut - spec.Band * 0.41f);
            float seam = spec.Cut + spec.Band * 0.6f;
            float rivetInset = spec.Cut * 0.5f + spec.Band * 0.32f;
            for (int y = 0; y < s; y++)
            {
                for (int x = 0; x < s; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    float outer = ChamferBox(p, centre, outerHalf, spec.Cut);
                    float inner = ChamferBox(p, centre, innerHalf, innerCut);
                    float alpha = Mathf.Clamp01(Cover(outer) - Cover(inner));
                    if (alpha <= 0f)
                    {
                        pixels[y * s + x] = default;
                        continue;
                    }

                    // Bevel: lit from above, a bright line just inside the rim, dark outer rim and inner lip.
                    float lum = Mathf.Lerp(0.66f, 1f, p.y / s);
                    if (outer > -1.1f)
                    {
                        lum *= 0.32f;
                    }
                    else if (outer > -2.1f)
                    {
                        lum = Mathf.Lerp(lum, 1f, p.y > s * 0.5f ? 0.85f : 0.35f);
                    }

                    if (inner < 1.1f)
                    {
                        lum *= 0.55f;
                    }

                    // Armour seams near each corner (stay at the corners when the frame is sliced).
                    bool seamX = Mathf.Abs(x + 0.5f - seam) < 0.6f || Mathf.Abs(s - (x + 0.5f) - seam) < 0.6f;
                    bool seamY = Mathf.Abs(y + 0.5f - seam) < 0.6f || Mathf.Abs(s - (y + 0.5f) - seam) < 0.6f;
                    bool inHorizontalBand = p.y < spec.Band + 1f || p.y > s - spec.Band - 1f;
                    bool inVerticalBand = p.x < spec.Band + 1f || p.x > s - spec.Band - 1f;
                    if (seamX && inHorizontalBand || seamY && inVerticalBand)
                    {
                        lum *= 0.38f;
                    }

                    // Rivets on the chamfers.
                    float rivet = float.MaxValue;
                    for (int c = 0; c < 4; c++)
                    {
                        float cx = (c & 1) == 0 ? rivetInset : s - rivetInset;
                        float cy = (c & 2) == 0 ? rivetInset : s - rivetInset;
                        rivet = Mathf.Min(rivet, Circle(p, new Vector2(cx, cy), Mathf.Max(0.8f, spec.Band * 0.24f)));
                    }

                    lum = Mathf.Lerp(lum, 1f, Cover(rivet) * 0.9f);
                    pixels[y * s + x] = Grey(lum, alpha);
                }
            }

            int b = spec.Border;
            return Finish(texture, pixels, new Vector4(b, b, b, b));
        }

        static Sprite BuildBrackets()
        {
            const int s = 48;
            const float arm = 14f;
            const float width = 3f;
            Texture2D texture = NewTexture(s, s, "UI_FocusBrackets");
            var pixels = new Color32[s * s];
            for (int y = 0; y < s; y++)
            {
                for (int x = 0; x < s; x++)
                {
                    float px = Mathf.Min(x + 0.5f, s - x - 0.5f);
                    float py = Mathf.Min(y + 0.5f, s - y - 0.5f);
                    var p = new Vector2(px, py);
                    float horizontal = RoundBox(p, new Vector2(arm * 0.5f, width * 0.5f), new Vector2(arm * 0.5f, width * 0.5f), 0.5f);
                    float vertical = RoundBox(p, new Vector2(width * 0.5f, arm * 0.5f), new Vector2(width * 0.5f, arm * 0.5f), 0.5f);
                    pixels[y * s + x] = Grey(1f, Cover(Mathf.Min(horizontal, vertical)));
                }
            }

            return Finish(texture, pixels, new Vector4(18f, 18f, 18f, 18f));
        }

        static Sprite BuildPattern()
        {
            const int s = 96;
            Texture2D texture = NewTexture(s, s, "UI_Pattern", TextureWrapMode.Repeat);
            var pixels = new Color32[s * s];
            var c = new Vector2(48f, 48f);
            for (int y = 0; y < s; y++)
            {
                for (int x = 0; x < s; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    // Circuit traces with 45° bends, a ring node and small via dots; the tile wraps seamlessly.
                    float d = Segment(p, new Vector2(0f, 20f), new Vector2(30f, 20f), 0.5f);
                    d = Mathf.Min(d, Segment(p, new Vector2(30f, 20f), new Vector2(40f, 30f), 0.5f));
                    d = Mathf.Min(d, Segment(p, new Vector2(56f, 66f), new Vector2(66f, 76f), 0.5f));
                    d = Mathf.Min(d, Segment(p, new Vector2(66f, 76f), new Vector2(96f, 76f), 0.5f));
                    d = Mathf.Min(d, Segment(p, new Vector2(76f, 0f), new Vector2(76f, 34f), 0.5f));
                    d = Mathf.Min(d, Segment(p, new Vector2(76f, 34f), new Vector2(62f, 48f), 0.5f));
                    d = Mathf.Min(d, Segment(p, new Vector2(20f, 96f), new Vector2(20f, 62f), 0.5f));
                    d = Mathf.Min(d, Mathf.Abs(Circle(p, c, 11f)) - 0.5f);
                    d = Mathf.Min(d, Mathf.Abs(Circle(p, c, 5f)) - 0.5f);
                    d = Mathf.Min(d, Circle(p, new Vector2(30f, 20f), 1.6f));
                    d = Mathf.Min(d, Circle(p, new Vector2(66f, 76f), 1.6f));
                    d = Mathf.Min(d, Circle(p, new Vector2(20f, 62f), 1.6f));
                    d = Mathf.Min(d, Circle(p, new Vector2(76f, 34f), 1.6f));
                    pixels[y * s + x] = Grey(1f, Cover(d));
                }
            }

            return Finish(texture, pixels);
        }

        static Sprite BuildGlow()
        {
            const int s = 64;
            Texture2D texture = NewTexture(s, s, "UI_Glow");
            var pixels = new Color32[s * s];
            for (int y = 0; y < s; y++)
            {
                for (int x = 0; x < s; x++)
                {
                    float r = new Vector2((x + 0.5f) / s * 2f - 1f, (y + 0.5f) / s * 2f - 1f).magnitude;
                    float a = Mathf.Clamp01(1f - r);
                    pixels[y * s + x] = Grey(1f, a * a * (3f - 2f * a));
                }
            }

            return Finish(texture, pixels);
        }

        static Sprite BuildLamp()
        {
            const int w = 28;
            const int h = 14;
            Texture2D texture = NewTexture(w, h, "UI_Lamp");
            var pixels = new Color32[w * h];
            var centre = new Vector2(w * 0.5f, h * 0.5f);
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    float d = RoundBox(p, centre, centre - new Vector2(0.5f, 0.5f), 6f);
                    float lum = Mathf.Lerp(0.72f, 1f, p.y / h);
                    if (d > -1.2f)
                    {
                        lum *= 0.45f;
                    }

                    float highlight = Cover(RoundBox(p, new Vector2(w * 0.5f, h - 4.5f), new Vector2(w * 0.5f - 5f, 1.2f), 1.2f));
                    lum = Mathf.Lerp(lum, 1f, highlight * 0.7f);
                    pixels[y * w + x] = Grey(lum, Cover(d));
                }
            }

            return Finish(texture, pixels, new Vector4(7f, 0f, 7f, 0f));
        }

        static Sprite BuildDiamond()
        {
            const int s = 20;
            Texture2D texture = NewTexture(s, s, "UI_Diamond");
            var pixels = new Color32[s * s];
            for (int y = 0; y < s; y++)
            {
                for (int x = 0; x < s; x++)
                {
                    float d = (Mathf.Abs(x + 0.5f - s * 0.5f) + Mathf.Abs(y + 0.5f - s * 0.5f) - (s * 0.5f - 1f)) * 0.7071f;
                    pixels[y * s + x] = Grey(1f, Cover(d));
                }
            }

            return Finish(texture, pixels);
        }

        static Sprite BuildSyncMark()
        {
            return BuildIcon(p =>
            {
                var c = new Vector2(32f, 32f);
                float ring = Mathf.Abs(Circle(p, c, 26f)) - 2.2f;
                float star = (Mathf.Abs(p.x - c.x) * 0.38f + Mathf.Abs(p.y - c.y) - 17f) * 0.9f;
                star = Mathf.Min(star, (Mathf.Abs(p.x - c.x) + Mathf.Abs(p.y - c.y) * 0.38f - 17f) * 0.9f);
                float ticks = float.MaxValue;
                for (int i = 0; i < 4; i++)
                {
                    float angle = (45f + 90f * i) * Mathf.Deg2Rad;
                    var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                    ticks = Mathf.Min(ticks, Segment(p, c + dir * 18f, c + dir * 23f, 1.4f));
                }

                return Mathf.Min(ring, Mathf.Min(star, ticks)) < 0f;
            });
        }

        static Sprite BuildHex(bool ring)
        {
            const int s = 64;
            Texture2D texture = NewTexture(s, s, ring ? "UI_HexRing" : "UI_HexFill");
            var pixels = new Color32[s * s];
            var c = new Vector2(32f, 32f);
            for (int y = 0; y < s; y++)
            {
                for (int x = 0; x < s; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    float d = Hexagon(p, c, 29.5f);
                    float alpha = ring ? Cover(Mathf.Abs(d + 1.6f) - 1.6f) : Cover(d);
                    float lum = ring ? Mathf.Lerp(0.7f, 1f, p.y / s) : 1f;
                    pixels[y * s + x] = Grey(lum, alpha);
                }
            }

            return Finish(texture, pixels);
        }

        static Sprite BuildGradient()
        {
            const int w = 4;
            const int h = 64;
            Texture2D texture = NewTexture(w, h, "UI_Gradient");
            var pixels = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                float t = (y + 0.5f) / h;
                for (int x = 0; x < w; x++)
                {
                    pixels[y * w + x] = Grey(1f, t * t);
                }
            }

            return Finish(texture, pixels);
        }

        /// <summary>64×64 icon from an inside test, 4×4 supersampled for clean edges.</summary>
        static Sprite BuildIcon(Func<Vector2, bool> inside)
        {
            const int s = 64;
            const int ss = 4;
            Texture2D texture = NewTexture(s, s, "UI_Icon");
            var pixels = new Color32[s * s];
            for (int y = 0; y < s; y++)
            {
                for (int x = 0; x < s; x++)
                {
                    int hits = 0;
                    for (int sy = 0; sy < ss; sy++)
                    {
                        for (int sx = 0; sx < ss; sx++)
                        {
                            if (inside(new Vector2(x + (sx + 0.5f) / ss, y + (sy + 0.5f) / ss)))
                            {
                                hits++;
                            }
                        }
                    }

                    pixels[y * s + x] = Grey(1f, hits / (float)(ss * ss));
                }
            }

            return Finish(texture, pixels);
        }

        // ------------------------------------------------------------ icon shapes (64×64, y up)

        static bool Teardrop(Vector2 p, Vector2 bulb, float radius, float tipY, float lean)
        {
            if ((p - bulb).magnitude < radius)
            {
                return true;
            }

            if (p.y < bulb.y || p.y > tipY)
            {
                return false;
            }

            float t = (p.y - bulb.y) / (tipY - bulb.y);
            float halfWidth = radius * Mathf.Pow(1f - t, 1.35f) * Mathf.Sqrt(Mathf.Max(0f, 1f - 0.15f * t));
            float axis = bulb.x + lean * t * t;
            return Mathf.Abs(p.x - axis) < halfWidth;
        }

        static bool FlameShape(Vector2 p)
        {
            bool outer = Teardrop(p, new Vector2(32f, 23f), 18f, 61f, 6f);
            bool inner = Teardrop(p, new Vector2(31f, 17f), 7.5f, 38f, -3f);
            return outer && !inner;
        }

        static bool SnowflakeShape(Vector2 p)
        {
            var c = new Vector2(32f, 32f);
            float d = float.MaxValue;
            for (int i = 0; i < 6; i++)
            {
                float angle = (90f + 60f * i) * Mathf.Deg2Rad;
                var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                var side = new Vector2(-dir.y, dir.x);
                d = Mathf.Min(d, Segment(p, c, c + dir * 27f, 2.8f));
                Vector2 branch = c + dir * 15f;
                d = Mathf.Min(d, Segment(p, branch, branch + (dir + side) * 6.5f, 2.2f));
                d = Mathf.Min(d, Segment(p, branch, branch + (dir - side) * 6.5f, 2.2f));
            }

            return d < 0f;
        }

        static readonly Vector2[] Bolt =
        {
            new Vector2(33f, 62f), new Vector2(13f, 30f), new Vector2(28f, 30f), new Vector2(19f, 2f),
            new Vector2(51f, 38f), new Vector2(35f, 38f), new Vector2(50f, 62f),
        };

        static bool BoltShape(Vector2 p) => InPolygon(p, Bolt);

        static bool OrbitShape(Vector2 p)
        {
            var c = new Vector2(32f, 32f);
            if (Circle(p, c, 10f) < 0f)
            {
                return true;
            }

            // A tilted orbit ring with a gap around the core.
            Vector2 q = p - c;
            float angle = -24f * Mathf.Deg2Rad;
            var r = new Vector2(q.x * Mathf.Cos(angle) - q.y * Mathf.Sin(angle), q.x * Mathf.Sin(angle) + q.y * Mathf.Cos(angle));
            float ellipse = new Vector2(r.x / 28f, r.y / 11f).magnitude;
            bool onRing = Mathf.Abs(ellipse - 1f) < 0.13f;
            return onRing && q.magnitude > 13.5f;
        }

        static bool NeutralShape(Vector2 p)
        {
            var c = new Vector2(32f, 32f);
            float diamond = (Mathf.Abs(p.x - c.x) + Mathf.Abs(p.y - c.y) - 26f) * 0.7071f;
            return Mathf.Abs(diamond + 2.5f) < 2.5f || Circle(p, c, 6f) < 0f;
        }

        static readonly Vector2[] Shield =
        {
            new Vector2(32f, 60f), new Vector2(54f, 52f), new Vector2(52f, 26f), new Vector2(32f, 4f),
            new Vector2(12f, 26f), new Vector2(10f, 52f),
        };

        static bool ShieldShape(Vector2 p) => InPolygon(p, Shield);

        static bool Chevron(Vector2 p, bool up)
        {
            float y = up ? p.y : 64f - p.y;
            float d = Mathf.Min(Segment(new Vector2(p.x, y), new Vector2(10f, 22f), new Vector2(32f, 44f), 5.5f),
                Segment(new Vector2(p.x, y), new Vector2(32f, 44f), new Vector2(54f, 22f), 5.5f));
            return d < 0f;
        }

        static bool Cross(Vector2 p)
        {
            return Mathf.Abs(p.x - 32f) < 7f && Mathf.Abs(p.y - 32f) < 24f || Mathf.Abs(p.y - 32f) < 7f && Mathf.Abs(p.x - 32f) < 24f;
        }
    }
}
