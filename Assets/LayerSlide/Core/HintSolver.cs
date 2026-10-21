using System.Collections.Generic;
using System.Diagnostics;

namespace LayerSlide.Core
{
    public enum SolverStatus
    {
        Idle,
        Running,
        Found,
        Exhausted,
        BudgetExceeded,
        Cancelled
    }

    /// <summary>One player action: slide <see cref="Block"/> to (X, Y), then peel through <see cref="Gate"/> when it is >= 0.</summary>
    public struct SolverAction
    {
        public int Block;
        public int X;
        public int Y;
        public int Gate;

        public bool IsPeel => Gate >= 0;
    }

    /// <summary>
    /// Deterministic weighted A* (priority = g + 2h) over packed board states (GDD 06 §8 and §12).
    /// The search is resumable: <see cref="Step"/> runs for a bounded time slice so the caller can spread
    /// it over frames. No shortest-path guarantee is made.
    /// </summary>
    public sealed class HintSolver
    {
        const int HeuristicLayerWeight = 12;
        const int Unsolvable = int.MaxValue;
        const ulong RemovedByte = 0xFF;

        struct Node
        {
            public ulong Key;
            public int G;
            public int Parent;
            public SolverAction Action;
        }

        struct OpenEntry
        {
            public int Priority;
            public int G;
            public int Sequence;
            public int Node;
        }

        readonly List<Node> _nodes = new List<Node>(1024);
        readonly Dictionary<ulong, int> _nodeByKey = new Dictionary<ulong, int>(1024);
        readonly List<SolverAction> _path = new List<SolverAction>();
        readonly Stopwatch _stopwatch = new Stopwatch();
        OpenEntry[] _heap = new OpenEntry[1024];
        int _heapCount;
        int _sequence;

        Board _scratch;
        Level _level;
        ulong _goalKey;

        public SolverStatus Status { get; private set; } = SolverStatus.Idle;
        public int MaxStates { get; set; } = 25000;
        public int DiscoveredStates => _nodeByKey.Count;
        public long CpuTicks { get; private set; }
        public int ExpandedNodes { get; private set; }

        /// <summary>Actions from the start state to the goal; valid when <see cref="Status"/> is Found.</summary>
        public IReadOnlyList<SolverAction> Path => _path;

        public void Begin(Board start)
        {
            Clear();
            _level = start.Level;
            if (_scratch == null || _scratch.Level != _level)
                _scratch = new Board(_level);

            _goalKey = 0;
            for (int b = 0; b < _level.Blocks.Length; b++)
                _goalKey |= RemovedByte << (8 * b);

            ulong rootKey = start.GetStateKey();
            int h = Heuristic(rootKey);
            if (h == Unsolvable)
            {
                Status = SolverStatus.Exhausted;
                return;
            }

            _nodes.Add(new Node { Key = rootKey, G = 0, Parent = -1, Action = new SolverAction { Gate = -1, Block = -1 } });
            _nodeByKey.Add(rootKey, 0);
            Push(new OpenEntry { Priority = 2 * h, G = 0, Sequence = _sequence++, Node = 0 });
            Status = SolverStatus.Running;
        }

        /// <summary>Continues the search for at most <paramref name="maxTicks"/> stopwatch ticks.</summary>
        public SolverStatus Step(long maxTicks)
        {
            if (Status != SolverStatus.Running) return Status;

            _stopwatch.Restart();
            while (Status == SolverStatus.Running)
            {
                if (_heapCount == 0)
                {
                    Status = SolverStatus.Exhausted;
                    break;
                }

                var entry = Pop();
                var node = _nodes[entry.Node];
                if (entry.G != node.G) continue; // stale entry; a cheaper path to this key was found later

                if (node.Key == _goalKey)
                {
                    BuildPath(entry.Node);
                    Status = SolverStatus.Found;
                    break;
                }

                Expand(entry.Node);
                ExpandedNodes++;

                if (_stopwatch.ElapsedTicks >= maxTicks) break;
            }
            _stopwatch.Stop();
            CpuTicks += _stopwatch.ElapsedTicks;
            return Status;
        }

        public void Cancel()
        {
            if (Status == SolverStatus.Running)
                Status = SolverStatus.Cancelled;
        }

        /// <summary>Releases the search tree so it does not outlive the search (GDD 06 §11).</summary>
        public void Clear()
        {
            _nodes.Clear();
            _nodeByKey.Clear();
            _path.Clear();
            _heapCount = 0;
            _sequence = 0;
            CpuTicks = 0;
            ExpandedNodes = 0;
            Status = SolverStatus.Idle;
        }

        void Expand(int nodeIndex)
        {
            var parent = _nodes[nodeIndex];
            _scratch.SetFromStateKey(parent.Key);
            int childG = parent.G + 1;

            for (int b = 0; b < _level.Blocks.Length; b++)
            {
                if (_scratch.IsRemoved(b)) continue;

                int x = _scratch.GetX(b), y = _scratch.GetY(b);
                int removed = _scratch.GetRemovedLayers(b);
                bool lastLayer = removed + 1 >= _level.Blocks[b].LayerCount;

                for (int s = 0; s < 4; s++)
                {
                    var side = (GateSide)s;
                    var axis = side.GetAxis();
                    int sign = side.GetSign();
                    RuleEngine.GetSlideRange(_scratch, b, axis, out int min, out int max);
                    int origin = axis == Axis.X ? x : y;
                    int reach = sign < 0 ? origin - min : max - origin;

                    for (int d = 0; d <= reach; d++)
                    {
                        int tx = axis == Axis.X ? x + sign * d : x;
                        int ty = axis == Axis.Y ? y + sign * d : y;

                        if (d > 0)
                            TryAddChild(nodeIndex, childG, ReplaceByte(parent.Key, b, Pack(tx, ty, removed)),
                                new SolverAction { Block = b, X = tx, Y = ty, Gate = -1 });

                        int gate = RuleEngine.FindExitGate(_scratch, b, tx, ty, side);
                        if (gate >= 0)
                        {
                            ulong peeled = lastLayer ? RemovedByte : Pack(tx, ty, removed + 1);
                            TryAddChild(nodeIndex, childG, ReplaceByte(parent.Key, b, peeled),
                                new SolverAction { Block = b, X = tx, Y = ty, Gate = gate });
                        }
                    }
                }
            }
        }

        void TryAddChild(int parent, int g, ulong key, SolverAction action)
        {
            if (Status != SolverStatus.Running) return;

            if (_nodeByKey.TryGetValue(key, out int existing))
            {
                if (_nodes[existing].G <= g) return;

                // Cheaper path to a known state: update and reopen. The older heap entry becomes stale.
                var node = _nodes[existing];
                node.G = g;
                node.Parent = parent;
                node.Action = action;
                _nodes[existing] = node;
                Push(new OpenEntry { Priority = g + 2 * Heuristic(key), G = g, Sequence = _sequence++, Node = existing });
                return;
            }

            int h = Heuristic(key);
            if (h == Unsolvable) return;

            if (_nodeByKey.Count >= MaxStates)
            {
                Status = SolverStatus.BudgetExceeded;
                return;
            }

            int index = _nodes.Count;
            _nodes.Add(new Node { Key = key, G = g, Parent = parent, Action = action });
            _nodeByKey.Add(key, index);
            Push(new OpenEntry { Priority = g + 2 * h, G = g, Sequence = _sequence++, Node = index });
        }

        void BuildPath(int goalNode)
        {
            _path.Clear();
            for (int n = goalNode; _nodes[n].Parent >= 0; n = _nodes[n].Parent)
                _path.Add(_nodes[n].Action);
            _path.Reverse();
        }

        /// <summary>
        /// h = 12 x remaining layers + for each active block the Manhattan distance to the closest gate of its
        /// active color that it fits through. Obstacles are ignored. Returns <see cref="Unsolvable"/> when an
        /// active layer has no compatible gate at all.
        /// </summary>
        int Heuristic(ulong key)
        {
            int total = 0;
            for (int b = 0; b < _level.Blocks.Length; b++)
            {
                int packed = (int)((key >> (8 * b)) & 0xFF);
                if (packed == (int)RemovedByte) continue;

                var spec = _level.Blocks[b];
                int x = packed & 0x7, y = (packed >> 3) & 0x7, removed = (packed >> 6) & 0x3;
                total += HeuristicLayerWeight * (spec.LayerCount - removed);

                var color = spec.Layers[removed];
                int best = Unsolvable;
                foreach (var gate in _level.Gates)
                {
                    if (gate.Color != color) continue;
                    int distance = DistanceToGate(spec, x, y, gate);
                    if (distance < best) best = distance;
                }
                if (best == Unsolvable) return Unsolvable;
                total += best;
            }
            return total;
        }

        int DistanceToGate(BlockSpec spec, int x, int y, GateSpec gate)
        {
            bool horizontalSide = gate.Side.GetAxis() == Axis.X;
            int span = horizontalSide ? spec.Height : spec.Width;
            int alignMax = gate.End - span;
            if (alignMax < gate.Start) return Unsolvable;

            int along = horizontalSide ? y : x;
            int alignDistance = along < gate.Start ? gate.Start - along : along > alignMax ? along - alignMax : 0;

            int edgeDistance;
            switch (gate.Side)
            {
                case GateSide.Left: edgeDistance = x; break;
                case GateSide.Right: edgeDistance = _level.Width - spec.Width - x; break;
                case GateSide.Bottom: edgeDistance = y; break;
                default: edgeDistance = _level.Height - spec.Height - y; break;
            }
            return alignDistance + edgeDistance;
        }

        static ulong Pack(int x, int y, int removed) => (ulong)(x | (y << 3) | (removed << 6));

        static ulong ReplaceByte(ulong key, int block, ulong value)
        {
            int shift = 8 * block;
            return (key & ~(0xFFUL << shift)) | (value << shift);
        }

        // Binary min-heap ordered by (priority, g, insertion sequence) for deterministic tie-breaking.
        void Push(OpenEntry entry)
        {
            if (_heapCount == _heap.Length)
                System.Array.Resize(ref _heap, _heap.Length * 2);

            int i = _heapCount++;
            while (i > 0)
            {
                int parent = (i - 1) >> 1;
                if (!Less(entry, _heap[parent])) break;
                _heap[i] = _heap[parent];
                i = parent;
            }
            _heap[i] = entry;
        }

        OpenEntry Pop()
        {
            var top = _heap[0];
            var last = _heap[--_heapCount];
            int i = 0;
            while (true)
            {
                int child = 2 * i + 1;
                if (child >= _heapCount) break;
                if (child + 1 < _heapCount && Less(_heap[child + 1], _heap[child])) child++;
                if (!Less(_heap[child], last)) break;
                _heap[i] = _heap[child];
                i = child;
            }
            if (_heapCount > 0) _heap[i] = last;
            return top;
        }

        static bool Less(in OpenEntry a, in OpenEntry b)
        {
            if (a.Priority != b.Priority) return a.Priority < b.Priority;
            if (a.G != b.G) return a.G < b.G;
            return a.Sequence < b.Sequence;
        }
    }
}
