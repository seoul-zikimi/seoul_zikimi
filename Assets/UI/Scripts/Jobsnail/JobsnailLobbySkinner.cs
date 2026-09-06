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
    // 세션 목록 카드에 뿌릴 짧은 모드 이름(kModeLabels와 인덱스 동일).
    private static readonly string[] kModeShortLabels = { "타임어택", "2vs2 대결", "자유 건축" };

    // 로비 우측 패널에 표시할 4종 모드 라벨(LobbyRoomNet.SelectedLobbyMode 인덱스와 동일).
    private static readonly string[] kLobbyModeLabels =
        { "타임어택", "2VS2 대전(아이템)", "2VS2 대전(타임어택)", "자유건축" };

    // 세션 프로퍼티 키 — 목록 카드/필터가 읽는 값.
    private const string kMapPropertyKey = "Map";
    private const string kModePropertyKey = "Mode";
    private const string kStatePropertyKey = "State";
    private const string kWeatherPropertyKey = "Weather";
    private const string kStateLobby = "Lobby";
    private const string kStateInGame = "InGame";

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

    private bool m_IsPrivateRoom;
    private bool m_IsStartingGame;
    private int m_CurrentRoomMaxPlayers = LobbyRoomNet.RoomCapacity;
    private string m_CurrentRoomName = DefaultRoomName;

    // 대기방 슬롯 표시용 재사용 버퍼(매 프레임 갱신, GC 방지).
    private readonly JobsnailLobbySlot[] m_SlotBuffer = new JobsnailLobbySlot[4];

    // 방 생성 맵 드롭다운 순번 → 카탈로그 인덱스(공터 제외 매핑).
    private readonly List<int> m_CreateMapOptionIndices = new();

    // 기록 텍스트 캐시(파일 읽기라 입력 바뀔 때만 재계산).
    private int m_RecordCacheMode = -1;
    private int m_RecordCacheMap = -1;
    private int m_RecordCachePlayers = -1;
    private string m_RecordCacheText = string.Empty;

    // 방 생성 화면에서 고른 맵/모드/날씨(생성 시 초기값. 맵·모드는 로비에서 계속 변경 가능).
    private int m_SelectedMap;
    private int m_SelectedMode;              // 0=타임어택, 1=2vs2, 2=자유 (kModeLabels 인덱스)
    private bool m_SelectedWeatherEnabled = true;

    // 호스트가 세션에 마지막으로 써넣은 메타데이터(맵/모드/상태). 값이 바뀔 때만 저장한다.
    private int m_LastSavedMapIndex = int.MinValue;
    private int m_LastSavedModeIndex = int.MinValue;
    private string m_LastSavedState;
    private bool m_IsSavingSessionMetadata;

    // ────────────────────────── 수명 주기 ──────────────────────────

    // LEGACY UI 비활성화(2026-08-21): Lobby 씬은 정적으로 배치된 UI_NEW_Canvas와
    // 기능별 UI_NEW 컨트롤러를 사용한다. 이전 스키너 구현은 비교/복구를 위해 보존한다.
#if false
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
#endif

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
        {
            m_CreateView.InitCreateForm(DefaultRoomName, DefaultPassword, m_IsPrivateRoom);
            InitCreateFormSelectors();
        }

        return m_CreateView;
    }

    /// <summary>방 생성 화면의 맵/모드 드롭다운과 날씨 토글을 현재 선택값으로 채운다.</summary>
    private void InitCreateFormSelectors()
    {
        var view = m_CreateView;
        if (view == null)
            return;

        // 기본값을 현재 static 선택값에 맞춘다.
        m_SelectedMode = Mathf.Clamp(GridSystem.GameLoopManager.HostSelectedMode, 0, kModeLabels.Length - 1);
        m_SelectedWeatherEnabled = GridSystem.GameLoopManager.HostWeatherEnabled;

        var catalog = GridSystem.MapCatalog.Instance;
        int mapCount = catalog != null ? catalog.Count : 0;
        m_SelectedMap = mapCount > 0 ? Mathf.Clamp(GridSystem.GameLoopManager.HostSelectedMap, 0, mapCount - 1) : 0;

        // 공터(2vs2 경기장)는 선택지에서 제외 — 대전 모드가 배경으로 자동 사용. 옵션 순번 → 카탈로그 인덱스 매핑 유지.
        m_CreateMapOptionIndices.Clear();
        var mapLabels = new List<string>(mapCount);
        for (int i = 0; i < mapCount; i++)
        {
            var def = catalog.Get(i);
            if (def != null && def.IsVersusArena) continue;
            m_CreateMapOptionIndices.Add(i);
            mapLabels.Add(def != null ? def.DisplayName : $"맵 {i + 1}");
        }
        if (mapCount > 0 && !m_CreateMapOptionIndices.Contains(m_SelectedMap) && m_CreateMapOptionIndices.Count > 0)
            m_SelectedMap = m_CreateMapOptionIndices[0];   // 저장된 선택이 공터였다면 첫 일반 맵으로

        view.BuildMapOptions(mapLabels, optionIndex =>
        {
            if (optionIndex >= 0 && optionIndex < m_CreateMapOptionIndices.Count)
                OnCreateMapSelected(m_CreateMapOptionIndices[optionIndex]);
        });
        var selectedDef = mapCount > 0 ? catalog.Get(m_SelectedMap) : null;
        view.SetMapLabel(selectedDef != null ? selectedDef.DisplayName : "맵 없음");

        view.BuildModeOptions(new List<string>(kModeLabels), OnCreateModeSelected);
        view.SetModeLabel(kModeLabels[m_SelectedMode]);

        view.ApplyWeather(m_SelectedWeatherEnabled);
    }

    private void OnCreateMapSelected(int index)
    {
        PlayUIClick();
        var catalog = GridSystem.MapCatalog.Instance;
        int mapCount = catalog != null ? catalog.Count : 0;
        if (mapCount <= 0)
            return;

        m_SelectedMap = Mathf.Clamp(index, 0, mapCount - 1);
        var def = catalog.Get(m_SelectedMap);
        if (m_CreateView != null)
        {
            m_CreateView.SetMapLabel(def != null ? def.DisplayName : $"맵 {m_SelectedMap + 1}");
            m_CreateView.SetMapOptionsOpen(false);
        }
    }

    private void OnCreateModeSelected(int index)
    {
        PlayUIClick();
        m_SelectedMode = Mathf.Clamp(index, 0, kModeLabels.Length - 1);
        if (m_CreateView != null)
        {
            m_CreateView.SetModeLabel(kModeLabels[m_SelectedMode]);
            m_CreateView.SetModeOptionsOpen(false);
        }
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
                // 기획: 방이 생긴 순서대로(오름차순) 정렬.
                SortOptions = new List<SortOption> { new(SortOrder.Ascending, SortField.CreationTime) }
            });

            m_Sessions.Clear();
            foreach (var session in result.Sessions)
                m_Sessions.Add(session);

            // 게임 플레이 중인 방은 목록에서 제외한다(기획서: "게임 플레이중인 방 제외").
            m_Sessions.RemoveAll(IsSessionInPlay);

            // 세션이 생긴 순서대로(오름차순) 정렬 — 목록 첫 칸(왼쪽 위)이 가장 먼저 만들어진 방.
            m_Sessions.Sort((a, b) => a.Created.CompareTo(b.Created));

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
                MaxPlayers = session.MaxPlayers,
                MapName = MapNameFromSession(session),
                ModeLabel = ModeLabelFromSession(session)
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

    /// <summary>세션 프로퍼티에서 문자열 값을 읽는다(맵·모드·상태 등). 값이 없으면 빈 문자열.
    /// 목록 화면에서는 방 생성/변경 시 세션에 저장된 값만 볼 수 있으므로,
    /// 저장돼 있지 않은 경우 카드에서 해당 줄은 표시되지 않는다.</summary>
    private static string ReadSessionProperty(ISessionInfo session, string key)
    {
        if (session.Properties != null && session.Properties.TryGetValue(key, out var property) && property != null)
            return property.Value ?? string.Empty;
        return string.Empty;
    }

    /// <summary>게임이 시작돼 플레이 중인 방인지. State=InGame 프로퍼티로 판단.</summary>
    private static bool IsSessionInPlay(ISessionInfo session)
    {
        return ReadSessionProperty(session, kStatePropertyKey) == kStateInGame;
    }

    /// <summary>세션에 저장된 맵 인덱스를 카탈로그의 표시 이름으로 바꾼다. 저장돼 있지 않으면 빈 문자열.</summary>
    private static string MapNameFromSession(ISessionInfo session)
    {
        if (!int.TryParse(ReadSessionProperty(session, kMapPropertyKey), out int mapIndex))
            return string.Empty;

        if (mapIndex == GridSystem.MapCatalog.RandomMapIndex)
            return "랜덤";
        var catalog = GridSystem.MapCatalog.Instance;
        var mapDef = catalog != null ? catalog.Get(mapIndex) : null;
        return mapDef != null ? mapDef.DisplayName : string.Empty;
    }

    // '랜덤' 선택 전용 썸네일(물음표 카드) — 실제 MapDef가 없어 카탈로그 밖 리소스에서 로드.
    private static Sprite s_RandomMapThumb;
    private static Sprite RandomMapThumb()
        => s_RandomMapThumb != null ? s_RandomMapThumb
            : s_RandomMapThumb = Resources.Load<Sprite>("UI_pngs/MapThumb_Random");

    /// <summary>세션에 저장된 모드 인덱스를 짧은 모드 이름으로 바꾼다. 저장돼 있지 않으면 빈 문자열.</summary>
    private static string ModeLabelFromSession(ISessionInfo session)
    {
        if (!int.TryParse(ReadSessionProperty(session, kModePropertyKey), out int modeIndex))
            return string.Empty;

        return modeIndex >= 0 && modeIndex < kModeShortLabels.Length ? kModeShortLabels[modeIndex] : string.Empty;
    }

    /// <summary>호스트일 때 현재 맵/모드/상태를 세션 프로퍼티에 반영한다.
    /// 값이 실제로 바뀐 항목만 저장하며, 매 프레임 호출돼도 변화 없으면 네트워크 요청을 보내지 않는다.
    /// 이 값들이 다른 플레이어의 세션 목록 카드(맵 이름·모드)와 "게임 중 방 제외" 필터의 근거가 된다.</summary>
    private async void SyncHostSessionMetadata(int mapIndex, int modeIndex, string state, bool force = false)
    {
        if (m_ActiveSession == null || !m_ActiveSession.IsHost)
            return;

        string effectiveState = state ?? m_LastSavedState ?? kStateLobby;
        bool changed = mapIndex != m_LastSavedMapIndex
                       || modeIndex != m_LastSavedModeIndex
                       || effectiveState != m_LastSavedState;

        // 값이 그대로거나(불필요한 요청 방지), 저장이 진행 중이면(다음 프레임에 재시도) 건너뛴다.
        // 단, 게임 시작 표시(force)는 진행 중이어도 반드시 내보낸다.
        if (!changed || (m_IsSavingSessionMetadata && !force))
            return;

        IHostSession host;
        try
        {
            host = m_ActiveSession.AsHost();
        }
        catch (System.Exception)
        {
            return;   // 호스트가 아니면 조용히 무시
        }

        // 저장 성공을 전제로 트래커를 먼저 갱신하고, 실패하면 되돌려 다음 프레임에 재시도한다.
        int prevMap = m_LastSavedMapIndex;
        int prevMode = m_LastSavedModeIndex;
        string prevState = m_LastSavedState;
        m_LastSavedMapIndex = mapIndex;
        m_LastSavedModeIndex = modeIndex;
        m_LastSavedState = effectiveState;

        host.SetProperty(kMapPropertyKey, new SessionProperty(mapIndex.ToString()));
        host.SetProperty(kModePropertyKey, new SessionProperty(modeIndex.ToString()));
        host.SetProperty(kStatePropertyKey, new SessionProperty(effectiveState));
        // 날씨는 생성 시 정해지고 로비에서 바뀌지 않지만, 선택값 보관 차원에서 함께 저장한다.
        host.SetProperty(kWeatherPropertyKey, new SessionProperty(GridSystem.GameLoopManager.HostWeatherEnabled ? "1" : "0"));

        m_IsSavingSessionMetadata = true;
        try
        {
            await host.SavePropertiesAsync();
        }
        catch (System.Exception ex)
        {
            m_LastSavedMapIndex = prevMap;
            m_LastSavedModeIndex = prevMode;
            m_LastSavedState = prevState;
            Debug.LogWarning($"[JobsnailLobbySkinner] 세션 메타데이터 저장 실패: {ex.Message}");
        }
        finally
        {
            m_IsSavingSessionMetadata = false;
        }
    }

    /// <summary>게임 시작 직전에 방을 '플레이 중'으로 표시해 세션 목록에서 빠지게 한다.</summary>
    private void MarkSessionInPlay()
    {
        var readyNet = FindFirstObjectByType<LobbyRoomNet>(FindObjectsInactive.Include);
        int mapIndex = readyNet != null && readyNet.IsSpawned ? readyNet.SelectedMap : 0;
        int modeIndex = Mathf.Clamp(GridSystem.GameLoopManager.HostSelectedMode, 0, 2);
        SyncHostSessionMetadata(mapIndex, modeIndex, kStateInGame, force: true);
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

        // 생성 화면에서 고른 맵/모드/날씨를 게임·로비에 반영(로비에서 맵/모드는 계속 변경 가능).
        GridSystem.GameLoopManager.HostSelectedMode = Mathf.Clamp(m_SelectedMode, 0, kModeLabels.Length - 1);
        GridSystem.GameLoopManager.HostSelectedMap = m_SelectedMap;
        GridSystem.GameLoopManager.HostWeatherEnabled = m_SelectedWeatherEnabled;

        view.SetCreateStatus("방 생성 중...");
        m_CurrentRoomName = roomName;
        createSession.RequestCreateSession(roomName, m_IsPrivateRoom, m_IsPrivateRoom ? password : "");
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

        m_CurrentRoomMaxPlayers = LobbyRoomNet.RoomCapacity;
        if (m_CreateView != null && !string.IsNullOrWhiteSpace(m_CreateView.RoomNameText))
            m_CurrentRoomName = m_CreateView.RoomNameText;
        LobbyRoomNet.RequiredTotalPlayers = LobbyRoomNet.RoomCapacity;
        EnsureHostStarted();

        // 인원을 받지 않으므로 항상 대기방으로 진입한다. 방장은 혼자여도 바로 시작 가능하고,
        // 팀원이 있으면 전원이 준비를 눌렀을 때 [게임 시작]이 활성화된다.
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

        int maxPlayers = LobbyRoomNet.RoomCapacity;   // 정원(슬롯 표시용). 시작 조건과는 무관.
        int joinedCount = hasReadyNet ? Mathf.Clamp(readyNet.ConnectedCount, 1, maxPlayers) : 1;
        int targetReadyCount = hasReadyNet ? readyNet.TargetReadyCount : Mathf.Max(0, joinedCount - 1);
        int readyCount = hasReadyNet ? readyNet.ReadyCount : 0;
        // 새 규칙: 방에 들어온 팀원(호스트 제외)이 모두 준비하면 시작 가능(서버가 IsAllReady로 판정).
        bool allReady = hasReadyNet && readyNet.IsAllReady;

        var catalog = GridSystem.MapCatalog.Instance;
        int mapIndex = hasReadyNet ? readyNet.SelectedMap : 0;
        var mapDef = catalog != null ? catalog.Get(mapIndex) : null;
        int mapCount = catalog != null ? catalog.Count : 0;
        int mode = Mathf.Clamp(GridSystem.GameLoopManager.HostSelectedMode, 0, 2);
        // 로비 4종 모드(네트워크 동기화 → 방장 아닌 사람도 현재 모드가 보인다).
        int lobbyMode = hasReadyNet ? Mathf.Clamp(readyNet.SelectedLobbyMode, 0, kLobbyModeLabels.Length - 1) : 0;
        bool weatherOn = hasReadyNet ? readyNet.WeatherOn : GridSystem.GameLoopManager.HostWeatherEnabled;

        // 기록 텍스트는 세이브 파일(ES3)을 읽으므로 매 프레임 재계산하지 않는다.
        // 입력(모드/맵/인원)이 바뀔 때만 갱신 — 매 프레임 파일 접근(+ MPPM 동시 접근 IOException) 방지.
        if (lobbyMode != m_RecordCacheMode || mapIndex != m_RecordCacheMap || joinedCount != m_RecordCachePlayers)
        {
            m_RecordCacheMode = lobbyMode;
            m_RecordCacheMap = mapIndex;
            m_RecordCachePlayers = joinedCount;
            m_RecordCacheText = BuildRecordText(lobbyMode, mapDef, joinedCount);
        }
        string recordText = m_RecordCacheText;

        // 네트워크 로스터 → 슬롯 표시 데이터(입장 순서 고정, 빈칸 유지).
        if (hasReadyNet && readyNet.SlotCount > 0)
        {
            for (int i = 0; i < m_SlotBuffer.Length; i++)
            {
                m_SlotBuffer[i] = new JobsnailLobbySlot
                {
                    Occupied = readyNet.IsSlotOccupied(i),
                    Name = readyNet.GetSlotName(i),
                    IsHost = readyNet.IsSlotHost(i),
                    Ready = readyNet.IsSlotReady(i),
                    CharacterId = readyNet.GetSlotCharacterId(i),
                    OutfitId = readyNet.GetSlotOutfitId(i),
                    Team = readyNet.GetSlotTeam(i)
                };
            }
        }
        else
        {
            for (int i = 0; i < m_SlotBuffer.Length; i++)
                m_SlotBuffer[i] = default;
        }

        var state = new JobsnailLobbyRoomState
        {
            Slots = hasReadyNet && readyNet.SlotCount > 0 ? m_SlotBuffer : null,
            RoomName = m_CurrentRoomName,
            RoomIsFull = joinedCount >= maxPlayers,
            ShowStartButton = isHost,
            StartInteractable = hasReadyNet && allReady && isNetworkServer,
            ShowReadyButton = !isHost,
            ReadyInteractable = hasReadyNet,
            IsLocallyReady = hasReadyNet && readyNet.IsLocallyReady,
            AllReady = allReady,
            StartHint = BuildStartHint(hasReadyNet, readyNet, isHost, isNetworkServer, allReady, joinedCount, maxPlayers),
            ReadyStatus = BuildReadyStatus(hasReadyNet, targetReadyCount, joinedCount, maxPlayers, readyCount),
            JoinedCount = joinedCount,
            ReadyCount = readyCount,
            MaxPlayers = maxPlayers,
            ShowModeButton = isHost,
            ModeLabel = kLobbyModeLabels[lobbyMode],
            // '랜덤'(센티널 -1)은 카탈로그에 MapDef가 없다 — 전용 물음표 썸네일과 이름으로 표시
            MapName = mapDef != null ? mapDef.DisplayName
                : mapIndex == GridSystem.MapCatalog.RandomMapIndex ? "랜덤" : string.Empty,
            MapThumbnail = mapDef != null ? mapDef.Thumbnail
                : mapIndex == GridSystem.MapCatalog.RandomMapIndex ? RandomMapThumb() : null,
            ShowMapArrows = isHost && isNetworkServer && mapCount > 1,
            WeatherOn = weatherOn,
            ShowWeatherToggle = isHost,
            RecordText = recordText,
            ShowTeamSelect = hasReadyNet && readyNet.IsVersusMode
        };

        view.ApplyLobbyRoomState(state);

        // 방장이면 현재 맵/모드를 세션에 반영해 다른 플레이어의 목록 카드에 보이게 한다.
        SyncHostSessionMetadata(mapIndex, mode, kStateLobby);
    }

    private static string BuildStartHint(bool hasReadyNet, LobbyRoomNet readyNet, bool isHost, bool isNetworkServer,
        bool allReady, int joinedCount, int maxPlayers)
    {
        if (!hasReadyNet)
            return "레디 시스템 연결 중...";

        if (isHost && !isNetworkServer)
            return "방장 권한 복구 중...";

        // 2vs2: 양 팀 인원이 안 맞으면(1v2 등) 시작 자체가 불가.
        if (readyNet.IsVersusMode && !readyNet.TeamsBalancedForStart())
            return "양 팀 인원을 같게 맞춰야 시작할 수 있어요 (1v1 또는 2v2)";

        if (isHost)
        {
            if (joinedCount <= 1)
                return "혼자서도 바로 시작할 수 있어요!";
            return allReady ? "모든 팀원 준비 완료!" : "팀원들이 준비하길 기다리는 중";
        }

        return readyNet.IsLocallyReady ? "방장이 시작하기를 기다리는 중" : "준비 완료를 눌러줘";
    }

    private static string BuildReadyStatus(bool hasReadyNet, int targetReadyCount,
        int joinedCount, int maxPlayers, int readyCount)
    {
        if (!hasReadyNet)
            return "준비 상태를 불러오는 중...";

        if (targetReadyCount <= 0)
            return $"입장 {joinedCount}/{maxPlayers} · 바로 시작 가능";

        return $"입장 {joinedCount}/{maxPlayers} · 준비 {readyCount}/{targetReadyCount}";
    }

    // 로비 우측 패널 기록: 타임어택=개인 N인 최고기록, 2vs2=맵 승패, 자유건축=없음.
    private static string BuildRecordText(int lobbyMode, GridSystem.MapDef mapDef, int playerCount)
    {
        int players = Mathf.Clamp(playerCount, 1, 4);
        switch (lobbyMode)
        {
            case 0: // 타임어택
                return $"({players}인) 최고 기록 : {BestAcrossAnswers(mapDef, players)}";
            case 1:
            case 2: // 2VS2 대전
                return mapDef != null ? $"전적 : {SaveService.FormatVersus(mapDef.DisplayName)}" : "전적 : 0승 0패";
            default: // 자유건축
                return string.Empty;
        }
    }

    // 저장 키는 '정답 구조물 이름' 단위 → 이 맵의 정답들 전체에서 최고를 합산(완성도 우선, 동률 시 시간).
    private static string BestAcrossAnswers(GridSystem.MapDef def, int players)
    {
        int bestPct = -1;
        float bestSec = 0f;
        void Consider(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (!SaveService.TryGetBest(key, players, out int pct, out float sec)) return;
            if (pct > bestPct || (pct == bestPct && sec < bestSec)) { bestPct = pct; bestSec = sec; }
        }

        if (def != null)
        {
            Consider(def.DisplayName);
            if (def.Answers != null)
                foreach (var a in def.Answers)
                    if (a != null) Consider(a.DisplayName);
        }

        if (bestPct < 0) return "-";
        int s = Mathf.RoundToInt(bestSec);
        return $"{bestPct}%  {s / 60}분 {s % 60:00}초";
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
            MarkSessionInPlay();   // 목록에서 이 방을 제외
            readyNet.OnStartGameButtonClicked();
            return;
        }

        if (m_LobbyRoomView != null)
            m_LobbyRoomView.SetLobbyStartHint("레디 시스템 연결 후 시작할 수 있어요.");
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

    internal void PrefabToggleMapOptions()
    {
        PlayUIClick();
        if (m_CreateView != null)
            m_CreateView.ToggleMapOptions();
    }

    internal void PrefabToggleModeOptions()
    {
        PlayUIClick();
        if (m_CreateView != null)
            m_CreateView.ToggleModeOptions();
    }

    internal void PrefabToggleWeather()
    {
        PlayUIClick();
        m_SelectedWeatherEnabled = !m_SelectedWeatherEnabled;
        if (m_CreateView != null)
            m_CreateView.ApplyWeather(m_SelectedWeatherEnabled);
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

        // 준비 상태의 팀원은 나갈 수 없다. '준비'를 해제해야 나갈 수 있음.
        var readyNet = FindFirstObjectByType<LobbyRoomNet>(FindObjectsInactive.Include);
        if (readyNet != null && readyNet.IsSpawned && !IsLocalSessionHost() && readyNet.IsLocallyReady)
        {
            if (m_LobbyRoomView != null)
                m_LobbyRoomView.SetLobbyStartHint("준비 상태에서는 나갈 수 없어요. '준비'를 먼저 해제해줘.");
            return;
        }

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
        var readyNet = FindFirstObjectByType<LobbyRoomNet>(FindObjectsInactive.Include);
        if (readyNet != null && readyNet.IsSpawned)
            readyNet.HostCycleMode();   // 서버가 4종 모드 순환 + GameLoopManager 정적값 반영(전원 동기화)
        UpdateLobbyRoomView();
    }

    internal void PrefabToggleLobbyWeather()
    {
        PlayUIClick();
        var readyNet = FindFirstObjectByType<LobbyRoomNet>(FindObjectsInactive.Include);
        if (readyNet != null && readyNet.IsSpawned)
            readyNet.HostToggleWeather();
        UpdateLobbyRoomView();
    }

    /// <summary>로컬 플레이어가 자기 팀 선택(0=파랑, 1=빨강).</summary>
    internal void PrefabSelectTeam(int team)
    {
        PlayUIClick();
        var readyNet = FindFirstObjectByType<LobbyRoomNet>(FindObjectsInactive.Include);
        if (readyNet != null && readyNet.IsSpawned)
            readyNet.RequestSetTeam(team);
        UpdateLobbyRoomView();
    }

    internal void PrefabStepMap(int direction)
    {
        PlayUIClick();
        var readyNet = FindFirstObjectByType<LobbyRoomNet>(FindObjectsInactive.Include);
        if (readyNet != null)
            readyNet.HostSelectMap(readyNet.SelectedMap + direction);

        // 맵 변경을 뷰와 세션 property에 즉시 반영.
        UpdateLobbyRoomView();
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
