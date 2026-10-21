using UnityEngine;

namespace LayerSlide.Services
{
    /// <summary>
    /// Process-wide service root, created once and kept across scene loads (GDD 06 §2).
    /// Gameplay can also be entered directly in the editor; the root is then created on demand.
    /// </summary>
    public sealed class AppServices : MonoBehaviour
    {
        static AppServices _instance;

        public SaveService Save { get; private set; }
        public AudioHapticsService Audio { get; private set; }
        public HintService Hints { get; private set; }

        public static AppServices Instance => _instance != null ? _instance : Create();

        static AppServices Create()
        {
            var root = new GameObject("[AppServices]");
            DontDestroyOnLoad(root);
            _instance = root.AddComponent<AppServices>();
            _instance.Save = new SaveService(Application.persistentDataPath);
            _instance.Audio = root.AddComponent<AudioHapticsService>();
            _instance.Hints = root.AddComponent<HintService>();
            return _instance;
        }

        void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }
    }
}
