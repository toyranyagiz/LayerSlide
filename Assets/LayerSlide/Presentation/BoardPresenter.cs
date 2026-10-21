using System.Collections.Generic;
using LayerSlide.Core;
using UnityEngine;

namespace LayerSlide.Presentation
{
    /// <summary>
    /// Turns the board model into 3D objects and plays their animations (GDD 06 §3). Board cell (x, y) maps to
    /// world X-Z with the board centered on the origin; world Y is visual height only.
    /// </summary>
    public sealed class BoardPresenter : MonoBehaviour
    {
        const float TileSize = 0.96f;
        const float TileThickness = 0.04f;
        const float WallHeight = 0.24f;
        const float PerimeterThickness = 0.12f;
        const float PerimeterHeight = 0.20f;
        const float FrameThickness = 0.03f;
        const float FrameY = 0.005f;
        const float FlashDuration = 0.12f;
        const float FootprintInset = 0.08f;

        [SerializeField] VisualTheme theme;

        Transform _tilesRoot, _wallsRoot, _gatesRoot, _blocksRoot, _fxRoot;
        readonly List<BlockView> _blocks = new List<BlockView>();
        readonly List<GateView> _gates = new List<GateView>();
        readonly List<(Vector3 min, Vector3 max)> _wallBounds = new List<(Vector3, Vector3)>();

        Level _level;
        FxPool _fx;
        MeshRenderer _selectionFrame;
        GameObject _hintRoot;
        MeshRenderer _hintBlockFrame, _hintTargetFrame, _hintArrow;
        GameObject _tutorialHand;
        int _hintGate = -1;
        int _armedGate = -1;
        float _selectionFlashUntil = -1f;

        // Deferred last-layer burst, fired when the peel animation ends.
        float _burstAt = -1f;
        Vector3 _burstPosition;
        LayerColor _burstColor;
        int _burstSeed;

        // Tutorial hand stroke.
        const float HandDuration = 1f;
        Vector3 _handFrom, _handTo;
        float _handTime = -1f;
        bool _handReduced;

        public VisualTheme Theme => theme;
        public Level Level => _level;

        void Awake()
        {
            _tilesRoot = CreateRoot("TilesRoot");
            _wallsRoot = CreateRoot("WallsRoot");
            _gatesRoot = CreateRoot("GatesRoot");
            _blocksRoot = CreateRoot("BlocksRoot");
            _fxRoot = CreateRoot("FxRoot");

            _fx = _fxRoot.gameObject.AddComponent<FxPool>();
            _fx.Initialize(theme);

            _selectionFrame = BlockView.CreateRenderer("SelectionFrame", _fxRoot, MeshLibrary.Frame(1f, 1f, FrameThickness), theme.white, false)
                .GetComponent<MeshRenderer>();
            _selectionFrame.gameObject.SetActive(false);

            _hintRoot = new GameObject("Hint");
            _hintRoot.transform.SetParent(_fxRoot, false);
            _hintBlockFrame = BlockView.CreateRenderer("BlockFrame", _hintRoot.transform, MeshLibrary.Frame(1f, 1f, 0.05f), theme.white, false).GetComponent<MeshRenderer>();
            _hintTargetFrame = BlockView.CreateRenderer("TargetFrame", _hintRoot.transform, MeshLibrary.Frame(1f, 1f, 0.03f), theme.white, false).GetComponent<MeshRenderer>();
            _hintArrow = BlockView.CreateRenderer("Arrow", _hintRoot.transform, MeshLibrary.Arrow(1f), theme.white, false).GetComponent<MeshRenderer>();
            _hintRoot.SetActive(false);

            _tutorialHand = new GameObject("TutorialHand");
            _tutorialHand.transform.SetParent(_fxRoot, false);
            BlockView.CreateRenderer("Outline", _tutorialHand.transform, MeshLibrary.Symbol(LayerColor.Red, 0.34f), theme.dark, false);
            BlockView.CreateRenderer("Fill", _tutorialHand.transform, MeshLibrary.Symbol(LayerColor.Red, 0.28f), theme.white, false)
                .transform.localPosition = new Vector3(0f, 0.002f, 0f);
            _tutorialHand.SetActive(false);
        }

        Transform CreateRoot(string rootName)
        {
            var root = new GameObject(rootName).transform;
            root.SetParent(transform, false);
            return root;
        }

        // ---------------------------------------------------------------- Build

        public void Build(Level level, Board board, bool showSymbols)
        {
            _level = level;
            ClearChildren(_tilesRoot);
            ClearChildren(_wallsRoot);
            ClearChildren(_gatesRoot);
            ClearChildren(_blocksRoot);
            _blocks.Clear();
            _gates.Clear();
            _wallBounds.Clear();
            _fx.Clear();
            HideHint();
            HideTutorialHand();
            _selectionFrame.gameObject.SetActive(false);
            _armedGate = -1;
            _burstAt = -1f;

            var tileMesh = MeshLibrary.Box(TileSize, TileThickness, TileSize);
            var wallMesh = MeshLibrary.Box(TileSize, WallHeight, TileSize);
            var crossMesh = MeshLibrary.Cross(0.3f, 0.05f);

            for (int y = 0; y < level.Height; y++)
            for (int x = 0; x < level.Width; x++)
            {
                if (level.IsWall(x, y))
                {
                    var wall = BlockView.CreateRenderer($"Wall_{x}_{y}", _wallsRoot, wallMesh, theme.wall, true);
                    var center = CellCenter(x, y);
                    wall.transform.localPosition = center;
                    BlockView.CreateRenderer("Cross", wall.transform, crossMesh, theme.dark, false).transform.localPosition = new Vector3(0f, WallHeight + 0.004f, 0f);
                    _wallBounds.Add((center - new Vector3(0.48f, 0f, 0.48f), center + new Vector3(0.48f, WallHeight, 0.48f)));
                }
                else
                {
                    var tile = BlockView.CreateRenderer($"Tile_{x}_{y}", _tilesRoot, tileMesh, theme.board, false);
                    tile.transform.localPosition = CellCenter(x, y) + Vector3.down * TileThickness;
                    tile.GetComponent<MeshRenderer>().receiveShadows = true;
                }
            }

            BuildPerimeter(level);

            foreach (var spec in level.Gates)
            {
                var view = new GameObject().AddComponent<GateView>();
                view.transform.SetParent(_gatesRoot, false);
                view.Initialize(spec, theme);
                view.transform.localPosition = GateCenter(level, spec);
                view.SetSymbolVisible(showSymbols);
                _gates.Add(view);
            }
            BuildGateDividers(level);

            foreach (var spec in level.Blocks)
            {
                var view = new GameObject().AddComponent<BlockView>();
                view.transform.SetParent(_blocksRoot, false);
                view.Initialize(spec, theme);
                view.SetSymbolsVisible(showSymbols);
                _blocks.Add(view);
            }

            SyncAll(board);
        }

        /// <summary>Instantly matches every view to the model; cancels running animations.</summary>
        public void SyncAll(Board board)
        {
            for (int b = 0; b < _blocks.Count; b++)
            {
                var view = _blocks[b];
                view.ShowLayers(board.GetRemovedLayers(b));
                view.SetLift(false, 0f);
                if (!board.IsRemoved(b))
                    view.SetPosition(BlockCenter(b, board.GetX(b), board.GetY(b)));
            }
            foreach (var gate in _gates)
                gate.ResetVisuals();
            _selectionFrame.gameObject.SetActive(false);
            _armedGate = -1;
            _burstAt = -1f;
        }

        public void SetSymbolsVisible(bool visible)
        {
            foreach (var block in _blocks) block.SetSymbolsVisible(visible);
            foreach (var gate in _gates) gate.SetSymbolVisible(visible);
        }

        void BuildPerimeter(Level level)
        {
            var mesh = MeshLibrary.Box(1f, PerimeterHeight, 1f);
            float t = PerimeterThickness;
            foreach (GateSide side in new[] { GateSide.Left, GateSide.Right, GateSide.Bottom, GateSide.Top })
            {
                bool vertical = side.GetAxis() == Axis.X;
                // Bottom/Top segments also cover the corners.
                float from = vertical ? 0f : -t;
                float to = vertical ? level.Height : level.Width + t;

                var openings = new List<(float start, float end)>();
                foreach (var gate in level.Gates)
                    if (gate.Side == side)
                        openings.Add((gate.Start, gate.End));
                openings.Sort((a, b) => a.start.CompareTo(b.start));

                float cursor = from;
                foreach (var (start, end) in openings)
                {
                    AddPerimeterSegment(level, mesh, side, cursor, start);
                    cursor = end;
                }
                AddPerimeterSegment(level, mesh, side, cursor, to);
            }
        }

        void AddPerimeterSegment(Level level, Mesh mesh, GateSide side, float from, float to)
        {
            if (to - from < 0.001f) return;

            bool vertical = side.GetAxis() == Axis.X;
            float t = PerimeterThickness;
            float along = (from + to) * 0.5f;
            float length = to - from;
            Vector3 center;
            switch (side)
            {
                case GateSide.Left: center = new Vector3(-level.Width * 0.5f - t * 0.5f, 0f, along - level.Height * 0.5f); break;
                case GateSide.Right: center = new Vector3(level.Width * 0.5f + t * 0.5f, 0f, along - level.Height * 0.5f); break;
                case GateSide.Bottom: center = new Vector3(along - level.Width * 0.5f, 0f, -level.Height * 0.5f - t * 0.5f); break;
                default: center = new Vector3(along - level.Width * 0.5f, 0f, level.Height * 0.5f + t * 0.5f); break;
            }

            var segment = BlockView.CreateRenderer("Perimeter", _wallsRoot, mesh, theme.perimeter, true);
            segment.transform.localPosition = center;
            segment.transform.localScale = vertical ? new Vector3(t, 1f, length) : new Vector3(length, 1f, t);

            var size = vertical ? new Vector3(t, PerimeterHeight, length) : new Vector3(length, PerimeterHeight, t);
            _wallBounds.Add((center - new Vector3(size.x * 0.5f, 0f, size.z * 0.5f), center + new Vector3(size.x * 0.5f, size.y, size.z * 0.5f)));
        }

        /// <summary>Adjacent gates on one edge are separate; a thin dark divider makes that readable.</summary>
        void BuildGateDividers(Level level)
        {
            var mesh = MeshLibrary.Box(1f, GateView.SurfaceHeight + 0.006f, 1f);
            foreach (var a in level.Gates)
            foreach (var b in level.Gates)
            {
                if (a.Side != b.Side || a.End != b.Start) continue;
                bool vertical = a.Side.GetAxis() == Axis.X;
                var divider = BlockView.CreateRenderer("GateDivider", _gatesRoot, mesh, theme.dark, false);
                var gateCenter = GateCenter(level, a);
                float boundary = a.End - (vertical ? level.Height : level.Width) * 0.5f;
                divider.transform.localPosition = vertical ? new Vector3(gateCenter.x, 0f, boundary) : new Vector3(boundary, 0f, gateCenter.z);
                divider.transform.localScale = vertical ? new Vector3(GateView.Depth, 1f, 0.02f) : new Vector3(0.02f, 1f, GateView.Depth);
            }
        }

        // ---------------------------------------------------------------- Coordinates

        public Vector3 CellCenter(int x, int y) =>
            new Vector3(x + 0.5f - _level.Width * 0.5f, 0f, y + 0.5f - _level.Height * 0.5f);

        /// <summary>World center of a block footprint placed at continuous cell position (x, y).</summary>
        public Vector3 BlockCenter(int block, float x, float y)
        {
            var spec = _level.Blocks[block];
            return new Vector3(x + spec.Width * 0.5f - _level.Width * 0.5f, 0f, y + spec.Height * 0.5f - _level.Height * 0.5f);
        }

        static Vector3 GateCenter(Level level, GateSpec gate)
        {
            float mid = gate.Start + gate.Length * 0.5f;
            float half = GateView.Depth * 0.5f;
            switch (gate.Side)
            {
                case GateSide.Left: return new Vector3(-level.Width * 0.5f - half, 0f, mid - level.Height * 0.5f);
                case GateSide.Right: return new Vector3(level.Width * 0.5f + half, 0f, mid - level.Height * 0.5f);
                case GateSide.Bottom: return new Vector3(mid - level.Width * 0.5f, 0f, -level.Height * 0.5f - half);
                default: return new Vector3(mid - level.Width * 0.5f, 0f, level.Height * 0.5f + half);
            }
        }

        static Vector3 Outward(GateSide side)
        {
            switch (side)
            {
                case GateSide.Left: return Vector3.left;
                case GateSide.Right: return Vector3.right;
                case GateSide.Bottom: return Vector3.back;
                default: return Vector3.forward;
            }
        }

        // ---------------------------------------------------------------- Picking

        /// <summary>
        /// Analytic slab test against the visible block boxes and walls (GDD 06 §5). Returns the nearest
        /// block, or -1 when nothing or a wall is hit first. Ties prefer walls, then the alphabetically first block.
        /// </summary>
        public int Pick(Ray ray, Board board)
        {
            float best = float.PositiveInfinity;
            int bestBlock = -1;

            foreach (var (min, max) in _wallBounds)
            {
                if (IntersectBox(ray, min, max, out float distance) && distance < best)
                {
                    best = distance;
                    bestBlock = -1;
                }
            }

            for (int b = 0; b < _blocks.Count; b++)
            {
                if (board.IsRemoved(b)) continue;
                var view = _blocks[b];
                var spec = view.Spec;
                var center = view.transform.localPosition;
                var half = new Vector3((spec.Width - FootprintInset) * 0.5f, 0f, (spec.Height - FootprintInset) * 0.5f);
                var min = center - half + Vector3.up * (BlockView.LowerLayerBase + view.CurrentLift);
                var max = center + half + Vector3.up * (view.TopY + view.CurrentLift);
                if (IntersectBox(ray, min, max, out float distance) && distance < best)
                {
                    best = distance;
                    bestBlock = b;
                }
            }
            return bestBlock;
        }

        static bool IntersectBox(Ray ray, Vector3 min, Vector3 max, out float distance)
        {
            float tMin = 0f, tMax = float.PositiveInfinity;
            for (int axis = 0; axis < 3; axis++)
            {
                float origin = ray.origin[axis], direction = ray.direction[axis];
                if (Mathf.Abs(direction) < 1e-8f)
                {
                    if (origin < min[axis] || origin > max[axis]) { distance = 0f; return false; }
                    continue;
                }
                float inv = 1f / direction;
                float t1 = (min[axis] - origin) * inv, t2 = (max[axis] - origin) * inv;
                if (t1 > t2) (t1, t2) = (t2, t1);
                tMin = Mathf.Max(tMin, t1);
                tMax = Mathf.Min(tMax, t2);
                if (tMin > tMax) { distance = 0f; return false; }
            }
            distance = tMin;
            return true;
        }

        // ---------------------------------------------------------------- Drag

        public void BeginDrag(int block, bool reducedMotion)
        {
            var view = _blocks[block];
            view.SetLift(true, reducedMotion ? 0f : 0.08f);
            _selectionFrame.sharedMaterial = theme.white;
            ShowSelectionFrame(block);
            HideHint();
            HideTutorialHand();
        }

        /// <summary>Shows the block at a continuous (already clamped) cell position and the armed gate, if any.</summary>
        public void UpdateDragPreview(int block, float x, float y, int armedGate, bool reducedMotion)
        {
            _blocks[block].SetPosition(BlockCenter(block, x, y));
            UpdateSelectionFrame(block);

            if (armedGate == _armedGate) return;
            if (_armedGate >= 0) _gates[_armedGate].SetArmed(false, reducedMotion);
            if (armedGate >= 0) _gates[armedGate].SetArmed(true, reducedMotion);
            _armedGate = armedGate;
        }

        /// <summary>Returns the dragged block to a cell without a command (no-op release or cancel).</summary>
        public void ReturnBlock(int block, int x, int y, bool reducedMotion)
        {
            ClearArmed(reducedMotion);
            var view = _blocks[block];
            view.MoveTo(BlockCenter(block, x, y), reducedMotion ? 0f : 0.1f);
            view.SetLift(false, reducedMotion ? 0f : 0.1f);
            _selectionFrame.gameObject.SetActive(false);
        }

        void ClearArmed(bool reducedMotion)
        {
            if (_armedGate >= 0) _gates[_armedGate].SetArmed(false, reducedMotion);
            _armedGate = -1;
        }

        void ShowSelectionFrame(int block)
        {
            var spec = _level.Blocks[block];
            _selectionFrame.GetComponent<MeshFilter>().sharedMesh = MeshLibrary.Frame(spec.Width, spec.Height, FrameThickness);
            UpdateSelectionFrame(block);
            _selectionFrame.gameObject.SetActive(true);
        }

        void UpdateSelectionFrame(int block)
        {
            var center = _blocks[block].transform.localPosition;
            _selectionFrame.transform.localPosition = new Vector3(center.x, FrameY, center.z);
        }

        /// <summary>Invalid exit attempt feedback: gate flash when a gate is there, otherwise the block frame.</summary>
        public void FlashInvalidExit(int block, GateSide side, int x, int y)
        {
            int nearGate = -1;
            var spec = _level.Blocks[block];
            bool vertical = side.GetAxis() == Axis.X;
            int from = vertical ? y : x, to = from + (vertical ? spec.Height : spec.Width);
            for (int g = 0; g < _gates.Count; g++)
            {
                var gate = _level.Gates[g];
                if (gate.Side == side && from < gate.End && gate.Start < to) { nearGate = g; break; }
            }

            if (nearGate >= 0)
            {
                _gates[nearGate].Flash();
                return;
            }
            ShowSelectionFrame(block);
            _selectionFrame.sharedMaterial = theme.wall;
            _selectionFlashUntil = Time.unscaledTime + FlashDuration;
        }

        // ---------------------------------------------------------------- Commands

        public void AnimateCommand(CommandRecord command, bool reducedMotion, int seed)
        {
            int block = _level.IndexOfBlock(command.blockId);
            var view = _blocks[block];
            var spec = _level.Blocks[block];
            ClearArmed(reducedMotion);
            _selectionFrame.gameObject.SetActive(false);

            var end = BlockCenter(block, command.visualEndX, command.visualEndY);
            if (!command.IsPeel)
            {
                view.MoveTo(end, reducedMotion ? 0f : 0.1f);
                view.SetLift(false, reducedMotion ? 0f : 0.1f);
                return;
            }

            // Slide + peel: the block is shown at the edge cell first, then the top shell leaves.
            view.SetPosition(end);
            var gate = _level.Gates[_level.IndexOfGate(command.gateId)];
            bool lastLayer = command.afterRemoved >= spec.LayerCount;
            float duration = reducedMotion ? 0.1f : 0.3f;
            view.PlayPeel(command.beforeRemoved, Outward(gate.Side), duration, reducedMotion, lastLayer);
            view.SetLift(false, reducedMotion ? 0f : duration);

            if (lastLayer && !reducedMotion)
            {
                _burstAt = Time.time + duration;
                _burstPosition = end + Vector3.up * 0.1f;
                _burstColor = spec.Layers[command.beforeRemoved];
                _burstSeed = seed;
            }
        }

        public void AnimateUndo(CommandRecord command, bool reducedMotion)
        {
            int block = _level.IndexOfBlock(command.blockId);
            var view = _blocks[block];
            bool wasRemoved = !view.gameObject.activeSelf;
            view.ShowLayers(command.beforeRemoved);
            var target = BlockCenter(block, command.beforeX, command.beforeY);
            if (wasRemoved)
                view.SetPosition(BlockCenter(block, command.visualEndX, command.visualEndY));
            view.MoveTo(target, reducedMotion ? 0f : 0.1f);
            view.SetLift(false, 0f);
            _burstAt = -1f;
        }

        public void PlayWin(int seed, bool reducedMotion)
        {
            if (!reducedMotion)
                _fx.Confetti(Vector3.up * 0.4f, 20, seed);
        }

        // ---------------------------------------------------------------- Hint

        public void ShowHint(SolverAction action, Board board)
        {
            var spec = _level.Blocks[action.Block];
            var start = BlockCenter(action.Block, board.GetX(action.Block), board.GetY(action.Block));
            var target = BlockCenter(action.Block, action.X, action.Y);
            float surface = _blocks[action.Block].TopY + 0.03f;

            _hintBlockFrame.GetComponent<MeshFilter>().sharedMesh = MeshLibrary.Frame(spec.Width + 0.08f, spec.Height + 0.08f, 0.05f);
            _hintBlockFrame.transform.localPosition = new Vector3(start.x, FrameY + 0.002f, start.z);

            bool moves = action.X != board.GetX(action.Block) || action.Y != board.GetY(action.Block);
            _hintTargetFrame.gameObject.SetActive(moves && !action.IsPeel);
            _hintTargetFrame.GetComponent<MeshFilter>().sharedMesh = MeshLibrary.Frame(spec.Width, spec.Height, FrameThickness);
            _hintTargetFrame.transform.localPosition = new Vector3(target.x, FrameY + 0.002f, target.z);

            Vector3 arrowEnd = target;
            Vector3 direction = (target - start).normalized;
            if (action.IsPeel)
            {
                var outward = Outward(_level.Gates[action.Gate].Side);
                float halfExtent = Mathf.Abs(outward.x) > 0f ? spec.Width * 0.5f : spec.Height * 0.5f;
                arrowEnd = target + outward * (halfExtent + 0.4f);
                direction = outward;
            }

            float length = Vector3.Distance(start, arrowEnd);
            _hintArrow.GetComponent<MeshFilter>().sharedMesh = MeshLibrary.Arrow(Mathf.Round(length * 10f) / 10f);
            _hintArrow.transform.localPosition = new Vector3(start.x, surface, start.z);
            _hintArrow.transform.localRotation = Quaternion.LookRotation(direction, Vector3.up);

            if (_hintGate >= 0) _gates[_hintGate].SetHighlighted(false);
            _hintGate = action.Gate;
            if (_hintGate >= 0) _gates[_hintGate].SetHighlighted(true);

            _hintRoot.SetActive(true);
        }

        public void HideHint()
        {
            if (_hintRoot != null) _hintRoot.SetActive(false);
            if (_hintGate >= 0 && _hintGate < _gates.Count) _gates[_hintGate].SetHighlighted(false);
            _hintGate = -1;
        }

        // ---------------------------------------------------------------- Tutorial hand (L001)

        /// <summary>One 1-second stroke from <paramref name="from"/> to <paramref name="to"/>; static when motion is reduced.</summary>
        public void PlayTutorialHand(Vector3 from, Vector3 to, bool reducedMotion)
        {
            _handFrom = from + Vector3.up * 0.4f;
            _handTo = to + Vector3.up * 0.4f;
            _handTime = 0f;
            _tutorialHand.transform.localPosition = reducedMotion ? _handTo : _handFrom;
            _tutorialHand.SetActive(true);
            _handReduced = reducedMotion;
        }

        public void HideTutorialHand()
        {
            _handTime = -1f;
            if (_tutorialHand != null) _tutorialHand.SetActive(false);
        }

        void Update()
        {
            if (_burstAt > 0f && Time.time >= _burstAt)
            {
                _burstAt = -1f;
                _fx.Burst(_burstPosition, _burstColor, 8, _burstSeed);
            }

            if (_selectionFlashUntil > 0f && Time.unscaledTime >= _selectionFlashUntil)
            {
                _selectionFlashUntil = -1f;
                _selectionFrame.sharedMaterial = theme.white;
                _selectionFrame.gameObject.SetActive(false);
            }

            if (_handTime >= 0f)
            {
                _handTime += Time.deltaTime;
                if (!_handReduced)
                    _tutorialHand.transform.localPosition = Vector3.Lerp(_handFrom, _handTo, Ease.OutCubic(_handTime / HandDuration));
                if (_handTime >= HandDuration)
                    HideTutorialHand();
            }
        }

        static void ClearChildren(Transform root)
        {
            for (int i = root.childCount - 1; i >= 0; i--)
                Destroy(root.GetChild(i).gameObject);
        }
    }
}
