namespace SeoulZikimi.Gameplay
{
    /// <summary>
    /// UI가 현재 게임 상태를 표시할 때 필요한 공통 읽기 모델이다.
    /// 기존 협동 GameLoopManager와 향후 2대2 루프가 같은 형태로 상태를 제공할 수 있다.
    /// </summary>
    public readonly struct GameplayRuntimeStatus
    {
        public GameModeKind Mode { get; }
        public GameplayPhase Phase { get; }
        public float TimeRemainingSeconds { get; }
        public int ConnectedPlayerCount { get; }
        public int FinishConsentCount { get; }
        public bool HasLocalFinishConsent { get; }
        public float CompletionPercent { get; }

        public GameplayRuntimeStatus(
            GameModeKind mode,
            GameplayPhase phase,
            float timeRemainingSeconds,
            int connectedPlayerCount,
            int finishConsentCount,
            bool hasLocalFinishConsent,
            float completionPercent)
        {
            Mode = mode;
            Phase = phase;
            TimeRemainingSeconds = timeRemainingSeconds;
            ConnectedPlayerCount = connectedPlayerCount;
            FinishConsentCount = finishConsentCount;
            HasLocalFinishConsent = hasLocalFinishConsent;
            CompletionPercent = completionPercent;
        }
    }

    public interface IGameplayRuntimeStatusSource
    {
        GameplayRuntimeStatus CaptureStatus();
    }
}
