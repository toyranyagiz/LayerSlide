using System;
using System.Collections;
using System.IO;
using LayerSlide.Gameplay;
using LayerSlide.Services;
using LayerSlide.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace LayerSlide.Tests
{
    /// <summary>
    /// End-to-end run of the real Gameplay scene: first launch opens L001, then every recorded solution is played
    /// through the same commit path the touch input uses, and progress must be on disk afterwards.
    /// Set LAYERSLIDE_CAPTURE_DIR to also write PNG captures for visual review.
    /// </summary>
    public class GameplaySmokeTests
    {
        static readonly string[] SaveFiles = { "save.json", "save.bak", "save.tmp" };

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

        [UnityTest]
        public IEnumerator FirstTenLevelsCanBeCompletedInOrder()
        {
            SceneManager.LoadScene("Gameplay");
            GameController game = null;
            yield return WaitUntil(() => (game = UnityEngine.Object.FindFirstObjectByType<GameController>()) != null && game.AcceptsBoardInput, 5f);
            var ui = UnityEngine.Object.FindFirstObjectByType<UIController>();

            Assert.AreEqual("L001", game.Board.Level.LevelId, "first launch opens L001");
            yield return Capture("01_L001_start");

            foreach (var levelId in LevelCatalog.LevelIds)
            {
                if (levelId != "L001")
                {
                    Assert.AreEqual(ModalId.Won, ui.Modal.Current);
                    game.OnBackPressed(); // Won -> level list
                    Assert.AreEqual(AppScreen.LevelSelect, ui.CurrentScreen);
                    if (levelId == "L009") yield return Capture("03_LevelSelect");
                    game.OnLevelSelected(levelId);
                    yield return WaitUntil(() => game.AcceptsBoardInput, 2f);
                    Assert.AreEqual(levelId, game.Board.Level.LevelId, $"{levelId} must be unlocked");
                    if (levelId == "L009") yield return Capture("04_L009_start");
                }

                var solution = LevelCatalog.LoadSolution(levelId);
                for (int i = 0; i < solution.actions.Count; i++)
                {
                    var action = solution.actions[i];
                    var level = game.Board.Level;
                    int block = level.IndexOfBlock(action.block);
                    int gate = string.IsNullOrEmpty(action.exit) ? -1 : level.IndexOfGate(action.exit);

                    yield return WaitUntil(() => game.AcceptsBoardInput, 2f);
                    game.OnBlockPressed(block);
                    game.OnBlockReleased(block, action.x, action.y, gate);
                    Assert.AreEqual(i + 1, game.Board.MoveCount, $"{levelId} action {i + 1} was not committed");
                    if (levelId == "L009" && i == 2) yield return Capture("05_L009_midway");
                }

                yield return WaitUntil(() => ui.Modal.Current == ModalId.Won, 2f);
                Assert.IsTrue(game.Board.IsCleared);
                if (levelId == "L001") yield return Capture("02_L001_won");
            }

            var saved = new SaveService(Application.persistentDataPath).Load(out var outcome);
            Assert.AreEqual(SaveLoadOutcome.Loaded, outcome);
            Assert.AreEqual(LevelCatalog.Count, saved.completedLevelIds.Count);
            Assert.IsTrue(saved.isFirstLaunchCompleted);

            game.OnBackPressed(); // Won -> list
            game.OnBackPressed(); // list -> home
            Assert.AreEqual(AppScreen.Home, ui.CurrentScreen);
            yield return Capture("06_Home");
        }

        static IEnumerator WaitUntil(Func<bool> condition, float timeoutSeconds)
        {
            float end = Time.realtimeSinceStartup + timeoutSeconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("Timed out waiting for the game.");
                yield return null;
            }
        }

        /// <summary>Renders background, board and UI into one PNG. Skipped unless LAYERSLIDE_CAPTURE_DIR is set.</summary>
        static IEnumerator Capture(string name)
        {
            string directory = Environment.GetEnvironmentVariable("LAYERSLIDE_CAPTURE_DIR");
            yield return new WaitForSecondsRealtime(0.5f);
            if (string.IsNullOrEmpty(directory)) yield break;

            var background = GameObject.Find("BackgroundCamera").GetComponent<Camera>();
            var board = GameObject.Find("BoardCamera").GetComponent<Camera>();
            var canvas = UnityEngine.Object.FindFirstObjectByType<UIController>().GetComponent<Canvas>();
            int width = Screen.width, height = Screen.height;
            var target = RenderTexture.GetTemporary(width, height, 24);

            background.targetTexture = target;
            background.Render();
            if (board.enabled)
            {
                board.targetTexture = target;
                board.Render();
                board.targetTexture = null;
            }

            var mask = background.cullingMask;
            var clear = background.clearFlags;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = background;
            canvas.planeDistance = 1f;
            background.cullingMask = 1 << LayerMask.NameToLayer("UI");
            background.clearFlags = CameraClearFlags.Nothing;
            Canvas.ForceUpdateCanvases();
            background.Render();
            background.cullingMask = mask;
            background.clearFlags = clear;
            background.targetTexture = null;
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            texture.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);

            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, name + ".png"), texture.EncodeToPNG());
            UnityEngine.Object.Destroy(texture);

            if (board.enabled)
                CaptureBoardPortrait(board, Path.Combine(directory, name + "_board.png"));
        }

        /// <summary>The batch editor screen is landscape; this renders the board alone at a phone-like 540x960.</summary>
        static void CaptureBoardPortrait(Camera board, string path)
        {
            const int width = 540, height = 960;
            var rect = board.rect;
            float size = board.orthographicSize;
            var target = RenderTexture.GetTemporary(width, height, 24);

            var level = UnityEngine.Object.FindFirstObjectByType<GameController>().Board.Level;
            float pitch = 65f * Mathf.Deg2Rad;
            float halfWidth = (level.Width + 2f) * 0.5f;
            float halfHeight = (level.Height + 2f) * Mathf.Sin(pitch) * 0.5f + 0.4f * Mathf.Cos(pitch);
            board.rect = new Rect(0f, 0f, 1f, 1f);
            board.orthographicSize = Mathf.Max(halfHeight, halfWidth / (width / (float)height));
            board.targetTexture = target;
            board.Render();
            board.targetTexture = null;
            board.rect = rect;
            board.orthographicSize = size;

            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            texture.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.Destroy(texture);
        }
    }
}
