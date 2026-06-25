using GridSystem;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed class JobsnailGameLoopHUD : MonoBehaviour
{
    private GameLoopManager m_Loop;
    private TextMeshProUGUI m_TimerText;
    private TextMeshProUGUI m_ConsentText;
    private TextMeshProUGUI m_ActionButtonText;
    private TextMeshProUGUI m_ResultText;
    private GameObject m_ResultPanel;
    private GameObject m_TopBar;
    private GameObject m_ConsentBox;
    private Button m_ActionButton;
    private Button m_LeaveButton;
    private bool m_UrgentBgmStarted;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != SceneNames.GameScene)
            return;

        EnsureEventSystem();
        var canvas = JobsnailUiKit.EnsureOverlayCanvas("@JobsnailGameLoopHUD", 120);
        var hud = canvas.GetComponent<JobsnailGameLoopHUD>();
        if (hud == null)
            canvas.gameObject.AddComponent<JobsnailGameLoopHUD>();
        else
            hud.Rebuild();
    }

    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null)
            return;

        var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        DontDestroyOnLoad(es);
    }

    private void Awake()
    {
        Rebuild();
    }

    private void Update()
    {
        if (m_Loop == null)
            m_Loop = FindFirstObjectByType<GameLoopManager>();

        bool ready = m_Loop != null && m_Loop.IsSpawned;
        SetVisible(ready);
        if (!ready)
        {
            if (m_ResultPanel != null)
                m_ResultPanel.SetActive(false);
            return;
        }

        if (m_Loop.IsBuilding)
        {
            if (m_ResultPanel != null)
                m_ResultPanel.SetActive(false);

            if (m_Loop.TimeLeft <= 0f)
            {
                m_Loop.RequestFinishByTimeout();
                return;
            }

            if (m_Loop.TimeLeft > 60f)
                m_UrgentBgmStarted = false;

            if (!m_UrgentBgmStarted && m_Loop.TimeLeft <= 60f)
            {
                m_UrgentBgmStarted = true;
                if (SoundManager.Instance != null)
                    SoundManager.Instance.SetPhase(global::GamePhase.BuildingUrgent);
            }
        }
        else if (m_ResultPanel == null)
        {
            BuildResultPanel(transform);
        }

        int secs = Mathf.CeilToInt(m_Loop.TimeLeft);
        m_TimerText.text = m_Loop.IsBuilding ? $"{secs / 60}:{secs % 60:00}" : "종료";
        m_ConsentText.text = m_Loop.IsBuilding
            ? $"종료 요청 {m_Loop.ConsentCount}/{Mathf.Max(1, m_Loop.PlayerCount)}"
            : $"다시 하기 {m_Loop.ConsentCount}/{Mathf.Max(1, m_Loop.PlayerCount)}";

        m_ActionButtonText.text = m_Loop.IsBuilding
            ? (m_Loop.HasLocalConsent ? "요청 취소" : "종료 요청")
            : (m_Loop.HasLocalConsent ? "준비 취소" : "한 판 더하기");

        if (m_ResultPanel != null)
            m_ResultPanel.SetActive(!m_Loop.IsBuilding);
        if (!m_Loop.IsBuilding)
        {
            var score = m_Loop.Score;
            m_ResultText.text =
                $"<size=38>정산서</size>\n\n" +
                $"소요시간   {FormatElapsed(m_Loop.TimeLeft)}\n" +
                $"건축 {score.Percent:F0}% 완료   ★★★\n" +
                $"점수 {score.score}/{score.maxScore}\n" +
                $"배치 정확 {score.placedCorrect}/{score.answerCells}\n" +
                $"공정 완료 {score.processCorrect}/{score.answerCells}";
        }
    }

    private void Rebuild()
    {
        var root = transform;

        for (int i = root.childCount - 1; i >= 0; i--)
            Destroy(root.GetChild(i).gameObject);

        m_ResultPanel = null;
        m_ResultText = null;
        m_Loop = null;
        m_UrgentBgmStarted = false;

        var top = JobsnailUiKit.Box("TopBar", root, new Vector2(0.42f, 0.92f), new Vector2(0.58f, 0.99f), Vector2.zero, Vector2.zero, new Color(0.84f, 0.82f, 0.70f, 0.92f));
        m_TopBar = top.gameObject;
        m_TimerText = JobsnailUiKit.Label("Timer", top.transform, "0:00", 34, Color.black, TextAlignmentOptions.Center, Vector2.zero, Vector2.zero);

        var consent = JobsnailUiKit.Box("ConsentBox", root, new Vector2(0.82f, 0.93f), new Vector2(0.98f, 0.985f), Vector2.zero, Vector2.zero, new Color(1f, 1f, 1f, 0.90f));
        m_ConsentBox = consent.gameObject;
        m_ConsentText = JobsnailUiKit.Label("Consent", consent.transform, "종료 요청 0/0", 20, Color.black, TextAlignmentOptions.Center, Vector2.zero, Vector2.zero);

        m_ActionButton = JobsnailUiKit.Button("EndRequestButton", root, null, new Vector2(0.865f, 0.875f), new Vector2(0.965f, 0.925f), Vector2.zero, Vector2.zero, OnActionClicked, "종료 요청");
        m_ActionButtonText = m_ActionButton.GetComponentInChildren<TextMeshProUGUI>();

        m_LeaveButton = JobsnailUiKit.Button("LeaveButton", root, null, new Vector2(0.02f, 0.02f), new Vector2(0.11f, 0.07f), Vector2.zero, Vector2.zero, OnLeaveClicked, "나가기");

        if (SoundManager.Instance != null)
            SoundManager.Instance.SetPhase(global::GamePhase.Building);
    }

    private void BuildResultPanel(Transform root)
    {
        if (m_ResultPanel != null)
            return;

        m_ResultPanel = JobsnailUiKit.Box("ResultPanelDim", root, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Color(0f, 0f, 0f, 0.32f)).gameObject;

        var receipt = JobsnailUiKit.Box("Receipt", m_ResultPanel.transform, new Vector2(0.38f, 0.18f), new Vector2(0.62f, 0.82f), Vector2.zero, Vector2.zero, Color.white);
        m_ResultText = JobsnailUiKit.Label("ResultText", receipt.transform, "정산서", 24, Color.black, TextAlignmentOptions.Center, Vector2.zero, Vector2.zero);

        JobsnailUiKit.Label("Logo", receipt.transform, "JOBSNAIL🐌", 18, new Color(1f, 0.45f, 0.12f), TextAlignmentOptions.Right, new Vector2(0, -170), new Vector2(260, 40));
        JobsnailUiKit.Button("RetryButton", receipt.transform, null, new Vector2(0.12f, 0.08f), new Vector2(0.42f, 0.18f), Vector2.zero, Vector2.zero, OnActionClicked, "한 판 더하기");
        JobsnailUiKit.Button("ResultLeaveButton", receipt.transform, null, new Vector2(0.58f, 0.08f), new Vector2(0.88f, 0.18f), Vector2.zero, Vector2.zero, OnLeaveClicked, "나가기");
        m_ResultPanel.SetActive(false);
    }

    private void SetVisible(bool visible)
    {
        if (m_TopBar != null)
            m_TopBar.SetActive(visible);
        if (m_ConsentBox != null)
            m_ConsentBox.SetActive(visible);
        m_ActionButton.gameObject.SetActive(visible);
        m_LeaveButton.gameObject.SetActive(visible);
        if (!visible && m_ResultPanel != null)
            m_ResultPanel.SetActive(false);
    }

    private void OnActionClicked()
    {
        if (m_Loop == null)
            m_Loop = FindFirstObjectByType<GameLoopManager>();
        m_Loop?.RequestToggleConsent();
    }

    private void OnLeaveClicked()
    {
        if (m_Loop == null)
            m_Loop = FindFirstObjectByType<GameLoopManager>();
        m_Loop?.RequestLeaveToLobby();
    }

    private static string FormatElapsed(float unusedTimeLeft)
    {
        // 지금 GameLoopManager는 종료 시 경과 시간을 따로 들고 있지 않아서, UI 문구만 시안 톤에 맞춰 둔다.
        return "-";
    }
}
