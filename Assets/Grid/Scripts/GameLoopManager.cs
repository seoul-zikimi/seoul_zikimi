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
        private readonly NetworkVariable<int> m_AnswerIndex =
            new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> m_MapIndex =
            new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);   // 배경 맵(MapCatalog 인덱스)
        private readonly NetworkList<ulong> m_Consents = new();   // 동의한 clientId (건축중=종료동의 / 종료중=재시작동의, 서버 관리)
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
        private int m_SurrenderWinner = -2;   // 서버: 항복으로 확정된 승자(-2=없음)

        private GridManager m_Grid;
        private GridNetwork m_Net;
        private bool m_UrgentBgmStarted;

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
        public string AnswerName => (m_Grid != null && m_Grid.Answer != null) ? m_Grid.Answer.DisplayName : "";

        /// <summary>현재 맵(MapCatalog 인덱스). 서버가 정하고 전 클라 동기화 — MapLoader가 이걸 보고 배경 스폰.</summary>
        public int MapIndex => m_MapIndex.Value;
        /// <summary>로비에서 호스트가 고른 맵(게임 시작 전 세팅). 서버 스폰 시 m_MapIndex로 복제됨.</summary>
        public static int HostSelectedMap = 0;

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
            // DDP 기믹 호스트들 — 맵 카드에 DdpGimmickConfig가 없으면 스스로 잠잔다
            // (WaterGate가 발굴터에 '물이 빠졌다'를 통지하므로 WaterGate를 먼저 부착한다)
            if (!TryGetComponent<WaterGateNetwork>(out _))
                gameObject.AddComponent<WaterGateNetwork>();
            if (!TryGetComponent<ExcavationNetwork>(out _))
                gameObject.AddComponent<ExcavationNetwork>();
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
            if (IsServer) m_MapIndex.Value = HostSelectedMap;   // 배경 맵 확정(전원 동기화)
            if (IsServer) m_Mode.Value = Mathf.Clamp(HostSelectedMode, 0, 2);   // 모드 확정(전원 동기화)
            ApplyMapAnswers();                         // 맵 전용 정답 세트가 있으면 교체(서버 랜덤픽 전에!)
            m_Grid.ConfigureVersus(IsVersus);          // 2vs2: 그리드 X 2배 + 분할벽(전 피어, 블록 배치 전)
            if (IsServer) PickRandomAnswer();          // 서버: 랜덤 정답 선택(전원 동기화)
            m_Grid.SelectAnswer(m_AnswerIndex.Value);  // 모든 클라(늦참 포함) 동일 정답 적용
            if (IsServer) ResetTimerAndPhase();        // 선택된 정답 기준 타이머
            OnPhaseChanged((int)Phase, (int)Phase);
            SubmitName();                              // 내 표시 이름 서버 등록(정산서 명단)
        }

        public override void OnNetworkDespawn()
        {
            m_Phase.OnValueChanged -= OnPhaseChanged;
            m_AnswerIndex.OnValueChanged -= OnAnswerIndexChanged;
        }

        private void OnAnswerIndexChanged(int _, int v) => m_Grid.SelectAnswer(v);

        /// <summary>페이즈 변경 통지(전 클라 로컬 호출) — 맵 기믹·HUD 등이 폴링 없이 반응할 수 있게.</summary>
        public static event System.Action<GamePhase> PhaseChanged;

        private void OnPhaseChanged(int _, int next)
        {
            PhaseChanged?.Invoke((GamePhase)next);
            if ((GamePhase)next == GamePhase.Building)
            {
                m_UrgentBgmStarted = false;
                GridSoundBridge.SetPhaseForMap("Building", m_MapIndex.Value);
            }
            else if ((GamePhase)next == GamePhase.Finished)
            {
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
            m_TimeLeft.Value = Mathf.Max(1f, t);
            m_Phase.Value = (int)GamePhase.Building;
            for (int i = m_Consents.Count - 1; i >= 0; i--) m_Consents.RemoveAt(i);
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

            // 입력(모든 클라): Enter = 동의 토글 (건축중=종료 동의 / 종료화면=재시작 동의)
            var kb = Keyboard.current;
            if (kb != null && kb.enterKey.wasPressedThisFrame)
                ToggleConsentRpc();

            if (IsBuilding && !m_UrgentBgmStarted && m_TimeLeft.Value <= 60f)
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
            for (int i = m_Names.Count - 1; i >= 0; i--)
                if (!Contains(ids, m_Names[i].Id)) m_Names.RemoveAt(i);
            for (int i = m_Teams.Count - 1; i >= 0; i--)
                if (!Contains(ids, m_Teams[i].Id)) m_Teams.RemoveAt(i);
            AssignTeams(ids);   // 2vs2: 새 접속자 팀 배정(협동 모드는 no-op)
            if (IsBuilding && IsVersus)
            {
                TryTeamSurrender(ids);   // 2vs2 건축중: 팀 전원 동의 = 그 팀 항복(즉시 패배)
            }
            else if (ids.Count > 0 && m_Consents.Count >= ids.Count)
            {
                if (IsBuilding) Finish();   // 건축 전원동의 → 종료
                else            Restart();  // 종료 전원동의 → 재시작
            }

            // 타이머
            if (IsBuilding)
            {
                m_TimeLeft.Value -= Time.deltaTime;
                if (m_TimeLeft.Value <= 0f) { m_TimeLeft.Value = 0f; Finish(); }
            }
        }

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

            // 2vs2: 승패 확정 — 항복이 있으면 그대로, 아니면 완성도(점수) 비교. 동점=무승부.
            if (IsServer && IsVersus && m_Net != null)
            {
                if (m_SurrenderWinner >= 0) m_Winner.Value = m_SurrenderWinner;
                else
                {
                    // Total = 건축 점수 + 보너스(DDP 유구 출토 등) — 보너스도 승패에 반영된다.
                    int a = m_Net.ScoreFor(0).Total, b = m_Net.ScoreFor(1).Total;
                    m_Winner.Value = a == b ? -1 : (a > b ? 0 : 1);
                }
            }
            m_SurrenderWinner = -2;

            m_Phase.Value = (int)GamePhase.Finished;
            for (int i = m_Consents.Count - 1; i >= 0; i--) m_Consents.RemoveAt(i);   // 종료 진입 → 동의 초기화(재시작 동의는 새로 받음)
        }

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
                    m_SurrenderWinner = 1 - team;
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
                if (m_Consents[i] == sender) { m_Consents.RemoveAt(i); return; }
            m_Consents.Add(sender);
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
            if (TryGetComponent<ExcavationNetwork>(out var dig)) dig.ServerReset();     // DDP: 발굴터·누적 출토 리셋
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

        public void RequestToggleConsent()
        {
            if (!IsSpawned) return;
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

        // 방으로 돌아가기: 세션·연결 유지한 채 전원이 대기방(Lobby 씬)으로 복귀(게임 시작의 역방향, 서버 권위).
        public void RequestReturnToRoom()
        {
            if (!IsSpawned) return;
            if (IsServer) ServerLoadRoom();
            else          ReturnToRoomRpc();
        }

        [Rpc(SendTo.Server)]
        private void ReturnToRoomRpc() => ServerLoadRoom();

        private void ServerLoadRoom()
        {
            var nm = NetworkManager.Singleton;
            if (nm != null && nm.SceneManager != null && nm.NetworkConfig.EnableSceneManagement)
                nm.SceneManager.LoadScene(SceneNames.Lobby, LoadSceneMode.Single);   // 전원 함께 방으로
            else
                SceneManager.LoadScene(SceneNames.Lobby);
        }

    }
}
