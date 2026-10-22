using LayerSlide.Core;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;

namespace LayerSlide.Presentation
{
    /// <summary>
    /// Shared materials and fonts. Every renderer uses these shared instances; nothing calls renderer.material,
    /// so no per-object material copies are created at runtime (GDD 04 §4).
    /// </summary>
    [CreateAssetMenu(menuName = "Layer Slide/Visual Theme", fileName = "VisualTheme")]
    public sealed class VisualTheme : ScriptableObject
    {
        [Header("Toy shader")]
        public Material red;
        public Material blue;
        public Material yellow;
        public Material board;
        [Tooltip("Cell tiles; boardCellAlt is the second checkerboard tone.")]
        public Material boardCell;
        public Material boardCellAlt;
        [Tooltip("Recessed floor under the cells; the gaps between tiles show it as grooves.")]
        public Material boardWell;
        public Material wall;

        public Material perimeter;

        [Header("Unlit")]
        public Material white;
        public Material dark;
        [Tooltip("Half-transparent white, used for the hint target cell.")]
        public Material whiteTransparent;
        [Tooltip("Vertical sky gradient drawn behind the board.")]
        public Material background;
        [Tooltip("Soft transparent blob shadow under blocks.")]
        public Material blobShadow;
        [Tooltip("Translucent white highlight band on top of each layer (candy gloss).")]
        public Material gloss;

        [Header("Meshes")]
        public MeshBank meshBank;

        [Header("Fonts")]
        [Tooltip("Body copy and small labels (Baloo 2 SemiBold).")]
        [FormerlySerializedAs("regularFont")]
        public TMP_FontAsset bodyFont;
        [Tooltip("Titles, buttons and numbers (Baloo 2 ExtraBold).")]
        [FormerlySerializedAs("semiBoldFont")]
        public TMP_FontAsset displayFont;
        [Tooltip("Display font preset with a thick dark outline and drop shadow (screen titles).")]
        public Material titleTextMaterial;
        [Tooltip("Display font preset with a soft drop shadow (button labels).")]
        public Material buttonTextMaterial;

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
