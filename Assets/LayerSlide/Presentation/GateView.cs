using LayerSlide.Core;
using UnityEngine;

namespace LayerSlide.Presentation
{
    /// <summary>Colored threshold outside the board edge with its symbol, armed scale and highlight frame.</summary>
    public sealed class GateView : MonoBehaviour
    {
        public const float Depth = 0.28f;
        public const float SurfaceHeight = 0.05f;
        const float PadInset = 0.04f;
        const float PadRadius = 0.10f;
        const float ArmedScale = 1.08f;
        const float ArmDuration = 0.08f;
        const float FlashDuration = 0.12f;
        const float SymbolSize = 0.22f;
        const float SymbolFillSize = 0.185f;
        const float BadgeGap = 0.10f;
        const float GlyphSize = 0.18f;
        const float LabelHeight = 0.13f;

        MeshRenderer _threshold;
        Material _colorMaterial;
        Material _flashMaterial;
        GameObject _symbol;
        MeshFilter _symbolOutline, _symbolFill;
        MeshFilter _lockGlyph;
        MeshRenderer _nextSwatch;
        VisualTheme _theme;
        int _phase = -1;
        GameObject _highlight;
        float _scaleFrom = 1f, _scaleTo = 1f, _scaleTime, _scaleDuration;
        float _flashUntil = -1f;
        bool _highlightRequested;

        public GateSpec Spec { get; private set; }

        public void Initialize(GateSpec spec, VisualTheme theme, Level level)

        {
            Spec = spec;
            _theme = theme;
            name = "Gate_" + spec.Id;
            _colorMaterial = theme.GetLayerMaterial(spec.Color);
            _flashMaterial = theme.wall;

            bool vertical = spec.Side.GetAxis() == Axis.X; // Left/Right gates run along Z
            float sizeX = vertical ? Depth : spec.Length;
            float sizeZ = vertical ? spec.Length : Depth;

            _threshold = BlockView.CreateRenderer("Threshold", transform,
                    MeshLibrary.RoundedSlab(sizeX - PadInset, SurfaceHeight, sizeZ - PadInset, PadRadius), _colorMaterial, castShadows: false)
                .GetComponent<MeshRenderer>();
            _threshold.receiveShadows = true;

            _symbol = new GameObject("Symbol");
            _symbol.transform.SetParent(transform, false);
            _symbol.transform.localPosition = new Vector3(0f, SurfaceHeight + 0.004f, 0f);
            _symbolOutline = BlockView.CreateRenderer("Outline", _symbol.transform, MeshLibrary.Symbol(spec.Color, SymbolSize), theme.dark, castShadows: false)
                .GetComponent<MeshFilter>();
            var fill = BlockView.CreateRenderer("Fill", _symbol.transform, MeshLibrary.Symbol(spec.Color, SymbolFillSize), theme.white, castShadows: false);
            fill.transform.localPosition = new Vector3(0f, 0.002f, 0f);
            _symbolFill = fill.GetComponent<MeshFilter>();

            _highlight = BlockView.CreateRenderer("Highlight", transform, MeshLibrary.Frame(sizeX + 0.06f, sizeZ + 0.06f, 0.04f), theme.white, castShadows: false);
            _highlight.transform.localPosition = new Vector3(0f, SurfaceHeight + 0.01f, 0f);
            _highlight.SetActive(false);

            BuildBadges(level);
        }

        /// <summary>
        /// Mechanic markers on the tray just outside the threshold, laid out along the gate: lock + key id,
        /// required plates, and the next phase color (GDD 04 §13). They stay visible with color symbols off.
        /// </summary>
        void BuildBadges(Level level)
        {
            var items = new System.Collections.Generic.List<(Transform item, float width)>();
            var root = new GameObject("Badges").transform;
            root.SetParent(transform, false);

            if (Spec.IsLocked)
            {
                var item = new GameObject("Key").transform;
                item.SetParent(root, false);
                _lockGlyph = BlockView.CreateRenderer("Lock", item, MeshLibrary.LockGlyph(GlyphSize, false), _theme.dark, false)
                    .GetComponent<MeshFilter>();
                _lockGlyph.transform.localPosition = new Vector3(-0.09f, 0f, 0f);
                WorldLabel.Create(item, level.Keys[Spec.RequiredKey], LabelHeight, _theme).transform.localPosition = new Vector3(0.09f, 0f, 0f);
                items.Add((item, 0.36f));
            }

            if (Spec.RequiredPlates.Length > 0)
            {
                var item = new GameObject("Plates").transform;
                item.SetParent(root, false);
                var ids = new string[Spec.RequiredPlates.Length];
                for (int i = 0; i < ids.Length; i++) ids[i] = level.Plates[Spec.RequiredPlates[i]].Id;
                BlockView.CreateRenderer("Ring", item, MeshLibrary.Ring(0.065f, 0.035f), _theme.dark, false)
                    .transform.localPosition = new Vector3(-0.08f * ids.Length - 0.04f, 0f, 0f);
                WorldLabel.Create(item, string.Join(" ", ids), LabelHeight, _theme).transform.localPosition = new Vector3(0.05f, 0f, 0f);
                items.Add((item, 0.2f + 0.18f * ids.Length));
            }

            if (Spec.HasPhase)
            {
                var item = new GameObject("NextColor").transform;
                item.SetParent(root, false);
                BlockView.CreateRenderer("Arrow", item, MeshLibrary.Arrow(0.12f), _theme.dark, false).transform.localPosition = new Vector3(-0.1f, 0f, 0f);
                item.GetChild(0).localRotation = Quaternion.Euler(0f, 90f, 0f);
                _nextSwatch = BlockView.CreateRenderer("Swatch", item, MeshLibrary.RoundedSlab(0.14f, 0.03f, 0.14f, 0.04f), _theme.white, false)
                    .GetComponent<MeshRenderer>();
                _nextSwatch.transform.localPosition = new Vector3(0.08f, -0.01f, 0f);
                items.Add((item, 0.34f));
            }

            if (items.Count == 0) return;

            // Badge row sits on the tray band beyond the threshold; X/Z of "along" depends on the edge.
            bool vertical = Spec.Side.GetAxis() == Axis.X;
            var outward = vertical ? new Vector3(Spec.Side.GetSign(), 0f, 0f) : new Vector3(0f, 0f, Spec.Side.GetSign());
            var along = vertical ? Vector3.forward : Vector3.right;
            float total = -BadgeGap;
            foreach (var (_, width) in items) total += width + BadgeGap;
            float cursor = -total * 0.5f;
            foreach (var (item, width) in items)
            {
                item.localPosition = along * (cursor + width * 0.5f);
                cursor += width + BadgeGap;
            }
            // Left/Right gates stack their badges vertically, which reads better than a long row beside the board.
            if (vertical)
            {
                for (int i = 0; i < items.Count; i++)
                    items[i].item.localPosition = Vector3.forward * ((items.Count - 1) * 0.5f - i) * 0.2f;
            }
            root.localPosition = outward * (Depth * 0.5f + (vertical ? 0.22f : 0.12f)) + Vector3.up * (SurfaceHeight * 0.2f);
        }

        /// <summary>Applies the derived mechanic state: live phase color and the lock (GDD 01 §12 F2, F4).</summary>
        public void ApplyState(Board board)
        {
            if (Spec.HasPhase)
            {
                int phase = board.Phase;
                if (phase != _phase)
                {
                    _phase = phase;
                    var color = Spec.ColorAt(phase);
                    _colorMaterial = _theme.GetLayerMaterial(color);
                    if (_flashUntil < 0f) _threshold.sharedMaterial = _colorMaterial;
                    _symbolOutline.sharedMesh = MeshLibrary.Symbol(color, SymbolSize);
                    _symbolFill.sharedMesh = MeshLibrary.Symbol(color, SymbolFillSize);
                    _nextSwatch.sharedMaterial = _theme.GetLayerMaterial(Spec.ColorAt(phase + 1));
                }
            }
            if (_lockGlyph != null)
                _lockGlyph.sharedMesh = MeshLibrary.LockGlyph(GlyphSize, board.HasKey(Spec.RequiredKey));
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
