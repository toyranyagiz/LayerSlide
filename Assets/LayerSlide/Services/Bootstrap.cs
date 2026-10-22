using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LayerSlide.Services
{
    /// <summary>
    /// Entry point of the Bootstrap scene: configures the app, creates services and loads Gameplay in the
    /// background while the splash (icon + name) is visible for at least a short moment.
    /// </summary>
    public sealed class Bootstrap : MonoBehaviour
    {
        public const string GameplayScene = "Gameplay";
        const float MinimumSplashSeconds = 0.6f;

        IEnumerator Start()
        {
            ConfigureApplication();
            _ = AppServices.Instance;

            float showUntil = Time.realtimeSinceStartup + MinimumSplashSeconds;
            var load = SceneManager.LoadSceneAsync(GameplayScene);
            load.allowSceneActivation = false;
            while (load.progress < 0.9f || Time.realtimeSinceStartup < showUntil)
                yield return null;
            load.allowSceneActivation = true;
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
