using System.Collections.Generic;
using LayerSlide.Core;
using LayerSlide.Services;
using NUnit.Framework;

namespace LayerSlide.Tests
{
    /// <summary>Content gate (GDD 02 §8, 03 §4): every shipped level validates and its recorded solution clears it.</summary>
    public class LevelContentTests
    {
        static IEnumerable<string> LevelIds => LevelCatalog.LevelIds;

        [TestCaseSource(nameof(LevelIds))]
        public void LevelIsValidAndRecordedSolutionClearsIt(string levelId)
        {
            Assert.IsTrue(LevelCatalog.TryLoadLevel(levelId, out var level, out string error), error);
            Assert.AreEqual(4, level.Width, "L001–L015 are 4x4");
            Assert.IsTrue(SolutionReplayer.Replay(level, LevelCatalog.LoadSolution(levelId), out error), error);
        }

        [TestCaseSource(nameof(LevelIds))]
        public void HintSolverFindsAValidSolutionFromTheStart(string levelId)
        {
            Assert.IsTrue(LevelCatalog.TryLoadLevel(levelId, out var level, out _));
            var board = new Board(level);
            var solver = new HintSolver { MaxStates = 250000 };
            solver.Begin(board);
            while (solver.Step(long.MaxValue) == SolverStatus.Running) { }

            Assert.AreEqual(SolverStatus.Found, solver.Status);
            foreach (var action in solver.Path)
            {
                Assert.IsTrue(RuleEngine.TryCreateCommand(board, action.Block, action.X, action.Y, action.Gate, out var command));
                RuleEngine.Apply(board, command);
            }
            Assert.IsTrue(board.IsCleared);
        }

        [Test]
        public void RecordedActionTotalMatchesTheGdd()
        {
            // L001–L010 in GDD 02 §3: 1+2+2+3+4+8+4+5+18+11.
            int total = 0;
            foreach (var id in LevelCatalog.LevelIds)
                total += LevelCatalog.LoadSolution(id).actions.Count;
            Assert.AreEqual(58, total);
        }

        [Test]
        public void SolverReportsExhaustedForAWalledInBlock()
        {
            var walls = new[]
            {
                new CellData { x = 0, y = 1 }, new CellData { x = 2, y = 1 },
                new CellData { x = 1, y = 0 }, new CellData { x = 1, y = 2 }
            };
            var board = TestLevels.Board(4,
                new[] { TestLevels.Block("A", 1, 1, 1, 1, "Red") },
                new[] { TestLevels.Gate("R", "Left", 1, 1, "Red") }, walls);

            var solver = new HintSolver();
            solver.Begin(board);
            while (solver.Step(long.MaxValue) == SolverStatus.Running) { }
            Assert.AreEqual(SolverStatus.Exhausted, solver.Status);
        }

        [Test]
        public void SolverStopsAtTheStateBudget()
        {
            Assert.IsTrue(LevelCatalog.TryLoadLevel("L009", out var level, out _));
            var solver = new HintSolver { MaxStates = 5 };
            solver.Begin(new Board(level));
            while (solver.Step(long.MaxValue) == SolverStatus.Running) { }
            Assert.AreEqual(SolverStatus.BudgetExceeded, solver.Status);
            Assert.LessOrEqual(solver.DiscoveredStates, 5);
        }
    }
}
