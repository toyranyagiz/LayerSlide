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
            int size = LevelCatalog.GetNumber(levelId) <= 15 ? 4 : 5;
            Assert.AreEqual(size, level.Width, "L001–L015 are 4x4, L016–L040 5x5");
            Assert.AreEqual(size, level.Height);
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

            // Every start is solvable, so the search may run out of budget (GDD 06 §8) but never report Exhausted.
            Assert.AreNotEqual(SolverStatus.Exhausted, solver.Status);
            if (solver.Status != SolverStatus.Found)
            {
                Assert.AreEqual(SolverStatus.BudgetExceeded, solver.Status);
                Assert.Ignore($"{levelId}: start position not solved within {solver.MaxStates} states; hints there come from the recorded solution.");
            }
            foreach (var action in solver.Path)
            {
                Assert.IsTrue(RuleEngine.TryCreateCommand(board, action.Block, action.X, action.Y, action.Gate, out var command));
                RuleEngine.Apply(board, command);
            }
            Assert.IsTrue(board.IsCleared);
        }

        [Test]
        public void RecordedTotalsMatchTheGdd()
        {
            // GDD 02 §5: 40 levels, 996 commands of which 351 peel and 645 only slide.
            int total = 0, peels = 0;
            foreach (var id in LevelCatalog.LevelIds)
            {
                var actions = LevelCatalog.LoadSolution(id).actions;
                total += actions.Count;
                foreach (var action in actions)
                    if (!string.IsNullOrEmpty(action.exit)) peels++;
            }
            Assert.AreEqual(40, LevelCatalog.Count);
            Assert.AreEqual(996, total);
            Assert.AreEqual(351, peels);
        }

        [Test]
        public void MechanicsUnlockOnTheGddSchedule()
        {
            // F1 rails and F2 keys from L001, F3 plates from L016, F4 phase gates from L031 (GDD 00 §2).
            foreach (var id in LevelCatalog.LevelIds)
            {
                Assert.IsTrue(LevelCatalog.TryLoadLevel(id, out var level, out _));
                int number = LevelCatalog.GetNumber(id);
                bool rail = false;
                foreach (var block in level.Blocks) rail |= block.MoveAxis != MoveAxis.Free;
                Assert.IsTrue(rail, $"{id} has a rail block");
                Assert.AreEqual(number <= 8 ? 1 : 2, level.Keys.Length, $"{id} keys");
                Assert.AreEqual(number < 16 ? 0 : number < 24 || number == 31 || number == 32 ? 1 : 2, level.Plates.Length, $"{id} plates");
                Assert.AreEqual(number >= 31, level.HasPhaseGate, $"{id} phase gate");
            }
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
