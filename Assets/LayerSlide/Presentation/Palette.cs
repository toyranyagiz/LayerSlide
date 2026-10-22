using LayerSlide.Core;
using UnityEngine;

namespace LayerSlide.Presentation
{
    /// <summary>
    /// Color identities shared by the 3D materials and the UI. Layer colors keep their GDD 04 §4 identities
    /// (red / blue / yellow) in a brighter "candy" tuning; everything else belongs to the toy-style theme.
    /// </summary>
    public static class Palette
    {
        // Layer identities.
        public static readonly Color Red = Hex(0xFF5468);
        public static readonly Color Blue = Hex(0x2F9BFF);
        public static readonly Color Yellow = Hex(0xFFC22E);

        // Board.
        public static readonly Color Board = Hex(0xF4F1FF);
        public static readonly Color BoardCell = Hex(0xE4DCFF);
        public static readonly Color BoardCellAlt = Hex(0xF2EEFF);
        public static readonly Color BoardWell = Hex(0xA99BE8);

        public static readonly Color Tray = Hex(0xFFFFFF);
        public static readonly Color Wall = Hex(0x9C8FD9);
        public static readonly Color Ambient = Hex(0xD9D2FF);

        // Backgrounds: vertical gradient, top to bottom.
        public static readonly Color SkyTop = Hex(0x8B7CF6);
        public static readonly Color SkyBottom = Hex(0x4A3BC9);
        public static readonly Color Environment = Hex(0x5B4BD6);

        // Text.
        public static readonly Color Text = Hex(0x2E2470);
        public static readonly Color TextMuted = Hex(0x7A72A8);
        public static readonly Color TextOutline = Hex(0x2A1F6B);

        // Panels.
        public static readonly Color Panel = Hex(0xFFFFFF);
        public static readonly Color PanelLip = Hex(0xD6CEF5);
        public static readonly Color PanelInset = Hex(0xF1EDFF);
        public static readonly Color Selection = Color.white;
        public static readonly Color Shadow = new Color(0.10f, 0.05f, 0.30f, 0.35f);

        // Chunky button faces and the darker "lip" under them.
        public static readonly Color Green = Hex(0x55D46A);
        public static readonly Color GreenLip = Hex(0x2C9E45);
        public static readonly Color Orange = Hex(0xFFAA2B);
        public static readonly Color OrangeLip = Hex(0xD9730F);
        public static readonly Color Sky = Hex(0x45B4FF);
        public static readonly Color SkyLip = Hex(0x1F78D1);
        public static readonly Color Pink = Hex(0xFF6B9A);
        public static readonly Color PinkLip = Hex(0xD13F6E);
        public static readonly Color Locked = Hex(0xBDB6DB);
        public static readonly Color LockedLip = Hex(0x938BB8);
        public static readonly Color Disabled = Hex(0x9AA6B4);

        public static Color Of(LayerColor color)
        {
            switch (color)
            {
                case LayerColor.Red: return Red;
                case LayerColor.Blue: return Blue;
                default: return Yellow;
            }
        }

        /// <summary>Turkish display name used in the HUD.</summary>
        public static string NameOf(LayerColor color)
        {
            switch (color)
            {
                case LayerColor.Red: return "Kırmızı";
                case LayerColor.Blue: return "Mavi";
                default: return "Sarı";
            }
        }

        /// <summary>Darker shade of <paramref name="color"/> used for lips, sides and outlines.</summary>
        public static Color Darken(Color color, float amount = 0.25f)
        {
            Color.RGBToHSV(color, out float h, out float s, out float v);
            var result = Color.HSVToRGB(h, Mathf.Clamp01(s + amount * 0.3f), Mathf.Clamp01(v - amount));
            result.a = color.a;
            return result;
        }

        static Color Hex(int rgb) =>
            new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
    }
}
