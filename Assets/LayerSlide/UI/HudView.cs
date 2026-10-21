using LayerSlide.Presentation;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LayerSlide.UI
{
    /// <summary>
    /// In-game HUD (GDD 04 §6): a 120 dp top area (menu / title / sound row, layer row, message row)
    /// and a 96 dp bottom area with Undo, Restart and Hint.
    /// </summary>
    public sealed class HudView
    {
        public const float TopHeight = 120f;
        public const float BottomHeight = 96f;

        public readonly RectTransform Root;

        readonly TextMeshProUGUI _title;
        readonly TextMeshProUGUI _layerInfo;
        readonly TextMeshProUGUI _message;
        readonly Image _soundIcon;
        readonly Button _undo, _restart, _hint;
        readonly TextMeshProUGUI _undoLabel, _restartLabel, _hintLabel;

        public HudView(UiFactory ui, RectTransform parent, IUiHandler handler)
        {
            Root = UiFactory.Stretch(UiFactory.CreateRect("Hud", parent));

            var topRow = UiFactory.TopStrip(UiFactory.CreateRect("TopRow", Root), 0f, 48f, 16f);
            var menu = ui.IconButton("Menu", topRow, SpriteFactory.Menu, Color.white, handler.OnMenuPressed, out _);
            UiFactory.Fixed((RectTransform)menu.transform, new Vector2(0f, 0.5f), Vector2.zero, new Vector2(48f, 48f));
            var sound = ui.IconButton("Sound", topRow, SpriteFactory.Speaker, Color.white, handler.OnMutePressed, out _soundIcon);
            UiFactory.Fixed((RectTransform)sound.transform, new Vector2(1f, 0.5f), Vector2.zero, new Vector2(48f, 48f));

            _title = ui.Text("Title", topRow, string.Empty, UiFactory.FontTitle, true, Color.white);
            UiFactory.Stretch(_title.rectTransform, 56f, 56f);
            _title.textWrappingMode = TextWrappingModes.NoWrap;

            _layerInfo = ui.Text("LayerInfo", Root, string.Empty, UiFactory.FontSmall, true, Color.white);
            UiFactory.TopStrip(_layerInfo.rectTransform, 48f, 24f, 16f);
            _layerInfo.textWrappingMode = TextWrappingModes.NoWrap;

            _message = ui.Text("Message", Root, string.Empty, UiFactory.FontBody, false, Color.white);
            UiFactory.TopStrip(_message.rectTransform, 72f, 48f, 16f);
            _message.overflowMode = TextOverflowModes.Truncate; // never spills over the board

            var bottomBar = UiFactory.BottomStrip(UiFactory.CreateRect("BottomBar", Root), 20f, UiFactory.ButtonHeight, 16f);
            var row = bottomBar.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 8f;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = true;

            _undo = ui.Button("Undo", bottomBar, "Geri al", Palette.Panel, Palette.Text, handler.OnUndoPressed, out _undoLabel);
            _restart = ui.Button("Restart", bottomBar, "Baştan", Palette.Panel, Palette.Text, handler.OnRestartPressed, out _restartLabel);
            _hint = ui.Button("Hint", bottomBar, "İpucu", Palette.Panel, Palette.Text, handler.OnHintPressed, out _hintLabel);
        }

        public void SetTitle(string title) => _title.text = title;
        public void SetLayerInfo(string text) => _layerInfo.text = text;
        public void SetMessage(string text) => _message.text = text;
        public void SetMuted(bool muted) => _soundIcon.sprite = muted ? SpriteFactory.SpeakerMuted : SpriteFactory.Speaker;

        public void SetButtons(bool undo, bool restart, bool hint, bool hintCancels)
        {
            UiFactory.SetInteractable(_undo, undo, _undoLabel);
            UiFactory.SetInteractable(_restart, restart, _restartLabel);
            UiFactory.SetInteractable(_hint, hint, _hintLabel);
            _hintLabel.text = hintCancels ? "İptal" : "İpucu";
        }
    }
}
