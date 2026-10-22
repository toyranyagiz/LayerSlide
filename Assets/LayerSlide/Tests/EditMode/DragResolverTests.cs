using LayerSlide.Core;
using NUnit.Framework;
using static LayerSlide.Tests.TestLevels;

namespace LayerSlide.Tests
{
    /// <summary>Input rules from GDD 01 §3–6 with the threshold vectors of GDD 08 §17.</summary>
    public class DragResolverTests
    {
        // A at (1,1); red gate on the left edge rows 0..3; blue gate on the right.
        static Board OpenBoard() => Board(4, new[] { Block("A", 1, 1, 1, 1, "Red", "Blue") },
            new[] { Gate("R", "Left", 0, 4, "Red"), Gate("B", "Right", 0, 4, "Blue") });

        static DragResolver Begin(Board board)
        {
            var drag = new DragResolver();
            drag.Begin(board, 0);
            return drag;
        }

        [TestCase(0.14999f, false)]
        [TestCase(0.15f, true)]
        [TestCase(0.15001f, true)]
        public void R03_AxisLocksAtPointFifteen(float distance, bool locked)
        {
            var drag = Begin(OpenBoard());
            drag.Update(1f + distance, 1f);
            Assert.AreEqual(locked, drag.AxisLocked);
        }

        [Test]
        public void R03_ExactDiagonalPicksXAndKeepsY()
        {
            var drag = Begin(OpenBoard());
            drag.Update(1.2f, 1.2f);
            Assert.IsTrue(drag.AxisLocked);
            Assert.AreEqual(Axis.X, drag.Axis);
            drag.Update(1.6f, 2.9f);
            Assert.AreEqual(1f, drag.PreviewY, "the other axis never moves");
        }

        [Test]
        public void R03_AxisStaysLockedUntilRelease()
        {
            var drag = Begin(OpenBoard());
            drag.Update(1f, 1.3f);
            Assert.AreEqual(Axis.Y, drag.Axis);
            drag.Update(3f, 1.3f);
            Assert.AreEqual(Axis.Y, drag.Axis);
            Assert.AreEqual(1f, drag.PreviewX);
        }

        [Test]
        public void R05_HalfCellRoundsAwayFromStart()
        {
            Assert.AreEqual(2, DragResolver.Snap(1.5f, 1, 0, 3));
            Assert.AreEqual(1, DragResolver.Snap(1.5f, 2, 0, 3));
            Assert.AreEqual(1, DragResolver.Snap(1.49f, 2, 0, 3), "off the exact half, plain nearest cell");
            Assert.AreEqual(2, DragResolver.Snap(1.51f, 1, 0, 3));
        }

        [Test]
        public void R05_ReturningWithinTheHoldStillUsesTheStartAsReference()
        {
            var drag = Begin(OpenBoard());
            drag.Update(2.7f, 1f);
            drag.Update(1.5f, 1f);
            Assert.IsTrue(drag.TryGetRelease(out int x, out _, out int gate));
            Assert.AreEqual(2, x);
            Assert.AreEqual(-1, gate);
        }

        [TestCase(0.24f, false)]
        [TestCase(0.24999f, false)]
        [TestCase(0.25f, true)]
        [TestCase(0.25001f, true)]
        public void R09_ExitIntentThreshold(float overshoot, bool armed)
        {
            var drag = Begin(OpenBoard());
            drag.Update(0f - overshoot, 1f);
            Assert.AreEqual(armed, drag.ArmedGate == 0);
            drag.TryGetRelease(out int x, out _, out int gate);
            Assert.AreEqual(0, x, "release lands on the last valid inner cell either way");
            Assert.AreEqual(armed ? 0 : -1, gate);
        }

        [Test]
        public void R10_PullingBackInDisarms()
        {
            var drag = Begin(OpenBoard());
            Assert.IsTrue(drag.Update(-0.4f, 1f), "arming is reported once");
            drag.Update(0.2f, 1f);
            Assert.AreEqual(-1, drag.ArmedGate);
            drag.TryGetRelease(out int x, out _, out int gate);
            Assert.AreEqual(0, x);
            Assert.AreEqual(-1, gate, "the inner move is kept, no peel");
        }

        [Test]
        public void WrongColorAtEdgeIsAnInvalidExitNotAPeel()
        {
            var drag = Begin(OpenBoard());
            drag.Update(3.5f, 1f); // right edge has the blue gate; active layer is red
            Assert.AreEqual(-1, drag.ArmedGate);
            Assert.IsTrue(drag.ExitIntentWithoutGate);
            Assert.AreEqual(GateSide.Right, drag.ExitSide);
        }

        [Test]
        public void BlockedEdgeCannotArmEvenWhenThePointerIsOutside()
        {
            var board = Board(4, new[] { Block("A", 2, 1, 1, 1, "Red"), Block("B", 0, 1, 1, 1, "Blue") },
                new[] { Gate("R", "Left", 0, 4, "Red"), Gate("B", "Bottom", 0, 4, "Blue") });
            var drag = new DragResolver();
            drag.Begin(board, 0);
            drag.Update(-2f, 1f);
            Assert.AreEqual(1, drag.Min, "B stops A at x = 1");
            Assert.AreEqual(-1, drag.ArmedGate);
            Assert.IsFalse(drag.ExitIntentWithoutGate, "not at the board edge, so not even an exit attempt");
        }

        [Test]
        public void BelowThresholdReleaseIsATap()
        {
            var drag = Begin(OpenBoard());
            drag.Update(1.1f, 1.05f);
            Assert.IsFalse(drag.TryGetRelease(out _, out _, out _));
        }
    }
}
