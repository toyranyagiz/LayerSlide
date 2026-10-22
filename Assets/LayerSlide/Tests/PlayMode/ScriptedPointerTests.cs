using System.Collections;
using System.Collections.Generic;
using System.IO;
using LayerSlide.Core;
using LayerSlide.Gameplay;
using LayerSlide.Presentation;
using LayerSlide.Services;
using LayerSlide.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace LayerSlide.Tests
{
    /// <summary>
    /// Plays all ten levels through the real InputController with a frame-by-frame scripted pointer: screen-space
    /// picking, grab offset, axis lock, gate intent, snap and commit are all exercised. Unlike the Input System test
    /// fixture, the scripted source is deterministic for long sequences in a batch-mode editor.
    /// </summary>
    public class ScriptedPointerTests
    {
        static readonly string[] SaveFiles = { "save.json", "save.bak", "save.tmp" };

        /// <summary>One queued sample is returned per frame; an empty queue means "no pointer".</summary>
        sealed class ScriptedPointer : IPointerSource
        {
            readonly Queue<(PointerPhase phase, Vector2 position)> _frames = new Queue<(PointerPhase, Vector2)>();
            public bool HasOwner { get; private set; }
            public bool Idle => _frames.Count == 0;

            public void Enqueue(PointerPhase phase, Vector2 position) => _frames.Enqueue((phase, position));

            public PointerPhase Read(out Vector2 position)
            {
                position = default;
                if (_frames.Count == 0) return PointerPhase.None;
                var (phase, at) = _frames.Dequeue();
                position = at;
                HasOwner = phase == PointerPhase.Down || phase == PointerPhase.Move;
                return phase;
            }

            public void Release()
            {
                HasOwner = false;
                _frames.Clear();
            }
        }

        GameController _game;
        UIController _ui;
        BoardPresenter _presenter;
        Camera _camera;
        ScriptedPointer _pointer;

        [SetUp]
        public void DeleteSave()
        {
            foreach (var name in SaveFiles)
            {
                string path = Path.Combine(Application.persistentDataPath, name);
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [TearDown]
        public void Cleanup() => DeleteSave();

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

        /// <summary>Down on the block top, 8 move frames to the target (+0.5 cell past the gate on exit), up.</summary>
        IEnumerator PlayAction(SolutionAction action)
        {
            var board = _game.Board;
            var level = board.Level;
            int block = level.IndexOfBlock(action.block);
            var from = _presenter.BlockCenter(block, board.GetX(block), board.GetY(block));
            var delta = _presenter.BlockCenter(block, action.x, action.y) - from;
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

            Vector2 start = _camera.WorldToScreenPoint(from + Vector3.up * 0.2f);
            var ray = _camera.ScreenPointToRay(start);
            var hit = ray.origin + ray.direction * (-ray.origin.y / ray.direction.y);

            _pointer.Enqueue(PointerPhase.Down, start);
            Vector2 last = start;
            for (int i = 1; i <= 8; i++)
            {
                last = _camera.WorldToScreenPoint(hit + delta * i / 8);
                _pointer.Enqueue(PointerPhase.Move, last);
            }
            _pointer.Enqueue(PointerPhase.Up, last);
            yield return WaitFor(() => _pointer.Idle);
            yield return null;
            yield return WaitIdle();
        }

        [UnityTest]
        [Timeout(3600000)] // 996 real drags
        public IEnumerator AllLevelsCanBeSolvedThroughTheInputController()
        {
            SceneManager.LoadScene("Gameplay");
            yield return WaitFor(() => (_game = Object.FindFirstObjectByType<GameController>()) != null && _game.Board != null && _game.AcceptsBoardInput);
            _ui = Object.FindFirstObjectByType<UIController>();
            _presenter = Object.FindFirstObjectByType<BoardPresenter>();
            _camera = GameObject.Find("BoardCamera").GetComponent<Camera>();
            _pointer = new ScriptedPointer();
            Object.FindFirstObjectByType<InputController>().SetPointerSource(_pointer);

            foreach (var levelId in LevelCatalog.LevelIds)
            {
                if (levelId != "L001")
                {
                    _game.OnBackPressed(); // Won -> level list
                    _game.OnLevelSelected(levelId);
                    yield return WaitIdle();
                }
                Assert.AreEqual(levelId, _game.Board.Level.LevelId, $"{levelId} must be unlocked");

                var actions = LevelCatalog.LoadSolution(levelId).actions;
                for (int i = 0; i < actions.Count; i++)
                {
                    yield return PlayAction(actions[i]);
                    Assert.AreEqual(i + 1, _game.Board.MoveCount,
                        $"{levelId} action {i + 1} ({actions[i].block} -> {actions[i].x},{actions[i].y} '{actions[i].exit}') did not commit");
                }
                yield return WaitFor(() => _ui.Modal.Current == ModalId.Won);
            }

            var saved = new SaveService(Application.persistentDataPath).Load(out _);
            Assert.AreEqual(LevelCatalog.Count, saved.completedLevelIds.Count);
        }
    }
}
