using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LayerSlide.UI
{
    /// <summary>Face, lip and label colors of a <see cref="ChunkyButton"/>.</summary>
    public readonly struct ButtonColors
    {
        public readonly Color Face, Lip, Content;

        public ButtonColors(Color face, Color lip, Color content)
        {
            Face = face;
            Lip = lip;
            Content = content;
        }
    }

    /// <summary>
    /// Toy-style "physical" button: a colored face resting on a darker lip with a soft drop shadow and a gloss
    /// band. Pressing sinks the face onto the lip; releasing springs it back with a small bounce. Visual only:
    /// clicks still go through the uGUI <see cref="UnityEngine.UI.Button"/> on the same object.
    /// </summary>
    public sealed class ChunkyButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        const float PressSpeed = 18f;
        const float BounceDuration = 0.22f;

        public Button Button { get; private set; }
        public Image Face { get; private set; }
        public Image Lip { get; private set; }
        public Image Gloss { get; private set; }
        public Image Icon { get; private set; }
        public TextMeshProUGUI Label { get; private set; }
        public RectTransform Content { get; private set; }

        CanvasGroup _group;
        RectTransform _face;
        float _lipHeight;
        float _press, _pressTarget;
        float _bounceTime = -1f;

        internal void Setup(Button button, Image face, Image lip, Image gloss, RectTransform content, float lipHeight)
        {
            Button = button;
            Face = face;
            Lip = lip;
            Gloss = gloss;
            Content = content;
            _face = face.rectTransform;
            _lipHeight = lipHeight;
            _group = gameObject.AddComponent<CanvasGroup>();
            ApplyPress();
        }

        internal void SetLabel(TextMeshProUGUI label) => Label = label;
        internal void SetIcon(Image icon) => Icon = icon;

        public void SetColors(ButtonColors colors)
        {
            Face.color = colors.Face;
            Lip.color = colors.Lip;
            if (Label != null) Label.color = colors.Content;
            if (Icon != null) Icon.color = colors.Content;
        }

        public void SetInteractable(bool interactable)
        {
            Button.interactable = interactable;
            _group.alpha = interactable ? 1f : 0.5f;
            if (!interactable) _pressTarget = 0f;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (Button.interactable && eventData.button == PointerEventData.InputButton.Left) _pressTarget = 1f;
        }

        public void OnPointerUp(PointerEventData eventData) => Release();

        public void OnPointerExit(PointerEventData eventData) => Release();

        void Release()
        {
            if (_pressTarget <= 0f) return;
            _pressTarget = 0f;
            if (!UiAnim.Reduced) _bounceTime = 0f;
        }

        void OnDisable()
        {
            _press = _pressTarget = 0f;
            _bounceTime = -1f;
            transform.localScale = Vector3.one;
            if (_face != null) ApplyPress();
        }

        void Update()
        {
            if (!Mathf.Approximately(_press, _pressTarget))
            {
                _press = UiAnim.Reduced ? _pressTarget : Mathf.MoveTowards(_press, _pressTarget, Time.unscaledDeltaTime * PressSpeed);
                ApplyPress();
            }

            if (_bounceTime >= 0f)
            {
                _bounceTime += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(_bounceTime / BounceDuration);
                transform.localScale = Vector3.one * (1f + Mathf.Sin(t * Mathf.PI) * 0.05f * (1f - t));
                if (t >= 1f)
                {
                    _bounceTime = -1f;
                    transform.localScale = Vector3.one;
                }
            }
        }

        /// <summary>The face keeps its height and slides down over the lip as it is pressed.</summary>
        void ApplyPress()
        {
            _face.offsetMin = new Vector2(0f, _lipHeight * (1f - _press));
            _face.offsetMax = new Vector2(0f, -_lipHeight * _press);
        }
    }
}
