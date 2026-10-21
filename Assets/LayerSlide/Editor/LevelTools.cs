using System.Collections.Generic;
using System.Text;
using LayerSlide.Core;
using LayerSlide.Services;
using UnityEditor;
using UnityEngine;

namespace LayerSlide.Editor
{
    /// <summary>Content pipeline check (GDD 02 §8): static validation plus a replay of every recorded solution.</summary>
    public static class LevelTools
    {
        [MenuItem("Tools/Layer Slide/Validate Levels")]
        public static void ValidateAll()
        {
            var report = new StringBuilder();
            int failures = 0, totalActions = 0;

            foreach (var id in LevelCatalog.LevelIds)
            {
                if (!LevelCatalog.TryLoadLevel(id, out var level, out string error))
                {
                    failures++;
                    report.AppendLine($"{id}: INVALID — {error}");
                    continue;
                }

                var solution = LevelCatalog.LoadSolution(id);
                if (!SolutionReplayer.Replay(level, solution, out error))
                {
                    failures++;
                    report.AppendLine($"{id}: REPLAY FAILED — {error}");
                    continue;
                }

                totalActions += solution.actions.Count;
                report.AppendLine($"{id}: ok ({level.Width}x{level.Height}, {level.Blocks.Length} blocks, {level.TotalLayers} layers, {solution.actions.Count} actions)");
            }

            report.AppendLine($"Total recorded actions: {totalActions}");
            if (failures > 0) Debug.LogError($"[LevelTools] {failures} level(s) failed:\n{report}");
            else Debug.Log($"[LevelTools] All {LevelCatalog.Count} levels valid.\n{report}");
        }

        [MenuItem("Tools/Layer Slide/Delete Local Save")]
        public static void DeleteSave()
        {
            foreach (var name in new List<string> { "save.json", "save.bak", "save.tmp" })
            {
                string path = System.IO.Path.Combine(Application.persistentDataPath, name);
                if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
            }
            Debug.Log("[LevelTools] Local save deleted.");
        }
    }
}
