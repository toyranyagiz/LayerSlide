using System.Collections.Generic;
using LayerSlide.Core;
using NUnit.Framework;
using static LayerSlide.Tests.TestLevels;

namespace LayerSlide.Tests
{
    /// <summary>F1 rails, F2 keys, F3 plates and F4 phase gates (GDD 01 §12–13).</summary>
    public class MechanicsTests
    {
        static BlockDefinition Rail(BlockDefinition block, string axis)
        {
            block.moveAxis = axis;
            return block;
        }

        static BlockDefinition Carrier(BlockDefinition block, string key)
        {
            block.keyId = key;
            return block;
        }

        static GateDefinition Locked(GateDefinition gate, string key = "", params string[] plates)
        {
            gate.requiredKey = key;
            gate.requiredPlates = new List<string>(plates);
            return gate;
        }

        static Board WithPlates(int size, BlockDefinition[] blocks, GateDefinition[] gates, params PlateDefinition[] plates)
        {
            var def = Definition(size, blocks, gates);
            def.plates = new List<PlateDefinition>(plates);
            return new Board(Level.Create(def));
        }

        static Board Do(Board board, string block, int x, int y, string gate = null)
        {
            Assert.IsTrue(TryDo(board, block, x, y, gate), $"{block} -> {x},{y} {gate}");
            return board;
        }

        static bool TryDo(Board board, string block, int x, int y, string gate = null)
        {
            var level = board.Level;
            if (!RuleEngine.TryCreateCommand(board, level.IndexOfBlock(block), x, y, gate == null ? -1 : level.IndexOfGate(gate), out var command))
                return false;
            RuleEngine.Apply(board, command);
            return true;
        }

        // ---------------------------------------------------------------- F1 rails

        [Test]
        public void RailBlockOnlySlidesAlongItsAxis()
        {
            var board = Board(4, new[] { Rail(Block("A", 1, 1, 1, 2, "Red"), "X") }, new[] { Gate("R", "Left", 0, 4, "Red") });
            RuleEngine.GetSlideRange(board, 0, Axis.Y, out int min, out int max);
            Assert.AreEqual((1, 1), (min, max), "a long vertical block on an X rail cannot move vertically");
            Assert.IsFalse(TryDo(board, "A", 1, 0));
            Assert.IsTrue(TryDo(board, "A", 3, 1));
        }

        [Test]
        public void RailBlockCannotPeelAcrossItsAxis()
        {
            var board = Board(4, new[] { Rail(Block("A", 1, 0, 1, 1, "Red"), "X") },
                new[] { Gate("B", "Bottom", 0, 4, "Red"), Gate("R", "Left", 0, 4, "Red") });
            Assert.IsFalse(TryDo(board, "A", 1, 0, "B"), "in-place peel on the bottom edge needs the Y axis");
            Assert.AreEqual(GateCheck.WrongAxis, RuleEngine.DiagnoseExit(board, 0, 1, 0, GateSide.Bottom, out _));
            Assert.IsTrue(TryDo(board, "A", 0, 0, "R"));
        }

        [Test]
        public void DragOnRailIgnoresTheForbiddenComponent()
        {
            var board = Board(4, new[] { Rail(Block("A", 1, 1, 1, 1, "Red"), "X") }, new[] { Gate("R", "Left", 0, 4, "Red") });
            var drag = new DragResolver();
            drag.Begin(board, 0);
            drag.Update(1.05f, 1.6f);
            Assert.IsFalse(drag.AxisLocked);
            Assert.IsTrue(drag.ForbiddenAxisAttempt);
            drag.Update(1.2f, 1.9f); // diagonal: the allowed component passes 0.15
            Assert.IsTrue(drag.AxisLocked);
            Assert.AreEqual(Axis.X, drag.Axis);
        }

        // ---------------------------------------------------------------- F2 keys

        [Test]
        public void KeyIsCollectedOnlyWhenTheCarrierIsGone()
        {
            var board = Board(4,
                new[] { Carrier(Block("A", 0, 1, 1, 1, "Red", "Yellow"), "K1"), Block("B", 3, 2, 1, 1, "Blue") },
                new[] { Gate("R", "Left", 0, 2, "Red"), Gate("Y", "Left", 2, 2, "Yellow"), Locked(Gate("B", "Right", 2, 1, "Blue"), "K1") });
            Assert.AreEqual(GateCheck.KeyRequired, RuleEngine.CheckGate(board, 1, 3, 2, 2));

            Do(board, "A", 0, 1, "R");
            Assert.IsFalse(board.HasKey(0), "peeling the top layer only does not give the key");
            Do(board, "A", 0, 2);
            Do(board, "A", 0, 2, "Y");
            Assert.IsTrue(board.HasKey(0));
            Assert.IsTrue(TryDo(board, "B", 3, 2, "B"));
        }

        [Test]
        public void UndoPutsTheKeyBackAndRelocksTheGate()
        {
            var board = Board(4,
                new[] { Carrier(Block("A", 0, 1, 1, 1, "Red"), "K1"), Block("B", 3, 2, 1, 1, "Blue") },
                new[] { Gate("R", "Left", 0, 4, "Red"), Locked(Gate("B", "Right", 2, 1, "Blue"), "K1") });
            RuleEngine.TryCreateCommand(board, 0, 0, 1, 0, out var take);
            RuleEngine.Apply(board, take);
            Assert.AreEqual(GateCheck.Ok, RuleEngine.CheckGate(board, 1, 3, 2, 1));
            RuleEngine.Revert(board, take);
            Assert.AreEqual(GateCheck.KeyRequired, RuleEngine.CheckGate(board, 1, 3, 2, 1));
        }

        // ---------------------------------------------------------------- F3 plates

        [Test]
        public void PlateMustBeHeldByAnotherBlock()
        {
            var gates = new[] { Locked(Gate("R", "Left", 0, 5, "Red"), "", "P1"), Gate("Y", "Right", 0, 5, "Yellow") };

            // A standing on the plate cannot open its own exit.
            var self = WithPlates(5, new[] { Block("A", 0, 2, 1, 1, "Red"), Block("B", 3, 3, 1, 1, "Yellow") }, gates,
                new PlateDefinition { id = "P1", x = 0, y = 0 });
            Do(self, "A", 0, 0);
            Assert.AreEqual(0, self.GetPlateHolder(0));
            Assert.AreEqual(GateCheck.PlatesRequired, RuleEngine.DiagnoseExit(self, 0, 0, 0, GateSide.Left, out _));
            Assert.IsFalse(TryDo(self, "A", 0, 0, "R"));

            // B holds the plate: A may leave.
            var plate = new PlateDefinition { id = "P1", x = 1, y = 0 };
            var held = WithPlates(5, new[] { Block("A", 0, 2, 1, 1, "Red"), Block("B", 1, 1, 1, 1, "Yellow") }, gates, plate);
            Assert.IsFalse(TryDo(held, "A", 0, 2, "R"));
            Do(held, "B", 1, 0);
            Assert.IsTrue(TryDo(held, "A", 0, 2, "R"));

            // Once the holder is gone the gate closes again.
            var released = WithPlates(5,
                new[] { Block("A", 0, 2, 1, 1, "Red"), Block("B", 1, 1, 1, 1, "Yellow"), Block("C", 0, 4, 1, 1, "Red") }, gates, plate);
            Do(released, "B", 1, 0);
            Do(released, "B", 4, 0, "Y");
            Assert.IsFalse(TryDo(released, "A", 0, 2, "R"));
        }

        [Test]
        public void PlatesMustNotStartCoveredOrTouch()
        {
            var def = Definition(5, new[] { Block("A", 1, 0, 1, 1, "Red") }, new[] { Gate("R", "Left", 0, 5, "Red") });
            def.plates = new List<PlateDefinition> { new PlateDefinition { id = "P1", x = 1, y = 0 }, new PlateDefinition { id = "P2", x = 2, y = 0 } };
            var errors = new List<string>();
            Assert.IsFalse(LevelValidator.Validate(def, errors));
            Assert.AreEqual(2, errors.Count, string.Join("\n", errors));
        }

        // ---------------------------------------------------------------- F4 phase gate

        [Test]
        public void PhaseFlipsOnEveryPeelAndChecksTheColorBeforeTheCommand()
        {
            var phase = Gate("B", "Bottom", 0, 5, "Blue");
            phase.cycleColors = new List<string> { "Blue", "Yellow" };
            var board = Board(5,
                new[] { Block("A", 0, 2, 1, 1, "Red"), Block("C", 2, 2, 1, 1, "Yellow"), Block("D", 3, 2, 1, 1, "Blue") },
                new[] { Gate("R", "Left", 0, 5, "Red"), phase });
            int gate = board.Level.IndexOfGate("B");

            Assert.AreEqual(LayerColor.Blue, RuleEngine.GetGateColor(board, gate));
            Assert.IsFalse(TryDo(board, "C", 2, 0, "B"), "phase 0 accepts blue only");
            Do(board, "C", 2, 0); // a plain slide never changes the phase
            Assert.AreEqual(0, board.Phase);

            Do(board, "A", 0, 2, "R");
            Assert.AreEqual(1, board.Phase);
            Assert.AreEqual(LayerColor.Yellow, RuleEngine.GetGateColor(board, gate));
            Do(board, "C", 2, 0, "B");
            Assert.AreEqual(LayerColor.Blue, RuleEngine.GetGateColor(board, gate), "the yellow peel flipped it back");
            Do(board, "D", 3, 0, "B");
            Assert.IsTrue(board.IsCleared);
        }

        [Test]
        public void ValidatorRejectsABrokenCycle()
        {
            var gate = Gate("B", "Bottom", 0, 4, "Yellow");
            gate.cycleColors = new List<string> { "Blue", "Yellow" };
            var def = Definition(4, new[] { Block("A", 1, 1, 1, 1, "Blue") }, new[] { gate });
            var errors = new List<string>();
            Assert.IsFalse(LevelValidator.Validate(def, errors));
            StringAssert.Contains("cycleColors[0]", string.Join("\n", errors));
        }
    }
}
