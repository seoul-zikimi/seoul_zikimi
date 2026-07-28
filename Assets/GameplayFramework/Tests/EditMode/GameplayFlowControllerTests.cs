using System.Collections.Generic;
using NUnit.Framework;

namespace SeoulZikimi.Gameplay.Tests
{
    public sealed class GameplayFlowControllerTests
    {
        [Test]
        public void TimeAttack_FollowsSelectionBuildingScoringRewardFlow()
        {
            var flow = CreateFlow();
            flow.StartSession(GameModeKind.TimeAttack, new[] { "p1", "p2" });
            Assert.That(flow.Phase, Is.EqualTo(GameplayPhase.BuildingSelection));

            flow.ConfirmBuildingSelection("building-a", 120f);
            flow.StartBuilding();
            flow.Tick(120f);

            Assert.That(flow.Phase, Is.EqualTo(GameplayPhase.Scoring));
            flow.CompleteScoring();
            Assert.That(flow.Phase, Is.EqualTo(GameplayPhase.Reward));
            flow.CompleteReward();
            Assert.That(flow.Phase, Is.EqualTo(GameplayPhase.Finished));
        }

        [Test]
        public void CooperativeFinish_RequiresEveryRemainingPlayer()
        {
            var flow = StartTimeAttack("p1", "p2", "p3");

            flow.ToggleBuildFinishConsent("p1");
            flow.NotifyPlayerLeft("p3");
            Assert.That(flow.Phase, Is.EqualTo(GameplayPhase.Building));

            flow.ToggleBuildFinishConsent("p2");
            Assert.That(flow.Phase, Is.EqualTo(GameplayPhase.Scoring));
        }

        [Test]
        public void LastRemainingPlayer_CanFinishAlone()
        {
            var flow = StartTimeAttack("p1", "p2");
            flow.NotifyPlayerLeft("p2");

            FinishConsentState state = flow.ToggleBuildFinishConsent("p1");

            Assert.That(state.RequiredCount, Is.EqualTo(1));
            Assert.That(flow.Phase, Is.EqualTo(GameplayPhase.Scoring));
        }

        [Test]
        public void TeamVersus_TeamConsentMeansSurrender()
        {
            var flow = CreateFlow();
            GameEndContext ended = default;
            flow.GameEnded += context => ended = context;
            flow.StartSession(GameModeKind.TeamVersus, new[] { "a1", "a2", "b1", "b2" });
            Assert.That(flow.Phase, Is.EqualTo(GameplayPhase.TeamMaking));

            flow.ConfirmTeamAssignment(
                new TeamRoster(
                    new Dictionary<string, string>
                    {
                        ["a1"] = "A",
                        ["a2"] = "A",
                        ["b1"] = "B",
                        ["b2"] = "B"
                    }));
            flow.ConfirmBuildingSelection("versus-building");
            flow.StartBuilding();
            flow.ToggleBuildFinishConsent("a1");
            flow.ToggleBuildFinishConsent("a2");

            Assert.That(flow.Phase, Is.EqualTo(GameplayPhase.Scoring));
            Assert.That(ended.Reason, Is.EqualTo(GameEndReason.Surrender));
            Assert.That(ended.SurrenderingTeamId, Is.EqualTo("A"));
        }

        [Test]
        public void TeamVersus_AlwaysUsesSevenMinuteTimer()
        {
            var flow = StartVersus();
            flow.Tick(419f);
            Assert.That(flow.Phase, Is.EqualTo(GameplayPhase.Building));

            flow.Tick(1f);
            Assert.That(flow.Phase, Is.EqualTo(GameplayPhase.Scoring));
        }

        [Test]
        public void FreeBuild_HasNoTimerScoringOrReward()
        {
            var flow = CreateFlow();
            flow.StartSession(GameModeKind.FreeBuild, new[] { "p1" });
            flow.ConfirmBuildingSelection("free-map");
            flow.StartBuilding();

            flow.Tick(100000f);

            Assert.That(flow.Phase, Is.EqualTo(GameplayPhase.Building));
            Assert.That(flow.CurrentMode.UsesScoring, Is.False);
            Assert.That(flow.CurrentMode.GrantsRewards, Is.False);
        }

        private static GameplayFlowController StartTimeAttack(params string[] players)
        {
            var flow = CreateFlow();
            flow.StartSession(GameModeKind.TimeAttack, players);
            flow.ConfirmBuildingSelection("building", 120f);
            flow.StartBuilding();
            return flow;
        }

        private static GameplayFlowController StartVersus()
        {
            var flow = CreateFlow();
            flow.StartSession(GameModeKind.TeamVersus, new[] { "a1", "a2", "b1", "b2" });
            flow.ConfirmTeamAssignment(
                new TeamRoster(
                    new Dictionary<string, string>
                    {
                        ["a1"] = "A",
                        ["a2"] = "A",
                        ["b1"] = "B",
                        ["b2"] = "B"
                    }));
            flow.ConfirmBuildingSelection("building", 10f);
            flow.StartBuilding();
            return flow;
        }

        private static GameplayFlowController CreateFlow()
            => new GameplayFlowController(GameModeCatalog.CreateDefault());
    }
}
