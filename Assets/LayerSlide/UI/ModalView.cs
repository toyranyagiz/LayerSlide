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

    /// <summary>Single modal slot: dimmed backdrop that blocks input underneath, title, message and stacked buttons.</summary>
    public sealed class ModalView
    {
        readonly UiFactory _ui;
        readonly RectTransform _root;
        readonly RectTransform _window;
        readonly TextMeshProUGUI _title;
        readonly TextMeshProUGUI _message;
        readonly RectTransform _buttons;
        readonly CanvasGroup _group;
        float _fadeTime, _fadeDuration;

        public ModalId Current { get; private set; } = ModalId.None;
        public bool IsOpen => Current != ModalId.None;

        public ModalView(UiFactory ui, RectTransform parent)
        {
            _ui = ui;
            var dim = ui.Panel("Modal", parent, new Color(0.04f, 0.07f, 0.11f, 0.6f), rounded: false, raycast: true);
            _root = UiFactory.Stretch(dim.rectTransform);
            _group = dim.gameObject.AddComponent<CanvasGroup>();

            var window = ui.Panel("Window", _root, Palette.Panel, rounded: true, raycast: true);
            _window = window.rectTransform;
            _window.anchorMin = _window.anchorMax = _window.pivot = new Vector2(0.5f, 0.5f);
            UiFactory.VerticalLayout(window.gameObject, 8f, new RectOffset(16, 16, 20, 16));
            var fitter = window.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _title = ui.Text("Title", _window, string.Empty, UiFactory.FontTitle, true, Palette.Text);
            _message = ui.Text("Message", _window, string.Empty, UiFactory.FontBody, false, Palette.Text);

            _buttons = UiFactory.CreateRect("Buttons", _window);
            UiFactory.VerticalLayout(_buttons.gameObject, 8f, new RectOffset(0, 0, 8, 0));

            _root.gameObject.SetActive(false);
        }

        public void SetWidth(float safeWidth) =>
            _window.sizeDelta = new Vector2(Mathf.Min(400f, safeWidth - 32f), _window.sizeDelta.y);

        /// <param name="fadeSeconds">Panel fade-in; buttons stay non-interactive until it ends.</param>
        public void Show(ModalId id, string title, string message, IReadOnlyList<(string label, Action action)> buttons, float fadeSeconds = 0f)
        {
            Current = id;
            _title.text = title;
            _title.gameObject.SetActive(!string.IsNullOrEmpty(title));
            _message.text = message;
            _message.gameObject.SetActive(!string.IsNullOrEmpty(message));

            UiFactory.ClearChildren(_buttons);
            for (int i = 0; i < buttons.Count; i++)
            {
                var (label, action) = buttons[i];
                bool primary = i == 0;
                var button = _ui.Button("Button", _buttons, label, primary ? Palette.Environment : new Color(0.91f, 0.94f, 0.96f, 1f),
                    primary ? Color.white : Palette.Text, null, out _);
                UiFactory.Bind(button, action);
                UiFactory.Height(button, UiFactory.ButtonHeight);
            }

            _fadeDuration = fadeSeconds;
            _fadeTime = 0f;
            _group.alpha = fadeSeconds > 0f ? 0f : 1f;
            _group.interactable = fadeSeconds <= 0f;
            _root.gameObject.SetActive(true);
            _root.SetAsLastSibling();
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
        float _hideAt = -1f;

        public ToastView(UiFactory ui, RectTransform parent)
        {
            var panel = ui.Panel("Toast", parent, new Color(0.09f, 0.14f, 0.2f, 0.95f), rounded: true);
            _root = UiFactory.TopStrip(panel.rectTransform, 72f, 48f, 16f);
            _text = ui.Text("Text", _root, string.Empty, UiFactory.FontSmall, true, Color.white);
            UiFactory.Stretch(_text.rectTransform, 12f, 12f);
            _root.gameObject.SetActive(false);
        }

        public void Show(string message, float seconds = 3f)
        {
            _text.text = message;
            _hideAt = Time.unscaledTime + seconds;
            _root.gameObject.SetActive(true);
            _root.SetAsLastSibling();
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
