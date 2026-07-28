using System;

namespace SeoulZikimi.Gameplay
{
    public interface IVersusResultResolver
    {
        VersusMatchResult Resolve(
            GameEndContext endContext,
            TeamCompletionScore firstTeam,
            TeamCompletionScore secondTeam);
    }

    /// <summary>
    /// 2대2 결과를 완성도로 비교한다. 항복이면 완성도와 관계없이 항복 팀이 패배한다.
    /// </summary>
    public sealed class CompletionVersusResultResolver : IVersusResultResolver
    {
        private readonly float _drawTolerance;

        public CompletionVersusResultResolver(float drawTolerance = 0.0001f)
        {
            if (drawTolerance < 0f)
                throw new ArgumentOutOfRangeException(nameof(drawTolerance));

            _drawTolerance = drawTolerance;
        }

        public VersusMatchResult Resolve(
            GameEndContext endContext,
            TeamCompletionScore firstTeam,
            TeamCompletionScore secondTeam)
        {
            if (firstTeam.TeamId == secondTeam.TeamId)
                throw new ArgumentException("서로 다른 두 팀의 점수가 필요합니다.");

            if (endContext.Reason == GameEndReason.EveryoneLeft
                || endContext.Reason == GameEndReason.Invalidated)
            {
                return new VersusMatchResult(
                    VersusOutcomeKind.Invalidated,
                    null,
                    null,
                    firstTeam,
                    secondTeam);
            }

            if (endContext.Reason == GameEndReason.Surrender)
                return ResolveSurrender(endContext, firstTeam, secondTeam);

            float difference = firstTeam.CompletionPercent - secondTeam.CompletionPercent;
            if (Math.Abs(difference) <= _drawTolerance)
            {
                return new VersusMatchResult(
                    VersusOutcomeKind.Draw,
                    null,
                    null,
                    firstTeam,
                    secondTeam);
            }

            TeamCompletionScore winner = difference > 0f ? firstTeam : secondTeam;
            TeamCompletionScore loser = difference > 0f ? secondTeam : firstTeam;
            return new VersusMatchResult(
                VersusOutcomeKind.TeamWin,
                winner.TeamId,
                loser.TeamId,
                firstTeam,
                secondTeam);
        }

        private static VersusMatchResult ResolveSurrender(
            GameEndContext endContext,
            TeamCompletionScore firstTeam,
            TeamCompletionScore secondTeam)
        {
            string surrenderingTeam = endContext.SurrenderingTeamId;
            if (surrenderingTeam != firstTeam.TeamId && surrenderingTeam != secondTeam.TeamId)
                throw new ArgumentException("항복 팀이 점수 대상 팀과 일치하지 않습니다.", nameof(endContext));

            string winner = surrenderingTeam == firstTeam.TeamId
                ? secondTeam.TeamId
                : firstTeam.TeamId;

            return new VersusMatchResult(
                VersusOutcomeKind.TeamWin,
                winner,
                surrenderingTeam,
                firstTeam,
                secondTeam);
        }
    }
}
