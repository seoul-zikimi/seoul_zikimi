using GridSystem;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed class JobsnailGameLoopHUD : MonoBehaviour
{
    private GameLoopManager m_Loop;
    private TextMeshProUGUI m_TimerText;
    private TextMeshProUGUI m_ResultScoreText;
    private TextMeshProUGUI m_ResultNamesText;
    private TextMeshProUGUI m_ResultStructText;
    private TextMeshProUGUI m_ResultTimeText;
    private TextMeshProUGUI m_ResultGradeText;
    private Image m_ResultGradeImage;
    private RawImage m_ResultImage;
    private AnswerPreview m_AnswerPreview;
    private GameObject m_TopBar;
    private GameObject m_ConsentBar;
    private GameObject m_ResultPanel;
    private GameObject m_SettingsPopup;
    private Button m_SettingsButton;
    private Button m_EndRequestButton;
    private TextMeshProUGUI m_EndRequestButtonLabel;
    private Image[] m_PeopleIcons;
    private bool m_ResultDismissed;
    private bool m_UrgentBgmStarted;
    private bool m_LastVisible;
    private int m_LastTimerSeconds = int.MinValue;
    private bool m_LastTimerBuilding;
    private bool m_LastLocalConsent;
    private bool m_LastEndButtonBuilding;
    private int m_LastPlayerCount = int.MinValue;
    private int m_LastConsentCount = int.MinValue;
    private bool m_LastResultShowing;
    private int m_LastResultPercent = int.MinValue;
    private int m_LastElapsedSeconds = int.MinValue;
    private int m_LastNameCount = int.MinValue;
    private string m_LastAnswerName;

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
        if (secs != m_LastTimerSeconds || m_Loop.IsBuilding != m_LastTimerBuilding)
        {
            m_LastTimerSeconds = secs;
            m_LastTimerBuilding = m_Loop.IsBuilding;
            SetTextIfChanged(m_TimerText, m_Loop.IsBuilding ? $"{secs / 60}:{secs % 60:00}" : "종료");
        }

        UpdateResultPanel();

        if (m_EndRequestButton != null)
        {
            bool localConsent = m_Loop.HasLocalConsent;
            if (localConsent != m_LastLocalConsent || m_Loop.IsBuilding != m_LastEndButtonBuilding)
            {
                m_LastLocalConsent = localConsent;
                m_LastEndButtonBuilding = m_Loop.IsBuilding;
                string verb = m_Loop.IsBuilding ? "종료 요청" : "재시작";
                SetTextIfChanged(m_EndRequestButtonLabel, (localConsent ? "동의 취소" : verb) + " (Enter)");
                SetButtonColor(m_EndRequestButton, localConsent ? new Color(0.56f, 0.86f, 0.48f, 1f) : new Color(1f, 0.78f, 0.44f, 1f));
            }
        }
        if (m_PeopleIcons != null && (m_Loop.PlayerCount != m_LastPlayerCount || m_Loop.ConsentCount != m_LastConsentCount))
        {
            m_LastPlayerCount = m_Loop.PlayerCount;
            m_LastConsentCount = m_Loop.ConsentCount;
            for (int i = 0; i < m_PeopleIcons.Length; i++)
            {
                bool connected = i < m_Loop.PlayerCount;
                SetActiveIfChanged(m_PeopleIcons[i].gameObject, connected);
                if (connected)   // 동의=검정(채움) / 미동의=흐린(빈칸)
                    SetImageColorIfChanged(m_PeopleIcons[i], i < m_Loop.ConsentCount ? Color.black : new Color(0f, 0f, 0f, 0.28f));
            }
        }

    }

    private void Rebuild()
    {
        var root = transform;

        for (int i = root.childCount - 1; i >= 0; i--)
            Destroy(root.GetChild(i).gameObject);

        m_Loop = null;
        m_UrgentBgmStarted = false;
        ResetCachedUiState();

        var top = JobsnailUiKit.Box("TopBar", root, new Vector2(0.42f, 0.92f), new Vector2(0.58f, 0.99f), Vector2.zero, Vector2.zero, new Color(0.84f, 0.82f, 0.70f, 0.92f));
        m_TopBar = top.gameObject;
        m_TimerText = JobsnailUiKit.Label("Timer", top.transform, "0:00", 34, Color.black, TextAlignmentOptions.Center, Vector2.zero, Vector2.zero);

        // 종료 요청(우상단, 톱니 왼쪽): 인원 아이콘 + [종료 요청] 버튼. 클릭/Enter 둘 다 종료 동의 토글 → 전원 동의 시 종료.
        var cbar = JobsnailUiKit.Box("EndRequestCluster", root, new Vector2(0.70f, 0.925f), new Vector2(0.945f, 0.985f), Vector2.zero, Vector2.zero, new Color(1f, 1f, 1f, 0f));
        m_ConsentBar = cbar.gameObject;
        var snail = JobsnailUiKit.Sprite("UI_pngs/3.inGame/Person_Icon") ?? JobsnailUiKit.Sprite("UI_pngs/3.inGame/Snail_Icon");
        m_PeopleIcons = new Image[4];
        for (int i = 0; i < m_PeopleIcons.Length; i++)
        {
            m_PeopleIcons[i] = JobsnailUiKit.Box($"P{i}", cbar.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(16 + i * 26, 0), new Vector2(22, 22), Color.white);
            m_PeopleIcons[i].sprite = snail;
            m_PeopleIcons[i].preserveAspect = true;
        }
        m_EndRequestButton = JobsnailUiKit.Button("EndRequestButton", cbar.transform, null, new Vector2(0.46f, 0.12f), new Vector2(1f, 0.88f), Vector2.zero, Vector2.zero, OnEndRequest, "종료 요청");
        m_EndRequestButtonLabel = m_EndRequestButton != null ? m_EndRequestButton.GetComponentInChildren<TextMeshProUGUI>() : null;
        SetButtonColor(m_EndRequestButton, new Color(1f, 0.78f, 0.44f, 1f));

        BuildSettingsButton(root);
        BuildSettingsPopup(root);
        BuildResultPanel(root);

        if (SoundManager.Instance != null)
            SoundManager.Instance.SetPhase(global::GamePhase.Building);
    }

    private void SetVisible(bool visible)
    {
        if (m_LastVisible == visible)
            return;

        m_LastVisible = visible;
        if (m_TopBar != null)
            SetActiveIfChanged(m_TopBar, visible);
        if (m_ConsentBar != null)
            SetActiveIfChanged(m_ConsentBar, visible);
        if (m_SettingsButton != null)
            SetActiveIfChanged(m_SettingsButton.gameObject, visible);
        if (!visible)
        {
            if (m_ResultPanel != null)
                SetActiveIfChanged(m_ResultPanel, false);
            if (m_SettingsPopup != null)
                SetActiveIfChanged(m_SettingsPopup, false);
        }
    }

    private void ResetCachedUiState()
    {
        m_LastVisible = true;
        m_LastTimerSeconds = int.MinValue;
        m_LastTimerBuilding = false;
        m_LastLocalConsent = false;
        m_LastEndButtonBuilding = false;
        m_LastPlayerCount = int.MinValue;
        m_LastConsentCount = int.MinValue;
        m_LastResultShowing = false;
        m_LastResultPercent = int.MinValue;
        m_LastElapsedSeconds = int.MinValue;
        m_LastNameCount = int.MinValue;
        m_LastAnswerName = null;
    }

    private static void SetActiveIfChanged(GameObject go, bool active)
    {
        if (go != null && go.activeSelf != active)
            go.SetActive(active);
    }

    private static void SetTextIfChanged(TMP_Text text, string value)
    {
        value ??= string.Empty;
        if (text != null && text.text != value)
            text.text = value;
    }

    private static void SetImageColorIfChanged(Image image, Color color)
    {
        if (image != null && image.color != color)
            image.color = color;
    }

    private static void SetTextColorIfChanged(TMP_Text text, Color color)
    {
        if (text != null && text.color != color)
            text.color = color;
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
            new Vector2(0.34f, 0.24f),
            new Vector2(0.66f, 0.76f),
            Vector2.zero,
            Vector2.zero,
            new Color(1f, 0.97f, 0.86f, 0.98f)).gameObject;
        m_SettingsPopup.SetActive(false);

        JobsnailUiKit.Label("Title", m_SettingsPopup.transform, "설정", 30, Color.black, TextAlignmentOptions.Center, new Vector2(0, 210), new Vector2(360, 56));

        // 볼륨 조절 UI (BGM / SFX)
        MakeVolumeSlider(m_SettingsPopup.transform, "BGM", new Vector2(0, 120), PlayerPrefs.GetFloat("BGMVolume", 0.8f), value =>
        {
            if (SoundManager.Instance != null) SoundManager.Instance.SetBGMVolume(value);
            else PlayerPrefs.SetFloat("BGMVolume", value);
        });
        MakeVolumeSlider(m_SettingsPopup.transform, "SFX", new Vector2(0, 60), PlayerPrefs.GetFloat("SFXVolume", 1f), value =>
        {
            if (SoundManager.Instance != null) SoundManager.Instance.SetSFXVolume(value);
            else PlayerPrefs.SetFloat("SFXVolume", value);
        });

        // 마우스 감도 조절 (PlayerCameraController 공유 static + PlayerPrefs)
        MakeVolumeSlider(m_SettingsPopup.transform, "감도", new Vector2(0, 0), PlayerPrefs.GetFloat("MouseSensitivity", 0.5f),
            value => Player.PlayerCameraController.SetSensitivity01(value));

        // 게임 나가기 (세션 이탈 → 로비)
        var exit = JobsnailUiKit.Button("ExitGameButton", m_SettingsPopup.transform, null, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -95), new Vector2(320, 52), async () =>
        {
            await JobsnailSessionManager.Instance.LeaveLobbyRoomSecurelyAsync();
        }, "게임 나가기");
        SetButtonColor(exit, new Color(1f, 0.62f, 0.62f, 1f));   // 분홍(주의)

        var close = JobsnailUiKit.Button("SettingsCloseButton", m_SettingsPopup.transform, null, new Vector2(0.88f, 0.88f), new Vector2(0.98f, 0.98f), Vector2.zero, Vector2.zero, ToggleSettingsPopup, "×");
        SetButtonColor(close, new Color(1f, 0.97f, 0.86f, 0f));
    }

    private void BuildResultPanel(Transform root)
    {
        m_ResultPanel = JobsnailUiKit.Box(
            "ResultPanel",
            root,
            new Vector2(0.30f, 0.10f),
            new Vector2(0.70f, 0.90f),
            Vector2.zero,
            Vector2.zero,
            new Color(1f, 1f, 1f, 0.98f)).gameObject;
        m_ResultPanel.SetActive(false);

        // ── 상단: 제목 / 명단 / JOBSNAIL 로고 ──
        var title = JobsnailUiKit.Label("Title", m_ResultPanel.transform, "정산서", 44, Color.black, TextAlignmentOptions.Center, new Vector2(0, 378), new Vector2(360, 64));
        title.fontStyle = FontStyles.Bold;
        m_ResultNamesText = JobsnailUiKit.Label("Players", m_ResultPanel.transform, "", 15, new Color(0.30f, 0.22f, 0.15f, 1f), TextAlignmentOptions.Left, new Vector2(-235, 320), new Vector2(290, 26));

        // JOBSNAIL 워드마크: 전용 PNG 있으면 그걸, 없으면 주황 굵은 텍스트로. 옆에 달팽이 아이콘.
        var brandSprite = JobsnailUiKit.Sprite("UI_pngs/3.inGame/JobSnailLogo");
        if (brandSprite != null)
        {
            var logo = JobsnailUiKit.Box("Logo", m_ResultPanel.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(245, 320), new Vector2(180, 50), Color.white);
            logo.sprite = brandSprite;
            logo.preserveAspect = true;
        }
        else
        {
            var brand = JobsnailUiKit.Label("Logo", m_ResultPanel.transform, "JOBSNAIL", 26, new Color(0.95f, 0.42f, 0.12f, 1f), TextAlignmentOptions.Right, new Vector2(215, 320), new Vector2(200, 40));
            brand.fontStyle = FontStyles.Bold | FontStyles.Italic;
            if (TMP_Settings.defaultFontAsset != null) brand.font = TMP_Settings.defaultFontAsset;   // 한글 폰트에 영문 글리프 없을 때 □ 방지
        }

        // JOBSNAIL 옆 달팽이 아이콘
        var snailSprite = JobsnailUiKit.Sprite("UI_pngs/3.inGame/Snail_Icon");
        if (snailSprite != null)
        {
            var snail = JobsnailUiKit.Box("LogoSnail", m_ResultPanel.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(356, 320), new Vector2(32, 32), Color.white);
            snail.sprite = snailSprite;
            snail.preserveAspect = true;
        }

        // ── 완성 건축물 이미지(AnswerPreview RenderTexture) + 프레임 ──
        JobsnailUiKit.Box("ImageFrame", m_ResultPanel.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 115), new Vector2(340, 270), new Color(0.85f, 0.85f, 0.85f, 1f));
        m_ResultImage = MakeRawImage(m_ResultPanel.transform, new Vector2(0, 115), new Vector2(324, 254));

        // ── 구조물명 / 소요시간 / 완성도 / 등급 스탬프 ──
        m_ResultStructText = JobsnailUiKit.Label("Structure", m_ResultPanel.transform, "", 24, Color.black, TextAlignmentOptions.Center, new Vector2(0, -50), new Vector2(440, 38));
        m_ResultTimeText = JobsnailUiKit.Label("Time", m_ResultPanel.transform, "소요시간     0 : 00", 22, new Color(0.20f, 0.18f, 0.14f, 1f), TextAlignmentOptions.Center, new Vector2(0, -108), new Vector2(440, 36));
        m_ResultScoreText = JobsnailUiKit.Label("Score", m_ResultPanel.transform, "건축 0 % 완료", 30, Color.black, TextAlignmentOptions.Center, new Vector2(0, -172), new Vector2(440, 52));

        // 등급(EXCELLENT/TRY AGAIN 등) 뒤에 깔리는 별 배경 — 등급보다 먼저 생성해 뒤에 그려짐.
        var starSprite = JobsnailUiKit.Sprite("UI_pngs/3.inGame/star");
        var star = JobsnailUiKit.Box("GradeStar", m_ResultPanel.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(160, -172), new Vector2(96, 96), starSprite != null ? Color.white : new Color(1f, 1f, 1f, 0f));
        star.sprite = starSprite;
        star.preserveAspect = true;

        m_ResultGradeText = JobsnailUiKit.Label("Grade", m_ResultPanel.transform, "", 34, new Color(0.85f, 0.15f, 0.12f, 1f), TextAlignmentOptions.Center, new Vector2(175, -172), new Vector2(240, 70));
        m_ResultGradeText.fontStyle = FontStyles.Bold;
        m_ResultGradeText.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -12f);

        // EXCELLENT! 스탬프 아트(있으면 텍스트 대신 사용, PNG가 이미 기울어져 있어 회전 안 함)
        var stampSprite = JobsnailUiKit.Sprite("UI_pngs/3.inGame/exellent");
        m_ResultGradeImage = JobsnailUiKit.Box("GradeStamp", m_ResultPanel.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(150, -172), new Vector2(240, 88), stampSprite != null ? Color.white : new Color(1f, 1f, 1f, 0f));
        m_ResultGradeImage.sprite = stampSprite;
        m_ResultGradeImage.preserveAspect = true;
        m_ResultGradeImage.gameObject.SetActive(false);

        // ── 구분선 ──
        JobsnailUiKit.Box("Divider", m_ResultPanel.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -252), new Vector2(640, 3), new Color(0.80f, 0.80f, 0.80f, 1f));

        // ── 방으로 돌아가기 / 나가기 (둘 다 베이지, 목업과 동일) ──
        var room = JobsnailUiKit.Button("RoomButton", m_ResultPanel.transform, null, new Vector2(0.14f, 0.05f), new Vector2(0.49f, 0.13f), Vector2.zero, Vector2.zero, () =>
        {
            if (m_Loop != null) m_Loop.RequestReturnToRoom();
        }, "방으로 돌아가기");
        SetButtonColor(room, new Color(0.97f, 0.85f, 0.58f, 1f));

        var leave = JobsnailUiKit.Button("LeaveButton", m_ResultPanel.transform, null, new Vector2(0.51f, 0.05f), new Vector2(0.86f, 0.13f), Vector2.zero, Vector2.zero, async () =>
        {
            await JobsnailSessionManager.Instance.LeaveLobbyRoomSecurelyAsync();
        }, "나가기");
        SetButtonColor(leave, new Color(0.97f, 0.85f, 0.58f, 1f));
    }

    private void UpdateResultPanel()
    {
        if (m_ResultPanel == null || m_Loop == null)
            return;

        if (m_Loop.IsBuilding) m_ResultDismissed = false;   // 새 라운드 → 다음 종료 때 결과창 다시 표시
        bool show = !m_Loop.IsBuilding && !m_ResultDismissed;
        if (show != m_LastResultShowing)
        {
            m_LastResultShowing = show;
            SetActiveIfChanged(m_ResultPanel, show);
            if (show)
            {
                m_LastResultPercent = int.MinValue;
                m_LastElapsedSeconds = int.MinValue;
                m_LastNameCount = int.MinValue;
                m_LastAnswerName = null;
            }
        }
        if (!show)
            return;

        var score = m_Loop.Score;
        int pct = Mathf.RoundToInt(score.Percent);
        if (pct != m_LastResultPercent)
        {
            m_LastResultPercent = pct;
            SetTextIfChanged(m_ResultScoreText, $"건축 {pct} % 완료");
            UpdateGrade(pct);
        }

        if (m_ResultStructText != null)
        {
            string nm = m_Loop.AnswerName;
            if (nm != m_LastAnswerName)
            {
                m_LastAnswerName = nm;
                SetTextIfChanged(m_ResultStructText, string.IsNullOrEmpty(nm) ? "" : nm);
            }
        }

        if (m_ResultTimeText != null)
        {
            int e = Mathf.Max(0, Mathf.RoundToInt(m_Loop.Elapsed));
            if (e != m_LastElapsedSeconds)
            {
                m_LastElapsedSeconds = e;
                SetTextIfChanged(m_ResultTimeText, $"소요시간     {e / 60} : {e % 60:00}");
            }
        }

        if (m_ResultNamesText != null && m_Loop.NameCount != m_LastNameCount)
        {
            m_LastNameCount = m_Loop.NameCount;
            string names = "";
            for (int i = 0; i < m_Loop.NameCount; i++)
                names += (i > 0 ? ", " : "") + m_Loop.GetName(i);
            SetTextIfChanged(m_ResultNamesText, names);
        }

        if (m_ResultImage != null)
        {
            if (m_AnswerPreview == null) m_AnswerPreview = FindFirstObjectByType<AnswerPreview>();
            if (m_AnswerPreview != null && m_AnswerPreview.RT != null && m_ResultImage.texture != m_AnswerPreview.RT)
                m_ResultImage.texture = m_AnswerPreview.RT;
        }
    }

    private void UpdateGrade(int pct)
    {
        if (m_ResultGradeText == null)
            return;

        bool useStamp = pct >= 90 && m_ResultGradeImage != null && m_ResultGradeImage.sprite != null;
        if (m_ResultGradeImage != null) SetActiveIfChanged(m_ResultGradeImage.gameObject, useStamp);
        SetActiveIfChanged(m_ResultGradeText.gameObject, !useStamp);

        if (pct >= 90)
        {
            SetTextIfChanged(m_ResultGradeText, "EXCELLENT!");
            SetTextColorIfChanged(m_ResultGradeText, new Color(0.85f, 0.15f, 0.12f, 1f));
        }
        else if (pct >= 70)
        {
            SetTextIfChanged(m_ResultGradeText, "GREAT!");
            SetTextColorIfChanged(m_ResultGradeText, new Color(0.90f, 0.45f, 0.10f, 1f));
        }
        else if (pct >= 50)
        {
            SetTextIfChanged(m_ResultGradeText, "GOOD!");
            SetTextColorIfChanged(m_ResultGradeText, new Color(0.30f, 0.55f, 0.85f, 1f));
        }
        else
        {
            SetTextIfChanged(m_ResultGradeText, "TRY AGAIN");
            SetTextColorIfChanged(m_ResultGradeText, new Color(0.45f, 0.40f, 0.35f, 1f));
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

    private static RawImage MakeRawImage(Transform parent, Vector2 anchored, Vector2 size)
    {
        var rt = JobsnailUiKit.Rect("ResultImage", parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), anchored, size);
        var ri = rt.gameObject.AddComponent<RawImage>();
        ri.raycastTarget = false;
        return ri;
    }

    private void OnEndRequest()
    {
        if (m_Loop == null) m_Loop = FindFirstObjectByType<GameLoopManager>();
        if (m_Loop != null) m_Loop.RequestToggleConsent();
    }

    private static void SetButtonColor(Button button, Color color)
    {
        if (button != null && button.targetGraphic != null)
            button.targetGraphic.color = color;
    }
}
