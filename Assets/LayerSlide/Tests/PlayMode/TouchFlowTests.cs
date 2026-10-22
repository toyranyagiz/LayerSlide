using System.Collections;
using System.IO;
using System.Linq;
using LayerSlide.Core;
using LayerSlide.Gameplay;
using LayerSlide.Presentation;
using LayerSlide.Services;
using LayerSlide.UI;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace LayerSlide.Tests
{
    /// <summary>
    /// Real-scene tests driven by simulated touchscreen events, so every move goes through PointerReader,
    /// picking, DragResolver and the commit path exactly like a finger on a phone.
    /// </summary>
    public class TouchFlowTests : InputTestFixture
    {
        static readonly string[] SaveFiles = { "save.json", "save.bak", "save.tmp" };

        GameController _game;
        UIController _ui;
        BoardPresenter _presenter;
        Camera _camera;

        // Every hold gets a fresh touch id (as Android does), so a stale Ended slot never shares an id with a new touch.
        int _nextTouchId = 100;

        public override void Setup()
        {
            base.Setup();
            // Batch-mode editors have no focused Game view: route all device input to the game regardless of focus,
            // as a phone in the foreground would.
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            // Read-value caching reports false "outdated cache" errors when the fixture updates input mid-frame.
            InputSystem.settings.SetInternalFeatureFlag("USE_READ_VALUE_CACHING", false);
            DeleteSave();
            InputSystem.AddDevice<Touchscreen>();
        }

        public override void TearDown()
        {
            DeleteSave();
            base.TearDown();
        }

        static void DeleteSave()
        {
            foreach (var name in SaveFiles)
            {
                string path = Path.Combine(Application.persistentDataPath, name);
                if (File.Exists(path)) File.Delete(path);
            }
        }

        // ================================================================ Helpers

        IEnumerator LoadGameplay()
        {
            SceneManager.LoadScene("Gameplay");
            yield return null;
            yield return WaitFor(() => (_game = Object.FindFirstObjectByType<GameController>()) != null && _game.Board != null || IsHome());
            _ui = Object.FindFirstObjectByType<UIController>();
            _presenter = Object.FindFirstObjectByType<BoardPresenter>(FindObjectsInactive.Include);
            _camera = GameObject.Find("BoardCamera").GetComponent<Camera>();
            yield return null;
        }

        bool IsHome() => Object.FindFirstObjectByType<UIController>() is UIController ui && ui.Home != null && ui.CurrentScreen == AppScreen.Home;

        static IEnumerator WaitFor(System.Func<bool> condition, float seconds = 5f)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("Timed out.");
                yield return null;
            }
        }

        IEnumerator WaitIdle() => WaitFor(() => _game.AcceptsBoardInput || _ui.Modal.Current == ModalId.Won);

        Vector2 ScreenOf(Vector3 world) => _camera.WorldToScreenPoint(world);

        static Vector3 PlaneHit(Ray ray) => ray.origin + ray.direction * (-ray.origin.y / ray.direction.y);

        /// <summary>Touches the top of a block and drags it by a world-space delta in several frames.</summary>
        IEnumerator Drag(int block, Vector3 delta, bool release = true, int steps = 8)
        {
            var board = _game.Board;
            var center = _presenter.BlockCenter(block, board.GetX(block), board.GetY(block));
            Vector2 start = ScreenOf(center + Vector3.up * 0.2f);
            var hit = PlaneHit(_camera.ScreenPointToRay(start));
            int id = _nextTouchId++;

            BeginTouch(id, start);
            yield return null;
            yield return null;
            Vector2 position = start;
            for (int i = 1; i <= steps; i++)
            {
                position = ScreenOf(hit + delta * i / steps);
                MoveTouch(id, position);
                yield return null;
            }
            if (!release) yield break;
            EndTouch(id, position);
            yield return null;
            yield return null;
        }

        /// <summary>Plays one recorded solution action with a real touch drag (exit = pull 0.5 cell past the gate).</summary>
        IEnumerator PlayAction(SolutionAction action)
        {
            var board = _game.Board;
            var level = board.Level;
            int block = level.IndexOfBlock(action.block);
            var delta = _presenter.BlockCenter(block, action.x, action.y) - _presenter.BlockCenter(block, board.GetX(block), board.GetY(block));
            if (!string.IsNullOrEmpty(action.exit))
            {
                switch (level.Gates[level.IndexOfGate(action.exit)].Side)
                {
                    case GateSide.Left: delta += Vector3.left * 0.5f; break;
                    case GateSide.Right: delta += Vector3.right * 0.5f; break;
                    case GateSide.Bottom: delta += Vector3.back * 0.5f; break;
                    default: delta += Vector3.forward * 0.5f; break;
                }
            }
            yield return Drag(block, delta);
            yield return WaitIdle();
        }

        IEnumerator PlayLevel(string levelId)
        {
            Assert.AreEqual(levelId, _game.Board.Level.LevelId);
            var actions = LevelCatalog.LoadSolution(levelId).actions;
            for (int i = 0; i < actions.Count; i++)
            {
                yield return PlayAction(actions[i]);
                Assert.AreEqual(i + 1, _game.Board.MoveCount, $"{levelId} action {i + 1} ({actions[i].block} -> {actions[i].x},{actions[i].y} '{actions[i].exit}') did not commit");
            }
            yield return WaitFor(() => _ui.Modal.Current == ModalId.Won);
        }

        void ClickModalButton(string label)
        {
            var button = Object.FindObjectsByType<Button>(FindObjectsSortMode.None)
                .FirstOrDefault(b => b.GetComponentInChildren<TextMeshProUGUI>()?.text == label && b.isActiveAndEnabled);
            Assert.IsNotNull(button, $"button '{label}' not found");
            button.onClick.Invoke();
        }

        static PlayerSave ReadSave() => new SaveService(Application.persistentDataPath).Load(out _);

        // ================================================================ Tests

        [UnityTest]
        public IEnumerator R04_SecondFingerNeverTakesOver()
        {
            yield return LoadGameplay();
            var board = _game.Board;
            Vector2 emptyCell = ScreenOf(_presenter.CellCenter(3, 3));
            BeginTouch(10, emptyCell); // first finger owns the hold but is not on a block
            yield return null;
            yield return null;

            yield return Drag(0, Vector3.left * 1.5f); // second finger drags the block out of the gate
            Assert.AreEqual(0, board.MoveCount, "a second finger is ignored");

            EndTouch(10, emptyCell);
            yield return null;
            yield return null;
            yield return Drag(0, Vector3.left * 1.5f); // a new press works
            yield return WaitIdle();
            Assert.AreEqual(1, board.MoveCount);
        }

        [UnityTest]
        public IEnumerator X02_DragEndingOverTheHudOnlyCommitsTheBoardMove()
        {
            yield return LoadGameplay();
            var board = _game.Board;
            var center = _presenter.BlockCenter(0, board.GetX(0), board.GetY(0));
            Vector2 start = ScreenOf(center + Vector3.up * 0.2f);
            var hit = PlaneHit(_camera.ScreenPointToRay(start));

            BeginTouch(20, start);
            yield return null;
            yield return null;
            MoveTouch(20, ScreenOf(hit + Vector3.left * 1.5f)); // locks X and arms the red gate
            yield return null;
            var boardRect = _ui.GetBoardRectPixels();
            Vector2 overUndo = new Vector2(boardRect.xMin + 10f, boardRect.yMin * 0.5f); // middle of the bottom bar, over "Geri al"
            MoveTouch(20, new Vector2(overUndo.x, overUndo.y));
            yield return null;
            EndTouch(20, overUndo);
            yield return null;
            yield return WaitFor(() => _ui.Modal.Current == ModalId.Won);
            Assert.AreEqual(1, board.MoveCount, "the HUD button under the finger did not run");
        }

        [UnityTest]
        public IEnumerator S03_X03_FocusLossDuringDragCancelsWithoutACommand()
        {
            yield return LoadGameplay();
            var board = _game.Board;
            yield return Drag(0, Vector3.left * 0.7f, release: false);
            _game.SendMessage("OnApplicationFocus", false);
            EndTouch(_nextTouchId - 1, ScreenOf(_presenter.CellCenter(0, 1))); // up arrives in the same frame
            yield return null;
            yield return null;
            Assert.AreEqual(0, board.MoveCount);
            Assert.AreEqual(1, board.GetX(0), "the hold start is kept");

            _game.SendMessage("OnApplicationFocus", true);
            yield return null;
            Assert.AreEqual(ModalId.Pause, _ui.Modal.Current, "returning shows Pause");
        }

        [UnityTest]
        public IEnumerator R13_WinIsRecordedOnceEvenWithRepeatedCallbacks()
        {
            yield return LoadGameplay();
            int wins = 0;
            _game.LevelCompleted += _ => wins++;
            yield return Drag(0, Vector3.left * 1.5f);
            _game.SendMessage("OnApplicationFocus", false); // during Resolving: jumps to the final state
            _game.SendMessage("OnApplicationFocus", false);
            _game.SendMessage("OnApplicationFocus", true);
            yield return new WaitForSecondsRealtime(0.5f);
            Assert.AreEqual(1, wins);
            Assert.AreEqual(ModalId.Won, _ui.Modal.Current);
            CollectionAssert.AreEqual(new[] { "L001" }, ReadSave().completedLevelIds);
        }

        [UnityTest]
        public IEnumerator S01_S02_RunAndUndoSurviveARestartOfTheApp()
        {
            yield return LoadGameplay();
            yield return PlayLevel("L001");
            ClickModalButton("Sonraki");
            yield return WaitIdle();
            yield return PlayAction(LevelCatalog.LoadSolution("L002").actions[0]);
            Assert.AreEqual(1, _game.Board.MoveCount);

            yield return LoadGameplay(); // process restart: profile is read from disk again
            Assert.AreEqual(AppScreen.Home, _ui.CurrentScreen);
            _game.OnContinuePressed();
            yield return WaitIdle();
            Assert.AreEqual("L002", _game.Board.Level.LevelId);
            Assert.AreEqual(1, _game.Board.MoveCount);

            _game.OnUndoPressed();
            yield return WaitIdle();
            yield return LoadGameplay();
            _game.OnContinuePressed();
            yield return WaitIdle();
            Assert.AreEqual(0, _game.Board.MoveCount, "the undone state is what was saved");
            Assert.AreEqual(1, ReadSave().currentRun.undoCount);
        }

        [UnityTest]
        public IEnumerator S04_ClosingDuringTheLastPeelKeepsTheWin()
        {
            yield return LoadGameplay();
            yield return Drag(0, Vector3.left * 1.5f);
            yield return LoadGameplay(); // killed while Resolving
            CollectionAssert.AreEqual(new[] { "L001" }, ReadSave().completedLevelIds);
        }

        [UnityTest]
        public IEnumerator S08_S09_BrokenRunIsResetButProgressAndSettingsStay()
        {
            yield return LoadGameplay();
            yield return PlayLevel("L001");
            ClickModalButton("Sonraki");
            yield return WaitIdle();
            yield return PlayAction(LevelCatalog.LoadSolution("L002").actions[0]);

            foreach (var tamper in new System.Action<PlayerSave>[]
                     {
                         save => save.currentRun.boardState.contentVersion = 2,   // S08
                         save => save.currentRun.history[0].afterX = 3            // S09
                     })
            {
                var service = new SaveService(Application.persistentDataPath);
                var save = service.Load(out _);
                if (save.currentRun.history.Count == 0)
                {
                    // Re-create one move for the second scenario.
                    yield return LoadGameplay();
                    _game.OnContinuePressed();
                    yield return WaitIdle();
                    yield return PlayAction(LevelCatalog.LoadSolution("L002").actions[0]);
                    save = service.Load(out _);
                }
                save.settings.sfxVolume = 33;
                tamper(save);
                Assert.IsTrue(service.TryWrite(save));

                yield return LoadGameplay();
                _game.OnContinuePressed();
                yield return WaitIdle();
                Assert.AreEqual("L002", _game.Board.Level.LevelId, "same level restarts");
                Assert.AreEqual(0, _game.Board.MoveCount, "only the run was reset");
                var after = ReadSave();
                CollectionAssert.Contains(after.completedLevelIds, "L001");
                Assert.AreEqual(33, after.settings.sfxVolume);
            }
        }

        [UnityTest]
        public IEnumerator X08_MuteRemembersTheLastVolumeAcrossRestarts()
        {
            yield return LoadGameplay();
            _game.OnVolumeChanged(37, commit: true);
            _game.OnMutePressed();
            Assert.AreEqual(0, ReadSave().settings.sfxVolume);

            yield return LoadGameplay();
            _game.OnMutePressed();
            var settings = ReadSave().settings;
            Assert.AreEqual(37, settings.sfxVolume);
            Assert.AreEqual(37, settings.lastNonZeroSfxVolume);
        }

        [UnityTest]
        public IEnumerator X06_SettingsReturnsToWhereItWasOpened()
        {
            yield return LoadGameplay();
            _game.OnMenuPressed();
            Assert.AreEqual(ModalId.Pause, _ui.Modal.Current);
            ClickModalButton("Ayarlar");
            Assert.AreEqual(AppScreen.Settings, _ui.CurrentScreen);
            _game.OnBackPressed();
            Assert.AreEqual(AppScreen.Playing, _ui.CurrentScreen);
            Assert.AreEqual(ModalId.Pause, _ui.Modal.Current);

            ClickModalButton("Ana menü");
            Assert.AreEqual(AppScreen.Home, _ui.CurrentScreen);
            _game.OnSettingsPressed();
            _game.OnAboutPressed();
            Assert.AreEqual(AppScreen.About, _ui.CurrentScreen);
            _game.OnBackPressed();
            Assert.AreEqual(AppScreen.Settings, _ui.CurrentScreen);
            _game.OnBackPressed();
            Assert.AreEqual(AppScreen.Home, _ui.CurrentScreen);
            yield break;
        }

        [UnityTest]
        public IEnumerator P02_P07_LockedLevelsAndLeaveRunConfirmation()
        {
            yield return LoadGameplay();
            _game.OnLevelsPressed();
            _game.OnLevelSelected("L005");
            Assert.AreEqual(AppScreen.LevelSelect, _ui.CurrentScreen, "locked level does not open");

            _game.OnLevelSelected("L001");
            yield return WaitIdle();
            yield return PlayLevel("L001");
            ClickModalButton("Sonraki");
            yield return WaitIdle();
            yield return PlayAction(LevelCatalog.LoadSolution("L002").actions[0]);

            _game.OnLevelsPressed();
            _game.OnLevelSelected("L001");
            Assert.AreEqual(ModalId.LeaveRunConfirm, _ui.Modal.Current);
            ClickModalButton("Vazgeç");
            Assert.AreEqual("L002", ReadSave().currentRun.boardState.levelId);
            Assert.AreEqual(1, ReadSave().currentRun.history.Count, "cancel keeps the run");

            _game.OnLevelSelected("L001");
            ClickModalButton("Diğer bölümü aç");
            yield return WaitIdle();
            Assert.AreEqual("L001", _game.Board.Level.LevelId);
            Assert.AreEqual(0, _game.Board.MoveCount, "replay starts fresh");
            CollectionAssert.Contains(ReadSave().completedLevelIds, "L001", "replay keeps the completed mark");
        }
    }
}
