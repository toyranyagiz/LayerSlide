using UnityEngine;

namespace LayerSlide.Services
{
    /// <summary>
    /// Output peak limiter with a -1 dBFS ceiling (GDD 04 §9). Sits next to the AudioListener so it processes the
    /// final mix: gain drops instantly on a peak and recovers over ~50 ms. Runs on the audio thread, no allocations.
    /// </summary>
    [RequireComponent(typeof(AudioListener))]
    public sealed class OutputLimiter : MonoBehaviour
    {
        const float Ceiling = 0.8913f; // -1 dBFS
        float _gain = 1f;
        float _release;

        void Awake() => _release = 1f - Mathf.Exp(-1f / (0.05f * AudioSettings.outputSampleRate));

        void OnAudioFilterRead(float[] data, int channels)
        {
            float gain = _gain;
            for (int i = 0; i < data.Length; i += channels)
            {
                float peak = 0f;
                for (int c = 0; c < channels; c++)
                    peak = Mathf.Max(peak, Mathf.Abs(data[i + c]));

                float target = peak * gain > Ceiling ? Ceiling / peak : 1f;
                gain = target < gain ? target : gain + (1f - gain) * _release;

                for (int c = 0; c < channels; c++)
                    data[i + c] = Mathf.Clamp(data[i + c] * gain, -Ceiling, Ceiling);
            }
            _gain = gain;
        }
    }
}
