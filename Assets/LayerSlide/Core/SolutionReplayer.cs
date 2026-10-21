namespace LayerSlide.Core
{
    /// <summary>Plays a <see cref="SolutionRecord"/> through the rule engine and checks that it clears the board.</summary>
    public static class SolutionReplayer
    {
        public static bool Replay(Level level, SolutionRecord solution, out string error)
        {
            error = null;
            if (solution == null || solution.actions == null) { error = "Solution is missing."; return false; }
            if (solution.levelId != level.LevelId || solution.contentVersion != level.ContentVersion || solution.ruleVersion != level.RuleVersion)
            {
                error = "Solution ids or versions do not match the level.";
                return false;
            }

            var board = new Board(level);
            for (int i = 0; i < solution.actions.Count; i++)
            {
                var action = solution.actions[i];
                int block = level.IndexOfBlock(action.block);
                int gate = string.IsNullOrEmpty(action.exit) ? -1 : level.IndexOfGate(action.exit);
                if (block < 0 || (gate < 0 && !string.IsNullOrEmpty(action.exit)))
                {
                    error = $"Action {i + 1} references unknown ids.";
                    return false;
                }

                if (!RuleEngine.TryCreateCommand(board, block, action.x, action.y, gate, out var command))
                {
                    error = $"Action {i + 1} ({action.block} -> {action.x},{action.y} exit '{action.exit}') is illegal.";
                    return false;
                }
                RuleEngine.Apply(board, command);
            }

            if (!board.IsCleared)
            {
                error = $"Board still has {board.ActiveBlockCount} block(s) after the solution.";
                return false;
            }
            return true;
        }
    }
}
