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

    /// <summary>
    /// Polls touch and mouse and tracks a single owning pointer (GDD 01 §3): the first finger that goes down
    /// owns the hold; other fingers are ignored and never take over when the owner lifts. Allocation-free.
    /// </summary>
    public sealed class PointerReader
    {
        const int MouseId = -2;
        const int NoOwner = -1;

        int _ownerId = NoOwner;

        public bool HasOwner => _ownerId != NoOwner;

        /// <summary>Drops the current owner; it must lift and press again to be read.</summary>
        public void Release() => _ownerId = NoOwner;

        public PointerPhase Read(out Vector2 position)
        {
            position = default;
            return HasOwner ? ReadOwner(out position) : ReadNewPress(out position);
        }

        PointerPhase ReadNewPress(out Vector2 position)
        {
            position = default;
            var touchscreen = Touchscreen.current;
            if (touchscreen != null)
            {
                var touches = touchscreen.touches;
                for (int i = 0; i < touches.Count; i++)
                {
                    var touch = touches[i];
                    if (!touch.press.wasPressedThisFrame) continue;
                    _ownerId = touch.touchId.ReadValue();
                    position = touch.position.ReadValue();
                    return PointerPhase.Down;
                }
            }

            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                _ownerId = MouseId;
                position = mouse.position.ReadValue();
                return PointerPhase.Down;
            }
            return PointerPhase.None;
        }

        PointerPhase ReadOwner(out Vector2 position)
        {
            position = default;
            if (_ownerId == MouseId)
            {
                var mouse = Mouse.current;
                if (mouse == null) return CancelOwner();
                position = mouse.position.ReadValue();
                if (mouse.leftButton.wasReleasedThisFrame || !mouse.leftButton.isPressed)
                {
                    _ownerId = NoOwner;
                    return PointerPhase.Up;
                }
                return PointerPhase.Move;
            }

            var touchscreen = Touchscreen.current;
            if (touchscreen == null) return CancelOwner();

            var touches = touchscreen.touches;
            for (int i = 0; i < touches.Count; i++)
            {
                var touch = touches[i];
                if (touch.touchId.ReadValue() != _ownerId) continue;

                position = touch.position.ReadValue();
                var phase = touch.phase.ReadValue();
                if (phase == UnityEngine.InputSystem.TouchPhase.Canceled) return CancelOwner();
                if (touch.press.wasReleasedThisFrame || phase == UnityEngine.InputSystem.TouchPhase.Ended)
                {
                    _ownerId = NoOwner;
                    return PointerPhase.Up;
                }
                return PointerPhase.Move;
            }

            // The owning touch disappeared without an up event: treat as pointer loss, never as a release.
            return CancelOwner();
        }

        PointerPhase CancelOwner()
        {
            _ownerId = NoOwner;
            return PointerPhase.Cancel;
        }
    }
}
