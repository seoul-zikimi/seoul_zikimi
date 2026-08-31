using System;

namespace SeoulZikimi.Gameplay
{
    /// <summary>
    /// 아이템의 아군/적군 대상을 결정하고 알맞은 효과 전략을 실행한다.
    /// 입력, 인벤토리, 네트워크 소유권 검증은 추후 서버 어댑터가 이 서비스 호출 전에 담당한다.
    /// </summary>
    public sealed class CompetitiveItemUseService
    {
        private readonly ICompetitiveItemDefinitionCatalog _definitions;
        private readonly ICompetitiveItemEffectCatalog _effects;
        private readonly IOpponentTeamResolver _opponents;

        public event Action<CompetitiveItemKind, CompetitiveItemUseRequest> ItemUsed;

        public CompetitiveItemUseService(
            ICompetitiveItemDefinitionCatalog definitions,
            ICompetitiveItemEffectCatalog effects,
            IOpponentTeamResolver opponents)
        {
            _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
            _effects = effects ?? throw new ArgumentNullException(nameof(effects));
            _opponents = opponents ?? throw new ArgumentNullException(nameof(opponents));
        }

        /// <summary>
        /// E키 사용 요청이 서버에서 유효하다고 확인된 뒤 호출한다.
        /// 대포도 다른 아이템과 같은 경로를 타며, 조준·발사 연출은 호출 전 클라이언트가 담당한다.
        /// </summary>
        public CompetitiveItemUseRequest Use(
            CompetitiveItemKind kind,
            string sourcePlayerId,
            string sourceTeamId)
        {
            CompetitiveItemDefinition definition = _definitions.Get(kind);
            string targetTeamId = definition.TargetSide == ItemTargetSide.Ally
                ? sourceTeamId
                : _opponents.GetOpponentTeamId(sourceTeamId);

            var request = new CompetitiveItemUseRequest(
                sourcePlayerId,
                sourceTeamId,
                targetTeamId);

            _effects.Get(kind).Apply(request);
            ItemUsed?.Invoke(kind, request);
            return request;
        }
    }
}
