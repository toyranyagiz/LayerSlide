using LayerSlide.Core;
using UnityEngine;

namespace LayerSlide.Presentation
{
    /// <summary>Fixed color identities from GDD 04 §4. Used by both 3D materials and UI.</summary>
    public static class Palette
    {
        public static readonly Color Red = Hex(0xEF5350);
        public static readonly Color Blue = Hex(0x42A5F5);
        public static readonly Color Yellow = Hex(0xFBC02D);
        public static readonly Color Board = Hex(0xE9EFF5);
        public static readonly Color Environment = Hex(0x172333);
        public static readonly Color Wall = Hex(0x67788A);
        public static readonly Color Text = Hex(0x172333);
        public static readonly Color Panel = Color.white;
        public static readonly Color Selection = Color.white;
        public static readonly Color Ambient = Hex(0xCBD5E1);
        public static readonly Color Locked = Hex(0xB8C2CE);
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

        static Color Hex(int rgb) =>
            new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
    }
}
