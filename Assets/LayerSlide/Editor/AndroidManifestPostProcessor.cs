#if UNITY_ANDROID
using System.IO;
using System.Xml;
using UnityEditor.Android;

namespace LayerSlide.Editor
{
    /// <summary>
    /// Adjusts the generated Gradle project (GDD 06 §13): the only game permission is VIBRATE, Android Auto Backup
    /// is off and the save is excluded from cloud backup and device-to-device transfer.
    /// </summary>
    public sealed class AndroidManifestPostProcessor : IPostGenerateGradleAndroidProject
    {
        const string AndroidNs = "http://schemas.android.com/apk/res/android";
        const string ToolsNs = "http://schemas.android.com/tools";

        static readonly string[] BlockedPermissions =
        {
            "android.permission.INTERNET",
            "android.permission.ACCESS_NETWORK_STATE",
            "android.permission.ACCESS_WIFI_STATE",
            "android.permission.READ_EXTERNAL_STORAGE",
            "android.permission.WRITE_EXTERNAL_STORAGE",
            "android.permission.READ_PHONE_STATE",
            "android.permission.RECORD_AUDIO",
            "android.permission.ACCESS_FINE_LOCATION",
            "android.permission.ACCESS_COARSE_LOCATION",
            "android.permission.CAMERA"
        };

        public int callbackOrder => 100;

        public void OnPostGenerateGradleAndroidProject(string unityLibraryPath)
        {
            StripPermissions(Path.Combine(unityLibraryPath, "src", "main", "AndroidManifest.xml"));

            string launcher = Path.GetFullPath(Path.Combine(unityLibraryPath, "..", "launcher", "src", "main"));
            WriteBackupRules(Path.Combine(launcher, "res", "xml"));
            PatchLauncherManifest(Path.Combine(launcher, "AndroidManifest.xml"));
        }

        /// <summary>Removes every uses-permission except VIBRATE from Unity's library manifest.</summary>
        static void StripPermissions(string manifestPath)
        {
            var document = Load(manifestPath, out var root);
            var nodes = root.SelectNodes("uses-permission");
            if (nodes != null)
                foreach (XmlElement node in nodes)
                    if (node.GetAttribute("name", AndroidNs) != "android.permission.VIBRATE")
                        root.RemoveChild(node);
            document.Save(manifestPath);
        }

        static void PatchLauncherManifest(string manifestPath)
        {
            var document = Load(manifestPath, out var root);
            root.SetAttribute("xmlns:tools", ToolsNs);

            // Explicit removals win over anything a merged library might declare.
            foreach (var permission in BlockedPermissions)
            {
                var element = document.CreateElement("uses-permission");
                element.SetAttribute("name", AndroidNs, permission);
                element.SetAttribute("node", ToolsNs, "remove");
                root.AppendChild(element);
            }

            var application = (XmlElement)root.SelectSingleNode("application");
            if (application == null)
            {
                application = document.CreateElement("application");
                root.AppendChild(application);
            }
            application.SetAttribute("allowBackup", AndroidNs, "false");
            application.SetAttribute("fullBackupContent", AndroidNs, "@xml/layerslide_backup_rules");
            application.SetAttribute("dataExtractionRules", AndroidNs, "@xml/layerslide_data_extraction_rules");
            application.SetAttribute("replace", ToolsNs, "android:allowBackup,android:fullBackupContent,android:dataExtractionRules");
            document.Save(manifestPath);
        }

        static void WriteBackupRules(string folder)
        {
            Directory.CreateDirectory(folder);
            const string excludeAll =
                "    <exclude domain=\"root\" path=\".\" />\n" +
                "    <exclude domain=\"file\" path=\".\" />\n" +
                "    <exclude domain=\"database\" path=\".\" />\n" +
                "    <exclude domain=\"sharedpref\" path=\".\" />\n" +
                "    <exclude domain=\"external\" path=\".\" />\n";

            File.WriteAllText(Path.Combine(folder, "layerslide_backup_rules.xml"),
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<full-backup-content>\n" + excludeAll + "</full-backup-content>\n");
            File.WriteAllText(Path.Combine(folder, "layerslide_data_extraction_rules.xml"),
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<data-extraction-rules>\n" +
                "  <cloud-backup>\n" + excludeAll + "  </cloud-backup>\n" +
                "  <device-transfer>\n" + excludeAll + "  </device-transfer>\n" +
                "</data-extraction-rules>\n");
        }

        static XmlDocument Load(string path, out XmlElement root)
        {
            var document = new XmlDocument();
            document.Load(path);
            root = document.DocumentElement;
            return document;
        }
    }
}
#endif
