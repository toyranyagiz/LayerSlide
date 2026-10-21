using System;
using UnityEngine;

namespace LayerSlide.UI
{
    /// <summary>
    /// UI sprites rasterized once at startup from signed distance functions, so icons do not depend on font
    /// glyph coverage (GDD 04 §6). Shapes are white; tint them with Image.color.
    /// </summary>
    public static class SpriteFactory
    {
        const int IconSize = 96;

        static Sprite _roundedRect, _roundedOutline, _lock, _check, _menu, _speaker, _speakerMuted;

        public static Sprite RoundedRect => _roundedRect ??= Create(64, p => RoundedBox(p, new Vector2(32, 32), new Vector2(32, 32), 14f), 20f);
        public static Sprite RoundedOutline => _roundedOutline ??= Create(64,
            p => Mathf.Abs(RoundedBox(p, new Vector2(32, 32), new Vector2(30, 30), 13f)) - 2f, 20f);

        public static Sprite Lock => _lock ??= Create(IconSize, p =>
        {
            float body = RoundedBox(p, new Vector2(48, 34), new Vector2(26, 20), 5f);
            float shackle = Mathf.Abs(Vector2.Distance(p, new Vector2(48, 56)) - 15f) - 5f;
            shackle = Mathf.Max(shackle, 54f - p.y); // keep only the upper arc
            float legs = Mathf.Min(Segment(p, new Vector2(33, 56), new Vector2(33, 50)), Segment(p, new Vector2(63, 56), new Vector2(63, 50))) - 5f;
            return Mathf.Min(body, Mathf.Min(shackle, legs));
        });

        public static Sprite Check => _check ??= Create(IconSize, p =>
            Mathf.Min(Segment(p, new Vector2(22, 50), new Vector2(40, 30)), Segment(p, new Vector2(40, 30), new Vector2(74, 68))) - 7f);

        public static Sprite Menu => _menu ??= Create(IconSize, p =>
        {
            float d = float.MaxValue;
            for (int i = 0; i < 3; i++)
                d = Mathf.Min(d, Segment(p, new Vector2(24, 30 + i * 18), new Vector2(72, 30 + i * 18)));
            return d - 4.5f;
        });

        public static Sprite Speaker => _speaker ??= Create(IconSize, p =>
        {
            float waves = Mathf.Min(Arc(p, new Vector2(46, 48), 16f), Arc(p, new Vector2(46, 48), 28f)) - 4f;
            return Mathf.Min(SpeakerBody(p), waves);
        });

        public static Sprite SpeakerMuted => _speakerMuted ??= Create(IconSize, p =>
        {
            float cross = Mathf.Min(Segment(p, new Vector2(60, 36), new Vector2(80, 60)), Segment(p, new Vector2(60, 60), new Vector2(80, 36))) - 4f;
            return Mathf.Min(SpeakerBody(p), cross);
        });

        static float SpeakerBody(Vector2 p)
        {
            float box = RoundedBox(p, new Vector2(26, 48), new Vector2(8, 11), 2f);
            // Cone: triangle-ish shape built from a box clipped by two half planes.
            float cone = Mathf.Max(RoundedBox(p, new Vector2(38, 48), new Vector2(12, 24), 2f),
                Mathf.Abs(p.y - 48f) - (p.x - 26f) * 1.1f - 6f);
            return Mathf.Min(box, cone);
        }

        static float Arc(Vector2 p, Vector2 center, float radius)
        {
            var d = p - center;
            float ring = Mathf.Abs(d.magnitude - radius);
            // Only the right-hand 90 degree sector.
            return d.x > Mathf.Abs(d.y) * 0.9f ? ring : float.MaxValue;
        }

        static float RoundedBox(Vector2 p, Vector2 center, Vector2 halfSize, float radius)
        {
            var q = new Vector2(Mathf.Abs(p.x - center.x), Mathf.Abs(p.y - center.y)) - halfSize + Vector2.one * radius;
            return Vector2.Max(q, Vector2.zero).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - radius;
        }

        static float Segment(Vector2 p, Vector2 a, Vector2 b)
        {
            var pa = p - a;
            var ba = b - a;
            float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Vector2.Dot(ba, ba));
            return (pa - ba * h).magnitude;
        }

        /// <param name="sdf">Signed distance in pixels; negative inside.</param>
        static Sprite Create(int size, Func<Vector2, float> sdf, float border = 0f)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float coverage = Mathf.Clamp01(0.5f - sdf(new Vector2(x + 0.5f, y + 0.5f)));
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(coverage * 255f));
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(border, border, border, border));
        }
    }
}
