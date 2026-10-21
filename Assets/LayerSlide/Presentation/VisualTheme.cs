using LayerSlide.Core;
using TMPro;
using UnityEngine;

namespace LayerSlide.Presentation
{
    /// <summary>
    /// Shared materials and fonts. Every renderer uses these shared instances; nothing calls renderer.material,
    /// so no per-object material copies are created at runtime (GDD 04 §4).
    /// </summary>
    [CreateAssetMenu(menuName = "Layer Slide/Visual Theme", fileName = "VisualTheme")]
    public sealed class VisualTheme : ScriptableObject
    {
        [Header("Lit")]
        public Material red;
        public Material blue;
        public Material yellow;
        public Material board;
        public Material wall;
        public Material perimeter;

        [Header("Unlit")]
        public Material white;
        public Material dark;

        [Header("Fonts")]
        public TMP_FontAsset regularFont;
        public TMP_FontAsset semiBoldFont;

        public Material GetLayerMaterial(LayerColor color)
        {
            switch (color)
            {
                case LayerColor.Red: return red;
                case LayerColor.Blue: return blue;
                default: return yellow;
            }
        }
    }
}
