using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace LayerSlide.Services
{
    /// <summary>
    /// Development-only local event log (GDD 08 §10): JSON Lines in persistentDataPath/logs/events.jsonl, capped at
    /// 2 MB per session, previous session kept as events.previous.jsonl. Nothing is sent anywhere and no personal
    /// data is written. In release builds (Debug.isDebugBuild == false) it never creates a file.
    /// Events are written only from commits and real UI results, never from pointer moves or animation callbacks.
    /// </summary>
    public static class LocalTestLogger
    {
        public const string FileName = "events.jsonl";
        public const string PreviousFileName = "events.previous.jsonl";
        const long MaxBytes = 2 * 1024 * 1024;

        static string _path;
        static string _sessionId;
        static long _bytes;
        static bool _initialized;
        static bool _truncated;

        public static bool Enabled => Debug.isDebugBuild;
        public static string Directory => Path.Combine(Application.persistentDataPath, "logs");

        /// <summary>True once the 2 MB cap stopped logging in this session (report as logTruncated=true).</summary>
        public static bool Truncated => _truncated;

        /// <summary>Common fields of every line.</summary>
        public struct Context
        {
            public string LevelId;
            public int ContentVersion;
            public int RuleVersion;
            public string AttemptId;
            public long ActiveMilliseconds;
        }

        public static void Log(string eventName, Context context, params (string key, object value)[] fields)
        {
            if (!Enabled || _truncated) return;
            try
            {
                EnsureInitialized();
                var line = new StringBuilder(256);
                line.Append('{');
                Append(line, "sessionId", _sessionId, first: true);
                Append(line, "appVersion", Application.version);
                Append(line, "eventName", eventName);
                Append(line, "levelId", context.LevelId ?? string.Empty);
                Append(line, "contentVersion", context.ContentVersion);
                Append(line, "ruleVersion", context.RuleVersion);
                Append(line, "attemptId", context.AttemptId ?? string.Empty);
                Append(line, "activeMilliseconds", context.ActiveMilliseconds);
                foreach (var (key, value) in fields)
                    Append(line, key, value);
                line.Append("}\n");

                byte[] bytes = Encoding.UTF8.GetBytes(line.ToString());
                if (_bytes + bytes.Length > MaxBytes)
                {
                    _truncated = true;
                    return;
                }
                using (var stream = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read))
                    stream.Write(bytes, 0, bytes.Length);
                _bytes += bytes.Length;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[LocalTestLogger] {exception.Message}");
            }
        }

        static void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;
            System.IO.Directory.CreateDirectory(Directory);
            _path = Path.Combine(Directory, FileName);
            string previous = Path.Combine(Directory, PreviousFileName);
            if (File.Exists(_path))
            {
                if (File.Exists(previous)) File.Delete(previous);
                File.Move(_path, previous);
            }
            _sessionId = Guid.NewGuid().ToString("N");
            _bytes = 0;
        }

        static void Append(StringBuilder line, string key, object value, bool first = false)
        {
            if (!first) line.Append(',');
            line.Append('"').Append(key).Append("\":");
            switch (value)
            {
                case null: line.Append("null"); break;
                case bool b: line.Append(b ? "true" : "false"); break;
                case int i: line.Append(i.ToString(CultureInfo.InvariantCulture)); break;
                case long l: line.Append(l.ToString(CultureInfo.InvariantCulture)); break;
                case float f: line.Append(f.ToString("0.###", CultureInfo.InvariantCulture)); break;
                case double d: line.Append(d.ToString("0.###", CultureInfo.InvariantCulture)); break;
                default: AppendString(line, value.ToString()); break;
            }
        }

        static void AppendString(StringBuilder line, string text)
        {
            line.Append('"');
            foreach (char c in text)
            {
                switch (c)
                {
                    case '"': line.Append("\\\""); break;
                    case '\\': line.Append("\\\\"); break;
                    case '\n': line.Append("\\n"); break;
                    case '\r': line.Append("\\r"); break;
                    case '\t': line.Append("\\t"); break;
                    default:
                        if (c < 0x20) line.Append("\\u").Append(((int)c).ToString("x4"));
                        else line.Append(c);
                        break;
                }
            }
            line.Append('"');
        }
    }
}
