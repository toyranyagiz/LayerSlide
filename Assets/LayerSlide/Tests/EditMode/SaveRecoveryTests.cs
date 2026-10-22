using System.IO;
using System.Security.Cryptography;
using System.Text;
using LayerSlide.Services;
using NUnit.Framework;
using UnityEngine;

namespace LayerSlide.Tests
{
    /// <summary>Write failure, interrupted writes, migration and payload shape checks (S10–S12, X09).</summary>
    public class SaveRecoveryTests
    {
        string _directory;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "LayerSlideSaveRecovery_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
        }

        string PathOf(string name) => Path.Combine(_directory, name);

        /// <summary>Writes an envelope around an arbitrary payload with a correct hash.</summary>
        void WriteEnvelope(string file, string payload, int envelopeVersion = 1)
        {
            string hash;
            using (var sha = SHA256.Create())
            {
                var builder = new StringBuilder();
                foreach (byte b in sha.ComputeHash(new UTF8Encoding(false).GetBytes(payload))) builder.Append(b.ToString("x2"));
                hash = builder.ToString();
            }
            var envelope = new SaveEnvelope { envelopeVersion = envelopeVersion, payloadJson = payload, sha256 = hash };
            File.WriteAllText(PathOf(file), JsonUtility.ToJson(envelope));
        }

        static string ValidPayload(long revision = 1) =>
            JsonUtility.ToJson(new PlayerSave { revision = revision, completedLevelIds = { "L001" } });

        [Test]
        public void S10_WriteFailureIsReportedAndRetriedLater()
        {
            var service = new SaveService(_directory);
            var profile = SaveService.CreateNewProfile();
            Assert.IsTrue(service.TryWrite(profile));

            using (new FileStream(PathOf("save.json"), FileMode.Open, FileAccess.Read, FileShare.None))
            {
                profile.completedLevelIds.Add("L001");
                Assert.IsFalse(service.TryWrite(profile), "a locked main must not be reported as saved");
                Assert.IsTrue(service.IsDirty);
            }

            Assert.IsTrue(service.TryWrite(profile), "next trigger retries");
            Assert.IsFalse(service.IsDirty);
            var loaded = new SaveService(_directory).Load(out _);
            CollectionAssert.AreEqual(new[] { "L001" }, loaded.completedLevelIds);
        }

        [Test]
        public void S11_TornTempIsIgnored()
        {
            WriteEnvelope("save.json", ValidPayload(3));
            File.WriteAllText(PathOf("save.tmp"), "{\"envelopeVersion\":1,\"payloadJson\":\"{\\\"saveVer"); // cut mid-write

            var loaded = new SaveService(_directory).Load(out var outcome);
            Assert.AreEqual(SaveLoadOutcome.Loaded, outcome);
            Assert.AreEqual(3, loaded.revision);
        }

        [Test]
        public void S11_CutAfterBackupBeforePromotion()
        {
            // bak = previous revision, tmp = new complete revision, main gone.
            WriteEnvelope("save.bak", ValidPayload(4));
            WriteEnvelope("save.tmp", ValidPayload(5));

            var loaded = new SaveService(_directory).Load(out var outcome);
            Assert.AreEqual(SaveLoadOutcome.Loaded, outcome, "a newer intact tmp is normal recovery, not corruption");
            Assert.AreEqual(5, loaded.revision);
            Assert.IsTrue(File.Exists(PathOf("save.json")), "main is restored");
            Assert.IsFalse(File.Exists(PathOf("save.tmp")));
        }

        [Test]
        public void S11_OnlyBackupLeft()
        {
            WriteEnvelope("save.bak", ValidPayload(2));
            var loaded = new SaveService(_directory).Load(out _);
            Assert.AreEqual(2, loaded.revision);
        }

        [Test]
        public void S12_Version1IsMigratedAndTheOldFileKept()
        {
            // saveVersion 1: run stored as "BoardState", settings without lastNonZero/colorSymbols, no attempt data.
            const string v1 = "{\"saveVersion\":1,\"revision\":7,\"completedLevelIds\":[\"L001\",\"L002\"]," +
                "\"tutorialCompletedIds\":[\"T01_Drag\"],\"settings\":{\"sfxVolume\":40,\"vibration\":true}," +
                "\"currentRun\":{\"BoardState\":{\"levelId\":\"L003\",\"contentVersion\":1,\"ruleVersion\":1," +
                "\"blocks\":[{\"id\":\"A\",\"x\":1,\"y\":1,\"removedLayerCount\":0}],\"moveCount\":0,\"isCompleted\":false},\"history\":[]}}";
            WriteEnvelope("save.json", v1);

            var loaded = new SaveService(_directory).Load(out var outcome);
            Assert.AreEqual(SaveLoadOutcome.Loaded, outcome);
            Assert.AreEqual(SaveService.SaveVersion, loaded.saveVersion);
            Assert.AreEqual(8, loaded.revision, "migration is written as a new revision");
            Assert.AreEqual(40, loaded.settings.sfxVolume);
            Assert.AreEqual(40, loaded.settings.lastNonZeroSfxVolume);
            Assert.IsTrue(loaded.settings.vibration);
            Assert.IsTrue(loaded.settings.colorSymbols, "missing settings get their defaults");
            Assert.AreEqual("L003", loaded.currentRun.boardState.levelId);
            Assert.AreEqual(32, loaded.currentRun.attemptId.Length);
            Assert.AreEqual(0, loaded.currentRun.undoCount);
            Assert.IsTrue(loaded.isFirstLaunchCompleted);

            StringAssert.Contains("\\\"saveVersion\\\":1", File.ReadAllText(PathOf("save.bak")), "the v1 file is kept as backup");
            var reloaded = new SaveService(_directory).Load(out _);
            Assert.AreEqual(8, reloaded.revision);
        }

        [TestCase("{\"saveVersion\":2}")]
        [TestCase("missing-run")]
        [TestCase("revision-as-string")]
        [TestCase("bool-as-number")]
        [TestCase("duplicate-completed")]
        [TestCase("unknown-level")]
        [TestCase("gap-in-completed")]
        [TestCase("volume-out-of-range")]
        public void X09_InvalidPayloadIsRejectedEvenWithACorrectHash(string variant)
        {
            string valid = ValidPayload();
            string payload;
            switch (variant)
            {
                case "missing-run": payload = valid.Replace("\"currentRun\":", "\"currentRunX\":"); break;
                case "revision-as-string": payload = valid.Replace("\"revision\":1", "\"revision\":\"1\""); break;
                case "bool-as-number": payload = valid.Replace("\"isFirstLaunchCompleted\":false", "\"isFirstLaunchCompleted\":0"); break;
                case "duplicate-completed": payload = valid.Replace("[\"L001\"]", "[\"L001\",\"L001\"]"); break;
                case "unknown-level": payload = valid.Replace("[\"L001\"]", "[\"L999\"]"); break;
                case "gap-in-completed": payload = valid.Replace("[\"L001\"]", "[\"L002\"]"); break;
                case "volume-out-of-range": payload = valid.Replace("\"sfxVolume\":70", "\"sfxVolume\":170"); break;
                default: payload = variant; break;
            }
            Assert.AreNotEqual(valid, payload, "the variant must change the payload");
            WriteEnvelope("save.json", payload);

            new SaveService(_directory).Load(out var outcome);
            Assert.AreEqual(SaveLoadOutcome.ProfileReset, outcome);
        }

        [Test]
        public void JsonTreeRejectsTrailingGarbageAndDuplicateKeys()
        {
            Assert.IsFalse(JsonTree.TryParse("{\"a\":1}x", out _));
            Assert.IsFalse(JsonTree.TryParse("{\"a\":1,\"a\":2}", out _));
            Assert.IsTrue(JsonTree.TryParse("{\"a\":[1,true,null,\"\\u00e7\"]}", out var root));
            Assert.AreEqual("ç", root.Get("a").Items[3].String);
        }
    }
}
