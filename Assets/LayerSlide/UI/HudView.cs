using System.Collections.Generic;
using LayerSlide.Presentation;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LayerSlide.UI
{
    /// <summary>One entry of the HUD mechanic row: an icon plus a short label such as "K1" or "B: M→S".</summary>
    public struct MechanicChip
    {
        public Sprite Icon;
        public Color IconColor;
        public string Label;
        public Color LabelColor;
    }

    /// <summary>
    /// In-game HUD (GDD 04 §6): a top area (pause / title / sound row, layer chip, message row) and a bottom
    /// bar with Undo, Restart and Hint as chunky icon buttons.
    /// </summary>
    public sealed class HudView
    {
        public const float TopHeight = 184f;
        const int MaxMechanicChips = 5;
        public const float BottomHeight = 112f;
        const float BottomButtonHeight = 76f;

        public readonly RectTransform Root;

        readonly TextMeshProUGUI _title;
        readonly RectTransform _layerChip;
        readonly TextMeshProUGUI _layerInfo;
        readonly TextMeshProUGUI _message;
        readonly RectTransform _mechanicRow;
        readonly Image[] _mechanicIcons = new Image[MaxMechanicChips];
        readonly TextMeshProUGUI[] _mechanicLabels = new TextMeshProUGUI[MaxMechanicChips];
        readonly ChunkyButton _sound;
        readonly ChunkyButton _undo, _restart, _hint;

        public HudView(UiFactory ui, RectTransform parent, IUiHandler handler)
        {
            Root = UiFactory.Stretch(UiFactory.CreateRect("Hud", parent));

            var topRow = UiFactory.TopStrip(UiFactory.CreateRect("TopRow", Root), 10f, 56f, 14f);
            var menu = ui.IconButton("Menu", topRow, SpriteFactory.Pause, UiFactory.White, handler.OnMenuPressed, 52f, 24f, 18f);
            UiFactory.Fixed((RectTransform)menu.transform, new Vector2(0f, 0.5f), Vector2.zero, new Vector2(52f, 52f));
            _sound = ui.IconButton("Sound", topRow, SpriteFactory.Speaker, UiFactory.White, handler.OnMutePressed, 52f, 30f, 18f);
            UiFactory.Fixed((RectTransform)_sound.transform, new Vector2(1f, 0.5f), Vector2.zero, new Vector2(52f, 52f));

            _title = ui.Text("Title", topRow, string.Empty, UiFactory.FontTitle, TextStyle.Title, Color.white);
            UiFactory.Stretch(_title.rectTransform, 64f, 64f, -2f, 2f);
            _title.textWrappingMode = TextWrappingModes.NoWrap;

            // Layer chip: translucent pill that hugs its text.
            var chipRow = UiFactory.TopStrip(UiFactory.CreateRect("LayerRow", Root), 72f, 30f, 16f);
            var chip = ui.Panel("LayerChip", chipRow, new Color(1f, 1f, 1f, 0.18f), 15f);
            _layerChip = chip.rectTransform;
            _layerChip.anchorMin = _layerChip.anchorMax = _layerChip.pivot = new Vector2(0.5f, 0.5f);
            var chipLayout = chip.gameObject.AddComponent<HorizontalLayoutGroup>();
            chipLayout.padding = new RectOffset(14, 14, 0, 0);
            chipLayout.childControlWidth = chipLayout.childControlHeight = true;
            chipLayout.childForceExpandWidth = chipLayout.childForceExpandHeight = false;
            chipLayout.childAlignment = TextAnchor.MiddleCenter;
            var fitter = chip.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
            _layerChip.sizeDelta = new Vector2(0f, 30f);
            _layerInfo = ui.Text("LayerInfo", chip.transform, string.Empty, UiFactory.FontSmall, TextStyle.Display, Color.white);
            _layerInfo.textWrappingMode = TextWrappingModes.NoWrap;
            _layerInfo.margin = new Vector4(0f, 3f, 0f, 0f);

            // Mechanic row (GDD 04 §13): K1/K2, P1/P2 and the phase gate, left to right; hidden items take no space.
            _mechanicRow = UiFactory.TopStrip(UiFactory.CreateRect("MechanicRow", Root), 106f, 24f, 16f);
            var mechanicLayout = _mechanicRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            mechanicLayout.spacing = 12f;
            mechanicLayout.childAlignment = TextAnchor.MiddleCenter;
            mechanicLayout.childControlWidth = mechanicLayout.childControlHeight = true;
            mechanicLayout.childForceExpandWidth = mechanicLayout.childForceExpandHeight = false;
            for (int i = 0; i < MaxMechanicChips; i++)
            {
                var chipRect = UiFactory.CreateRect("Mechanic" + i, _mechanicRow);
                var chipLayoutGroup = chipRect.gameObject.AddComponent<HorizontalLayoutGroup>();
                chipLayoutGroup.spacing = 4f;
                chipLayoutGroup.childAlignment = TextAnchor.MiddleCenter;
                chipLayoutGroup.childControlWidth = chipLayoutGroup.childControlHeight = true;
                chipLayoutGroup.childForceExpandWidth = chipLayoutGroup.childForceExpandHeight = false;

                _mechanicIcons[i] = UiFactory.Icon(chipRect, SpriteFactory.Key, Color.white, 16f);
                var iconSize = _mechanicIcons[i].gameObject.AddComponent<LayoutElement>();
                iconSize.preferredWidth = iconSize.preferredHeight = 16f;
                _mechanicLabels[i] = ui.Text("Label", chipRect, string.Empty, UiFactory.FontSmall, TextStyle.Display, Color.white);
                _mechanicLabels[i].textWrappingMode = TextWrappingModes.NoWrap;
                _mechanicLabels[i].margin = new Vector4(0f, 3f, 0f, 0f);
                chipRect.gameObject.SetActive(false);
            }
            _mechanicRow.gameObject.SetActive(false);

            _message = ui.Text("Message", Root, string.Empty, 16f, TextStyle.Body, Color.white);
            UiFactory.TopStrip(_message.rectTransform, 132f, 48f, 12f);
            _message.lineSpacing = -28f; // Baloo has tall default lines; two lines must fit the row (GDD 04 §6)
            _message.alignment = TextAlignmentOptions.Top;
            _message.overflowMode = TextOverflowModes.Truncate; // never spills over the board

            var bottomBar = UiFactory.BottomStrip(UiFactory.CreateRect("BottomBar", Root), 18f, BottomButtonHeight, 16f);
            var row = bottomBar.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 12f;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = true;

            _undo = ui.Button("Undo", bottomBar, "Geri al", UiFactory.Sky, handler.OnUndoPressed, SpriteFactory.Undo, ButtonLayout.IconTop,
                22f, UiFactory.FontSmall + 1f, 30f);
            _restart = ui.Button("Restart", bottomBar, "Baştan", UiFactory.Sky, handler.OnRestartPressed, SpriteFactory.Restart, ButtonLayout.IconTop,
                22f, UiFactory.FontSmall + 1f, 30f);
            _hint = ui.Button("Hint", bottomBar, "İpucu", UiFactory.Orange, handler.OnHintPressed, SpriteFactory.Bulb, ButtonLayout.IconTop,
                22f, UiFactory.FontSmall + 1f, 30f);
        }

        public void SetTitle(string title) => _title.text = title;

        public void SetLayerInfo(string text)
        {
            _layerInfo.text = text;
            _layerChip.gameObject.SetActive(!string.IsNullOrEmpty(text));
        }

        public void SetMessage(string text) => _message.text = text;

        /// <summary>Shows up to five mechanic chips; an empty list hides the row.</summary>
        public void SetMechanics(IReadOnlyList<MechanicChip> chips)
        {
            int count = Mathf.Min(chips.Count, MaxMechanicChips);
            _mechanicRow.gameObject.SetActive(count > 0);
            for (int i = 0; i < MaxMechanicChips; i++)
            {
                bool shown = i < count;
                _mechanicIcons[i].transform.parent.gameObject.SetActive(shown);
                if (!shown) continue;
                _mechanicIcons[i].sprite = chips[i].Icon;
                _mechanicIcons[i].color = chips[i].IconColor;
                _mechanicLabels[i].text = chips[i].Label;
                _mechanicLabels[i].color = chips[i].LabelColor;
            }
        }


        public void SetMuted(bool muted) => _sound.Icon.sprite = muted ? SpriteFactory.SpeakerMuted : SpriteFactory.Speaker;

        public void SetButtons(bool undo, bool restart, bool hint, bool hintCancels)
        {
            _undo.SetInteractable(undo);
            _restart.SetInteractable(restart);
            _hint.SetInteractable(hint);
            _hint.Label.text = hintCancels ? "İptal" : "İpucu";
            _hint.Icon.sprite = hintCancels ? SpriteFactory.Close : SpriteFactory.Bulb;
            _hint.SetColors(hintCancels ? UiFactory.Pink : UiFactory.Orange);
        }
    }
}
