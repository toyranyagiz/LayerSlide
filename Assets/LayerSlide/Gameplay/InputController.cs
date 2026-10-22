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
        /// <summary>Outward release on a reachable edge that no gate accepts; <paramref name="check"/> says what is missing.</summary>
        void OnInvalidExit(int block, GateSide side, int x, int y, GateCheck check, int gate);
        /// <summary>A rail block was pulled only along its forbidden axis (F1).</summary>
        void OnForbiddenAxis(int block);
        void OnGateArmed();
        void OnDragCancelled(int block);
    }

    /// <summary>
    /// Turns pointer input into board holds: ownership, analytic picking, the world-plane grab offset and preview.
    /// The drag math itself lives in <see cref="DragResolver"/>. The model is never touched during a hold and the
    /// per-frame path does not allocate.
    /// </summary>
    [DefaultExecutionOrder(-10)]
    public sealed class InputController : MonoBehaviour
    {
        [SerializeField] BoardPresenter presenter;
        [SerializeField] CameraFramer boardCamera;
        [SerializeField] UIController ui;

        IPointerSource _pointer = new PointerReader();
        readonly DragResolver _drag = new DragResolver();
        IBoardInputHandler _handler;
        bool _dragging;
        bool _armedSoundPlayed;
        Vector3 _grabOffset;

        public bool IsDragging => _dragging;

        public void Initialize(IBoardInputHandler handler) => _handler = handler;

        /// <summary>Test seam: replaces the device reader (e.g. with a scripted pointer).</summary>
        internal void SetPointerSource(IPointerSource source) => _pointer = source;

        /// <summary>Cancels the preview without a command (focus loss, back key, resize). The finger must lift.</summary>
        public void CancelActiveDrag()
        {
            if (_dragging)
            {
                _dragging = false;
                _handler.OnDragCancelled(_drag.Block);
            }
            _pointer.Release();
        }

        void Update()
        {
            if (_handler == null) return;

            switch (_pointer.Read(out var position))
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

        /// <summary>A hold that does not start on a block is never dragged; the reader keeps it owned until it lifts.</summary>
        void OnDown(Vector2 screenPosition)
        {
            if (!_handler.AcceptsBoardInput || ui.IsPointerOverUi(screenPosition)) return;
            if (!ui.GetBoardRectPixels().Contains(screenPosition)) return;

            var board = _handler.Board;
            var ray = boardCamera.Camera.ScreenPointToRay(screenPosition);
            int block = presenter.Pick(ray, board);
            if (block < 0 || !TryHitPlane(ray, out var hit)) return;

            _drag.Begin(board, block);
            _grabOffset = hit - presenter.BlockCenter(block, _drag.StartX, _drag.StartY);
            _dragging = true;
            _armedSoundPlayed = false;
            _handler.OnBlockPressed(block);
        }

        void OnMove(Vector2 screenPosition)
        {
            if (!TryRequestedPosition(screenPosition, out float x, out float y)) return;

            bool newlyArmed = _drag.Update(x, y);
            if (!_drag.AxisLocked) return;

            presenter.UpdateDragPreview(_drag.Block, _drag.PreviewX, _drag.PreviewY, _drag.ArmedGate, _handler.ReducedMotion);
            if (newlyArmed && !_armedSoundPlayed)
            {
                _armedSoundPlayed = true;
                _handler.OnGateArmed();
            }
        }

        void OnUp(Vector2 screenPosition)
        {
            OnMove(screenPosition);
            _dragging = false;

            if (!_drag.TryGetRelease(out int x, out int y, out int gate))
            {
                if (_drag.ForbiddenAxisAttempt) _handler.OnForbiddenAxis(_drag.Block);
                _handler.OnBlockTapped(_drag.Block);
                return;
            }

            if (gate < 0 && _drag.ExitIntentWithoutGate)
            {
                _drag.GetExitEdge(out int edgeX, out int edgeY);
                _handler.OnInvalidExit(_drag.Block, _drag.ExitSide, edgeX, edgeY, _drag.ExitCheck, _drag.RefusingGate);
            }
            _handler.OnBlockReleased(_drag.Block, x, y, gate);
        }

        /// <summary>Requested block cell position from the pointer, keeping the grab offset (GDD 06 §5).</summary>
        bool TryRequestedPosition(Vector2 screenPosition, out float x, out float y)
        {
            x = y = 0f;
            if (!TryHitPlane(boardCamera.Camera.ScreenPointToRay(screenPosition), out var hit)) return false;

            var level = _handler.Board.Level;
            var spec = level.Blocks[_drag.Block];
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
