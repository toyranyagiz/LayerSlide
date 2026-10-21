using System.Collections.Generic;
using LayerSlide.Core;
using UnityEngine;

namespace LayerSlide.Services
{
    /// <summary>Ordered list of shipped levels and loader for their embedded JSON (Resources/Levels, Resources/Solutions).</summary>
    public static class LevelCatalog
    {
        static readonly string[] Ids = { "L001", "L002", "L003", "L004", "L005", "L006", "L007", "L008", "L009", "L010" };

        static readonly string[] Titles =
        {
            "İlk sürükleme", "Renk eşleştirme", "İkinci katman", "Eksen değişimi", "Önce alan aç",
            "Geçici taşıma", "Kapı genişliği", "Alt renkleri oku", "Dönüş güzergâhı", "İlk birleşim"
        };

        static readonly Dictionary<string, Level> LevelCache = new Dictionary<string, Level>();
        static readonly Dictionary<string, SolutionRecord> SolutionCache = new Dictionary<string, SolutionRecord>();

        public static int Count => Ids.Length;
        public static IReadOnlyList<string> LevelIds => Ids;

        public static int IndexOf(string levelId) => System.Array.IndexOf(Ids, levelId);
        public static bool Contains(string levelId) => IndexOf(levelId) >= 0;
        public static string GetTitle(string levelId) => Titles[IndexOf(levelId)];
        public static int GetNumber(string levelId) => IndexOf(levelId) + 1;

        public static bool TryLoadLevel(string levelId, out Level level, out string error)
        {
            error = null;
            if (LevelCache.TryGetValue(levelId, out level)) return true;

            var asset = Resources.Load<TextAsset>("Levels/" + levelId);
            if (asset == null)
            {
                error = $"Missing level data for {levelId}.";
                return false;
            }

            try
            {
                var definition = JsonUtility.FromJson<LevelDefinition>(asset.text);
                if (definition.levelId != levelId)
                    throw new System.FormatException($"File {levelId} contains level '{definition.levelId}'.");
                level = Level.Create(definition);
            }
            catch (System.Exception exception)
            {
                error = exception.Message;
                return false;
            }
            finally
            {
                Resources.UnloadAsset(asset);
            }

            LevelCache[levelId] = level;
            return true;
        }

        public static SolutionRecord LoadSolution(string levelId)
        {
            if (SolutionCache.TryGetValue(levelId, out var solution)) return solution;

            var asset = Resources.Load<TextAsset>("Solutions/" + levelId);
            if (asset == null) return null;
            solution = JsonUtility.FromJson<SolutionRecord>(asset.text);
            Resources.UnloadAsset(asset);
            SolutionCache[levelId] = solution;
            return solution;
        }
    }
}
