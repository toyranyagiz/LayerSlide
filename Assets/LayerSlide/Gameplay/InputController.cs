using LayerSlide.Core;
using LayerSlide.Presentation;
using LayerSlide.UI;
using UnityEngine;

namespace LayerSlide.Gameplay
{
    /// <summary>Receives the outcome of a board hold. Implemented by <see cref="GameController"/>.</summary>
    public interface IBoardInputHandler
    {
        Board Board { get; }
        bool AcceptsBoardInput { get; }
        bool ReducedMotion { get; }

        void OnBlockPressed(int block);
        /// <summary>Released before the axis locked: only the layer info is shown.</summary>
        void OnBlockTapped(int block);
        /// <summary>Released after a drag. The handler validates and commits through the rule engine.</summary>
        void OnBlockReleased(int block, int x, int y, int gate);
        void OnInvalidExit(int block, GateSide side, int x, int y);
        void OnGateArmed();
        void OnDragCancelled(int block);
    }

    /// <summary>
    /// Pointer ownership, picking, axis lock, continuous preview, gate intent and snap-on-release (GDD 01 §3–6).
    /// The model is never touched during a hold; only the final release produces at most one command.
    /// Runs every frame without allocations.
    /// </summary>
    [DefaultExecutionOrder(-10)]
    public sealed class InputController : MonoBehaviour
    {
        [SerializeField] BoardPresenter presenter;
        [SerializeField] CameraFramer boardCamera;
        [SerializeField] UIController ui;

        readonly PointerReader _pointer = new PointerReader();
        IBoardInputHandler _handler;

        bool _dragging;
        int _block;
        int _startX, _startY;
        Vector3 _grabOffset;
        bool _axisLocked;
        Axis _axis;
        int _min, _max;
        float _value;          // clamped continuous position on the locked axis
        int _armedGate;
        bool _armedPlayed;
        bool _exitIntentWithoutGate;
        GateSide _exitSide;

        public bool IsDragging => _dragging;

        public void Initialize(IBoardInputHandler handler) => _handler = handler;

        /// <summary>Cancels the preview without a command (focus loss, back key, resize). The finger must lift.</summary>
        public void CancelActiveDrag()
        {
            if (_dragging)
            {
                _dragging = false;
                _handler.OnDragCancelled(_block);
            }
            _pointer.Release();
        }

        void Update()
        {
            if (_handler == null) return;

            var phase = _pointer.Read(out var position);
            switch (phase)
            {
                case PointerPhase.Down:
                    OnDown(position);
                    break;
                case PointerPhase.Move:
                    if (_dragging) OnMove(position);
                    break;
                case PointerPhase.Up:
                    if (_dragging) OnUp(position);
                    break;
                case PointerPhase.Cancel:
                    if (_dragging) CancelActiveDrag();
                    break;
            }
        }

        void OnDown(Vector2 screenPosition)
        {
            // A hold that does not start on a block is simply never dragged; the reader keeps it owned until it lifts.
            if (!_handler.AcceptsBoardInput || ui.IsPointerOverUi(screenPosition)) return;
            if (!ui.GetBoardRectPixels().Contains(screenPosition)) return;

            var board = _handler.Board;
            var ray = boardCamera.Camera.ScreenPointToRay(screenPosition);
            int block = presenter.Pick(ray, board);
            if (block < 0 || !TryHitPlane(ray, out var hit)) return;

            _dragging = true;
            _block = block;
            _startX = board.GetX(block);
            _startY = board.GetY(block);
            _grabOffset = hit - presenter.BlockCenter(block, _startX, _startY);
            _axisLocked = false;
            _armedGate = -1;
            _armedPlayed = false;
            _exitIntentWithoutGate = false;
            _handler.OnBlockPressed(block);
        }

        void OnMove(Vector2 screenPosition)
        {
            if (!TryRequestedPosition(screenPosition, out float requestedX, out float requestedY)) return;

            if (!_axisLocked)
            {
                float dx = requestedX - _startX, dy = requestedY - _startY;
                float ax = Mathf.Abs(dx), ay = Mathf.Abs(dy);
                if (Mathf.Max(ax, ay) + RuleConstants.Epsilon < RuleConstants.AxisLockThreshold) return;

                // Exact ties pick X.
                _axis = ax + RuleConstants.Epsilon >= ay ? Axis.X : Axis.Y;
                _axisLocked = true;
                RuleEngine.GetSlideRange(_handler.Board, _block, _axis, out _min, out _max);
            }

            float requested = _axis == Axis.X ? requestedX : requestedY;
            _value = Mathf.Clamp(requested, _min, _max);
            UpdateExitIntent(requested);

            float previewX = _axis == Axis.X ? _value : _startX;
            float previewY = _axis == Axis.Y ? _value : _startY;
            presenter.UpdateDragPreview(_block, previewX, previewY, _armedGate, _handler.ReducedMotion);
        }

        /// <summary>Arms a gate when the finger pulls at least 0.25 cell past a reachable board edge (GDD 01 §6).</summary>
        void UpdateExitIntent(float requested)
        {
            var board = _handler.Board;
            int gate = -1;
            bool intent = false;

            if (_min - requested + RuleConstants.Epsilon >= RuleConstants.ExitIntentThreshold)
            {
                _exitSide = GateSideExtensions.FromAxis(_axis, -1);
                intent = IsEdge(_min, _exitSide);
                if (intent) gate = FindGateAt(_min, _exitSide, board);
            }
            else if (requested - _max + RuleConstants.Epsilon >= RuleConstants.ExitIntentThreshold)
            {
                _exitSide = GateSideExtensions.FromAxis(_axis, 1);
                intent = IsEdge(_max, _exitSide);
                if (intent) gate = FindGateAt(_max, _exitSide, board);
            }

            _exitIntentWithoutGate = intent && gate < 0;
            _armedGate = gate;
            if (gate >= 0 && !_armedPlayed)
            {
                _armedPlayed = true;
                _handler.OnGateArmed();
            }
        }

        void OnUp(Vector2 screenPosition)
        {
            OnMove(screenPosition);
            _dragging = false;

            if (!_axisLocked)
            {
                _handler.OnBlockTapped(_block);
                return;
            }

            int snapped = _armedGate >= 0
                ? (_exitSide.GetSign() < 0 ? _min : _max)
                : Snap(_value, _axis == Axis.X ? _startX : _startY);
            int x = _axis == Axis.X ? snapped : _startX;
            int y = _axis == Axis.Y ? snapped : _startY;

            if (_armedGate < 0 && _exitIntentWithoutGate)
            {
                int edge = _exitSide.GetSign() < 0 ? _min : _max;
                _handler.OnInvalidExit(_block, _exitSide, _axis == Axis.X ? edge : _startX, _axis == Axis.Y ? edge : _startY);
            }
            _handler.OnBlockReleased(_block, x, y, _armedGate);
        }

        /// <summary>Nearest cell; an exact half rounds away from where the hold started (GDD 01 §4).</summary>
        int Snap(float value, int start)
        {
            float floor = Mathf.Floor(value);
            float fraction = value - floor;
            int low = (int)floor, high = low + 1;
            int result;
            if (Mathf.Abs(fraction - 0.5f) <= RuleConstants.Epsilon)
                result = Mathf.Abs(high - start) >= Mathf.Abs(low - start) ? high : low;
            else
                result = fraction > 0.5f ? high : low;
            return Mathf.Clamp(result, _min, _max);
        }

        bool IsEdge(int position, GateSide side)
        {
            int x = _axis == Axis.X ? position : _startX;
            int y = _axis == Axis.Y ? position : _startY;
            return RuleEngine.IsOnEdge(_handler.Board, _block, x, y, side);
        }

        int FindGateAt(int position, GateSide side, Board board)
        {
            int x = _axis == Axis.X ? position : _startX;
            int y = _axis == Axis.Y ? position : _startY;
            return RuleEngine.FindExitGate(board, _block, x, y, side);
        }

        bool TryRequestedPosition(Vector2 screenPosition, out float x, out float y)
        {
            x = y = 0f;
            var ray = boardCamera.Camera.ScreenPointToRay(screenPosition);
            if (!TryHitPlane(ray, out var hit)) return false;

            var level = _handler.Board.Level;
            var spec = level.Blocks[_block];
            var center = hit - _grabOffset;
            x = center.x + level.Width * 0.5f - spec.Width * 0.5f;
            y = center.z + level.Height * 0.5f - spec.Height * 0.5f;
            return true;
        }

        /// <summary>Intersects with the world Y = 0 plane; dragging always works in that plane.</summary>
        static bool TryHitPlane(Ray ray, out Vector3 hit)
        {
            hit = default;
            if (Mathf.Abs(ray.direction.y) < 1e-6f) return false;
            float t = -ray.origin.y / ray.direction.y;
            if (t < 0f) return false;
            hit = ray.origin + ray.direction * t;
            return true;
        }
    }
}
