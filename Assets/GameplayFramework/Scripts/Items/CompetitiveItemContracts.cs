using SeoulZikimi.Weather;

namespace SeoulZikimi.Gameplay
{
    public interface ICompetitiveItemSelector
    {
        CompetitiveItemKind Select();
    }

    public interface ICompetitiveItemEffect
    {
        CompetitiveItemKind Kind { get; }
        void Apply(CompetitiveItemUseRequest request);
    }

    public interface ICompetitiveItemEffectCatalog
    {
        ICompetitiveItemEffect Get(CompetitiveItemKind kind);
    }

    /// <summary>
    /// TODO(Network): sourceTeamId의 상대 팀을 서버 권위 팀 정보에서 찾아야 한다.
    /// 2대2 네트워크 팀 배정이 구현되면 이 인터페이스의 어댑터만 추가한다.
    /// </summary>
    public interface IOpponentTeamResolver
    {
        string GetOpponentTeamId(string sourceTeamId);
    }

    /// <summary>
    /// 실제 아이템 오브젝트 생성/제거 경계다.
    /// TimedWorldSpawn은 맵 전용 구현체가 유효한 랜덤 위치를 골라 생성해야 한다.
    /// 현재 단계에서는 프리팹이나 맵 오브젝트를 만들지 않는다.
    /// </summary>
    public interface ICompetitiveItemSpawnGateway
    {
        string Spawn(CompetitiveItemSpawnRequest request);
        void Despawn(string itemInstanceId, ItemDespawnReason reason);
    }

    /// <summary>
    /// TODO(Grid/Network): 상대 팀 영역의 미고정 재료를 모두 무너뜨리고,
    /// 기존 RuntimeGrid.SettleUnsupported 규칙으로 위쪽 재료의 연쇄 붕괴까지 처리해야 한다.
    /// </summary>
    public interface IUnfixedConstructionTarget
    {
        void CollapseAllUnfixed(string teamId);
    }

    /// <summary>
    /// 기존 날씨가 있으면 새 날씨로 교체하고 durationSeconds 타이머를 처음부터 시작해야 한다.
    /// </summary>
    public interface ITemporaryTeamWeatherTarget
    {
        void ApplyTemporaryWeather(
            string teamId,
            WeatherKind weather,
            float durationSeconds);
    }

    /// <summary>TODO(VFX): 지정 팀의 카메라/진영에만 짙은 안개를 표시하는 어댑터가 필요하다.</summary>
    public interface ITeamFogTarget
    {
        void ApplyFog(string teamId, float durationSeconds);
    }

    /// <summary>
    /// TODO(Player/Network): 지정 팀 전원의 최종 이동 속도 배율에 시간제 modifier를 적용해야 한다.
    /// </summary>
    public interface ITeamMovementModifierTarget
    {
        void ApplyMovementSpeedMultiplier(
            string teamId,
            float multiplier,
            float durationSeconds);
    }

    /// <summary>
    /// TODO(Player/Network): PlayerCarry의 공정 진행량에 시간제 속도 배율을 적용해야 한다.
    /// </summary>
    public interface ITeamProcessModifierTarget
    {
        void ApplyProcessSpeedMultiplier(
            string teamId,
            float multiplier,
            float durationSeconds);
    }

    /// <summary>TODO(Order/Network): 지정 팀의 새 재료 주문 요청을 서버에서 거절해야 한다.</summary>
    public interface ITeamOrderLockTarget
    {
        void LockNewOrders(string teamId, float durationSeconds);
    }

    /// <summary>
    /// TODO(Weather/Network): 우산 지속 중 지정 팀의 미끄러짐과 날씨성 붕괴 효과를 무시해야 한다.
    /// </summary>
    public interface ITeamWeatherImmunityTarget
    {
        void ApplyWeatherImmunity(string teamId, float durationSeconds);
    }
}
