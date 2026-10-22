using UnityEngine;
using UnityEngine.Audio;

namespace LayerSlide.Services
{
    /// <summary>The seven effect clips (indexed by <see cref="Sfx"/>) and the single SFX mixer group they play through.</summary>
    public sealed class SfxLibrary : ScriptableObject
    {
        public const string ResourcePath = "Audio/SfxLibrary";

        public AudioClip[] clips = new AudioClip[7];
        public AudioMixerGroup output;
    }
}
