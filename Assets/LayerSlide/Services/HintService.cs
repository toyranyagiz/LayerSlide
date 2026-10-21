using System;
using System.Collections.Generic;
using System.Diagnostics;
using LayerSlide.Core;
using UnityEngine;

namespace LayerSlide.Services
{
    public enum HintStatus
    {
        Found,
        Exhausted,
        BudgetExceeded,
        Cancelled,
        Failed
    }

    public readonly struct HintResult
    {
        public readonly HintStatus Status;
        public readonly SolverAction Action;
        public readonly int StateRevision;
        public readonly int RunGeneration;

        public HintResult(HintStatus status, SolverAction action, int stateRevision, int runGeneration)
        {
            Status = status;
            Action = action;
            StateRevision = stateRevision;
            RunGeneration = runGeneration;
        }
    }

    /// <summary>
    /// Runs the hint search sliced across frames on the main thread with the phone budget from GDD 06 §8:
    /// 25 000 states, 250 ms total CPU, 2 ms per frame, 1 500 ms wall clock. Results are cached per state.
    /// </summary>
    public sealed class HintService : MonoBehaviour
    {
        const int MaxStates = 25000;
        const double MaxCpuMilliseconds = 250;
        const double SliceMilliseconds = 2;
        const double MaxWallMilliseconds = 1500;
        const int CacheCapacity = 128;

        readonly struct CacheKey : IEquatable<CacheKey>
        {
            readonly string _levelId;
            readonly int _contentVersion;
            readonly ulong _state;

            public CacheKey(Level level, ulong state)
            {
                _levelId = level.LevelId;
                _contentVersion = level.ContentVersion;
                _state = state;
            }

            public bool Equals(CacheKey other) =>
                _state == other._state && _contentVersion == other._contentVersion && _levelId == other._levelId;

            public override bool Equals(object obj) => obj is CacheKey other && Equals(other);
            public override int GetHashCode() => _state.GetHashCode() ^ (_levelId.GetHashCode() * 31) ^ _contentVersion;
        }

        struct CacheEntry
        {
            public CacheKey Key;
            public HintStatus Status;
            public SolverAction Action;
        }

        readonly HintSolver _solver = new HintSolver { MaxStates = MaxStates };
        readonly Dictionary<CacheKey, LinkedListNode<CacheEntry>> _cache = new Dictionary<CacheKey, LinkedListNode<CacheEntry>>();
        readonly LinkedList<CacheEntry> _lru = new LinkedList<CacheEntry>();
        readonly Dictionary<CacheKey, SolverAction> _recordedPaths = new Dictionary<CacheKey, SolverAction>();
        readonly HashSet<string> _recordedLevels = new HashSet<string>();
        readonly Stopwatch _wallClock = new Stopwatch();

        Action<HintResult> _callback;
        CacheKey _activeKey;
        int _revision;
        int _generation;

        public bool IsSearching => _callback != null;

        /// <summary>Starts a search from the current board. Only one search runs at a time.</summary>
        public void Request(Board board, int stateRevision, int runGeneration, Action<HintResult> callback)
        {
            Cancel();
            _revision = stateRevision;
            _generation = runGeneration;
            _activeKey = new CacheKey(board.Level, board.GetStateKey());

            if (TryGetKnownAnswer(board, out var known))
            {
                callback(new HintResult(known.Status, known.Action, stateRevision, runGeneration));
                return;
            }

            _callback = callback;
            _wallClock.Restart();
            _solver.Begin(board);
        }

        public void Cancel()
        {
            if (!IsSearching) return;
            _solver.Cancel();
            Finish(HintStatus.Cancelled, default, cache: false);
        }

        void Update()
        {
            if (!IsSearching) return;

            SolverStatus status;
            try
            {
                status = _solver.Step(MillisecondsToTicks(SliceMilliseconds));
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogError($"[HintService] Solver failed: {exception}");
                Finish(HintStatus.Failed, default, cache: false);
                return;
            }

            switch (status)
            {
                case SolverStatus.Found:
                    Finish(HintStatus.Found, _solver.Path[0], cache: true);
                    return;
                case SolverStatus.Exhausted:
                    Finish(HintStatus.Exhausted, default, cache: true);
                    return;
                case SolverStatus.BudgetExceeded:
                    Finish(HintStatus.BudgetExceeded, default, cache: true);
                    return;
            }

            bool cpuSpent = _solver.CpuTicks >= MillisecondsToTicks(MaxCpuMilliseconds);
            bool wallSpent = _wallClock.Elapsed.TotalMilliseconds >= MaxWallMilliseconds;
            if (cpuSpent || wallSpent)
                Finish(HintStatus.BudgetExceeded, default, cache: true);
        }

        void Finish(HintStatus status, SolverAction action, bool cache)
        {
            var callback = _callback;
            _callback = null;
            _wallClock.Stop();
            _solver.Clear();

            if (cache)
                AddToCache(new CacheEntry { Key = _activeKey, Status = status, Action = action });

            callback?.Invoke(new HintResult(status, action, _revision, _generation));
        }

        bool TryGetKnownAnswer(Board board, out CacheEntry entry)
        {
            if (_cache.TryGetValue(_activeKey, out var node))
            {
                _lru.Remove(node);
                _lru.AddFirst(node);
                entry = node.Value;
                return true;
            }

            EnsureRecordedPath(board.Level);
            if (_recordedPaths.TryGetValue(_activeKey, out var action))
            {
                entry = new CacheEntry { Key = _activeKey, Status = HintStatus.Found, Action = action };
                return true;
            }

            entry = default;
            return false;
        }

        /// <summary>
        /// Indexes every state along the shipped, verified solution so hints along that path are instant.
        /// </summary>
        void EnsureRecordedPath(Level level)
        {
            if (!_recordedLevels.Add(level.LevelId)) return;

            var solution = LevelCatalog.LoadSolution(level.LevelId);
            if (solution == null || solution.contentVersion != level.ContentVersion || solution.ruleVersion != level.RuleVersion)
                return;

            var board = new Board(level);
            foreach (var step in solution.actions)
            {
                int block = level.IndexOfBlock(step.block);
                int gate = string.IsNullOrEmpty(step.exit) ? -1 : level.IndexOfGate(step.exit);
                if (!RuleEngine.TryCreateCommand(board, block, step.x, step.y, gate, out var command))
                    return;

                var key = new CacheKey(level, board.GetStateKey());
                if (!_recordedPaths.ContainsKey(key))
                    _recordedPaths.Add(key, new SolverAction { Block = block, X = step.x, Y = step.y, Gate = gate });
                RuleEngine.Apply(board, command);
            }
        }

        void AddToCache(CacheEntry entry)
        {
            if (_cache.TryGetValue(entry.Key, out var existing))
            {
                _lru.Remove(existing);
                _cache.Remove(entry.Key);
            }
            else if (_cache.Count >= CacheCapacity)
            {
                var oldest = _lru.Last;
                _lru.RemoveLast();
                _cache.Remove(oldest.Value.Key);
            }

            _cache.Add(entry.Key, _lru.AddFirst(entry));
        }

        static long MillisecondsToTicks(double milliseconds) => (long)(milliseconds * Stopwatch.Frequency / 1000.0);
    }
}
