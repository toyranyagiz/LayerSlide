using System;
using System.Collections.Generic;
using LayerSlide.Core;
using LayerSlide.Presentation;
using LayerSlide.Services;
using LayerSlide.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LayerSlide.Gameplay
{
    /// <summary>
    /// Board state machine and screen flow (GDD 01 §8–11, 05 §10). Owns the run: every command is validated by
    /// the rule engine, committed, saved once, and only then animated.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class GameController : MonoBehaviour, IUiHandler, IBoardInputHandler
    {
        enum PlayState
        {
            Loading,
            Idle,
            Dragging,
            Settling,
            Resolving,
            HintSearching,
            Paused,
            Won,
            Error
        }

        const float SettleSeconds = 0.1f;
        const float ResolveSeconds = 0.3f;
        const float ReducedResolveSeconds = 0.1f;
        const float WinFadeSeconds = 0.15f;
        const float LayerInfoSeconds = 1f;
        const float HintSeconds = 2f;
        const float MessageSeconds = 3f;
        const float HandFirstDelay = 2f;
        const float HandRepeatDelay = 5f;

        const string SaveFailedText = "Kayıt yazılamadı. Depolama alanını kontrol et.";

        [SerializeField] BoardPresenter presenter;
        [SerializeField] InputController input;
        [SerializeField] UIController ui;
        [SerializeField] CameraFramer boardCamera;

        SaveService _save;
        AudioHapticsService _audio;
        HintService _hints;
        PlayerSave _profile;
        bool _saveLocked;

        Level _level;
        Board _board;
        readonly CommandHistory _history = new CommandHistory();
        readonly TutorialTracker _tutorial = new TutorialTracker();

        PlayState _state = PlayState.Loading;
        PlayState _stateAfterTimer;
        float _stateTimerEnds;
        int _stateRevision;
        int _runGeneration;
        float _activeSecondsCarry;

        bool _settingsFromPause;
        bool _restartFromPause;
        bool _pauseOnFocusReturn;
        string _pendingLevelId;

        // HUD info state.
        int _focusBlock = -1;
        float _focusUntil;
        float _clearedUntil;
        string _statusMessage;
        float _statusUntil;
        float _hintVisibleUntil;
        float _idleSeconds;
        float _nextHandAt;
        string _shownLayerInfo;
        string _shownMessage;

        // HUD strings are cached so the per-frame refresh does not allocate.
        readonly Dictionary<int, string> _sequenceCache = new Dictionary<int, string>();
        Level _sequenceLevel;
        int _remainingCacheKey = -1;
        string _remainingCacheText;
        bool _tutorialShownLogged;

        /// <summary>Raised once per won run, in the same step that records the completion.</summary>
        public event Action<string> LevelCompleted;

        public Board Board => _board;
        public bool ReducedMotion => _profile != null && _profile.settings.reducedMotion;

        public bool AcceptsBoardInput =>
            _state == PlayState.Idle && ui.CurrentScreen == AppScreen.Playing && !ui.Modal.IsOpen;

        // ================================================================ Lifecycle

        void Start()
        {
            Bootstrap.ConfigureApplication();
            var services = AppServices.Instance;
            _save = services.Save;
            _audio = services.Audio;
            _hints = services.Hints;

            ui.Build(this);
            ui.LayoutChanged += OnLayoutChanged;
            input.Initialize(this);
            SetBoardVisible(false);

            _profile = _save.Load(out var outcome);
            if (outcome == SaveLoadOutcome.NewerVersion)
            {
                ShowNewerSaveBlocker();
                return;
            }

            _audio.Apply(_profile.settings);
            ui.Hud.SetMuted(_profile.settings.sfxVolume == 0);

            string notice = outcome == SaveLoadOutcome.RecoveredFromBackup ? "Son kayıt okunamadı; yedek kayıt yüklendi."
                : outcome == SaveLoadOutcome.ProfileReset ? "Kayıtlar okunamadı; oyun yeni profille başlatıldı."
                : null;
            if (!ValidateSavedRun())
                notice ??= "Bu bölümün kaydı yenilendi; bölüm baştan açılacak.";
            if (notice != null)
                ui.Toast.Show(notice, MessageSeconds);

            if (_profile.isFirstLaunchCompleted)
                ShowHome();
            else
                StartLevel(LevelCatalog.LevelIds[0], resume: false);
        }

        void Update()
        {
            if (_profile == null) return;

            HandleBackKey();
            UpdateStateTimer();
            TrackActiveTime();
            UpdateTutorialHand();
            RefreshHudText();
        }

        void OnApplicationPause(bool paused)
        {
            if (paused) HandleFocusLost();
            else HandleFocusRegained();
        }

        void OnApplicationFocus(bool focused)
        {
            if (!focused) HandleFocusLost();
            else HandleFocusRegained();
        }

        void HandleFocusLost()
        {
            if (_profile == null || _saveLocked) return;

            if (_state == PlayState.Dragging) input.CancelActiveDrag();
            if (_state == PlayState.HintSearching) _hints.Cancel();
            _audio.StopAll();

            if (_state == PlayState.Settling || _state == PlayState.Resolving)
                FinishStateTimerNow();

            bool playing = ui.CurrentScreen == AppScreen.Playing && _board != null;
            _pauseOnFocusReturn = playing && (_state == PlayState.Idle || _state == PlayState.Dragging);
            if (playing) SyncRunToProfile();
            Save();
        }

        void HandleFocusRegained()
        {
            if (!_pauseOnFocusReturn) return;
            _pauseOnFocusReturn = false;
            if (_state == PlayState.Idle && !ui.Modal.IsOpen && ui.CurrentScreen == AppScreen.Playing)
                OpenPause();
        }

        void OnLayoutChanged()
        {
            if (_state == PlayState.Dragging) input.CancelActiveDrag();
            if (_level != null) FrameBoard();
        }

        // ================================================================ Level flow

        void StartLevel(string levelId, bool resume)
        {
            input.CancelActiveDrag();
            _hints.Cancel();
            ui.Modal.Hide();
            _state = PlayState.Loading;

            // Validate the target before touching the current run, so a broken level never costs progress.
            if (!LevelCatalog.TryLoadLevel(levelId, out var level, out string error))
            {
                Debug.LogError($"[GameController] {error}");
                ShowLoadError(levelId, resume);
                return;
            }
            if (_level != null && _board != null && !_board.IsCleared && _history.Count > 0 && !(resume && _level.LevelId == levelId))
                Log("level_left");

            _level = level;
            _board = new Board(level);
            _runGeneration++;
            _stateRevision++;

            var run = _profile.currentRun;
            bool resumed = resume && _profile.HasRun && run.boardState.levelId == levelId && !run.boardState.isCompleted
                && _history.TryRestore(_board, run.history, out _);
            if (!resumed)
            {
                _board.ResetToStart();
                _history.Clear();
                _profile.currentRun = NewRun();
            }

            _tutorial.Begin(level, _profile.tutorialCompletedIds);
            presenter.Build(level, _board, _profile.settings.colorSymbols);
            SetBoardVisible(true);
            ui.ShowScreen(AppScreen.Playing);
            ui.Hud.SetTitle($"Bölüm {LevelCatalog.GetNumber(levelId)}");
            FrameBoard();

            _focusBlock = -1;
            _statusMessage = null;
            _idleSeconds = 0f;
            _nextHandAt = HandFirstDelay;
            _profile.isFirstLaunchCompleted = true;
            SyncRunToProfile();
            Save();
            SetState(PlayState.Idle);
            _tutorialShownLogged = false;
            Log("level_started", ("attemptId", _profile.currentRun.attemptId), ("resumed", resumed));
        }

        RunSave NewRun() => new RunSave
        {
            attemptId = Guid.NewGuid().ToString("N"),
            activeMilliseconds = 0,
            undoCount = 0
        };

        /// <summary>
        /// Replays the saved history through the rule engine (GDD 06 §7). A run that does not match exactly is
        /// replaced by a fresh run of the same level when that level is still unlocked.
        /// </summary>
        bool ValidateSavedRun()
        {
            if (!_profile.HasRun) return true;

            var run = _profile.currentRun;
            string levelId = run.boardState.levelId;
            Level level = null;
            bool levelAvailable = LevelCatalog.Contains(levelId) && IsUnlocked(levelId)
                && LevelCatalog.TryLoadLevel(levelId, out level, out _);

            if (levelAvailable && run.history != null && run.boardState.blocks != null)
            {
                var board = new Board(level);
                var history = new CommandHistory();
                bool valid = history.TryRestore(board, run.history, out _)
                    && board.Matches(run.boardState)
                    && board.MoveCount == history.Count
                    && (!run.boardState.isCompleted || _profile.completedLevelIds.Contains(levelId));
                if (valid) return true;
            }

            // Only the run is reset; completed levels, tutorials and settings stay.
            if (levelAvailable)
            {
                var freshRun = NewRun();
                new Board(level).WriteTo(freshRun.boardState);
                _profile.currentRun = freshRun;
            }
            else
            {
                _profile.currentRun = new RunSave();
            }
            Save();
            return false;
        }

        void SyncRunToProfile()
        {
            var run = _profile.currentRun;
            _board.WriteTo(run.boardState);
            _history.CopyTo(run.history);
            run.activeMilliseconds += (long)(_activeSecondsCarry * 1000f);
            _activeSecondsCarry = 0f;
        }

        void Save()
        {
            if (_saveLocked) return;
            if (!_save.TryWrite(_profile))
                ui.Toast.Show(SaveFailedText, MessageSeconds);
        }

        // ================================================================ Board input (IBoardInputHandler)

        public void OnBlockPressed(int block)
        {
            SetState(PlayState.Dragging);
            presenter.BeginDrag(block, ReducedMotion);
            _audio.Play(Sfx.Select);
            _focusBlock = block;
            _focusUntil = float.PositiveInfinity;
            _hintVisibleUntil = 0f;
            ResetIdle();
        }

        public void OnBlockTapped(int block)
        {
            presenter.ReturnBlock(block, _board.GetX(block), _board.GetY(block), ReducedMotion);
            _focusUntil = Time.unscaledTime + LayerInfoSeconds;
            SetState(PlayState.Idle);
        }

        public void OnBlockReleased(int block, int x, int y, int gate)
        {
            if (_state != PlayState.Dragging) return;

            if (!RuleEngine.TryCreateCommand(_board, block, x, y, gate, out var command))
            {
                // Nothing changed: no command, no history, no save (GDD 01 §7).
                presenter.ReturnBlock(block, _board.GetX(block), _board.GetY(block), ReducedMotion);
                _focusUntil = Time.unscaledTime + LayerInfoSeconds;
                EnterTimedState(PlayState.Settling, ReducedMotion ? 0f : SettleSeconds, PlayState.Idle);
                return;
            }
            Commit(command);
        }

        public void OnInvalidExit(int block, GateSide side, int x, int y)
        {
            presenter.FlashInvalidExit(block, side, x, y);
            _audio.Play(Sfx.Blocked);
            Log("invalid_exit_attempt", ("reason", InvalidExitReason(block, side, x, y)));
        }

        /// <summary>"wrong_color", "partial_cover" (a gate is there but does not accept the block) or "no_gate".</summary>
        string InvalidExitReason(int block, GateSide side, int x, int y)
        {
            var spec = _level.Blocks[block];
            bool vertical = side.GetAxis() == Axis.X;
            int from = vertical ? y : x, to = from + (vertical ? spec.Height : spec.Width);
            string reason = "no_gate";
            foreach (var gate in _level.Gates)
            {
                if (gate.Side != side || to <= gate.Start || from >= gate.End) continue;
                if (gate.Color != _board.GetActiveColor(block)) reason = "wrong_color";
                else return "partial_cover";
            }
            return reason;
        }

        public void OnGateArmed() => _audio.Play(Sfx.GateReady);

        public void OnDragCancelled(int block)
        {
            presenter.ReturnBlock(block, _board.GetX(block), _board.GetY(block), true);
            _focusUntil = Time.unscaledTime;
            SetState(PlayState.Idle);
        }

        // ================================================================ Commands

        void Commit(CommandRecord command)
        {
            RuleEngine.Apply(_board, command);
            _history.Push(command);
            _stateRevision++;

            string completedTag = _tutorial.OnCommand(_board, command);
            if (completedTag != null) AddUnique(_profile.tutorialCompletedIds, completedTag);

            bool won = _board.IsCleared;
            string wonTag = null;
            if (won)
            {
                AddUnique(_profile.completedLevelIds, _level.LevelId);
                wonTag = _tutorial.OnLevelWon();
                if (wonTag != null) AddUnique(_profile.tutorialCompletedIds, wonTag);
            }

            // Progress, board, history and tutorial are written together, once, before any animation.
            SyncRunToProfile();
            Save();

            var axis = RuleEngine.GetCommandAxis(_level, command);
            int distance = Mathf.Abs(command.visualEndX - command.beforeX) + Mathf.Abs(command.visualEndY - command.beforeY);
            Log("move_committed", ("blockId", command.blockId), ("axis", axis == Axis.X ? "X" : "Y"), ("distance", distance), ("peeled", command.IsPeel));
            if (command.IsPeel)
            {
                int peeledBlock = _level.IndexOfBlock(command.blockId);
                Log("layer_peeled", ("gateId", command.gateId), ("remainingLayers", _board.GetRemainingLayers(peeledBlock)), ("blockRemoved", _board.IsRemoved(peeledBlock)));
            }
            if (completedTag != null) Log("tutorial_completed", ("tag", completedTag));
            if (wonTag != null) Log("tutorial_completed", ("tag", wonTag));
            if (won)
            {
                Log("level_completed", ("moves", _board.MoveCount), ("undoCount", _profile.currentRun.undoCount));
                LevelCompleted?.Invoke(_level.LevelId);
            }

            presenter.AnimateCommand(command, ReducedMotion, _history.Count);
            int block = _level.IndexOfBlock(command.blockId);
            _focusBlock = block;
            _focusUntil = Time.unscaledTime + LayerInfoSeconds;

            if (!command.IsPeel)
            {
                EnterTimedState(PlayState.Settling, ReducedMotion ? 0f : SettleSeconds, PlayState.Idle);
                return;
            }

            bool cleared = _board.IsRemoved(block);
            _audio.Play(cleared ? Sfx.Clear : Sfx.Peel);
            _audio.Vibrate(cleared ? 30 : 20);
            if (cleared)
            {
                _focusBlock = -1;
                _clearedUntil = Time.unscaledTime + LayerInfoSeconds;
            }
            EnterTimedState(PlayState.Resolving, ReducedMotion ? ReducedResolveSeconds : ResolveSeconds,
                won ? PlayState.Won : PlayState.Idle);
        }

        void Undo()
        {
            if (_state != PlayState.Idle || _history.Count == 0 || _board.IsCleared) return;

            var command = _history.Pop();
            RuleEngine.Revert(_board, command);
            _stateRevision++;
            _profile.currentRun.undoCount++;
            SyncRunToProfile();
            Save();

            presenter.AnimateUndo(command, ReducedMotion);
            presenter.HideHint();
            _audio.Play(Sfx.Undo);
            Log("undo_used", ("historyDepth", _history.Count));
            EnterTimedState(PlayState.Settling, ReducedMotion ? 0f : SettleSeconds, PlayState.Idle);
        }

        void Restart()
        {
            ui.Modal.Hide();
            _hints.Cancel();
            _board.ResetToStart();
            _history.Clear();
            _runGeneration++;
            _stateRevision++;
            _profile.currentRun = NewRun();
            _tutorial.Begin(_level, _profile.tutorialCompletedIds);
            presenter.SyncAll(_board);
            presenter.HideHint();
            _nextHandAt = HandFirstDelay;
            _idleSeconds = 0f;
            SyncRunToProfile();
            Save();
            SetState(PlayState.Idle);
            Log("level_restarted", ("attemptId", _profile.currentRun.attemptId));
        }

        // ================================================================ State machine

        void SetState(PlayState state)
        {
            _state = state;
            RefreshHudButtons();
        }

        void EnterTimedState(PlayState state, float seconds, PlayState next)
        {
            _stateAfterTimer = next;
            _stateTimerEnds = Time.unscaledTime + seconds;
            SetState(state);
        }

        void UpdateStateTimer()
        {
            if ((_state == PlayState.Settling || _state == PlayState.Resolving) && Time.unscaledTime >= _stateTimerEnds)
                FinishStateTimerNow();
        }

        void FinishStateTimerNow()
        {
            if (_stateAfterTimer == PlayState.Won)
                EnterWon();
            else
                SetState(PlayState.Idle);
        }

        void EnterWon()
        {
            if (_state == PlayState.Won) return; // a repeated timer/focus callback must not win twice
            SetState(PlayState.Won);
            presenter.HideHint();
            presenter.HideTutorialHand();
            presenter.PlayWin(_history.Count, ReducedMotion);
            _audio.Play(Sfx.Win);

            int index = LevelCatalog.IndexOf(_level.LevelId);
            bool last = index == LevelCatalog.Count - 1;
            string levelId = _level.LevelId;
            var buttons = new List<(string, Action)>();
            if (!last) buttons.Add(("Sonraki", () => StartLevel(LevelCatalog.LevelIds[index + 1], resume: false)));
            buttons.Add(("Tekrar oyna", () => StartLevel(levelId, resume: false)));
            buttons.Add(("Bölümler", ShowLevelSelect));

            string title = last ? $"{LevelCatalog.Count} bölüm tamamlandı!" : "Bölüm tamamlandı";
            ui.Modal.Show(ModalId.Won, title, null, buttons, ReducedMotion ? 0f : WinFadeSeconds);
        }

        void TrackActiveTime()
        {
            if (ui.CurrentScreen != AppScreen.Playing || !Application.isFocused) return;
            if (_state == PlayState.Idle || _state == PlayState.Dragging || _state == PlayState.Settling || _state == PlayState.Resolving)
                _activeSecondsCarry += Time.unscaledDeltaTime;
        }

        // ================================================================ HUD

        void RefreshHudButtons()
        {
            if (_board == null) return;
            bool idle = _state == PlayState.Idle && !ui.Modal.IsOpen;
            bool searching = _state == PlayState.HintSearching;
            ui.Hud.SetButtons(
                undo: idle && _history.Count > 0 && !_board.IsCleared,
                restart: idle && !_board.IsCleared,
                hint: (idle && !_board.IsCleared) || searching,
                hintCancels: searching);
        }

        /// <summary>Updates the layer row and message row; strings are only rebuilt when their content changes.</summary>
        void RefreshHudText()
        {
            if (_board == null || ui.CurrentScreen != AppScreen.Playing) return;
            float now = Time.unscaledTime;

            string layerInfo;
            if (now < _clearedUntil) layerInfo = "Blok temizlendi";
            else if (_focusBlock >= 0 && (now < _focusUntil || now < _hintVisibleUntil) && !_board.IsRemoved(_focusBlock))
                layerInfo = LayerSequence(_focusBlock);
            else layerInfo = RemainingLayersText();
            if (layerInfo != _shownLayerInfo)
            {
                _shownLayerInfo = layerInfo;
                ui.Hud.SetLayerInfo(layerInfo);
            }

            if (_hintVisibleUntil > 0f && now >= _hintVisibleUntil)
            {
                _hintVisibleUntil = 0f;
                presenter.HideHint();
            }

            // Priority: hint status > tutorial (errors use the toast on top).
            string message = _statusMessage != null && (now < _statusUntil || _state == PlayState.HintSearching)
                ? _statusMessage
                : _hintVisibleUntil > 0f ? null : _tutorial.GetMessage(_board);
            if (message != _shownMessage)
            {
                _shownMessage = message;
                ui.Hud.SetMessage(message ?? string.Empty);
                if (!_tutorialShownLogged && message != null && _tutorial.ActiveTag != null)
                {
                    _tutorialShownLogged = true;
                    Log("tutorial_shown", ("tag", _tutorial.ActiveTag));
                }
            }
        }

        string LayerSequence(int block)
        {
            if (_sequenceLevel != _level)
            {
                _sequenceCache.Clear();
                _sequenceLevel = _level;
            }

            int removed = _board.GetRemovedLayers(block);
            int key = block * 4 + removed;
            if (_sequenceCache.TryGetValue(key, out var text)) return text;

            var layers = _level.Blocks[block].Layers;
            text = removed + 1 < layers.Length
                ? $"Şimdi: {Palette.NameOf(layers[removed])} → Sonra: {Palette.NameOf(layers[removed + 1])}"
                : $"Şimdi: {Palette.NameOf(layers[removed])} • Son katman";
            _sequenceCache[key] = text;
            return text;
        }

        string RemainingLayersText()
        {
            int remaining = 0;
            for (int b = 0; b < _board.BlockCount; b++)
                remaining += _board.GetRemainingLayers(b);
            if (remaining != _remainingCacheKey)
            {
                _remainingCacheKey = remaining;
                _remainingCacheText = $"Kalan katman: {remaining}";
            }
            return _remainingCacheText;
        }

        void ShowStatus(string message, float seconds)
        {
            _statusMessage = message;
            _statusUntil = Time.unscaledTime + seconds;
        }

        void ResetIdle()
        {
            _idleSeconds = 0f;
            _nextHandAt = HandRepeatDelay;
            presenter.HideTutorialHand();
        }

        /// <summary>L001 only: hand trace after 2 s of inactivity, then every 5 s, until the first peel (GDD 02 §5).</summary>
        void UpdateTutorialHand()
        {
            if (!_tutorial.ShowsDragHand || _state != PlayState.Idle || ui.Modal.IsOpen || ui.CurrentScreen != AppScreen.Playing)
                return;

            _idleSeconds += Time.unscaledDeltaTime;
            if (_idleSeconds < _nextHandAt) return;

            int block = _level.IndexOfBlock("A");
            if (block < 0 || _board.IsRemoved(block)) return;
            var from = presenter.BlockCenter(block, _board.GetX(block), _board.GetY(block));
            var to = presenter.BlockCenter(block, 0, _board.GetY(block)) + Vector3.left * RuleConstants.ExitIntentThreshold;
            presenter.PlayTutorialHand(from, to, ReducedMotion);
            _nextHandAt = _idleSeconds + HandRepeatDelay;
        }

        // ================================================================ Hint

        void RequestHint()
        {
            if (_state != PlayState.Idle || _board.IsCleared) return;

            SetState(PlayState.HintSearching);
            presenter.HideHint();
            presenter.HideTutorialHand();
            _statusMessage = "Çözüm aranıyor…";
            _hints.Request(_board, _stateRevision, _runGeneration, OnHintResult);
        }

        void OnHintResult(HintResult result)
        {
            // Results for an older board, run or a cancelled search never reach the screen.
            if (result.RunGeneration != _runGeneration || result.StateRevision != _stateRevision || _state != PlayState.HintSearching)
                return;

            _statusMessage = null;
            SetState(PlayState.Idle);
            Log("hint_result", ("status", result.Status.ToString()), ("expanded", result.Expanded), ("cpuMs", result.CpuMilliseconds));
            switch (result.Status)
            {
                case HintStatus.Found:
                    var action = result.Action;
                    if (!RuleEngine.TryCreateCommand(_board, action.Block, action.X, action.Y, action.Gate, out _))
                    {
                        ShowStatus("İpucu şu anda kullanılamıyor. Yeniden dene.", MessageSeconds);
                        return;
                    }
                    presenter.ShowHint(action, _board, ReducedMotion);
                    _hintVisibleUntil = Time.unscaledTime + HintSeconds;
                    _focusBlock = action.Block;
                    break;
                case HintStatus.Exhausted:
                    ShowStatus("Bu konumdan çözüm yok. Geri al veya baştan başla.", MessageSeconds);
                    break;
                case HintStatus.BudgetExceeded:
                    ShowStatus("Bu konum için ipucu bulunamadı. Geri alabilir veya baştan başlayabilirsin.", MessageSeconds);
                    break;
                case HintStatus.Failed:
                    ShowStatus("İpucu şu anda kullanılamıyor. Yeniden dene.", MessageSeconds);
                    break;
            }
        }

        // ================================================================ Screens and modals

        void ShowHome()
        {
            LeavePlaying();
            bool allDone = _profile.completedLevelIds.Count >= LevelCatalog.Count;
            bool hasOpenRun = _profile.HasRun && !_profile.currentRun.boardState.isCompleted;
            ui.Home.SetContinueLabel(allDone && !hasOpenRun ? "Bölümleri tekrar oyna" : "Devam et");
            ui.ShowScreen(AppScreen.Home);
        }

        void ShowLevelSelect()
        {
            LeavePlaying();
            ui.LevelSelect.Refresh(GetCellState);
            ui.ShowScreen(AppScreen.LevelSelect);
        }

        /// <summary>Leaving the board keeps the run as it is; only the screen changes.</summary>
        void LeavePlaying()
        {
            if (ui.CurrentScreen == AppScreen.Playing && _board != null && !_board.IsCleared && _history.Count > 0)
                Log("level_left");
            input.CancelActiveDrag();
            _hints.Cancel();
            ui.Modal.Hide();
            if (_board != null && ui.CurrentScreen == AppScreen.Playing)
            {
                SyncRunToProfile();
                Save();
            }
            presenter.HideHint();
            SetBoardVisible(false);
            if (_state != PlayState.Loading) SetState(PlayState.Paused);
        }

        void OpenPause()
        {
            SetState(PlayState.Paused);
            presenter.HideTutorialHand();
            ui.Modal.Show(ModalId.Pause, "Duraklatıldı", null, new List<(string, Action)>
            {
                ("Devam et", ResumeFromPause),
                ("Baştan başla", () => RequestRestart(fromPause: true)),
                ("Ayarlar", () => OpenSettings(fromPause: true)),
                ("Bölümler", ShowLevelSelect),
                ("Ana menü", ShowHome)
            });
            SyncRunToProfile();
            Save();
        }

        void ResumeFromPause()
        {
            ui.Modal.Hide();
            SetState(_board.IsCleared ? PlayState.Won : PlayState.Idle);
        }

        void RequestRestart(bool fromPause)
        {
            if (_history.Count == 0)
            {
                Restart();
                return;
            }

            _restartFromPause = fromPause;
            SetState(PlayState.Paused);
            ui.Modal.Show(ModalId.RestartConfirm, null, "Bu bölüme baştan başlansın mı?", new List<(string, Action)>
            {
                ("Vazgeç", CancelRestart),
                ("Baştan başla", Restart)
            });
        }

        void CancelRestart()
        {
            if (_restartFromPause) OpenPause();
            else ResumeFromPause();
        }

        void OpenSettings(bool fromPause)
        {
            _settingsFromPause = fromPause;
            ui.Modal.Hide();
            if (fromPause) SetBoardVisible(false);
            ui.Settings.Refresh(_profile.settings);
            ui.ShowScreen(AppScreen.Settings);
        }

        void CloseSettings()
        {
            Save();
            if (_settingsFromPause && _board != null)
            {
                ui.ShowScreen(AppScreen.Playing);
                SetBoardVisible(true);
                OpenPause();
            }
            else
            {
                ShowHome();
            }
        }

        void ShowLoadError(string levelId, bool resume)
        {
            SetState(PlayState.Error);
            ui.Modal.Show(ModalId.Error, null, "Bölüm yüklenemedi.", new List<(string, Action)>
            {
                ("Yeniden dene", () => StartLevel(levelId, resume)),
                ("Bölümler", ShowLevelSelect)
            });
        }

        void ShowNewerSaveBlocker()
        {
            _saveLocked = true;
            ui.ShowScreen(AppScreen.Home);
            ui.Modal.Show(ModalId.NewerSave, null, "Kayıt daha yeni bir oyun sürümüne ait.",
                new List<(string, Action)> { ("Çık", Application.Quit) });
        }

        void ShowExitConfirm()
        {
            ui.Modal.Show(ModalId.ExitConfirm, null, "Oyundan çıkılsın mı?", new List<(string, Action)>
            {
                ("Vazgeç", ui.Modal.Hide),
                ("Çık", () => { Save(); Application.Quit(); })
            });
        }

        /// <summary>System back (Android back maps to Escape), resolved by the topmost screen or modal (GDD 01 §9).</summary>
        void HandleBackKey()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.escapeKey.wasPressedThisFrame) return;
            OnBackPressed();
        }

        void FrameBoard() => boardCamera.Frame(_level.Width, _level.Height, ui.GetBoardRectPixels());

        void SetBoardVisible(bool visible)
        {
            presenter.gameObject.SetActive(visible);
            boardCamera.Camera.enabled = visible;
        }

        // ================================================================ Local test log (development builds only)

        void Log(string eventName, params (string, object)[] fields)
        {
            if (!LocalTestLogger.Enabled || _level == null) return;
            var run = _profile.currentRun;
            LocalTestLogger.Log(eventName, new LocalTestLogger.Context
            {
                LevelId = _level.LevelId,
                ContentVersion = _level.ContentVersion,
                RuleVersion = _level.RuleVersion,
                AttemptId = run.attemptId,
                ActiveMilliseconds = run.activeMilliseconds + (long)(_activeSecondsCarry * 1000f)
            }, fields);
        }

        // ================================================================ Progress

        int ContiguousCompletedCount()
        {
            int count = 0;
            while (count < LevelCatalog.Count && _profile.completedLevelIds.Contains(LevelCatalog.LevelIds[count]))
                count++;
            return count;
        }

        bool IsUnlocked(string levelId) => LevelCatalog.IndexOf(levelId) <= Mathf.Min(ContiguousCompletedCount(), LevelCatalog.Count - 1);

        LevelCellState GetCellState(string levelId)
        {
            if (_profile.completedLevelIds.Contains(levelId)) return LevelCellState.Completed;
            return IsUnlocked(levelId) ? LevelCellState.Open : LevelCellState.Locked;
        }

        static void AddUnique(List<string> list, string value)
        {
            if (!list.Contains(value)) list.Add(value);
        }

        // ================================================================ IUiHandler

        public void OnContinuePressed()
        {
            if (_profile.HasRun && !_profile.currentRun.boardState.isCompleted)
            {
                StartLevel(_profile.currentRun.boardState.levelId, resume: true);
                return;
            }

            foreach (var id in LevelCatalog.LevelIds)
            {
                if (IsUnlocked(id) && !_profile.completedLevelIds.Contains(id))
                {
                    StartLevel(id, resume: false);
                    return;
                }
            }
            ShowLevelSelect();
        }

        public void OnLevelsPressed() => ShowLevelSelect();

        public void OnSettingsPressed() => OpenSettings(fromPause: false);

        public void OnLevelSelected(string levelId)
        {
            if (!IsUnlocked(levelId) || ui.Modal.IsOpen) return;

            var run = _profile.currentRun;
            bool openRun = _profile.HasRun && !run.boardState.isCompleted;
            if (openRun && run.boardState.levelId == levelId)
            {
                StartLevel(levelId, resume: true);
                return;
            }

            if (openRun && run.history.Count > 0)
            {
                _pendingLevelId = levelId;
                ui.Modal.Show(ModalId.LeaveRunConfirm, null, "Bu bölümdeki ilerlemeyi bırakıp diğer bölümü açmak istiyor musun?",
                    new List<(string, Action)>
                    {
                        ("Vazgeç", () => { _pendingLevelId = null; ui.Modal.Hide(); }),
                        ("Diğer bölümü aç", () => StartLevel(_pendingLevelId, resume: false))
                    });
                return;
            }
            StartLevel(levelId, resume: false);
        }

        public void OnBackPressed()
        {
            if (_saveLocked) return;

            switch (ui.Modal.Current)
            {
                case ModalId.Pause: ResumeFromPause(); return;
                case ModalId.RestartConfirm: CancelRestart(); return;
                case ModalId.LeaveRunConfirm:
                case ModalId.ExitConfirm: ui.Modal.Hide(); return;
                case ModalId.Won:
                case ModalId.Error: ShowLevelSelect(); return;
            }

            switch (ui.CurrentScreen)
            {
                case AppScreen.Settings: CloseSettings(); return;
                case AppScreen.About: ui.ShowScreen(AppScreen.Settings); return;
                case AppScreen.LevelSelect: ShowHome(); return;
                case AppScreen.Home: ShowExitConfirm(); return;
            }

            switch (_state)
            {
                case PlayState.Dragging:
                    input.CancelActiveDrag();
                    OpenPause();
                    break;
                case PlayState.HintSearching:
                    _hints.Cancel();
                    break;
                case PlayState.Idle:
                    OpenPause();
                    break;
            }
        }

        public void OnMenuPressed()
        {
            if (_state == PlayState.Idle && !ui.Modal.IsOpen) OpenPause();
        }

        public void OnMutePressed()
        {
            var settings = _profile.settings;
            settings.sfxVolume = settings.sfxVolume > 0 ? 0 : settings.lastNonZeroSfxVolume;
            ApplySettings();
            Save();
        }

        public void OnUndoPressed() => Undo();

        public void OnRestartPressed()
        {
            if (_state == PlayState.Idle && !ui.Modal.IsOpen) RequestRestart(fromPause: false);
        }

        public void OnHintPressed()
        {
            if (_state == PlayState.HintSearching) _hints.Cancel();
            else RequestHint();
        }

        public void OnVolumeChanged(int volume, bool commit)
        {
            var settings = _profile.settings;
            settings.sfxVolume = Mathf.Clamp(volume, 0, 100);
            if (settings.sfxVolume > 0) settings.lastNonZeroSfxVolume = settings.sfxVolume;
            ApplySettings();
            if (commit) Save();
        }

        public void OnVibrationToggled() => ToggleSetting(s => s.vibration = !s.vibration);
        public void OnSymbolsToggled() => ToggleSetting(s => s.colorSymbols = !s.colorSymbols);
        public void OnReducedMotionToggled() => ToggleSetting(s => s.reducedMotion = !s.reducedMotion);

        public void OnAboutPressed() => ui.ShowScreen(AppScreen.About);

        void ToggleSetting(Action<SettingsData> change)
        {
            change(_profile.settings);
            ApplySettings();
            Save();
        }

        void ApplySettings()
        {
            var settings = _profile.settings;
            _audio.Apply(settings);
            ui.Hud.SetMuted(settings.sfxVolume == 0);
            ui.Settings.Refresh(settings);
            if (_level != null) presenter.SetSymbolsVisible(settings.colorSymbols);
        }
    }
}
