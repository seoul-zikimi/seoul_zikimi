using SeoulZikimi.Gameplay;
using UnityEngine;

namespace GridSystem
{
    /// <summary>
    /// 현재 동작 중인 협동 GameLoopManager와 새 UI 계약을 연결하는 어댑터다.
    ///
    /// 중요한 원칙:
    /// - 기존 GameLoopManager가 타이머/동의/채점의 유일한 실행 주체다.
    /// - 새 GameplayFlowController를 동시에 실행하지 않는다.
    /// - 2대2 전용 팀/아이템 로직은 팀 네트워크가 준비될 때까지 연결하지 않는다.
    ///
    /// 나중에 직접 만든 UI의 GameplayFlowUI를 m_FlowUI에 연결하면
    /// '건축 종료'와 '게임 나가기' 함수가 기존 멀티플레이 흐름을 그대로 호출한다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(GameLoopManager))]
    public sealed class CurrentCoopGameplayAdapter :
        MonoBehaviour,
        IGameplayRuntimeStatusSource,
        ILeaveGameGateway
    {
        [SerializeField] private GameplayFlowUI m_FlowUI = null;

        private GameLoopManager _loop;
        private GameplayFlowUI _boundUI;

        private void Awake()
        {
            _loop = GetComponent<GameLoopManager>();
        }

        private void OnEnable()
        {
            GameplayFlowUI flowUI = m_FlowUI;
            if (flowUI == null)
                flowUI = FindFirstObjectByType<GameplayFlowUI>(FindObjectsInactive.Include);

            if (flowUI != null)
                BindUI(flowUI);
        }

        private void OnDisable()
        {
            UnbindUI();
        }

        /// <summary>
        /// 런타임 생성 UI 또는 나중에 만든 프리팹 UI를 코드로 연결할 때 호출한다.
        /// 같은 UI를 중복 연결해도 이벤트는 한 번만 등록된다.
        /// </summary>
        public void BindUI(GameplayFlowUI flowUI)
        {
            if (_boundUI == flowUI)
                return;

            UnbindUI();
            _boundUI = flowUI;
            if (_boundUI == null)
                return;

            _boundUI.BuildFinishConsentRequested += OnBuildFinishConsentRequested;
            _boundUI.LeaveGameRequested += OnLeaveGameRequested;

            // 현재 협동 루프는 게임 진입 시 자동으로 정답 선택과 타이머 시작을 수행한다.
            // 따라서 ModeSelected, BuildingSelected, StartBuildingRequested에는 연결하지 않는다.
            // 아이템은 인벤토리/팀 소유권이 없으므로 UseHeldItemRequested에도 연결하지 않는다.
        }

        /// <summary>현재 연결된 UI의 이벤트 구독을 안전하게 해제한다.</summary>
        public void UnbindUI()
        {
            if (_boundUI == null)
                return;

            _boundUI.BuildFinishConsentRequested -= OnBuildFinishConsentRequested;
            _boundUI.LeaveGameRequested -= OnLeaveGameRequested;
            _boundUI = null;
        }

        /// <summary>
        /// 타이머, 접속 인원, 종료 동의, 완성도를 새 UI가 표시할 수 있는 공통 상태로 반환한다.
        /// 현재 기존 게임은 협동 타임어택이므로 Mode는 TimeAttack으로 제공한다.
        /// </summary>
        public GameplayRuntimeStatus CaptureStatus()
        {
            EnsureLoop();
            GameplayPhase phase = _loop.Phase == GamePhase.Building
                ? GameplayPhase.Building
                : GameplayPhase.Finished;

            return new GameplayRuntimeStatus(
                GameModeKind.TimeAttack,
                phase,
                _loop.TimeLeft,
                _loop.PlayerCount,
                _loop.ConsentCount,
                _loop.HasLocalConsent,
                _loop.Score.Percent);
        }

        /// <summary>IGameplayRuntimeStatusSource를 모르는 일반 UI 코드용 동일 기능 함수다.</summary>
        public GameplayRuntimeStatus GetCurrentStatus() => CaptureStatus();

        /// <summary>
        /// 채점 요청이 아니라 개인 이탈이다. 기존 GameLoopManager의 세션 종료/로비 이동 함수를 사용한다.
        /// </summary>
        public void LeaveWithoutScoring()
        {
            EnsureLoop();
            _loop.RequestLeaveToLobby();
        }

        private void OnBuildFinishConsentRequested()
        {
            EnsureLoop();
            _loop.RequestToggleConsent();
        }

        private void OnLeaveGameRequested()
            => LeaveWithoutScoring();

        private void EnsureLoop()
        {
            if (_loop == null)
                _loop = GetComponent<GameLoopManager>();
        }
    }
}
