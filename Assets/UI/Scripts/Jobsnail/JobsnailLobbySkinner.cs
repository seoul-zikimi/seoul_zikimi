using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>로비 씬의 네트워크 세션 컨트롤러.
///
/// UI는 전부 Resources의 Jobsnail 프리팹(JobsnailSessionOverlay / JobsnailCreateOverlay /
/// JobsnailLobbyRoomOverlay)과 거기에 붙은 <see cref="JobsnailLobbyPrefabView"/>가 담당한다.
/// 이 클래스는 프리팹을 띄우고, UGS 세션(목록 조회·생성·입장·파괴)과 Netcode 상태만 다룬 뒤
/// 표시에 필요한 값을 View에 넘긴다. 위젯을 코드로 만들지 않는다.</summary>
public sealed class JobsnailLobbySkinner : MonoBehaviour
{
    internal static JobsnailLobbySkinner ActiveInstance { get; private set; }

    private const string SessionOverlayPrefabPath = "UI/Jobsnail/Prefabs/JobsnailSessionOverlay";
    private const string CreateOverlayPrefabPath = "UI/Jobsnail/Prefabs/JobsnailCreateOverlay";
    private const string LobbyRoomOverlayPrefabPath = "UI/Jobsnail/Prefabs/JobsnailLobbyRoomOverlay";

    private const string DefaultRoomName = "신체 건강한 달팽이 구합니다";
    private const string DefaultPassword = "abcdefgh";

    private static readonly string[] kModeLabels = { "모드 · 타임어택", "모드 · 2vs2 대결", "모드 · 자유 건축" };

    // 구 씬에 남아 있는 레거시 HUD들 — 프리팹 UI와 겹치므로 항상 꺼둔다.
    private static readonly string[] kLegacyHudNames =
    {
        "StartHUD", "CreateSessionHUD", "SessionListHUD", "JoinCodeHUD", "JoinByCodeHUD", "LobbyRoomHUD"
    };

    private JobsnailLobbyPrefabView m_SessionView;
    private JobsnailLobbyPrefabView m_CreateView;
    private JobsnailLobbyPrefabView m_LobbyRoomView;

    private readonly List<ISessionInfo> m_Sessions = new();
    private readonly List<JobsnailSessionCardData> m_SessionCards = new();
    private string m_PendingJoinSessionId;

    private CreateSession m_CreateSession;
    private ISession m_ActiveSession;
    private string m_ActiveSessionHostId;

    private int m_SelectedMaxPlayers = 1;
    private bool m_IsPrivateRoom;
    private bool m_IsStartingGame;
    private int m_CurrentRoomMaxPlayers = 1;
    private string m_CurrentRoomName = DefaultRoomName;

    // ────────────────────────── 수명 주기 ──────────────────────────

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

    private void Awake()
    {
        ActiveInstance = this;
    }

    private void Start()
    {
        int savedMaxPlayers = LobbyRoomNet.RequiredTotalPlayers;
        if (savedMaxPlayers > 1)
            m_CurrentRoomMaxPlayers = Mathf.Clamp(savedMaxPlayers, 1, 4);

        EnsureOverlayCanvas();
        HideLegacyLobbyHuds();

        if (SoundManager.Instance != null)
            SoundManager.Instance.SetPhase(global::GamePhase.Lobby);

        m_CreateSession = FindFirstObjectByType<CreateSession>(FindObjectsInactive.Include);
        SubscribeCreateSession(m_CreateSession);

        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
            ShowSessionList();
        else
            RefreshOverlayState();
    }

    private void OnDestroy()
    {
        if (ActiveInstance == this)
            ActiveInstance = null;

        UnsubscribeCreateSession(m_CreateSession);
        ClearActiveSession();
    }

    private void Update()
    {
        RefreshOverlayState();
    }

    /// <summary>프리팹 오버레이가 정상 축척으로 뜨도록 캔버스만 보장한다(위젯 생성 아님).</summary>
    private void EnsureOverlayCanvas()
    {
        var canvas = GetComponent<Canvas>();
        if (canvas == null)
            return;

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

    // ────────────────────────── 화면 전환 ──────────────────────────

    private void RefreshOverlayState()
    {
        bool isConnected = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

        if (!isConnected)
        {
            SetViewVisible(m_LobbyRoomView, false);
            return;
        }

        SetViewVisible(m_CreateView, false);
        SetViewVisible(m_SessionView, false);
        HideLegacyLobbyHuds();

        if (m_IsStartingGame)
        {
            SetViewVisible(m_LobbyRoomView, false);
            return;
        }

        ShowLobbyRoom();
    }

    private void ShowCreateSession()
    {
        Debug.Log("[JobsnailLobbySkinner] 방 생성 화면 열기");
        HideLegacyLobbyHuds();
        ShowSessionOverlay();

        var view = EnsureCreateView();
        if (view == null)
            return;

        view.SetVisible(true);
    }

    private void ShowSessionList()
    {
        Debug.Log("[JobsnailLobbySkinner] 세션 목록 화면 열기");
        HideLegacyLobbyHuds();
        SetViewVisible(m_CreateView, false);
        ShowSessionOverlay();
        _ = RefreshSessionListAsync();
    }

    private void ShowSessionOverlay()
    {
        var view = EnsureSessionView();
        if (view != null)
            view.SetVisible(true);
    }

    private void ShowLobbyRoom()
    {
        var view = EnsureLobbyRoomView();
        if (view == null)
            return;

        view.SetVisible(true);
        UpdateLobbyRoomView();
    }

    private static void SetViewVisible(JobsnailLobbyPrefabView view, bool visible)
    {
        if (view != null)
            view.SetVisible(visible);
    }

    // ────────────────────────── 프리팹 로드 ──────────────────────────

    private JobsnailLobbyPrefabView EnsureSessionView()
    {
        if (m_SessionView != null)
            return m_SessionView;

        m_SessionView = LoadOverlay(SessionOverlayPrefabPath, transform, "@JobsnailSessionOverlay",
            JobsnailLobbyPrefabView.OverlayKind.SessionList);
        return m_SessionView;
    }

    private JobsnailLobbyPrefabView EnsureCreateView()
    {
        if (m_CreateView != null)
            return m_CreateView;

        // 방 생성 모달은 세션 목록 PC 화면 안쪽에 얹힌다.
        var sessionView = EnsureSessionView();
        Transform parent = sessionView != null && sessionView.PcRoot != null ? sessionView.PcRoot : transform;

        m_CreateView = LoadOverlay(CreateOverlayPrefabPath, parent, "@JobsnailCreateOverlay",
            JobsnailLobbyPrefabView.OverlayKind.CreateRoom);

        if (m_CreateView != null)
            m_CreateView.InitCreateForm(DefaultRoomName, DefaultPassword, m_SelectedMaxPlayers, m_IsPrivateRoom);

        return m_CreateView;
    }

    private JobsnailLobbyPrefabView EnsureLobbyRoomView()
    {
        if (m_LobbyRoomView != null)
            return m_LobbyRoomView;

        m_LobbyRoomView = LoadOverlay(LobbyRoomOverlayPrefabPath, transform, "@JobsnailLobbyRoomOverlay",
            JobsnailLobbyPrefabView.OverlayKind.LobbyRoom);
        return m_LobbyRoomView;
    }

    private JobsnailLobbyPrefabView LoadOverlay(string resourcesPath, Transform parent, string instanceName,
        JobsnailLobbyPrefabView.OverlayKind expectedKind)
    {
        var prefab = Resources.Load<GameObject>(resourcesPath);
        if (prefab == null)
        {
            Debug.LogError($"[JobsnailLobbySkinner] UI 프리팹을 찾지 못했습니다: Resources/{resourcesPath}");
            return null;
        }

        var instance = Instantiate(prefab, parent, false);
        instance.name = instanceName;

        var view = instance.GetComponent<JobsnailLobbyPrefabView>();
        if (view == null)
        {
            Debug.LogError($"[JobsnailLobbySkinner] {resourcesPath} 루트에 JobsnailLobbyPrefabView가 없습니다.");
            Destroy(instance);
            return null;
        }

        if (view.Kind != expectedKind)
            Debug.LogWarning($"[JobsnailLobbySkinner] {resourcesPath}의 Kind가 {view.Kind}입니다. 기대값: {expectedKind}");

        view.Bind(this);
        return view;
    }

    // ────────────────────────── 세션 목록 조회 ──────────────────────────

    private async Task RefreshSessionListAsync()
    {
        var view = EnsureSessionView();
        if (view == null)
            return;

        view.ClearSessionCards();
        view.SetSessionStatus("방 목록을 불러오는 중...");

        try
        {
            await EnsureServicesReadyAsync();

            var result = await MultiplayerService.Instance.QuerySessionsAsync(new QuerySessionsOptions
            {
                SortOptions = new List<SortOption> { new(SortOrder.Descending, SortField.Name) }
            });

            m_Sessions.Clear();
            foreach (var session in result.Sessions)
                m_Sessions.Add(session);

            PublishSessionCards();
        }
        catch (System.Exception ex)
        {
            view.SetSessionStatus($"방 목록 불러오기 실패: {ex.Message}");
        }
    }

    private void PublishSessionCards()
    {
        var view = m_SessionView;
        if (view == null)
            return;

        if (m_Sessions.Count == 0)
        {
            view.ClearSessionCards();
            view.SetSessionStatus("현재 열린 방이 없어요. 방 만들기를 눌러 새 방을 만들어줘!");
            return;
        }

        m_SessionCards.Clear();
        foreach (var session in m_Sessions)
        {
            m_SessionCards.Add(new JobsnailSessionCardData
            {
                Name = session.Name,
                HasPassword = HasPassword(session),
                Joined = Mathf.Max(0, session.MaxPlayers - session.AvailableSlots),
                MaxPlayers = session.MaxPlayers
            });
        }

        view.SetSessionStatus(string.Empty);
        view.SetSessionCards(m_SessionCards, OnSessionCardSelected);
    }

    private void OnSessionCardSelected(int index)
    {
        PlayUIClick();

        if (index < 0 || index >= m_Sessions.Count)
            return;

        var session = m_Sessions[index];
        m_CurrentRoomMaxPlayers = Mathf.Clamp(session.MaxPlayers, 1, 4);
        m_CurrentRoomName = string.IsNullOrWhiteSpace(session.Name) ? "이름 없는 방" : session.Name;

        if (HasPassword(session))
        {
            m_PendingJoinSessionId = session.Id;
            if (m_SessionView != null)
                m_SessionView.ShowJoinPasswordPrompt();
            return;
        }

        _ = JoinSessionAsync(session.Id, null);
    }

    private static bool HasPassword(ISessionInfo session)
    {
        return session.Properties != null && session.Properties.ContainsKey("PasswordHash");
    }

    // ────────────────────────── 세션 입장 ──────────────────────────

    private async Task JoinSessionAsync(string sessionId, string password)
    {
        if (string.IsNullOrEmpty(sessionId))
            return;

        SetSessionStatus("방 입장 중...");

        try
        {
            await EnsureServicesReadyAsync();
            PrepareNetworkTransport();

            var sessionBrowser = FindFirstObjectByType<SessionBrowser>(FindObjectsInactive.Include);
            string sessionType = sessionBrowser != null && sessionBrowser.SessionSettings != null
                ? sessionBrowser.SessionSettings.sessionType
                : "default-session";

            var session = await MultiplayerService.Instance.JoinSessionByIdAsync(sessionId, new JoinSessionOptions
            {
                Type = sessionType,
                Password = string.IsNullOrEmpty(password) ? null : password
            });

            SetActiveSession(session);
            if (session != null)
            {
                m_CurrentRoomName = string.IsNullOrWhiteSpace(session.Name) ? m_CurrentRoomName : session.Name;
                m_CurrentRoomMaxPlayers = Mathf.Clamp(session.MaxPlayers, 1, 4);
                LobbyRoomNet.RequiredTotalPlayers = m_CurrentRoomMaxPlayers;
            }

            if (IsLocalSessionHost())
                EnsureHostStarted();
            else if (NetworkManager.Singleton != null && !NetworkManager.Singleton.IsListening)
                NetworkManager.Singleton.StartClient();

            if (m_SessionView != null)
                m_SessionView.HideJoinPasswordPrompt();

            SetViewVisible(m_CreateView, false);
            SetViewVisible(m_SessionView, false);
            ShowLobbyRoom();
        }
        catch (System.Exception ex)
        {
            SetSessionStatus($"방 입장 실패: {ex.Message}");
        }
    }

    // ────────────────────────── 방 생성 ──────────────────────────

    private void SubmitCreateSession()
    {
        var view = m_CreateView;
        if (view == null)
            return;

        string roomName = view.RoomNameText;
        string password = view.PasswordText;

        if (string.IsNullOrEmpty(roomName))
        {
            view.SetCreateStatus("방 이름을 입력해줘!");
            return;
        }

        if (m_IsPrivateRoom && string.IsNullOrWhiteSpace(password))
        {
            view.SetCreateStatus("비밀방 비밀번호를 입력해줘.");
            return;
        }

        var createSession = m_CreateSession != null
            ? m_CreateSession
            : FindFirstObjectByType<CreateSession>(FindObjectsInactive.Include);

        if (createSession == null)
        {
            view.SetCreateStatus("CreateSession 스크립트를 못 찾았어.");
            return;
        }

        m_CreateSession = createSession;
        SubscribeCreateSession(createSession);

        view.SetCreateStatus("방 생성 중...");
        m_CurrentRoomName = roomName;
        createSession.RequestCreateSession(roomName, m_IsPrivateRoom, m_IsPrivateRoom ? password : "", m_SelectedMaxPlayers);
    }

    private void SubscribeCreateSession(CreateSession createSession)
    {
        if (createSession == null)
            return;

        createSession.CreateSessioinBtnOnClick -= OnCreateSessionCompleted;
        createSession.CreateSessioinBtnOnClick += OnCreateSessionCompleted;
        createSession.CreateSessionFailed -= OnCreateSessionFailed;
        createSession.CreateSessionFailed += OnCreateSessionFailed;
        createSession.SessionCreated -= OnSessionCreated;
        createSession.SessionCreated += OnSessionCreated;
    }

    private void UnsubscribeCreateSession(CreateSession createSession)
    {
        if (createSession == null)
            return;

        createSession.CreateSessioinBtnOnClick -= OnCreateSessionCompleted;
        createSession.CreateSessionFailed -= OnCreateSessionFailed;
        createSession.SessionCreated -= OnSessionCreated;
    }

    private void OnCreateSessionCompleted(bool active)
    {
        if (!active)
            return;

        SetViewVisible(m_CreateView, false);
        SetViewVisible(m_SessionView, false);
        HideLegacyLobbyHuds();

        m_CurrentRoomMaxPlayers = Mathf.Clamp(m_SelectedMaxPlayers, 1, 4);
        if (m_CreateView != null && !string.IsNullOrWhiteSpace(m_CreateView.RoomNameText))
            m_CurrentRoomName = m_CreateView.RoomNameText;
        LobbyRoomNet.RequiredTotalPlayers = m_CurrentRoomMaxPlayers;
        EnsureHostStarted();

        if (m_SelectedMaxPlayers <= 1)
        {
            m_IsStartingGame = true;
            SetViewVisible(m_LobbyRoomView, false);
            StartCoroutine(StartSinglePlayerGameAfterHostReady());
            return;
        }

        ShowLobbyRoom();
    }

    private void OnCreateSessionFailed(string message)
    {
        if (m_CreateView != null)
            m_CreateView.SetCreateStatus(string.IsNullOrEmpty(message) ? "방 생성에 실패했어." : message);
    }

    private void OnSessionCreated(ISession session)
    {
        SetActiveSession(session);
    }

    // ────────────────────────── 활성 세션 추적 / 파괴 ──────────────────────────

    private void SetActiveSession(ISession session)
    {
        if (ReferenceEquals(m_ActiveSession, session))
            return;

        ClearActiveSession();
        m_ActiveSession = session;
        JobsnailSessionManager.Instance.RegisterActiveSession(m_ActiveSession);
        m_ActiveSessionHostId = session?.Host;

        if (m_ActiveSession == null)
            return;

        m_ActiveSession.SessionHostChanged += OnActiveSessionHostChanged;
        m_ActiveSession.Changed += OnActiveSessionChanged;
    }

    private void ClearActiveSession()
    {
        if (m_ActiveSession != null)
        {
            m_ActiveSession.SessionHostChanged -= OnActiveSessionHostChanged;
            m_ActiveSession.Changed -= OnActiveSessionChanged;
        }

        m_ActiveSession = null;
        m_ActiveSessionHostId = null;
    }

    private void OnActiveSessionChanged()
    {
        if (m_ActiveSession == null)
            return;

        m_ActiveSessionHostId = m_ActiveSession.Host;
        if (!string.IsNullOrWhiteSpace(m_ActiveSession.Name))
            m_CurrentRoomName = m_ActiveSession.Name;

        UpdateLobbyRoomView();
        JobsnailSessionManager.Instance.RegisterActiveSession(m_ActiveSession);
    }

    private async void OnActiveSessionHostChanged(string newHostId)
    {
        m_ActiveSessionHostId = newHostId;
        Debug.LogWarning($"[JobsnailLobbySkinner] 세션 방장 변경 감지됨 (새 방장: {newHostId}) ➡️ 세션을 종료합니다.");

        await JobsnailSessionManager.Instance.EndSessionBecauseHostLeftAsync($"JobsnailLobbySkinner 방장 변경 감지: {newHostId}");
    }

    private bool IsLocalSessionHost()
    {
        if (m_ActiveSession != null)
        {
            if (m_ActiveSession.IsHost)
                return true;

            string localPlayerId = null;
            if (UnityServices.State == ServicesInitializationState.Initialized && AuthenticationService.Instance.IsSignedIn)
                localPlayerId = AuthenticationService.Instance.PlayerId;

            string hostId = !string.IsNullOrEmpty(m_ActiveSessionHostId) ? m_ActiveSessionHostId : m_ActiveSession.Host;
            if (!string.IsNullOrEmpty(localPlayerId) && !string.IsNullOrEmpty(hostId))
                return localPlayerId == hostId;
        }

        return NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;
    }

    // ────────────────────────── 대기방 상태 → View ──────────────────────────

    private void UpdateLobbyRoomView()
    {
        var view = m_LobbyRoomView;
        if (view == null || !view.IsVisible)
            return;

        var readyNet = FindFirstObjectByType<LobbyRoomNet>(FindObjectsInactive.Include);
        bool isHost = IsLocalSessionHost();
        bool isNetworkServer = NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
        bool hasReadyNet = readyNet != null && readyNet.IsSpawned;

        int maxPlayers = hasReadyNet && readyNet.MaxPlayers > 1
            ? Mathf.Clamp(readyNet.MaxPlayers, 1, 4)
            : Mathf.Clamp(m_CurrentRoomMaxPlayers, 1, 4);
        int expectedReadyCount = Mathf.Max(0, maxPlayers - 1);
        int joinedCount = hasReadyNet ? Mathf.Clamp(readyNet.ConnectedCount, 1, maxPlayers) : 1;
        int targetReadyCount = Mathf.Max(expectedReadyCount, hasReadyNet ? readyNet.TargetReadyCount : 0);
        int readyCount = hasReadyNet ? readyNet.ReadyCount : 0;
        bool roomIsFullEnough = joinedCount >= maxPlayers;
        bool singlePlayerRoom = maxPlayers <= 1;
        bool allReady = hasReadyNet && roomIsFullEnough && (singlePlayerRoom || readyNet.IsAllReady);

        var catalog = GridSystem.MapCatalog.Instance;
        int mapIndex = hasReadyNet ? readyNet.SelectedMap : 0;
        var mapDef = catalog != null ? catalog.Get(mapIndex) : null;
        int mapCount = catalog != null ? catalog.Count : 0;
        int mode = Mathf.Clamp(GridSystem.GameLoopManager.HostSelectedMode, 0, 2);

        var state = new JobsnailLobbyRoomState
        {
            RoomName = m_CurrentRoomName,
            RoomIsFull = roomIsFullEnough,
            ShowStartButton = isHost,
            StartInteractable = hasReadyNet && allReady && isNetworkServer,
            ShowReadyButton = !isHost,
            ReadyInteractable = hasReadyNet,
            IsLocallyReady = hasReadyNet && readyNet.IsLocallyReady,
            AllReady = allReady,
            StartHint = BuildStartHint(hasReadyNet, readyNet, isHost, isNetworkServer, singlePlayerRoom, allReady, joinedCount, maxPlayers),
            ReadyStatus = BuildReadyStatus(hasReadyNet, singlePlayerRoom, targetReadyCount, joinedCount, maxPlayers, readyCount, expectedReadyCount),
            JoinedCount = joinedCount,
            ReadyCount = readyCount,
            MaxPlayers = maxPlayers,
            ShowModeButton = isHost,
            ModeLabel = kModeLabels[mode],
            MapName = mapDef != null ? mapDef.DisplayName : string.Empty,
            MapThumbnail = mapDef != null ? mapDef.Thumbnail : null,
            ShowMapArrows = isHost && isNetworkServer && mapCount > 1
        };

        view.ApplyLobbyRoomState(state);
    }

    private static string BuildStartHint(bool hasReadyNet, LobbyRoomNet readyNet, bool isHost, bool isNetworkServer,
        bool singlePlayerRoom, bool allReady, int joinedCount, int maxPlayers)
    {
        if (!hasReadyNet)
            return "레디 시스템 연결 중...";

        if (isHost && !isNetworkServer)
            return "방장 권한 복구 중...";

        if (isHost)
        {
            if (singlePlayerRoom)
                return "혼자 플레이 시작 가능!";
            return allReady ? "모든 팀원 준비 완료!" : $"팀원을 기다리는 중 ({joinedCount}/{maxPlayers})";
        }

        return readyNet.IsLocallyReady ? "방장이 시작하기를 기다리는 중" : "준비 완료를 눌러줘";
    }

    private static string BuildReadyStatus(bool hasReadyNet, bool singlePlayerRoom, int targetReadyCount,
        int joinedCount, int maxPlayers, int readyCount, int expectedReadyCount)
    {
        if (!hasReadyNet)
            return "준비 상태를 불러오는 중...";

        if (singlePlayerRoom || targetReadyCount <= 0)
            return "입장 1/1 · 시작 가능";

        return $"입장 {joinedCount}/{maxPlayers} · 준비 {readyCount}/{expectedReadyCount}";
    }

    // ────────────────────────── 준비 / 게임 시작 ──────────────────────────

    private void ToggleReadyState()
    {
        var readyNet = FindFirstObjectByType<LobbyRoomNet>(FindObjectsInactive.Include);
        if (readyNet == null || !readyNet.IsSpawned)
            return;

        readyNet.ToggleReadyState();
        UpdateLobbyRoomView();
    }

    private void TryStartLobbyGame()
    {
        var readyNet = FindFirstObjectByType<LobbyRoomNet>(FindObjectsInactive.Include);

        if (readyNet != null && readyNet.IsSpawned)
        {
            readyNet.OnStartGameButtonClicked();
            return;
        }

        if (m_LobbyRoomView != null)
            m_LobbyRoomView.SetLobbyStartHint("레디 시스템 연결 후 시작할 수 있어요.");
    }

    private IEnumerator StartSinglePlayerGameAfterHostReady()
    {
        SetViewVisible(m_LobbyRoomView, false);

        float timeout = Time.unscaledTime + 6f;
        while (Time.unscaledTime < timeout)
        {
            EnsureHostStarted();

            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && NetworkManager.Singleton.IsServer)
                break;

            yield return null;
        }

        yield return null;
        yield return null;

        StartGameFromLobby();
    }

    private static void StartGameFromLobby()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
            return;

        PlayUIClick();

        if (NetworkManager.Singleton.SceneManager != null && NetworkManager.Singleton.NetworkConfig.EnableSceneManagement)
        {
            NetworkManager.Singleton.SceneManager.LoadScene(SceneNames.GameScene, LoadSceneMode.Single);
            return;
        }

        SceneManager.LoadScene(SceneNames.GameScene, LoadSceneMode.Single);
    }

    // ────────────────────────── View → 컨트롤러 (프리팹 버튼 바인딩 대상) ──────────────────────────

    internal void PrefabShowCreateSession()
    {
        PlayUIClick();
        ShowCreateSession();
    }

    internal void PrefabRefreshSessionList()
    {
        PlayUIClick();
        _ = RefreshSessionListAsync();
    }

    internal void PrefabBackToMain()
    {
        PlayUIClick();
        SetViewVisible(m_SessionView, false);
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.Shutdown();
        SceneManager.LoadScene(SceneNames.BootstrapScene);
    }

    internal void PrefabConfirmJoinPassword()
    {
        PlayUIClick();
        string password = m_SessionView != null ? m_SessionView.JoinPasswordText : string.Empty;
        _ = JoinSessionAsync(m_PendingJoinSessionId, password);
    }

    internal void PrefabCancelJoinPassword()
    {
        PlayUIClick();
        m_PendingJoinSessionId = null;
        if (m_SessionView != null)
            m_SessionView.HideJoinPasswordPrompt();
    }

    internal void PrefabCloseCreateOverlay()
    {
        PlayUIClick();
        SetViewVisible(m_CreateView, false);
    }

    internal void PrefabSubmitCreateSession()
    {
        PlayUIClick();
        SubmitCreateSession();
    }

    internal void PrefabToggleMaxPlayersOptions()
    {
        PlayUIClick();
        if (m_CreateView != null)
            m_CreateView.ToggleMaxPlayersOptions();
    }

    internal void PrefabSelectMaxPlayers(int value)
    {
        PlayUIClick();
        m_SelectedMaxPlayers = Mathf.Clamp(value, 1, 4);
        if (m_CreateView != null)
        {
            m_CreateView.SetMaxPlayersLabel(m_SelectedMaxPlayers);
            m_CreateView.SetMaxPlayersOptionsOpen(false);
        }
    }

    internal void PrefabSetRoomType(bool isPrivate)
    {
        PlayUIClick();
        m_IsPrivateRoom = isPrivate;
        if (m_CreateView != null)
            m_CreateView.ApplyRoomType(isPrivate);
    }

    internal async void PrefabLeaveLobbyRoom()
    {
        PlayUIClick();
        await JobsnailSessionManager.Instance.LeaveLobbyRoomSecurelyAsync();
    }

    internal void PrefabStartLobbyGame()
    {
        PlayUIClick();
        TryStartLobbyGame();
    }

    internal void PrefabToggleReadyState()
    {
        PlayUIClick();
        ToggleReadyState();
    }

    internal void PrefabCycleGameMode()
    {
        PlayUIClick();
        GridSystem.GameLoopManager.HostSelectedMode = (GridSystem.GameLoopManager.HostSelectedMode + 1) % 3;
        UpdateLobbyRoomView();
    }

    internal void PrefabStepMap(int direction)
    {
        PlayUIClick();
        var readyNet = FindFirstObjectByType<LobbyRoomNet>(FindObjectsInactive.Include);
        if (readyNet != null)
            readyNet.HostSelectMap(readyNet.SelectedMap + direction);
    }

    // ────────────────────────── UGS / Netcode 유틸 ──────────────────────────

    private static void EnsureHostStarted()
    {
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.IsListening)
            return;

        PrepareNetworkTransport();
        NetworkManager.Singleton.StartHost();
    }

    private static async Task EnsureServicesReadyAsync()
    {
        if (UnityServices.State == ServicesInitializationState.Uninitialized)
            await UnityServices.InitializeAsync();

        while (UnityServices.State == ServicesInitializationState.Initializing)
            await Task.Yield();

        if (UnityServices.State != ServicesInitializationState.Initialized)
            await UnityServices.InitializeAsync();

        if (!AuthenticationService.Instance.IsSignedIn)
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
    }

    private static void PrepareNetworkTransport()
    {
        if (NetworkManager.Singleton == null)
            return;

        var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        if (transport != null)
            NetworkManager.Singleton.NetworkConfig.NetworkTransport = transport;
    }

    private void SetSessionStatus(string message)
    {
        if (m_SessionView != null)
            m_SessionView.SetSessionStatus(message);
    }

    /// <summary>씬에 남아 있는 구 로비 HUD들을 끈다(프리팹 UI와 중복 표시 방지).</summary>
    private void HideLegacyLobbyHuds()
    {
        foreach (string hudName in kLegacyHudNames)
        {
            var hud = FindDeep(transform, hudName);
            if (hud != null)
                hud.gameObject.SetActive(false);
        }
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
