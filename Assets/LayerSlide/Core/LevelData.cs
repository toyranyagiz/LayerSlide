using System;
using System.Collections.Generic;

namespace LayerSlide.Core
{
    // Serializable DTOs that mirror the JSON contract in GDD 06 §4 field-for-field.
    // Field names are part of the file format and must not be renamed.

    [Serializable]
    public class CellData
    {
        public int x;
        public int y;
    }

    [Serializable]
    public class BlockDefinition
    {
        public string id;
        public int x;
        public int y;
        public int width;
        public int height;
        public List<string> layers = new List<string>();
    }

    [Serializable]
    public class GateDefinition
    {
        public string id;
        public string side;
        public int start;
        public int length;
        public string color;
    }

    [Serializable]
    public class LevelDefinition
    {
        public int schemaVersion;
        public string levelId;
        public int contentVersion;
        public int ruleVersion;
        public int width;
        public int height;
        public List<CellData> walls = new List<CellData>();
        public List<BlockDefinition> blocks = new List<BlockDefinition>();
        public List<GateDefinition> gates = new List<GateDefinition>();
        public List<string> tutorialTags = new List<string>();
    }

    [Serializable]
    public class SolutionAction
    {
        public string block;
        public int x;
        public int y;
        /// <summary>Gate id, or "" when the action only slides.</summary>
        public string exit;
    }

    [Serializable]
    public class SolutionRecord
    {
        public string levelId;
        public int contentVersion;
        public int ruleVersion;
        public List<SolutionAction> actions = new List<SolutionAction>();
    }
}
