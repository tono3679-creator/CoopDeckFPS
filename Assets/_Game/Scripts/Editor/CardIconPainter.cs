using System.IO;
using UnityEditor;
using UnityEngine;

namespace CoopDeck.EditorTools
{
    public enum CardGlyph
    {
        Spear,
        Bolt,
        Vortex,
        Collapse,
        Meteor,
        Shield,
        Cross,
        Crosshair,
        Nova,
        ArrowUp,
        Grenade,
        Spike,
    }

    /// <summary>
    /// Draws placeholder card icons (element-colored tile + white glyph) with anti-aliased signed
    /// distance shapes and saves them as sprites. No external art; artists can overwrite the PNGs.
    /// </summary>
    public static class CardIconPainter
    {
        const int Size = 128;

        public static Sprite EnsureIcon(string assetPath, CardGlyph glyph, Color elementColor, bool overwrite)
        {
            EditorUtil.EnsureFolder(Path.GetDirectoryName(assetPath)?.Replace('\\', '/'));
            if (overwrite || !File.Exists(assetPath))
            {
                var canvas = new IconCanvas(Size);
                canvas.Background(elementColor);
                canvas.Shadowed(c => DrawGlyph(c, glyph, elementColor));
                File.WriteAllBytes(assetPath, canvas.ToPng());
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);

                var importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        }

        static void DrawGlyph(IconCanvas c, CardGlyph glyph, Color element)
        {
            switch (glyph)
            {
                case CardGlyph.Spear:
                    c.Segment(0.2f, 0.2f, 0.66f, 0.66f, 0.07f);
                    c.Polygon(new[] { new Vector2(0.84f, 0.84f), new Vector2(0.58f, 0.74f), new Vector2(0.74f, 0.58f) });
                    c.Polygon(new[] { new Vector2(0.24f, 0.6f), new Vector2(0.3f, 0.7f), new Vector2(0.36f, 0.6f), new Vector2(0.3f, 0.5f) });
                    break;
                case CardGlyph.Bolt:
                    c.Polygon(new[]
                    {
                        new Vector2(0.6f, 0.92f), new Vector2(0.28f, 0.48f), new Vector2(0.47f, 0.48f), new Vector2(0.36f, 0.08f),
                        new Vector2(0.74f, 0.56f), new Vector2(0.54f, 0.56f), new Vector2(0.7f, 0.92f),
                    });
                    c.Circle(0.16f, 0.24f, 0.06f);
                    c.Circle(0.84f, 0.3f, 0.05f);
                    break;
                case CardGlyph.Vortex:
                    c.Arc(0.5f, 0.5f, 0.34f, 0.065f, 20f, 300f);
                    c.Arc(0.5f, 0.5f, 0.23f, 0.06f, 140f, 420f);
                    c.Arc(0.5f, 0.5f, 0.12f, 0.055f, 260f, 520f);
                    break;
                case CardGlyph.Collapse:
                    c.Ring(0.5f, 0.5f, 0.36f, 0.035f);
                    c.Ring(0.5f, 0.5f, 0.25f, 0.045f);
                    c.Circle(0.5f, 0.5f, 0.12f);
                    for (int i = 0; i < 4; i++)
                    {
                        float a = (45f + 90f * i) * Mathf.Deg2Rad;
                        c.Segment(0.5f + Mathf.Cos(a) * 0.46f, 0.5f + Mathf.Sin(a) * 0.46f, 0.5f + Mathf.Cos(a) * 0.31f, 0.5f + Mathf.Sin(a) * 0.31f, 0.05f);
                    }

                    break;
                case CardGlyph.Meteor:
                    c.Segment(0.2f, 0.84f, 0.56f, 0.46f, 0.1f);
                    c.Segment(0.34f, 0.9f, 0.6f, 0.56f, 0.05f);
                    c.Segment(0.12f, 0.7f, 0.48f, 0.42f, 0.05f);
                    c.Circle(0.62f, 0.38f, 0.18f);
                    break;
                case CardGlyph.Shield:
                {
                    Vector2[] outer = { new Vector2(0.5f, 0.9f), new Vector2(0.82f, 0.78f), new Vector2(0.78f, 0.42f), new Vector2(0.5f, 0.1f), new Vector2(0.22f, 0.42f), new Vector2(0.18f, 0.78f) };
                    c.Polygon(outer);
                    c.Paint = Darken(element, 0.75f);
                    c.Polygon(Scale(outer, 0.62f));
                    c.Paint = Color.white;
                    c.Segment(0.5f, 0.32f, 0.5f, 0.72f, 0.07f);
                    break;
                }
                case CardGlyph.Cross:
                    c.Ring(0.5f, 0.5f, 0.4f, 0.04f);
                    c.Segment(0.5f, 0.24f, 0.5f, 0.76f, 0.17f);
                    c.Segment(0.24f, 0.5f, 0.76f, 0.5f, 0.17f);
                    break;
                case CardGlyph.Crosshair:
                    c.Ring(0.5f, 0.5f, 0.28f, 0.06f);
                    c.Segment(0.5f, 0.06f, 0.5f, 0.3f, 0.06f);
                    c.Segment(0.5f, 0.7f, 0.5f, 0.94f, 0.06f);
                    c.Segment(0.06f, 0.5f, 0.3f, 0.5f, 0.06f);
                    c.Segment(0.7f, 0.5f, 0.94f, 0.5f, 0.06f);
                    c.Circle(0.5f, 0.5f, 0.06f);
                    break;
                case CardGlyph.Nova:
                    for (int i = 0; i < 8; i++)
                    {
                        float a = 45f * i * Mathf.Deg2Rad;
                        float outerRadius = i % 2 == 0 ? 0.42f : 0.32f;
                        c.Segment(0.5f + Mathf.Cos(a) * 0.14f, 0.5f + Mathf.Sin(a) * 0.14f, 0.5f + Mathf.Cos(a) * outerRadius, 0.5f + Mathf.Sin(a) * outerRadius, 0.055f);
                    }

                    c.Circle(0.5f, 0.5f, 0.1f);
                    break;
                case CardGlyph.ArrowUp:
                    c.Polygon(new[] { new Vector2(0.5f, 0.9f), new Vector2(0.22f, 0.58f), new Vector2(0.78f, 0.58f) });
                    c.Segment(0.5f, 0.6f, 0.5f, 0.18f, 0.16f);
                    c.Polygon(new[] { new Vector2(0.8f, 0.42f), new Vector2(0.68f, 0.26f), new Vector2(0.76f, 0.26f), new Vector2(0.7f, 0.1f), new Vector2(0.86f, 0.3f), new Vector2(0.78f, 0.3f) });
                    break;
                case CardGlyph.Grenade:
                    c.Circle(0.5f, 0.4f, 0.25f);
                    c.Segment(0.42f, 0.7f, 0.58f, 0.7f, 0.11f);
                    c.Ring(0.7f, 0.78f, 0.07f, 0.035f);
                    c.Paint = Darken(element, 0.7f);
                    c.Segment(0.5f, 0.22f, 0.5f, 0.58f, 0.04f);
                    c.Segment(0.32f, 0.4f, 0.68f, 0.4f, 0.04f);
                    c.Paint = Color.white;
                    break;
                case CardGlyph.Spike:
                    c.Ring(0.5f, 0.42f, 0.3f, 0.04f);
                    c.Ring(0.5f, 0.42f, 0.17f, 0.035f);
                    c.Polygon(new[] { new Vector2(0.5f, 0.12f), new Vector2(0.38f, 0.9f), new Vector2(0.62f, 0.9f) });
                    break;
            }
        }

        static Color Darken(Color color, float factor) => new Color(color.r * factor, color.g * factor, color.b * factor, 1f);

        static Vector2[] Scale(Vector2[] points, float factor)
        {
            var center = new Vector2(0.5f, 0.5f);
            var scaled = new Vector2[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                scaled[i] = center + (points[i] - center) * factor;
            }

            return scaled;
        }

        /// <summary>Tiny SDF rasterizer. Coordinates are 0..1 with y up.</summary>
        sealed class IconCanvas
        {
            readonly int size;
            readonly Color[] pixels;
            Vector2 offset;
            float alphaScale = 1f;

            public IconCanvas(int size)
            {
                this.size = size;
                pixels = new Color[size * size];
            }

            public Color Paint { get; set; } = Color.white;

            public void Background(Color element)
            {
                Color top = Color.Lerp(element, Color.white, 0.18f);
                Color bottom = element * 0.5f;
                bottom.a = 1f;
                ForEachPixel((p, i) =>
                {
                    float d = RoundedBox(p, new Vector2(0.5f, 0.5f), new Vector2(0.46f, 0.46f), 0.12f);
                    float coverage = Coverage(d);
                    if (coverage <= 0f)
                    {
                        return;
                    }

                    Color fill = Color.Lerp(bottom, top, p.y);
                    float border = Coverage(Mathf.Abs(d + 0.015f) - 0.012f);
                    fill = Color.Lerp(fill, Color.Lerp(element, Color.white, 0.55f), border);
                    fill.a = coverage;
                    pixels[i] = fill;
                });
            }

            /// <summary>Draws the glyph twice: a soft dark copy offset down-right, then the real one.</summary>
            public void Shadowed(System.Action<IconCanvas> draw)
            {
                Color paint = Paint;
                Paint = new Color(0f, 0f, 0f, 1f);
                offset = new Vector2(0.018f, -0.018f);
                alphaScale = 0.35f;
                draw(this);
                offset = Vector2.zero;
                alphaScale = 1f;
                Paint = paint;
                draw(this);
            }

            public void Circle(float x, float y, float radius) =>
                Shape(p => (p - new Vector2(x, y)).magnitude - radius);

            public void Ring(float x, float y, float radius, float thickness) =>
                Shape(p => Mathf.Abs((p - new Vector2(x, y)).magnitude - radius) - thickness * 0.5f);

            public void Segment(float x0, float y0, float x1, float y1, float thickness) =>
                Shape(p => DistanceToSegment(p, new Vector2(x0, y0), new Vector2(x1, y1)) - thickness * 0.5f);

            public void Arc(float x, float y, float radius, float thickness, float startDegrees, float endDegrees)
            {
                var center = new Vector2(x, y);
                Shape(p =>
                {
                    Vector2 v = p - center;
                    float angle = Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg;
                    float sweep = endDegrees - startDegrees;
                    float relative = Mathf.Repeat(angle - startDegrees, 360f);
                    bool inside = relative <= sweep || sweep >= 360f;
                    if (!inside && sweep > 360f)
                    {
                        inside = relative + 360f <= sweep;
                    }

                    if (inside)
                    {
                        return Mathf.Abs(v.magnitude - radius) - thickness * 0.5f;
                    }

                    Vector2 a = center + Polar(startDegrees) * radius;
                    Vector2 b = center + Polar(endDegrees) * radius;
                    return Mathf.Min((p - a).magnitude, (p - b).magnitude) - thickness * 0.5f;
                });
            }

            public void Polygon(Vector2[] points) =>
                Shape(p =>
                {
                    float distance = float.MaxValue;
                    bool inside = false;
                    for (int i = 0, j = points.Length - 1; i < points.Length; j = i++)
                    {
                        distance = Mathf.Min(distance, DistanceToSegment(p, points[j], points[i]));
                        if ((points[i].y > p.y) != (points[j].y > p.y) &&
                            p.x < (points[j].x - points[i].x) * (p.y - points[i].y) / (points[j].y - points[i].y) + points[i].x)
                        {
                            inside = !inside;
                        }
                    }

                    return inside ? -distance : distance;
                });

            void Shape(System.Func<Vector2, float> signedDistance)
            {
                Color paint = Paint;
                ForEachPixel((p, i) =>
                {
                    float coverage = Coverage(signedDistance(p - offset)) * alphaScale * paint.a;
                    if (coverage <= 0f || pixels[i].a <= 0f)
                    {
                        return; // never paint outside the tile
                    }

                    Color current = pixels[i];
                    Color blended = Color.Lerp(current, paint, coverage);
                    blended.a = current.a;
                    pixels[i] = blended;
                });
            }

            void ForEachPixel(System.Action<Vector2, int> action)
            {
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        action(new Vector2((x + 0.5f) / size, (y + 0.5f) / size), y * size + x);
                    }
                }
            }

            float Coverage(float signedDistance) => Mathf.Clamp01(0.5f - signedDistance * size);

            public byte[] ToPng()
            {
                var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
                texture.SetPixels(pixels);
                texture.Apply();
                byte[] png = texture.EncodeToPNG();
                Object.DestroyImmediate(texture);
                return png;
            }

            static float RoundedBox(Vector2 p, Vector2 center, Vector2 half, float radius)
            {
                Vector2 q = new Vector2(Mathf.Abs(p.x - center.x), Mathf.Abs(p.y - center.y)) - half + new Vector2(radius, radius);
                return new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - radius;
            }

            static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
            {
                Vector2 ab = b - a;
                float t = ab.sqrMagnitude > 0f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
                return (p - (a + ab * t)).magnitude;
            }

            static Vector2 Polar(float degrees) => new Vector2(Mathf.Cos(degrees * Mathf.Deg2Rad), Mathf.Sin(degrees * Mathf.Deg2Rad));
        }
    }
}
