using UnityEngine;

namespace LayerSlide.UI
{
    /// <summary>
    /// Tiny unscaled-time UI animations (pop-in, idle bob, breathing pulse, slow spin). Each instance drives one
    /// RectTransform and never allocates per frame. <see cref="Reduced"/> mirrors the reduced-motion setting:
    /// pop-ins finish instantly and idle loops hold still.
    /// </summary>
    public sealed class UiAnim : MonoBehaviour
    {
        public enum Kind
        {
            Pop,
            Bob,
            Pulse,
            Spin
        }

        public static bool Reduced { get; set; }

        Kind _kind;
        RectTransform _rect;
        Vector2 _basePosition;
        float _amount, _period, _phase, _delay, _time;
        bool _done;

        /// <summary>Scales from 0.6 to 1 with a soft overshoot over ~0.32 s, after <paramref name="delay"/> seconds.</summary>
        public static UiAnim Pop(RectTransform rect, float delay = 0f) => Attach(rect, Kind.Pop, 0f, 0.32f, 0f, delay);

        /// <summary>Floats up and down by <paramref name="amount"/> dp.</summary>
        public static UiAnim Bob(RectTransform rect, float amount, float period, float phase = 0f) => Attach(rect, Kind.Bob, amount, period, phase, 0f);

        /// <summary>Breathes between 1 and 1 + <paramref name="amount"/> scale.</summary>
        public static UiAnim Pulse(RectTransform rect, float amount, float period) => Attach(rect, Kind.Pulse, amount, period, 0f, 0f);

        /// <summary>Rotates by <paramref name="degreesPerSecond"/>.</summary>
        public static UiAnim Spin(RectTransform rect, float degreesPerSecond) => Attach(rect, Kind.Spin, degreesPerSecond, 1f, 0f, 0f);

        static UiAnim Attach(RectTransform rect, Kind kind, float amount, float period, float phase, float delay)
        {
            var anim = rect.gameObject.AddComponent<UiAnim>();
            anim._kind = kind;
            anim._rect = rect;
            anim._amount = amount;
            anim._period = Mathf.Max(0.01f, period);
            anim._phase = phase;
            anim._delay = delay;
            anim._basePosition = rect.anchoredPosition;
            anim.Restart();
            return anim;
        }

        /// <summary>Replays a pop (used each time a modal opens).</summary>
        public void Restart()
        {
            _time = -_delay;
            _done = false;
            if (_kind == Kind.Pop)
                _rect.localScale = Reduced ? Vector3.one : Vector3.one * 0.6f;
        }

        void OnEnable()
        {
            if (_rect != null && _kind == Kind.Bob) _basePosition = _rect.anchoredPosition;
        }

        void Update()
        {
            if (_done) return;
            _time += Time.unscaledDeltaTime;

            switch (_kind)
            {
                case Kind.Pop:
                    if (Reduced || _time >= _period)
                    {
                        _rect.localScale = Vector3.one;
                        _done = true;
                        return;
                    }
                    _rect.localScale = Vector3.one * Mathf.LerpUnclamped(0.6f, 1f, BackOut(Mathf.Clamp01(_time / _period)));
                    break;
                case Kind.Bob:
                    if (Reduced) return;
                    float bob = Mathf.Sin((_time / _period + _phase) * Mathf.PI * 2f) * _amount;
                    _rect.anchoredPosition = _basePosition + new Vector2(0f, bob);
                    break;
                case Kind.Pulse:
                    float pulse = Reduced ? 0f : (Mathf.Sin(_time / _period * Mathf.PI * 2f) * 0.5f + 0.5f) * _amount;
                    _rect.localScale = Vector3.one * (1f + pulse);
                    break;
                case Kind.Spin:
                    if (Reduced) return;
                    _rect.localRotation = Quaternion.Euler(0f, 0f, -_time * _amount);
                    break;
            }
        }

        public static float BackOut(float t)
        {
            const float s = 1.70158f;
            t -= 1f;
            return t * t * ((s + 1f) * t + s) + 1f;
        }
    }
}
