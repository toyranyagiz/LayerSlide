using System.Collections.Generic;

namespace LayerSlide.Core
{
    /// <summary>Static data checks for a <see cref="LevelDefinition"/> (GDD 06 §9).</summary>
    public static class LevelValidator
    {
        public static bool Validate(LevelDefinition def, List<string> errors)
        {
            int startCount = errors.Count;
            if (def == null)
            {
                errors.Add("Definition is null.");
                return false;
            }

            if (string.IsNullOrEmpty(def.levelId)) errors.Add("levelId is empty.");
            if (def.schemaVersion != RuleConstants.SchemaVersion) errors.Add($"Unsupported schemaVersion {def.schemaVersion}.");
            if (def.ruleVersion != RuleConstants.RuleVersion) errors.Add($"Unsupported ruleVersion {def.ruleVersion}.");
            if (def.contentVersion < 1) errors.Add("contentVersion must be >= 1.");
            if (def.width < 4 || def.width > 5 || def.height < 4 || def.height > 5)
                errors.Add($"Board size {def.width}x{def.height} is outside 4..5.");
            if (def.walls == null || def.blocks == null || def.gates == null || def.tutorialTags == null)
            {
                errors.Add("walls, blocks, gates and tutorialTags must all be present.");
                return false;
            }
            if (errors.Count > startCount)
                return false;

            int w = def.width, h = def.height;
            var occupied = new string[w * h];

            foreach (var wall in def.walls)
            {
                if (!InBoard(wall.x, wall.y, w, h)) { errors.Add($"Wall ({wall.x},{wall.y}) is outside the board."); continue; }
                ref var cell = ref occupied[wall.y * w + wall.x];
                if (cell != null) errors.Add($"Wall ({wall.x},{wall.y}) is duplicated.");
                cell = "#wall";
            }

            if (def.blocks.Count == 0) errors.Add("Level has no blocks.");
            if (def.blocks.Count > RuleConstants.MaxBlocks) errors.Add($"Level has more than {RuleConstants.MaxBlocks} blocks.");

            var blockIds = new HashSet<string>();
            foreach (var block in def.blocks)
            {
                if (string.IsNullOrEmpty(block.id) || !blockIds.Add(block.id))
                    errors.Add($"Block id '{block.id}' is empty or duplicated.");

                bool validShape = (block.width == 1 && block.height == 1)
                    || (block.width == 2 && block.height == 1)
                    || (block.width == 1 && block.height == 2);
                if (!validShape) { errors.Add($"Block {block.id} has invalid footprint {block.width}x{block.height}."); continue; }

                ValidateLayers(block, errors);

                for (int dy = 0; dy < block.height; dy++)
                for (int dx = 0; dx < block.width; dx++)
                {
                    int cx = block.x + dx, cy = block.y + dy;
                    if (!InBoard(cx, cy, w, h)) { errors.Add($"Block {block.id} leaves the board at ({cx},{cy})."); continue; }
                    ref var cell = ref occupied[cy * w + cx];
                    if (cell != null) errors.Add($"Block {block.id} overlaps {cell} at ({cx},{cy}).");
                    cell = block.id;
                }
            }

            ValidateGates(def, errors);
            ValidateGateCoverage(def, errors);

            return errors.Count == startCount;
        }

        static void ValidateLayers(BlockDefinition block, List<string> errors)
        {
            if (block.layers == null || block.layers.Count < 1 || block.layers.Count > RuleConstants.MaxLayers)
            {
                errors.Add($"Block {block.id} must have 1..{RuleConstants.MaxLayers} layers.");
                return;
            }

            for (int i = 0; i < block.layers.Count; i++)
            {
                if (!Level.TryParseColor(block.layers[i], out _))
                    errors.Add($"Block {block.id} has unknown color '{block.layers[i]}'.");
                if (i > 0 && block.layers[i] == block.layers[i - 1])
                    errors.Add($"Block {block.id} has two adjacent '{block.layers[i]}' layers.");
            }
        }

        static void ValidateGates(LevelDefinition def, List<string> errors)
        {
            var gateIds = new HashSet<string>();
            for (int i = 0; i < def.gates.Count; i++)
            {
                var gate = def.gates[i];
                if (string.IsNullOrEmpty(gate.id) || !gateIds.Add(gate.id))
                    errors.Add($"Gate id '{gate.id}' is empty or duplicated.");
                if (!Level.TryParseColor(gate.color, out _))
                    errors.Add($"Gate {gate.id} has unknown color '{gate.color}'.");
                if (!Level.TryParseSide(gate.side, out var side))
                {
                    errors.Add($"Gate {gate.id} has unknown side '{gate.side}'.");
                    continue;
                }

                int edgeLength = side.GetAxis() == Axis.X ? def.height : def.width;
                if (gate.length < 1 || gate.start < 0 || gate.start + gate.length > edgeLength)
                    errors.Add($"Gate {gate.id} range {gate.start}+{gate.length} exceeds the {side} edge.");

                for (int j = 0; j < i; j++)
                {
                    var other = def.gates[j];
                    if (other.side == gate.side && gate.start < other.start + other.length && other.start < gate.start + gate.length)
                        errors.Add($"Gates {other.id} and {gate.id} overlap on the {gate.side} edge.");
                }
            }
        }

        /// <summary>Every layer color must have at least one gate the block's footprint fits through.</summary>
        static void ValidateGateCoverage(LevelDefinition def, List<string> errors)
        {
            foreach (var block in def.blocks)
            {
                if (block.layers == null) continue;
                foreach (var color in block.layers)
                {
                    bool found = false;
                    foreach (var gate in def.gates)
                    {
                        if (gate.color != color || !Level.TryParseSide(gate.side, out var side)) continue;
                        int span = side.GetAxis() == Axis.X ? block.height : block.width;
                        if (gate.length >= span) { found = true; break; }
                    }
                    if (!found)
                        errors.Add($"Block {block.id} layer '{color}' has no gate it fits through.");
                }
            }
        }

        static bool InBoard(int x, int y, int w, int h) => x >= 0 && y >= 0 && x < w && y < h;
    }
}
