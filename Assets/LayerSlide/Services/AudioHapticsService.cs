using UnityEngine;

namespace LayerSlide.Services
{
    /// <summary>
    /// Plays the seven effects on four voices routed to the SFX mixer group, plus optional vibration (GDD 04 §9).
    /// Clips come from the audio assets in <see cref="SfxLibrary"/>; synthesis is only a fallback for missing assets.
    /// </summary>
    public sealed class AudioHapticsService : MonoBehaviour
    {
        const int VoiceCount = 4;

        readonly AudioClip[] _clips = new AudioClip[7];
        readonly AudioSource[] _voices = new AudioSource[VoiceCount];
        readonly Sfx[] _voiceSfx = new Sfx[VoiceCount];
        readonly float[] _voiceStartTime = new float[VoiceCount];

        float _gain = 0.7f;
        bool _vibration;

#if UNITY_ANDROID && !UNITY_EDITOR
        AndroidJavaObject _vibrator;
#endif

        void Awake()
        {
            var library = Resources.Load<SfxLibrary>(SfxLibrary.ResourcePath);
            for (int i = 0; i < VoiceCount; i++)
            {
                var source = gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0f;
                if (library != null) source.outputAudioMixerGroup = library.output;
                _voices[i] = source;
            }

            for (int i = 0; i < _clips.Length; i++)
            {
                var clip = library != null && i < library.clips.Length ? library.clips[i] : null;
                _clips[i] = clip != null ? clip : SfxSynth.CreateClip((Sfx)i);
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                    _vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
            }
            catch (System.Exception)
            {
                _vibrator = null;
            }
#endif
        }

        public void Apply(SettingsData settings)
        {
            _gain = Mathf.Clamp(settings.sfxVolume, 0, 100) / 100f;
            _vibration = settings.vibration;
            foreach (var voice in _voices)
                voice.volume = _gain;
        }

        /// <summary>Volume 0 starts nothing.</summary>
        public void Play(Sfx sfx)
        {
            if (_gain <= 0f) return;

            int voice = PickVoice();
            if (voice < 0) return;

            var source = _voices[voice];
            source.Stop();
            source.clip = _clips[(int)sfx];
            source.volume = _gain;
            source.Play();
            _voiceSfx[voice] = sfx;
            _voiceStartTime[voice] = Time.unscaledTime;
        }

        /// <summary>Pause / focus loss: stop everything; nothing is replayed on resume.</summary>
        public void StopAll()
        {
            foreach (var voice in _voices)
                voice.Stop();
        }

        /// <summary>Peel = 20 ms, last layer = 30 ms. Independent of the volume setting; silently skipped when unsupported.</summary>
        public void Vibrate(int milliseconds)
        {
            if (!_vibration) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_vibrator != null)
            {
                try { _vibrator.Call("vibrate", (long)milliseconds); return; }
                catch (System.Exception) { _vibrator = null; }
            }
            // Fallback; referencing it also makes Unity declare the VIBRATE permission.
            Handheld.Vibrate();
#endif
        }

        /// <summary>Free voice first, then the oldest select/blocked, then the oldest non-win voice; all-win drops the request.</summary>
        int PickVoice()
        {
            for (int i = 0; i < VoiceCount; i++)
                if (!_voices[i].isPlaying)
                    return i;

            int pick = OldestVoice(sfx => sfx == Sfx.Select || sfx == Sfx.Blocked);
            if (pick < 0) pick = OldestVoice(sfx => sfx != Sfx.Win);
            return pick;
        }

        int OldestVoice(System.Predicate<Sfx> match)
        {
            int pick = -1;
            for (int i = 0; i < VoiceCount; i++)
                if (match(_voiceSfx[i]) && (pick < 0 || _voiceStartTime[i] < _voiceStartTime[pick]))
                    pick = i;
            return pick;
        }
    }
}
