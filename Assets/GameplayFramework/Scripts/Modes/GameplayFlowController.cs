using System;
using System.Collections.Generic;

namespace SeoulZikimi.Gameplay
{
    /// <summary>
    /// 팀 구성 → 건축물 선택 → 건축 → 채점 → 보상 순서를 관리하는 엔진 비의존 상태 머신이다.
    /// 네트워크, UI, 실제 채점 구현은 이벤트를 구독하거나 공개 함수를 호출하는 어댑터가 담당한다.
    /// </summary>
    public sealed class GameplayFlowController
    {
        private readonly IGameModeCatalog _modes;
        private readonly HashSet<string> _activePlayers = new();
        private GameModeDefinition _mode;
        private TeamRoster _roster;
        private IEarlyFinishPolicy _finishPolicy;
        private string _buildingId;
        private float _timeLimitSeconds;

        public GameplayPhase Phase { get; private set; } = GameplayPhase.WaitingForPlayers;
        public float ElapsedSeconds { get; private set; }
        public float TimeRemaining =>
            _timeLimitSeconds > 0f ? Math.Max(0f, _timeLimitSeconds - ElapsedSeconds) : 0f;
        public GameModeDefinition CurrentMode => _mode;

        public event Action<GameplayPhase> PhaseChanged;
        public event Action<GameModeDefinition, IReadOnlyCollection<string>> TeamAssignmentRequested;
        public event Action<GameModeDefinition> BuildingSelectionRequested;
        public event Action<FinishConsentState> FinishConsentChanged;
        public event Action<GameEndContext> GameEnded;

        public GameplayFlowController(IGameModeCatalog modes)
        {
            _modes = modes ?? throw new ArgumentNullException(nameof(modes));
        }

        /// <summary>
        /// 로비에서 게임 시작이 확정됐을 때 호출한다.
        /// 2대2 모드라면 TeamAssignmentRequested까지만 발생하고 네트워크 팀 배정을 기다린다.
        /// </summary>
        public void StartSession(GameModeKind modeKind, IReadOnlyCollection<string> playerIds)
        {
            if (playerIds == null)
                throw new ArgumentNullException(nameof(playerIds));
            if (Phase != GameplayPhase.WaitingForPlayers
                && Phase != GameplayPhase.Finished
                && Phase != GameplayPhase.Invalidated)
                throw new InvalidOperationException("이미 진행 중인 게임이 있습니다.");

            _mode = _modes.Get(modeKind);
            ValidatePlayerCount(_mode, playerIds.Count);

            _activePlayers.Clear();
            foreach (string playerId in playerIds)
            {
                if (string.IsNullOrWhiteSpace(playerId) || !_activePlayers.Add(playerId))
                    throw new ArgumentException("플레이어 ID는 비어 있거나 중복될 수 없습니다.", nameof(playerIds));
            }

            _roster = null;
            _buildingId = null;
            _finishPolicy = null;
            ElapsedSeconds = 0f;

            if (_mode.RequiresTeamMaking)
            {
                SetPhase(GameplayPhase.TeamMaking);
                TeamAssignmentRequested?.Invoke(_mode, _activePlayers);
                return;
            }

            PrepareBuildingSelection();
        }

        /// <summary>
        /// TODO(Network): 서버에서 2대2 팀 배정이 완료된 후 호출한다.
        /// 모든 참가자가 정확히 한 팀에 포함되어야 다음 단계로 넘어간다.
        /// </summary>
        public void ConfirmTeamAssignment(TeamRoster roster)
        {
            if (Phase != GameplayPhase.TeamMaking)
                throw new InvalidOperationException("현재 팀 구성 단계가 아닙니다.");

            _roster = roster ?? throw new ArgumentNullException(nameof(roster));
            foreach (string playerId in _activePlayers)
            {
                if (!_roster.ContainsPlayer(playerId))
                    throw new ArgumentException($"팀이 없는 플레이어입니다: {playerId}", nameof(roster));
            }

            ValidateTeamSizes();
            PrepareBuildingSelection();
        }

        /// <summary>
        /// 건축물 선택 UI에서 정답 건축물이 확정됐을 때 호출한다.
        /// buildingTimeLimitSeconds는 타임어택 모드에서 건축물별 제한시간으로 사용한다.
        /// </summary>
        public void ConfirmBuildingSelection(
            string buildingId,
            float buildingTimeLimitSeconds = 0f)
        {
            if (Phase != GameplayPhase.BuildingSelection)
                throw new InvalidOperationException("현재 건축물 선택 단계가 아닙니다.");
            if (string.IsNullOrWhiteSpace(buildingId))
                throw new ArgumentException("건축물 ID가 필요합니다.", nameof(buildingId));

            _buildingId = buildingId;
            _timeLimitSeconds = ResolveTimeLimit(_mode, buildingTimeLimitSeconds);
        }

        /// <summary>맵과 정답 로딩이 끝난 뒤 호출하면 건축 타이머가 시작된다.</summary>
        public void StartBuilding()
        {
            if (Phase != GameplayPhase.BuildingSelection || string.IsNullOrWhiteSpace(_buildingId))
                throw new InvalidOperationException("건축물 선택이 먼저 완료되어야 합니다.");

            _finishPolicy = CreateFinishPolicy();
            _finishPolicy.ConsentChanged += OnFinishConsentChanged;
            ElapsedSeconds = 0f;
            SetPhase(GameplayPhase.Building);
        }

        /// <summary>서버 권위 게임 루프에서 deltaTime을 전달한다.</summary>
        public void Tick(float deltaTime)
        {
            if (deltaTime < 0f)
                throw new ArgumentOutOfRangeException(nameof(deltaTime));
            if (Phase != GameplayPhase.Building)
                return;

            ElapsedSeconds += deltaTime;
            if (_timeLimitSeconds > 0f && ElapsedSeconds >= _timeLimitSeconds)
            {
                ElapsedSeconds = _timeLimitSeconds;
                EndGame(GameEndReason.Timeout, null);
            }
        }

        /// <summary>
        /// '건축 종료' UI 함수가 호출한다.
        /// 협동 모드는 전원 동의 시 채점, 2대2 모드는 요청한 팀 전원 동의 시 그 팀이 항복한다.
        /// </summary>
        public FinishConsentState ToggleBuildFinishConsent(string playerId)
        {
            if (Phase != GameplayPhase.Building)
                throw new InvalidOperationException("건축 중에만 종료 동의를 변경할 수 있습니다.");

            FinishConsentState state = _finishPolicy.ToggleConsent(playerId);
            return state;
        }

        /// <summary>
        /// 개인이 '게임 나가기'를 눌렀을 때 로컬 세션 종료 전에 호출한다.
        /// 마지막 플레이어까지 나가면 채점과 보상 없이 게임을 무효화한다.
        /// </summary>
        public void NotifyPlayerLeft(string playerId)
        {
            if (!_activePlayers.Remove(playerId))
                return;

            _finishPolicy?.RemovePlayer(playerId);
            if (_activePlayers.Count == 0 && Phase == GameplayPhase.Building)
                EndGame(GameEndReason.EveryoneLeft, null);
        }

        /// <summary>실제 채점 시스템이 계산을 끝냈을 때 호출한다.</summary>
        public void CompleteScoring()
        {
            if (Phase != GameplayPhase.Scoring)
                throw new InvalidOperationException("현재 채점 단계가 아닙니다.");

            if (_mode.GrantsRewards)
                SetPhase(GameplayPhase.Reward);
            else
                SetPhase(GameplayPhase.Finished);
        }

        /// <summary>보상 지급/표시가 끝났을 때 호출한다.</summary>
        public void CompleteReward()
        {
            if (Phase != GameplayPhase.Reward)
                throw new InvalidOperationException("현재 보상 단계가 아닙니다.");

            SetPhase(GameplayPhase.Finished);
        }

        private void PrepareBuildingSelection()
        {
            SetPhase(GameplayPhase.BuildingSelection);
            BuildingSelectionRequested?.Invoke(_mode);
        }

        private IEarlyFinishPolicy CreateFinishPolicy()
        {
            IEarlyFinishPolicy policy;
            switch (_mode.EarlyFinishResolution)
            {
                case EarlyFinishResolution.FinishAndScore:
                    policy = new AllPlayersConsentPolicy(_activePlayers);
                    break;
                case EarlyFinishResolution.Surrender:
                    if (_roster == null)
                        throw new InvalidOperationException("2대2 항복 정책에는 팀 정보가 필요합니다.");
                    policy = new TeamSurrenderConsentPolicy(_roster, _activePlayers);
                    break;
                default:
                    policy = new DisabledEarlyFinishPolicy();
                    break;
            }

            return policy;
        }

        private void OnFinishConsentChanged(FinishConsentState state)
        {
            FinishConsentChanged?.Invoke(state);
            if (state.IsResolved)
                EndGame(state.Resolution, state.GroupId);
        }

        private void EndGame(GameEndReason reason, string surrenderingTeamId)
        {
            if (Phase != GameplayPhase.Building)
                return;

            if (_finishPolicy != null)
                _finishPolicy.ConsentChanged -= OnFinishConsentChanged;

            var context = new GameEndContext(
                _mode.Kind,
                reason,
                surrenderingTeamId,
                ElapsedSeconds,
                _buildingId);

            if (reason == GameEndReason.EveryoneLeft || reason == GameEndReason.Invalidated)
                SetPhase(GameplayPhase.Invalidated);
            else if (_mode.UsesScoring)
                SetPhase(GameplayPhase.Scoring);
            else
                SetPhase(GameplayPhase.Finished);

            GameEnded?.Invoke(context);
        }

        private void SetPhase(GameplayPhase phase)
        {
            Phase = phase;
            PhaseChanged?.Invoke(phase);
        }

        private static float ResolveTimeLimit(
            GameModeDefinition mode,
            float buildingTimeLimitSeconds)
        {
            switch (mode.TimeLimitPolicy)
            {
                case TimeLimitPolicy.Fixed:
                    return mode.FixedTimeLimitSeconds;
                case TimeLimitPolicy.BuildingDefinition:
                    if (buildingTimeLimitSeconds <= 0f)
                        throw new ArgumentOutOfRangeException(
                            nameof(buildingTimeLimitSeconds),
                            "타임어택 건축물에는 제한시간이 필요합니다.");
                    return buildingTimeLimitSeconds;
                default:
                    return 0f;
            }
        }

        private static void ValidatePlayerCount(GameModeDefinition mode, int count)
        {
            if (count < mode.MinimumPlayers || count > mode.MaximumPlayers)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(count),
                    $"{mode.Kind} 모드는 {mode.MinimumPlayers}~{mode.MaximumPlayers}명이 필요합니다.");
            }
        }

        private void ValidateTeamSizes()
        {
            var counts = new Dictionary<string, int>();
            foreach (string playerId in _activePlayers)
            {
                string teamId = _roster.GetTeamId(playerId);
                counts.TryGetValue(teamId, out int count);
                counts[teamId] = count + 1;
            }

            if (counts.Count != _mode.TeamCount)
                throw new ArgumentException($"{_mode.TeamCount}개 팀이 필요합니다.", nameof(_roster));

            foreach (var pair in counts)
            {
                if (pair.Value != _mode.PlayersPerTeam)
                {
                    throw new ArgumentException(
                        $"{pair.Key} 팀은 {_mode.PlayersPerTeam}명이어야 합니다.",
                        nameof(_roster));
                }
            }
        }
    }
}
