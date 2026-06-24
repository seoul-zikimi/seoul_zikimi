using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed class JobsnailLobbySkinner : MonoBehaviour
{
    private static Font s_DefaultFont;

    private GameObject m_EntryOverlay;
    private GameObject m_SessionOverlay;
    private GameObject m_CreateOverlay;
    private GameObject m_LobbyRoomOverlay;
    private RectTransform m_SessionPcRoot;
    private StartUGUI m_StartUi;
    private InputField m_CustomRoomNameInput;
    private InputField m_CustomPasswordInput;
    private Text m_CustomCreateStatus;
    private Text m_MaxPlayersText;
    private GameObject m_MaxPlayersOptions;
    private int m_SelectedMaxPlayers = 1;
    private bool m_IsPrivateRoom;
    private Image m_PrivateRoomButtonImage;
    private Image m_PublicRoomButtonImage;
    private GameObject m_PasswordLabel;
    private GameObject m_PasswordHint;
    private CreateSession m_CreateSession;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != SceneNames.Lobby)
            return;

        var canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null || canvas.GetComponent<JobsnailLobbySkinner>() != null)
            return;

        canvas.gameObject.AddComponent<JobsnailLobbySkinner>();
    }

    private void Start()
    {
        var canvas = GetComponent<Canvas>();
        if (canvas != null)
        {
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            if (!TryGetComponent(out GraphicRaycaster _))
                gameObject.AddComponent<GraphicRaycaster>();
            if (!TryGetComponent(out CanvasScaler scaler))
                scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
        }

        AddBackground();
        AddHudFrames(transform);
        SkinButtons(transform);
        SkinTexts(transform);
        SkinPanels(transform);
        if (SoundManager.Instance != null)
            SoundManager.Instance.SetPhase(global::GamePhase.Lobby);
        m_StartUi = FindFirstObjectByType<StartUGUI>(FindObjectsInactive.Include);
        m_CreateSession = FindFirstObjectByType<CreateSession>(FindObjectsInactive.Include);
        if (m_CreateSession != null)
        {
            m_CreateSession.CreateSessioinBtnOnClick -= OnCreateSessionCompleted;
            m_CreateSession.CreateSessioinBtnOnClick += OnCreateSessionCompleted;
            m_CreateSession.CreateSessionFailed -= OnCreateSessionFailed;
            m_CreateSession.CreateSessionFailed += OnCreateSessionFailed;
        }
        ResetToEntryIfNotConnected(transform);
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
            ShowSessionList();
        else
            RefreshEntryOverlay();
    }

    private void OnDestroy()
    {
        if (m_CreateSession != null)
        {
            m_CreateSession.CreateSessioinBtnOnClick -= OnCreateSessionCompleted;
            m_CreateSession.CreateSessionFailed -= OnCreateSessionFailed;
        }
    }

    private void Update()
    {
        RefreshEntryOverlay();
    }

    private void AddBackground()
    {
        if (transform.Find("@JobsnailSessionBackground") != null)
            return;

        var bg = JobsnailUiKit.Image("@JobsnailSessionBackground", transform, JobsnailUiKit.Sprite("UI_pngs/2.sesh/Session_BG"));
        bg.transform.SetAsFirstSibling();
    }

    private static void SkinButtons(Transform root)
    {
        foreach (var button in root.GetComponentsInChildren<Button>(true))
        {
            var image = button.GetComponent<Image>();
            if (image == null)
                image = button.gameObject.AddComponent<Image>();

            string n = button.name.ToLowerInvariant();
            Sprite sprite = null;

            if (n.Contains("create") || n.Contains("make"))
                sprite = JobsnailUiKit.Sprite("UI_pngs/2.sesh/MakeSession_Btn");
            else if (n.Contains("start"))
                sprite = JobsnailUiKit.Sprite("UI_pngs/2.sesh/StartBuild_Btn");
            else if (n.Contains("quit") || n.Contains("leave") || n.Contains("back"))
                sprite = JobsnailUiKit.Sprite("UI_pngs/2.sesh/QuitSession_Btn");
            else if (n.Contains("ready"))
                sprite = JobsnailUiKit.Sprite("UI_pngs/2.sesh/Ready_White_Btn");
            else if (n.Contains("join") || n.Contains("host"))
                image.color = new Color(1f, 0.80f, 0.50f, 1f);

            if (sprite != null)
            {
                image.sprite = sprite;
                image.color = Color.white;
                image.preserveAspect = true;
                HideChildLabels(button.transform);
            }

            button.targetGraphic = image;
        }
    }

    private static void HideChildLabels(Transform button)
    {
        foreach (var label in button.GetComponentsInChildren<TextMeshProUGUI>(true))
            label.gameObject.SetActive(false);
    }

    private static void SkinTexts(Transform root)
    {
        foreach (var label in root.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            label.color = JobsnailUiKit.Brown;
            if (label.fontSize < 18)
                label.fontSize = 18;
        }
    }

    private static void SkinPanels(Transform root)
    {
        foreach (var image in root.GetComponentsInChildren<Image>(true))
        {
            if (image.transform == root || image.transform.name.StartsWith("@Jobsnail"))
                continue;

            if (image.GetComponent<Button>() != null ||
                image.GetComponent<Toggle>() != null ||
                image.GetComponent<InputField>() != null ||
                image.GetComponent<TMP_InputField>() != null ||
                image.GetComponent<Scrollbar>() != null)
                continue;

            string n = image.name.ToLowerInvariant();
            if (n.Contains("hud") || n.Contains("panel") || IsBigWhitePanel(image))
                image.color = new Color(1f, 1f, 1f, 0f);
            else if (n.Contains("background"))
                image.color = new Color(1f, 0.96f, 0.78f, 1f);
        }
    }

    private static bool IsBigWhitePanel(Image image)
    {
        var rt = image.rectTransform;
        bool isLarge = rt.rect.width > 500f && rt.rect.height > 300f;
        Color c = image.color;
        bool isWhiteish = c.r > 0.85f && c.g > 0.85f && c.b > 0.85f;
        return isLarge && isWhiteish;
    }

    private static void AddHudFrames(Transform root)
    {
        AddFrame(root, "StartHUD", "UI_pngs/2.sesh/Session_PC_BG");
        AddFrame(root, "SessionListHUD", "UI_pngs/2.sesh/Session_PC_BG");
        AddFrame(root, "CreateSessionHUD", "UI_pngs/2.sesh/MakeSession_Popup");
        AddFrame(root, "JoinByCodeHUD", "UI_pngs/2.sesh/Alert_Popup");
        AddFrame(root, "LobbyRoomHUD", "UI_pngs/2.sesh/Session_PC_BG");
        AddFrame(root, "JoinCodeHUD", "UI_pngs/2.sesh/Alert_Popup");
    }

    private static void AddFrame(Transform root, string hudName, string spritePath)
    {
        var hud = FindDeep(root, hudName);
        if (hud == null || hud.Find("@JobsnailFrame") != null)
            return;

        var frame = JobsnailUiKit.Image("@JobsnailFrame", hud, JobsnailUiKit.Sprite(spritePath));
        frame.transform.SetAsFirstSibling();

        var rt = frame.rectTransform;
        rt.anchorMin = new Vector2(0.08f, 0.08f);
        rt.anchorMax = new Vector2(0.92f, 0.92f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        frame.preserveAspect = true;
    }

    private static void ResetToEntryIfNotConnected(Transform root)
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            return;

        SetActive(root, "StartHUD", true);
        SetActive(root, "CreateSessionHUD", false);
        SetActive(root, "SessionListHUD", false);
        SetActive(root, "JoinCodeHUD", false);
        SetActive(root, "JoinByCodeHUD", false);
        SetActive(root, "LobbyRoomHUD", false);
    }

    private static void SetActive(Transform root, string name, bool active)
    {
        var child = FindDeep(root, name);
        if (child != null)
            child.gameObject.SetActive(active);
    }

    private void AddEntryOverlay(Transform root)
    {
        var oldOverlay = root.Find("@JobsnailEntryOverlay");
        if (oldOverlay != null)
        {
            m_EntryOverlay = oldOverlay.gameObject;
            return;
        }

        var overlay = new GameObject("@JobsnailEntryOverlay", typeof(RectTransform));
        overlay.transform.SetParent(root, false);
        var overlayRt = (RectTransform)overlay.transform;
        overlayRt.anchorMin = Vector2.zero;
        overlayRt.anchorMax = Vector2.one;
        overlayRt.offsetMin = Vector2.zero;
        overlayRt.offsetMax = Vector2.zero;
        overlay.transform.SetAsLastSibling();
        m_EntryOverlay = overlay;

        var panel = JobsnailUiKit.Box("Panel", overlay.transform, new Vector2(0.24f, 0.20f), new Vector2(0.76f, 0.82f), Vector2.zero, Vector2.zero, new Color(1f, 0.96f, 0.78f, 1f));
        panel.raycastTarget = false;

        MakeText(overlay.transform, "구인 건설 현장 리스트", 36, Color.black, new Vector2(0, 210), new Vector2(640, 70), TextAnchor.MiddleCenter);

        MakeVisibleButton(overlay.transform, "방 만들기", new Vector2(0.36f, 0.46f), new Vector2(0.64f, 0.56f), () =>
        {
            ShowCreateSession();
        });
        MakeVisibleButton(overlay.transform, "방 목록 보기 / 입장", new Vector2(0.36f, 0.32f), new Vector2(0.64f, 0.42f), () =>
        {
            ShowSessionList();
        });
        MakeVisibleButton(overlay.transform, "메인으로", new Vector2(0.04f, 0.06f), new Vector2(0.16f, 0.13f), () =>
        {
            if (NetworkManager.Singleton != null) NetworkManager.Singleton.Shutdown();
            SceneManager.LoadScene(SceneNames.BootstrapScene);
        }, 18);
    }

    private void RefreshEntryOverlay()
    {
        bool isConnected = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        if (isConnected)
        {
            HideEntryOverlay();
            HideCustomCreateOverlay();
            HideCustomSessionOverlay();
            SetActive(transform, "StartHUD", false);
            SetActive(transform, "CreateSessionHUD", false);
            SetActive(transform, "SessionListHUD", false);
            SetActive(transform, "JoinCodeHUD", false);
            SetActive(transform, "JoinByCodeHUD", false);
            SetActive(transform, "LobbyRoomHUD", false);
            ShowCustomLobbyRoomOverlay();
            return;
        }

        HideCustomLobbyRoomOverlay();
        SetActive(transform, "LobbyRoomHUD", false);

        bool secondaryScreenOpen =
            IsCustomScreenOpen() ||
            IsActive("CreateSessionHUD") ||
            IsActive("SessionListHUD") ||
            IsActive("JoinCodeHUD") ||
            IsActive("JoinByCodeHUD");

        SetActive(transform, "StartHUD", !secondaryScreenOpen);

        if (m_EntryOverlay != null)
        {
            m_EntryOverlay.SetActive(!secondaryScreenOpen);
            m_EntryOverlay.transform.SetAsLastSibling();
        }
    }

    private bool IsActive(string name)
    {
        var child = FindDeep(transform, name);
        return child != null && child.gameObject.activeInHierarchy;
    }

    private void HideEntryOverlay()
    {
        if (m_EntryOverlay != null)
            m_EntryOverlay.SetActive(false);
    }

    private void ShowCreateSession()
    {
        Debug.Log("[JobsnailLobbySkinner] Open CreateSessionHUD");
        HideEntryOverlay();
        HideOriginalLobbyHuds();
        ShowCustomSessionOverlay();
        ShowCustomCreateOverlay();
    }

    private void ShowSessionList()
    {
        Debug.Log("[JobsnailLobbySkinner] Open SessionListHUD");
        HideEntryOverlay();
        HideCustomCreateOverlay();
        HideOriginalLobbyHuds();
        ShowCustomSessionOverlay();
    }

    private bool IsCustomScreenOpen()
    {
        return (m_SessionOverlay != null && m_SessionOverlay.activeSelf) ||
               (m_CreateOverlay != null && m_CreateOverlay.activeSelf);
    }

    private void HideOriginalLobbyHuds()
    {
        SetActive(transform, "StartHUD", false);
        SetActive(transform, "CreateSessionHUD", false);
        SetActive(transform, "SessionListHUD", false);
        SetActive(transform, "JoinCodeHUD", false);
        SetActive(transform, "JoinByCodeHUD", false);
        SetActive(transform, "LobbyRoomHUD", false);
        HideOriginalHudGraphics("CreateSessionHUD");
        HideOriginalHudGraphics("SessionListHUD");
    }

    private void ShowCustomSessionOverlay()
    {
        if (m_SessionOverlay == null)
            BuildCustomSessionOverlay();

        if (m_SessionOverlay != null)
        {
            m_SessionOverlay.SetActive(true);
            m_SessionOverlay.transform.SetAsLastSibling();
        }
    }

    private void HideCustomSessionOverlay()
    {
        if (m_SessionOverlay != null)
            m_SessionOverlay.SetActive(false);
    }

    private void BuildCustomSessionOverlay()
    {
        m_SessionOverlay = new GameObject("@JobsnailSessionOverlay", typeof(RectTransform));
        m_SessionOverlay.transform.SetParent(transform, false);
        var overlayRt = (RectTransform)m_SessionOverlay.transform;
        overlayRt.anchorMin = Vector2.zero;
        overlayRt.anchorMax = Vector2.one;
        overlayRt.offsetMin = Vector2.zero;
        overlayRt.offsetMax = Vector2.zero;

        m_SessionPcRoot = new GameObject("PcRoot", typeof(RectTransform)).GetComponent<RectTransform>();
        m_SessionPcRoot.SetParent(m_SessionOverlay.transform, false);
        m_SessionPcRoot.anchorMin = new Vector2(0.5f, 0.5f);
        m_SessionPcRoot.anchorMax = new Vector2(0.5f, 0.5f);
        m_SessionPcRoot.anchoredPosition = Vector2.zero;
        m_SessionPcRoot.sizeDelta = new Vector2(1210, 765);

        var pcSprite = JobsnailUiKit.Sprite("UI_pngs/2.sesh/Session_PC_BG");
        var pc = JobsnailUiKit.Image("SessionPc", m_SessionPcRoot, pcSprite);
        var pcRt = pc.rectTransform;
        pcRt.anchorMin = Vector2.zero;
        pcRt.anchorMax = Vector2.one;
        pcRt.offsetMin = Vector2.zero;
        pcRt.offsetMax = Vector2.zero;
        pc.preserveAspect = true;

        if (pcSprite == null)
        {
            var fallbackPanel = JobsnailUiKit.Box("PcFallbackPanel", m_SessionPcRoot, new Vector2(0.11f, 0.13f), new Vector2(0.89f, 0.86f), Vector2.zero, Vector2.zero, new Color(1f, 0.96f, 0.78f, 1f));
            fallbackPanel.transform.SetAsFirstSibling();
        }

        MakeText(m_SessionPcRoot, "구인 건설 현장 리스트", 34, Color.black, new Vector2(0, 205), new Vector2(520, 60), TextAnchor.MiddleCenter);
        MakeFixedButton(m_SessionPcRoot, "방 만들기", new Vector2(322, 204), new Vector2(122, 38), ShowCreateSession, 17);

        var listPanel = JobsnailUiKit.Box("ListPanel", m_SessionPcRoot, new Vector2(0.22f, 0.18f), new Vector2(0.78f, 0.70f), Vector2.zero, Vector2.zero, new Color(1f, 0.76f, 0.42f, 1f));
        listPanel.raycastTarget = false;

        MakeText(m_SessionPcRoot, "방을 만들면 대기실로 이동합니다", 18, new Color(0.25f, 0.18f, 0.12f, 1f), new Vector2(0, 20), new Vector2(420, 34), TextAnchor.MiddleCenter);

        MakeFixedButton(m_SessionPcRoot, "메인으로", new Vector2(-485, -290), new Vector2(105, 50), () =>
        {
            HideCustomSessionOverlay();
            if (NetworkManager.Singleton != null) NetworkManager.Singleton.Shutdown();
            SceneManager.LoadScene(SceneNames.BootstrapScene);
        }, 18, Color.white);
    }

    private void MakeRoomCard(Vector2 anchored, string roomName, string tag, string count, Color tagColor)
    {
        var cardSprite = JobsnailUiKit.Sprite("UI_pngs/2.sesh/SessionList_Unit");
        Image card;
        if (cardSprite != null)
        {
            card = JobsnailUiKit.Image("RoomCard", m_SessionPcRoot, cardSprite);
            var rt = card.rectTransform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchored;
            rt.sizeDelta = new Vector2(360, 136);
            card.preserveAspect = true;
        }
        else
        {
            card = JobsnailUiKit.Box("RoomCard", m_SessionPcRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), anchored, new Vector2(360, 136), Color.white);
        }
        card.raycastTarget = true;

        if (string.IsNullOrEmpty(roomName))
            return;

        MakeText(card.transform, roomName, 17, Color.black, new Vector2(0, 25), new Vector2(300, 32), TextAnchor.MiddleCenter);
        MakeText(card.transform, tag, 14, Color.black, new Vector2(-90, -28), new Vector2(82, 26), TextAnchor.MiddleCenter, tagColor);
        MakeText(card.transform, "인원 " + count, 14, Color.black, new Vector2(105, -28), new Vector2(90, 26), TextAnchor.MiddleCenter);
    }

    private void ShowCustomCreateOverlay()
    {
        if (m_CreateOverlay == null)
            BuildCustomCreateOverlay();

        if (m_CreateOverlay != null)
        {
            m_CreateOverlay.SetActive(true);
            m_CreateOverlay.transform.SetAsLastSibling();
        }
    }

    private void HideCustomCreateOverlay()
    {
        if (m_CreateOverlay != null)
            m_CreateOverlay.SetActive(false);
    }

    private void OnCreateSessionCompleted(bool active)
    {
        if (!active)
            return;

        HideCustomCreateOverlay();
        HideCustomSessionOverlay();
        SetActive(transform, "CreateSessionHUD", false);
        SetActive(transform, "SessionListHUD", false);
        SetActive(transform, "StartHUD", false);
        SetActive(transform, "JoinCodeHUD", false);
        SetActive(transform, "JoinByCodeHUD", false);
        EnsureHostStarted();
        SetActive(transform, "LobbyRoomHUD", false);
        ShowCustomLobbyRoomOverlay();
    }

    private void ShowCustomLobbyRoomOverlay()
    {
        if (m_LobbyRoomOverlay == null)
            BuildCustomLobbyRoomOverlay();

        if (m_LobbyRoomOverlay != null)
        {
            UpdateCustomLobbyRoomOverlay();
            m_LobbyRoomOverlay.SetActive(true);
            m_LobbyRoomOverlay.transform.SetAsLastSibling();
        }
    }

    private void HideCustomLobbyRoomOverlay()
    {
        if (m_LobbyRoomOverlay != null)
            m_LobbyRoomOverlay.SetActive(false);
    }

    private void BuildCustomLobbyRoomOverlay()
    {
        m_LobbyRoomOverlay = new GameObject("@JobsnailLobbyRoomOverlay", typeof(RectTransform));
        m_LobbyRoomOverlay.transform.SetParent(transform, false);
        var overlayRt = (RectTransform)m_LobbyRoomOverlay.transform;
        overlayRt.anchorMin = Vector2.zero;
        overlayRt.anchorMax = Vector2.one;
        overlayRt.offsetMin = Vector2.zero;
        overlayRt.offsetMax = Vector2.zero;

        var pcRoot = new GameObject("LobbyPcRoot", typeof(RectTransform)).GetComponent<RectTransform>();
        pcRoot.SetParent(m_LobbyRoomOverlay.transform, false);
        pcRoot.anchorMin = new Vector2(0.5f, 0.5f);
        pcRoot.anchorMax = new Vector2(0.5f, 0.5f);
        pcRoot.anchoredPosition = Vector2.zero;
        pcRoot.sizeDelta = new Vector2(1210, 765);

        var pcSprite = JobsnailUiKit.Sprite("UI_pngs/2.sesh/Session_PC_BG");
        var pc = JobsnailUiKit.Image("LobbySessionPc", pcRoot, pcSprite);
        var pcRt = pc.rectTransform;
        pcRt.anchorMin = Vector2.zero;
        pcRt.anchorMax = Vector2.one;
        pcRt.offsetMin = Vector2.zero;
        pcRt.offsetMax = Vector2.zero;
        pc.preserveAspect = true;

        MakeText(pcRoot, "구인 대기", 34, Color.black, new Vector2(0, 205), new Vector2(520, 60), TextAnchor.MiddleCenter);
        MakeText(pcRoot, "신체 건강한 달팽이 구합니다", 24, Color.black, new Vector2(0, 145), new Vector2(520, 42), TextAnchor.MiddleCenter);

        MakeLobbySlot(pcRoot, new Vector2(-275, 45), "유저 1", "방장 / 준비 완료");
        MakeLobbySlot(pcRoot, new Vector2(85, 45), "유저 2", "대기중...");
        MakeLobbySlot(pcRoot, new Vector2(-275, -105), "유저 3", "대기중...");
        MakeLobbySlot(pcRoot, new Vector2(85, -105), "유저 4", "대기중...");

        MakeText(pcRoot, "현재 선택된 맵 이미지", 16, Color.black, new Vector2(355, 10), new Vector2(150, 110), TextAnchor.MiddleCenter, new Color(0.82f, 0.82f, 0.82f, 1f));
        MakeFixedButton(pcRoot, "나가기", new Vector2(-485, -290), new Vector2(105, 50), () =>
        {
            if (NetworkManager.Singleton != null)
                NetworkManager.Singleton.Shutdown();
            SceneManager.LoadScene(SceneNames.BootstrapScene);
        }, 18, Color.white);

        MakeFixedButton(pcRoot, "건축 시작!", new Vector2(352, -235), new Vector2(150, 58), StartGameFromLobby, 18, new Color(1f, 0.78f, 0.44f, 1f));
        MakeText(pcRoot, "방장만 시작할 수 있어요", 13, new Color(0.35f, 0.25f, 0.18f, 1f), new Vector2(352, -285), new Vector2(220, 24), TextAnchor.MiddleCenter);
    }

    private static void MakeLobbySlot(Transform parent, Vector2 anchored, string name, string status)
    {
        var slot = JobsnailUiKit.Box("LobbyUserSlot", parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), anchored, new Vector2(330, 118), Color.white);
        MakeText(slot.transform, "유저\n캐릭터", 14, Color.black, new Vector2(-105, 0), new Vector2(84, 84), TextAnchor.MiddleCenter, new Color(0.86f, 0.86f, 0.86f, 1f));
        MakeText(slot.transform, name, 17, Color.black, new Vector2(38, 22), new Vector2(170, 30), TextAnchor.MiddleLeft);
        MakeText(slot.transform, status, 16, Color.black, new Vector2(38, -22), new Vector2(170, 30), TextAnchor.MiddleRight);
    }

    private void UpdateCustomLobbyRoomOverlay()
    {
        // 지금 기존 세션 코드에는 준비/멤버 목록 동기화가 아직 없어서,
        // 화면은 안정적인 대기실 레이아웃만 유지한다.
    }

    private static void StartGameFromLobby()
    {
        if (NetworkManager.Singleton == null)
            return;

        if (!NetworkManager.Singleton.IsServer)
            return;

        if (SoundManager.Instance != null)
            SoundManager.Instance.PlaySFX(SFXType.UIClick);

        NetworkManager.Singleton.SceneManager.LoadScene(SceneNames.GameScene, LoadSceneMode.Single);
    }

    private void OnCreateSessionFailed(string message)
    {
        SetCreateStatus(string.IsNullOrEmpty(message) ? "방 생성에 실패했어." : message);
    }

    private void BuildCustomCreateOverlay()
    {
        if (m_SessionPcRoot == null)
            ShowCustomSessionOverlay();

        m_CreateOverlay = new GameObject("@JobsnailCreateOverlay", typeof(RectTransform));
        m_CreateOverlay.transform.SetParent(m_SessionPcRoot != null ? m_SessionPcRoot : transform, false);
        var overlayRt = (RectTransform)m_CreateOverlay.transform;
        overlayRt.anchorMin = Vector2.zero;
        overlayRt.anchorMax = Vector2.one;
        overlayRt.offsetMin = Vector2.zero;
        overlayRt.offsetMax = Vector2.zero;

        var panel = JobsnailUiKit.Box("CreateModalPanel", m_CreateOverlay.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(430, 285), new Color(1f, 1f, 1f, 0.98f));
        panel.raycastTarget = true;

        var titleBar = JobsnailUiKit.Box("CreateModalTitleBar", m_CreateOverlay.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 127), new Vector2(430, 30), new Color(0.78f, 0.93f, 0.96f, 1f));
        titleBar.raycastTarget = false;
        MakeText(m_CreateOverlay.transform, "방 생성하기", 12, Color.black, new Vector2(-166, 127), new Vector2(120, 28), TextAnchor.MiddleLeft);
        MakeFixedButton(m_CreateOverlay.transform, "×", new Vector2(205, 127), new Vector2(28, 28), () =>
        {
            HideCustomCreateOverlay();
            SetActive(transform, "CreateSessionHUD", false);
        }, 18, new Color(0f, 0f, 0f, 0f));

        MakeText(m_CreateOverlay.transform, "방 이름", 18, Color.black, new Vector2(-115, 76), new Vector2(90, 28), TextAnchor.MiddleRight);
        MakeText(m_CreateOverlay.transform, "(최대 15자)", 10, Color.black, new Vector2(-115, 57), new Vector2(90, 18), TextAnchor.MiddleRight);
        m_CustomRoomNameInput = MakeLegacyInput(m_CreateOverlay.transform, "신체 건강한 달팽이 구합니다", new Vector2(82, 73), new Vector2(225, 28));

        MakeText(m_CreateOverlay.transform, "최대 인원", 18, Color.black, new Vector2(-115, 30), new Vector2(90, 28), TextAnchor.MiddleRight);
        MakeMaxPlayersDropdown(m_CreateOverlay.transform, new Vector2(35, 29));

        MakeText(m_CreateOverlay.transform, "방 종류", 18, Color.black, new Vector2(-115, -17), new Vector2(90, 28), TextAnchor.MiddleRight);
        MakeText(m_CreateOverlay.transform, "(택 1)", 10, Color.black, new Vector2(-115, -35), new Vector2(90, 18), TextAnchor.MiddleRight);
        MakeRoomTypeButtons(m_CreateOverlay.transform);

        m_PasswordLabel = MakeText(m_CreateOverlay.transform, "비밀번호", 18, Color.black, new Vector2(-115, -68), new Vector2(90, 28), TextAnchor.MiddleRight).gameObject;
        m_PasswordHint = MakeText(m_CreateOverlay.transform, "(8자 이상)", 10, Color.black, new Vector2(-115, -86), new Vector2(90, 18), TextAnchor.MiddleRight).gameObject;
        m_CustomPasswordInput = MakeLegacyInput(m_CreateOverlay.transform, "abcdefgh", new Vector2(82, -67), new Vector2(225, 28));
        m_CustomPasswordInput.contentType = InputField.ContentType.Standard;

        m_CustomCreateStatus = MakeText(m_CreateOverlay.transform, "", 12, new Color(0.65f, 0.16f, 0.12f, 1f), new Vector2(0, -103), new Vector2(330, 22), TextAnchor.MiddleCenter);

        MakeFixedButton(m_CreateOverlay.transform, "방 만들기", new Vector2(0, -122), new Vector2(120, 36), SubmitCustomCreateSession, 16);

        SetRoomType(false);
    }

    private void SubmitCustomCreateSession()
    {
        string roomName = m_CustomRoomNameInput != null ? m_CustomRoomNameInput.text.Trim() : "";
        bool isPrivate = m_IsPrivateRoom;
        string password = m_CustomPasswordInput != null ? m_CustomPasswordInput.text.Trim() : "";

        if (string.IsNullOrEmpty(roomName))
        {
            SetCreateStatus("방 이름을 입력해줘!");
            return;
        }

        if (isPrivate && password.Length < 8)
        {
            SetCreateStatus("비밀방 비밀번호는 8자 이상이어야 해.");
            return;
        }

        var createSession = FindFirstObjectByType<CreateSession>(FindObjectsInactive.Include);
        if (createSession == null)
        {
            SetCreateStatus("CreateSession 스크립트를 못 찾았어.");
            return;
        }

        createSession.CreateSessioinBtnOnClick -= OnCreateSessionCompleted;
        createSession.CreateSessioinBtnOnClick += OnCreateSessionCompleted;
        createSession.CreateSessionFailed -= OnCreateSessionFailed;
        createSession.CreateSessionFailed += OnCreateSessionFailed;

        SetCreateStatus("방 생성 중...");
        createSession.RequestCreateSession(roomName, isPrivate, isPrivate ? password : "", m_SelectedMaxPlayers);
    }

    private static void EnsureHostStarted()
    {
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.IsListening)
            return;

        NetworkManager.Singleton.StartHost();
    }

    private void SetCreateStatus(string message)
    {
        if (m_CustomCreateStatus != null)
            m_CustomCreateStatus.text = message;
    }

    private void HideOriginalHudGraphics(string hudName)
    {
        var hud = FindDeep(transform, hudName);
        if (hud == null)
            return;

        var group = hud.GetComponent<CanvasGroup>();
        if (group == null)
            group = hud.gameObject.AddComponent<CanvasGroup>();

        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;
    }

    private static Button MakeVisibleButton(Transform parent, string label, Vector2 anchorMin, Vector2 anchorMax, UnityAction onClick, int fontSize = 24)
    {
        var button = JobsnailUiKit.Button(label + "Button", parent, null, anchorMin, anchorMax, Vector2.zero, Vector2.zero, onClick);
        var image = button.GetComponent<Image>();
        if (image != null)
        {
            image.color = new Color(1f, 0.78f, 0.44f, 1f);
            image.raycastTarget = true;
        }
        MakeText(button.transform, label, fontSize, Color.black, Vector2.zero, Vector2.zero, TextAnchor.MiddleCenter);
        return button;
    }

    private static Button MakeFixedButton(Transform parent, string label, Vector2 anchored, Vector2 size, UnityAction onClick, int fontSize = 18, Color? color = null)
    {
        var go = new GameObject(label + "Button", typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchored;
        rt.sizeDelta = size;

        var image = go.GetComponent<Image>();
        image.color = color ?? new Color(1f, 0.78f, 0.44f, 1f);
        image.raycastTarget = true;

        var button = go.GetComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(PlayUIClick);
        if (onClick != null)
            button.onClick.AddListener(onClick);

        MakeText(go.transform, label, fontSize, Color.black, Vector2.zero, Vector2.zero, TextAnchor.MiddleCenter);
        return button;
    }

    private static InputField MakeLegacyInput(Transform parent, string value, Vector2 anchored, Vector2 size)
    {
        var go = new GameObject("InputField", typeof(RectTransform), typeof(Image), typeof(InputField));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchored;
        rt.sizeDelta = size;

        var image = go.GetComponent<Image>();
        image.color = new Color(0.86f, 0.86f, 0.86f, 1f);

        var text = MakeText(go.transform, value, 20, Color.black, Vector2.zero, Vector2.zero, TextAnchor.MiddleCenter);
        text.raycastTarget = false;

        var placeholder = MakeText(go.transform, "Enter text...", 20, new Color(0f, 0f, 0f, 0.35f), Vector2.zero, Vector2.zero, TextAnchor.MiddleCenter);
        placeholder.raycastTarget = false;

        var input = go.GetComponent<InputField>();
        input.textComponent = text;
        input.placeholder = placeholder;
        input.text = value;
        input.characterLimit = 20;
        return input;
    }

    private void MakeMaxPlayersDropdown(Transform parent, Vector2 anchored)
    {
        var button = MakeFixedButton(parent, "", anchored, new Vector2(90, 26), ToggleMaxPlayersOptions, 16, new Color(0.83f, 0.83f, 0.83f, 1f));
        m_MaxPlayersText = MakeText(button.transform, "1명 ▼", 16, Color.black, Vector2.zero, Vector2.zero, TextAnchor.MiddleCenter);

        m_MaxPlayersOptions = new GameObject("MaxPlayersOptions", typeof(RectTransform), typeof(Image));
        m_MaxPlayersOptions.transform.SetParent(parent, false);
        var rt = (RectTransform)m_MaxPlayersOptions.transform;
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchored + new Vector2(0, -58);
        rt.sizeDelta = new Vector2(90, 96);

        var bg = m_MaxPlayersOptions.GetComponent<Image>();
        bg.color = new Color(1f, 1f, 1f, 0.98f);
        bg.raycastTarget = true;

        for (int i = 1; i <= 4; i++)
        {
            int value = i;
            MakeFixedButton(m_MaxPlayersOptions.transform, $"{i}명", new Vector2(0, 36 - (i - 1) * 24), new Vector2(88, 24), () =>
            {
                m_SelectedMaxPlayers = value;
                if (m_MaxPlayersText != null)
                    m_MaxPlayersText.text = $"{value}명 ▼";
                if (m_MaxPlayersOptions != null)
                    m_MaxPlayersOptions.SetActive(false);
            }, 14, i == 1 ? new Color(1f, 0.80f, 0.46f, 1f) : new Color(1f, 1f, 1f, 1f));
        }

        m_MaxPlayersOptions.SetActive(false);
    }

    private void ToggleMaxPlayersOptions()
    {
        if (m_MaxPlayersOptions != null)
        {
            m_MaxPlayersOptions.SetActive(!m_MaxPlayersOptions.activeSelf);
            m_MaxPlayersOptions.transform.SetAsLastSibling();
        }
    }

    private void MakeRoomTypeButtons(Transform parent)
    {
        var privateButton = MakeFixedButton(parent, "비밀방", new Vector2(35, -17), new Vector2(100, 28), () => SetRoomType(true), 15, new Color(1f, 0.55f, 0.55f, 1f));
        m_PrivateRoomButtonImage = privateButton.GetComponent<Image>();

        var publicButton = MakeFixedButton(parent, "공개방", new Vector2(155, -17), new Vector2(90, 28), () => SetRoomType(false), 15, new Color(0.83f, 0.83f, 0.83f, 1f));
        m_PublicRoomButtonImage = publicButton.GetComponent<Image>();
    }

    private void SetRoomType(bool isPrivate)
    {
        m_IsPrivateRoom = isPrivate;

        if (m_PrivateRoomButtonImage != null)
            m_PrivateRoomButtonImage.color = isPrivate ? new Color(1f, 0.55f, 0.55f, 1f) : new Color(0.83f, 0.83f, 0.83f, 1f);

        if (m_PublicRoomButtonImage != null)
            m_PublicRoomButtonImage.color = isPrivate ? new Color(0.83f, 0.83f, 0.83f, 1f) : new Color(0.58f, 1f, 0.54f, 1f);

        bool showPassword = isPrivate;
        if (m_PasswordLabel != null)
            m_PasswordLabel.SetActive(showPassword);
        if (m_PasswordHint != null)
            m_PasswordHint.SetActive(showPassword);
        if (m_CustomPasswordInput != null)
            m_CustomPasswordInput.gameObject.SetActive(showPassword);
    }

    private static Toggle MakeToggle(Transform parent, string label, Vector2 anchored, bool initialValue)
    {
        var go = new GameObject(label + "Toggle", typeof(RectTransform), typeof(Toggle));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchored;
        rt.sizeDelta = new Vector2(150, 42);

        var bg = JobsnailUiKit.Box("Background", go.transform, new Vector2(0f, 0.25f), new Vector2(0.20f, 0.75f), Vector2.zero, Vector2.zero, new Color(1f, 0.55f, 0.55f, 1f));
        bg.raycastTarget = true;

        var check = JobsnailUiKit.Box("Checkmark", bg.transform, new Vector2(0.18f, 0.18f), new Vector2(0.82f, 0.82f), Vector2.zero, Vector2.zero, new Color(0.55f, 0.08f, 0.08f, 1f));
        check.raycastTarget = false;

        MakeText(go.transform, label, 20, Color.black, new Vector2(35, 0), new Vector2(110, 40), TextAnchor.MiddleLeft);

        var toggle = go.GetComponent<Toggle>();
        toggle.targetGraphic = bg;
        toggle.graphic = check;
        toggle.isOn = initialValue;
        return toggle;
    }

    private static Text MakeText(Transform parent, string text, int size, Color color, Vector2 anchored, Vector2 sizeDelta, TextAnchor anchor, Color? background = null)
    {
        var go = new GameObject(text, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = sizeDelta == Vector2.zero ? Vector2.zero : new Vector2(0.5f, 0.5f);
        rt.anchorMax = sizeDelta == Vector2.zero ? Vector2.one : new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchored;
        rt.sizeDelta = sizeDelta;

        if (background.HasValue)
        {
            var image = go.AddComponent<Image>();
            image.color = background.Value;
            image.raycastTarget = false;
        }

        var label = go.AddComponent<Text>();
        label.text = text;
        var font = GetDefaultFont();
        if (font != null)
            label.font = font;
        label.fontSize = size;
        label.color = color;
        label.alignment = anchor;
        label.raycastTarget = false;
        return label;
    }

    private static Font GetDefaultFont()
    {
        if (s_DefaultFont != null)
            return s_DefaultFont;

        s_DefaultFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (s_DefaultFont == null)
            s_DefaultFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
        if (s_DefaultFont == null)
            s_DefaultFont = Font.CreateDynamicFontFromOSFont("Apple SD Gothic Neo", 16);
        if (s_DefaultFont == null)
            s_DefaultFont = Font.CreateDynamicFontFromOSFont("Arial", 16);
        if (s_DefaultFont == null)
            s_DefaultFont = Font.CreateDynamicFontFromOSFont("Helvetica", 16);

        return s_DefaultFont;
    }

    private static void PlayUIClick()
    {
        if (SoundManager.Instance != null)
            SoundManager.Instance.PlaySFX(SFXType.UIClick);
    }

    private static Transform FindDeep(Transform root, string name)
    {
        if (root == null)
            return null;

        if (root.name == name)
            return root;

        for (int i = 0; i < root.childCount; i++)
        {
            var found = FindDeep(root.GetChild(i), name);
            if (found != null)
                return found;
        }

        return null;
    }
}
