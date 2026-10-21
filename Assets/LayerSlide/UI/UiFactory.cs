using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace LayerSlide.UI
{
    /// <summary>Small helpers for building uGUI hierarchies from code. All sizes are in dp (1 canvas unit = 1 dp).</summary>
    public sealed class UiFactory
    {
        public const float ButtonHeight = 56f;
        public const float FontTitle = 20f;
        public const float FontBody = 16f;
        public const float FontButton = 17f;
        public const float FontSmall = 14f;

        public readonly TMP_FontAsset Regular;
        public readonly TMP_FontAsset SemiBold;

        public UiFactory(TMP_FontAsset regular, TMP_FontAsset semiBold)
        {
            Regular = regular;
            SemiBold = semiBold;
        }

        public static RectTransform CreateRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent != null ? parent.gameObject.layer : 5;
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        public static RectTransform Stretch(RectTransform rect, float left = 0f, float right = 0f, float top = 0f, float bottom = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
            return rect;
        }

        /// <summary>Full-width strip anchored to the top of its parent.</summary>
        public static RectTransform TopStrip(RectTransform rect, float top, float height, float sidePadding = 0f)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(sidePadding, -top - height);
            rect.offsetMax = new Vector2(-sidePadding, -top);
            return rect;
        }

        /// <summary>Full-width strip anchored to the bottom of its parent.</summary>
        public static RectTransform BottomStrip(RectTransform rect, float bottom, float height, float sidePadding = 0f)
        {
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.offsetMin = new Vector2(sidePadding, bottom);
            rect.offsetMax = new Vector2(-sidePadding, bottom + height);
            return rect;
        }

        public static void Fixed(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        public Image Panel(string name, Transform parent, Color color, bool rounded, bool raycast = false)
        {
            var image = CreateRect(name, parent).gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = raycast;
            if (rounded)
            {
                image.sprite = SpriteFactory.RoundedRect;
                image.type = Image.Type.Sliced;
            }
            return image;
        }

        public TextMeshProUGUI Text(string name, Transform parent, string text, float size, bool semiBold, Color color,
            TextAlignmentOptions alignment = TextAlignmentOptions.Center)
        {
            var label = CreateRect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
            label.font = semiBold ? SemiBold : Regular;
            label.fontSize = size;
            label.enableAutoSizing = false;
            label.color = color;
            label.alignment = alignment;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.overflowMode = TextOverflowModes.Overflow;
            label.lineSpacing = 0f;
            label.raycastTarget = false;
            label.text = text;
            return label;
        }

        public Button Button(string name, Transform parent, string label, Color background, Color textColor, UnityAction onClick,
            out TextMeshProUGUI text)
        {
            var image = Panel(name, parent, background, rounded: true, raycast: true);
            var button = image.gameObject.AddComponent<Button>();
            var colors = button.colors;
            colors.highlightedColor = Color.white;
            colors.pressedColor = new Color(0.85f, 0.88f, 0.92f, 1f);
            colors.disabledColor = new Color(1f, 1f, 1f, 0.45f);
            colors.fadeDuration = 0.05f;
            button.colors = colors;
            button.targetGraphic = image;
            if (onClick != null) button.onClick.AddListener(onClick);

            text = Text("Label", image.transform, label, FontButton, true, textColor);
            Stretch(text.rectTransform, 8f, 8f, 0f, 0f);
            return button;
        }

        public Button IconButton(string name, Transform parent, Sprite icon, Color iconColor, UnityAction onClick, out Image iconImage)
        {
            // Transparent 48x48 hit target with a 28 dp icon.
            var hit = Panel(name, parent, new Color(1f, 1f, 1f, 0f), rounded: false, raycast: true);
            var button = hit.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            if (onClick != null) button.onClick.AddListener(onClick);

            iconImage = CreateRect("Icon", hit.transform).gameObject.AddComponent<Image>();
            iconImage.sprite = icon;
            iconImage.color = iconColor;
            iconImage.raycastTarget = false;
            Fixed(iconImage.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(28f, 28f));
            return button;
        }

        public static VerticalLayoutGroup VerticalLayout(GameObject go, float spacing, RectOffset padding, TextAnchor alignment = TextAnchor.UpperCenter)
        {
            var layout = go.AddComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.padding = padding;
            layout.childAlignment = alignment;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            return layout;
        }

        public static LayoutElement Height(Component component, float height)
        {
            if (!component.TryGetComponent(out LayoutElement element))
                element = component.gameObject.AddComponent<LayoutElement>();
            element.minHeight = height;
            element.preferredHeight = height;
            return element;
        }

        /// <summary>Vertical ScrollRect filling <paramref name="parent"/>; returns the content rect (auto-sized).</summary>
        public static RectTransform VerticalScroll(Transform parent, out ScrollRect scroll)
        {
            var viewport = CreateRect("Viewport", parent);
            viewport.gameObject.AddComponent<RectMask2D>();
            var viewportImage = viewport.gameObject.AddComponent<Image>();
            viewportImage.color = new Color(0f, 0f, 0f, 0f);

            var content = CreateRect("Content", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = content.offsetMax = Vector2.zero;
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.content = content;
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 20f;
            return content;
        }

        public static void SetInteractable(Button button, bool interactable, TextMeshProUGUI label = null)
        {
            button.interactable = interactable;
            if (label != null)
            {
                var color = label.color;
                color.a = interactable ? 1f : 0.45f;
                label.color = color;
            }
        }

        public static void ClearChildren(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(parent.GetChild(i).gameObject);
        }

        public static void Bind(Button button, Action action)
        {
            button.onClick.RemoveAllListeners();
            if (action != null) button.onClick.AddListener(() => action());
        }
    }
}
