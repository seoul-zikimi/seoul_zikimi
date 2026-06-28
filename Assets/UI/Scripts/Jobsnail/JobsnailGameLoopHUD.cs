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
    private TextMeshProUGUI m_ResultScoreText;
    private TextMeshProUGUI m_ResultPlacementText;
    private TextMeshProUGUI m_ResultProcessText;
    private TextMeshProUGUI m_ResultConsentText;
    private GameObject m_TopBar;
    private GameObject m_ConsentBar;
    private GameObject m_ResultPanel;
    private GameObject m_SettingsPopup;
    private Button m_SettingsButton;
    private Button m_ResultRestartButton;
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
            return;

        if (m_Loop.IsBuilding)
        {
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

        int secs = Mathf.CeilToInt(m_Loop.TimeLeft);
        m_TimerText.text = m_Loop.IsBuilding ? $"{secs / 60}:{secs % 60:00}" : "종료";

        UpdateResultPanel();

        if (m_ConsentText != null)
        {
            string verb = m_Loop.IsBuilding ? "건축 종료" : "재시작";
            string mine = m_Loop.HasLocalConsent ? "  ✓동의함" : "";
            m_ConsentText.text = $"Enter — {verb} 동의  {m_Loop.ConsentCount}/{m_Loop.PlayerCount}{mine}";
        }

    }

    private void Rebuild()
    {
        var root = transform;

        for (int i = root.childCount - 1; i >= 0; i--)
            Destroy(root.GetChild(i).gameObject);

        m_Loop = null;
        m_UrgentBgmStarted = false;

        var top = JobsnailUiKit.Box("TopBar", root, new Vector2(0.42f, 0.92f), new Vector2(0.58f, 0.99f), Vector2.zero, Vector2.zero, new Color(0.84f, 0.82f, 0.70f, 0.92f));
        m_TopBar = top.gameObject;
        m_TimerText = JobsnailUiKit.Label("Timer", top.transform, "0:00", 34, Color.black, TextAlignmentOptions.Center, Vector2.zero, Vector2.zero);

        // 전원 동의 안내(Enter): 건축 중 = 종료 동의 / 종료 화면 = 재시작 동의. N/M = 동의/접속 인원.
        var cbar = JobsnailUiKit.Box("ConsentBar", root, new Vector2(0.33f, 0.845f), new Vector2(0.67f, 0.905f), Vector2.zero, Vector2.zero, new Color(0.12f, 0.12f, 0.14f, 0.78f));
        m_ConsentBar = cbar.gameObject;
        m_ConsentText = JobsnailUiKit.Label("Consent", cbar.transform, "", 17, Color.white, TextAlignmentOptions.Center, Vector2.zero, Vector2.zero);

        BuildSettingsButton(root);
        BuildSettingsPopup(root);
        BuildResultPanel(root);

        if (SoundManager.Instance != null)
            SoundManager.Instance.SetPhase(global::GamePhase.Building);
    }

    private void SetVisible(bool visible)
    {
        if (m_TopBar != null)
            m_TopBar.SetActive(visible);
        if (m_ConsentBar != null)
            m_ConsentBar.SetActive(visible);
        if (m_SettingsButton != null)
            m_SettingsButton.gameObject.SetActive(visible);
        if (!visible)
        {
            if (m_ResultPanel != null)
                m_ResultPanel.SetActive(false);
            if (m_SettingsPopup != null)
                m_SettingsPopup.SetActive(false);
        }
    }

    private void BuildSettingsButton(Transform root)
    {
        var sprite = JobsnailUiKit.Sprite("UI_pngs/settingsicon");
        m_SettingsButton = JobsnailUiKit.Button(
            "SettingsIconButton",
            root,
            sprite,
            new Vector2(0.955f, 0.925f),
            new Vector2(0.99f, 0.985f),
            Vector2.zero,
            Vector2.zero,
            ToggleSettingsPopup,
            sprite == null ? "설정" : null);

        if (m_SettingsButton.targetGraphic is Image image)
        {
            image.raycastTarget = true;
            image.color = Color.white;
            image.preserveAspect = true;
        }
    }

    private void BuildSettingsPopup(Transform root)
    {
        m_SettingsPopup = JobsnailUiKit.Box(
            "InGameSettingsPopup",
            root,
            new Vector2(0.36f, 0.31f),
            new Vector2(0.64f, 0.72f),
            Vector2.zero,
            Vector2.zero,
            new Color(1f, 0.97f, 0.86f, 0.98f)).gameObject;
        m_SettingsPopup.SetActive(false);

        JobsnailUiKit.Label("Title", m_SettingsPopup.transform, "설정", 30, Color.black, TextAlignmentOptions.Center, new Vector2(0, 138), new Vector2(360, 56));
        MakeVolumeSlider(m_SettingsPopup.transform, "BGM", new Vector2(0, 56), PlayerPrefs.GetFloat("BGMVolume", 0.8f), value =>
        {
            if (SoundManager.Instance != null) SoundManager.Instance.SetBGMVolume(value);
            else PlayerPrefs.SetFloat("BGMVolume", value);
        });
        MakeVolumeSlider(m_SettingsPopup.transform, "SFX", new Vector2(0, -12), PlayerPrefs.GetFloat("SFXVolume", 1f), value =>
        {
            if (SoundManager.Instance != null) SoundManager.Instance.SetSFXVolume(value);
            else PlayerPrefs.SetFloat("SFXVolume", value);
        });

        var done = JobsnailUiKit.Button("SettingsDoneButton", m_SettingsPopup.transform, null, new Vector2(0.30f, 0.15f), new Vector2(0.70f, 0.27f), Vector2.zero, Vector2.zero, ToggleSettingsPopup, "완료");
        SetButtonColor(done, new Color(1f, 0.78f, 0.44f, 1f));

        var close = JobsnailUiKit.Button("SettingsCloseButton", m_SettingsPopup.transform, null, new Vector2(0.88f, 0.88f), new Vector2(0.98f, 0.98f), Vector2.zero, Vector2.zero, ToggleSettingsPopup, "×");
        SetButtonColor(close, new Color(1f, 0.97f, 0.86f, 0f));
    }

    private void BuildResultPanel(Transform root)
    {
        m_ResultPanel = JobsnailUiKit.Box(
            "ResultPanel",
            root,
            new Vector2(0.34f, 0.20f),
            new Vector2(0.66f, 0.78f),
            Vector2.zero,
            Vector2.zero,
            new Color(1f, 0.98f, 0.92f, 0.98f)).gameObject;
        m_ResultPanel.SetActive(false);

        JobsnailUiKit.Label("Title", m_ResultPanel.transform, "정산서", 32, Color.black, TextAlignmentOptions.Center, new Vector2(0, 230), new Vector2(420, 60));
        JobsnailUiKit.Label("Subtitle", m_ResultPanel.transform, "작업 결과", 17, new Color(0.25f, 0.20f, 0.16f, 1f), TextAlignmentOptions.Center, new Vector2(0, 190), new Vector2(420, 36));
        m_ResultScoreText = JobsnailUiKit.Label("Score", m_ResultPanel.transform, "건축 0% 완료", 30, Color.black, TextAlignmentOptions.Center, new Vector2(0, 120), new Vector2(430, 52));
        m_ResultPlacementText = JobsnailUiKit.Label("Placement", m_ResultPanel.transform, "배치 정확 0 / 0", 22, Color.black, TextAlignmentOptions.Center, new Vector2(0, 62), new Vector2(430, 40));
        m_ResultProcessText = JobsnailUiKit.Label("Process", m_ResultPanel.transform, "공정 완료 0 / 0", 22, Color.black, TextAlignmentOptions.Center, new Vector2(0, 20), new Vector2(430, 40));
        m_ResultConsentText = JobsnailUiKit.Label("Consent", m_ResultPanel.transform, "재시작 동의 0 / 0", 18, new Color(0.30f, 0.22f, 0.15f, 1f), TextAlignmentOptions.Center, new Vector2(0, -52), new Vector2(430, 36));

        m_ResultRestartButton = JobsnailUiKit.Button("RestartConsentButton", m_ResultPanel.transform, null, new Vector2(0.18f, 0.08f), new Vector2(0.46f, 0.18f), Vector2.zero, Vector2.zero, () =>
        {
            if (m_Loop != null)
                m_Loop.RequestToggleConsent();
        }, "재시작 동의");
        SetButtonColor(m_ResultRestartButton, new Color(1f, 0.78f, 0.44f, 1f));

        var leave = JobsnailUiKit.Button("LeaveButton", m_ResultPanel.transform, null, new Vector2(0.54f, 0.08f), new Vector2(0.82f, 0.18f), Vector2.zero, Vector2.zero, () =>
        {
            if (m_Loop != null)
                m_Loop.RequestLeaveToLobby();
            else
                SceneManager.LoadScene(SceneNames.BootstrapScene);
        }, "나가기");
        SetButtonColor(leave, new Color(1f, 0.78f, 0.44f, 1f));
    }

    private void UpdateResultPanel()
    {
        if (m_ResultPanel == null || m_Loop == null)
            return;

        bool show = !m_Loop.IsBuilding;
        m_ResultPanel.SetActive(show);
        m_ConsentBar.SetActive(m_Loop.IsBuilding);
        if (!show)
            return;

        var score = m_Loop.Score;
        int answerCells = Mathf.Max(0, score.answerCells);
        m_ResultScoreText.text = $"건축 {score.Percent:F0}% 완료";
        m_ResultPlacementText.text = $"배치 정확 {score.placedCorrect} / {answerCells}";
        m_ResultProcessText.text = $"공정 완료 {score.processCorrect} / {answerCells}";
        m_ResultConsentText.text = $"재시작 동의 {m_Loop.ConsentCount} / {m_Loop.PlayerCount}";

        if (m_ResultRestartButton != null)
        {
            var label = m_ResultRestartButton.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null)
                label.text = m_Loop.HasLocalConsent ? "동의 완료" : "재시작 동의";
            SetButtonColor(m_ResultRestartButton, m_Loop.HasLocalConsent ? new Color(0.56f, 0.86f, 0.48f, 1f) : new Color(1f, 0.78f, 0.44f, 1f));
        }
    }

    private void ToggleSettingsPopup()
    {
        if (m_SettingsPopup == null)
            return;

        bool show = !m_SettingsPopup.activeSelf;
        m_SettingsPopup.SetActive(show);
        if (!show && SoundManager.Instance != null)
            SoundManager.Instance.SaveVolumes();
    }

    private static void MakeVolumeSlider(Transform parent, string label, Vector2 anchored, float value, UnityEngine.Events.UnityAction<float> onChanged)
    {
        var row = new GameObject(label + "VolumeRow", typeof(RectTransform));
        row.transform.SetParent(parent, false);
        var rowRt = (RectTransform)row.transform;
        rowRt.anchorMin = new Vector2(0.5f, 0.5f);
        rowRt.anchorMax = new Vector2(0.5f, 0.5f);
        rowRt.anchoredPosition = anchored;
        rowRt.sizeDelta = new Vector2(340, 44);

        JobsnailUiKit.Label(label + "Label", row.transform, label, 18, Color.black, TextAlignmentOptions.Left, new Vector2(-126, 0), new Vector2(70, 36));

        var sliderGo = new GameObject("Slider", typeof(RectTransform), typeof(Slider));
        sliderGo.transform.SetParent(row.transform, false);
        var rt = (RectTransform)sliderGo.transform;
        rt.anchorMin = new Vector2(0.35f, 0.25f);
        rt.anchorMax = new Vector2(0.95f, 0.75f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var background = JobsnailUiKit.Box("Background", sliderGo.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Color(0.82f, 0.82f, 0.82f, 1f));
        background.raycastTarget = true;

        var fillArea = new GameObject("Fill Area", typeof(RectTransform));
        fillArea.transform.SetParent(sliderGo.transform, false);
        var fillAreaRt = (RectTransform)fillArea.transform;
        fillAreaRt.anchorMin = Vector2.zero;
        fillAreaRt.anchorMax = Vector2.one;
        fillAreaRt.offsetMin = new Vector2(4, 4);
        fillAreaRt.offsetMax = new Vector2(-4, -4);

        var fill = JobsnailUiKit.Box("Fill", fillArea.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Color(1f, 0.72f, 0.36f, 1f));
        fill.raycastTarget = true;

        var handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
        handleArea.transform.SetParent(sliderGo.transform, false);
        var handleAreaRt = (RectTransform)handleArea.transform;
        handleAreaRt.anchorMin = Vector2.zero;
        handleAreaRt.anchorMax = Vector2.one;
        handleAreaRt.offsetMin = new Vector2(8, 0);
        handleAreaRt.offsetMax = new Vector2(-8, 0);

        var handle = JobsnailUiKit.Box("Handle", handleArea.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(20, 30), new Color(0.32f, 0.22f, 0.15f, 1f));
        handle.raycastTarget = true;

        var slider = sliderGo.GetComponent<Slider>();
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.value = Mathf.Clamp01(value);
        slider.fillRect = fill.rectTransform;
        slider.handleRect = handle.rectTransform;
        slider.targetGraphic = handle;
        slider.onValueChanged.AddListener(onChanged);
    }

    private static void SetButtonColor(Button button, Color color)
    {
        if (button != null && button.targetGraphic != null)
            button.targetGraphic.color = color;
    }
}
