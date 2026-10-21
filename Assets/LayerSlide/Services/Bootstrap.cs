using UnityEngine;
using UnityEngine.SceneManagement;

namespace LayerSlide.Services
{
    /// <summary>Entry point of the Bootstrap scene: configures the app, creates services and loads Gameplay.</summary>
    public sealed class Bootstrap : MonoBehaviour
    {
        public const string GameplayScene = "Gameplay";

        void Start()
        {
            ConfigureApplication();
            _ = AppServices.Instance;
            SceneManager.LoadScene(GameplayScene);
        }

        public static void ConfigureApplication()
        {
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
            Screen.orientation = ScreenOrientation.Portrait;
            Screen.sleepTimeout = SleepTimeout.SystemSetting;
        }
    }
}
