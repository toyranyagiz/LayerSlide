using System.Collections.Generic;
using LayerSlide.Core;
using LayerSlide.Gameplay;
using LayerSlide.Services;
using NUnit.Framework;

namespace LayerSlide.Tests
{
    /// <summary>Tutorial life cycle for the campaign 2 tags (GDD 02 §4).</summary>
    public class TutorialTrackerTests
    {
        static (Level level, Board board) Load(string id)
        {
            Assert.IsTrue(LevelCatalog.TryLoadLevel(id, out var level, out _));
            return (level, new Board(level));
        }

        static CommandRecord Do(Board board, List<CommandRecord> history, string block, int x, int y, string gate)
        {
            var level = board.Level;
            Assert.IsTrue(RuleEngine.TryCreateCommand(board, level.IndexOfBlock(block), x, y,
                string.IsNullOrEmpty(gate) ? -1 : level.IndexOfGate(gate), out var command), $"{block} -> {x},{y} {gate}");
            RuleEngine.Apply(board, command);
            history.Add(command);
            return command;
        }

        /// <summary>Plays the recorded solution and returns the 1-based command index at which <paramref name="tag"/> completed, or 0.</summary>
        static int CompletionStep(string levelId, string tag)
        {
            var (level, board) = Load(levelId);
            var tracker = new TutorialTracker();
            tracker.Begin(level, new List<string>());
            var history = new List<CommandRecord>();
            var actions = LevelCatalog.LoadSolution(levelId).actions;
            for (int i = 0; i < actions.Count; i++)
            {
                var a = actions[i];
                var command = Do(board, history, a.block, a.x, a.y, a.exit);
                if (new List<string>(tracker.OnCommand(board, command, history)).Contains(tag)) return i + 1;
            }
            return 0;
        }

        [Test]
        public void L001ShowsRailThenKeyThenLayerSteps()
        {
            var (level, board) = Load("L001");
            var tracker = new TutorialTracker();
            tracker.Begin(level, new List<string>());
            var history = new List<CommandRecord>();
            Assert.AreEqual(TutorialTracker.RailTag, tracker.ActiveTag);
            Assert.IsTrue(tracker.ShowsGhostArrow(0));

            // Parking the free block C does not teach the rail.
            CollectionAssert.IsEmpty(tracker.OnCommand(board, Do(board, history, "C", 0, 0, ""), history));
            Assert.IsFalse(tracker.ShowsGhostArrow(history.Count));

            // A (X rail) peels: rail done; its last layer gives K1.
            CollectionAssert.AreEqual(new[] { TutorialTracker.RailTag }, tracker.OnCommand(board, Do(board, history, "A", 0, 1, "R"), history));
            Assert.AreEqual("Şimdi kilidi açılan kapıyı kullan.", tracker.GetMessage(board));

            CollectionAssert.AreEqual(new[] { TutorialTracker.KeyTag }, tracker.OnCommand(board, Do(board, history, "B", 1, 0, "B"), history));
            Assert.AreEqual("Üst renk çıkar; alttaki renk sıradadır.", tracker.GetMessage(board));

            tracker.OnCommand(board, Do(board, history, "C", 0, 1, ""), history);
            var peel = Do(board, history, "C", 3, 1, "Y");
            CollectionAssert.IsEmpty(tracker.OnCommand(board, peel, history));
            Assert.AreEqual("Blok kaldı. Şimdi alttan çıkan rengi eşleştir.", tracker.GetMessage(board));

            RuleEngine.Revert(board, peel); // Undo / reload recompute the message from the board
            Assert.AreEqual("Üst renk çıkar; alttaki renk sıradadır.", tracker.GetMessage(board));
        }

        [Test]
        public void KeyMessageAsksForTheCarrierUntilK1IsCollected()
        {
            var (level, board) = Load("L001");
            var tracker = new TutorialTracker();
            tracker.Begin(level, new List<string> { TutorialTracker.RailTag });
            Assert.AreEqual("Anahtarlı bloğun bütün katmanlarını çıkar.", tracker.GetMessage(board));
        }

        [Test]
        public void CompletedTutorialIsNotShownAgain()
        {
            var (level, board) = Load("L001");
            var tracker = new TutorialTracker();
            tracker.Begin(level, new List<string> { TutorialTracker.RailTag, TutorialTracker.KeyTag, TutorialTracker.LayersTag });
            Assert.IsNull(tracker.ActiveTag);
            Assert.IsNull(tracker.GetMessage(board));
            Assert.IsFalse(tracker.ShowsGhostArrow(0));
        }

        [Test]
        public void WinningClosesEveryOpenTag()
        {
            var (level, _) = Load("L001");
            var tracker = new TutorialTracker();
            tracker.Begin(level, new List<string>());
            Assert.AreEqual(3, tracker.OnLevelWon().Count);
            Assert.IsNull(tracker.ActiveTag);
        }

        [Test]
        public void PlateTagCompletesOnThePlateGatePeel()
        {
            int step = CompletionStep("L016", TutorialTracker.PlateTag);
            Assert.Greater(step, 0);

            var (level, _) = Load("L016");
            var action = LevelCatalog.LoadSolution("L016").actions[step - 1];
            Assert.Greater(level.Gates[level.IndexOfGate(action.exit)].RequiredPlates.Length, 0);
        }

        [Test]
        public void PhaseTagNeedsPeelsInBothPhases()
        {
            int step = CompletionStep("L031", TutorialTracker.PhaseTag);
            Assert.Greater(step, 0);

            // Replaying only up to the step before must leave it open.
            var (level, board) = Load("L031");
            var tracker = new TutorialTracker();
            tracker.Begin(level, new List<string>());
            var history = new List<CommandRecord>();
            var actions = LevelCatalog.LoadSolution("L031").actions;
            for (int i = 0; i < step - 1; i++)
            {
                var a = actions[i];
                tracker.OnCommand(board, Do(board, history, a.block, a.x, a.y, a.exit), history);
            }
            Assert.AreEqual(TutorialTracker.PhaseTag, tracker.ActiveTag);
        }
    }
}
