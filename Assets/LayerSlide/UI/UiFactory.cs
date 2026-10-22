using System;
using LayerSlide.Presentation;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace LayerSlide.UI
{
    public enum TextStyle
    {
        /// <summary>Body font, plain.</summary>
        Body,
        /// <summary>Display font, plain.</summary>
        Display,
        /// <summary>Display font with a thick outline and drop shadow, for titles on the sky background.</summary>
        Title,
        /// <summary>Display font with a soft drop shadow, for button labels.</summary>
        Button
    }

    public enum ButtonLayout
    {
        LabelOnly,
        IconOnly,
        IconLeft,
        IconTop
    }

    /// <summary>Small helpers for building uGUI hierarchies from code. All sizes are in dp (1 canvas unit = 1 dp).</summary>
    public sealed class UiFactory
    {
        public const float ButtonHeight = 60f;
        public const float LipHeight = 6f;
        public const float FontHero = 52f;
        public const float FontTitle = 30f;
        public const float FontHeader = 26f;
        public const float FontButton = 22f;
        public const float FontBody = 18f;
        public const float FontSmall = 15f;

        public static readonly ButtonColors Green = new ButtonColors(Palette.Green, Palette.GreenLip, Color.white);
        public static readonly ButtonColors Orange = new ButtonColors(Palette.Orange, Palette.OrangeLip, Color.white);
        public static readonly ButtonColors Sky = new ButtonColors(Palette.Sky, Palette.SkyLip, Color.white);
        public static readonly ButtonColors Pink = new ButtonColors(Palette.Pink, Palette.PinkLip, Color.white);
        public static readonly ButtonColors White = new ButtonColors(Palette.Panel, Palette.PanelLip, Palette.Text);
        public static readonly ButtonColors Locked = new ButtonColors(Palette.Locked, Palette.LockedLip, Color.white);

        public readonly TMP_FontAsset Body;
        public readonly TMP_FontAsset Display;
        readonly Material _titleMaterial;
        readonly Material _buttonMaterial;

        public UiFactory(VisualTheme theme)
        {
            Body = theme.bodyFont;
            Display = theme.displayFont;
            _titleMaterial = theme.titleTextMaterial;
            _buttonMaterial = theme.buttonTextMaterial;
        }

        // ---------------------------------------------------------------- Rects

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

        // ---------------------------------------------------------------- Graphics

        /// <param name="radius">Corner radius in dp; 0 gives a plain rectangle.</param>
        public Image Panel(string name, Transform parent, Color color, float radius, bool raycast = false)
        {
            var image = CreateRect(name, parent).gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = raycast;
            if (radius > 0f) MakeRounded(image, radius);
            return image;
        }

        public static void MakeRounded(Image image, float radius)
        {
            image.sprite = SpriteFactory.RoundedRect;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = SpriteFactory.RadiusMultiplier(radius);
        }

        /// <summary>Soft drop shadow behind <paramref name="parent"/>'s rect, offset downwards.</summary>
        public static Image DropShadow(Transform parent, float spread, float offsetY, float alpha = 1f)
        {
            var image = CreateRect("Shadow", parent).gameObject.AddComponent<Image>();
            image.sprite = SpriteFactory.SoftShadow;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 1.4f;
            var color = Palette.Shadow;
            color.a *= alpha;
            image.color = color;
            image.raycastTarget = false;
            Stretch(image.rectTransform, -spread, -spread, -spread + offsetY, -spread - offsetY);
            image.transform.SetAsFirstSibling();
            IgnoreLayout(image);
            return image;
        }

        public static Image Icon(Transform parent, Sprite sprite, Color color, float size)
        {
            var image = CreateRect("Icon", parent).gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            image.preserveAspect = true;
            Fixed(image.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size, size));
            return image;
        }

        /// <summary>
        /// White rounded card with a lavender lip and soft shadow. Children go into the returned face rect; give it
        /// a layout group and the card (the face's parent) grows to fit it.
        /// </summary>
        public RectTransform Card(string name, Transform parent, float radius = 26f, Color? face = null)
        {
            var root = CreateRect(name, parent);
            VerticalLayout(root.gameObject, 0f, new RectOffset(0, 0, 0, (int)LipHeight + 2));
            DropShadow(root, 10f, 8f);
            var lip = Panel("Lip", root, Palette.PanelLip, radius, raycast: true);
            Stretch(lip.rectTransform);
            IgnoreLayout(lip);
            var body = Panel("Face", root, face ?? Palette.Panel, radius, raycast: true);
            return body.rectTransform;
        }

        public static void IgnoreLayout(Component component)
        {
            if (!component.TryGetComponent(out LayoutElement element))
                element = component.gameObject.AddComponent<LayoutElement>();
            element.ignoreLayout = true;
        }

        // ---------------------------------------------------------------- Text

        public TextMeshProUGUI Text(string name, Transform parent, string text, float size, TextStyle style, Color color,
            TextAlignmentOptions alignment = TextAlignmentOptions.Center)
        {
            var label = CreateRect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
            label.font = style == TextStyle.Body ? Body : Display;
            if (style == TextStyle.Title && _titleMaterial != null) label.fontSharedMaterial = _titleMaterial;
            else if (style == TextStyle.Button && _buttonMaterial != null) label.fontSharedMaterial = _buttonMaterial;
            label.fontSize = size;
            label.enableAutoSizing = false;
            label.color = color;
            label.alignment = alignment;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.overflowMode = TextOverflowModes.Overflow;
            label.lineSpacing = -8f;
            label.raycastTarget = false;
            label.text = text;
            return label;
        }

        // ---------------------------------------------------------------- Buttons

        /// <summary>
        /// Chunky button: shadow, lip, face (with gloss) and its content. <paramref name="icon"/> may be null for
        /// <see cref="ButtonLayout.LabelOnly"/>.
        /// </summary>
        public ChunkyButton Button(string name, Transform parent, string label, ButtonColors colors, UnityAction onClick,
            Sprite icon = null, ButtonLayout layout = ButtonLayout.LabelOnly, float radius = 20f, float fontSize = FontButton, float iconSize = 28f)
        {
            var root = CreateRect(name, parent);
            DropShadow(root, 6f, 5f, 0.8f);

            var lip = Panel("Lip", root, colors.Lip, radius);
            Stretch(lip.rectTransform);

            // The face is the raycast target and the Button's graphic.
            var face = Panel("Face", root, colors.Face, radius, raycast: true);
            Stretch(face.rectTransform);

            var gloss = Panel("Gloss", face.transform, new Color(1f, 1f, 1f, 0.22f), radius * 0.8f);
            var glossRect = gloss.rectTransform;
            glossRect.anchorMin = new Vector2(0f, 0.52f);
            glossRect.anchorMax = new Vector2(1f, 1f);
            glossRect.offsetMin = new Vector2(6f, 0f);
            glossRect.offsetMax = new Vector2(-6f, -4f);

            var content = Stretch(CreateRect("Content", face.transform), 8f, 8f, 2f, 2f);

            var button = root.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = face;
            if (onClick != null) button.onClick.AddListener(onClick);

            var chunky = root.gameObject.AddComponent<ChunkyButton>();
            chunky.Setup(button, face, lip, gloss, content, LipHeight);

            if (layout != ButtonLayout.LabelOnly && icon != null)
            {
                var image = Icon(content, icon, colors.Content, iconSize);
                chunky.SetIcon(image);
                if (layout == ButtonLayout.IconTop)
                {
                    Fixed(image.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -4f), new Vector2(iconSize, iconSize));
                }
                else if (layout == ButtonLayout.IconLeft)
                {
                    var element = image.gameObject.AddComponent<LayoutElement>();
                    element.preferredWidth = element.preferredHeight = iconSize;
                }
            }

            if (layout != ButtonLayout.IconOnly)
            {
                var text = Text("Label", content, label, fontSize, TextStyle.Button, colors.Content);
                text.textWrappingMode = TextWrappingModes.NoWrap;
                chunky.SetLabel(text);
                if (layout == ButtonLayout.IconTop)
                    BottomStrip(text.rectTransform, 0f, fontSize + 6f);
                else if (layout != ButtonLayout.IconLeft)
                    Stretch(text.rectTransform);
            }

            if (layout == ButtonLayout.IconLeft)
            {
                var row = content.gameObject.AddComponent<HorizontalLayoutGroup>();
                row.childAlignment = TextAnchor.MiddleCenter;
                row.spacing = 10f;
                row.childControlWidth = row.childControlHeight = true;
                row.childForceExpandWidth = row.childForceExpandHeight = false;
            }

            return chunky;
        }

        /// <summary>Square chunky button with a centered icon (round when <paramref name="radius"/> is half the size).</summary>
        public ChunkyButton IconButton(string name, Transform parent, Sprite icon, ButtonColors colors, UnityAction onClick, float size = 52f,
            float iconSize = 28f, float radius = 18f)
        {
            var button = Button(name, parent, null, colors, onClick, icon, ButtonLayout.IconOnly, radius, iconSize: iconSize);
            ((RectTransform)button.transform).sizeDelta = new Vector2(size, size);
            button.Icon.rectTransform.anchoredPosition = new Vector2(0f, 1f);
            return button;
        }

        // ---------------------------------------------------------------- Layout

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
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.scrollSensitivity = 20f;
            return content;
        }

        public static void ClearChildren(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(parent.GetChild(i).gameObject);
        }

        public static void Bind(UnityEngine.UI.Button button, Action action)
        {
            button.onClick.RemoveAllListeners();
            if (action != null) button.onClick.AddListener(() => action());
        }
    }
}
