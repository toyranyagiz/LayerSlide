using UnityEngine;

namespace LayerSlide.Presentation
{
    /// <summary>
    /// Fixed 65 degree orthographic board camera (GDD 04 §3). It renders the whole screen so the sky gradient
    /// behind the board continues under the HUD; the board itself is scaled to fit the BoardRect plus a 1-unit
    /// margin and shifted (orthographic, so a plain camera offset) to the BoardRect's center.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class CameraFramer : MonoBehaviour
    {
        const float Pitch = 65f;
        const float Distance = 10f;
        const float Margin = 0.62f; // just past the tray base (0.54 per side)
        const float MaxVisualHeight = 0.40f;
        const float FarClip = 30f;

        [Tooltip("Unlit sky gradient drawn on a quad at the far plane.")]
        [SerializeField] Material background;

        Camera _camera;
        Vector3 _basePosition;
        Transform _backdrop;

        public Camera Camera => _camera != null ? _camera : _camera = GetComponent<Camera>();

        void Awake()
        {
            var cam = Camera;
            cam.orthographic = true;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = FarClip;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Palette.Environment;
            float pitch = Pitch * Mathf.Deg2Rad;
            _basePosition = new Vector3(0f, Distance * Mathf.Sin(pitch), -Distance * Mathf.Cos(pitch));
            transform.SetPositionAndRotation(_basePosition, Quaternion.Euler(Pitch, 0f, 0f));

            if (background != null)
            {
                var quad = new GameObject("SkyBackdrop");
                quad.transform.SetParent(transform, false);
                quad.transform.localPosition = new Vector3(0f, 0f, FarClip - 1f);
                quad.AddComponent<MeshFilter>().sharedMesh = BuildBackdropMesh();
                var renderer = quad.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = background;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                _backdrop = quad.transform;
            }
        }

        /// <param name="boardRect">BoardRect in screen pixels (origin bottom-left).</param>
        public void Frame(int boardWidth, int boardHeight, Rect boardRect)
        {
            var cam = Camera;
            float screenWidth = Mathf.Max(1, Screen.width), screenHeight = Mathf.Max(1, Screen.height);
            cam.rect = new Rect(0f, 0f, 1f, 1f);

            float pitch = Pitch * Mathf.Deg2Rad;
            float halfWidth = (boardWidth + 2f * Margin) * 0.5f;
            float halfHeight = (boardHeight + 2f * Margin) * Mathf.Sin(pitch) * 0.5f + MaxVisualHeight * Mathf.Cos(pitch);

            // Pixels per world unit so the framed area fits the BoardRect; then the full-screen size that gives it.
            float pixelsPerUnit = Mathf.Max(0.001f, Mathf.Min(boardRect.height / (2f * halfHeight), boardRect.width / (2f * halfWidth)));
            cam.orthographicSize = screenHeight / (2f * pixelsPerUnit);

            // Shift so the board's center lands on the BoardRect's center.
            var offset = (new Vector2(screenWidth, screenHeight) * 0.5f - boardRect.center) / pixelsPerUnit;
            transform.position = _basePosition + transform.right * offset.x + transform.up * offset.y;

            if (_backdrop != null)
            {
                float height = cam.orthographicSize * 2f * 1.02f;
                _backdrop.localScale = new Vector3(height * screenWidth / screenHeight, height, 1f);
            }
        }

        /// <summary>Unit quad in the camera's X-Y plane facing the camera, UV v = 0 at the bottom.</summary>
        static Mesh BuildBackdropMesh()
        {
            var mesh = new Mesh { name = "SkyBackdrop" };
            mesh.SetVertices(new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(0.5f, -0.5f, 0f) });
            mesh.SetUVs(0, new[] { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f) });
            mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
