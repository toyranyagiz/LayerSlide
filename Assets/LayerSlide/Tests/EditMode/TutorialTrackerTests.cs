using System.Collections.Generic;
using LayerSlide.Core;
using LayerSlide.Gameplay;
using LayerSlide.Services;
using NUnit.Framework;

namespace LayerSlide.Tests
{
    /// <summary>Tutorial life cycle (GDD 02 §5, X04, X05).</summary>
    public class TutorialTrackerTests
    {
        static (Level level, Board board) Load(string id)
        {
            Assert.IsTrue(LevelCatalog.TryLoadLevel(id, out var level, out _));
            return (level, new Board(level));
        }

        static CommandRecord Do(Board board, string block, int x, int y, string gate)
        {
            var level = board.Level;
            Assert.IsTrue(RuleEngine.TryCreateCommand(board, level.IndexOfBlock(block), x, y,
                gate == null ? -1 : level.IndexOfGate(gate), out var command));
            RuleEngine.Apply(board, command);
            return command;
        }

        [Test]
        public void X04_L003MessageFollowsTheBoardThroughUndo()
        {
            var (level, board) = Load("L003");
            var tracker = new TutorialTracker();
            tracker.Begin(level, new List<string>());
            Assert.AreEqual("Üstteki rengi kapıya götür.", tracker.GetMessage(board));

            var peel = Do(board, "A", 0, 1, "R");
            Assert.IsNull(tracker.OnCommand(board, peel), "one peel does not finish T03");
            Assert.AreEqual("Alt katman kaldı. Şimdi mavi kapıya götür.", tracker.GetMessage(board));

            RuleEngine.Revert(board, peel); // Undo / reload recompute from the board
            Assert.AreEqual("Üstteki rengi kapıya götür.", tracker.GetMessage(board));
        }

        [Test]
        public void X04_CompletedTutorialIsNotShownAgain()
        {
            var (level, board) = Load("L003");
            var tracker = new TutorialTracker();
            tracker.Begin(level, new List<string> { "T03_Layers" });
            Assert.IsNull(tracker.ActiveTag);
            Assert.IsNull(tracker.GetMessage(board));
        }

        [Test]
        public void X05_L004CompletesOnlyOnTheFirstYCommandAfterThePeel()
        {
            var (level, board) = Load("L004");
            var tracker = new TutorialTracker();
            tracker.Begin(level, new List<string>());

            Assert.IsNull(tracker.OnCommand(board, Do(board, "A", 0, 1, "R")), "the peel itself is along X");
            Assert.IsNull(tracker.OnCommand(board, Do(board, "A", 2, 1, null)), "horizontal move does not count");
            Assert.AreEqual("T04_Axis", tracker.OnCommand(board, Do(board, "A", 2, 0, "B")));
        }

        [Test]
        public void X05_WinningClosesAnOpenTutorial()
        {
            var (level, _) = Load("L006");
            var tracker = new TutorialTracker();
            tracker.Begin(level, new List<string>());
            Assert.AreEqual("T06_Park", tracker.OnLevelWon());
            Assert.IsNull(tracker.ActiveTag);
        }

        [Test]
        public void L001DragHandOnlyWhileT01IsOpen()
        {
            var (level, board) = Load("L001");
            var tracker = new TutorialTracker();
            tracker.Begin(level, new List<string>());
            Assert.IsTrue(tracker.ShowsDragHand);
            Assert.AreEqual("T01_Drag", tracker.OnCommand(board, Do(board, "A", 0, 1, "R")));
            Assert.IsFalse(tracker.ShowsDragHand);
        }
    }
}
