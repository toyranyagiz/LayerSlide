using System.Collections.Generic;
using LayerSlide.Core;
using LayerSlide.Presentation;

namespace LayerSlide.Gameplay
{
    /// <summary>
    /// Tutorial text and completion rules for L001–L008 (GDD 02 §4–5). The message is always derived from the
    /// current board, so Undo / reload show the right step without storing extra state.
    /// </summary>
    public sealed class TutorialTracker
    {
        public const string DragTag = "T01_Drag";

        string _tag;

        /// <summary>Tag still waiting for completion in this level, or null.</summary>
        public string ActiveTag { get; private set; }

        public void Begin(Level level, ICollection<string> completedTags)
        {
            _tag = level.TutorialTags.Length > 0 ? level.TutorialTags[0] : null;
            ActiveTag = _tag != null && !completedTags.Contains(_tag) ? _tag : null;
        }

        public bool ShowsDragHand => ActiveTag == DragTag;

        public string GetMessage(Board board)
        {
            switch (ActiveTag)
            {
                case "T01_Drag": return "Bloğu aynı renkteki kapıya sürükle.";
                case "T02_Color": return "Her renk kendi kapısından çıkar.";
                case "T03_Layers":
                    int a = board.Level.IndexOfBlock("A");
                    if (a >= 0 && !board.IsRemoved(a) && board.GetActiveColor(a) != LayerColor.Red)
                        return $"Alt katman kaldı. Şimdi {Palette.NameOf(board.GetActiveColor(a)).ToLowerInvariant()} kapıya götür.";
                    return "Üstteki rengi kapıya götür.";
                case "T04_Axis": return "Eksen değiştirmek için bırakıp yeniden tut.";
                case "T05_ClearSpace": return "Son katman çıkınca yer açılır.";
                case "T06_Park": return "Yol açmak için bir bloğu geçici olarak taşı.";
                case "T07_GateWidth": return "Bloğun bütün kenarı kapıya sığmalı.";
                case "T08_NextColor": return "Sonraki rengi üstteki göstergeden takip et.";
                default: return null;
            }
        }

        /// <summary>Checks the finish condition after a committed command. Returns the tag when it completes now.</summary>
        public string OnCommand(Board boardAfter, CommandRecord command)
        {
            if (ActiveTag == null) return null;

            bool done;
            switch (ActiveTag)
            {
                case "T01_Drag":
                case "T02_Color":
                case "T08_NextColor":
                    done = command.IsPeel;
                    break;
                case "T03_Layers":
                case "T07_GateWidth":
                    done = IsBlockRemoved(boardAfter, "A");
                    break;
                case "T04_Axis":
                    // First Y-axis command of A after its first layer is gone.
                    done = command.blockId == "A" && command.beforeRemoved >= 1
                        && RuleEngine.GetCommandAxis(boardAfter.Level, command) == Axis.Y;
                    break;
                case "T05_ClearSpace":
                    done = IsBlockRemoved(boardAfter, "B");
                    break;
                case "T06_Park":
                    done = !command.IsPeel;
                    break;
                default:
                    done = false;
                    break;
            }
            return done ? Complete() : null;
        }

        /// <summary>Winning completes any tutorial left open, whatever solution the player used.</summary>
        public string OnLevelWon() => ActiveTag != null ? Complete() : null;

        string Complete()
        {
            string tag = ActiveTag;
            ActiveTag = null;
            return tag;
        }

        static bool IsBlockRemoved(Board board, string id)
        {
            int block = board.Level.IndexOfBlock(id);
            return block >= 0 && board.IsRemoved(block);
        }
    }
}
