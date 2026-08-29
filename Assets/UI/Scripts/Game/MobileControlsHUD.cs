using GridSystem;
using Player;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 모바일 게임플레이 컨트롤 Canvas 구동기.
/// 비주얼 오브젝트는 Resources/UI/Mobile/MobileControlsCanvas 프리팹에 있고,
/// 이 컴포넌트는 입력 연결·가용 상태·Device Simulator 표시만 담당한다.
/// </summary>
public sealed class MobileControlsHUD : MonoBehaviour
{
    private const string kEditorPreviewPref = "Jobsnail_ForceMobileUI";
    private static MobileControlsHUD s_Instance;

    private GameObject m_ControlLayer;
    private GameObject m_EmotePanel;
    private GameObject m_ProcessButton;
    private GameObject m_RevertButton;
    private GameObject m_ThrowButton;
    private CanvasGroup m_AnswerToggleGroup;
    private GameLoopManager m_Loop;
    private float m_NextLoopFind;
    private bool m_PhoneOpen;
    private bool m_LastControlsVisible;
    private float m_NextStateRefresh;

    public static bool ShouldUseMobileUI
    {
        get
        {
#if UNITY_EDITOR
            if (PlayerPrefs.GetInt(kEditorPreviewPref, 0) == 1)
                return true;
#endif
            return Application.isMobilePlatform || Touchscreen.current != null;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != SceneNames.GameScene || s_Instance != null)
            return;

        var prefab = Resources.Load<GameObject>("UI/Mobile/MobileControlsCanvas");
        if (prefab == null)
        {
            Debug.LogWarning("[Mobile UI] MobileControlsCanvas 프리팹이 없습니다.");
            return;
        }

        var instance = Instantiate(prefab);
        instance.name = "@MobileControlsCanvas";
        DontDestroyOnLoad(instance);
    }

    private void Awake()
    {
        if (s_Instance != null && s_Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        s_Instance = this;

        m_ControlLayer = Find("ControlLayer");
        m_EmotePanel = Find("EmotePanel");
        m_ProcessButton = Find("ProcessButton");
        m_RevertButton = Find("RevertButton");
        m_ThrowButton = Find("ThrowButton");

        var answerToggle = Find("AnswerToggleButton");
        m_AnswerToggleGroup = answerToggle != null ? answerToggle.GetComponent<CanvasGroup>() : null;

        WireClick("JumpButton", MobileGameplayInput.PressJump);
        WireClick("ScaffoldButton", MobileGameplayInput.PressScaffold);
        WireClick("RotateButton", MobileGameplayInput.PressRotateHeld);
        WireClick("PhoneButton", MobileGameplayInput.ToggleOrder);
        WireClick("AnswerToggleButton", ToggleAnswerGhost);
        WireClick("EmoteButton", ToggleEmotes);
        // 대사 전체(EmoteDefs.Count)를 와이어링 — 프리팹 행이 모자라면 WireClick이 조용히 넘어간다.
        // 행 수는 MobileControlsPrefabGenerator가 같은 EmoteDefs.Count로 만든다.
        for (int i = 0; i < EmoteDefs.Count; i++)
        {
            int index = i;
            WireClick($"Emote{index + 1}", () =>
            {
                MobileGameplayInput.TriggerEmote(index);
                if (m_EmotePanel != null) m_EmotePanel.SetActive(false);
            });
        }

        JobsnailUiKit.ApplyFontPolicy(transform);
        if (m_EmotePanel != null) m_EmotePanel.SetActive(false);
        if (m_ControlLayer != null) m_ControlLayer.SetActive(false);

        // 모바일은 폰(TAB)과 별개로 인월드 정답 고스트를 눈 버튼으로 켜고 끈다 — 기본 켜짐.
        if (ShouldUseMobileUI) AnswerPreview.GhostPinned = true;
        UpdateAnswerToggleVisual();
    }

    private void ToggleAnswerGhost()
    {
        AnswerPreview.GhostPinned = !AnswerPreview.GhostPinned;
        UpdateAnswerToggleVisual();
    }

    private void UpdateAnswerToggleVisual()
    {
        if (m_AnswerToggleGroup != null)
            m_AnswerToggleGroup.alpha = AnswerPreview.GhostPinned ? 1f : 0.38f;
    }

    private void OnEnable()
    {
        AnswerPanelHUD.PhoneVisibilityChanged -= OnPhoneVisibility;
        AnswerPanelHUD.PhoneVisibilityChanged += OnPhoneVisibility;
    }

    private void OnDisable()
    {
        AnswerPanelHUD.PhoneVisibilityChanged -= OnPhoneVisibility;
        MobileGameplayInput.HasVisibleMoveControl = false;
        MobileGameplayInput.WorldInputLocked = false;
        MobileGameplayInput.ReleaseAll();
    }

    private void OnDestroy()
    {
        if (s_Instance == this) s_Instance = null;
    }

    private void Update()
    {
        bool inGame = SceneManager.GetActiveScene().name == SceneNames.GameScene;
        // 정산(크레인샷) 단계에선 컨트롤을 내려 하단 중앙의 '건축물 둘러보기' 버튼 등을 가리지 않는다.
        bool show = inGame && ShouldUseMobileUI && !m_PhoneOpen && InBuildPhase();
        if (show != m_LastControlsVisible)
        {
            m_LastControlsVisible = show;
            if (m_ControlLayer != null) m_ControlLayer.SetActive(show);
            MobileGameplayInput.HasVisibleMoveControl = show;
            if (!show)
            {
                if (m_EmotePanel != null) m_EmotePanel.SetActive(false);
                MobileGameplayInput.ReleaseAll();
            }
        }

        if (!show || Time.unscaledTime < m_NextStateRefresh)
            return;
        m_NextStateRefresh = Time.unscaledTime + 0.1f;

#if UNITY_EDITOR
        bool preview = PlayerPrefs.GetInt(kEditorPreviewPref, 0) == 1 && !MobileGameplayInput.Available;
#else
        bool preview = false;
#endif
        SetActionVisible(m_ProcessButton, preview || MobileGameplayInput.ProcessActionAvailable);
        SetActionVisible(m_RevertButton, preview || MobileGameplayInput.ProcessCancelAvailable);
        SetActionVisible(m_ThrowButton, preview || MobileGameplayInput.ThrowAvailable);
    }

    private void OnPhoneVisibility(bool visible)
    {
        m_PhoneOpen = visible;
        // 폰이 열려 컨트롤이 숨는 동안 MobileTouchInputDriver의 보이지 않는 이동/카메라/탭이 되살아나지 않게 잠근다.
        MobileGameplayInput.WorldInputLocked = visible && ShouldUseMobileUI;
        if (visible && m_EmotePanel != null)
            m_EmotePanel.SetActive(false);
    }

    // AnswerPreview.Building()과 같은 기준: 루프 매니저가 없으면 항상 건축 중으로 본다.
    private bool InBuildPhase()
    {
        if (m_Loop == null && Time.unscaledTime >= m_NextLoopFind)
        {
            m_NextLoopFind = Time.unscaledTime + 1f;
            m_Loop = FindFirstObjectByType<GameLoopManager>();
        }
        return m_Loop == null || m_Loop.IsBuilding;
    }

    private void ToggleEmotes()
    {
        if (m_EmotePanel != null)
            m_EmotePanel.SetActive(!m_EmotePanel.activeSelf);
    }

    private static void SetActionVisible(GameObject target, bool visible)
    {
        if (target != null && target.activeSelf != visible)
            target.SetActive(visible);
    }

    private void WireClick(string objectName, UnityEngine.Events.UnityAction action)
    {
        var target = Find(objectName);
        var button = target != null ? target.GetComponent<Button>() : null;
        if (button == null) return;
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(action);
    }

    private GameObject Find(string objectName)
    {
        foreach (Transform child in GetComponentsInChildren<Transform>(true))
            if (child.name == objectName)
                return child.gameObject;
        return null;
    }
}
