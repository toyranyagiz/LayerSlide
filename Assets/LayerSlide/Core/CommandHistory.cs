using System.Collections.Generic;

namespace LayerSlide.Core
{
    /// <summary>Unlimited undo stack of committed commands for the current run (GDD 01 §7).</summary>
    public sealed class CommandHistory
    {
        readonly List<CommandRecord> _records = new List<CommandRecord>();

        public int Count => _records.Count;
        public IReadOnlyList<CommandRecord> Records => _records;

        public void Push(CommandRecord record) => _records.Add(record);

        public CommandRecord Pop()
        {
            int last = _records.Count - 1;
            var record = _records[last];
            _records.RemoveAt(last);
            return record;
        }

        public void Clear() => _records.Clear();

        public void CopyTo(List<CommandRecord> target)
        {
            target.Clear();
            target.AddRange(_records);
        }

        /// <summary>
        /// Replays saved records from the level start and accepts them only if every record is exactly
        /// what the rule engine produces. On success the board holds the final state.
        /// </summary>
        public bool TryRestore(Board board, IReadOnlyList<CommandRecord> saved, out string error)
        {
            board.ResetToStart();
            _records.Clear();
            error = null;

            for (int i = 0; i < saved.Count; i++)
            {
                var record = saved[i];
                if (record == null) { error = $"History[{i}] is null."; break; }

                int block = board.Level.IndexOfBlock(record.blockId);
                int gate = string.IsNullOrEmpty(record.gateId) ? -1 : board.Level.IndexOfGate(record.gateId);
                if (block < 0 || (gate < 0 && !string.IsNullOrEmpty(record.gateId)))
                {
                    error = $"History[{i}] references unknown ids.";
                    break;
                }

                if (!RuleEngine.TryCreateCommand(board, block, record.visualEndX, record.visualEndY, gate, out var expected)
                    || !SameCommand(expected, record))
                {
                    error = $"History[{i}] does not replay.";
                    break;
                }

                RuleEngine.Apply(board, expected);
                _records.Add(expected);
            }

            if (error == null) return true;
            board.ResetToStart();
            _records.Clear();
            return false;
        }

        static bool SameCommand(CommandRecord a, CommandRecord b) =>
            a.blockId == b.blockId
            && a.beforeX == b.beforeX && a.beforeY == b.beforeY && a.beforeRemoved == b.beforeRemoved
            && a.afterX == b.afterX && a.afterY == b.afterY && a.afterRemoved == b.afterRemoved
            && a.beforeMoveCount == b.beforeMoveCount
            && a.visualEndX == b.visualEndX && a.visualEndY == b.visualEndY
            && (a.gateId ?? string.Empty) == (b.gateId ?? string.Empty);
    }
}
