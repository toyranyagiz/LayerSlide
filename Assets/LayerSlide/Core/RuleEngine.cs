namespace LayerSlide.Core
{
    /// <summary>
    /// The single authority for legal movement, gate acceptance and command results (GDD 01).
    /// Input, hints and the solution replayer all go through these methods.
    /// </summary>
    public static class RuleEngine
    {
        /// <summary>True when the block's footprint at (x, y) is on the board and every cell is empty or the block itself.</summary>
        public static bool CanOccupy(Board board, int block, int x, int y)
        {
            var spec = board.Level.Blocks[block];
            for (int dy = 0; dy < spec.Height; dy++)
            for (int dx = 0; dx < spec.Width; dx++)
            {
                int cell = board.GetCell(x + dx, y + dy);
                if (cell != Board.EmptyCell && cell != block)
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Inclusive range of positions the block can reach along one axis from where it stands now.
        /// Every intermediate cell is checked, so a block never jumps over an obstacle.
        /// </summary>
        public static void GetSlideRange(Board board, int block, Axis axis, out int min, out int max)
        {
            int x = board.GetX(block), y = board.GetY(block);
            min = max = axis == Axis.X ? x : y;
            if (board.IsRemoved(block)) return;

            if (axis == Axis.X)
            {
                while (CanOccupy(board, block, min - 1, y)) min--;
                while (CanOccupy(board, block, max + 1, y)) max++;
            }
            else
            {
                while (CanOccupy(board, block, x, min - 1)) min--;
                while (CanOccupy(board, block, x, max + 1)) max++;
            }
        }

        /// <summary>True when the block placed at (x, y) touches the given board edge.</summary>
        public static bool IsOnEdge(Board board, int block, int x, int y, GateSide side)
        {
            var spec = board.Level.Blocks[block];
            switch (side)
            {
                case GateSide.Left: return x == 0;
                case GateSide.Right: return x + spec.Width == board.Level.Width;
                case GateSide.Bottom: return y == 0;
                default: return y + spec.Height == board.Level.Height;
            }
        }

        /// <summary>
        /// Gate on <paramref name="side"/> that accepts the block's active layer when it stands at (x, y), or -1.
        /// Requires: block on that edge, matching color and the whole block edge inside the gate range (GDD 01 §5).
        /// </summary>
        public static int FindExitGate(Board board, int block, int x, int y, GateSide side)
        {
            if (board.IsRemoved(block) || !IsOnEdge(board, block, x, y, side)) return -1;

            var spec = board.Level.Blocks[block];
            var color = board.GetActiveColor(block);
            bool horizontalSide = side.GetAxis() == Axis.X;
            int from = horizontalSide ? y : x;
            int to = from + (horizontalSide ? spec.Height : spec.Width);

            var gates = board.Level.Gates;
            for (int g = 0; g < gates.Length; g++)
            {
                var gate = gates[g];
                if (gate.Side == side && gate.Color == color && from >= gate.Start && to <= gate.End)
                    return g;
            }
            return -1;
        }

        /// <summary>
        /// Builds the command for "slide <paramref name="block"/> to (targetX, targetY), then peel through
        /// <paramref name="gateIndex"/> (or -1 for none)". Returns false for illegal or no-op requests.
        /// </summary>
        public static bool TryCreateCommand(Board board, int block, int targetX, int targetY, int gateIndex, out CommandRecord command)
        {
            command = null;
            if (block < 0 || block >= board.BlockCount || board.IsRemoved(block)) return false;

            int x = board.GetX(block), y = board.GetY(block);
            int dx = targetX - x, dy = targetY - y;
            if (dx != 0 && dy != 0) return false;

            bool moved = dx != 0 || dy != 0;
            if (moved)
            {
                var axis = dx != 0 ? Axis.X : Axis.Y;
                GetSlideRange(board, block, axis, out int min, out int max);
                int target = axis == Axis.X ? targetX : targetY;
                if (target < min || target > max) return false;
            }

            if (gateIndex >= 0)
            {
                if (gateIndex >= board.Level.Gates.Length) return false;
                var side = board.Level.Gates[gateIndex].Side;
                if (moved)
                {
                    // A combined slide + peel must travel along the gate's axis, toward the gate.
                    var axis = dx != 0 ? Axis.X : Axis.Y;
                    int sign = dx + dy > 0 ? 1 : -1;
                    if (axis != side.GetAxis() || sign != side.GetSign()) return false;
                }
                if (FindExitGate(board, block, targetX, targetY, side) != gateIndex) return false;
            }
            else if (!moved)
            {
                return false;
            }

            int removedBefore = board.GetRemovedLayers(block);
            int removedAfter = removedBefore + (gateIndex >= 0 ? 1 : 0);
            bool cleared = removedAfter >= board.Level.Blocks[block].LayerCount;

            command = new CommandRecord
            {
                blockId = board.Level.Blocks[block].Id,
                beforeX = x,
                beforeY = y,
                beforeRemoved = removedBefore,
                afterX = cleared ? -1 : targetX,
                afterY = cleared ? -1 : targetY,
                afterRemoved = removedAfter,
                beforeMoveCount = board.MoveCount,
                visualEndX = targetX,
                visualEndY = targetY,
                gateId = gateIndex >= 0 ? board.Level.Gates[gateIndex].Id : string.Empty
            };
            return true;
        }

        public static void Apply(Board board, CommandRecord command)
        {
            int block = board.Level.IndexOfBlock(command.blockId);
            board.SetBlock(block, command.afterX, command.afterY, command.afterRemoved);
            board.MoveCount = command.beforeMoveCount + 1;
        }

        /// <summary>Restores the exact state before <paramref name="command"/>; a removed block comes back.</summary>
        public static void Revert(Board board, CommandRecord command)
        {
            int block = board.Level.IndexOfBlock(command.blockId);
            board.SetBlock(block, command.beforeX, command.beforeY, command.beforeRemoved);
            board.MoveCount = command.beforeMoveCount;
        }

        /// <summary>Axis of a command: from its movement, or from the gate side for an in-place peel.</summary>
        public static Axis GetCommandAxis(Level level, CommandRecord command)
        {
            if (command.visualEndX != command.beforeX) return Axis.X;
            if (command.visualEndY != command.beforeY) return Axis.Y;
            int gate = level.IndexOfGate(command.gateId);
            return gate >= 0 ? level.Gates[gate].Side.GetAxis() : Axis.X;
        }
    }
}
