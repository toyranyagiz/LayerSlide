using System.Collections.Generic;
using LayerSlide.Presentation;
using LayerSlide.Services;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LayerSlide.UI
{
    public enum LevelCellState
    {
        Locked,
        Open,
        Completed
    }

    /// <summary>Main menu: Continue (or "replay levels"), Levels, Settings.</summary>
    public sealed class HomeView
    {
        public readonly RectTransform Root;
        readonly TextMeshProUGUI _continueLabel;

        public HomeView(UiFactory ui, RectTransform parent, IUiHandler handler)
        {
            Root = UiFactory.Stretch(UiFactory.CreateRect("Home", parent));

            var column = UiFactory.CreateRect("Column", Root);
            UiFactory.Fixed(column, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(288f, 360f));
            UiFactory.VerticalLayout(column.gameObject, 12f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleCenter);

            var title = ui.Text("Title", column, "Layer Slide", 36f, true, Color.white);
            UiFactory.Height(title, 96f);

            var continueButton = ui.Button("Continue", column, "Devam et", Palette.Panel, Palette.Text, handler.OnContinuePressed, out _continueLabel);
            UiFactory.Height(continueButton, UiFactory.ButtonHeight);
            var levels = ui.Button("Levels", column, "Bölümler", Palette.Panel, Palette.Text, handler.OnLevelsPressed, out _);
            UiFactory.Height(levels, UiFactory.ButtonHeight);
            var settings = ui.Button("Settings", column, "Ayarlar", Palette.Panel, Palette.Text, handler.OnSettingsPressed, out _);
            UiFactory.Height(settings, UiFactory.ButtonHeight);
        }

        public void SetContinueLabel(string label) => _continueLabel.text = label;
    }

    /// <summary>5-column level grid with locked / open / completed states (GDD 04 §10).</summary>
    public sealed class LevelSelectView
    {
        public readonly RectTransform Root;

        sealed class Cell
        {
            public Button Button;
            public Image Background;
            public Image Frame;
            public TextMeshProUGUI Number;
            public Image Lock;
            public Image Check;
        }

        readonly GridLayoutGroup _grid;
        readonly List<Cell> _cells = new List<Cell>();

        public LevelSelectView(UiFactory ui, RectTransform parent, IUiHandler handler)
        {
            Root = UiFactory.Stretch(UiFactory.CreateRect("LevelSelect", parent));
            BuildHeader(ui, Root, "Bölümler", handler.OnBackPressed);

            var body = UiFactory.Stretch(UiFactory.CreateRect("Body", Root), 0f, 0f, 56f, 0f);
            var content = UiFactory.VerticalScroll(body, out _);
            UiFactory.Stretch((RectTransform)content.parent);

            _grid = content.gameObject.AddComponent<GridLayoutGroup>();
            _grid.padding = new RectOffset(16, 16, 8, 16);
            _grid.spacing = new Vector2(8f, 8f);
            _grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            _grid.constraintCount = 5;

            var ids = LevelCatalog.LevelIds;
            for (int i = 0; i < ids.Count; i++)
            {
                string id = ids[i];
                var button = ui.Button("Level" + id, content, (i + 1).ToString(), Palette.Panel, Palette.Text, () => handler.OnLevelSelected(id), out var number);
                var cell = new Cell { Button = button, Background = (Image)button.targetGraphic, Number = number };

                cell.Frame = ui.Panel("Frame", button.transform, Palette.Blue, rounded: false);
                cell.Frame.sprite = SpriteFactory.RoundedOutline;
                cell.Frame.type = Image.Type.Sliced;
                UiFactory.Stretch(cell.Frame.rectTransform);

                cell.Lock = CreateIcon(button.transform, SpriteFactory.Lock, Palette.Text, new Vector2(0.5f, 0.5f), Vector2.zero, 26f);
                cell.Check = CreateIcon(button.transform, SpriteFactory.Check, Palette.Blue, new Vector2(1f, 1f), new Vector2(-4f, -4f), 18f);
                _cells.Add(cell);
            }
        }

        public void SetWidth(float safeWidth)
        {
            float cellWidth = Mathf.Max(40f, (safeWidth - 64f) / 5f);
            _grid.cellSize = new Vector2(cellWidth, UiFactory.ButtonHeight);
        }

        public void Refresh(System.Func<string, LevelCellState> stateOf)
        {
            var ids = LevelCatalog.LevelIds;
            for (int i = 0; i < _cells.Count; i++)
            {
                var cell = _cells[i];
                var state = stateOf(ids[i]);
                bool locked = state == LevelCellState.Locked;
                cell.Button.interactable = !locked;
                cell.Background.color = locked ? Palette.Locked : Palette.Panel;
                cell.Number.gameObject.SetActive(!locked);
                cell.Lock.gameObject.SetActive(locked);
                cell.Frame.gameObject.SetActive(state == LevelCellState.Completed);
                cell.Check.gameObject.SetActive(state == LevelCellState.Completed);
            }
        }

        static Image CreateIcon(Transform parent, Sprite sprite, Color color, Vector2 anchor, Vector2 position, float size)
        {
            var image = UiFactory.CreateRect("Icon", parent).gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            UiFactory.Fixed(image.rectTransform, anchor, position, new Vector2(size, size));
            return image;
        }

        /// <summary>Shared 56 dp header with a back button and centered title.</summary>
        public static void BuildHeader(UiFactory ui, RectTransform parent, string title, UnityEngine.Events.UnityAction onBack)
        {
            var header = UiFactory.TopStrip(UiFactory.CreateRect("Header", parent), 0f, 56f, 16f);
            var back = ui.Button("Back", header, "Geri", new Color(1f, 1f, 1f, 0.12f), Color.white, onBack, out _);
            UiFactory.Fixed((RectTransform)back.transform, new Vector2(0f, 0.5f), Vector2.zero, new Vector2(80f, 48f));
            var label = ui.Text("Title", header, title, UiFactory.FontTitle, true, Color.white);
            UiFactory.Stretch(label.rectTransform, 96f, 96f);
        }
    }

    /// <summary>Volume slider and three toggles; values apply immediately (GDD 05 §7).</summary>
    public sealed class SettingsView
    {
        public readonly RectTransform Root;

        readonly Slider _volume;
        readonly TextMeshProUGUI _volumeValue;
        readonly TextMeshProUGUI _vibration, _symbols, _reducedMotion;
        bool _refreshing;

        public SettingsView(UiFactory ui, RectTransform parent, IUiHandler handler)
        {
            Root = UiFactory.Stretch(UiFactory.CreateRect("Settings", parent));
            LevelSelectView.BuildHeader(ui, Root, "Ayarlar", handler.OnBackPressed);

            var body = UiFactory.Stretch(UiFactory.CreateRect("Body", Root), 0f, 0f, 56f, 0f);
            var content = UiFactory.VerticalScroll(body, out _);
            UiFactory.Stretch((RectTransform)content.parent);
            UiFactory.VerticalLayout(content.gameObject, 12f, new RectOffset(16, 16, 16, 16));

            var volumeLabel = ui.Text("VolumeLabel", content, "Efekt sesi", UiFactory.FontBody, true, Color.white, TextAlignmentOptions.Left);
            UiFactory.Height(volumeLabel, 24f);

            var volumeRow = UiFactory.CreateRect("VolumeRow", content);
            UiFactory.Height(volumeRow, 48f);
            _volume = CreateSlider(ui, volumeRow);
            _volume.onValueChanged.AddListener(value =>
            {
                _volumeValue.text = ((int)value).ToString();
                if (!_refreshing) handler.OnVolumeChanged((int)value, false);
            });
            var trigger = _volume.gameObject.AddComponent<EventTrigger>();
            var pointerUp = new EventTrigger.Entry { eventID = EventTriggerType.PointerUp };
            pointerUp.callback.AddListener(_ => handler.OnVolumeChanged((int)_volume.value, true));
            trigger.triggers.Add(pointerUp);

            _volumeValue = ui.Text("Value", volumeRow, "70", UiFactory.FontBody, true, Color.white, TextAlignmentOptions.Right);
            UiFactory.Fixed(_volumeValue.rectTransform, new Vector2(1f, 0.5f), Vector2.zero, new Vector2(48f, 48f));

            _vibration = CreateToggle(ui, content, handler.OnVibrationToggled);
            _symbols = CreateToggle(ui, content, handler.OnSymbolsToggled);
            _reducedMotion = CreateToggle(ui, content, handler.OnReducedMotionToggled);

            var about = ui.Button("About", content, "Hakkında", new Color(1f, 1f, 1f, 0.12f), Color.white, handler.OnAboutPressed, out _);
            UiFactory.Height(about, UiFactory.ButtonHeight);
        }

        public void Refresh(SettingsData settings)
        {
            _refreshing = true;
            _volume.value = settings.sfxVolume;
            _volumeValue.text = settings.sfxVolume.ToString();
            _refreshing = false;
            _vibration.text = "Titreşim: " + OnOff(settings.vibration);
            _symbols.text = "Renk simgeleri: " + OnOff(settings.colorSymbols);
            _reducedMotion.text = "Azaltılmış hareket: " + OnOff(settings.reducedMotion);
        }

        public int CurrentVolume => (int)_volume.value;

        static string OnOff(bool value) => value ? "Açık" : "Kapalı";

        static TextMeshProUGUI CreateToggle(UiFactory ui, Transform parent, UnityEngine.Events.UnityAction onClick)
        {
            var button = ui.Button("Toggle", parent, string.Empty, Palette.Panel, Palette.Text, onClick, out var label);
            UiFactory.Height(button, UiFactory.ButtonHeight);
            return label;
        }

        static Slider CreateSlider(UiFactory ui, RectTransform parent)
        {
            var root = UiFactory.CreateRect("Slider", parent);
            UiFactory.Stretch(root, 0f, 56f);
            var slider = root.gameObject.AddComponent<Slider>();

            var track = ui.Panel("Track", root, new Color(1f, 1f, 1f, 0.25f), rounded: true);
            track.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            track.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            track.rectTransform.sizeDelta = new Vector2(0f, 8f);

            var fillArea = UiFactory.CreateRect("FillArea", root);
            fillArea.anchorMin = new Vector2(0f, 0.5f);
            fillArea.anchorMax = new Vector2(1f, 0.5f);
            fillArea.sizeDelta = new Vector2(0f, 8f);
            var fill = ui.Panel("Fill", fillArea, Palette.Blue, rounded: true);
            fill.rectTransform.sizeDelta = Vector2.zero;

            var handleArea = UiFactory.Stretch(UiFactory.CreateRect("HandleArea", root), 14f, 14f);
            var handle = ui.Panel("Handle", handleArea, Color.white, rounded: true, raycast: true);
            handle.rectTransform.sizeDelta = new Vector2(28f, 28f);

            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f;
            slider.maxValue = 100f;
            slider.wholeNumbers = true;
            return slider;
        }
    }

    /// <summary>About panel: version, author and the embedded font license.</summary>
    public sealed class AboutView
    {
        public readonly RectTransform Root;

        public AboutView(UiFactory ui, RectTransform parent, IUiHandler handler)
        {
            Root = UiFactory.Stretch(UiFactory.CreateRect("About", parent));
            LevelSelectView.BuildHeader(ui, Root, "Hakkında", handler.OnBackPressed);

            var body = UiFactory.Stretch(UiFactory.CreateRect("Body", Root), 0f, 0f, 56f, 0f);
            var content = UiFactory.VerticalScroll(body, out _);
            UiFactory.Stretch((RectTransform)content.parent);
            UiFactory.VerticalLayout(content.gameObject, 12f, new RectOffset(16, 16, 16, 24));

            var license = Resources.Load<TextAsset>("Licenses/NotoSans-OFL");
            string text = "<b>Layer Slide</b>\nSürüm " + Application.version + "\nMehmet Yağız Toyran\n\n"
                + "Yazı tipi: Noto Sans — SIL Open Font License 1.1\n\n"
                + (license != null ? license.text : string.Empty);

            var label = ui.Text("Text", content, text, UiFactory.FontSmall, false, Color.white, TextAlignmentOptions.TopLeft);
            label.richText = true;
        }
    }
}
