using System.IO;
using LayerSlide.Services;
using NUnit.Framework;
using UnityEngine;

namespace LayerSlide.Tests
{
    /// <summary>Save envelope, revision selection and recovery (subset of GDD 08 S01–S13).</summary>
    public class SaveServiceTests
    {
        string _directory;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "LayerSlideSaveTests_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
        }

        string PathOf(string name) => Path.Combine(_directory, name);

        [Test]
        public void CleanInstallCreatesDefaultProfile()
        {
            var profile = new SaveService(_directory).Load(out var outcome);
            Assert.AreEqual(SaveLoadOutcome.CleanInstall, outcome);
            Assert.AreEqual(70, profile.settings.sfxVolume);
            Assert.IsFalse(profile.isFirstLaunchCompleted);
            Assert.IsFalse(profile.HasRun);
        }

        [Test]
        public void WriteThenLoadRoundTrips()
        {
            var service = new SaveService(_directory);
            var profile = SaveService.CreateNewProfile();
            profile.completedLevelIds.Add("L001");
            profile.settings.sfxVolume = 37;
            Assert.IsTrue(service.TryWrite(profile));
            Assert.IsTrue(service.TryWrite(profile));

            var loaded = new SaveService(_directory).Load(out var outcome);
            Assert.AreEqual(SaveLoadOutcome.Loaded, outcome);
            Assert.AreEqual(2, loaded.revision);
            Assert.AreEqual(37, loaded.settings.sfxVolume);
            CollectionAssert.AreEqual(new[] { "L001" }, loaded.completedLevelIds);
            Assert.IsTrue(File.Exists(PathOf("save.bak")), "previous valid main becomes the backup");
            Assert.IsFalse(File.Exists(PathOf("save.tmp")));
        }

        [Test]
        public void S05_CorruptMainFallsBackToBackup()
        {
            var service = new SaveService(_directory);
            var profile = SaveService.CreateNewProfile();
            service.TryWrite(profile);
            service.TryWrite(profile);
            File.WriteAllText(PathOf("save.json"), "{ broken");

            var loaded = new SaveService(_directory).Load(out var outcome);
            Assert.AreEqual(SaveLoadOutcome.RecoveredFromBackup, outcome);
            Assert.AreEqual(1, loaded.revision);
        }

        [Test]
        public void S06_NewerValidTempWins()
        {
            var service = new SaveService(_directory);
            var profile = SaveService.CreateNewProfile();
            service.TryWrite(profile);
            File.Copy(PathOf("save.json"), PathOf("save.keep"));
            service.TryWrite(profile);
            // Simulate an interrupted promotion: revision 2 left in tmp, revision 1 in main.
            File.Copy(PathOf("save.json"), PathOf("save.tmp"));
            File.Copy(PathOf("save.keep"), PathOf("save.json"), true);

            var loaded = new SaveService(_directory).Load(out var outcome);
            Assert.AreEqual(SaveLoadOutcome.Loaded, outcome);
            Assert.AreEqual(2, loaded.revision);
        }

        [Test]
        public void S07_AllCorruptStartsFreshProfile()
        {
            File.WriteAllText(PathOf("save.json"), "x");
            File.WriteAllText(PathOf("save.bak"), "y");
            var loaded = new SaveService(_directory).Load(out var outcome);
            Assert.AreEqual(SaveLoadOutcome.ProfileReset, outcome);
            Assert.AreEqual(0, loaded.completedLevelIds.Count);
        }

        [Test]
        public void X09_TamperedPayloadIsRejected()
        {
            var service = new SaveService(_directory);
            service.TryWrite(SaveService.CreateNewProfile());
            string text = File.ReadAllText(PathOf("save.json"));
            File.WriteAllText(PathOf("save.json"), text.Replace("sfxVolume\\\":70", "sfxVolume\\\":71"));

            new SaveService(_directory).Load(out var outcome);
            Assert.AreEqual(SaveLoadOutcome.ProfileReset, outcome, "hash mismatch must not load");
        }

        [Test]
        public void S13_NewerSaveVersionIsNeverOverwritten()
        {
            var service = new SaveService(_directory);
            service.TryWrite(SaveService.CreateNewProfile());
            string original = File.ReadAllText(PathOf("save.json"));
            var envelope = JsonUtility.FromJson<SaveEnvelope>(original);
            envelope.envelopeVersion = 99;
            File.WriteAllText(PathOf("save.json"), JsonUtility.ToJson(envelope));
            string newer = File.ReadAllText(PathOf("save.json"));

            var loaded = new SaveService(_directory).Load(out var outcome);
            Assert.AreEqual(SaveLoadOutcome.NewerVersion, outcome);
            Assert.IsNull(loaded);
            Assert.AreEqual(newer, File.ReadAllText(PathOf("save.json")));
        }
    }
}
