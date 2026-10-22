using System.Collections.Generic;
using LayerSlide.Core;

namespace LayerSlide.Gameplay
{
    /// <summary>
    /// Tutorial text and completion rules (GDD 02 §4). A level may carry several tags; only the first open one is
    /// shown, but every open tag is checked on each command. Messages and the phase count are derived from the
    /// current board and history, so Undo / reload show the right step without storing extra state.
    /// </summary>
    public sealed class TutorialTracker
    {
        public const string RailTag = "T11_Rail";
        public const string KeyTag = "T12_Key";
        public const string LayersTag = "T03_Layers";
        public const string PlateTag = "T13_Plate";
        public const string PhaseTag = "T14_Phase";

        readonly List<string> _open = new List<string>();
        readonly List<string> _completedNow = new List<string>();
        Level _level;

        /// <summary>First tag still waiting for completion in this level, or null.</summary>
        public string ActiveTag => _open.Count > 0 ? _open[0] : null;

        public void Begin(Level level, ICollection<string> completedTags)
        {
            _level = level;
            _open.Clear();
            foreach (var tag in level.TutorialTags)
                if (!completedTags.Contains(tag))
                    _open.Add(tag);
        }

        /// <summary>The ghost arrow runs until the first valid command of a level that opens with the rail lesson.</summary>
        public bool ShowsGhostArrow(int historyCount) => historyCount == 0 && _open.Contains(RailTag);

        public string GetMessage(Board board)
        {
            switch (ActiveTag)
            {
                case RailTag: return "Çift oklu blok yalnızca ok yönünde kayar.";
                case KeyTag:
                    int k1 = System.Array.IndexOf(board.Level.Keys, "K1");
                    return k1 >= 0 && board.HasKey(k1)
                        ? "Şimdi kilidi açılan kapıyı kullan."
                        : "Anahtarlı bloğun bütün katmanlarını çıkar.";
                case LayersTag:
                    int c = board.Level.IndexOfBlock("C");
                    return c >= 0 && board.GetRemovedLayers(c) >= 1
                        ? "Blok kaldı. Şimdi alttan çıkan rengi eşleştir."
                        : "Üst renk çıkar; alttaki renk sıradadır.";
                case PlateTag: return "Bir bloğu P1 üzerinde tut; başka bloğu kapıdan çıkar.";
                case PhaseTag: return "Her katman çıkışında B kapısının rengi değişir. Kaydırmak değiştirmez.";
                default: return null;
            }
        }

        /// <summary>
        /// Checks every open tag after a committed command (<paramref name="history"/> already holds it).
        /// Returns the tags that completed now; the list is reused between calls.
        /// </summary>
        public IReadOnlyList<string> OnCommand(Board boardAfter, CommandRecord command, IReadOnlyList<CommandRecord> history)
        {
            _completedNow.Clear();
            for (int i = _open.Count - 1; i >= 0; i--)
            {
                if (IsDone(_open[i], boardAfter, command, history))
                {
                    _completedNow.Insert(0, _open[i]);
                    _open.RemoveAt(i);
                }
            }
            return _completedNow;
        }

        /// <summary>Winning completes every tag left open, whatever solution the player used.</summary>
        public IReadOnlyList<string> OnLevelWon()
        {
            _completedNow.Clear();
            _completedNow.AddRange(_open);
            _open.Clear();
            return _completedNow;
        }

        bool IsDone(string tag, Board board, CommandRecord command, IReadOnlyList<CommandRecord> history)
        {
            int gate = command.IsPeel ? _level.IndexOfGate(command.gateId) : -1;
            switch (tag)
            {
                case RailTag:
                    return command.Moved && _level.Blocks[_level.IndexOfBlock(command.blockId)].MoveAxis != MoveAxis.Free;
                case KeyTag:
                    return gate >= 0 && _level.Gates[gate].IsLocked && _level.Keys[_level.Gates[gate].RequiredKey] == "K1";
                case LayersTag:
                    int c = _level.IndexOfBlock("C");
                    return c >= 0 && board.IsRemoved(c);
                case PlateTag:
                    return gate >= 0 && _level.Gates[gate].RequiredPlates.Length > 0;
                case PhaseTag:
                    return SawBothPhases(history);
                default:
                    return false;
            }
        }

        /// <summary>Replays the live history: a phase gate peel must have happened in phase 0 and in phase 1.</summary>
        bool SawBothPhases(IReadOnlyList<CommandRecord> history)
        {
            int phase = _level.InitialPhase;
            bool sawZero = false, sawOne = false;
            foreach (var record in history)
            {
                if (!record.IsPeel) continue;
                int gate = _level.IndexOfGate(record.gateId);
                if (gate >= 0 && _level.Gates[gate].HasPhase)
                {
                    if (phase == 0) sawZero = true;
                    else sawOne = true;
                }
                phase ^= 1;
            }
            return sawZero && sawOne;
        }
    }
}
