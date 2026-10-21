using System.Collections.Generic;
using LayerSlide.Core;
using NUnit.Framework;
using static LayerSlide.Tests.TestLevels;

namespace LayerSlide.Tests
{
    /// <summary>Rule tests from GDD 08 §2 that do not need real input (R01, R02, R06–R08, R11, R12, R14–R16, R18–R20).</summary>
    public class RuleEngineTests
    {
        static readonly GateDefinition[] RedLeft = { Gate("R", "Left", 0, 4, "Red") };

        [Test]
        public void R01_SlideStopsAtFirstObstacle()
        {
            var board = Board(4, new[] { Block("A", 0, 0, 1, 1, "Red"), Block("B", 2, 0, 1, 1, "Red") }, RedLeft);
            RuleEngine.GetSlideRange(board, 0, Axis.X, out int min, out int max);
            Assert.AreEqual(0, min);
            Assert.AreEqual(1, max);
            Assert.IsFalse(RuleEngine.TryCreateCommand(board, 0, 3, 0, -1, out _), "must not jump over B");
        }

        [Test]
        public void R02_LongBlocksMoveOnBothAxes()
        {
            var board = Board(4, new[] { Block("A", 1, 1, 2, 1, "Red"), Block("B", 1, 3, 1, 1, "Red") }, RedLeft);
            RuleEngine.GetSlideRange(board, 0, Axis.Y, out int minY, out int maxY);
            Assert.AreEqual(0, minY);
            Assert.AreEqual(2, maxY, "B blocks the left half of the top row");
            RuleEngine.GetSlideRange(board, 0, Axis.X, out int minX, out int maxX);
            Assert.AreEqual(0, minX);
            Assert.AreEqual(2, maxX);
        }

        [TestCase("Left", 1, 1, 0, 1)]
        [TestCase("Right", 1, 1, 3, 1)]
        [TestCase("Bottom", 1, 1, 1, 0)]
        [TestCase("Top", 1, 1, 1, 3)]
        public void R06_PeelOnEveryEdge(string side, int start, int length, int x, int y)
        {
            var board = Board(4, new[] { Block("A", 1, 1, 1, 1, "Red", "Blue") },
                new[] { Gate("G", side, start, length, "Red"), Gate("B", side == "Left" ? "Right" : "Left", 0, 4, "Blue") });
            Assert.IsTrue(RuleEngine.TryCreateCommand(board, 0, x, y, 0, out var command));
            RuleEngine.Apply(board, command);
            Assert.AreEqual(1, board.GetRemovedLayers(0));
            Assert.AreEqual(LayerColor.Blue, board.GetActiveColor(0));
        }

        [Test]
        public void R07_PartialCoverageDoesNotPeel()
        {
            var board = Board(4, new[] { Block("A", 0, 1, 1, 2, "Red") }, new[] { Gate("R", "Left", 1, 1, "Red"), Gate("R2", "Right", 0, 4, "Red") });
            Assert.AreEqual(-1, RuleEngine.FindExitGate(board, 0, 0, 1, GateSide.Left));
        }

        [Test]
        public void R08_WrongColorDoesNotPeel()
        {
            var board = Board(4, new[] { Block("A", 0, 1, 1, 1, "Blue") }, new[] { Gate("R", "Left", 1, 1, "Red"), Gate("B", "Right", 0, 4, "Blue") });
            Assert.AreEqual(-1, RuleEngine.FindExitGate(board, 0, 0, 1, GateSide.Left));
        }

        [Test]
        public void R11_FirstPeelKeepsFootprint()
        {
            var board = Board(4, new[] { Block("A", 0, 1, 2, 1, "Red", "Blue") }, new[] { Gate("R", "Left", 1, 1, "Red"), Gate("B", "Bottom", 0, 4, "Blue") });
            Assert.IsTrue(RuleEngine.TryCreateCommand(board, 0, 0, 1, 0, out var command));
            RuleEngine.Apply(board, command);
            Assert.AreEqual(0, board.GetX(0));
            Assert.AreEqual(1, board.GetY(0));
            Assert.AreEqual(0, board.GetCell(1, 1), "both cells stay occupied");
        }

        [Test]
        public void R12_LastLayerFreesAllCells()
        {
            var board = Board(4, new[] { Block("A", 0, 1, 2, 1, "Red") }, new[] { Gate("R", "Left", 1, 1, "Red") });
            Assert.IsTrue(RuleEngine.TryCreateCommand(board, 0, 0, 1, 0, out var command));
            RuleEngine.Apply(board, command);
            Assert.IsTrue(board.IsCleared);
            Assert.AreEqual(Core.Board.EmptyCell, board.GetCell(0, 1));
            Assert.AreEqual(Core.Board.EmptyCell, board.GetCell(1, 1));
            Assert.AreEqual(-1, command.afterX);
            Assert.AreEqual(0, command.visualEndX);
        }

        [Test]
        public void R14_R15_UndoRestoresSlidePeelAndRemovedBlock()
        {
            var board = Board(4, new[] { Block("A", 2, 1, 1, 1, "Red") }, new[] { Gate("R", "Left", 1, 1, "Red") });
            Assert.IsTrue(RuleEngine.TryCreateCommand(board, 0, 0, 1, 0, out var command));
            RuleEngine.Apply(board, command);
            Assert.IsTrue(board.IsCleared);

            RuleEngine.Revert(board, command);
            Assert.AreEqual(2, board.GetX(0));
            Assert.AreEqual(0, board.GetRemovedLayers(0));
            Assert.AreEqual(0, board.MoveCount);
            Assert.AreEqual(0, board.GetCell(2, 1));
        }

        [Test]
        public void R16_NoOpReleaseIsNotACommand()
        {
            var board = Board(4, new[] { Block("A", 2, 1, 1, 1, "Red") }, RedLeft);
            Assert.IsFalse(RuleEngine.TryCreateCommand(board, 0, 2, 1, -1, out _));
            Assert.AreEqual(0, board.MoveCount);
        }

        [Test]
        public void R18_ValidatorRejectsSameAdjacentColors()
        {
            var errors = new List<string>();
            var definition = Definition(4, new[] { Block("A", 0, 0, 1, 1, "Red", "Red") }, RedLeft);
            Assert.IsFalse(LevelValidator.Validate(definition, errors));
            StringAssert.Contains("adjacent", string.Join(" ", errors));
        }

        [Test]
        public void R19_AdjacentGatesDoNotCombine()
        {
            var board = Board(4, new[] { Block("A", 0, 1, 1, 2, "Red") },
                new[] { Gate("R1", "Left", 1, 1, "Red"), Gate("R2", "Left", 2, 1, "Red"), Gate("R3", "Right", 0, 4, "Red") });
            Assert.AreEqual(-1, RuleEngine.FindExitGate(board, 0, 0, 1, GateSide.Left));
        }

        [Test]
        public void R20_CornerOnlyUsesTheMovementAxisGate()
        {
            var board = Board(4, new[] { Block("A", 1, 0, 1, 1, "Red") },
                new[] { Gate("L", "Left", 0, 1, "Red"), Gate("D", "Bottom", 0, 1, "Red") });
            const int left = 0, bottom = 1;
            Assert.IsTrue(RuleEngine.TryCreateCommand(board, 0, 0, 0, left, out _));
            Assert.IsFalse(RuleEngine.TryCreateCommand(board, 0, 0, 0, bottom, out _), "moving along X cannot exit through the bottom gate");
        }

        [Test]
        public void CombinedCommandMustMoveTowardItsGate()
        {
            var board = Board(4, new[] { Block("A", 1, 1, 1, 1, "Red") }, new[] { Gate("R", "Right", 1, 1, "Red") });
            Assert.IsTrue(RuleEngine.TryCreateCommand(board, 0, 3, 1, 0, out _));
            Assert.IsFalse(RuleEngine.TryCreateCommand(board, 0, 1, 2, 0, out _));
        }

        [Test]
        public void HistoryRestoreRejectsTamperedRecords()
        {
            var board = Board(4, new[] { Block("A", 2, 1, 1, 1, "Red") }, new[] { Gate("R", "Left", 1, 1, "Red") });
            Assert.IsTrue(RuleEngine.TryCreateCommand(board, 0, 1, 1, -1, out var command));
            command.afterX = 3;

            var history = new CommandHistory();
            Assert.IsFalse(history.TryRestore(board, new[] { command }, out _));
            Assert.AreEqual(0, history.Count);
            Assert.AreEqual(2, board.GetX(0), "board is reset to the start");
        }
    }
}
