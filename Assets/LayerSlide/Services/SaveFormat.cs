using System;
using System.Collections.Generic;
using LayerSlide.Core;

namespace LayerSlide.Services
{
    /// <summary>
    /// Field/type checks for the save payload and the migration of saveVersion 1/2 (campaign 1) to 3 (GDD 06 §7).
    /// Runs on a parsed <see cref="JsonNode"/> tree, so a missing field or a wrong type rejects the candidate
    /// instead of being silently defaulted by JsonUtility.
    /// </summary>
    public static class SaveFormat
    {
        /// <summary>Reads a payload of a supported version into a profile. Null when the shape is invalid.</summary>
        public static PlayerSave Read(JsonNode root, out int version, out bool migrated)
        {
            migrated = false;
            version = 0;
            if (root == null || root.Kind != JsonKind.Object) return null;
            var versionNode = root.Get("saveVersion");
            if (versionNode == null || !versionNode.IsInteger) return null;
            version = (int)versionNode.Number;

            if (version == SaveService.SaveVersion)
                return ReadProfile(root, strict: true, campaign2: true);
            if (version == 1 || version == 2)
            {
                // Each old file is first checked against its own schema, then moved to campaign 2.
                var old = ReadProfile(root, strict: version == 2, campaign2: false);
                if (old == null) return null;
                migrated = true;
                return MigrateToCampaign2(old);
            }
            return null;
        }

        /// <summary>
        /// Campaign 1 to 2 (GDD 05 §12): settings stay; completed ids are archived and never unlock the new
        /// layouts; tutorials, the run and the first-launch flag start over so the new campaign opens at L001.
        /// </summary>
        static PlayerSave MigrateToCampaign2(PlayerSave old)
        {
            var save = new PlayerSave
            {
                saveVersion = SaveService.SaveVersion,
                campaignVersion = RuleConstants.CampaignVersion,
                revision = old.revision,
                settings = old.settings,
                isFirstLaunchCompleted = false
            };
            save.legacyCampaign1CompletedLevelIds.AddRange(old.completedLevelIds);
            return save;
        }

        /// <summary>
        /// strict = every field must exist with the right type (saveVersion 2 and 3).
        /// Otherwise (saveVersion 1) missing settings / run counters fall back to defaults and the run may be
        /// stored under "BoardState". campaign2 = saveVersion 3 fields (campaign version, legacy list).
        /// </summary>
        static PlayerSave ReadProfile(JsonNode root, bool strict, bool campaign2)
        {
            var save = new PlayerSave { saveVersion = SaveService.SaveVersion };
            if (campaign2)
            {
                if (!Int(root, "campaignVersion", true, out save.campaignVersion)) return null;
                if (!StringList(root, "legacyCampaign1CompletedLevelIds", true, save.legacyCampaign1CompletedLevelIds)) return null;
            }
            if (!Long(root, "revision", true, out save.revision) || save.revision < 0) return null;
            if (!StringList(root, "completedLevelIds", true, save.completedLevelIds)) return null;
            if (!StringList(root, "tutorialCompletedIds", strict, save.tutorialCompletedIds)) return null;
            if (!Bool(root, "isFirstLaunchCompleted", strict, out save.isFirstLaunchCompleted)) return null;
            if (!strict && root.Get("isFirstLaunchCompleted") == null)
                save.isFirstLaunchCompleted = save.completedLevelIds.Count > 0;

            if (!ReadSettings(root.Get("settings"), strict, save.settings)) return null;

            var run = root.Get("currentRun");
            if (run == null && strict) return null;
            if (run != null && run.Kind != JsonKind.Null)
            {
                save.currentRun = ReadRun(run, strict, campaign2);
                if (save.currentRun == null) return null;
            }
            return save;
        }

        static bool ReadSettings(JsonNode node, bool strict, SettingsData settings)
        {
            if (node == null) return !strict;
            if (node.Kind != JsonKind.Object) return false;

            bool hasVolume = node.Get("sfxVolume") != null;
            if (!Int(node, "sfxVolume", strict, out int volume)) return false;
            if (hasVolume) settings.sfxVolume = volume;

            bool hasLast = node.Get("lastNonZeroSfxVolume") != null;
            if (!Int(node, "lastNonZeroSfxVolume", strict, out int last)) return false;
            if (hasLast) settings.lastNonZeroSfxVolume = last;
            else if (settings.sfxVolume > 0) settings.lastNonZeroSfxVolume = settings.sfxVolume;

            if (!OptionalBool(node, "vibration", strict, ref settings.vibration)) return false;
            if (!OptionalBool(node, "colorSymbols", strict, ref settings.colorSymbols)) return false;
            if (!OptionalBool(node, "reducedMotion", strict, ref settings.reducedMotion)) return false;
            return true;
        }

        static RunSave ReadRun(JsonNode node, bool strict, bool campaign2)
        {
            if (node.Kind != JsonKind.Object) return null;
            var run = new RunSave();

            var board = node.Get("boardState") ?? (strict ? null : node.Get("BoardState"));
            if (board == null || !ReadBoardState(board, run.boardState, campaign2)) return null;

            var history = node.Get("history");
            if (history == null)
            {
                if (strict) return null;
            }
            else
            {
                if (history.Kind != JsonKind.Array) return null;
                foreach (var item in history.Items)
                {
                    var record = ReadCommand(item);
                    if (record == null) return null;
                    run.history.Add(record);
                }
            }

            if (!String(node, "attemptId", strict, out run.attemptId)) return null;
            if (string.IsNullOrEmpty(run.attemptId) && !run.IsEmpty) run.attemptId = Guid.NewGuid().ToString("N");
            if (!Long(node, "activeMilliseconds", strict, out run.activeMilliseconds) || run.activeMilliseconds < 0) return null;
            if (!Int(node, "undoCount", strict, out run.undoCount) || run.undoCount < 0) return null;
            return run;
        }

        static bool ReadBoardState(JsonNode node, BoardState state, bool campaign2)
        {
            if (node.Kind != JsonKind.Object) return false;
            if (campaign2 && !Int(node, "campaignVersion", true, out state.campaignVersion)) return false;

            if (!String(node, "levelId", true, out state.levelId)) return false;
            if (!Int(node, "contentVersion", true, out state.contentVersion)) return false;
            if (!Int(node, "ruleVersion", true, out state.ruleVersion)) return false;
            if (!Int(node, "moveCount", true, out state.moveCount) || state.moveCount < 0) return false;
            if (!Bool(node, "isCompleted", true, out state.isCompleted)) return false;

            var blocks = node.Get("blocks");
            if (blocks == null || blocks.Kind != JsonKind.Array) return false;
            foreach (var item in blocks.Items)
            {
                if (item.Kind != JsonKind.Object) return false;
                var block = new RuntimeBlock();
                if (!String(item, "id", true, out block.id)
                    || !Int(item, "x", true, out block.x)
                    || !Int(item, "y", true, out block.y)
                    || !Int(item, "removedLayerCount", true, out block.removedLayerCount)
                    || block.removedLayerCount < 0)
                    return false;
                state.blocks.Add(block);
            }
            return true;
        }

        static CommandRecord ReadCommand(JsonNode node)
        {
            if (node.Kind != JsonKind.Object) return null;
            var c = new CommandRecord();
            bool ok = String(node, "blockId", true, out c.blockId)
                && Int(node, "beforeX", true, out c.beforeX) && Int(node, "beforeY", true, out c.beforeY)
                && Int(node, "beforeRemoved", true, out c.beforeRemoved)
                && Int(node, "afterX", true, out c.afterX) && Int(node, "afterY", true, out c.afterY)
                && Int(node, "afterRemoved", true, out c.afterRemoved)
                && Int(node, "beforeMoveCount", true, out c.beforeMoveCount)
                && Int(node, "visualEndX", true, out c.visualEndX) && Int(node, "visualEndY", true, out c.visualEndY)
                && String(node, "gateId", true, out c.gateId);
            return ok ? c : null;
        }

        // ---------------------------------------------------------------- Typed field readers
        // Each returns false for a wrong type, or for a missing field when it is required.

        static bool Int(JsonNode node, string field, bool required, out int value)
        {
            value = 0;
            var child = node.Get(field);
            if (child == null) return !required;
            if (!child.IsInteger || child.Number < int.MinValue || child.Number > int.MaxValue) return false;
            value = (int)child.Number;
            return true;
        }

        static bool Long(JsonNode node, string field, bool required, out long value)
        {
            value = 0;
            var child = node.Get(field);
            if (child == null) return !required;
            if (!child.IsInteger) return false;
            value = (long)child.Number;
            return true;
        }

        static bool Bool(JsonNode node, string field, bool required, out bool value)
        {
            value = false;
            var child = node.Get(field);
            if (child == null) return !required;
            if (child.Kind != JsonKind.Bool) return false;
            value = child.Bool;
            return true;
        }

        static bool OptionalBool(JsonNode node, string field, bool required, ref bool value)
        {
            var child = node.Get(field);
            if (child == null) return !required;
            if (child.Kind != JsonKind.Bool) return false;
            value = child.Bool;
            return true;
        }

        static bool String(JsonNode node, string field, bool required, out string value)
        {
            value = string.Empty;
            var child = node.Get(field);
            if (child == null) return !required;
            if (child.Kind != JsonKind.String) return false;
            value = child.String;
            return true;
        }

        static bool StringList(JsonNode node, string field, bool required, List<string> target)
        {
            var child = node.Get(field);
            if (child == null) return !required;
            if (child.Kind != JsonKind.Array) return false;
            foreach (var item in child.Items)
            {
                if (item.Kind != JsonKind.String) return false;
                target.Add(item.String);
            }
            return true;
        }
    }
}
