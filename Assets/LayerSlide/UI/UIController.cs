using System;
using System.Collections.Generic;
using LayerSlide.Presentation;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace LayerSlide.UI
{
    /// <summary>
    /// Builds and lays out every screen on one Screen Space Overlay canvas. Canvas units are dp: the scale
    /// factor is Android's display density (screen width / 360 in the editor), see GDD 04 §6.
    /// </summary>
    [RequireComponent(typeof(Canvas))]
    public sealed class UIController : MonoBehaviour
    {
        const float ReferenceWidthDp = 360f;
        const float MinLayoutWidthDp = 320f;
        const float BoardSidePaddingDp = 16f;

        [SerializeField] VisualTheme theme;

        Canvas _canvas;
        CanvasScaler _scaler;
        GraphicRaycaster _raycaster;
        RectTransform _safeRoot;
        GameObject _backdrop;
        Rect _appliedSafeArea;
        Vector2Int _appliedScreen;

        readonly List<RaycastResult> _raycastResults = new List<RaycastResult>();
        PointerEventData _pointerData;

        public HudView Hud { get; private set; }
        public HomeView Home { get; private set; }
        public LevelSelectView LevelSelect { get; private set; }
        public SettingsView Settings { get; private set; }
        public AboutView About { get; private set; }
        public ModalView Modal { get; private set; }
        public ToastView Toast { get; private set; }
        public AppScreen CurrentScreen { get; private set; } = AppScreen.Home;

        /// <summary>Raised after the screen size or safe area changed and the layout was re-applied.</summary>
        public event Action LayoutChanged;

        public void Build(IUiHandler handler)
        {
            _canvas = GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            if (!TryGetComponent(out _scaler)) _scaler = gameObject.AddComponent<CanvasScaler>();
            _scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            if (!TryGetComponent(out _raycaster)) _raycaster = gameObject.AddComponent<GraphicRaycaster>();
            EnsureEventSystem();

            var ui = new UiFactory(theme);

            _backdrop = BuildBackdrop(ui);

            _safeRoot = UiFactory.CreateRect("SafeArea", transform);
            Hud = new HudView(ui, _safeRoot, handler);
            Home = new HomeView(ui, _safeRoot, handler);
            LevelSelect = new LevelSelectView(ui, _safeRoot, handler);
            Settings = new SettingsView(ui, _safeRoot, handler);
            About = new AboutView(ui, _safeRoot, handler);
            Toast = new ToastView(ui, _safeRoot);
            Modal = new ModalView(ui, _safeRoot);

            ApplyLayout();
            ShowScreen(AppScreen.Home);
        }

        /// <summary>Mirrors the reduced-motion setting into UI animations (pop-ins, bobbing, button bounce).</summary>
        public bool ReducedMotion
        {
            get => UiAnim.Reduced;
            set => UiAnim.Reduced = value;
        }

        /// <summary>Menu background: the sky gradient with a few soft floating shapes.</summary>
        GameObject BuildBackdrop(UiFactory ui)
        {
            var backdrop = UiFactory.Stretch(UiFactory.CreateRect("Backdrop", transform));
            var gradient = backdrop.gameObject.AddComponent<Image>();
            gradient.sprite = SpriteFactory.VerticalGradient(Palette.SkyTop, Palette.SkyBottom);
            gradient.raycastTarget = false;

            // (anchor x, anchor y, size, sprite, color, bob period, phase, spin)
            var shapes = new (float x, float y, float size, Sprite sprite, Color color, float period, float phase, float spin)[]
            {
                (0.10f, 0.88f, 220f, SpriteFactory.Circle, new Color(1f, 1f, 1f, 0.06f), 7f, 0.0f, 0f),
                (0.95f, 0.70f, 260f, SpriteFactory.Circle, new Color(1f, 1f, 1f, 0.05f), 9f, 0.4f, 0f),
                (0.05f, 0.22f, 180f, SpriteFactory.Circle, new Color(1f, 1f, 1f, 0.05f), 8f, 0.7f, 0f),
                (0.88f, 0.92f, 34f, SpriteFactory.Sparkle, new Color(1f, 1f, 1f, 0.55f), 3.1f, 0.2f, 0f),
                (0.16f, 0.62f, 22f, SpriteFactory.Sparkle, new Color(1f, 1f, 1f, 0.45f), 2.7f, 0.6f, 0f),
                (0.82f, 0.40f, 26f, SpriteFactory.Sparkle, new Color(1f, 1f, 1f, 0.40f), 3.4f, 0.1f, 0f),
                (0.24f, 0.95f, 40f, SpriteFactory.RoundedRect, WithAlpha(Palette.Red, 0.35f), 4.2f, 0.3f, 14f),
                (0.92f, 0.16f, 46f, SpriteFactory.RoundedRect, WithAlpha(Palette.Yellow, 0.35f), 4.8f, 0.8f, -10f),
                (0.08f, 0.44f, 34f, SpriteFactory.RoundedRect, WithAlpha(Palette.Blue, 0.45f), 5.3f, 0.5f, 12f)
            };
            foreach (var shape in shapes)
            {
                var image = UiFactory.Icon(backdrop, shape.sprite, shape.color, shape.size);
                image.preserveAspect = false;
                if (shape.sprite == SpriteFactory.RoundedRect)
                {
                    image.type = Image.Type.Sliced;
                    image.pixelsPerUnitMultiplier = SpriteFactory.RadiusMultiplier(shape.size * 0.28f);
                }
                var rect = image.rectTransform;
                rect.anchorMin = rect.anchorMax = new Vector2(shape.x, shape.y);
                rect.anchoredPosition = Vector2.zero;
                UiAnim.Bob(rect, shape.size * 0.06f + 4f, shape.period, shape.phase);
                if (shape.spin != 0f) UiAnim.Spin(rect, shape.spin);
            }
            return backdrop.gameObject;
        }

        static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        public void ShowScreen(AppScreen screen)
        {
            CurrentScreen = screen;
            Hud.Root.gameObject.SetActive(screen == AppScreen.Playing);
            Home.Root.gameObject.SetActive(screen == AppScreen.Home);
            LevelSelect.Root.gameObject.SetActive(screen == AppScreen.LevelSelect);
            Settings.Root.gameObject.SetActive(screen == AppScreen.Settings);
            About.Root.gameObject.SetActive(screen == AppScreen.About);
            _backdrop.SetActive(screen != AppScreen.Playing);
        }

        /// <summary>Board viewport in screen pixels: the safe area minus the HUD bands and side padding.</summary>
        public Rect GetBoardRectPixels()
        {
            var safe = Screen.safeArea;
            float scale = _scaler.scaleFactor;
            float left = safe.xMin + BoardSidePaddingDp * scale;
            float right = safe.xMax - BoardSidePaddingDp * scale;
            float bottom = safe.yMin + HudView.BottomHeight * scale;
            float top = safe.yMax - HudView.TopHeight * scale;
            return new Rect(left, bottom, Mathf.Max(1f, right - left), Mathf.Max(1f, top - bottom));
        }

        /// <summary>True when a raycast-target UI element is under the screen point (used before starting a board drag).</summary>
        public bool IsPointerOverUi(Vector2 screenPosition)
        {
            var eventSystem = EventSystem.current;
            if (eventSystem == null) return false;
            _pointerData ??= new PointerEventData(eventSystem);
            _pointerData.position = screenPosition;
            _raycastResults.Clear();
            _raycaster.Raycast(_pointerData, _raycastResults);
            return _raycastResults.Count > 0;
        }

        void Update()
        {
            if (_safeRoot == null) return;

            if (Screen.safeArea != _appliedSafeArea || Screen.width != _appliedScreen.x || Screen.height != _appliedScreen.y)
            {
                ApplyLayout();
                LayoutChanged?.Invoke();
            }

            Modal.Tick(Time.unscaledDeltaTime);
            Toast.Tick();
        }

        void ApplyLayout()
        {
            _appliedSafeArea = Screen.safeArea;
            _appliedScreen = new Vector2Int(Screen.width, Screen.height);

            // Never lay out narrower than 320 dp; very narrow windows scale the whole UI down instead.
            float scale = ReadDensity();
            if (_appliedSafeArea.width / scale < MinLayoutWidthDp)
                scale = _appliedSafeArea.width / MinLayoutWidthDp;
            _scaler.scaleFactor = Mathf.Max(0.1f, scale);

            float width = Mathf.Max(1, Screen.width), height = Mathf.Max(1, Screen.height);
            _safeRoot.anchorMin = new Vector2(_appliedSafeArea.xMin / width, _appliedSafeArea.yMin / height);
            _safeRoot.anchorMax = new Vector2(_appliedSafeArea.xMax / width, _appliedSafeArea.yMax / height);
            _safeRoot.offsetMin = _safeRoot.offsetMax = Vector2.zero;

            float safeWidthDp = _appliedSafeArea.width / _scaler.scaleFactor;
            LevelSelect.SetWidth(safeWidthDp);
            Modal.SetWidth(safeWidthDp);
        }

        static float ReadDensity()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var resources = activity.Call<AndroidJavaObject>("getResources"))
                using (var metrics = resources.Call<AndroidJavaObject>("getDisplayMetrics"))
                    return metrics.Get<float>("density");
            }
            catch (Exception)
            {
                // Fall through to the width-based estimate.
            }
#endif
            return Mathf.Max(1, Screen.width) / ReferenceWidthDp;
        }

        static void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }
    }
}
