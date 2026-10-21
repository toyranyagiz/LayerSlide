namespace LayerSlide.UI
{
    public enum AppScreen
    {
        Home,
        LevelSelect,
        Playing,
        Settings,
        About
    }

    /// <summary>User intents raised by the UI. Implemented by the game controller, which owns all decisions.</summary>
    public interface IUiHandler
    {
        void OnContinuePressed();
        void OnLevelsPressed();
        void OnSettingsPressed();
        void OnLevelSelected(string levelId);
        void OnBackPressed();

        void OnMenuPressed();
        void OnMutePressed();
        void OnUndoPressed();
        void OnRestartPressed();
        void OnHintPressed();

        void OnVolumeChanged(int volume, bool commit);
        void OnVibrationToggled();
        void OnSymbolsToggled();
        void OnReducedMotionToggled();
        void OnAboutPressed();
    }
}
