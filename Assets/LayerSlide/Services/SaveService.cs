using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using LayerSlide.Core;
using UnityEngine;

namespace LayerSlide.Services
{
    public enum SaveLoadOutcome
    {
        /// <summary>No save files existed.</summary>
        CleanInstall,
        /// <summary>The newest valid candidate was loaded (including normal recovery of a newer tmp file).</summary>
        Loaded,
        /// <summary>main.json was unreadable and the backup was used.</summary>
        RecoveredFromBackup,
        /// <summary>Files existed but none was valid; a fresh profile was created.</summary>
        ProfileReset,
        /// <summary>The save belongs to a newer game version and must not be touched.</summary>
        NewerVersion,
        /// <summary>A campaign 1 profile was moved to campaign 2: settings kept, progress archived.</summary>
        CampaignMigrated
    }

    /// <summary>
    /// Single writer for the player profile: one envelope (payload + SHA-256) written atomically through
    /// save.tmp, with the previous valid main kept as save.bak (GDD 06 §7).
    /// </summary>
    public sealed class SaveService
    {
        public const int SaveVersion = 3;
        public const int EnvelopeVersion = 1;

        readonly string _mainPath;
        readonly string _backupPath;
        readonly string _tempPath;
        long _lastAttemptedRevision;

        /// <summary>True while the in-memory profile has changes the disk does not.</summary>
        public bool IsDirty { get; private set; }

        public SaveService(string directory)
        {
            Directory.CreateDirectory(directory);
            _mainPath = Path.Combine(directory, "save.json");
            _backupPath = Path.Combine(directory, "save.bak");
            _tempPath = Path.Combine(directory, "save.tmp");
        }

        public static PlayerSave CreateNewProfile() => new PlayerSave();

        public PlayerSave Load(out SaveLoadOutcome outcome)
        {
            bool anyFile = File.Exists(_mainPath) || File.Exists(_tempPath) || File.Exists(_backupPath);
            if (!anyFile)
            {
                outcome = SaveLoadOutcome.CleanInstall;
                return CreateNewProfile();
            }

            // Tie-break order on equal revision is main -> tmp -> bak.
            var main = ReadCandidate(_mainPath, out bool mainNewer, out bool mainMigrated);
            var temp = ReadCandidate(_tempPath, out bool tempNewer, out bool tempMigrated);
            var backup = ReadCandidate(_backupPath, out bool backupNewer, out bool backupMigrated);

            if (mainNewer || tempNewer || backupNewer)
            {
                outcome = SaveLoadOutcome.NewerVersion;
                return null;
            }

            PlayerSave best = main;
            if (temp != null && (best == null || temp.revision > best.revision)) best = temp;
            if (backup != null && (best == null || backup.revision > best.revision)) best = backup;

            if (best == null)
            {
                outcome = SaveLoadOutcome.ProfileReset;
                var fresh = CreateNewProfile();
                TryWrite(fresh);
                return fresh;
            }

            _lastAttemptedRevision = best.revision;
            bool migrated = (best == main && mainMigrated) || (best == temp && tempMigrated) || (best == backup && backupMigrated);
            outcome = migrated ? SaveLoadOutcome.CampaignMigrated
                : best == backup && File.Exists(_mainPath) && main == null ? SaveLoadOutcome.RecoveredFromBackup
                : SaveLoadOutcome.Loaded;

            if (migrated)
            {
                // saveVersion 1/2 -> 3: converted in memory and validated; written as a new revision. The previous
                // valid main is kept as save.bak by the normal write path.
                TryWrite(best);
            }
            else if (best != main)
            {
                // Put the chosen profile back into main without bumping the revision (no new player action happened).
                TryWriteRevision(best, best.revision);
            }
            return best;
        }

        /// <summary>Writes the profile with the next revision. Returns false (and stays dirty) on any IO failure.</summary>
        public bool TryWrite(PlayerSave save) => TryWriteRevision(save, _lastAttemptedRevision + 1);

        public void MarkDirty() => IsDirty = true;

        bool TryWriteRevision(PlayerSave save, long revision)
        {
            _lastAttemptedRevision = revision;
            save.revision = revision;
            save.saveVersion = SaveVersion;

            try
            {
                string payload = JsonUtility.ToJson(save);
                var envelope = new SaveEnvelope { envelopeVersion = EnvelopeVersion, payloadJson = payload, sha256 = Sha256Hex(payload) };
                byte[] bytes = new UTF8Encoding(false).GetBytes(JsonUtility.ToJson(envelope));

                using (var stream = new FileStream(_tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }

                if (ReadCandidate(_tempPath, out _, out _) == null)
                    throw new IOException("Temporary save failed verification.");

                bool hasMain = File.Exists(_mainPath);
                if (hasMain && ReadCandidate(_mainPath, out _, out _) != null)
                    File.Copy(_mainPath, _backupPath, true);

                if (hasMain)
                    File.Replace(_tempPath, _mainPath, null);
                else
                    File.Move(_tempPath, _mainPath);

                IsDirty = false;
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[SaveService] Write failed: {exception.Message}");
                IsDirty = true;
                return false;
            }
        }

        /// <summary>
        /// Reads and fully validates one file: UTF-8 JSON, envelope version, SHA-256, payload version, field types
        /// and profile rules. Returns null for anything unreadable or inconsistent.
        /// </summary>
        static PlayerSave ReadCandidate(string path, out bool isNewerVersion, out bool migrated)
        {
            isNewerVersion = false;
            migrated = false;
            if (!File.Exists(path)) return null;

            try
            {
                string text = File.ReadAllText(path, new UTF8Encoding(false, true));
                if (!JsonTree.TryParse(text, out var envelope) || envelope.Kind != JsonKind.Object) return null;

                var version = envelope.Get("envelopeVersion");
                var payloadNode = envelope.Get("payloadJson");
                var hashNode = envelope.Get("sha256");
                if (version == null || !version.IsInteger || payloadNode?.Kind != JsonKind.String || hashNode?.Kind != JsonKind.String)
                    return null;
                if (version.Number > EnvelopeVersion) { isNewerVersion = true; return null; }
                if ((int)version.Number != EnvelopeVersion) return null;

                string payload = payloadNode.String;
                if (hashNode.String != Sha256Hex(payload)) return null;
                if (!JsonTree.TryParse(payload, out var root)) return null;

                var save = SaveFormat.Read(root, out int saveVersion, out migrated);
                if (saveVersion > SaveVersion) { isNewerVersion = true; return null; }
                return save != null && IsProfileValid(save) ? save : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        static bool IsProfileValid(PlayerSave save)
        {
            if (save.saveVersion != SaveVersion || save.revision < 0) return false;
            if (save.campaignVersion != RuleConstants.CampaignVersion) return false;
            if (save.completedLevelIds == null || save.tutorialCompletedIds == null || save.settings == null || save.currentRun == null
                || save.legacyCampaign1CompletedLevelIds == null)
                return false;

            // Campaign 1 archive: unique ids of the old L001–L030 range only.
            var legacy = new HashSet<string>();
            foreach (var id in save.legacyCampaign1CompletedLevelIds)
                if (!IsCampaign1Id(id) || !legacy.Add(id))
                    return false;


            // Completed levels must be unique, known, and contiguous from the first level.
            var seen = new HashSet<string>();
            foreach (var id in save.completedLevelIds)
                if (!LevelCatalog.Contains(id) || !seen.Add(id))
                    return false;
            for (int i = 0; i < save.completedLevelIds.Count; i++)
                if (!seen.Contains(LevelCatalog.LevelIds[i]))
                    return false;

            seen.Clear();
            foreach (var id in save.tutorialCompletedIds)
                if (string.IsNullOrEmpty(id) || !seen.Add(id))
                    return false;

            var settings = save.settings;
            return settings.sfxVolume >= 0 && settings.sfxVolume <= 100
                && settings.lastNonZeroSfxVolume >= 1 && settings.lastNonZeroSfxVolume <= 100;
        }

        static bool IsCampaign1Id(string id) =>
            id != null && id.Length == 4 && id[0] == 'L' && int.TryParse(id.Substring(1), out int number) && number >= 1 && number <= 30;

        static string Sha256Hex(string text)

        {
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(new UTF8Encoding(false).GetBytes(text));
                var builder = new StringBuilder(64);
                foreach (byte b in hash)
                    builder.Append(b.ToString("x2"));
                return builder.ToString();
            }
        }
    }
}
