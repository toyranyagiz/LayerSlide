using System.Collections.Generic;
using LayerSlide.Core;
using UnityEngine;

namespace LayerSlide.Services
{
    /// <summary>Ordered list of shipped levels and loader for their embedded JSON (Resources/Levels, Resources/Solutions).</summary>
    public static class LevelCatalog
    {
        /// <summary>Campaign 2 (GDD v2.0): L001–L030 redesigned, L031–L040 new.</summary>
        public const int CampaignVersion = RuleConstants.CampaignVersion;

        static readonly string[] Ids = BuildIds(40);

        static readonly string[] Titles =
        {
            "Anahtar için yol aç", "İki yön, iki katman", "İlk sıkışma", "Yan yol", "Duvarın çevresi",
            "Dar çıkış", "Yer değiştir", "Geri dönüş", "İki anahtar", "Kilit zinciri",
            "Kapıya hazırlık", "Alan takası", "Bir adım sonrası", "Yolu açık tut", "Ray ve kilit sınavı",
            "Plakayı tut", "Tutucu seçimi", "Plakaya giden yol", "Anahtar ve tutucu", "Yer aç, basılı tut",
            "Sıralı çözülme", "Kapıyı açık bırak", "Son tutucu", "İki plaka", "Çifte nöbet",
            "Dar park alanı", "İki tutuculu zincir", "Yeri devret", "Kilitli geçiş", "Üç mekanik sınavı",
            "Kapının ikinci rengi", "Fazı hazırla", "Dört koşul", "Tutucu ve renk sırası", "Doğru anda soy",
            "Renk sırasını koru", "Kapı ritmi", "Son hamleyi sakla", "Bütün bağlantılar", "Usta planı"
        };

        static string[] BuildIds(int count)
        {
            var ids = new string[count];
            for (int i = 0; i < count; i++) ids[i] = "L" + (i + 1).ToString("000");
            return ids;
        }

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
