using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using LayerSlide.Core;
using LayerSlide.Services;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace LayerSlide.Editor
{
    /// <summary>
    /// Release / development APK builds with the release keystore and a BuildManifest.json (GDD 06 §13, 07 F04–F05).
    /// The keystore and its passwords live outside the repository in %USERPROFILE%/.layerslide/keystore.properties
    /// (or the file named by LAYERSLIDE_KEYSTORE_PROPERTIES); they are applied only for the duration of a build.
    /// Batch: Unity -batchmode -buildTarget Android -projectPath . -executeMethod LayerSlide.Editor.AndroidBuild.BuildReleaseBatch -quit
    /// </summary>
    public static class AndroidBuild
    {
        public const string GddVersion = "1.1";
        const string OutputFolder = "Builds";

        [MenuItem("Tools/Layer Slide/Build/Release APK")]
        public static void BuildReleaseMenu() => Build(development: false);

        [MenuItem("Tools/Layer Slide/Build/Development APK")]
        public static void BuildDevelopmentMenu() => Build(development: true);

        public static void BuildReleaseBatch() => RunBatch(false);
        public static void BuildDevelopmentBatch() => RunBatch(true);

        static void RunBatch(bool development)
        {
            try
            {
                Build(development);
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        public static string Build(bool development)
        {
            // Content gate: no APK with invalid data or a solution that does not replay (GDD 02 §8, 06 §13).
            if (!LevelTools.ValidateAll())
                throw new BuildFailedException("Level validation failed; see the console.");

            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android
                && !EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
                throw new BuildFailedException("Could not switch to the Android build target.");

            var keystore = ReadKeystore();
            Directory.CreateDirectory(OutputFolder);
            string apkName = $"LayerSlide-{PlayerSettings.bundleVersion}-arm64{(development ? "-dev" : string.Empty)}.apk";
            string apkPath = Path.Combine(OutputFolder, apkName);

            var options = new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = apkPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = development ? BuildOptions.Development : BuildOptions.None
            };

            EditorUserBuildSettings.buildAppBundle = false;
            EditorUserBuildSettings.exportAsGoogleAndroidProject = false;
            EditorUserBuildSettings.development = development;
            EditorUserBuildSettings.allowDebugging = false;
            EditorUserBuildSettings.connectProfiler = false;
            PlayerSettings.Android.forceInternetPermission = false;
            PlayerSettings.Android.forceSDCardPermission = false;

            PlayerSettings.Android.useCustomKeystore = true;
            PlayerSettings.Android.keystoreName = keystore["storePath"];
            PlayerSettings.Android.keystorePass = keystore["storePassword"];
            PlayerSettings.Android.keyaliasName = keystore["alias"];
            PlayerSettings.Android.keyaliasPass = keystore["keyPassword"];
            BuildReport report;
            try
            {
                report = BuildPipeline.BuildPlayer(options);
            }
            finally
            {
                // Never leave a machine-specific keystore path or passwords in ProjectSettings.
                PlayerSettings.Android.useCustomKeystore = false;
                PlayerSettings.Android.keystoreName = string.Empty;
                PlayerSettings.Android.keystorePass = string.Empty;
                PlayerSettings.Android.keyaliasName = string.Empty;
                PlayerSettings.Android.keyaliasPass = string.Empty;
                EditorUserBuildSettings.development = false;
                AssetDatabase.SaveAssets();
            }

            if (report.summary.result != BuildResult.Succeeded)
                throw new BuildFailedException($"Build failed: {report.summary.result}, {report.summary.totalErrors} error(s).");

            string manifestPath = WriteBuildManifest(apkPath, development);
            Debug.Log($"[AndroidBuild] {apkPath} ({new FileInfo(apkPath).Length:N0} bytes)\nManifest: {manifestPath}");
            return apkPath;
        }

        static Dictionary<string, string> ReadKeystore()
        {
            string path = Environment.GetEnvironmentVariable("LAYERSLIDE_KEYSTORE_PROPERTIES");
            if (string.IsNullOrEmpty(path))
                path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".layerslide", "keystore.properties");
            if (!File.Exists(path))
                throw new BuildFailedException($"Keystore properties not found at {path}. See README (Release imzası).");

            var values = new Dictionary<string, string>();
            foreach (var line in File.ReadAllLines(path))
            {
                int split = line.IndexOf('=');
                if (split > 0) values[line.Substring(0, split).Trim()] = line.Substring(split + 1).Trim();
            }
            foreach (var key in new[] { "storePath", "alias", "storePassword", "keyPassword" })
                if (!values.ContainsKey(key))
                    throw new BuildFailedException($"Keystore properties are missing '{key}'.");
            return values;
        }

        // ---------------------------------------------------------------- BuildManifest.json

        [Serializable]
        class LevelEntry
        {
            public string id;
            public int contentVersion;
            public int ruleVersion;
            public string levelSha256;
            public string solutionSha256;
            public int actionCount;
        }

        [Serializable]
        class PackageEntry
        {
            public string name;
            public string version;
        }

        [Serializable]
        class BuildManifest
        {
            public string appVersion;
            public int versionCode;
            public string gddVersion;
            public string gitCommit;
            public bool gitDirty;
            public string editorVersion;
            public bool developmentBuild;
            public List<PackageEntry> packages = new List<PackageEntry>();
            public List<LevelEntry> levels = new List<LevelEntry>();
            public int totalActionCount;
            public string apkFile;
            public long apkBytes;
            public string apkSha256;
        }

        static string WriteBuildManifest(string apkPath, bool development)
        {
            var manifest = new BuildManifest
            {
                appVersion = PlayerSettings.bundleVersion,
                versionCode = PlayerSettings.Android.bundleVersionCode,
                gddVersion = GddVersion,
                gitCommit = Git("rev-parse HEAD"),
                gitDirty = !string.IsNullOrWhiteSpace(Git("status --porcelain")),
                editorVersion = Application.unityVersion,
                developmentBuild = development,
                apkFile = Path.GetFileName(apkPath),
                apkBytes = new FileInfo(apkPath).Length,
                apkSha256 = Sha256File(apkPath)
            };

            if (JsonTree.TryParse(File.ReadAllText("Packages/packages-lock.json"), out var lockFile)
                && lockFile.Get("dependencies") is JsonNode dependencies)
            {
                foreach (var pair in dependencies.Fields.OrderBy(p => p.Key, StringComparer.Ordinal))
                    manifest.packages.Add(new PackageEntry { name = pair.Key, version = pair.Value.Get("version")?.String ?? string.Empty });
            }

            foreach (var id in LevelCatalog.LevelIds)
            {
                LevelCatalog.TryLoadLevel(id, out var level, out _);
                var solution = LevelCatalog.LoadSolution(id);
                manifest.levels.Add(new LevelEntry
                {
                    id = id,
                    contentVersion = level.ContentVersion,
                    ruleVersion = level.RuleVersion,
                    levelSha256 = Sha256File($"Assets/LayerSlide/Resources/Levels/{id}.json"),
                    solutionSha256 = Sha256File($"Assets/LayerSlide/Resources/Solutions/{id}.json"),
                    actionCount = solution.actions.Count
                });
                manifest.totalActionCount += solution.actions.Count;
            }

            string path = Path.ChangeExtension(apkPath, null) + ".BuildManifest.json";
            File.WriteAllText(path, JsonUtility.ToJson(manifest, true));
            return path;
        }

        static string Sha256File(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
                return string.Concat(sha.ComputeHash(stream).Select(b => b.ToString("x2")));
        }

        static string Git(string arguments)
        {
            try
            {
                var info = new ProcessStartInfo("git", arguments)
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Directory.GetCurrentDirectory()
                };
                using (var process = Process.Start(info))
                {
                    string output = process.StandardOutput.ReadToEnd().Trim();
                    process.WaitForExit();
                    return output;
                }
            }
            catch (Exception)
            {
                return "unknown";
            }
        }
    }
}
