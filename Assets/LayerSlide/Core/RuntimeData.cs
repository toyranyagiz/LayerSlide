using System;
using System.Collections.Generic;

namespace LayerSlide.Core
{
    [Serializable]
    public class RuntimeBlock
    {
        public string id;
        public int x;
        public int y;
        public int removedLayerCount;
    }

    [Serializable]
    public class BoardState
    {
        public string levelId;
        public int contentVersion;
        public int ruleVersion;
        public List<RuntimeBlock> blocks = new List<RuntimeBlock>();
        public int moveCount;
        public bool isCompleted;
    }

    /// <summary>
    /// One atomic player command: a single-axis slide plus at most one layer peel.
    /// When the last layer is removed, after* is (-1,-1) and visualEnd* keeps the edge cell the block left from.
    /// </summary>
    [Serializable]
    public class CommandRecord
    {
        public string blockId;
        public int beforeX;
        public int beforeY;
        public int beforeRemoved;
        public int afterX;
        public int afterY;
        public int afterRemoved;
        public int beforeMoveCount;
        public int visualEndX;
        public int visualEndY;
        /// <summary>Gate id used for the peel, or "" for a plain slide.</summary>
        public string gateId;

        public bool IsPeel => !string.IsNullOrEmpty(gateId);
        public bool Moved => visualEndX != beforeX || visualEndY != beforeY;
    }
}
