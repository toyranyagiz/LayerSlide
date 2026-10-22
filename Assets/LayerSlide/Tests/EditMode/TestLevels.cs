using System.Collections.Generic;
using LayerSlide.Core;

namespace LayerSlide.Tests
{
    /// <summary>Compact builders for hand-made test boards.</summary>
    static class TestLevels
    {
        public static BlockDefinition Block(string id, int x, int y, int w, int h, params string[] layers) =>
            new BlockDefinition { id = id, x = x, y = y, width = w, height = h, layers = new List<string>(layers) };

        public static GateDefinition Gate(string id, string side, int start, int length, string color) =>
            new GateDefinition { id = id, side = side, start = start, length = length, color = color };

        public static LevelDefinition Definition(int size, BlockDefinition[] blocks, GateDefinition[] gates, params CellData[] walls) =>
            new LevelDefinition
            {
                schemaVersion = 2,
                levelId = "T000",
                contentVersion = 1,
                ruleVersion = 2,
                width = size,
                height = size,
                walls = new List<CellData>(walls),
                blocks = new List<BlockDefinition>(blocks),
                gates = new List<GateDefinition>(gates),
                tutorialTags = new List<string>(),
                plates = new List<PlateDefinition>()
            };

        public static Board Board(int size, BlockDefinition[] blocks, GateDefinition[] gates, params CellData[] walls) =>
            new Board(Level.Create(Definition(size, blocks, gates, walls)));
    }
}
