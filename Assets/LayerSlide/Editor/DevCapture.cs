using System.IO;
using LayerSlide.UI;
using UnityEditor;
using UnityEngine;

namespace LayerSlide.Editor
{
    /// <summary>
    /// Play-mode capture of background, board and UI into Captures/&lt;name&gt;.png at half resolution. Works when the
    /// Game view is not focused (ScreenCapture then misses the overlay canvas). Local review only; Captures/ is ignored.
    /// </summary>
    public static class DevCapture
    {
        const string Folder = "Captures";

        [MenuItem("Tools/Layer Slide/Capture Game View")]
        static void CaptureMenu() => Capture("capture");

        public static string Capture(string name) => Capture(name, false);

        /// <param name="full">Keep full resolution (detail review) instead of halving.</param>
        public static string Capture(string name, bool full)
        {
            if (!Application.isPlaying) return "not playing";

            // Full resolution so the canvas keeps its scale; halved on the CPU at the end.
            int width = Screen.width, height = Screen.height;
            var background = GameObject.Find("BackgroundCamera").GetComponent<Camera>();
            var board = GameObject.Find("BoardCamera").GetComponent<Camera>();
            var canvas = Object.FindFirstObjectByType<UIController>().GetComponent<Canvas>();
            int uiLayer = 1 << LayerMask.NameToLayer("UI");

            // Pass 1: the scene (board camera when visible, else the background clear).
            var scene = Render(board.enabled ? board : background, width, height);

            // Pass 2: the canvas alone over transparent black, composited on the CPU. Rendering both into one
            // target does not work under URP: every base camera clears the color buffer it renders to.
            var mask = background.cullingMask;
            var clearFlags = background.clearFlags;
            var clearColor = background.backgroundColor;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = background;
            canvas.planeDistance = 1f;
            background.cullingMask = uiLayer;
            background.clearFlags = CameraClearFlags.SolidColor;
            background.backgroundColor = new Color(0f, 0f, 0f, 0f);
            Canvas.ForceUpdateCanvases();
            var ui = Render(background, width, height);
            background.cullingMask = mask;
            background.clearFlags = clearFlags;
            background.backgroundColor = clearColor;
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            // UI blending leaves premultiplied color over black: out = ui + scene * (1 - ui.a); 2x2 box downsample.
            int scale = full ? 1 : 2;
            int outWidth = width / scale, outHeight = height / scale;
            var output = new Color32[outWidth * outHeight];
            for (int y = 0; y < outHeight; y++)
            for (int x = 0; x < outWidth; x++)
            {
                float r = 0f, g = 0f, b = 0f;
                for (int dy = 0; dy < scale; dy++)
                for (int dx = 0; dx < scale; dx++)
                {
                    int i = (y * scale + dy) * width + x * scale + dx;
                    Color32 s = scene[i], u = ui[i];
                    float keep = 1f - u.a / 255f;
                    r += Mathf.Min(255f, u.r + s.r * keep);
                    g += Mathf.Min(255f, u.g + s.g * keep);
                    b += Mathf.Min(255f, u.b + s.b * keep);
                }
                float n = scale * scale;
                output[y * outWidth + x] = new Color32((byte)(r / n), (byte)(g / n), (byte)(b / n), 255);

            }
            width = outWidth;
            height = outHeight;

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.SetPixels32(output);
            texture.Apply();
            Directory.CreateDirectory(Folder);
            string path = Path.GetFullPath(Path.Combine(Folder, name + ".png"));
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            return path;
        }

        static Color32[] Render(Camera camera, int width, int height)
        {
            var target = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            camera.targetTexture = target;
            camera.Render();
            camera.targetTexture = null;

            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            texture.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            var pixels = texture.GetPixels32();
            Object.DestroyImmediate(texture);
            return pixels;
        }
    }
}
