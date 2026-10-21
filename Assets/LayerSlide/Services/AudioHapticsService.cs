using UnityEngine;

namespace LayerSlide.Services
{
    public enum Sfx
    {
        Select,
        Blocked,
        GateReady,
        Peel,
        Clear,
        Undo,
        Win
    }

    /// <summary>
    /// Seven short synthesized effects on four voices plus optional vibration (GDD 04 §9).
    /// Clips are generated once at startup, so no copyrighted samples ship with the game.
    /// </summary>
    public sealed class AudioHapticsService : MonoBehaviour
    {
        const int SampleRate = 44100;
        const int VoiceCount = 4;
        const float PeakAmplitude = 0.708f; // -3 dBFS

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
            for (int i = 0; i < VoiceCount; i++)
            {
                var source = gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0f;
                _voices[i] = source;
            }

            _clips[(int)Sfx.Select] = Synthesize("select", 0.060f, 880f, 990f, Wave.Sine);
            _clips[(int)Sfx.Blocked] = Synthesize("blocked", 0.050f, 200f, 180f, Wave.Soft);
            _clips[(int)Sfx.GateReady] = Synthesize("gate_ready", 0.060f, 1320f, 1480f, Wave.Sine);
            _clips[(int)Sfx.Peel] = Synthesize("peel", 0.180f, 920f, 520f, Wave.Sine);
            _clips[(int)Sfx.Clear] = Synthesize("clear", 0.220f, 620f, 1240f, Wave.Sine);
            _clips[(int)Sfx.Undo] = Synthesize("undo", 0.080f, 540f, 380f, Wave.Sine);
            _clips[(int)Sfx.Win] = SynthesizeArpeggio("win", 0.600f, new[] { 523.25f, 659.25f, 783.99f, 1046.5f });

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
            // Fallback also makes Unity add the VIBRATE permission to the manifest.
            Handheld.Vibrate();
#endif
        }

        /// <summary>Free voice first, then the oldest select/blocked, then the oldest non-win voice (GDD 04 §9).</summary>
        int PickVoice()
        {
            for (int i = 0; i < VoiceCount; i++)
                if (!_voices[i].isPlaying)
                    return i;

            int pick = OldestVoice(v => v == Sfx.Select || v == Sfx.Blocked);
            if (pick < 0) pick = OldestVoice(v => v != Sfx.Win);
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

        enum Wave
        {
            Sine,
            Soft
        }

        static AudioClip Synthesize(string name, float duration, float startHz, float endHz, Wave wave)
        {
            int count = Mathf.CeilToInt(duration * SampleRate);
            var data = new float[count];
            double phase = 0;
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)count;
                float frequency = Mathf.Lerp(startHz, endHz, t);
                phase += 2.0 * Mathf.PI * frequency / SampleRate;
                float sample = (float)System.Math.Sin(phase);
                if (wave == Wave.Soft)
                    sample = Mathf.Clamp(sample * 2.5f, -1f, 1f) * 0.8f; // rounded square: duller "thud"
                data[i] = sample * Envelope(t);
            }
            return CreateClip(name, data);
        }

        static AudioClip SynthesizeArpeggio(string name, float duration, float[] notes)
        {
            int count = Mathf.CeilToInt(duration * SampleRate);
            var data = new float[count];
            int noteLength = count / notes.Length;
            for (int n = 0; n < notes.Length; n++)
            {
                int start = n * noteLength;
                int length = n == notes.Length - 1 ? count - start : noteLength + noteLength / 2;
                for (int i = 0; i < length && start + i < count; i++)
                {
                    float t = i / (float)length;
                    data[start + i] += Mathf.Sin(2f * Mathf.PI * notes[n] * i / SampleRate) * Envelope(t) * 0.6f;
                }
            }
            return CreateClip(name, data);
        }

        /// <summary>5 ms attack and exponential decay to avoid clicks.</summary>
        static float Envelope(float t)
        {
            float attack = Mathf.Clamp01(t / 0.05f);
            return attack * Mathf.Exp(-4f * t) * (1f - t);
        }

        static AudioClip CreateClip(string name, float[] data)
        {
            float peak = 0f;
            foreach (float sample in data)
                peak = Mathf.Max(peak, Mathf.Abs(sample));
            if (peak > 0f)
            {
                float scale = PeakAmplitude / peak;
                for (int i = 0; i < data.Length; i++)
                    data[i] *= scale;
            }

            var clip = AudioClip.Create(name, data.Length, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
