using LayerSlide.Core;
using UnityEngine;

namespace LayerSlide.Presentation
{
    /// <summary>
    /// 3D view of one block: a stack of rounded layer slabs with symbols. Animations are simple timed
    /// interpolations driven from Update; the view never decides rule outcomes (GDD 01 rule 8).
    /// </summary>
    public sealed class BlockView : MonoBehaviour
    {
        public const float LayerHeight = 0.16f;
        public const float LowerLayerBase = 0.02f;
        public const float UpperLayerBase = 0.18f;
        public const float SelectionLift = 0.06f;
        const float FootprintInset = 0.08f;
        const float SlabRadius = 0.16f;
        const float ShadowPadding = 0.34f;
        const float GlossDepth = 0.12f;
        const float SymbolDiameter = 0.24f;
        const float SymbolOutline = 0.04f;
        const float RailOffset = 0.24f;
        const float RailLength = 0.12f;
        const float RailWidth = 0.12f;
        const float RailFlashDuration = 0.12f;
        const float BadgeDiameter = 0.16f;

        sealed class LayerVisual
        {
            public Transform Root;
            public GameObject Symbol;
        }

        Transform _lift;
        LayerVisual[] _layers;
        BlockSpec _spec;
        Transform _keyBadge;
        readonly System.Collections.Generic.List<Transform> _railMarkers = new System.Collections.Generic.List<Transform>();
        float _railFlashUntil = -1f;

        // Lift animation.
        float _liftFrom, _liftTo, _liftTime, _liftDuration;

        // Slide animation.
        Vector3 _moveFrom, _moveTo;
        float _moveTime, _moveDuration;

        // Peel animation: the outgoing shell slides out through the gate, then shrinks.
        LayerVisual _peeling;
        Vector3 _peelFrom, _peelTo;
        float _peelTime, _peelDuration;
        bool _peelReduced;
        bool _hideWhenPeeled;
        bool _peelSettleStarted;

        public int BlockIndex { get; private set; }
        public BlockSpec Spec => _spec;
        public float CurrentLift => _lift.localPosition.y;

        /// <summary>World-space top of the visible layer stack, including the selection lift.</summary>
        public float TopY { get; private set; }

        public bool IsAnimating => _moveTime < _moveDuration || _peeling != null || _liftTime < _liftDuration;

        public void Initialize(BlockSpec spec, VisualTheme theme, string keyId)
        {
            _spec = spec;
            BlockIndex = spec.Index;
            name = "Block_" + spec.Id;

            _lift = new GameObject("Lift").transform;
            _lift.SetParent(transform, false);

            // Soft contact shadow on the board; it stays down when the block lifts, which sells the lift.
            if (theme.blobShadow != null)
            {
                var shadow = CreateMeshObject("BlobShadow", transform,
                    MeshLibrary.Quad(spec.Width + ShadowPadding, spec.Height + ShadowPadding), theme.blobShadow, castShadows: false);
                shadow.transform.localPosition = new Vector3(0f, 0.004f, -0.04f);
                shadow.GetComponent<MeshRenderer>().receiveShadows = false;
            }

            var slab = MeshLibrary.RoundedSlab(spec.Width - FootprintInset, LayerHeight, spec.Height - FootprintInset, SlabRadius);
            _layers = new LayerVisual[spec.LayerCount];
            for (int i = 0; i < spec.LayerCount; i++)
            {
                var color = spec.Layers[i];
                var layer = CreateMeshObject("Layer" + i, _lift, slab, theme.GetLayerMaterial(color), castShadows: true);

                // Candy gloss: a soft white band along the far top edge of the layer.
                if (theme.gloss != null)
                {
                    float glossWidth = spec.Width - FootprintInset - 0.24f;
                    var gloss = CreateMeshObject("Gloss", layer.transform, MeshLibrary.RoundedSlab(glossWidth, 0.002f, GlossDepth, GlossDepth * 0.5f),
                        theme.gloss, castShadows: false);
                    gloss.transform.localPosition = new Vector3(0f, LayerHeight + 0.001f, (spec.Height - FootprintInset) * 0.5f - 0.14f - GlossDepth * 0.5f);
                    gloss.GetComponent<MeshRenderer>().receiveShadows = false;
                }

                var symbol = new GameObject("Symbol");
                symbol.transform.SetParent(layer.transform, false);
                symbol.transform.localPosition = new Vector3(0f, LayerHeight + 0.004f, 0f);
                CreateMeshObject("Outline", symbol.transform, MeshLibrary.Symbol(color, SymbolDiameter), theme.dark, castShadows: false);
                var fill = CreateMeshObject("Fill", symbol.transform, MeshLibrary.Symbol(color, SymbolDiameter - SymbolOutline), theme.white, castShadows: false);
                fill.transform.localPosition = new Vector3(0f, 0.002f, 0f);

                // Rail arrows stay visible when color symbols are off: they are mechanic information (GDD 04 §13).
                if (spec.MoveAxis != MoveAxis.Free)
                {
                    var rail = CreateMeshObject("Rail", layer.transform, MeshLibrary.RailArrows(RailOffset, RailLength, RailWidth),
                        theme.white, castShadows: false);
                    rail.transform.localPosition = new Vector3(0f, LayerHeight + 0.006f, 0f);
                    rail.transform.localRotation = Quaternion.Euler(0f, spec.MoveAxis == MoveAxis.X ? 90f : 0f, 0f);
                    _railMarkers.Add(rail.transform);
                }

                _layers[i] = new LayerVisual { Root = layer.transform, Symbol = symbol };
            }

            // Key badge on the top-left corner; it rides on the lift so it stays until the last layer is gone.
            if (spec.CarriesKey)
            {
                _keyBadge = new GameObject("KeyBadge").transform;
                _keyBadge.SetParent(_lift, false);
                CreateMeshObject("Outline", _keyBadge, MeshLibrary.Symbol(LayerColor.Red, BadgeDiameter + 0.03f), theme.dark, castShadows: false);
                var disc = CreateMeshObject("Disc", _keyBadge, MeshLibrary.Symbol(LayerColor.Red, BadgeDiameter), theme.white, castShadows: false);
                disc.transform.localPosition = new Vector3(0f, 0.002f, 0f);
                var glyph = CreateMeshObject("Key", _keyBadge, MeshLibrary.KeyGlyph(BadgeDiameter * 0.75f), theme.dark, castShadows: false);
                glyph.transform.localPosition = new Vector3(0f, 0.004f, 0f);
                var label = WorldLabel.Create(_keyBadge, keyId, 0.13f, theme);
                label.transform.localPosition = new Vector3(BadgeDiameter * 0.5f + 0.09f, 0.004f, 0f);
                _keyBadge.localPosition = new Vector3(-(spec.Width - FootprintInset) * 0.5f + 0.13f, 0f,
                    (spec.Height - FootprintInset) * 0.5f - 0.13f);
            }
        }

        /// <summary>Forbidden-axis feedback: the rail arrows grow for 120 ms (no move, no sound repeat).</summary>
        public void FlashRail()
        {
            if (_railMarkers.Count == 0) return;
            _railFlashUntil = Time.unscaledTime + RailFlashDuration;
            foreach (var marker in _railMarkers) marker.localScale = Vector3.one * 1.35f;
        }

        public void SetSymbolsVisible(bool visible)
        {
            foreach (var layer in _layers)
                layer.Symbol.SetActive(visible);
        }

        /// <summary>Instantly shows the state with <paramref name="removedLayers"/> peeled; cancels any animation.</summary>
        public void ShowLayers(int removedLayers)
        {
            _peeling = null;
            int remaining = _spec.LayerCount - removedLayers;
            gameObject.SetActive(remaining > 0);

            for (int i = 0; i < _layers.Length; i++)
            {
                var root = _layers[i].Root;
                bool visible = i >= removedLayers;
                root.gameObject.SetActive(visible);
                root.localScale = Vector3.one;
                if (!visible) continue;

                // The active layer sits on top only when a lower layer is still under it.
                bool isUpper = remaining == 2 && i == removedLayers;
                root.localPosition = new Vector3(0f, isUpper ? UpperLayerBase : LowerLayerBase, 0f);
            }
            TopY = (remaining == 2 ? UpperLayerBase : LowerLayerBase) + LayerHeight;
            PlaceKeyBadge();
        }

        void PlaceKeyBadge()
        {
            if (_keyBadge == null) return;
            var position = _keyBadge.localPosition;
            _keyBadge.localPosition = new Vector3(position.x, TopY + 0.008f, position.z);
        }

        public void SetPosition(Vector3 position)
        {
            transform.localPosition = position;
            _moveDuration = 0f;
            _moveTime = 0f;
        }

        public void MoveTo(Vector3 position, float duration)
        {
            if (duration <= 0f)
            {
                SetPosition(position);
                return;
            }
            _moveFrom = transform.localPosition;
            _moveTo = position;
            _moveTime = 0f;
            _moveDuration = duration;
        }

        public void SetLift(bool lifted, float duration)
        {
            float target = lifted ? SelectionLift : 0f;
            if (duration <= 0f)
            {
                _lift.localPosition = new Vector3(0f, target, 0f);
                _liftDuration = 0f;
                _liftTime = 0f;
                return;
            }
            _liftFrom = _lift.localPosition.y;
            _liftTo = target;
            _liftTime = 0f;
            _liftDuration = duration;
        }

        /// <summary>
        /// Animates the top layer leaving through a gate in <paramref name="outward"/> direction. The model has
        /// already been committed; this only shows it. When <paramref name="lastLayer"/> the block hides afterwards.
        /// </summary>
        public void PlayPeel(int peeledLayer, Vector3 outward, float duration, bool reducedMotion, bool lastLayer)
        {
            _peeling = _layers[peeledLayer];
            _peelFrom = _peeling.Root.localPosition;
            float halfExtent = Mathf.Abs(outward.x) > 0f ? _spec.Width * 0.5f : _spec.Height * 0.5f;
            _peelTo = _peelFrom + outward * (halfExtent + 1f);
            _peelTime = 0f;
            _peelDuration = duration;
            _peelReduced = reducedMotion;
            _hideWhenPeeled = lastLayer;
            _peelSettleStarted = false;
        }

        void Update()
        {
            float dt = Time.deltaTime;

            if (_liftTime < _liftDuration)
            {
                _liftTime += dt;
                float y = Mathf.Lerp(_liftFrom, _liftTo, Ease.OutCubic(_liftTime / _liftDuration));
                _lift.localPosition = new Vector3(0f, y, 0f);
            }

            if (_moveTime < _moveDuration)
            {
                _moveTime += dt;
                transform.localPosition = Vector3.LerpUnclamped(_moveFrom, _moveTo, Ease.OutCubic(_moveTime / _moveDuration));
            }

            if (_peeling != null)
                UpdatePeel(dt);

            if (_railFlashUntil > 0f && Time.unscaledTime >= _railFlashUntil)
            {
                _railFlashUntil = -1f;
                foreach (var marker in _railMarkers) marker.localScale = Vector3.one;
            }
        }

        void UpdatePeel(float dt)
        {
            _peelTime += dt;
            var root = _peeling.Root;

            if (_peelReduced)
            {
                root.localScale = Vector3.one * (1f - Mathf.Clamp01(_peelTime / _peelDuration));
            }
            else
            {
                // 240 ms outward slide whose last 80 ms also shrink the shell to zero, then a 60 ms settle that
                // only lowers the selection lift; the remaining layer never drops (GDD 04 §8, §12).
                const float slide = 0.24f, shrink = 0.08f, settle = 0.06f;
                float t = Mathf.Clamp01(_peelTime / slide);
                root.localPosition = Vector3.Lerp(_peelFrom, _peelTo, Ease.OutCubic(t));
                float shrinkT = Mathf.Clamp01((_peelTime - (slide - shrink)) / shrink);
                root.localScale = Vector3.one * (1f - shrinkT);
                if (!_peelSettleStarted && _peelTime >= slide)
                {
                    _peelSettleStarted = true;
                    SetLift(false, settle);
                }
            }

            if (_peelTime < _peelDuration) return;

            root.gameObject.SetActive(false);
            root.localScale = Vector3.one;
            _peeling = null;
            TopY = LowerLayerBase + LayerHeight;
            PlaceKeyBadge();

            if (_hideWhenPeeled)
                gameObject.SetActive(false);
        }

        static GameObject CreateMeshObject(string name, Transform parent, Mesh mesh, Material material, bool castShadows)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = castShadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = castShadows;
            return go;
        }

        internal static GameObject CreateRenderer(string name, Transform parent, Mesh mesh, Material material, bool castShadows) =>
            CreateMeshObject(name, parent, mesh, material, castShadows);
    }

    public static class Ease
    {
        public static float OutCubic(float t)
        {
            t = Mathf.Clamp01(t);
            float inv = 1f - t;
            return 1f - inv * inv * inv;
        }
    }
}
