using NUnit.Framework;

namespace SeoulZikimi.Gameplay.Tests
{
    public sealed class VersusResultResolverTests
    {
        [Test]
        public void HigherCompletionWins()
        {
            var resolver = new CompletionVersusResultResolver();

            VersusMatchResult result = resolver.Resolve(
                EndedBy(GameEndReason.Timeout),
                new TeamCompletionScore("A", 80f),
                new TeamCompletionScore("B", 79f));

            Assert.That(result.Outcome, Is.EqualTo(VersusOutcomeKind.TeamWin));
            Assert.That(result.WinnerTeamId, Is.EqualTo("A"));
        }

        [Test]
        public void EqualCompletionIsDraw()
        {
            var resolver = new CompletionVersusResultResolver();

            VersusMatchResult result = resolver.Resolve(
                EndedBy(GameEndReason.Timeout),
                new TeamCompletionScore("A", 80f),
                new TeamCompletionScore("B", 80f));

            Assert.That(result.Outcome, Is.EqualTo(VersusOutcomeKind.Draw));
            Assert.That(result.WinnerTeamId, Is.Null);
        }

        [Test]
        public void SurrenderingTeamLosesRegardlessOfCompletion()
        {
            var resolver = new CompletionVersusResultResolver();
            var end = new GameEndContext(
                GameModeKind.TeamVersus,
                GameEndReason.Surrender,
                "A",
                10f,
                "building");

            VersusMatchResult result = resolver.Resolve(
                end,
                new TeamCompletionScore("A", 100f),
                new TeamCompletionScore("B", 1f));

            Assert.That(result.WinnerTeamId, Is.EqualTo("B"));
            Assert.That(result.LoserTeamId, Is.EqualTo("A"));
        }

        private static GameEndContext EndedBy(GameEndReason reason)
            => new GameEndContext(
                GameModeKind.TeamVersus,
                reason,
                null,
                420f,
                "building");
    }
}
