using System;
using System.Collections.Generic;

namespace LayerSlide.Core
{
    public sealed class BlockSpec
    {
        public readonly int Index;
        public readonly string Id;
        public readonly int StartX;
        public readonly int StartY;
        public readonly int Width;
        public readonly int Height;
        /// <summary>Top (active) layer first.</summary>
        public readonly LayerColor[] Layers;

        public BlockSpec(int index, string id, int startX, int startY, int width, int height, LayerColor[] layers)
        {
            Index = index;
            Id = id;
            StartX = startX;
            StartY = startY;
            Width = width;
            Height = height;
            Layers = layers;
        }

        public int LayerCount => Layers.Length;
    }

    public sealed class GateSpec
    {
        public readonly int Index;
        public readonly string Id;
        public readonly GateSide Side;
        public readonly int Start;
        public readonly int Length;
        public readonly LayerColor Color;

        public GateSpec(int index, string id, GateSide side, int start, int length, LayerColor color)
        {
            Index = index;
            Id = id;
            Side = side;
            Start = start;
            Length = length;
            Color = color;
        }

        public int End => Start + Length;
    }

    /// <summary>
    /// Immutable, validated level. Blocks are stored in ordinal id order so that block index order
    /// equals the alphabetical order the solver and state key require (GDD 06 §8).
    /// </summary>
    public sealed class Level
    {
        public readonly string LevelId;
        public readonly int ContentVersion;
        public readonly int RuleVersion;
        public readonly int Width;
        public readonly int Height;
        public readonly BlockSpec[] Blocks;
        public readonly GateSpec[] Gates;
        public readonly (int x, int y)[] Walls;
        public readonly string[] TutorialTags;
        public readonly int TotalLayers;

        readonly bool[] _wallMask;

        Level(LevelDefinition def, BlockSpec[] blocks, GateSpec[] gates)
        {
            LevelId = def.levelId;
            ContentVersion = def.contentVersion;
            RuleVersion = def.ruleVersion;
            Width = def.width;
            Height = def.height;
            Blocks = blocks;
            Gates = gates;
            TutorialTags = def.tutorialTags.ToArray();

            Walls = new (int, int)[def.walls.Count];
            _wallMask = new bool[Width * Height];
            for (int i = 0; i < def.walls.Count; i++)
            {
                Walls[i] = (def.walls[i].x, def.walls[i].y);
                _wallMask[def.walls[i].y * Width + def.walls[i].x] = true;
            }

            foreach (var block in blocks)
                TotalLayers += block.LayerCount;
        }

        public bool IsWall(int x, int y) => _wallMask[y * Width + x];

        public int IndexOfBlock(string id)
        {
            for (int i = 0; i < Blocks.Length; i++)
                if (Blocks[i].Id == id)
                    return i;
            return -1;
        }

        public int IndexOfGate(string id)
        {
            for (int i = 0; i < Gates.Length; i++)
                if (Gates[i].Id == id)
                    return i;
            return -1;
        }

        /// <summary>Validates the definition and builds a level. Throws <see cref="FormatException"/> with all problems listed.</summary>
        public static Level Create(LevelDefinition def)
        {
            var errors = new List<string>();
            if (!LevelValidator.Validate(def, errors))
                throw new FormatException($"Level '{def?.levelId}' is invalid:\n- " + string.Join("\n- ", errors));

            var sorted = new List<BlockDefinition>(def.blocks);
            sorted.Sort((a, b) => string.CompareOrdinal(a.id, b.id));

            var blocks = new BlockSpec[sorted.Count];
            for (int i = 0; i < sorted.Count; i++)
            {
                var b = sorted[i];
                var layers = new LayerColor[b.layers.Count];
                for (int l = 0; l < layers.Length; l++)
                    layers[l] = ParseColor(b.layers[l]);
                blocks[i] = new BlockSpec(i, b.id, b.x, b.y, b.width, b.height, layers);
            }

            var gates = new GateSpec[def.gates.Count];
            for (int i = 0; i < gates.Length; i++)
            {
                var g = def.gates[i];
                gates[i] = new GateSpec(i, g.id, ParseSide(g.side), g.start, g.length, ParseColor(g.color));
            }

            return new Level(def, blocks, gates);
        }

        public static bool TryParseColor(string value, out LayerColor color)
        {
            switch (value)
            {
                case "Red": color = LayerColor.Red; return true;
                case "Blue": color = LayerColor.Blue; return true;
                case "Yellow": color = LayerColor.Yellow; return true;
                default: color = default; return false;
            }
        }

        public static bool TryParseSide(string value, out GateSide side)
        {
            switch (value)
            {
                case "Left": side = GateSide.Left; return true;
                case "Right": side = GateSide.Right; return true;
                case "Bottom": side = GateSide.Bottom; return true;
                case "Top": side = GateSide.Top; return true;
                default: side = default; return false;
            }
        }

        static LayerColor ParseColor(string value) =>
            TryParseColor(value, out var color) ? color : throw new FormatException($"Unknown color '{value}'.");

        static GateSide ParseSide(string value) =>
            TryParseSide(value, out var side) ? side : throw new FormatException($"Unknown gate side '{value}'.");
    }
}
