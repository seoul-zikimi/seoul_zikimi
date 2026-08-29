using GridSystem;
using Player;
using TMPro;
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

    private static bool s_InGameScene;   // Scene.name 접근은 호출마다 문자열 할당 — 전환 이벤트로 캐시

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;
        SceneManager.activeSceneChanged += OnActiveSceneChanged;
        s_InGameScene = SceneManager.GetActiveScene().name == SceneNames.GameScene;
        OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    private static void OnActiveSceneChanged(Scene _, Scene next)
        => s_InGameScene = next.name == SceneNames.GameScene;

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

        // 폰(작은 화면)에선 터치 컨트롤을 물리적으로 키운다 — 가장자리 앵커라 잘림 없음. 태블릿은 1배 그대로.
        MobileUiScale.Apply(GetComponent<CanvasScaler>());

        WireClick("JumpButton", MobileGameplayInput.PressJump);
        WireClick("ScaffoldButton", MobileGameplayInput.PressScaffold);
        WireClick("RotateButton", MobileGameplayInput.PressRotateHeld);
        WireClick("PhoneButton", MobileGameplayInput.ToggleOrder);
        WireClick("AnswerToggleButton", ToggleAnswerGhost);
        WireClick("EmoteButton", ToggleEmotes);
        RebuildEmoteRows();   // 행 생성·라벨·와이어링을 EmoteDefs 기준으로 일괄 — 프리팹이 낡아도 안전

        JobsnailUiKit.ApplyFontPolicy(transform);
        if (m_EmotePanel != null) m_EmotePanel.SetActive(false);
        if (m_ControlLayer != null) m_ControlLayer.SetActive(false);

        // 버튼 배치 커스터마이즈(배틀그라운드식): 저장된 배치 적용 + 감정표현 아래 진입 버튼
        m_Customizer = gameObject.AddComponent<MobileLayoutCustomizer>();
        var safeArea = Find("SafeArea");
        if (safeArea != null)
        {
            m_Customizer.ApplySaved(safeArea.transform);
            BuildLayoutEditEntry(safeArea.transform);
        }

        // 모바일은 폰(TAB)과 별개로 인월드 정답 고스트를 눈 버튼으로 켜고 끈다 — 기본 켜짐.
        if (ShouldUseMobileUI) AnswerPreview.GhostPinned = true;
        UpdateAnswerToggleVisual();
    }

    private MobileLayoutCustomizer m_Customizer;

    // '버튼 배치 ✎' 진입 필 — 감정표현 드롭다운 아래(우상단). 프리팹 재생성 없이 런타임 생성.
    private void BuildLayoutEditEntry(Transform safeArea)
    {
        var go = new GameObject("LayoutEditButton", typeof(RectTransform)) { layer = 5 };
        var rt = (RectTransform)go.transform;
        rt.SetParent(safeArea, false);
        rt.anchorMin = rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(-130f, -260f);
        rt.sizeDelta = new Vector2(200f, 56f);
        var img = go.AddComponent<UnityEngine.UI.Image>();
        img.sprite = JobsnailUiKit.Sprite("UI_pngs/MyPage/RoundRect");
        img.type = UnityEngine.UI.Image.Type.Sliced;
        img.color = new Color(0.94f, 0.94f, 0.93f, 0.55f);   // 옅게 — 보조 기능
        var btn = go.AddComponent<UnityEngine.UI.Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(() => m_Customizer.BeginEdit());
        var label = new GameObject("Label", typeof(RectTransform)) { layer = 5 };
        var lrt = (RectTransform)label.transform;
        lrt.SetParent(rt, false);
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one; lrt.sizeDelta = Vector2.zero;
        var l = label.AddComponent<TMPro.TextMeshProUGUI>();
        l.text = "버튼 배치 ✎"; l.font = JobsnailUiKit.TmpFont; l.fontSize = 24;
        l.fontStyle = TMPro.FontStyles.Bold; l.color = new Color(0.2f, 0.2f, 0.19f, 0.85f);
        l.alignment = TMPro.TextAlignmentOptions.Center; l.raycastTarget = false;
    }

    // 감정표현 드롭다운을 실제 발동 대사(EmoteDefs) 기준으로 재구성 — 프리팹에 구워진 옛 이모지 이름(미소·붐업 등)과
    // 실제 대사가 달랐고, 대사 11종 중 8종만 노출되던 것도 함께 해소. 대사를 바꾸면 여기도 자동 반영(휠 UI와 동일 원칙).
    private void RebuildEmoteRows()
    {
        if (m_EmotePanel == null) return;
        var panel = (RectTransform)m_EmotePanel.transform;
        var template = Find("Emote1");
        if (template == null) return;

        // 행 크기·피치는 프리팹 생성기(MobileControlsPrefabGenerator.BuildEmotes)와 반드시 동일 값 —
        // 대사 11종이 우상단 버튼 아래 화면 안에 다 들어가도록 좁힌 수치(44/48).
        const float kRowHeight = 44f;
        const float kPitch = 48f;
        int count = EmoteDefs.Count;

        // 행 수에 맞춰 패널 높이부터 조정 — 위 모서리는 고정(감정표현 버튼과의 간격 유지), 아래로만 늘린다.
        // 행 좌표가 패널 '중심' 기준이라 리사이즈를 먼저 해야 행 배치가 새 높이에 맞는다.
        float top = panel.anchoredPosition.y + panel.sizeDelta.y * 0.5f;
        float h = count * kPitch + 16f;   // 생성기와 동일 식(위 8/아래 8 패딩)
        panel.sizeDelta = new Vector2(panel.sizeDelta.x, h);
        panel.anchoredPosition = new Vector2(panel.anchoredPosition.x, top - h * 0.5f);

        float firstRowY = h * 0.5f - 8f - kRowHeight * 0.5f;
        for (int i = 0; i < count; i++)
        {
            var row = Find($"Emote{i + 1}");
            if (row == null)
            {
                row = Instantiate(template, panel);
                row.name = $"Emote{i + 1}";
            }
            var rowRt = (RectTransform)row.transform;
            rowRt.anchoredPosition = new Vector2(0f, firstRowY - i * kPitch);
            rowRt.sizeDelta = new Vector2(196f, kRowHeight);   // 옛(48px) 프리팹 행도 새 크기로 통일
            var label = row.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null)
            {
                label.text = EmoteDefs.All[i].Line;
                label.textWrappingMode = TextWrappingModes.NoWrap;   // 긴 대사는 줄바꿈 대신 글자 축소
                label.enableAutoSizing = true;
                label.fontSizeMax = 20f; label.fontSizeMin = 12f;   // 생성기와 동일(행 44px 기준)
            }

            int index = i;
            var btn = row.GetComponent<Button>();
            if (btn == null) continue;
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(() =>
            {
                MobileGameplayInput.TriggerEmote(index);
                if (m_EmotePanel != null) m_EmotePanel.SetActive(false);
            });
        }
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
        bool inGame = s_InGameScene;
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
        bool force = preview || MobileLayoutCustomizer.Editing;   // 배치 편집 중엔 전부 선명·터치 가능(드래그용)
        SetActionVisible(m_ProcessButton, force || MobileGameplayInput.ProcessActionAvailable);
        SetActionVisible(m_RevertButton, force || MobileGameplayInput.ProcessCancelAvailable);
        SetActionVisible(m_ThrowButton, force || MobileGameplayInput.ThrowAvailable);
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

    // 사용 불가 버튼을 숨기는 대신 반투명으로 — 버튼이 '까꿍' 나타나지 않고 항상 제자리에 있다(기획 피드백 2026-08-30).
    // 비활성 동안엔 터치도 통과시켜(blocksRaycasts=false) 그 자리의 카메라 드래그·월드 탭을 막지 않는다.
    private const float kDisabledAlpha = 0.35f;

    private static void SetActionVisible(GameObject target, bool available)
    {
        if (target == null) return;
        if (!target.activeSelf) target.SetActive(true);   // 프리팹에 숨김으로 저장돼 있던 상태 복구
        var cg = target.GetComponent<CanvasGroup>();
        if (cg == null) cg = target.AddComponent<CanvasGroup>();
        float a = available ? 1f : kDisabledAlpha;
        if (cg.alpha == a) return;
        cg.alpha = a;
        cg.interactable = available;
        cg.blocksRaycasts = available;
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
