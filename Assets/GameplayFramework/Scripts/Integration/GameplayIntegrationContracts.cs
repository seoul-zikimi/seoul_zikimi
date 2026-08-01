namespace SeoulZikimi.Gameplay
{
    /// <summary>
    /// TODO(Network): Netcode의 LocalClientId와 서버 TeamRoster에서 로컬 신원을 제공하는 어댑터가 필요하다.
    /// </summary>
    public interface ILocalGameplayIdentity
    {
        string PlayerId { get; }
        string TeamId { get; }
    }

    /// <summary>
    /// TODO(Network): 개인 이탈을 서버에 통지하고 연결을 종료하되, 채점과 보상을 요청하지 않아야 한다.
    /// </summary>
    public interface ILeaveGameGateway
    {
        void LeaveWithoutScoring();
    }

    /// <summary>
    /// TODO(Inventory): 현재 손에 든 경쟁 아이템 조회와 사용 완료 제거를 실제 인벤토리에 연결해야 한다.
    /// 대포를 포함한 모든 아이템 종류를 반환한다.
    /// </summary>
    public interface IHeldCompetitiveItemGateway
    {
        bool TryGetHeldItem(out CompetitiveItemKind kind);
        void ConsumeHeldItem();
    }

    /// <summary>
    /// 기존 시스템과 새 도메인 로직 사이의 얇은 연결 서비스다.
    /// UI는 네트워크나 인벤토리를 직접 알지 않고 이 서비스의 함수만 호출할 수 있다.
    /// </summary>
    public sealed class GameplayCommandService
    {
        private readonly GameplayFlowController _flow;
        private readonly CompetitiveItemUseService _items;
        private readonly ILocalGameplayIdentity _identity;
        private readonly ILeaveGameGateway _leave;
        private readonly IHeldCompetitiveItemGateway _heldItem;

        public GameplayCommandService(
            GameplayFlowController flow,
            CompetitiveItemUseService items,
            ILocalGameplayIdentity identity,
            ILeaveGameGateway leave,
            IHeldCompetitiveItemGateway heldItem)
        {
            _flow = flow ?? throw new System.ArgumentNullException(nameof(flow));
            _items = items ?? throw new System.ArgumentNullException(nameof(items));
            _identity = identity ?? throw new System.ArgumentNullException(nameof(identity));
            _leave = leave ?? throw new System.ArgumentNullException(nameof(leave));
            _heldItem = heldItem ?? throw new System.ArgumentNullException(nameof(heldItem));
        }

        public FinishConsentState ToggleBuildFinishConsent()
            => _flow.ToggleBuildFinishConsent(_identity.PlayerId);

        public void LeaveGame()
        {
            _flow.NotifyPlayerLeft(_identity.PlayerId);
            _leave.LeaveWithoutScoring();
        }

        public bool UseHeldItem()
        {
            if (!_heldItem.TryGetHeldItem(out CompetitiveItemKind kind))
                return false;

            _items.Use(kind, _identity.PlayerId, _identity.TeamId);
            _heldItem.ConsumeHeldItem();
            return true;
        }
    }
}
