using TMPro;
using UnityEngine;

namespace LayerSlide.Presentation
{
    /// <summary>
    /// Flat 3D text lying on the board (K1/K2/P1/P2 tags, GDD 04 §13). Uses the shipped TMP atlas; it casts no
    /// shadow and is never a pick target.
    /// </summary>
    public static class WorldLabel
    {
        /// <param name="height">Cap height in world units.</param>
        public static TextMeshPro Create(Transform parent, string text, float height, VisualTheme theme, Color? color = null)
        {
            var go = new GameObject("Label_" + text);
            go.transform.SetParent(parent, false);
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // lie flat, readable from the tilted camera

            var label = go.AddComponent<TextMeshPro>();
            if (theme.displayFont != null) label.font = theme.displayFont;
            label.text = text;
            // TMP world text: fontSize 10 is roughly one unit of line height; caps take ~70% of it.
            label.fontSize = height * 10f / 0.7f;
            label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.color = color ?? Palette.TextOutline;
            label.rectTransform.sizeDelta = new Vector2(1f, height * 2f);
            label.raycastTarget = false;

            var renderer = label.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return label;
        }
    }
}
