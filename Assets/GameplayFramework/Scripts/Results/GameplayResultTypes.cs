using System;

namespace SeoulZikimi.Gameplay
{
    public enum VersusOutcomeKind
    {
        TeamWin,
        Draw,
        Invalidated
    }

    public readonly struct TeamCompletionScore
    {
        public string TeamId { get; }
        public float CompletionPercent { get; }

        public TeamCompletionScore(string teamId, float completionPercent)
        {
            if (string.IsNullOrWhiteSpace(teamId))
                throw new ArgumentException("팀 ID가 필요합니다.", nameof(teamId));
            if (completionPercent < 0f || completionPercent > 100f)
                throw new ArgumentOutOfRangeException(nameof(completionPercent));

            TeamId = teamId;
            CompletionPercent = completionPercent;
        }
    }

    public readonly struct VersusMatchResult
    {
        public VersusOutcomeKind Outcome { get; }
        public string WinnerTeamId { get; }
        public string LoserTeamId { get; }
        public TeamCompletionScore FirstTeam { get; }
        public TeamCompletionScore SecondTeam { get; }

        public VersusMatchResult(
            VersusOutcomeKind outcome,
            string winnerTeamId,
            string loserTeamId,
            TeamCompletionScore firstTeam,
            TeamCompletionScore secondTeam)
        {
            Outcome = outcome;
            WinnerTeamId = winnerTeamId;
            LoserTeamId = loserTeamId;
            FirstTeam = firstTeam;
            SecondTeam = secondTeam;
        }
    }
}
