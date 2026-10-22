using System;
using System.Collections.Generic;
using LayerSlide.Presentation;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LayerSlide.UI
{
    public enum ModalId
    {
        None,
        Pause,
        RestartConfirm,
        LeaveRunConfirm,
        ExitConfirm,
        Won,
        Error,
        NewerSave
    }

    /// <summary>
    /// Single modal slot: dimmed backdrop that blocks input underneath and a white card that pops in, with an
    /// optional ribbon title, a message and stacked chunky buttons (the first one is the primary action).
    /// The Won modal adds a spinning ray burst and a star above the card.
    /// </summary>
    public sealed class ModalView
    {
        const float RibbonHeight = 56f;

        readonly UiFactory _ui;
        readonly RectTransform _root;
        readonly RectTransform _window;
        readonly RectTransform _ribbon;
        readonly TextMeshProUGUI _title;
        readonly TextMeshProUGUI _message;
        readonly RectTransform _buttons;
        readonly RectTransform _celebration;
        readonly VerticalLayoutGroup _cardLayout;
        readonly CanvasGroup _group;
        readonly UiAnim _pop;
        float _fadeTime, _fadeDuration;

        public ModalId Current { get; private set; } = ModalId.None;
        public bool IsOpen => Current != ModalId.None;

        public ModalView(UiFactory ui, RectTransform parent)
        {
            _ui = ui;
            var dim = ui.Panel("Modal", parent, new Color(0.12f, 0.07f, 0.35f, 0.62f), 0f, raycast: true);
            _root = UiFactory.Stretch(dim.rectTransform);
            _group = dim.gameObject.AddComponent<CanvasGroup>();

            _window = UiFactory.CreateRect("Window", _root);
            _window.anchorMin = _window.anchorMax = _window.pivot = new Vector2(0.5f, 0.5f);
            var windowLayout = UiFactory.VerticalLayout(_window.gameObject, 0f, new RectOffset(0, 0, 0, 0));
            windowLayout.childControlHeight = true;
            var fitter = _window.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Won-only decoration, centered on the card's top edge.
            _celebration = UiFactory.CreateRect("Celebration", _window);
            UiFactory.IgnoreLayout(_celebration);
            UiFactory.Fixed(_celebration, new Vector2(0.5f, 1f), new Vector2(0f, 0f), new Vector2(240f, 240f));
            _celebration.pivot = new Vector2(0.5f, 0.5f);
            _celebration.anchoredPosition = new Vector2(0f, 104f);
            var rays = UiFactory.Icon(_celebration, SpriteFactory.Sunburst, new Color(1f, 0.92f, 0.55f, 0.75f), 300f);
            UiAnim.Spin(rays.rectTransform, 14f);
            var starShadow = UiFactory.Icon(_celebration, SpriteFactory.Star, Palette.OrangeLip, 116f);
            starShadow.rectTransform.anchoredPosition = new Vector2(0f, -6f);
            var star = UiFactory.Icon(_celebration, SpriteFactory.Star, Palette.Yellow, 112f);
            UiAnim.Pulse(star.rectTransform, 0.06f, 1.2f);
            var shine = UiFactory.Icon(star.transform, SpriteFactory.Sparkle, Color.white, 30f);
            shine.rectTransform.anchoredPosition = new Vector2(-18f, 16f);
            _celebration.gameObject.SetActive(false);

            var card = ui.Card("Card", _window);
            _cardLayout = UiFactory.VerticalLayout(card.gameObject, 10f, new RectOffset(20, 20, 22, 20));

            // Ribbon title sits across the card's top edge.
            var ribbonRoot = UiFactory.CreateRect("Ribbon", card);
            UiFactory.IgnoreLayout(ribbonRoot);
            UiFactory.Fixed(ribbonRoot, new Vector2(0.5f, 1f), new Vector2(0f, RibbonHeight * 0.5f), new Vector2(260f, RibbonHeight));
            _ribbon = ribbonRoot;
            UiFactory.DropShadow(ribbonRoot, 6f, 5f, 0.8f);
            var ribbonLip = ui.Panel("Lip", ribbonRoot, Palette.PinkLip, 22f);
            UiFactory.Stretch(ribbonLip.rectTransform);
            var ribbonFace = ui.Panel("Face", ribbonRoot, Palette.Pink, 22f);
            UiFactory.Stretch(ribbonFace.rectTransform, 0f, 0f, 0f, UiFactory.LipHeight);
            var ribbonGloss = ui.Panel("Gloss", ribbonFace.transform, new Color(1f, 1f, 1f, 0.22f), 16f);
            UiFactory.Stretch(ribbonGloss.rectTransform, 8f, 8f, 4f, 24f);
            _title = ui.Text("Title", ribbonFace.transform, string.Empty, UiFactory.FontHeader, TextStyle.Title, Color.white);
            UiFactory.Stretch(_title.rectTransform, 10f, 10f, 0f, 0f);
            _title.textWrappingMode = TextWrappingModes.NoWrap;
            _title.enableAutoSizing = true;
            _title.fontSizeMin = 16f;
            _title.fontSizeMax = UiFactory.FontHeader;

            _message = ui.Text("Message", card, string.Empty, UiFactory.FontBody + 1f, TextStyle.Display, Palette.Text);
            _message.lineSpacing = -4f;

            _buttons = UiFactory.CreateRect("Buttons", card);
            UiFactory.VerticalLayout(_buttons.gameObject, 10f, new RectOffset(0, 0, 6, 0));

            _pop = UiAnim.Pop(_window);
            _root.gameObject.SetActive(false);
        }

        public void SetWidth(float safeWidth) =>
            _window.sizeDelta = new Vector2(Mathf.Min(360f, safeWidth - 40f), _window.sizeDelta.y);

        /// <param name="fadeSeconds">Panel fade-in; buttons stay non-interactive until it ends.</param>
        public void Show(ModalId id, string title, string message, IReadOnlyList<(string label, Action action)> buttons, float fadeSeconds = 0f)
        {
            Current = id;
            bool hasTitle = !string.IsNullOrEmpty(title);
            _title.text = title;
            _ribbon.gameObject.SetActive(hasTitle);
            _cardLayout.padding = new RectOffset(20, 20, hasTitle ? (int)(RibbonHeight * 0.5f) + 18 : 24, 20);
            _message.text = message;
            _message.gameObject.SetActive(!string.IsNullOrEmpty(message));
            _celebration.gameObject.SetActive(id == ModalId.Won);

            UiFactory.ClearChildren(_buttons);
            for (int i = 0; i < buttons.Count; i++)
            {
                var (label, action) = buttons[i];
                var colors = i == 0 ? UiFactory.Green : UiFactory.White;
                var button = _ui.Button("Button", _buttons, label, colors, null);
                UiFactory.Bind(button.Button, action);
                UiFactory.Height(button, i == 0 ? UiFactory.ButtonHeight + 4f : UiFactory.ButtonHeight - 4f);
            }

            _fadeDuration = fadeSeconds;
            _fadeTime = 0f;
            _group.alpha = fadeSeconds > 0f ? 0f : 1f;
            _group.interactable = fadeSeconds <= 0f;
            _root.gameObject.SetActive(true);
            _root.SetAsLastSibling();
            _pop.Restart();
        }

        public void Hide()
        {
            Current = ModalId.None;
            _root.gameObject.SetActive(false);
        }

        public void Tick(float deltaTime)
        {
            if (!IsOpen || _fadeTime >= _fadeDuration) return;
            _fadeTime += deltaTime;
            _group.alpha = Mathf.Clamp01(_fadeTime / _fadeDuration);
            if (_fadeTime >= _fadeDuration) _group.interactable = true;
        }
    }

    /// <summary>Short non-blocking notice shown over the message row (save errors, recovery notices).</summary>
    public sealed class ToastView
    {
        readonly RectTransform _root;
        readonly TextMeshProUGUI _text;
        readonly UiAnim _pop;
        float _hideAt = -1f;

        public ToastView(UiFactory ui, RectTransform parent)
        {
            _root = UiFactory.TopStrip(UiFactory.CreateRect("Toast", parent), 96f, 48f, 24f);
            UiFactory.DropShadow(_root, 6f, 4f, 0.8f);
            var panel = ui.Panel("Panel", _root, new Color(0.17f, 0.11f, 0.42f, 0.96f), 24f);
            UiFactory.Stretch(panel.rectTransform);
            _text = ui.Text("Text", panel.transform, string.Empty, UiFactory.FontSmall, TextStyle.Display, Color.white);
            UiFactory.Stretch(_text.rectTransform, 16f, 16f);
            _text.lineSpacing = 0f;
            _pop = UiAnim.Pop(_root);
            _root.gameObject.SetActive(false);
        }

        public void Show(string message, float seconds = 3f)
        {
            _text.text = message;
            _hideAt = Time.unscaledTime + seconds;
            _root.gameObject.SetActive(true);
            _root.SetAsLastSibling();
            _pop.Restart();
        }

        public void Tick()
        {
            if (_hideAt > 0f && Time.unscaledTime >= _hideAt)
            {
                _hideAt = -1f;
                _root.gameObject.SetActive(false);
            }
        }
    }
}
