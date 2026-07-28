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
        private readonly NetworkList<ulong> m_Consents = new();   // 동의한 clientId (건축중=종료동의 / 종료중=재시작동의, 서버 관리)
        private readonly NetworkList<NameEntry> m_Names = new();   // 접속 플레이어 표시 이름(서버 관리, 정산서 명단용)

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
        public float TimeLimit => (m_Grid != null && m_Grid.Answer != null) ? m_Grid.Answer.TimeLimitSeconds : 0f;
        public float Elapsed => Mathf.Max(0f, TimeLimit - TimeLeft);
        public string AnswerName => (m_Grid != null && m_Grid.Answer != null) ? m_Grid.Answer.DisplayName : "";

        private void Awake()
        {
            m_Grid = GetComponent<GridManager>();
            m_Net = GetComponent<GridNetwork>();
        }

        public override void OnNetworkSpawn()
        {
            m_Phase.OnValueChanged += OnPhaseChanged;
            m_AnswerIndex.OnValueChanged += OnAnswerIndexChanged;

            if (TutorialSession.IsActive)
            {
                // 튜토리얼: 랜덤 정답 대신 전용 정답 고정, 시간제한 없음, 전원동의 종료 로직은 Update()에서 스킵.
                m_Grid.UseTutorialAnswer();
                if (IsServer) { m_TimeLeft.Value = float.MaxValue; m_Phase.Value = (int)GamePhase.Building; }
                OnPhaseChanged((int)Phase, (int)Phase);
                SubmitName();
                return;
            }

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

        private void OnPhaseChanged(int _, int next)
        {
            if ((GamePhase)next == GamePhase.Building)
            {
                m_UrgentBgmStarted = false;
                GridSoundBridge.SetPhase("Building");
            }
            else if ((GamePhase)next == GamePhase.Finished)
            {
                GridSoundBridge.SetPhase("Result");
                GridSoundBridge.PlaySFX("GameOver");
            }
        }

        // 서버: 정답 목록에서 랜덤으로 하나 고른다(1개뿐이면 0). 코스메틱 아님 — 인덱스를 복제.
        private void PickRandomAnswer()
        {
            int n = m_Grid != null ? m_Grid.AnswerCount : 0;
            m_AnswerIndex.Value = n > 1 ? UnityEngine.Random.Range(0, n) : 0;
        }

        private void ResetTimerAndPhase()
        {
            float t = (m_Grid != null && m_Grid.Answer != null) ? m_Grid.Answer.TimeLimitSeconds : 180f;
            m_TimeLeft.Value = Mathf.Max(1f, t);
            m_Phase.Value = (int)GamePhase.Building;
            for (int i = m_Consents.Count - 1; i >= 0; i--) m_Consents.RemoveAt(i);
        }

        private void Update()
        {
            if (!IsSpawned) return;   // 스폰 전/디스폰 후엔 네트워크 상태 접근 금지(NullRef 방지)
            if (TutorialSession.IsActive) return;   // 튜토리얼: 타이머·전원동의·자동종료 전부 TutorialManager가 대신함

            // 입력(모든 클라): Enter = 동의 토글 (건축중=종료 동의 / 종료화면=재시작 동의)
            var kb = Keyboard.current;
            if (kb != null && kb.enterKey.wasPressedThisFrame)
                ToggleConsentRpc();

            if (IsBuilding && !m_UrgentBgmStarted && m_TimeLeft.Value <= 60f)
            {
                m_UrgentBgmStarted = true;
                GridSoundBridge.SetPhase("BuildingUrgent");
            }

            if (!IsServer) return;

            // 접속 인원 갱신 + 끊긴 클라 동의 정리 + 전원동의 검사(건축→종료 / 종료→재시작)
            var ids = NetworkManager.Singleton.ConnectedClientsIds;
            m_PlayerCount.Value = ids.Count;
            for (int i = m_Consents.Count - 1; i >= 0; i--)
                if (!Contains(ids, m_Consents[i])) m_Consents.RemoveAt(i);
            for (int i = m_Names.Count - 1; i >= 0; i--)
                if (!Contains(ids, m_Names[i].Id)) m_Names.RemoveAt(i);
            if (ids.Count > 0 && m_Consents.Count >= ids.Count)
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
            m_Phase.Value = (int)GamePhase.Finished;
            for (int i = m_Consents.Count - 1; i >= 0; i--) m_Consents.RemoveAt(i);   // 종료 진입 → 동의 초기화(재시작 동의는 새로 받음)
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
            PickRandomAnswer();                        // 재시작마다 새 랜덤 정답
            m_Grid.SelectAnswer(m_AnswerIndex.Value);
            if (m_Net != null) m_Net.ServerResetGrid();   // 그리드 + 바닥/배송 재료 정리
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
