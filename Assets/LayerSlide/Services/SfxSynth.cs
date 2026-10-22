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
    /// Deterministic synthesis of the seven effects (GDD 04 §9): mono, 44.1 kHz, peak-normalized to -3 dBFS.
    /// The editor writes these samples to WAV assets; the runtime only falls back to it if an asset is missing.
    /// </summary>
    public static class SfxSynth
    {
        public const int SampleRate = 44100;
        const float PeakAmplitude = 0.708f; // -3 dBFS

        public static readonly string[] FileNames = { "select", "blocked", "gate_ready", "peel", "clear", "undo", "win" };

        public static float[] Generate(Sfx sfx)
        {
            switch (sfx)
            {
                case Sfx.Select: return Normalize(Sweep(0.060f, 880f, 990f, false));
                case Sfx.Blocked: return Normalize(Sweep(0.050f, 200f, 180f, true));
                case Sfx.GateReady: return Normalize(Sweep(0.060f, 1320f, 1480f, false));
                case Sfx.Peel: return Normalize(Sweep(0.180f, 920f, 520f, false));
                case Sfx.Clear: return Normalize(Sweep(0.220f, 620f, 1240f, false));
                case Sfx.Undo: return Normalize(Sweep(0.080f, 540f, 380f, false));
                default: return Normalize(Arpeggio(0.600f, new[] { 523.25f, 659.25f, 783.99f, 1046.5f }));
            }
        }

        public static AudioClip CreateClip(Sfx sfx)
        {
            var data = Generate(sfx);
            var clip = AudioClip.Create(FileNames[(int)sfx], data.Length, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static float[] Sweep(float duration, float startHz, float endHz, bool soft)
        {
            int count = Mathf.CeilToInt(duration * SampleRate);
            var data = new float[count];
            double phase = 0;
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)count;
                phase += 2.0 * Mathf.PI * Mathf.Lerp(startHz, endHz, t) / SampleRate;
                float sample = (float)System.Math.Sin(phase);
                if (soft) sample = Mathf.Clamp(sample * 2.5f, -1f, 1f) * 0.8f; // rounded square: a duller "thud"
                data[i] = sample * Envelope(t);
            }
            return data;
        }

        static float[] Arpeggio(float duration, float[] notes)
        {
            int count = Mathf.CeilToInt(duration * SampleRate);
            var data = new float[count];
            int noteLength = count / notes.Length;
            for (int n = 0; n < notes.Length; n++)
            {
                int start = n * noteLength;
                int length = n == notes.Length - 1 ? count - start : noteLength + noteLength / 2;
                for (int i = 0; i < length && start + i < count; i++)
                    data[start + i] += Mathf.Sin(2f * Mathf.PI * notes[n] * i / SampleRate) * Envelope(i / (float)length) * 0.6f;
            }
            return data;
        }

        /// <summary>Short attack and exponential decay to avoid clicks.</summary>
        static float Envelope(float t) => Mathf.Clamp01(t / 0.05f) * Mathf.Exp(-4f * t) * (1f - t);

        static float[] Normalize(float[] data)
        {
            float peak = 0f;
            foreach (float sample in data)
                peak = Mathf.Max(peak, Mathf.Abs(sample));
            if (peak > 0f)
                for (int i = 0; i < data.Length; i++)
                    data[i] *= PeakAmplitude / peak;
            return data;
        }
    }
}
