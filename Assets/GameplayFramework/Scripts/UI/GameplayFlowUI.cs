using System;
using UnityEngine;

namespace SeoulZikimi.Gameplay
{
    /// <summary>
    /// 실제 Button이나 Panel을 생성하지 않는 UI 로직 브리지다.
    /// 나중에 만든 UI의 onClick을 아래 공개 함수에 연결하고, 이벤트를 게임/네트워크 계층에서 구독한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GameplayFlowUI : MonoBehaviour
    {
        public event Action<GameModeKind> ModeSelected;
        public event Action<string> BuildingSelected;
        public event Action StartBuildingRequested;
        public event Action BuildFinishConsentRequested;
        public event Action LeaveGameRequested;
        public event Action UseHeldItemRequested;

        /// <summary>타임어택 모드 Button.onClick에 연결한다.</summary>
        public void SelectTimeAttackMode()
            => ModeSelected?.Invoke(GameModeKind.TimeAttack);

        /// <summary>
        /// 2대2 모드 Button.onClick에 연결한다.
        /// TODO(Network): 구독자는 4명 확인과 서버 팀 배정을 완료한 뒤 게임을 시작해야 한다.
        /// </summary>
        public void SelectTeamVersusMode()
            => ModeSelected?.Invoke(GameModeKind.TeamVersus);

        /// <summary>자유 건축 모드 Button.onClick에 연결한다.</summary>
        public void SelectFreeBuildMode()
            => ModeSelected?.Invoke(GameModeKind.FreeBuild);

        /// <summary>
        /// 건축물 카드/버튼에서 건축물 ID를 전달한다.
        /// UI 표시 이름이 아니라 변하지 않는 데이터 ID를 사용해야 한다.
        /// </summary>
        public void SelectBuilding(string buildingId)
        {
            if (string.IsNullOrWhiteSpace(buildingId))
                throw new ArgumentException("건축물 ID가 필요합니다.", nameof(buildingId));

            BuildingSelected?.Invoke(buildingId);
        }

        /// <summary>맵과 정답 로딩이 완료된 뒤 '건축 시작' 함수에 연결한다.</summary>
        public void RequestStartBuilding()
            => StartBuildingRequested?.Invoke();

        /// <summary>
        /// '건축 종료' Button.onClick에 연결한다.
        /// 협동에서는 동의 토글, 2대2에서는 소속 팀의 항복 동의 토글로 처리해야 한다.
        /// TODO(Network): 로컬 플레이어 ID를 포함한 서버 요청으로 변환해야 한다.
        /// </summary>
        public void RequestToggleBuildFinishConsent()
            => BuildFinishConsentRequested?.Invoke();

        /// <summary>
        /// 설정 UI의 '게임 나가기' Button.onClick에 연결한다.
        /// 채점/보상 요청이 아니라 개인 세션 이탈 요청이다.
        /// TODO(Network): GameplayFlowController.NotifyPlayerLeft 호출 후 안전하게 세션을 나가야 한다.
        /// </summary>
        public void RequestLeaveGame()
            => LeaveGameRequested?.Invoke();

        /// <summary>
        /// 아이템을 든 상태에서 E 입력이 들어오면 호출한다(대포는 꾹 눌렀다 뗄 때).
        /// TODO(Network): 서버가 소유권과 팀을 검증한 뒤 CompetitiveItemUseService.Use를 호출해야 한다.
        /// </summary>
        public void RequestUseHeldItem()
            => UseHeldItemRequested?.Invoke();
    }
}
