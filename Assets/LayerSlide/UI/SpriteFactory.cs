using System;
using UnityEngine;

namespace LayerSlide.UI
{
    /// <summary>
    /// UI sprites rasterized once at startup from signed distance functions, so icons do not depend on font
    /// glyph coverage (GDD 04 §6). Shapes are white; tint them with Image.color. Icons are drawn on a 128 px
    /// canvas with thick, rounded strokes so they stay legible at 24–40 dp.
    /// </summary>
    public static class SpriteFactory
    {
        const int IconSize = 128;

        /// <summary>Corner radius of <see cref="RoundedRect"/> in sprite pixels; see <see cref="RadiusMultiplier"/>.</summary>
        public const float RoundedRadiusPx = 32f;

        // Caches use Unity's null check, not ??=: with domain reload disabled the statics outlive a play session
        // whose sprites were destroyed, and ??= would hand those dead objects back.
        static Sprite _roundedRect, _roundedOutline, _softShadow, _circle, _gloss;
        static Sprite _lock, _key, _ring, _check, _menu, _pause, _speaker, _speakerMuted, _undo, _restart, _bulb, _gear, _home, _play,
            _levels, _back, _close, _star, _sparkle;

        // ---------------------------------------------------------------- Panels

        /// <summary>9-sliced rounded rectangle; set Image.pixelsPerUnitMultiplier via <see cref="RadiusMultiplier"/>.</summary>
        public static Sprite RoundedRect => _roundedRect != null ? _roundedRect : _roundedRect = Create(128, p => RoundedBox(p, new Vector2(64, 64), new Vector2(64, 64), RoundedRadiusPx), 40f);

        public static Sprite RoundedOutline => _roundedOutline != null ? _roundedOutline : _roundedOutline = Create(128,
            p => Mathf.Abs(RoundedBox(p, new Vector2(64, 64), new Vector2(60, 60), RoundedRadiusPx - 4f)) - 4f, 40f);

        /// <summary>Blurred rounded rectangle for drop shadows (alpha falls off over 20 px).</summary>
        public static Sprite SoftShadow => _softShadow != null ? _softShadow : _softShadow = CreateSoft(128,
            p => RoundedBox(p, new Vector2(64, 64), new Vector2(40, 40), 16f), 22f, 60f);

        public static Sprite Circle => _circle != null ? _circle : _circle = Create(IconSize, p => Vector2.Distance(p, new Vector2(64, 64)) - 62f);

        /// <summary>Top highlight band of a glossy button: a flat ellipse-ended strip, white, tint for strength.</summary>
        public static Sprite Gloss => _gloss != null ? _gloss : _gloss = Create(128, p => RoundedBox(p, new Vector2(64, 64), new Vector2(64, 64), 64f), 60f);

        /// <summary>Image.pixelsPerUnitMultiplier that gives <see cref="RoundedRect"/> a corner radius of <paramref name="radiusDp"/>.</summary>
        public static float RadiusMultiplier(float radiusDp) => RoundedRadiusPx / Mathf.Max(1f, radiusDp);

        /// <summary>Vertical gradient (top to bottom), stretched over a full-screen Image.</summary>
        public static Sprite VerticalGradient(Color top, Color bottom)
        {
            const int height = 256;
            var texture = new Texture2D(2, height, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            var pixels = new Color32[2 * height];
            for (int y = 0; y < height; y++)
            {
                // Slight ease so the middle stays bright, like a lit sky.
                float t = y / (height - 1f);
                Color32 color = Color.Lerp(bottom, top, t * t * (3f - 2f * t));
                pixels[y * 2] = pixels[y * 2 + 1] = color;
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0, 0, 2, height), new Vector2(0.5f, 0.5f), 100f);
        }

        // ---------------------------------------------------------------- Icons

        public static Sprite Lock => _lock != null ? _lock : _lock = Create(IconSize, p =>
        {
            float body = RoundedBox(p, new Vector2(64, 46), new Vector2(34, 28), 9f);
            float shackle = Mathf.Abs(Vector2.Distance(p, new Vector2(64, 76)) - 20f) - 7f;
            shackle = Mathf.Max(shackle, 76f - p.y); // upper arc only
            float legs = Mathf.Min(Segment(p, new Vector2(44, 76), new Vector2(44, 66)), Segment(p, new Vector2(84, 76), new Vector2(84, 66))) - 7f;
            float keyhole = Mathf.Min(Vector2.Distance(p, new Vector2(64, 50)) - 7f, RoundedBox(p, new Vector2(64, 38), new Vector2(3, 9), 3f));
            return Mathf.Max(Mathf.Min(body, Mathf.Min(shackle, legs)), -keyhole);
        });

        public static Sprite Key => _key != null ? _key : _key = Create(IconSize, p =>
        {
            float bow = Mathf.Abs(Vector2.Distance(p, new Vector2(40, 64)) - 18f) - 8f;
            float shaft = Segment(p, new Vector2(58, 64), new Vector2(108, 64)) - 8f;
            float teeth = Mathf.Min(Segment(p, new Vector2(96, 64), new Vector2(96, 44)), Segment(p, new Vector2(82, 64), new Vector2(82, 48))) - 7f;
            return Mathf.Min(bow, Mathf.Min(shaft, teeth));
        });

        public static Sprite Ring => _ring != null ? _ring : _ring = Create(IconSize, p => Mathf.Abs(Vector2.Distance(p, new Vector2(64, 64)) - 48f) - 10f);

        public static Sprite Check => _check != null ? _check : _check = Create(IconSize, p =>
            Mathf.Min(Segment(p, new Vector2(30, 66), new Vector2(54, 42)), Segment(p, new Vector2(54, 42), new Vector2(100, 90))) - 11f);

        public static Sprite Menu => _menu != null ? _menu : _menu = Create(IconSize, p =>
        {
            float d = float.MaxValue;
            for (int i = 0; i < 3; i++)
                d = Mathf.Min(d, Segment(p, new Vector2(32, 40 + i * 24), new Vector2(96, 40 + i * 24)));
            return d - 7f;
        });

        public static Sprite Pause => _pause != null ? _pause : _pause = Create(IconSize, p =>
            Mathf.Min(RoundedBox(p, new Vector2(46, 64), new Vector2(11, 34), 8f), RoundedBox(p, new Vector2(82, 64), new Vector2(11, 34), 8f)));

        public static Sprite Speaker => _speaker != null ? _speaker : _speaker = Create(IconSize, p =>
        {
            float waves = Mathf.Min(Arc(p, new Vector2(56, 64), 22f, -50f, 50f), Arc(p, new Vector2(56, 64), 40f, -50f, 50f)) - 6f;
            return Mathf.Min(SpeakerBody(p), waves);
        });

        public static Sprite SpeakerMuted => _speakerMuted != null ? _speakerMuted : _speakerMuted = Create(IconSize, p =>
        {
            float cross = Mathf.Min(Segment(p, new Vector2(80, 48), new Vector2(106, 80)), Segment(p, new Vector2(80, 80), new Vector2(106, 48))) - 6.5f;
            return Mathf.Min(SpeakerBody(p), cross);
        });

        /// <summary>Counter-clockwise arrow bending back to the left.</summary>
        public static Sprite Undo => _undo != null ? _undo : _undo = Create(IconSize, p =>
        {
            float arc = Arc(p, new Vector2(70, 58), 30f, -100f, 90f) - 8f;
            float tail = Segment(p, new Vector2(70, 88), new Vector2(46, 88)) - 8f;
            float head = Triangle(p, new Vector2(22, 88), new Vector2(50, 112), new Vector2(50, 64)) - 3f;
            return Mathf.Min(Mathf.Min(arc, tail), head);
        });

        /// <summary>Circular arrow (restart).</summary>
        public static Sprite Restart => _restart != null ? _restart : _restart = Create(IconSize, p =>
        {
            float arc = Arc(p, new Vector2(64, 64), 34f, -200f, 60f) - 8.5f;
            var tip = new Vector2(64, 64) + Polar(60f, 34f);
            float head = Triangle(p, tip + new Vector2(22, 4), tip + new Vector2(-14, 20), tip + new Vector2(-6, -18)) - 3f;
            return Mathf.Min(arc, head);
        });

        /// <summary>Light bulb (hint).</summary>
        public static Sprite Bulb => _bulb != null ? _bulb : _bulb = Create(IconSize, p =>
        {
            float glass = Vector2.Distance(p, new Vector2(64, 76)) - 32f;
            float neck = RoundedBox(p, new Vector2(64, 46), new Vector2(18, 14), 6f);
            float bulb = SmoothMin(glass, neck, 8f);
            float cap = Mathf.Min(RoundedBox(p, new Vector2(64, 26), new Vector2(16, 5), 5f), RoundedBox(p, new Vector2(64, 14), new Vector2(10, 5), 5f));
            float shine = Arc(p, new Vector2(64, 76), 20f, 110f, 170f) - 4.5f;
            return Mathf.Min(Mathf.Max(bulb, -shine), cap);
        });

        public static Sprite Gear => _gear != null ? _gear : _gear = Create(IconSize, p =>
        {
            var c = new Vector2(64, 64);
            float d = Vector2.Distance(p, c) - 34f;
            for (int i = 0; i < 8; i++)
            {
                var q = Rotate(p - c, i * 45f);
                d = Mathf.Min(d, RoundedBox(q, new Vector2(0, 42), new Vector2(10, 12), 4f));
            }
            return Mathf.Max(d, -(Vector2.Distance(p, c) - 14f));
        });

        public static Sprite Home => _home != null ? _home : _home = Create(IconSize, p =>
        {
            float roof = Triangle(p, new Vector2(64, 112), new Vector2(14, 66), new Vector2(114, 66)) - 4f;
            float body = RoundedBox(p, new Vector2(64, 44), new Vector2(34, 30), 6f);
            float door = RoundedBox(p, new Vector2(64, 30), new Vector2(10, 16), 5f);
            return Mathf.Max(Mathf.Min(roof, body), -door);
        });

        public static Sprite Play => _play != null ? _play : _play = Create(IconSize, p =>
            Triangle(p, new Vector2(44, 22), new Vector2(44, 106), new Vector2(108, 64)) - 7f);

        /// <summary>2x2 grid of rounded squares (level select).</summary>
        public static Sprite Levels => _levels != null ? _levels : _levels = Create(IconSize, p =>
        {
            float d = float.MaxValue;
            for (int y = 0; y < 2; y++)
            for (int x = 0; x < 2; x++)
                d = Mathf.Min(d, RoundedBox(p, new Vector2(40 + x * 48, 40 + y * 48), new Vector2(19, 19), 7f));
            return d;
        });

        public static Sprite Back => _back != null ? _back : _back = Create(IconSize, p =>
            Mathf.Min(Segment(p, new Vector2(78, 102), new Vector2(40, 64)), Segment(p, new Vector2(40, 64), new Vector2(78, 26))) - 11f);

        public static Sprite Close => _close != null ? _close : _close = Create(IconSize, p =>
            Mathf.Min(Segment(p, new Vector2(36, 36), new Vector2(92, 92)), Segment(p, new Vector2(36, 92), new Vector2(92, 36))) - 10f);

        public static Sprite Star => _star != null ? _star : _star = Create(IconSize, p => Star5(p - new Vector2(64, 60), 58f, 0.48f) - 4f);

        static Sprite _sunburst;

        /// <summary>Twelve soft light rays fading out from the center (win celebration).</summary>
        public static Sprite Sunburst => _sunburst != null ? _sunburst : _sunburst = Rasterize(256, 0f, p =>
        {
            var d = p - new Vector2(128, 128);
            float r = d.magnitude / 128f;
            if (r >= 1f) return 0f;
            float angle = Mathf.Atan2(d.y, d.x);
            // Wedges: |sin(6θ)| peaks give 12 rays; sharpen and soften the edge.
            float wedge = Mathf.Clamp01((Mathf.Abs(Mathf.Cos(angle * 6f)) - 0.55f) / 0.25f);
            float fade = Mathf.Pow(1f - r, 1.4f);
            float glow = Mathf.Clamp01(1f - r * 2.2f) * 0.6f;
            return Mathf.Clamp01(wedge * fade + glow);
        });

        /// <summary>Four-point sparkle used as a decoration.</summary>
        public static Sprite Sparkle => _sparkle != null ? _sparkle : _sparkle = Create(IconSize, p =>
        {
            var q = p - new Vector2(64, 64);
            float a = Mathf.Abs(q.x), b = Mathf.Abs(q.y);
            // Concave diamond: |x|^0.5 + |y|^0.5 = R^0.5, approximated as a distance.
            float r = Mathf.Sqrt(a / 60f) + Mathf.Sqrt(b / 60f);
            return (r - 1f) * 30f;
        });

        // ---------------------------------------------------------------- SDF primitives

        static float SpeakerBody(Vector2 p)
        {
            float box = RoundedBox(p, new Vector2(28, 64), new Vector2(12, 16), 4f);
            float cone = Triangle(p, new Vector2(30, 64), new Vector2(66, 104), new Vector2(66, 24)) - 4f;
            return SmoothMin(box, cone, 4f);
        }

        static Vector2 Polar(float degrees, float radius) =>
            new Vector2(Mathf.Cos(degrees * Mathf.Deg2Rad), Mathf.Sin(degrees * Mathf.Deg2Rad)) * radius;

        static Vector2 Rotate(Vector2 v, float degrees)
        {
            float r = degrees * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
            return new Vector2(c * v.x - s * v.y, s * v.x + c * v.y);
        }

        /// <summary>Distance to a circular arc from <paramref name="fromDeg"/> to <paramref name="toDeg"/> (counter-clockwise).</summary>
        static float Arc(Vector2 p, Vector2 center, float radius, float fromDeg, float toDeg)
        {
            var d = p - center;
            float angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            float span = toDeg - fromDeg;
            float rel = Mathf.Repeat(angle - fromDeg, 360f);
            if (rel <= span) return Mathf.Abs(d.magnitude - radius);
            var a = center + Polar(fromDeg, radius);
            var b = center + Polar(toDeg, radius);
            return Mathf.Min(Vector2.Distance(p, a), Vector2.Distance(p, b));
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

        /// <summary>Exact signed distance to a triangle (negative inside).</summary>
        static float Triangle(Vector2 p, Vector2 p0, Vector2 p1, Vector2 p2)
        {
            Vector2 e0 = p1 - p0, e1 = p2 - p1, e2 = p0 - p2;
            Vector2 v0 = p - p0, v1 = p - p1, v2 = p - p2;
            Vector2 pq0 = v0 - e0 * Mathf.Clamp01(Vector2.Dot(v0, e0) / Vector2.Dot(e0, e0));
            Vector2 pq1 = v1 - e1 * Mathf.Clamp01(Vector2.Dot(v1, e1) / Vector2.Dot(e1, e1));
            Vector2 pq2 = v2 - e2 * Mathf.Clamp01(Vector2.Dot(v2, e2) / Vector2.Dot(e2, e2));
            float s = Mathf.Sign(e0.x * e2.y - e0.y * e2.x);
            var d = Vector2.Min(Vector2.Min(
                    new Vector2(Vector2.Dot(pq0, pq0), s * (v0.x * e0.y - v0.y * e0.x)),
                    new Vector2(Vector2.Dot(pq1, pq1), s * (v1.x * e1.y - v1.y * e1.x))),
                new Vector2(Vector2.Dot(pq2, pq2), s * (v2.x * e2.y - v2.y * e2.x)));
            return -Mathf.Sqrt(d.x) * Mathf.Sign(d.y);
        }

        /// <summary>Five-point star with outer radius <paramref name="r"/>; <paramref name="inner"/> is the inner/outer ratio.</summary>
        static float Star5(Vector2 p, float r, float inner)
        {
            var k1 = new Vector2(0.809016994f, -0.587785252f);
            var k2 = new Vector2(-k1.x, k1.y);
            p.x = Mathf.Abs(p.x);
            p -= 2f * Mathf.Max(Vector2.Dot(k1, p), 0f) * k1;
            p -= 2f * Mathf.Max(Vector2.Dot(k2, p), 0f) * k2;
            p.x = Mathf.Abs(p.x);
            p.y -= r;
            var ba = inner * new Vector2(-k1.y, k1.x) - new Vector2(0f, 1f);
            float h = Mathf.Clamp(Vector2.Dot(p, ba) / Vector2.Dot(ba, ba), 0f, r);
            return (p - ba * h).magnitude * Mathf.Sign(p.y * ba.x - p.x * ba.y);
        }

        static float SmoothMin(float a, float b, float k)
        {
            float h = Mathf.Clamp01(0.5f + 0.5f * (b - a) / k);
            return Mathf.Lerp(b, a, h) - k * h * (1f - h);
        }

        // ---------------------------------------------------------------- Rasterizing

        /// <param name="sdf">Signed distance in pixels; negative inside.</param>
        static Sprite Create(int size, Func<Vector2, float> sdf, float border = 0f) =>
            Rasterize(size, border, p => Mathf.Clamp01(0.5f - sdf(p)));

        /// <summary>Soft-edged shape: alpha falls from 1 at the outline to 0 at <paramref name="blur"/> px outside.</summary>
        static Sprite CreateSoft(int size, Func<Vector2, float> sdf, float blur, float border) =>
            Rasterize(size, border, p =>
            {
                float t = Mathf.Clamp01(1f - sdf(p) / blur);
                return t * t * (3f - 2f * t);
            });

        static Sprite Rasterize(int size, float border, Func<Vector2, float> coverage)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(coverage(new Vector2(x + 0.5f, y + 0.5f)) * 255f));
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(border, border, border, border));
        }
    }
}
