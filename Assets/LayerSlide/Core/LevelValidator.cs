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
            if (def.walls == null || def.blocks == null || def.gates == null || def.tutorialTags == null || def.plates == null)
            {
                errors.Add("walls, blocks, gates, plates and tutorialTags must all be present.");
                return false;
            }
            if (def.initialPhase != 0 && def.initialPhase != 1) errors.Add($"initialPhase {def.initialPhase} must be 0 or 1.");
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
            var keyIds = new HashSet<string>();
            foreach (var block in def.blocks)
            {
                if (string.IsNullOrEmpty(block.id) || !blockIds.Add(block.id))
                    errors.Add($"Block id '{block.id}' is empty or duplicated.");

                bool validShape = (block.width == 1 && block.height == 1)
                    || (block.width == 2 && block.height == 1)
                    || (block.width == 1 && block.height == 2);
                if (!validShape) { errors.Add($"Block {block.id} has invalid footprint {block.width}x{block.height}."); continue; }

                ValidateLayers(block, errors);
                if (!Level.TryParseAxis(block.moveAxis, out _))
                    errors.Add($"Block {block.id} has unknown moveAxis '{block.moveAxis}'.");
                if (!string.IsNullOrEmpty(block.keyId) && !keyIds.Add(block.keyId))
                    errors.Add($"Key {block.keyId} has more than one carrier.");

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

            var plateIds = ValidatePlates(def, occupied, errors);
            ValidateGates(def, errors);
            ValidateGateMechanics(def, keyIds, plateIds, errors);
            ValidateGateCoverage(def, errors);

            return errors.Count == startCount;
        }

        /// <summary>Plates sit on free floor cells, start empty and never touch each other (GDD 01 §12 F3).</summary>
        static HashSet<string> ValidatePlates(LevelDefinition def, string[] occupied, List<string> errors)
        {
            var ids = new HashSet<string>();
            int w = def.width, h = def.height;
            for (int i = 0; i < def.plates.Count; i++)
            {
                var plate = def.plates[i];
                if (string.IsNullOrEmpty(plate.id) || !ids.Add(plate.id))
                    errors.Add($"Plate id '{plate.id}' is empty or duplicated.");
                if (!InBoard(plate.x, plate.y, w, h)) { errors.Add($"Plate {plate.id} is outside the board."); continue; }
                var cell = occupied[plate.y * w + plate.x];
                if (cell != null) errors.Add($"Plate {plate.id} starts under {cell}.");

                for (int j = 0; j < i; j++)
                {
                    var other = def.plates[j];
                    if (System.Math.Abs(other.x - plate.x) + System.Math.Abs(other.y - plate.y) < 2)
                        errors.Add($"Plates {other.id} and {plate.id} are closer than 2 cells.");
                }
            }
            return ids;
        }

        static void ValidateGateMechanics(LevelDefinition def, HashSet<string> keyIds, HashSet<string> plateIds, List<string> errors)
        {
            foreach (var gate in def.gates)
            {
                if (gate.requiredPlates == null || gate.cycleColors == null)
                {
                    errors.Add($"Gate {gate.id} must list requiredPlates and cycleColors.");
                    continue;
                }
                if (!string.IsNullOrEmpty(gate.requiredKey) && !keyIds.Contains(gate.requiredKey))
                    errors.Add($"Gate {gate.id} requires key {gate.requiredKey} that no block carries.");
                foreach (var plate in gate.requiredPlates)
                    if (!plateIds.Contains(plate))
                        errors.Add($"Gate {gate.id} requires unknown plate {plate}.");

                if (gate.cycleColors.Count == 0) continue;
                if (gate.cycleColors.Count != 2 || gate.cycleColors[0] == gate.cycleColors[1])
                    errors.Add($"Gate {gate.id} cycleColors must be two different colors.");
                else if (gate.color != gate.cycleColors[0])
                    errors.Add($"Gate {gate.id} color must equal cycleColors[0].");
                foreach (var color in gate.cycleColors)
                    if (!Level.TryParseColor(color, out _))
                        errors.Add($"Gate {gate.id} has unknown cycle color '{color}'.");
            }
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

        /// <summary>Every layer color must have at least one gate the block fits through along an axis its rail allows.</summary>
        static void ValidateGateCoverage(LevelDefinition def, List<string> errors)
        {
            foreach (var block in def.blocks)
            {
                if (block.layers == null || !Level.TryParseAxis(block.moveAxis, out var rail)) continue;
                foreach (var color in block.layers)
                {
                    bool found = false;
                    foreach (var gate in def.gates)
                    {
                        bool colorFits = gate.color == color || (gate.cycleColors != null && gate.cycleColors.Contains(color));
                        if (!colorFits || !Level.TryParseSide(gate.side, out var side)) continue;
                        if (rail != MoveAxis.Free && (rail == MoveAxis.X) != (side.GetAxis() == Axis.X)) continue;
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
