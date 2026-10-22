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
        public readonly MoveAxis MoveAxis;
        /// <summary>Index into <see cref="Level.Keys"/> of the key this block carries, or -1.</summary>
        public readonly int Key;

        public BlockSpec(int index, string id, int startX, int startY, int width, int height, LayerColor[] layers,
            MoveAxis moveAxis, int key)
        {
            Index = index;
            Id = id;
            StartX = startX;
            StartY = startY;
            Width = width;
            Height = height;
            Layers = layers;
            MoveAxis = moveAxis;
            Key = key;
        }

        public int LayerCount => Layers.Length;
        public bool CarriesKey => Key >= 0;

        public bool AllowsAxis(Axis axis) =>
            MoveAxis == MoveAxis.Free || (MoveAxis == MoveAxis.X) == (axis == Axis.X);
    }

    public sealed class GateSpec
    {
        public readonly int Index;
        public readonly string Id;
        public readonly GateSide Side;
        public readonly int Start;
        public readonly int Length;
        /// <summary>Fixed color; for a phase gate this equals CycleColors[0] and the live color comes from the phase.</summary>
        public readonly LayerColor Color;
        /// <summary>Index into <see cref="Level.Keys"/> of the key that unlocks the gate, or -1.</summary>
        public readonly int RequiredKey;
        /// <summary>Indices into <see cref="Level.Plates"/> that other blocks must hold down at the same time.</summary>
        public readonly int[] RequiredPlates;
        /// <summary>Empty, or the two colors of an F4 phase gate.</summary>
        public readonly LayerColor[] CycleColors;

        public GateSpec(int index, string id, GateSide side, int start, int length, LayerColor color,
            int requiredKey, int[] requiredPlates, LayerColor[] cycleColors)
        {
            Index = index;
            Id = id;
            Side = side;
            Start = start;
            Length = length;
            Color = color;
            RequiredKey = requiredKey;
            RequiredPlates = requiredPlates;
            CycleColors = cycleColors;
        }

        public int End => Start + Length;
        public bool IsLocked => RequiredKey >= 0;
        public bool HasPhase => CycleColors.Length == 2;

        /// <summary>Color the gate accepts while the board is in <paramref name="phase"/>.</summary>
        public LayerColor ColorAt(int phase) => HasPhase ? CycleColors[phase & 1] : Color;
    }

    public sealed class PlateSpec
    {
        public readonly int Index;
        public readonly string Id;
        public readonly int X;
        public readonly int Y;

        public PlateSpec(int index, string id, int x, int y)
        {
            Index = index;
            Id = id;
            X = x;
            Y = y;
        }
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
        public readonly PlateSpec[] Plates;
        /// <summary>Distinct key ids (K1, K2) in ordinal order.</summary>
        public readonly string[] Keys;
        public readonly (int x, int y)[] Walls;
        public readonly int InitialPhase;
        public readonly bool HasPhaseGate;
        public readonly string[] TutorialTags;
        public readonly int TotalLayers;

        readonly bool[] _wallMask;
        readonly int[] _plateMask;

        Level(LevelDefinition def, BlockSpec[] blocks, GateSpec[] gates, PlateSpec[] plates, string[] keys)
        {
            LevelId = def.levelId;
            ContentVersion = def.contentVersion;
            RuleVersion = def.ruleVersion;
            Width = def.width;
            Height = def.height;
            Blocks = blocks;
            Gates = gates;
            Plates = plates;
            Keys = keys;
            InitialPhase = def.initialPhase;
            TutorialTags = def.tutorialTags.ToArray();
            foreach (var gate in gates)
                HasPhaseGate |= gate.HasPhase;

            Walls = new (int, int)[def.walls.Count];
            _wallMask = new bool[Width * Height];
            for (int i = 0; i < def.walls.Count; i++)
            {
                Walls[i] = (def.walls[i].x, def.walls[i].y);
                _wallMask[def.walls[i].y * Width + def.walls[i].x] = true;
            }

            _plateMask = new int[Width * Height];
            for (int i = 0; i < _plateMask.Length; i++) _plateMask[i] = -1;
            foreach (var plate in plates)
                _plateMask[plate.Y * Width + plate.X] = plate.Index;

            foreach (var block in blocks)
                TotalLayers += block.LayerCount;
        }

        public bool IsWall(int x, int y) => _wallMask[y * Width + x];

        /// <summary>Plate index on the cell, or -1.</summary>
        public int PlateAt(int x, int y) => _plateMask[y * Width + x];

        /// <summary>Block index that carries <paramref name="key"/>, or -1.</summary>
        public int CarrierOf(int key)
        {
            for (int i = 0; i < Blocks.Length; i++)
                if (Blocks[i].Key == key)
                    return i;
            return -1;
        }

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

            var keyList = new List<string>();
            foreach (var b in sorted)
                if (!string.IsNullOrEmpty(b.keyId))
                    keyList.Add(b.keyId);
            keyList.Sort(string.CompareOrdinal);
            var keys = keyList.ToArray();

            var plates = new PlateSpec[def.plates.Count];
            for (int i = 0; i < plates.Length; i++)
                plates[i] = new PlateSpec(i, def.plates[i].id, def.plates[i].x, def.plates[i].y);

            var blocks = new BlockSpec[sorted.Count];
            for (int i = 0; i < sorted.Count; i++)
            {
                var b = sorted[i];
                var layers = new LayerColor[b.layers.Count];
                for (int l = 0; l < layers.Length; l++)
                    layers[l] = ParseColor(b.layers[l]);
                blocks[i] = new BlockSpec(i, b.id, b.x, b.y, b.width, b.height, layers, ParseAxis(b.moveAxis),
                    KeyIndex(keys, b.keyId));
            }

            var gates = new GateSpec[def.gates.Count];
            for (int i = 0; i < gates.Length; i++)
            {
                var g = def.gates[i];
                var required = new int[g.requiredPlates.Count];
                for (int p = 0; p < required.Length; p++)
                    required[p] = Array.FindIndex(plates, plate => plate.Id == g.requiredPlates[p]);
                var cycle = new LayerColor[g.cycleColors.Count];
                for (int c = 0; c < cycle.Length; c++)
                    cycle[c] = ParseColor(g.cycleColors[c]);
                gates[i] = new GateSpec(i, g.id, ParseSide(g.side), g.start, g.length, ParseColor(g.color),
                    KeyIndex(keys, g.requiredKey), required, cycle);
            }

            return new Level(def, blocks, gates, plates, keys);
        }

        static int KeyIndex(string[] keys, string id) => string.IsNullOrEmpty(id) ? -1 : Array.IndexOf(keys, id);

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

        public static bool TryParseAxis(string value, out MoveAxis axis)
        {
            switch (value)
            {
                case "Free": axis = MoveAxis.Free; return true;
                case "X": axis = MoveAxis.X; return true;
                case "Y": axis = MoveAxis.Y; return true;
                default: axis = default; return false;
            }
        }

        static LayerColor ParseColor(string value) =>
            TryParseColor(value, out var color) ? color : throw new FormatException($"Unknown color '{value}'.");

        static GateSide ParseSide(string value) =>
            TryParseSide(value, out var side) ? side : throw new FormatException($"Unknown gate side '{value}'.");

        static MoveAxis ParseAxis(string value) =>
            TryParseAxis(value, out var axis) ? axis : throw new FormatException($"Unknown moveAxis '{value}'.");
    }
}
