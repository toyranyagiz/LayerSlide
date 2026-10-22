using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using LayerSlide.Core;
using LayerSlide.Presentation;
using LayerSlide.Services;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Audio;

namespace LayerSlide.Editor
{
    /// <summary>
    /// Generates the project's own assets (GDD 04 §11–12): baked meshes, the seven WAV effects with their import
    /// settings, the SFX mixer, the app icon and the splash logo. Called by <see cref="ProjectSetup"/>.
    /// </summary>
    public static class AssetBaker
    {
        const string Root = "Assets/LayerSlide";
        public const string MeshBankPath = Root + "/Art/Meshes/MeshBank.asset";
        const string AudioFolder = Root + "/Audio";
        const string MixerPath = AudioFolder + "/LayerSlide.mixer";
        const string LibraryPath = Root + "/Resources/Audio/SfxLibrary.asset";
        public const string IconPath = Root + "/Art/Icon.png";

        // ---------------------------------------------------------------- Meshes

        /// <summary>Generates every mesh the presenter can ask for and stores them as sub-assets of one bank.</summary>
        public static MeshBank BakeMeshes()
        {
            ProjectSetup.EnsureFolder(Path.GetDirectoryName(MeshBankPath).Replace('\\', '/'));
            MeshLibrary.ClearCache();
            MeshLibrary.KeepReadable = true;
            try
            {
                foreach (var (w, h) in new[] { (1, 1), (2, 1), (1, 2) })
                {
                    MeshLibrary.RoundedSlab(w - 0.08f, BlockView.LayerHeight, h - 0.08f);
                    MeshLibrary.Frame(w, h, 0.03f);
                    MeshLibrary.Frame(w + 0.08f, h + 0.08f, 0.05f);
                }
                MeshLibrary.Box(0.96f, 0.04f, 0.96f);
                MeshLibrary.Box(0.96f, 0.24f, 0.96f);
                MeshLibrary.Box(1f, 0.20f, 1f);
                MeshLibrary.Box(0.04f, 0.04f, 0.04f);
                MeshLibrary.Box(1f, GateView.SurfaceHeight + 0.006f, 1f);
                for (int length = 1; length <= 5; length++)
                {
                    MeshLibrary.Box(GateView.Depth, GateView.SurfaceHeight, length);
                    MeshLibrary.Box(length, GateView.SurfaceHeight, GateView.Depth);
                    MeshLibrary.Frame(GateView.Depth + 0.06f, length + 0.06f, 0.04f);
                    MeshLibrary.Frame(length + 0.06f, GateView.Depth + 0.06f, 0.04f);
                }
                MeshLibrary.Frame(1f, 1f, 0.03f);
                MeshLibrary.Frame(1f, 1f, 0.05f);
                MeshLibrary.Cross(0.3f, 0.05f);
                foreach (LayerColor color in Enum.GetValues(typeof(LayerColor)))
                {
                    MeshLibrary.Symbol(color, 0.22f);
                    MeshLibrary.Symbol(color, 0.185f);
                }
                MeshLibrary.Hand(1f);
                MeshLibrary.Hand(1.12f);
                for (int tenths = 1; tenths <= 80; tenths++)
                    MeshLibrary.Arrow(tenths / 10f);

                var bank = AssetDatabase.LoadAssetAtPath<MeshBank>(MeshBankPath);
                if (bank != null)
                {
                    foreach (var old in AssetDatabase.LoadAllAssetRepresentationsAtPath(MeshBankPath))
                        UnityEngine.Object.DestroyImmediate(old, true);
                }
                else
                {
                    bank = ScriptableObject.CreateInstance<MeshBank>();
                    AssetDatabase.CreateAsset(bank, MeshBankPath);
                }

                var entries = new List<MeshBank.Entry>();
                foreach (var pair in MeshLibrary.Loaded)
                {
                    pair.Value.name = pair.Key;
                    AssetDatabase.AddObjectToAsset(pair.Value, bank);
                    entries.Add(new MeshBank.Entry { key = pair.Key, mesh = pair.Value });
                }
                entries.Sort((a, b) => string.CompareOrdinal(a.key, b.key));
                bank.SetEntries(entries);
                EditorUtility.SetDirty(bank);
                AssetDatabase.SaveAssets();
                return bank;
            }
            finally
            {
                MeshLibrary.KeepReadable = false;
                MeshLibrary.ClearCache();
            }
        }

        // ---------------------------------------------------------------- Audio

        public static void BakeAudio()
        {
            ProjectSetup.EnsureFolder(AudioFolder);
            ProjectSetup.EnsureFolder(Root + "/Resources/Audio");

            var clips = new AudioClip[7];
            for (int i = 0; i < clips.Length; i++)
            {
                string path = $"{AudioFolder}/{SfxSynth.FileNames[i]}.wav";
                WriteWav(path, SfxSynth.Generate((Sfx)i), SfxSynth.SampleRate);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

                var importer = (AudioImporter)AssetImporter.GetAtPath(path);
                importer.forceToMono = true;
                importer.loadInBackground = false;
                var settings = importer.defaultSampleSettings;
                settings.loadType = AudioClipLoadType.DecompressOnLoad;
                settings.compressionFormat = AudioCompressionFormat.PCM;
                settings.preloadAudioData = true;
                importer.defaultSampleSettings = settings;
                importer.SaveAndReimport();
                clips[i] = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            }

            var library = AssetDatabase.LoadAssetAtPath<SfxLibrary>(LibraryPath);
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<SfxLibrary>();
                AssetDatabase.CreateAsset(library, LibraryPath);
            }
            library.clips = clips;
            library.output = CreateMixerGroup();
            EditorUtility.SetDirty(library);
        }

        /// <summary>
        /// Creates LayerSlide.mixer with one "SFX" group. Unity has no public API for creating mixers, so this uses
        /// the editor's internal AudioMixerController; if that ever fails, sounds play unrouted (output = null).
        /// </summary>
        static AudioMixerGroup CreateMixerGroup()
        {
            var existing = AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
            if (existing == null)
            {
                try
                {
                    var controllerType = typeof(AudioImporter).Assembly.GetType("UnityEditor.Audio.AudioMixerController");
                    var create = controllerType.GetMethod("CreateMixerControllerAtPath", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    var controller = create.Invoke(null, new object[] { MixerPath });

                    var master = controllerType.GetProperty("masterGroup", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(controller);
                    var newGroup = controllerType.GetMethod("CreateNewGroup", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    var group = newGroup.Invoke(controller, new object[] { "SFX", false });
                    controllerType.GetMethod("AddChildToParent", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        .Invoke(controller, new[] { group, master });
                    AssetDatabase.SaveAssets();
                    existing = AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[AssetBaker] Could not create the audio mixer: {exception.Message}");
                    return null;
                }
            }

            var groups = existing != null ? existing.FindMatchingGroups("SFX") : Array.Empty<AudioMixerGroup>();
            return groups.Length > 0 ? groups[0] : null;
        }

        static void WriteWav(string assetPath, float[] samples, int sampleRate)
        {
            using (var stream = new FileStream(assetPath, FileMode.Create, FileAccess.Write))
            using (var writer = new BinaryWriter(stream))
            {
                int dataBytes = samples.Length * 2;
                writer.Write(new[] { 'R', 'I', 'F', 'F' });
                writer.Write(36 + dataBytes);
                writer.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
                writer.Write(16);
                writer.Write((short)1);          // PCM
                writer.Write((short)1);          // mono
                writer.Write(sampleRate);
                writer.Write(sampleRate * 2);    // byte rate
                writer.Write((short)2);          // block align
                writer.Write((short)16);         // bits per sample
                writer.Write(new[] { 'd', 'a', 't', 'a' });
                writer.Write(dataBytes);
                foreach (float sample in samples)
                    writer.Write((short)Mathf.RoundToInt(Mathf.Clamp(sample, -1f, 1f) * short.MaxValue));
            }
        }

        // ---------------------------------------------------------------- Icon and splash

        /// <summary>
        /// 512x512 icon: one block with a red top layer over a blue lower layer on navy, no text (GDD 04 §11).
        /// Also used as the splash logo shown on the same navy background.
        /// </summary>
        public static void BakeIconAndSplash()
        {
            const int size = 512;
            var pixels = new Color32[size * size];
            Color navy = Palette.Environment, red = Palette.Red, blue = Palette.Blue;
            Color blueSide = blue * 0.78f, redSide = red * 0.80f;
            blueSide.a = redSide.a = 1f;

            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                Color color = navy;
                // Layers drawn bottom-up: lower layer front face, lower top, upper front face, upper top, symbol.
                color = Over(color, blueSide, Coverage(RoundedBox(p, new Vector2(256, 176), new Vector2(150, 44), 34)));
                color = Over(color, blue, Coverage(RoundedBox(p, new Vector2(256, 212), new Vector2(150, 92), 34)));
                color = Over(color, redSide, Coverage(RoundedBox(p, new Vector2(256, 250), new Vector2(150, 44), 34)));
                color = Over(color, red, Coverage(RoundedBox(p, new Vector2(256, 300), new Vector2(150, 92), 34)));
                color = Over(color, Palette.Text, Coverage(Vector2.Distance(p, new Vector2(256, 300)) - 40f));
                color = Over(color, Color.white, Coverage(Vector2.Distance(p, new Vector2(256, 300)) - 33f));
                pixels[y * size + x] = color;
            }

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels);
            texture.Apply();
            File.WriteAllBytes(IconPath, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(IconPath, ImportAssetOptions.ForceSynchronousImport);

            var importer = (TextureImporter)AssetImporter.GetAtPath(IconPath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.SaveAndReimport();

            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
            PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);

            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(IconPath);
            PlayerSettings.SplashScreen.backgroundColor = Palette.Environment;
            PlayerSettings.SplashScreen.unityLogoStyle = PlayerSettings.SplashScreen.UnityLogoStyle.LightOnDark;
            PlayerSettings.SplashScreen.drawMode = PlayerSettings.SplashScreen.DrawMode.AllSequential;
            PlayerSettings.SplashScreen.logos = new[] { PlayerSettings.SplashScreenLogo.Create(1.5f, sprite) };
        }

        static Color Over(Color under, Color over, float coverage) => Color.Lerp(under, over, coverage);

        static float Coverage(float distance) => Mathf.Clamp01(0.5f - distance);

        static float RoundedBox(Vector2 p, Vector2 center, Vector2 halfSize, float radius)
        {
            var q = new Vector2(Mathf.Abs(p.x - center.x), Mathf.Abs(p.y - center.y)) - halfSize + Vector2.one * radius;
            return Vector2.Max(q, Vector2.zero).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - radius;
        }
    }
}
