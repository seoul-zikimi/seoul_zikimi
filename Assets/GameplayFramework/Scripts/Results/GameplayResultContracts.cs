namespace SeoulZikimi.Gameplay
{
    /// <summary>
    /// TODO(Grid/Network): 2대2 맵의 각 팀 건축 영역을 별도로 채점해 완성도를 반환해야 한다.
    /// 현재 GridNetwork.Score는 맵 전체 점수 하나만 제공하므로 팀 영역 구조가 확정된 뒤 어댑터를 구현한다.
    /// </summary>
    public interface ITeamCompletionScoreGateway
    {
        TeamCompletionScore GetCompletionScore(string teamId);
    }

    /// <summary>
    /// TODO(Reward): 채점 결과에 맞는 재화 지급과 건물/스킨 해금을 담당하는 외부 보상 시스템 경계다.
    /// 자유 모드와 무효화된 게임에서는 호출하지 않는다.
    /// </summary>
    public interface IGameplayRewardGateway
    {
        void GrantRewards(GameEndContext endContext);
    }
}
