using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using LayerSlide.Core;
using LayerSlide.Services;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace LayerSlide.Editor
{
    /// <summary>Content checks, local save reset and test log export.</summary>
    public static class LevelTools
    {
        const string PackageName = "com.mehmetyagiztoyran.layerslide";

        [MenuItem("Tools/Layer Slide/Validate Levels")]
        static void ValidateAllMenu() => ValidateAll();

        /// <summary>Static validation plus a replay of every recorded solution (GDD 02 §8). Also the build gate.</summary>
        public static bool ValidateAll()
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
            return failures == 0;
        }

        [MenuItem("Tools/Layer Slide/Delete Local Save")]
        public static void DeleteSave()
        {
            foreach (var name in new[] { "save.json", "save.bak", "save.tmp" })
            {
                string path = Path.Combine(Application.persistentDataPath, name);
                if (File.Exists(path)) File.Delete(path);
            }
            Debug.Log("[LevelTools] Local save deleted.");
        }

        /// <summary>Copies the editor's local event logs plus build/version info into one folder (GDD 08 §10).</summary>
        [MenuItem("Tools/Layer Slide/Export Test Log/From Editor")]
        public static void ExportEditorLog()
        {
            string target = CreateExportFolder("editor");
            foreach (var name in new[] { LocalTestLogger.FileName, LocalTestLogger.PreviousFileName })
            {
                string source = Path.Combine(LocalTestLogger.Directory, name);
                if (File.Exists(source)) File.Copy(source, Path.Combine(target, name), true);
            }
            FinishExport(target);
        }

        /// <summary>Pulls the logs of a development build from a USB-connected device with adb (run-as).</summary>
        [MenuItem("Tools/Layer Slide/Export Test Log/From Android Device (adb)")]
        public static void ExportDeviceLog()
        {
            string adb = Path.Combine(EditorApplication.applicationContentsPath, "PlaybackEngines", "AndroidPlayer", "SDK", "platform-tools", "adb.exe");
            if (!File.Exists(adb)) adb = "adb";

            string target = CreateExportFolder("device");
            foreach (var name in new[] { LocalTestLogger.FileName, LocalTestLogger.PreviousFileName })
            {
                string content = Run(adb, $"exec-out run-as {PackageName} cat files/logs/{name}", out int exitCode);
                if (exitCode == 0 && !string.IsNullOrEmpty(content))
                    File.WriteAllText(Path.Combine(target, name), content);
            }
            File.WriteAllText(Path.Combine(target, "device.txt"), Run(adb, "shell getprop ro.product.model", out _).Trim()
                + "\nAndroid " + Run(adb, "shell getprop ro.build.version.release", out _).Trim() + "\n");
            FinishExport(target);
        }

        static string CreateExportFolder(string source)
        {
            string folder = Path.Combine("TestLogs", $"{DateTime.Now:yyyyMMdd-HHmmss}-{source}");
            Directory.CreateDirectory(folder);
            return folder;
        }

        static void FinishExport(string folder)
        {
            File.WriteAllText(Path.Combine(folder, "build.txt"),
                $"appVersion={PlayerSettings.bundleVersion}\nversionCode={PlayerSettings.Android.bundleVersionCode}\n" +
                $"gddVersion={AndroidBuild.GddVersion}\neditorVersion={Application.unityVersion}\n" +
                $"ruleVersion={RuleConstants.RuleVersion}\nlogTruncated={LocalTestLogger.Truncated}\n");
            Debug.Log($"[LevelTools] Test log exported to {Path.GetFullPath(folder)}");
            EditorUtility.RevealInFinder(folder);
        }

        static string Run(string file, string arguments, out int exitCode)
        {
            try
            {
                var info = new ProcessStartInfo(file, arguments) { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
                using (var process = Process.Start(info))
                {
                    string output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();
                    exitCode = process.ExitCode;
                    return output;
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[LevelTools] {file} failed: {exception.Message}");
                exitCode = -1;
                return string.Empty;
            }
        }
    }
}
