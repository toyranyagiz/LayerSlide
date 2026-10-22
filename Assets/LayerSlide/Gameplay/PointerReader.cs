using UnityEngine;
using UnityEngine.InputSystem;

namespace LayerSlide.Gameplay
{
    public enum PointerPhase
    {
        None,
        Down,
        Move,
        Up,
        Cancel
    }

    /// <summary>Source of the single owning pointer. Tests can substitute a scripted source.</summary>
    public interface IPointerSource
    {
        bool HasOwner { get; }
        PointerPhase Read(out Vector2 position);
        void Release();
    }

    /// <summary>
    /// Polls touch and mouse and tracks a single owning pointer (GDD 01 §3): the first finger that goes down owns
    /// the hold; other fingers are ignored and never take over when the owner lifts. Presses are detected by
    /// comparing with the previous poll instead of per-update edge flags, so a press is never lost when the input
    /// update and the frame do not line up. Allocation-free.
    /// </summary>
    public sealed class PointerReader : IPointerSource
    {
        const int MouseId = int.MinValue;
        const int NoOwner = -1;
        const int MaxTouches = 16;

        readonly int[] _previousTouches = new int[MaxTouches];
        readonly int[] _currentTouches = new int[MaxTouches];
        int _previousCount;
        int _currentCount;
        bool _mouseWasPressed;
        int _ownerId = NoOwner;
        int _ownerSlot = -1; // touch ids are reused, and a stale Ended slot may carry the same id as the owner
        Vector2 _lastOwnerPosition;

        public bool HasOwner => _ownerId != NoOwner;

        /// <summary>Drops the current owner; that pointer must lift and press again to be read.</summary>
        public void Release()
        {
            _ownerId = NoOwner;
            _ownerSlot = -1;
        }

        public PointerPhase Read(out Vector2 position)
        {
            var touchscreen = Touchscreen.current;
            var mouse = Mouse.current;
            _currentCount = 0;

            PointerPhase result = PointerPhase.None;
            position = _lastOwnerPosition;
            bool ownerSeen = false;

            if (touchscreen != null)
            {
                var touches = touchscreen.touches;
                for (int i = 0; i < touches.Count; i++)
                {
                    var touch = touches[i];
                    int id = touch.touchId.ReadValue();
                    var phase = touch.phase.ReadValue();
                    bool pressed = touch.press.isPressed && phase != UnityEngine.InputSystem.TouchPhase.Canceled;

                    if (i == _ownerSlot && id == _ownerId)
                    {
                        ownerSeen = true;
                        position = _lastOwnerPosition = touch.position.ReadValue();
                        if (phase == UnityEngine.InputSystem.TouchPhase.Canceled) result = CancelOwner();
                        else if (!pressed) result = ReleaseOwner();
                        else result = PointerPhase.Move;
                    }

                    if (!pressed || _currentCount >= MaxTouches) continue;
                    _currentTouches[_currentCount++] = id;

                    // A finger that was not down at the previous poll is a new press.
                    if (result == PointerPhase.None && !HasOwner && !WasDown(id))
                    {
                        _ownerId = id;
                        _ownerSlot = i;
                        position = _lastOwnerPosition = touch.position.ReadValue();
                        result = PointerPhase.Down;
                        ownerSeen = true;
                    }
                }
            }

            if (_ownerId == MouseId)
            {
                ownerSeen = mouse != null;
                if (mouse != null)
                {
                    position = _lastOwnerPosition = mouse.position.ReadValue();
                    result = mouse.leftButton.isPressed ? PointerPhase.Move : ReleaseOwner();
                }
            }
            else if (result == PointerPhase.None && !HasOwner && mouse != null && mouse.leftButton.isPressed && !_mouseWasPressed)
            {
                _ownerId = MouseId;
                _ownerSlot = -1;
                position = _lastOwnerPosition = mouse.position.ReadValue();
                result = PointerPhase.Down;
                ownerSeen = true;
            }
            _mouseWasPressed = mouse != null && mouse.leftButton.isPressed;

            // The owner vanished without an up: pointer loss, never a release (GDD 01 §11).
            if (HasOwner && !ownerSeen && result == PointerPhase.None)
                result = CancelOwner();

            System.Array.Copy(_currentTouches, _previousTouches, _currentCount);
            _previousCount = _currentCount;
            return result;
        }

        bool WasDown(int id)
        {
            for (int i = 0; i < _previousCount; i++)
                if (_previousTouches[i] == id)
                    return true;
            return false;
        }

        PointerPhase ReleaseOwner()
        {
            _ownerId = NoOwner;
            _ownerSlot = -1;
            return PointerPhase.Up;
        }

        PointerPhase CancelOwner()
        {
            _ownerId = NoOwner;
            _ownerSlot = -1;
            return PointerPhase.Cancel;
        }
    }
}
