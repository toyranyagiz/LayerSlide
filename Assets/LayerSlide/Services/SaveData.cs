using System;
using System.Collections.Generic;
using LayerSlide.Core;

namespace LayerSlide.Services
{
    // Player save DTOs (GDD 06 §4, saveVersion = 3). Field names are the file format.

    [Serializable]
    public class RunSave
    {
        public BoardState boardState = new BoardState();
        public List<CommandRecord> history = new List<CommandRecord>();
        public string attemptId = string.Empty;
        public long activeMilliseconds;
        public int undoCount;

        /// <summary>
        /// JsonUtility cannot write null for nested classes, so "no current run" is stored as a run with an empty level id.
        /// </summary>
        public bool IsEmpty => boardState == null || string.IsNullOrEmpty(boardState.levelId);
    }

    [Serializable]
    public class SettingsData
    {
        public int sfxVolume = 70;
        public int lastNonZeroSfxVolume = 70;
        public bool vibration;
        public bool colorSymbols = true;
        public bool reducedMotion;
    }

    [Serializable]
    public class PlayerSave
    {
        public int saveVersion = SaveService.SaveVersion;
        public int campaignVersion = RuleConstants.CampaignVersion;
        /// <summary>Levels completed in campaign 1 (archived by the v3 migration); never unlocks anything.</summary>
        public List<string> legacyCampaign1CompletedLevelIds = new List<string>();
        public long revision;

        public List<string> completedLevelIds = new List<string>();
        public List<string> tutorialCompletedIds = new List<string>();
        public RunSave currentRun = new RunSave();
        public SettingsData settings = new SettingsData();
        public bool isFirstLaunchCompleted;

        public bool HasRun => currentRun != null && !currentRun.IsEmpty;
    }

    [Serializable]
    public class SaveEnvelope
    {
        public int envelopeVersion;
        public string payloadJson;
        public string sha256;
    }
}
