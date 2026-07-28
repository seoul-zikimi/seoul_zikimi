using System;

namespace SeoulZikimi.Gameplay
{
    public enum GameModeKind
    {
        TimeAttack,
        TeamVersus,
        FreeBuild
    }

    public enum GameplayPhase
    {
        WaitingForPlayers,
        TeamMaking,
        BuildingSelection,
        Building,
        Scoring,
        Reward,
        Finished,
        Invalidated
    }

    public enum TimeLimitPolicy
    {
        BuildingDefinition,
        Fixed,
        Unlimited
    }

    public enum EarlyFinishResolution
    {
        FinishAndScore,
        Surrender,
        Disabled
    }

    public enum GameEndReason
    {
        Timeout,
        TeamConsent,
        Surrender,
        EveryoneLeft,
        Invalidated
    }

    /// <summary>
    /// 한 게임 모드의 규칙 데이터다. 모드 로직은 이 값을 읽기만 하므로 수치 변경에 코드 수정이 필요 없다.
    /// </summary>
    public sealed class GameModeDefinition
    {
        public GameModeKind Kind { get; }
        public int MinimumPlayers { get; }
        public int MaximumPlayers { get; }
        public int TeamCount { get; }
        public int PlayersPerTeam { get; }
        public TimeLimitPolicy TimeLimitPolicy { get; }
        public float FixedTimeLimitSeconds { get; }
        public EarlyFinishResolution EarlyFinishResolution { get; }
        public bool UsesScoring { get; }
        public bool GrantsRewards { get; }

        public bool RequiresTeamMaking => TeamCount > 1;

        public GameModeDefinition(
            GameModeKind kind,
            int minimumPlayers,
            int maximumPlayers,
            int teamCount,
            int playersPerTeam,
            TimeLimitPolicy timeLimitPolicy,
            float fixedTimeLimitSeconds,
            EarlyFinishResolution earlyFinishResolution,
            bool usesScoring,
            bool grantsRewards)
        {
            if (minimumPlayers <= 0)
                throw new ArgumentOutOfRangeException(nameof(minimumPlayers));
            if (maximumPlayers < minimumPlayers)
                throw new ArgumentOutOfRangeException(nameof(maximumPlayers));
            if (teamCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(teamCount));
            if (playersPerTeam <= 0)
                throw new ArgumentOutOfRangeException(nameof(playersPerTeam));
            if (timeLimitPolicy == TimeLimitPolicy.Fixed && fixedTimeLimitSeconds <= 0f)
                throw new ArgumentOutOfRangeException(nameof(fixedTimeLimitSeconds));

            Kind = kind;
            MinimumPlayers = minimumPlayers;
            MaximumPlayers = maximumPlayers;
            TeamCount = teamCount;
            PlayersPerTeam = playersPerTeam;
            TimeLimitPolicy = timeLimitPolicy;
            FixedTimeLimitSeconds = fixedTimeLimitSeconds;
            EarlyFinishResolution = earlyFinishResolution;
            UsesScoring = usesScoring;
            GrantsRewards = grantsRewards;
        }
    }

    public readonly struct GameEndContext
    {
        public GameModeKind Mode { get; }
        public GameEndReason Reason { get; }
        public string SurrenderingTeamId { get; }
        public float ElapsedSeconds { get; }
        public string BuildingId { get; }

        public GameEndContext(
            GameModeKind mode,
            GameEndReason reason,
            string surrenderingTeamId,
            float elapsedSeconds,
            string buildingId)
        {
            Mode = mode;
            Reason = reason;
            SurrenderingTeamId = surrenderingTeamId;
            ElapsedSeconds = elapsedSeconds;
            BuildingId = buildingId;
        }
    }

    public readonly struct FinishConsentState
    {
        public string GroupId { get; }
        public int ConsentCount { get; }
        public int RequiredCount { get; }
        public bool IsResolved { get; }
        public GameEndReason Resolution { get; }

        public FinishConsentState(
            string groupId,
            int consentCount,
            int requiredCount,
            bool isResolved,
            GameEndReason resolution)
        {
            GroupId = groupId;
            ConsentCount = consentCount;
            RequiredCount = requiredCount;
            IsResolved = isResolved;
            Resolution = resolution;
        }
    }
}
