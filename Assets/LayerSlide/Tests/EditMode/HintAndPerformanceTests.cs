using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using LayerSlide.Core;
using LayerSlide.Services;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace LayerSlide.Tests
{
    /// <summary>Hint cache and cancellation (H01, H03, H07, X12, X13) and budgets that can be checked in the editor (F04, F07).</summary>
    public class HintAndPerformanceTests
    {
        GameObject _host;
        HintService _hints;

        [SetUp]
        public void SetUp()
        {
            _host = new GameObject("HintServiceTest");
            _hints = _host.AddComponent<HintService>();
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_host);

        HintResult RunToEnd(Board board, int revision = 1, int generation = 1)
        {
            HintResult? result = null;
            _hints.Request(board, revision, generation, r => result = r);
            for (int i = 0; i < 10000 && result == null; i++) _hints.Tick();
            Assert.IsTrue(result.HasValue, "search did not finish");
            return result.Value;
        }

        /// <summary>
        /// Distinct reachable states of a level that are not on its recorded solution path (those are answered
        /// from the shipped solution and never enter the runtime cache).
        /// </summary>
        static List<Board> ReachableStates(string levelId, int count)
        {
            Assert.IsTrue(LevelCatalog.TryLoadLevel(levelId, out var level, out _));
            var recorded = new HashSet<ulong>();
            var replay = new Board(level);
            foreach (var step in LevelCatalog.LoadSolution(levelId).actions)
            {
                recorded.Add(replay.GetStateKey());
                int gate = string.IsNullOrEmpty(step.exit) ? -1 : level.IndexOfGate(step.exit);
                RuleEngine.TryCreateCommand(replay, level.IndexOfBlock(step.block), step.x, step.y, gate, out var command);
                RuleEngine.Apply(replay, command);
            }

            var seen = new HashSet<ulong>();
            var queue = new Queue<ulong>();
            var states = new List<Board>();
            var scratch = new Board(level);
            queue.Enqueue(scratch.GetStateKey());
            seen.Add(scratch.GetStateKey());

            while (queue.Count > 0 && states.Count < count)
            {
                ulong key = queue.Dequeue();
                if (!recorded.Contains(key))
                {
                    var copy = new Board(level);
                    copy.SetFromStateKey(key);
                    states.Add(copy);
                }

                for (int b = 0; b < level.Blocks.Length; b++)
                for (int x = 0; x < level.Width; x++)
                for (int y = 0; y < level.Height; y++)
                {
                    scratch.SetFromStateKey(key);
                    if (RuleEngine.TryCreateCommand(scratch, b, x, y, -1, out var command))
                    {
                        RuleEngine.Apply(scratch, command);
                        if (seen.Add(scratch.GetStateKey())) queue.Enqueue(scratch.GetStateKey());
                    }
                }
            }
            Assert.AreEqual(count, states.Count, "level has too few reachable states for this test");
            return states;
        }

        [Test]
        public void H01_StartStateHintIsAValidFirstActionAndDoesNotPlayIt()
        {
            Assert.IsTrue(LevelCatalog.TryLoadLevel("L006", out var level, out _));
            var board = new Board(level);
            var result = RunToEnd(board);
            Assert.AreEqual(HintStatus.Found, result.Status);
            Assert.IsTrue(RuleEngine.TryCreateCommand(board, result.Action.Block, result.Action.X, result.Action.Y, result.Action.Gate, out _));
            Assert.AreEqual(0, board.MoveCount, "hint never moves the board");
        }

        [Test]
        public void H03_SameStateIsAnsweredFromTheCache()
        {
            var board = ReachableStates("L009", 1)[0];
            var first = RunToEnd(board);
            Assert.Greater(first.Expanded, 0, "first answer comes from a search");
            var second = RunToEnd(board);
            Assert.AreEqual(0, second.Expanded, "second answer comes from the cache");
            Assert.AreEqual(first.Status, second.Status);
            Assert.AreEqual(first.Action.Block, second.Action.Block);
            Assert.AreEqual(first.Action.X, second.Action.X);
            Assert.AreEqual(first.Action.Y, second.Action.Y);
        }

        [Test]
        public void X13_RuntimeCacheNeverExceeds128()
        {
            foreach (var board in ReachableStates("L010", 129))
                RunToEnd(board);
            Assert.AreEqual(128, _hints.CacheCount);
        }

        [Test]
        public void H07_X12_CancelReturnsCancelledWithTheRequestTokens()
        {
            var board = ReachableStates("L009", 1)[0];
            HintResult? result = null;
            _hints.Request(board, 41, 7, r => result = r);
            _hints.Cancel();
            Assert.IsTrue(result.HasValue);
            Assert.AreEqual(HintStatus.Cancelled, result.Value.Status);
            Assert.AreEqual(41, result.Value.StateRevision, "the caller compares these tokens and drops stale results");
            Assert.AreEqual(7, result.Value.RunGeneration);
            Assert.IsFalse(_hints.IsSearching);
            Assert.AreEqual(0, _hints.CacheCount, "cancelled searches are not cached");
        }

        [Test]
        public void F04_DragMathDoesNotAllocate()
        {
            Assert.IsTrue(LevelCatalog.TryLoadLevel("L009", out var level, out _));
            var board = new Board(level);
            var drag = new DragResolver();
            drag.Begin(board, 0);
            drag.Update(2.4f, 0f); // lock the axis outside the measured block

            Assert.That(() =>
            {
                for (int i = 0; i < 100; i++)
                {
                    drag.Update(2f + (i % 20) * 0.08f, 0.01f);
                    drag.TryGetRelease(out _, out _, out _);
                }
            }, Is.Not.AllocatingGCMemory());
        }

        /// <summary>
        /// F07 (editor part): 10 000 commands in L004, saved, reloaded and replayed, then fully undone.
        /// Device timings are measured separately on real phones.
        /// </summary>
        [Test]
        public void F07_TenThousandCommandHistorySurvivesSaveLoadAndUndo()
        {
            Assert.IsTrue(LevelCatalog.TryLoadLevel("L004", out var level, out _));
            var board = new Board(level);
            var history = new CommandHistory();

            // Shuttle the first block that can slide at the start between its start cell and a neighbour.
            int block = -1, startX = 0, startY = 0, otherX = 0, otherY = 0;
            for (int b = 0; b < level.Blocks.Length && block < 0; b++)
            foreach (var axis in new[] { Axis.X, Axis.Y })
            {
                RuleEngine.GetSlideRange(board, b, axis, out int min, out int max);
                if (min == max) continue;
                block = b;
                startX = otherX = board.GetX(b);
                startY = otherY = board.GetY(b);
                int start = axis == Axis.X ? startX : startY, target = start < max ? start + 1 : start - 1;
                if (axis == Axis.X) otherX = target; else otherY = target;
                break;
            }
            Assert.GreaterOrEqual(block, 0);

            for (int i = 0; i < 10000; i++)
            {
                bool outbound = i % 2 == 0;
                Assert.IsTrue(RuleEngine.TryCreateCommand(board, block, outbound ? otherX : startX, outbound ? otherY : startY, -1, out var command));
                RuleEngine.Apply(board, command);
                history.Push(command);
            }

            var profile = SaveService.CreateNewProfile();
            board.WriteTo(profile.currentRun.boardState);
            history.CopyTo(profile.currentRun.history);
            profile.currentRun.attemptId = new string('a', 32);

            string directory = Path.Combine(Path.GetTempPath(), "LayerSlideF07_" + System.Guid.NewGuid().ToString("N"));
            try
            {
                var service = new SaveService(directory);
                var write = Stopwatch.StartNew();
                Assert.IsTrue(service.TryWrite(profile));
                write.Stop();

                var load = Stopwatch.StartNew();
                var loaded = new SaveService(directory).Load(out _);
                var restoredBoard = new Board(level);
                var restored = new CommandHistory();
                Assert.IsTrue(restored.TryRestore(restoredBoard, loaded.currentRun.history, out string error), error);
                load.Stop();

                Assert.AreEqual(10000, restored.Count);
                Assert.AreEqual(10000, restoredBoard.MoveCount);
                Assert.IsTrue(restoredBoard.Matches(loaded.currentRun.boardState));

                while (restored.Count > 0) RuleEngine.Revert(restoredBoard, restored.Pop());
                Assert.AreEqual(startX, restoredBoard.GetX(block));
                Assert.AreEqual(startY, restoredBoard.GetY(block));

                Assert.AreEqual(0, restoredBoard.MoveCount);

                UnityEngine.Debug.Log($"[F07] write {write.ElapsedMilliseconds} ms, load+replay {load.ElapsedMilliseconds} ms");
                Assert.Less(write.ElapsedMilliseconds, 250, "commit save budget");
                Assert.Less(load.ElapsedMilliseconds, 10000, "stress cold start budget");
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }
    }
}
