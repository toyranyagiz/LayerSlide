using LayerSlide.Core;
using UnityEngine;

namespace LayerSlide.Presentation
{
    /// <summary>Colored threshold outside the board edge with its symbol, armed scale and highlight frame.</summary>
    public sealed class GateView : MonoBehaviour
    {
        public const float Depth = 0.28f;
        public const float SurfaceHeight = 0.03f;
        const float ArmedScale = 1.08f;
        const float ArmDuration = 0.08f;
        const float FlashDuration = 0.12f;

        MeshRenderer _threshold;
        Material _colorMaterial;
        Material _flashMaterial;
        GameObject _symbol;
        GameObject _highlight;
        float _scaleFrom = 1f, _scaleTo = 1f, _scaleTime, _scaleDuration;
        float _flashUntil = -1f;
        bool _highlightRequested;

        public GateSpec Spec { get; private set; }

        public void Initialize(GateSpec spec, VisualTheme theme)
        {
            Spec = spec;
            name = "Gate_" + spec.Id;
            _colorMaterial = theme.GetLayerMaterial(spec.Color);
            _flashMaterial = theme.wall;

            bool vertical = spec.Side.GetAxis() == Axis.X; // Left/Right gates run along Z
            float sizeX = vertical ? Depth : spec.Length;
            float sizeZ = vertical ? spec.Length : Depth;

            _threshold = BlockView.CreateRenderer("Threshold", transform, MeshLibrary.Box(sizeX, SurfaceHeight, sizeZ), _colorMaterial, castShadows: false)
                .GetComponent<MeshRenderer>();

            _symbol = new GameObject("Symbol");
            _symbol.transform.SetParent(transform, false);
            _symbol.transform.localPosition = new Vector3(0f, SurfaceHeight + 0.004f, 0f);
            BlockView.CreateRenderer("Outline", _symbol.transform, MeshLibrary.Symbol(spec.Color, 0.22f), theme.dark, castShadows: false);
            var fill = BlockView.CreateRenderer("Fill", _symbol.transform, MeshLibrary.Symbol(spec.Color, 0.185f), theme.white, castShadows: false);
            fill.transform.localPosition = new Vector3(0f, 0.002f, 0f);

            _highlight = BlockView.CreateRenderer("Highlight", transform, MeshLibrary.Frame(sizeX + 0.06f, sizeZ + 0.06f, 0.04f), theme.white, castShadows: false);
            _highlight.transform.localPosition = new Vector3(0f, SurfaceHeight + 0.01f, 0f);
            _highlight.SetActive(false);
        }

        public void SetSymbolVisible(bool visible) => _symbol.SetActive(visible);

        public void SetArmed(bool armed, bool reducedMotion)
        {
            float target = armed && !reducedMotion ? ArmedScale : 1f;
            _highlight.SetActive(armed || _highlightRequested);
            if (Mathf.Approximately(target, _scaleTo)) return;
            _scaleFrom = transform.localScale.x;
            _scaleTo = target;
            _scaleTime = 0f;
            _scaleDuration = ArmDuration;
        }

        /// <summary>Hint highlight of the target gate.</summary>
        public void SetHighlighted(bool highlighted)
        {
            _highlightRequested = highlighted;
            _highlight.SetActive(highlighted);
        }

        /// <summary>Invalid exit feedback: the threshold flashes the neutral wall color for 120 ms.</summary>
        public void Flash()
        {
            _flashUntil = Time.unscaledTime + FlashDuration;
            _threshold.sharedMaterial = _flashMaterial;
        }

        public void ResetVisuals()
        {
            _highlightRequested = false;
            _highlight.SetActive(false);
            _scaleTo = _scaleFrom = 1f;
            _scaleDuration = 0f;
            transform.localScale = Vector3.one;
            _flashUntil = -1f;
            _threshold.sharedMaterial = _colorMaterial;
        }

        void Update()
        {
            if (_scaleTime < _scaleDuration)
            {
                _scaleTime += Time.deltaTime;
                transform.localScale = Vector3.one * Mathf.Lerp(_scaleFrom, _scaleTo, Ease.OutCubic(_scaleTime / _scaleDuration));
            }

            if (_flashUntil > 0f && Time.unscaledTime >= _flashUntil)
            {
                _flashUntil = -1f;
                _threshold.sharedMaterial = _colorMaterial;
            }
        }
    }
}
