using System;

namespace LayerSlide.Core
{
    /// <summary>
    /// Mutable board model: per-block position and removed layer count plus an occupancy buffer.
    /// Removed blocks keep their slot with position (-1,-1). Only <see cref="RuleEngine"/> should mutate it.
    /// </summary>
    public sealed class Board
    {
        public const int EmptyCell = -1;
        public const int WallCell = -2;

        const int RemovedKeyByte = 0xFF;

        public Level Level { get; }
        public int MoveCount { get; internal set; }
        public int ActiveBlockCount { get; private set; }
        public bool IsCleared => ActiveBlockCount == 0;

        readonly int[] _x;
        readonly int[] _y;
        readonly int[] _removed;
        readonly int[] _cells;

        public Board(Level level)
        {
            Level = level ?? throw new ArgumentNullException(nameof(level));
            int count = level.Blocks.Length;
            _x = new int[count];
            _y = new int[count];
            _removed = new int[count];
            _cells = new int[level.Width * level.Height];
            ResetToStart();
        }

        public int BlockCount => _x.Length;
        public int GetX(int block) => _x[block];
        public int GetY(int block) => _y[block];
        public int GetRemovedLayers(int block) => _removed[block];
        public int GetRemainingLayers(int block) => Level.Blocks[block].LayerCount - _removed[block];
        public bool IsRemoved(int block) => _removed[block] >= Level.Blocks[block].LayerCount;
        public LayerColor GetActiveColor(int block) => Level.Blocks[block].Layers[_removed[block]];

        public bool IsInside(int x, int y) => x >= 0 && y >= 0 && x < Level.Width && y < Level.Height;

        /// <summary>Block index, <see cref="EmptyCell"/> or <see cref="WallCell"/>. Cells outside the board read as walls.</summary>
        public int GetCell(int x, int y) => IsInside(x, y) ? _cells[y * Level.Width + x] : WallCell;

        public void ResetToStart()
        {
            for (int i = 0; i < _x.Length; i++)
            {
                var spec = Level.Blocks[i];
                _x[i] = spec.StartX;
                _y[i] = spec.StartY;
                _removed[i] = 0;
            }
            MoveCount = 0;
            RebuildOccupancy();
        }

        public void CopyFrom(Board other)
        {
            if (other.Level != Level) throw new ArgumentException("Boards belong to different levels.");
            Array.Copy(other._x, _x, _x.Length);
            Array.Copy(other._y, _y, _y.Length);
            Array.Copy(other._removed, _removed, _removed.Length);
            MoveCount = other.MoveCount;
            RebuildOccupancy();
        }

        internal void SetBlock(int block, int x, int y, int removed)
        {
            _x[block] = x;
            _y[block] = y;
            _removed[block] = removed;
            RebuildOccupancy();
        }

        // The board has at most 25 cells, so a full rebuild is cheaper to reason about than incremental updates (GDD 06 §5).
        void RebuildOccupancy()
        {
            int width = Level.Width;
            for (int i = 0; i < _cells.Length; i++)
                _cells[i] = Level.IsWall(i % width, i / width) ? WallCell : EmptyCell;

            ActiveBlockCount = 0;
            for (int b = 0; b < _x.Length; b++)
            {
                if (IsRemoved(b)) continue;
                ActiveBlockCount++;
                var spec = Level.Blocks[b];
                for (int dy = 0; dy < spec.Height; dy++)
                for (int dx = 0; dx < spec.Width; dx++)
                    _cells[(_y[b] + dy) * width + _x[b] + dx] = b;
            }
        }

        /// <summary>
        /// Packs (x, y, removed) of every block into one byte each, in id order (GDD 06 §12).
        /// Level id and versions are constant per board and therefore not part of the key.
        /// </summary>
        public ulong GetStateKey()
        {
            ulong key = 0;
            for (int b = 0; b < _x.Length; b++)
            {
                ulong packed = IsRemoved(b)
                    ? RemovedKeyByte
                    : (ulong)(_x[b] | (_y[b] << 3) | (_removed[b] << 6));
                key |= packed << (8 * b);
            }
            return key;
        }

        public void SetFromStateKey(ulong key)
        {
            for (int b = 0; b < _x.Length; b++)
            {
                int packed = (int)((key >> (8 * b)) & 0xFF);
                if (packed == RemovedKeyByte)
                {
                    _x[b] = -1;
                    _y[b] = -1;
                    _removed[b] = Level.Blocks[b].LayerCount;
                }
                else
                {
                    _x[b] = packed & 0x7;
                    _y[b] = (packed >> 3) & 0x7;
                    _removed[b] = (packed >> 6) & 0x3;
                }
            }
            RebuildOccupancy();
        }

        public void WriteTo(BoardState state)
        {
            state.levelId = Level.LevelId;
            state.contentVersion = Level.ContentVersion;
            state.ruleVersion = Level.RuleVersion;
            state.moveCount = MoveCount;
            state.isCompleted = IsCleared;
            state.blocks.Clear();
            for (int b = 0; b < _x.Length; b++)
            {
                state.blocks.Add(new RuntimeBlock
                {
                    id = Level.Blocks[b].Id,
                    x = _x[b],
                    y = _y[b],
                    removedLayerCount = _removed[b]
                });
            }
        }

        /// <summary>True when this board matches a saved state exactly (positions, layers, move count).</summary>
        public bool Matches(BoardState state)
        {
            if (state == null || state.blocks == null || state.blocks.Count != _x.Length) return false;
            if (state.levelId != Level.LevelId || state.contentVersion != Level.ContentVersion || state.ruleVersion != Level.RuleVersion)
                return false;
            if (state.moveCount != MoveCount || state.isCompleted != IsCleared) return false;

            for (int b = 0; b < _x.Length; b++)
            {
                var saved = state.blocks[b];
                if (saved.id != Level.Blocks[b].Id || saved.x != _x[b] || saved.y != _y[b] || saved.removedLayerCount != _removed[b])
                    return false;
            }
            return true;
        }
    }
}
