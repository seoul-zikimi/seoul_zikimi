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

        private GridManager m_Grid;
        private GridNetwork m_Net;
        private GUIStyle m_Big, m_Mid, m_Small;

        public GamePhase Phase => (GamePhase)m_Phase.Value;
        public float TimeLeft => m_TimeLeft.Value;
        public bool IsBuilding => Phase == GamePhase.Building;

        private void Awake()
        {
            m_Grid = GetComponent<GridManager>();
            m_Net = GetComponent<GridNetwork>();
        }

        public override void OnNetworkSpawn()
        {
            m_AnswerIndex.OnValueChanged += OnAnswerIndexChanged;
            if (IsServer) PickRandomAnswer();          // 서버: 랜덤 정답 선택(전원 동기화)
            m_Grid.SelectAnswer(m_AnswerIndex.Value);  // 모든 클라(늦참 포함) 동일 정답 적용
            if (IsServer) ResetTimerAndPhase();        // 선택된 정답 기준 타이머
        }

        public override void OnNetworkDespawn()
        {
            m_AnswerIndex.OnValueChanged -= OnAnswerIndexChanged;
        }

        private void OnAnswerIndexChanged(int _, int v) => m_Grid.SelectAnswer(v);

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

            // 입력(모든 클라): Enter = 동의 토글 (건축중=종료 동의 / 종료화면=재시작 동의)
            var kb = Keyboard.current;
            if (kb != null && kb.enterKey.wasPressedThisFrame)
                ToggleConsentRpc();

            if (!IsServer) return;

            // 접속 인원 갱신 + 끊긴 클라 동의 정리 + 전원동의 검사(건축→종료 / 종료→재시작)
            var ids = NetworkManager.Singleton.ConnectedClientsIds;
            m_PlayerCount.Value = ids.Count;
            for (int i = m_Consents.Count - 1; i >= 0; i--)
                if (!Contains(ids, m_Consents[i])) m_Consents.RemoveAt(i);
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

        private void OnGUI()
        {
            if (!Application.isPlaying || !IsSpawned) return;
            if (m_Big == null)
            {
                m_Big   = new GUIStyle(GUI.skin.label) { fontSize = 38, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
                m_Mid   = new GUIStyle(GUI.skin.label) { fontSize = 20, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
                m_Small = new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            }

            int secs = Mathf.CeilToInt(TimeLeft);
            string timer = $"{secs / 60}:{secs % 60:00}";

            var tRect = new Rect(Screen.width / 2f - 110f, 8f, 220f, 48f);
            DrawBox(tRect, 0.55f);
            GUI.Label(tRect, IsBuilding ? timer : "종료", m_Big);

            if (IsBuilding)
            {
                string consent = $"건축종료 동의 {m_Consents.Count}/{m_PlayerCount.Value}";
                string hint = LocalConsented() ? "Enter = 동의 취소" : "Enter = 건축 종료 동의";
                GUI.Label(new Rect(Screen.width / 2f - 140f, 56f, 280f, 22f), consent, m_Mid);
                GUI.Label(new Rect(Screen.width / 2f - 140f, 78f, 280f, 20f), hint, m_Small);
            }
            else
            {
                var sc = m_Net != null ? m_Net.Score : default;
                var box = new Rect(Screen.width / 2f - 220f, Screen.height / 2f - 110f, 440f, 220f);
                DrawBox(box, 0.82f);
                GUI.Label(new Rect(box.x, box.y + 20f, box.width, 48f), "건축 종료!", m_Big);
                GUI.Label(new Rect(box.x, box.y + 78f, box.width, 30f), $"점수 {sc.Percent:F0}%  ({sc.score}/{sc.maxScore})", m_Mid);
                GUI.Label(new Rect(box.x, box.y + 112f, box.width, 24f), $"배치 정확 {sc.placedCorrect}/{sc.answerCells}", m_Small);
                GUI.Label(new Rect(box.x, box.y + 134f, box.width, 24f), $"공정 완료 {sc.processCorrect}/{sc.answerCells}", m_Small);
                GUI.Label(new Rect(box.x, box.y + 166f, box.width, 26f), $"재시작 동의 {m_Consents.Count}/{m_PlayerCount.Value}", m_Mid);
                GUI.Label(new Rect(box.x, box.y + 192f, box.width, 22f), LocalConsented() ? "Enter = 동의 취소" : "Enter = 재시작 동의", m_Small);
            }

            // 나가기(채점 없이 세션 이탈) → 연결 끊고 Bootstrap(메뉴)으로 복귀.
            // 좌하단(빈 공간)에 둠 — 우하단은 정답 미리보기 박스와 겹침.
            if (GUI.Button(new Rect(12f, Screen.height - 46f, 100f, 34f), "나가기"))
            {
                if (NetworkManager.Singleton != null) NetworkManager.Singleton.Shutdown();
                SceneManager.LoadScene(SceneNames.BootstrapScene);   // 메뉴로 복귀(다시 Host/Client)
            }
        }

        private static void DrawBox(Rect r, float a)
        {
            var prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, a);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = prev;
        }
    }
}
