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

    /// <summary>Main menu: stacked-block logo, a big Continue (or "replay levels") button, Levels and Settings.</summary>
    public sealed class HomeView
    {
        public readonly RectTransform Root;
        readonly TextMeshProUGUI _continueLabel;

        public HomeView(UiFactory ui, RectTransform parent, IUiHandler handler)
        {
            Root = UiFactory.Stretch(UiFactory.CreateRect("Home", parent));

            // Logo: three bobbing layer slabs over the title.
            var logo = UiFactory.CreateRect("Logo", Root);
            UiFactory.Fixed(logo, new Vector2(0.5f, 0.62f), Vector2.zero, new Vector2(300f, 300f));
            BuildLogoStack(ui, logo);

            var title = ui.Text("Title", logo, "Layer\nSlide", UiFactory.FontHero, TextStyle.Title, Color.white);
            title.lineSpacing = -58f;
            UiFactory.Fixed(title.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, -10f), new Vector2(300f, 130f));
            title.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 3f);

            var column = UiFactory.CreateRect("Column", Root);
            UiFactory.Fixed(column, new Vector2(0.5f, 0f), new Vector2(0f, 56f), new Vector2(296f, 168f));

            var play = ui.Button("Continue", column, "Devam et", UiFactory.Green, handler.OnContinuePressed, SpriteFactory.Play,
                ButtonLayout.IconLeft, 26f, UiFactory.FontTitle, 30f);
            _continueLabel = play.Label;
            var playRect = (RectTransform)play.transform;
            UiFactory.TopStrip(playRect, 0f, 80f);
            UiAnim.Pulse(play.Content, 0.04f, 1.6f);

            var row = UiFactory.BottomStrip(UiFactory.CreateRect("Row", column), 0f, UiFactory.ButtonHeight);
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 12f;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = true;
            ui.Button("Levels", row, "Bölümler", UiFactory.Sky, handler.OnLevelsPressed, SpriteFactory.Levels, ButtonLayout.IconLeft,
                20f, UiFactory.FontBody + 1f, 22f);
            ui.Button("Settings", row, "Ayarlar", UiFactory.Pink, handler.OnSettingsPressed, SpriteFactory.Gear, ButtonLayout.IconLeft,
                20f, UiFactory.FontBody + 1f, 22f);
        }

        static void BuildLogoStack(UiFactory ui, RectTransform logo)
        {
            var colors = new[] { (Palette.Blue, 0f), (Palette.Yellow, 0.33f), (Palette.Red, 0.66f) };
            for (int i = 0; i < colors.Length; i++)
            {
                var (color, phase) = colors[i];
                var slab = UiFactory.CreateRect("Slab" + i, logo);
                float width = 196f - i * 30f;
                UiFactory.Fixed(slab, new Vector2(0.5f, 0.5f), new Vector2(0f, 34f + i * 44f), new Vector2(width, 64f));
                slab.pivot = new Vector2(0.5f, 0.5f);
                slab.anchoredPosition = new Vector2(i == 1 ? 8f : 0f, 34f + i * 44f);
                slab.localRotation = Quaternion.Euler(0f, 0f, i == 1 ? -4f : (i == 2 ? 3f : 0f));

                UiFactory.DropShadow(slab, 8f, 6f, 0.9f);
                var side = ui.Panel("Side", slab, Palette.Darken(color, 0.22f), 20f);
                UiFactory.Stretch(side.rectTransform);
                var top = ui.Panel("Top", slab, color, 20f);
                UiFactory.Stretch(top.rectTransform, 0f, 0f, 0f, 12f);
                var shine = ui.Panel("Shine", top.transform, new Color(1f, 1f, 1f, 0.28f), 12f);
                UiFactory.Stretch(shine.rectTransform, 12f, 12f, 6f, 26f);
                var dot = UiFactory.Icon(top.transform, SpriteFactory.Circle, new Color(1f, 1f, 1f, 0.9f), 18f);
                dot.rectTransform.anchoredPosition = new Vector2(0f, -2f);

                UiAnim.Bob(slab, 5f, 2.4f, phase);
            }
        }

        public void SetContinueLabel(string label) => _continueLabel.text = label;
    }

    /// <summary>5-column grid of chunky level tiles with locked / open / completed states (GDD 04 §10).</summary>
    public sealed class LevelSelectView
    {
        public readonly RectTransform Root;

        sealed class Cell
        {
            public ChunkyButton Button;
            public RectTransform Badge;
            public Image Lock;
        }

        readonly GridLayoutGroup _grid;
        readonly List<Cell> _cells = new List<Cell>();

        public LevelSelectView(UiFactory ui, RectTransform parent, IUiHandler handler)
        {
            Root = UiFactory.Stretch(UiFactory.CreateRect("LevelSelect", parent));
            BuildHeader(ui, Root, "Bölümler", handler.OnBackPressed);

            var body = UiFactory.Stretch(UiFactory.CreateRect("Body", Root), 0f, 0f, HeaderHeight, 0f);
            var content = UiFactory.VerticalScroll(body, out _);
            UiFactory.Stretch((RectTransform)content.parent);

            _grid = content.gameObject.AddComponent<GridLayoutGroup>();
            _grid.padding = new RectOffset(18, 18, 14, 24);
            _grid.spacing = new Vector2(12f, 14f);
            _grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            _grid.constraintCount = 5;
            _grid.childAlignment = TextAnchor.UpperCenter;

            var ids = LevelCatalog.LevelIds;
            for (int i = 0; i < ids.Count; i++)
            {
                string id = ids[i];
                var button = ui.Button("Level" + id, content, (i + 1).ToString(), UiFactory.Orange, () => handler.OnLevelSelected(id),
                    null, ButtonLayout.LabelOnly, 18f, UiFactory.FontTitle);
                var cell = new Cell { Button = button };

                cell.Lock = UiFactory.Icon(button.Content, SpriteFactory.Lock, Color.white, 30f);

                // Completed badge: white disc with a green check, poking out of the top-right corner.
                var badge = UiFactory.CreateRect("Badge", button.transform);
                UiFactory.Fixed(badge, new Vector2(1f, 1f), new Vector2(8f, 8f), new Vector2(26f, 26f));
                var disc = UiFactory.Icon(badge, SpriteFactory.Circle, Color.white, 26f);
                UiFactory.Stretch(disc.rectTransform);
                UiFactory.Icon(badge, SpriteFactory.Check, Palette.GreenLip, 16f);
                cell.Badge = badge;
                _cells.Add(cell);
            }
        }

        public void SetWidth(float safeWidth)
        {
            float cellWidth = Mathf.Max(40f, (safeWidth - 36f - 48f) / 5f);
            _grid.cellSize = new Vector2(cellWidth, cellWidth + UiFactory.LipHeight);
        }

        public void Refresh(System.Func<string, LevelCellState> stateOf)
        {
            var ids = LevelCatalog.LevelIds;
            for (int i = 0; i < _cells.Count; i++)
            {
                var cell = _cells[i];
                var state = stateOf(ids[i]);
                bool locked = state == LevelCellState.Locked;
                cell.Button.Button.interactable = !locked;
                cell.Button.SetColors(locked ? UiFactory.Locked : state == LevelCellState.Completed ? UiFactory.Green : UiFactory.Orange);
                cell.Button.Label.gameObject.SetActive(!locked);
                cell.Lock.gameObject.SetActive(locked);
                cell.Badge.gameObject.SetActive(state == LevelCellState.Completed);
            }
        }

        public const float HeaderHeight = 72f;

        /// <summary>Shared header: round back button and an outlined title.</summary>
        public static void BuildHeader(UiFactory ui, RectTransform parent, string title, UnityEngine.Events.UnityAction onBack)
        {
            var header = UiFactory.TopStrip(UiFactory.CreateRect("Header", parent), 10f, 56f, 14f);
            var back = ui.IconButton("Back", header, SpriteFactory.Back, UiFactory.White, onBack, 52f, 24f, 18f);
            UiFactory.Fixed((RectTransform)back.transform, new Vector2(0f, 0.5f), Vector2.zero, new Vector2(52f, 52f));
            var label = ui.Text("Title", header, title, UiFactory.FontTitle, TextStyle.Title, Color.white);
            UiFactory.Stretch(label.rectTransform, 64f, 64f, -2f, 2f);
        }
    }

    /// <summary>Volume slider and three switches on a white card; values apply immediately (GDD 05 §7).</summary>
    public sealed class SettingsView
    {
        public readonly RectTransform Root;

        sealed class Switch
        {
            public Image Track;
            public RectTransform Knob;
            public TextMeshProUGUI State;
        }

        readonly Slider _volume;
        readonly TextMeshProUGUI _volumeValue;
        readonly Switch _vibration, _symbols, _reducedMotion;
        bool _refreshing;

        public SettingsView(UiFactory ui, RectTransform parent, IUiHandler handler)
        {
            Root = UiFactory.Stretch(UiFactory.CreateRect("Settings", parent));
            LevelSelectView.BuildHeader(ui, Root, "Ayarlar", handler.OnBackPressed);

            var body = UiFactory.Stretch(UiFactory.CreateRect("Body", Root), 0f, 0f, LevelSelectView.HeaderHeight, 0f);
            var content = UiFactory.VerticalScroll(body, out _);
            UiFactory.Stretch((RectTransform)content.parent);
            UiFactory.VerticalLayout(content.gameObject, 18f, new RectOffset(18, 18, 14, 24));

            var card = ui.Card("Card", content);
            UiFactory.VerticalLayout(card.gameObject, 0f, new RectOffset(20, 20, 10, 6));

            var volumeLabel = ui.Text("VolumeLabel", card, "Efekt sesi", UiFactory.FontBody, TextStyle.Display, Palette.Text, TextAlignmentOptions.Left);
            UiFactory.Height(volumeLabel, 34f);

            var volumeRow = UiFactory.CreateRect("VolumeRow", card);
            UiFactory.Height(volumeRow, 42f);
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

            _volumeValue = ui.Text("Value", volumeRow, "70", UiFactory.FontBody, TextStyle.Display, Palette.Text, TextAlignmentOptions.Right);
            UiFactory.Fixed(_volumeValue.rectTransform, new Vector2(1f, 0.5f), Vector2.zero, new Vector2(48f, 42f));

            _vibration = CreateSwitchRow(ui, card, "Titreşim", handler.OnVibrationToggled);
            _symbols = CreateSwitchRow(ui, card, "Renk simgeleri", handler.OnSymbolsToggled);
            _reducedMotion = CreateSwitchRow(ui, card, "Azaltılmış hareket", handler.OnReducedMotionToggled);

            var about = ui.Button("About", content, "Hakkında", UiFactory.White, handler.OnAboutPressed);
            UiFactory.Height(about, UiFactory.ButtonHeight);
        }

        public void Refresh(SettingsData settings)
        {
            _refreshing = true;
            _volume.value = settings.sfxVolume;
            _volumeValue.text = settings.sfxVolume.ToString();
            _refreshing = false;
            SetSwitch(_vibration, settings.vibration);
            SetSwitch(_symbols, settings.colorSymbols);
            SetSwitch(_reducedMotion, settings.reducedMotion);
        }

        public int CurrentVolume => (int)_volume.value;

        static void SetSwitch(Switch control, bool on)
        {
            control.Track.color = on ? Palette.Green : Palette.Locked;
            control.Knob.anchorMin = control.Knob.anchorMax = control.Knob.pivot = new Vector2(on ? 1f : 0f, 0.5f);
            control.Knob.anchoredPosition = new Vector2(on ? -4f : 4f, 0f);
            control.State.text = on ? "Açık" : "Kapalı";
        }

        /// <summary>Whole row is one tap target; the switch on the right shows the state.</summary>
        static Switch CreateSwitchRow(UiFactory ui, Transform parent, string label, UnityEngine.Events.UnityAction onClick)
        {
            var row = ui.Panel("Toggle", parent, new Color(1f, 1f, 1f, 0f), 0f, raycast: true);
            UiFactory.Height(row, 68f);
            var button = row.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(onClick);

            var divider = ui.Panel("Divider", row.transform, Palette.PanelInset, 1.5f);
            UiFactory.TopStrip(divider.rectTransform, 0f, 3f);

            var text = ui.Text("Label", row.transform, label, UiFactory.FontBody, TextStyle.Display, Palette.Text, TextAlignmentOptions.Left);
            UiFactory.Stretch(text.rectTransform, 0f, 120f);

            var state = ui.Text("State", row.transform, string.Empty, UiFactory.FontSmall, TextStyle.Body, Palette.TextMuted, TextAlignmentOptions.Right);
            UiFactory.Fixed(state.rectTransform, new Vector2(1f, 0.5f), new Vector2(-72f, 0f), new Vector2(60f, 30f));

            var track = ui.Panel("Track", row.transform, Palette.Locked, 17f);
            UiFactory.Fixed(track.rectTransform, new Vector2(1f, 0.5f), Vector2.zero, new Vector2(62f, 34f));
            var knob = UiFactory.CreateRect("Knob", track.transform);
            knob.sizeDelta = new Vector2(26f, 26f);
            UiFactory.DropShadow(knob, 3f, 2f, 0.7f);
            var disc = UiFactory.Icon(knob, SpriteFactory.Circle, Color.white, 26f);
            UiFactory.Stretch(disc.rectTransform);

            return new Switch { Track = track, Knob = knob, State = state };
        }

        static Slider CreateSlider(UiFactory ui, RectTransform parent)
        {
            var root = UiFactory.CreateRect("Slider", parent);
            UiFactory.Stretch(root, 0f, 60f);
            var slider = root.gameObject.AddComponent<Slider>();

            var track = ui.Panel("Track", root, Palette.PanelInset, 7f);
            track.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            track.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            track.rectTransform.sizeDelta = new Vector2(0f, 14f);

            var fillArea = UiFactory.CreateRect("FillArea", root);
            fillArea.anchorMin = new Vector2(0f, 0.5f);
            fillArea.anchorMax = new Vector2(1f, 0.5f);
            fillArea.sizeDelta = new Vector2(0f, 14f);
            var fill = ui.Panel("Fill", fillArea, Palette.Sky, 7f);
            fill.rectTransform.sizeDelta = Vector2.zero;

            // The slider stretches the handle to the handle area's height, so the area is exactly the knob height.
            var handleArea = UiFactory.Stretch(UiFactory.CreateRect("HandleArea", root), 16f, 16f, 5f, 5f);
            var handle = UiFactory.CreateRect("Handle", handleArea);
            handle.sizeDelta = new Vector2(32f, 0f);
            UiFactory.DropShadow(handle, 4f, 3f, 0.8f);
            var ring = ui.Panel("Ring", handle, Palette.SkyLip, 16f, raycast: true);
            UiFactory.Stretch(ring.rectTransform);
            var face = ui.Panel("Face", handle, Color.white, 12f);
            UiFactory.Stretch(face.rectTransform, 4f, 4f, 4f, 4f);

            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle;
            slider.targetGraphic = ring;
            slider.transition = Selectable.Transition.None;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f;
            slider.maxValue = 100f;
            slider.wholeNumbers = true;
            return slider;
        }
    }

    /// <summary>About panel: version, author and the embedded font licenses.</summary>
    public sealed class AboutView
    {
        public readonly RectTransform Root;

        public AboutView(UiFactory ui, RectTransform parent, IUiHandler handler)
        {
            Root = UiFactory.Stretch(UiFactory.CreateRect("About", parent));
            LevelSelectView.BuildHeader(ui, Root, "Hakkında", handler.OnBackPressed);

            var body = UiFactory.Stretch(UiFactory.CreateRect("Body", Root), 0f, 0f, LevelSelectView.HeaderHeight, 0f);
            var content = UiFactory.VerticalScroll(body, out _);
            UiFactory.Stretch((RectTransform)content.parent);
            UiFactory.VerticalLayout(content.gameObject, 12f, new RectOffset(18, 18, 14, 28));

            var card = ui.Card("Card", content);
            UiFactory.VerticalLayout(card.gameObject, 8f, new RectOffset(20, 20, 18, 22));

            var heading = ui.Text("Heading", card, "Layer Slide", UiFactory.FontHeader, TextStyle.Display, Palette.Text, TextAlignmentOptions.TopLeft);
            heading.lineSpacing = 0f;
            var meta = ui.Text("Meta", card, "Sürüm " + Application.version + "\nMehmet Yağız Toyran", UiFactory.FontBody, TextStyle.Body,
                Palette.TextMuted, TextAlignmentOptions.TopLeft);
            meta.lineSpacing = 0f;

            var baloo = Resources.Load<TextAsset>("Licenses/Baloo2-OFL");
            var noto = Resources.Load<TextAsset>("Licenses/NotoSans-OFL");
            string text = "Yazı tipleri: Baloo 2 ve Noto Sans — SIL Open Font License 1.1\n\n"
                + (baloo != null ? baloo.text + "\n\n" : string.Empty)
                + (noto != null ? noto.text : string.Empty);

            var label = ui.Text("Text", card, text, UiFactory.FontSmall - 2f, TextStyle.Body, Palette.Text, TextAlignmentOptions.TopLeft);
            label.lineSpacing = 0f;
            label.richText = false;
        }
    }
}
