using UnityEngine;

namespace LayerSlide.Presentation
{
    /// <summary>
    /// Fixed 65 degree orthographic board camera. Its viewport equals the BoardRect and the size is chosen
    /// so the board plus a 1-unit margin always fits (GDD 04 §3).
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class CameraFramer : MonoBehaviour
    {
        const float Pitch = 65f;
        const float Distance = 10f;
        const float Margin = 1f;
        const float MaxVisualHeight = 0.40f;

        Camera _camera;

        public Camera Camera => _camera != null ? _camera : _camera = GetComponent<Camera>();

        void Awake()
        {
            var cam = Camera;
            cam.orthographic = true;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 30f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Palette.Environment;
            float pitch = Pitch * Mathf.Deg2Rad;
            transform.SetPositionAndRotation(
                new Vector3(0f, Distance * Mathf.Sin(pitch), -Distance * Mathf.Cos(pitch)),
                Quaternion.Euler(Pitch, 0f, 0f));
        }

        /// <param name="boardRect">BoardRect in screen pixels (origin bottom-left).</param>
        public void Frame(int boardWidth, int boardHeight, Rect boardRect)
        {
            var cam = Camera;
            float screenWidth = Mathf.Max(1, Screen.width), screenHeight = Mathf.Max(1, Screen.height);
            cam.rect = new Rect(boardRect.x / screenWidth, boardRect.y / screenHeight, boardRect.width / screenWidth, boardRect.height / screenHeight);

            float pitch = Pitch * Mathf.Deg2Rad;
            float halfWidth = (boardWidth + 2f * Margin) * 0.5f;
            float halfHeight = (boardHeight + 2f * Margin) * Mathf.Sin(pitch) * 0.5f + MaxVisualHeight * Mathf.Cos(pitch);
            float aspect = boardRect.height > 0f ? boardRect.width / boardRect.height : 1f;
            cam.orthographicSize = Mathf.Max(halfHeight, halfWidth / aspect);
        }
    }
}
