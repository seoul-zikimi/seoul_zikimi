using SeoulZikimi.Gameplay;
using SeoulZikimi.Weather;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace GridSystem
{
    public enum GamePhase { Building, Finished }

    /// <summary>
    /// 게임 루프(L1~L3): 서버 권위 타이머/페이즈 + 전원동의 종료 + 채점 + 전원동의 재시작.
    /// 건축 중 Enter = '건축 종료' 동의 토글 → 접속 전원 동의 시 종료(또는 시간초과).
    /// 종료 화면 Enter = '재시작' 동의 토글 → 접속 전원 동의 시 새 라운드(그리드·재료·타이머 리셋).
    /// 동의(m_Consents)는 두 페이즈가 재사용하고, 종료 진입 시 초기화한다. GridManager/GridNetwork 와 같은 오브젝트.
    /// </summary>
    [RequireComponent(typeof(GridManager))]
    public class GameLoopManager : NetworkBehaviour
    {
        private readonly NetworkVariable<int> m_Phase =
            new((int)GamePhase.Building, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<float> m_TimeLeft =
            new(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> m_PlayerCount =
            new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<float> m_CompletedElapsed =
            new(-1f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);   // 협동: 100% 찍은 순간의 경과 초(-1=미완공)
        private readonly NetworkVariable<int> m_AnswerIndex =
            new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> m_MapIndex =
            new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);   // 배경 맵(MapCatalog 인덱스)
        private readonly NetworkList<ulong> m_Consents = new();   // 동의한 clientId (건축중=종료동의 / 종료중=재시작동의, 서버 관리)
        private readonly NetworkList<ulong> m_RoomVotes = new();   // '방으로 돌아가기'를 직접 누른 clientId (서버 관리)
        private readonly NetworkList<NameEntry> m_Names = new();   // 접속 플레이어 표시 이름(서버 관리, 정산서 명단용)

        // ── 게임 모드(GameplayFramework 통합 1단계) ──
        /// <summary>로비에서 방장이 고른 모드(0=타임어택, 1=2vs2, 2=자유). 서버 스폰 시 m_Mode로 복제.</summary>
        public static int HostSelectedMode = (int)GameModeKind.TimeAttack;

        private readonly NetworkVariable<int> m_Mode =
            new((int)GameModeKind.TimeAttack, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkList<TeamEntry> m_Teams = new();   // 2vs2 팀 배정(서버 관리, 접속순 번갈아)
        private readonly NetworkVariable<int> m_Winner =
            new(-2, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);   // -2=미정 -1=무승부 0/1=승리 팀
        private GameModeCatalog m_Modes;
        private int m_ForcedWinner = -2;   // 서버: 점수 비교 전에 확정된 승자(항복 상대팀·선(先)100% 달성팀. -2=없음)

        private GridManager m_Grid;
        private GridNetwork m_Net;
        private bool m_UrgentBgmStarted;

        // ── 매치 시작 게이트: 전원 로딩 대기 → 동기 카운트다운 → 타이머 가동 ──
        // 로딩 속도 편차로 늦게 들어온 사람만 시간이 깎이는 문제 방지.
        private const float kCountdownSeconds = 3f;      // 3-2-1(각 1초) 뒤 START!
        private const float kLoadingTimeoutSeconds = 30f; // 로딩 무한 대기 방지(끊긴 클라는 어차피 ids에서 빠짐)
        private const float kMinLoadingShowSeconds = 2f; // 로딩이 즉시 끝나도 로딩 화면(거북이)을 최소 이만큼은 보여준다

        private readonly NetworkVariable<float> m_CountdownStart =
            new(-1f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);   // 서버시각, -1=전원 로딩 대기 중
        private readonly NetworkVariable<int> m_LoadedCount =
            new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);      // 로딩 완료 인원(로딩바용)
        private readonly System.Collections.Generic.HashSet<ulong> m_LoadedClients = new();             // 서버 전용
        private float m_ServerSpawnedAt;                                                                // 서버 전용(타임아웃 기준)

        private float NowNet => NetworkManager != null ? (float)NetworkManager.ServerTime.Time : Time.time;

        /// <summary>전원 로딩이 끝나 카운트다운이 잡혔는가(로딩 화면 → 카운트다운 전환 신호).</summary>
        public bool CountdownArmed => m_CountdownStart.Value >= 0f;
        /// <summary>카운트다운 남은 초(3→0). 미시작 -1, 끝나면 음수로 계속 감소.</summary>
        public float CountdownRemaining => CountdownArmed ? (m_CountdownStart.Value + kCountdownSeconds) - NowNet : -1f;
        /// <summary>카운트다운까지 끝나 실제 게임(타이머·입력)이 시작됐는가.</summary>
        public bool MatchStarted => CountdownArmed && CountdownRemaining <= 0f;
        /// <summary>로딩 완료 인원 / 전체 인원(로딩바 표시용).</summary>
        public int LoadedPlayerCount => m_LoadedCount.Value;

        public GamePhase Phase => (GamePhase)m_Phase.Value;
        public float TimeLeft => m_TimeLeft.Value;
        public bool IsBuilding => Phase == GamePhase.Building;
        public int PlayerCount => m_PlayerCount.Value;
        public int ConsentCount => m_Consents.Count;
        public ScoreSnapshot Score => m_Net != null ? m_Net.Score : default;

        // 정산서용: 접속 플레이어 이름(서버가 NetworkList로 복제) / 소요시간 / 구조물 이름
        public int NameCount => m_Names.Count;
        public string GetName(int i) => (i >= 0 && i < m_Names.Count) ? m_Names[i].Name.ToString() : "";
        /// <summary>clientId로 표시 이름 조회(캐릭터 위 네임태그용). 없으면 빈 문자열.</summary>
        public string GetNameFor(ulong clientId)
        {
            for (int i = 0; i < m_Names.Count; i++)
                if (m_Names[i].Id == clientId) return m_Names[i].Name.ToString();
            return "";
        }

        // ── 모드 조회(전 클라 동일) ──
        public GameModeKind Mode => (GameModeKind)m_Mode.Value;
        public GameModeDefinition ModeDef => (m_Modes ??= GameModeCatalog.CreateDefault()).Get(Mode);
        public bool IsVersus => Mode == GameModeKind.TeamVersus;

        /// <summary>자유 건축 모드(로비 '자유 건축 모드') — 시간 무제한·채점 없음·전 맵 건축 에셋 주문 가능·정답 고스트/정오답 틴트 없음.
        /// 튜토리얼은 시간제한을 없애려고 같은 FreeBuild 모드를 빌려 쓰지만(TutorialFlowController) 정답 안내가 핵심이라 제외한다.</summary>
        public bool IsFreeBuild
        {
            get
            {
                if (Mode != GameModeKind.FreeBuild) return false;
                var def = MapCatalog.Instance != null ? MapCatalog.Instance.Get(m_MapIndex.Value) : null;
                return def == null || !def.IsTutorial;
            }
        }

        /// <summary>clientId의 팀(0=A, 1=B). 미배정/협동 모드는 -1.</summary>
        public int GetTeam(ulong clientId)
        {
            for (int i = 0; i < m_Teams.Count; i++)
                if (m_Teams[i].Id == clientId) return m_Teams[i].Team;
            return -1;
        }

        public int LocalTeam =>
            (IsSpawned && NetworkManager.Singleton != null) ? GetTeam(NetworkManager.Singleton.LocalClientId) : -1;

        /// <summary>2vs2 승자 팀(-2=미정, -1=무승부, 0/1). 종료 시 확정.</summary>
        public int WinnerTeam => m_Winner.Value;

        public float TimeLimit
        {
            get
            {
                var def = ModeDef;
                if (def.TimeLimitPolicy == TimeLimitPolicy.Fixed) return def.FixedTimeLimitSeconds;        // 2vs2 = 7분 고정
                if (def.TimeLimitPolicy == TimeLimitPolicy.Unlimited) return 0f;                            // 자유모드 = 무제한
                return (m_Grid != null && m_Grid.Answer != null) ? m_Grid.Answer.TimeLimitSeconds : 0f;     // 타임어택 = 정답별
            }
        }
        public float Elapsed => Mathf.Max(0f, TimeLimit - TimeLeft);

        /// <summary>정산서·최고기록에 쓸 소요시간. 협동에서 100%를 찍었으면 '찍은 그 순간'의 시간으로 고정된다.
        /// 완공해도 판은 안 끝난다(기획 09/07) — 기념사진 등으로 남아 있을 수 있게 남은 시간은 종료벨 역할만 하고,
        /// 기록만 완공 시점에서 멈춘다. 미완공이면 평소대로 경과 시간.</summary>
        public float RecordTime => m_CompletedElapsed.Value >= 0f ? m_CompletedElapsed.Value : Elapsed;
        public string AnswerName => (m_Grid != null && m_Grid.Answer != null) ? m_Grid.Answer.DisplayName : "";

        /// <summary>현재 맵(MapCatalog 인덱스). 서버가 정하고 전 클라 동기화 — MapLoader가 이걸 보고 배경 스폰.</summary>
        public int MapIndex => m_MapIndex.Value;
        private static int s_HostSelectedMap = 0;
        private static int s_RandomMapPick = -1;   // '랜덤'일 때 이번 판에 뽑힌 실제 맵(선택이 바뀌면 무효)

        /// <summary>로비에서 호스트가 고른 맵(게임 시작 전 세팅). 서버 스폰 시 m_MapIndex로 복제됨.
        /// MapCatalog.RandomMapIndex(-1)면 '랜덤' — 실제 맵은 ResolvedHostMap이 게임 씬에서 한 번만 뽑는다.</summary>
        public static int HostSelectedMap
        {
            get => s_HostSelectedMap;
            // 값을 다시 넣는 건 곧 "다음 판 세팅"(로비 복귀·맵 재선택)이므로 지난 판의 랜덤 확정값을 버린다.
            // 이게 없으면 '랜덤'으로 계속 돌릴 때 첫 판에 뽑힌 맵이 그대로 굳는다.
            set { s_HostSelectedMap = value; s_RandomMapPick = -1; }
        }

        /// <summary>서버가 이번 판에 실제로 쓸 맵 인덱스. '랜덤'이면 처음 물어볼 때 한 번 뽑고 그 판 내내 같은 값을 준다 —
        /// GridNetwork(그리드 크기)와 GameLoopManager(배경 확정)의 스폰 순서가 보장되지 않아 둘이 같은 맵을 봐야 한다.</summary>
        public static int ResolvedHostMap
        {
            get
            {
                if (s_HostSelectedMap != MapCatalog.RandomMapIndex) return s_HostSelectedMap;
                if (s_RandomMapPick < 0)
                    s_RandomMapPick = MapCatalog.Instance != null ? MapCatalog.Instance.PickRandomPlayable() : 0;
                return s_RandomMapPick;
            }
        }

        /// <summary>방 생성 시 호스트가 고른 날씨 ON/OFF. 현재는 선택값만 보관하며(세션 프로퍼티에도 저장),
        /// 실제 인게임 날씨 적용은 날씨 시스템을 게임 루프에 연결하는 별도 작업에서 사용한다.</summary>
        public static bool HostWeatherEnabled = true;

        /// <summary>세션 생성 시 확정되는 계절 선택 방식. 현재 UI는 ON/OFF만 제공하므로 ON이면 랜덤이 기본이다.</summary>
        public static SeasonSelectionMode HostSeasonSelectionMode = SeasonSelectionMode.Random;

        /// <summary>고정 계절 UI가 추가될 때 사용할 값. 랜덤 모드에서는 선택에 사용되지 않는다.</summary>
        public static Season HostFixedSeason = Season.Spring;

        /// <summary>2vs2 대전에서 경쟁 아이템 사용 여부(true=아이템전, false=순수 타임어택전).
        /// 로비 모드 4종 중 2vs2 두 변형을 구분하는 플래그. 실제 인게임 아이템 스폰 On/Off 연결은
        /// GameplayFramework의 아이템 시스템을 붙일 때 이 값을 참조한다(현재는 선택값 보관).</summary>
        public static bool HostVersusUsesItems = true;

        private void Awake()
        {
            m_Grid = GetComponent<GridManager>();
            m_Net = GetComponent<GridNetwork>();

            // 기존 협동 루프는 그대로 유지하고, 새 Gameplay UI 계약과 연결하는 얇은 어댑터만 런타임에 보장한다.
            // 어댑터는 별도 상태 머신을 실행하지 않으며 종료 동의/나가기 전달과 상태 조회만 담당한다.
            if (!TryGetComponent<CurrentCoopGameplayAdapter>(out _))
                gameObject.AddComponent<CurrentCoopGameplayAdapter>();
            // 2vs2 경쟁 아이템 호스트(런타임 보장 — 씬 수정 불필요, 서버/클라 동일 순서로 부착)
            if (!TryGetComponent<ItemNetwork>(out _))
                gameObject.AddComponent<ItemNetwork>();
            // 남산 기믹 호스트들 — 맵 카드에 NamsanGimmickConfig가 없으면 스스로 잠잔다(다른 맵 영향 0)
            if (!TryGetComponent<CableCarNetwork>(out _))
                gameObject.AddComponent<CableCarNetwork>();
            if (!TryGetComponent<ElevatorNetwork>(out _))
                gameObject.AddComponent<ElevatorNetwork>();
            if (!TryGetComponent<GustNetwork>(out _))
                gameObject.AddComponent<GustNetwork>();
            // 롯데월드 기믹 호스트 — 맵 카드에 LotteGimmickConfig가 없으면 스스로 잠잔다
            if (!TryGetComponent<ParadeNetwork>(out _))
                gameObject.AddComponent<ParadeNetwork>();
            // DDP 기믹 호스트 — 맵 카드에 DdpGimmickConfig가 없으면 스스로 잠잔다
            if (!TryGetComponent<WaterGateNetwork>(out _))
                gameObject.AddComponent<WaterGateNetwork>();
            // ExcavationNetwork(유구 발굴터)는 더 이상 붙이지 않는다 — DDP 맵에서 뺀 기믹(08/31 기획 결정).
            // 물길 하나로 충분하고, 발굴은 손이 많이 가는데 재미 대비 효과가 작았다. LedRoseNetwork와 같은 처리.
            // 경복궁 기믹 호스트들 — 맵 카드에 GyeongbokgungGimmickConfig가 없으면 스스로 잠잔다
            // (사방신이 화재 면역/봉인을 제공하므로 Guardian을 Fire보다 먼저 부착)
            if (!TryGetComponent<GuardianNetwork>(out _))
                gameObject.AddComponent<GuardianNetwork>();
            if (!TryGetComponent<FireNetwork>(out _))
                gameObject.AddComponent<FireNetwork>();
            // LedRoseNetwork(LED 장미 발판)는 더 이상 붙이지 않는다 — DDP 맵에서 뺀 기믹.
            // 광장 위에 분홍 원판이 떠 있는 그림이 보기 싫고 동선에도 도움이 안 됐다.
        }

        public override void OnNetworkSpawn()
        {
            m_Phase.OnValueChanged += OnPhaseChanged;
            m_AnswerIndex.OnValueChanged += OnAnswerIndexChanged;
            if (IsServer) m_MapIndex.Value = ResolvedHostMap;   // 배경 맵 확정('랜덤'이면 여기서 실제 맵으로, 전원 동기화)
            if (IsServer) m_Mode.Value = Mathf.Clamp(HostSelectedMode, 0, 2);   // 모드 확정(전원 동기화)
            ApplyMapAnswers();                         // 맵 전용 정답 세트가 있으면 교체(서버 랜덤픽 전에!)
            m_Grid.ConfigureVersus(IsVersus);          // 2vs2: 그리드 X 2배 + 분할벽(전 피어, 블록 배치 전)
            if (IsServer) PickRandomAnswer();          // 서버: 랜덤 정답 선택(전원 동기화)
            m_Grid.SelectAnswer(m_AnswerIndex.Value);  // 모든 클라(늦참 포함) 동일 정답 적용
            if (IsServer) ResetTimerAndPhase();        // 선택된 정답 기준 타이머
            OnPhaseChanged((int)Phase, (int)Phase);
            SubmitName();                              // 내 표시 이름 서버 등록(정산서 명단)

            if (IsServer) m_ServerSpawnedAt = NowNet;
            SubmitLoadedRpc();                         // 내 씬 로딩 완료 통지 → 전원 모이면 카운트다운
        }

        // ── 매치 시작 게이트(서버) ──────────────────────────────────

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void SubmitLoadedRpc(RpcParams rpc = default)
        {
            m_LoadedClients.Add(rpc.Receive.SenderClientId);
        }

        // 서버 Update에서 호출: 접속자 전원 로딩 완료(또는 타임아웃) 시 카운트다운 예약.
        private void ServerTickMatchGate(System.Collections.Generic.IReadOnlyList<ulong> ids)
        {
            if (m_CountdownStart.Value >= 0f) return;

            m_LoadedClients.RemoveWhere(id => !Contains(ids, id));   // 끊긴 클라 정리
            m_LoadedCount.Value = m_LoadedClients.Count;

            bool everyoneIn = ids.Count > 0 && m_LoadedClients.Count >= ids.Count;
            bool timedOut = NowNet - m_ServerSpawnedAt >= kLoadingTimeoutSeconds;
            if (everyoneIn || timedOut)
                // 복제 지연 + 막판 합류자 여유(1.5s)를 두되, 카운트다운 시작 시각이 라운드 시작 후
                // 최소 노출(kMinLoadingShowSeconds)보다 빨라지지 않게 — 클라는 이 시각까지 로딩 화면을 유지한다(MatchStartHUD).
                m_CountdownStart.Value = Mathf.Max(NowNet + 1.5f, m_ServerSpawnedAt + kMinLoadingShowSeconds);
        }

        public override void OnNetworkDespawn()
        {
            m_Phase.OnValueChanged -= OnPhaseChanged;
            m_AnswerIndex.OnValueChanged -= OnAnswerIndexChanged;
            GameplayInputBlocker.MatchGateBlocked = false;   // 씬 이탈 시 잠금 해제(로비에서 입력 막힘 방지)
        }

        private void OnAnswerIndexChanged(int _, int v) => m_Grid.SelectAnswer(v);

        /// <summary>페이즈 변경 통지(전 클라 로컬 호출) — 맵 기믹·HUD 등이 폴링 없이 반응할 수 있게.</summary>
        public static event System.Action<GamePhase> PhaseChanged;

        /// <summary>로비로 복귀한 뒤 늦게 도착한 콜백이 로비 BGM을 게임 BGM으로 덮는 것을 막는 가드.
        /// 씬 전환은 전부 Single 모드라 활성 씬 이름만 보면 충분하다.</summary>
        private static bool InGameScene => SceneManager.GetActiveScene().name == SceneNames.GameScene;

        private void OnPhaseChanged(int _, int next)
        {
            PhaseChanged?.Invoke((GamePhase)next);
            if ((GamePhase)next == GamePhase.Building)
            {
                m_UrgentBgmStarted = false;
                if (InGameScene)
                    GridSoundBridge.SetPhaseForMap("Building", m_MapIndex.Value);
            }
            else if ((GamePhase)next == GamePhase.Finished)
            {
                if (InGameScene)
                    GridSoundBridge.SetPhaseForMap("Result", m_MapIndex.Value);
                GridSoundBridge.PlaySFX("GameOver");
            }
        }

        // 선택된 맵(MapDef)이 전용 정답 세트를 가지면 GridManager 목록을 교체.
        // 서버·클라 모두 같은 MapDef를 로드하므로 이후 인덱스 동기화가 그대로 유효하다.
        private void ApplyMapAnswers()
        {
            var catalog = MapCatalog.Instance;
            var def = catalog != null ? catalog.Get(m_MapIndex.Value) : null;
            if (def != null && def.Answers != null && def.Answers.Count > 0)
                m_Grid.SetAnswers(def.Answers);
        }

        // 서버: 정답 목록에서 랜덤으로 하나 고른다(1개뿐이면 0). 코스메틱 아님 — 인덱스를 복제.
        private void PickRandomAnswer()
        {
            int n = m_Grid != null ? m_Grid.AnswerCount : 0;
            m_AnswerIndex.Value = n > 1 ? UnityEngine.Random.Range(0, n) : 0;
        }

        private void ResetTimerAndPhase()
        {
            var def = ModeDef;
            float t;
            if (def.TimeLimitPolicy == TimeLimitPolicy.Fixed) t = def.FixedTimeLimitSeconds;          // 2vs2 = 7분 고정
            else if (def.TimeLimitPolicy == TimeLimitPolicy.Unlimited) t = float.MaxValue;            // 자유모드
            else t = (m_Grid != null && m_Grid.Answer != null) ? m_Grid.Answer.TimeLimitSeconds : 180f;
            m_ServerTimeLeft = Mathf.Max(1f, t);
            m_TimeLeft.Value = m_ServerTimeLeft;
            m_CompletedElapsed.Value = -1f;   // 새 라운드 → 완공 시각 기록 초기화
            m_Phase.Value = (int)GamePhase.Building;
            for (int i = m_Consents.Count - 1; i >= 0; i--) m_Consents.RemoveAt(i);
            for (int i = m_RoomVotes.Count - 1; i >= 0; i--) m_RoomVotes.RemoveAt(i);   // 새 라운드 → 방 복귀 표도 초기화

            // 라운드(재)시작마다 카운트다운을 다시 잡는다 — 첫 판은 전원 로딩 후,
            // 재시작은 전원이 이미 로딩돼 있으므로 다음 서버 틱에 바로 3-2-1이 걸린다.
            m_CountdownStart.Value = -1f;
            m_ServerSpawnedAt = NowNet;
        }

        // 서버: 2vs2 팀 배정 — 미배정 접속자를 인원 적은 팀에 순서대로. 협동 모드는 배정 안 함.
        private void AssignTeams(System.Collections.Generic.IReadOnlyList<ulong> ids)
        {
            if (!IsVersus) return;
            for (int k = 0; k < ids.Count; k++)
            {
                if (GetTeam(ids[k]) >= 0) continue;
                int a = 0, b = 0;
                for (int i = 0; i < m_Teams.Count; i++) { if (m_Teams[i].Team == 0) a++; else b++; }
                m_Teams.Add(new TeamEntry { Id = ids[k], Team = a <= b ? 0 : 1 });
            }
        }

        private void Update()
        {
            if (!IsSpawned) return;   // 스폰 전/디스폰 후엔 네트워크 상태 접근 금지(NullRef 방지)

            // 전원 로딩 대기 + 카운트다운 동안 플레이어 입력 잠금(전 클라).
            GameplayInputBlocker.MatchGateBlocked = IsBuilding && !MatchStarted;

            // 입력(모든 클라): Enter = 동의 토글 (건축중=종료 동의 / 종료화면=재시작 동의)
            // RequestToggleConsent 경유 — 버튼 submit과 같은 프레임에 겹쳐도 한 번만 토글.
            // 숫자패드 Enter도 인정(메인 Enter만 보면 "엔터 눌러도 안 돼"가 된다).
            // 동의 엔터 규칙:
            //  · 종료 화면 = 게이트 무관(재시작 동의는 언제든) — 재시작 직후 "엔터 안 된다" 방지
            //  · 건축 중 = 매치 시작(3-2-1 끝) 후에만 — 카운트다운 중 조기종료가 터지면
            //    엔터 연타 시 종료↔재시작 무한 루프에 갇힌다(입력 잠금 지속 = 캐릭터 못 움직임)
            var kb = Keyboard.current;
            bool consentGateOk = !IsBuilding || !GameplayInputBlocker.Blocked;
            if (!GameplayInputBlocker.ManualBlocked && consentGateOk && kb != null
                && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame))
                RequestToggleConsent();

            if (IsBuilding && !m_UrgentBgmStarted && m_TimeLeft.Value <= 60f && InGameScene)
            {
                m_UrgentBgmStarted = true;
                GridSoundBridge.SetPhaseForMap("BuildingUrgent", m_MapIndex.Value);
            }

            if (!IsServer) return;

            // 접속 인원 갱신 + 끊긴 클라 동의 정리 + 전원동의 검사(건축→종료 / 종료→재시작)
            var ids = NetworkManager.Singleton.ConnectedClientsIds;
            m_PlayerCount.Value = ids.Count;
            for (int i = m_Consents.Count - 1; i >= 0; i--)
                if (!Contains(ids, m_Consents[i])) m_Consents.RemoveAt(i);
            for (int i = m_RoomVotes.Count - 1; i >= 0; i--)
                if (!Contains(ids, m_RoomVotes[i])) m_RoomVotes.RemoveAt(i);

            // 방으로 돌아가기: 각자 눌러야 이동한다. 한 명이 눌러도 나머지는 끌려가지 않고,
            // 접속 전원이 누른 순간 세션·연결을 유지한 채 함께 대기방으로 복귀한다.
            if (ids.Count > 0 && m_RoomVotes.Count >= ids.Count)
            {
                ServerLoadRoom();
                return;
            }

            for (int i = m_Names.Count - 1; i >= 0; i--)
                if (!Contains(ids, m_Names[i].Id)) m_Names.RemoveAt(i);
            for (int i = m_Teams.Count - 1; i >= 0; i--)
                if (!Contains(ids, m_Teams[i].Id)) m_Teams.RemoveAt(i);
            AssignTeams(ids);   // 2vs2: 새 접속자 팀 배정(협동 모드는 no-op)
            ServerTickMatchGate(ids);   // 전원 로딩 완료 → 카운트다운 예약
            ServerStampCompletion();    // 협동: 100% 찍은 순간의 시간을 기록용으로 고정(판은 계속 진행)
            if (IsBuilding && IsVersus)
            {
                if (TryEarlyVictory()) return;   // [기획 09/02] 먼저 100% 찍은 팀 즉시 승리
                TryTeamSurrender(ids);           // 2vs2 건축중: 팀 전원 동의 = 그 팀 항복(즉시 패배)
            }
            else if (ids.Count > 0 && m_Consents.Count >= ids.Count)
            {
                // 전원동의 → 종료/재시작. 즉시 하면 동의 아이콘이 검게 채워진 걸 볼 새가 없어서
                // (특히 혼자 테스트 = 엔터 즉시 전환) 잠깐 보여주고 진행한다.
                if (m_ServerConsentFireAt < 0f) m_ServerConsentFireAt = Time.time + 0.1f;
            }
            else m_ServerConsentFireAt = -1f;   // 동의가 깨지면(취소/이탈) 예약 취소

            if (m_ServerConsentFireAt >= 0f && Time.time >= m_ServerConsentFireAt)
            {
                m_ServerConsentFireAt = -1f;
                if (IsBuilding) Finish();   // 건축 전원동의 → 종료
                else            Restart();  // 종료 전원동의 → 재시작
            }

            // 타이머 — 카운트다운(START!)이 끝난 뒤부터 흐른다(로딩 편차 보정).
            // 원본은 서버 로컬 float로 깎고, 복제는 0.1초 격자로 내려갈 때만 —
            // 매 프레임 NetworkVariable에 쓰면 경기 내내 매 틱 델타가 전송된다(표시는 초 단위라 0.1초면 충분).
            if (IsBuilding && MatchStarted)
            {
                m_ServerTimeLeft -= Time.deltaTime;
                if (m_ServerTimeLeft <= 0f)
                {
                    m_ServerTimeLeft = 0f; m_TimeLeft.Value = 0f;
                    // 2vs2 타임오버도 승리 시네마틱을 거친다(QA — 선100%만 나오고 타임오버 승리는 안 나옴).
                    if (IsVersus && m_Net != null)
                    {
                        int ta = m_Net.ScoreFor(0).Total, tb = m_Net.ScoreFor(1).Total;
                        FinishWithVictoryCinematic(ta == tb ? -1 : (ta > tb ? 0 : 1), byCompletion: false);
                    }
                    else Finish();
                }
                else if (m_ServerTimeLeft < 1e9f)   // 자유모드(float.MaxValue)는 사실상 안 줄어 복제 불필요
                {
                    float q = Mathf.Floor(m_ServerTimeLeft * 10f) * 0.1f;
                    if (q < m_TimeLeft.Value) m_TimeLeft.Value = q;
                }
            }
        }

        // 서버(협동): 만점(모든 칸 배치+공정 완료)을 처음 찍은 순간의 경과 시간을 박아 둔다.
        // 조기 종료는 하지 않는다 — 완공 후에도 기념사진 등으로 남고 싶을 수 있어 남은 시간은 '종료벨'로만 쓰고,
        // 정산서·최고기록에 들어가는 시간만 여기서 멈춘다(QA: 완공했는데 시간이 계속 흘러 기록이 늘어남).
        // 반올림 100%(99.6% 표시 100)에 속지 않게 score>=maxScore 원값으로 판정.
        private void ServerStampCompletion()
        {
            if (IsVersus || !IsBuilding || !MatchStarted) return;
            if (m_CompletedElapsed.Value >= 0f || m_Net == null) return;
            var s = m_Net.Score;
            if (s.maxScore > 0 && s.score >= s.maxScore)
                m_CompletedElapsed.Value = Mathf.Max(0f, TimeLimit - m_ServerTimeLeft);
        }

        private float m_ServerTimeLeft;   // 서버 권위 타이머 원본 — m_TimeLeft는 이 값의 0.1초 격자 복제본
        private float m_ServerConsentFireAt = -1f;   // 전원동의 처리 예약 시각(아이콘 채움 잠깐 보여주기, -1=없음)

        private static bool Contains(System.Collections.Generic.IReadOnlyList<ulong> ids, ulong id)
        {
            for (int i = 0; i < ids.Count; i++) if (ids[i] == id) return true;
            return false;
        }

        private void Finish()
        {
            if (!IsBuilding) return;
            if (IsServer && m_Net != null)
                m_Net.RecomputeScore();

            // 2vs2: 승패 확정 — 항복/선100%로 이미 정해졌으면 그대로, 아니면 완성도(점수) 비교. 동점=무승부.
            if (IsServer && IsVersus && m_Net != null)
            {
                if (m_ForcedWinner >= 0) m_Winner.Value = m_ForcedWinner;
                else
                {
                    // Total = 건축 점수 + 보너스(DDP 유구 출토 등) — 보너스도 승패에 반영된다.
                    int a = m_Net.ScoreFor(0).Total, b = m_Net.ScoreFor(1).Total;
                    m_Winner.Value = a == b ? -1 : (a > b ? 0 : 1);
                }
            }
            m_ForcedWinner = -2;

            m_Phase.Value = (int)GamePhase.Finished;
            for (int i = m_Consents.Count - 1; i >= 0; i--) m_Consents.RemoveAt(i);   // 종료 진입 → 동의 초기화(재시작 동의는 새로 받음)
        }

        // 서버: 2vs2 승리 조건(기획 변경 09/02) — 먼저 100%(모든 칸 배치+공정 완료)를 찍는 팀이 즉시 승리.
        // 아무도 못 찍으면 기존대로 타이머 종료 시 완성도 비교(높은 쪽 승 / 동점 DRAW).
        // 반올림 100%(99.6% 표시 100)에 속지 않게 score>=maxScore 원값으로 판정(GameLoopHUD.IsComplete와 동일).
        // 확정 즉시 전 클라에 "완공!!" 시네마틱(슬로모+줌)을 틀고, 정산은 연출이 걷힐 때 들어간다.
        private bool m_EarlyFinishPending;   // 서버: 연출 대기 중(중복 트리거·항복 처리 차단)

        private bool TryEarlyVictory()
        {
            if (m_EarlyFinishPending) return true;
            if (!MatchStarted || m_Net == null) return false;
            var a = m_Net.ScoreFor(0);
            var b = m_Net.ScoreFor(1);
            bool aDone = a.maxScore > 0 && a.score >= a.maxScore;
            bool bDone = b.maxScore > 0 && b.score >= b.maxScore;
            if (!aDone && !bDone) return false;
            // 같은 프레임 동시 완성(사실상 희귀)이면 보너스 포함 총점으로 표시 승자를 정하고, 그마저 같으면 -1(동시).
            int winner = aDone != bDone ? (aDone ? 0 : 1)
                : a.Total == b.Total ? -1 : (a.Total > b.Total ? 0 : 1);
            FinishWithVictoryCinematic(winner, byCompletion: true);
            return true;
        }

        /// <summary>2vs2 승부 확정 연출 공용 진입로(선100%·타임오버) — 전 클라 시네마틱 후 정산.</summary>
        private void FinishWithVictoryCinematic(int winner, bool byCompletion)
        {
            if (m_EarlyFinishPending) return;
            if (winner >= 0) m_ForcedWinner = winner;
            m_EarlyFinishPending = true;
            EarlyVictoryRpc(winner, byCompletion);
            StartCoroutine(FinishAfterVictoryCinematic());
        }

        private System.Collections.IEnumerator FinishAfterVictoryCinematic()
        {
            // 연출(2.4s)보다 살짝 길게 — 배너가 걷히는 타이밍에 정산서가 자연스럽게 등장.
            // Realtime: 호스트도 연출 슬로모(timeScale 0.25)를 같이 받으므로 스케일 무관 대기.
            yield return new WaitForSecondsRealtime(VictoryFx.kDuration + 0.2f);
            m_EarlyFinishPending = false;
            if (IsBuilding) Finish();   // 그 사이 타이머 만료 등으로 이미 끝났으면 no-op
        }

        [Rpc(SendTo.Everyone)]
        private void EarlyVictoryRpc(int winnerTeam, bool byCompletion)
            => VictoryFx.Play(winnerTeam, LocalTeam, byCompletion);

        // 서버: 2vs2 조기 종료 = 항복(기획). 해당 팀 전원이 동의하면 그 팀 패배로 즉시 종료.
        private bool TryTeamSurrender(System.Collections.Generic.IReadOnlyList<ulong> ids)
        {
            for (int team = 0; team <= 1; team++)
            {
                int members = 0, agreed = 0;
                for (int i = 0; i < ids.Count; i++)
                {
                    if (GetTeam(ids[i]) != team) continue;
                    members++;
                    if (Contains2(m_Consents, ids[i])) agreed++;
                }
                if (members > 0 && agreed >= members)
                {
                    m_ForcedWinner = 1 - team;
                    Finish();
                    return true;
                }
            }
            return false;
        }

        private static bool Contains2(NetworkList<ulong> list, ulong id)
        {
            for (int i = 0; i < list.Count; i++) if (list[i] == id) return true;
            return false;
        }

        // Enter = 동의 토글(건축중=종료 동의 / 종료화면=재시작 동의). 두 페이즈 모두 유효.
        [Rpc(SendTo.Server)]
        private void ToggleConsentRpc(RpcParams rpc = default)
        {
            ulong sender = rpc.Receive.SenderClientId;
            for (int i = 0; i < m_Consents.Count; i++)
                if (m_Consents[i] == sender)
                {
                    m_Consents.RemoveAt(i);
                    Debug.Log($"[Consent] 해제: 클라 {sender} → {m_Consents.Count}/{m_PlayerCount.Value}");
                    return;
                }
            m_Consents.Add(sender);
            Debug.Log($"[Consent] 동의: 클라 {sender} → {m_Consents.Count}/{m_PlayerCount.Value}");
        }

        // 각 클라가 스폰 시 자기 표시 이름(PlayerPrefs 닉네임)을 서버로 제출 → NetworkList로 전원 복제.
        private void SubmitName()
        {
            string nick = PlayerPrefs.GetString("PlayerNickname", "");
            if (string.IsNullOrEmpty(nick))
                nick = $"플레이어{(NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0)}";
            if (nick.Length > 12) nick = nick.Substring(0, 12);                              // UI 길이 제한
            while (System.Text.Encoding.UTF8.GetByteCount(nick) > 28 && nick.Length > 0)     // FixedString32Bytes(≤29byte) 오버플로 방지(이모지 등)
                nick = nick.Substring(0, nick.Length - 1);
            SubmitNameRpc(nick);
        }

        [Rpc(SendTo.Server)]
        private void SubmitNameRpc(FixedString32Bytes name, RpcParams rpc = default)
        {
            ulong sender = rpc.Receive.SenderClientId;
            for (int i = 0; i < m_Names.Count; i++)
                if (m_Names[i].Id == sender) { var e = m_Names[i]; e.Name = name; m_Names[i] = e; return; }
            m_Names.Add(new NameEntry { Id = sender, Name = name });
        }

        private struct TeamEntry : INetworkSerializable, System.IEquatable<TeamEntry>
        {
            public ulong Id;
            public int Team;   // 0=A, 1=B
            public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
            {
                serializer.SerializeValue(ref Id);
                serializer.SerializeValue(ref Team);
            }
            public bool Equals(TeamEntry other) => Id == other.Id && Team == other.Team;
        }

        private struct NameEntry : INetworkSerializable, System.IEquatable<NameEntry>
        {
            public ulong Id;
            public FixedString32Bytes Name;
            public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
            {
                serializer.SerializeValue(ref Id);
                serializer.SerializeValue(ref Name);
            }
            public bool Equals(NameEntry other) => Id == other.Id && Name.Equals(other.Name);
        }

        // 종료 화면에서 접속 전원이 재시작 동의 → 새 랜덤 정답으로 다음 라운드(서버 전용, 전원동의 검사에서만 호출).
        private void Restart()
        {
            m_Winner.Value = -2;                       // 2vs2 승패 초기화
            if (TryGetComponent<ItemNetwork>(out var items)) items.ServerReset();   // 경쟁 아이템 정리
            if (TryGetComponent<CableCarNetwork>(out var cable)) cable.ServerReset();   // 남산: 곤돌라·대기열 정리
            if (TryGetComponent<ElevatorNetwork>(out var elev)) elev.ServerReset();     // 남산: 엘리베이터 재잠금
            if (TryGetComponent<GustNetwork>(out var gust)) gust.ServerReset();         // 남산: 돌풍 주기 리셋
            if (TryGetComponent<ParadeNetwork>(out var parade)) parade.ServerReset();   // 롯데월드: 퍼레이드 주기 리셋
            if (TryGetComponent<WaterGateNetwork>(out var water)) water.ServerReset();  // DDP: 물길 주기 리셋
            if (TryGetComponent<GuardianNetwork>(out var guard)) guard.ServerReset();   // 경복궁: 사방신 리셋
            if (TryGetComponent<FireNetwork>(out var fire)) fire.ServerReset();         // 경복궁: 화마 리셋
            if (TryGetComponent<MaterialDepot>(out var depot)) depot.ServerResetOrders();   // 주문 한도(MaxSpawnCount) 누적 리셋
            PickRandomAnswer();                        // 재시작마다 새 랜덤 정답
            m_Grid.SelectAnswer(m_AnswerIndex.Value);
            if (m_Net != null) m_Net.ServerResetGrid();   // 그리드 + 바닥/배송 재료 정리
            if (m_Net != null) m_Net.ServerSpawnPresetBlocks();   // 기본 제공 블록 다시 깔기(경복궁 등)
            ResetTimerAndPhase();                          // 타이머·페이즈 리셋 + 동의 초기화
        }

        private bool LocalConsented()
        {
            ulong me = NetworkManager.Singleton.LocalClientId;
            for (int i = 0; i < m_Consents.Count; i++) if (m_Consents[i] == me) return true;
            return false;
        }

        public bool HasLocalConsent => IsSpawned && NetworkManager.Singleton != null && LocalConsented();

        // 같은 프레임 중복 토글 가드 — 엔터가 '버튼 submit'과 '직접 폴링' 두 경로로 들어오면
        // 동의가 켜졌다 바로 꺼져 원위치가 된다(동의 아이콘이 계속 반투명이던 버그).
        private int m_LastConsentToggleFrame = -1;

        public void RequestToggleConsent()
        {
            if (!IsSpawned) return;
            if (Time.frameCount == m_LastConsentToggleFrame) return;
            m_LastConsentToggleFrame = Time.frameCount;
            ToggleConsentRpc();
        }

        public void RequestFinishByTimeout()
        {
            if (!IsSpawned || !IsBuilding) return;

            if (IsServer)
                Finish();
            else
                FinishByTimeoutRpc();
        }

        [Rpc(SendTo.Server)]
        private void FinishByTimeoutRpc()
        {
            if (IsBuilding)
                Finish();
        }

        // 로비로 돌아가기: 세션 나가고(연결 끊고) 로비 씬(메뉴/방목록)으로. 각자 개별 이탈.
        public void RequestLeaveToLobby()
        {
            if (NetworkManager.Singleton != null) NetworkManager.Singleton.Shutdown();
            SceneManager.LoadScene(SceneNames.Lobby);
        }

        /// <summary>내가 '방으로 돌아가기'를 이미 눌렀는지.</summary>
        public bool HasLocalRoomReturnVote
        {
            get
            {
                if (!IsSpawned || NetworkManager.Singleton == null) return false;
                ulong me = NetworkManager.Singleton.LocalClientId;
                for (int i = 0; i < m_RoomVotes.Count; i++) if (m_RoomVotes[i] == me) return true;
                return false;
            }
        }

        /// <summary>'방으로 돌아가기'를 누른 인원 수(대기 안내용).</summary>
        public int RoomReturnVoteCount => m_RoomVotes.Count;

        // 방으로 돌아가기: 각자 눌러야 이동한다(누른 사람만 표가 등록되고, 전원이 누르면 함께 복귀).
        // Netcode의 씬 관리가 서버 권위라 누른 사람만 다른 씬으로 갈라설 수는 없다 —
        // 대신 한 명의 클릭이 나머지를 끌고 가지 않도록 서버가 표를 모아 전원 클릭 시에만 전환한다.
        public void RequestReturnToRoom()
        {
            if (!IsSpawned) return;
            ReturnToRoomVoteRpc();
        }

        [Rpc(SendTo.Server)]
        private void ReturnToRoomVoteRpc(RpcParams rpc = default)
        {
            ulong sender = rpc.Receive.SenderClientId;
            for (int i = 0; i < m_RoomVotes.Count; i++)
                if (m_RoomVotes[i] == sender) return;   // 이미 누름(취소는 없다)
            m_RoomVotes.Add(sender);
        }

        private bool m_RoomLoadStarted;   // 씬 전환 요청은 한 번만(다음 프레임 중복 요청 방지)

        private void ServerLoadRoom()
        {
            if (m_RoomLoadStarted) return;
            m_RoomLoadStarted = true;

            var nm = NetworkManager.Singleton;
            if (nm != null && nm.SceneManager != null && nm.NetworkConfig.EnableSceneManagement)
                nm.SceneManager.LoadScene(SceneNames.Lobby, LoadSceneMode.Single);   // 전원 함께 방으로
            else
                SceneManager.LoadScene(SceneNames.Lobby);
        }

    }
}
