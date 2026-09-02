using Unity.Netcode;
using Unity.Collections;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using TMPro;
using UnityEngine.SceneManagement;
using System;

public class LobbyRoomNet : NetworkBehaviour
{
    /// <summary>방 정원(세션 최대 인원). 더 이상 생성 시 인원을 받지 않으므로 고정값을 쓴다.</summary>
    public const int RoomCapacity = 4;

    // 예전 "생성 시 정한 최대 인원" 값. 더 이상 시작 조건에 쓰지 않고 정원(RoomCapacity)으로 통일한다.
    public static int RequiredTotalPlayers { get; set; } = RoomCapacity;

    [Header("UI 연결")]
    public Button readyButton;      // 클라이언트 화면에만 뜰 [준비] 버튼
    public Button startButton;      // 호스트 화면에만 뜰 [게임 시작] 버튼
    public TMP_Text readyStatusText; // (선택) "모든 플레이어가 준비하길 기다리는 중..." 등을 띄울 텍스트

    // 💡 [핵심] 모든 클라이언트가 준비 완료되었는지 서버가 체크해서 동기화하는 네트워크 변수
    private NetworkVariable<bool> m_IsAllReady = new NetworkVariable<bool>(
        false, 
        NetworkVariableReadPermission.Everyone, 
        NetworkVariableWritePermission.Server
    );

    // 서버(호스트)의 메모리에서만 관리할 '준비 완료된 클라이언트 ID' 목록
    private HashSet<ulong> m_ReadyClients = new HashSet<ulong>();
    private bool m_IsLocallyReady = false;
    private NetworkVariable<int> m_ReadyCount = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );
    private NetworkVariable<int> m_TargetReadyCount = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );
    private NetworkVariable<int> m_ConnectedCount = new NetworkVariable<int>(
        1,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );
    private NetworkVariable<int> m_MaxPlayers = new NetworkVariable<int>(
        1,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // 아직 넷코드에 붙지 않은 '입장 중'인 팀원이 있는지(서버 판정 → 전원 복제).
    // 이게 true면 시작 버튼을 잠근다 — 안 그러면 로딩 중인 팀원이 준비도 못 한 채 게임에 끌려온다.
    private NetworkVariable<bool> m_JoinInProgress = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public bool IsAllReady => m_IsAllReady.Value;
    public bool IsLocallyReady => m_IsLocallyReady;
    public int ReadyCount => m_ReadyCount.Value;
    public int TargetReadyCount => m_TargetReadyCount.Value;
    public int ConnectedCount => m_ConnectedCount.Value;
    public int MaxPlayers => m_MaxPlayers.Value;
    public bool CanHostEditSettings => IsHost;
    public bool IsVersusMode => m_LobbyMode.Value == 1 || m_LobbyMode.Value == 2;
    /// <summary>누군가 방에 들어오는 중(세션엔 있는데 넷코드 연결 전)인지.</summary>
    public bool IsJoinInProgress => m_JoinInProgress.Value;
    public bool CanStartGame =>
        m_IsAllReady.Value && !m_JoinInProgress.Value && (!IsVersusMode || HasValidVersusBalance());
    public event Action StateChanged;
    public event Action<string, string> ChatMessageReceived;

    // ── 맵 선택(방장이 고르면 방 전원에게 동기화, 게임 시작 시 GameLoopManager로 전달) ──
    private NetworkVariable<int> m_MapIndex = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public int SelectedMap => m_MapIndex.Value;
    public event System.Action<int> MapChanged;   // 로비 UI 갱신용

    // ── 모드/날씨(방장이 로비에서 고르면 방 전원에게 동기화) ──
    // 로비 모드 4종: 0=타임어택, 1=2VS2 대전(아이템), 2=2VS2 대전(타임어택), 3=자유건축
    private NetworkVariable<int> m_LobbyMode = new(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<bool> m_WeatherOn = new(
        true, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<int> m_SeasonSelectionMode = new(
        (int)SeoulZikimi.Weather.SeasonSelectionMode.Random,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<int> m_FixedSeason = new(
        (int)SeoulZikimi.Weather.Season.Spring,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public int SelectedLobbyMode => m_LobbyMode.Value;
    public bool WeatherOn => m_WeatherOn.Value;
    public SeoulZikimi.Weather.SeasonSelectionMode SeasonSelectionMode =>
        (SeoulZikimi.Weather.SeasonSelectionMode)m_SeasonSelectionMode.Value;
    public SeoulZikimi.Weather.Season FixedSeason => (SeoulZikimi.Weather.Season)m_FixedSeason.Value;
    public const int LobbyModeCount = 4;

    /// <summary>방장 전용: 모드 순환(0→1→2→3→0). GameLoopManager 정적값에 즉시 반영.</summary>
    public void HostCycleMode()
    {
        if (!IsServer) return;
        bool wasVersus = IsVersusMode;
        m_LobbyMode.Value = (m_LobbyMode.Value + 1) % LobbyModeCount;
        ApplyLobbyModeToGameLoop(m_LobbyMode.Value);
        // 비팀 모드 → 2vs2로 처음 들어올 때 위치 기본 팀(왼쪽 레드/오른쪽 블루)으로 초기화.
        if (IsVersusMode && !wasVersus)
            AssignDefaultTeamsByPosition();
        CheckAllPlayersReady();   // 모드가 팀 밸런스 게이트에 영향
    }

    public void HostSelectMode(int index)
    {
        if (!IsServer) return;
        m_LobbyMode.Value = Mathf.Clamp(index, 0, LobbyModeCount - 1);
        ApplyLobbyModeToGameLoop(m_LobbyMode.Value);
    }

    /// <summary>방장 전용: 날씨 ON/OFF 토글.</summary>
    public void HostToggleWeather()
    {
        if (!IsServer) return;
        m_WeatherOn.Value = !m_WeatherOn.Value;
        GridSystem.GameLoopManager.HostWeatherEnabled = m_WeatherOn.Value;
    }

    // 로비 모드(0~3) → GameLoopManager 모드(0 타임어택/1 2vs2/2 자유) + 아이템 플래그로 매핑.
    private static void ApplyLobbyModeToGameLoop(int lobbyMode)
    {
        switch (lobbyMode)
        {
            case 1: GridSystem.GameLoopManager.HostSelectedMode = 1; GridSystem.GameLoopManager.HostVersusUsesItems = true; break;
            case 2: GridSystem.GameLoopManager.HostSelectedMode = 1; GridSystem.GameLoopManager.HostVersusUsesItems = false; break;
            case 3: GridSystem.GameLoopManager.HostSelectedMode = 2; break;
            default: GridSystem.GameLoopManager.HostSelectedMode = 0; break;
        }
    }

    // GameLoopManager 정적값(생성 화면에서 고른 값)에서 로비 모드 인덱스를 역산.
    private static int DeriveLobbyMode()
    {
        int m = Mathf.Clamp(GridSystem.GameLoopManager.HostSelectedMode, 0, 2);
        if (m == 0) return 0;
        if (m == 2) return 3;
        return GridSystem.GameLoopManager.HostVersusUsesItems ? 1 : 2;
    }

    // ── 로비 슬롯 로스터(입장 순서대로 왼쪽부터 고정 배치, 중간에 나가면 빈칸 유지) ──
    // 인덱스 = 슬롯 위치(0~3). 서버가 관리하고 전원에게 복제한다.
    private readonly NetworkList<LobbySlot> m_Slots = new();

    // 현재 방장의 clientId(방장 이양 대비 NetworkVariable로 복제). 지금은 최초 호스트로 고정.
    private NetworkVariable<ulong> m_HostClientId = new(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public int SlotCount => m_Slots.Count;
    public bool IsSlotOccupied(int i) => i >= 0 && i < m_Slots.Count && m_Slots[i].Occupied;
    public string GetSlotName(int i) => (i >= 0 && i < m_Slots.Count) ? m_Slots[i].Nickname.ToString() : "";
    public string GetSlotCharacterId(int i) => (i >= 0 && i < m_Slots.Count) ? m_Slots[i].CharacterId.ToString() : "";
    public string GetSlotOutfitId(int i) => (i >= 0 && i < m_Slots.Count) ? m_Slots[i].OutfitId.ToString() : "";
    public bool IsSlotReady(int i) => i >= 0 && i < m_Slots.Count && m_Slots[i].Ready;
    public int GetSlotTeam(int i) => (i >= 0 && i < m_Slots.Count) ? m_Slots[i].Team : 0;
    public int LocalTeam
    {
        get
        {
            ulong id = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0;
            int slot = FindSlotByClient(id);
            return slot >= 0 ? m_Slots[slot].Team : 0;
        }
    }

    public bool IsSlotLocal(int i)
    {
        ulong id = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0;
        return i >= 0 && i < m_Slots.Count && m_Slots[i].Occupied && m_Slots[i].ClientId == id;
    }

    public bool IsSlotHost(int i) =>
        i >= 0 && i < m_Slots.Count && m_Slots[i].Occupied && m_Slots[i].ClientId == m_HostClientId.Value;

    private struct LobbySlot : INetworkSerializable, System.IEquatable<LobbySlot>
    {
        public bool Occupied;
        public ulong ClientId;
        public FixedString32Bytes Nickname;
        public FixedString64Bytes CharacterId;
        public FixedString64Bytes OutfitId;
        public bool Ready;
        public byte Team;   // 0=미지정/A, 1=B (2vs2 단계에서 사용)

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Occupied);
            serializer.SerializeValue(ref ClientId);
            serializer.SerializeValue(ref Nickname);
            serializer.SerializeValue(ref CharacterId);
            serializer.SerializeValue(ref OutfitId);
            serializer.SerializeValue(ref Ready);
            serializer.SerializeValue(ref Team);
        }

        public bool Equals(LobbySlot other) =>
            Occupied == other.Occupied && ClientId == other.ClientId &&
            Nickname.Equals(other.Nickname) && CharacterId.Equals(other.CharacterId) &&
            OutfitId.Equals(other.OutfitId) && Ready == other.Ready && Team == other.Team;
    }

    /// <summary>방장 전용: 맵 선택(카탈로그 인덱스, 순환). 클라가 부르면 무시.
    /// 공터(2vs2 경기장)·튜토리얼은 선택지가 아니므로 건너뛴다 — ◀▶로 순환 시 방향 유지.
    /// MapCatalog.RandomMapIndex('랜덤')는 실제 맵이 아니라 그대로 통과시킨다.</summary>
    public void HostSelectMap(int index)
    {
        if (!IsServer) return;
        int dir = index >= m_MapIndex.Value ? 1 : -1;
        m_MapIndex.Value = SkipUnselectable(WrapMapIndex(index), dir);
    }

    /// <summary>서버 전용: 선택지 필터를 거치지 않고 맵을 그대로 지정한다.
    /// 튜토리얼처럼 목록에서 뺀 맵을 코드가 직접 지정해야 할 때만 쓴다(HostSelectMap은 그런 맵을 건너뛴다).</summary>
    public void HostSetMapExact(int index)
    {
        if (!IsServer) return;
        m_MapIndex.Value = index;
    }

    public void SelectLocalTeam(int team)
    {
        if (!IsSpawned || NetworkManager.Singleton == null || !IsVersusMode || (!IsHost && m_IsLocallyReady))
            return;
        SelectTeamRpc((byte)Mathf.Clamp(team, 0, 1));
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void SelectTeamRpc(byte team, RpcParams rpc = default)
    {
        int slot = FindSlotByClient(rpc.Receive.SenderClientId);
        if (slot < 0)
            return;
        LobbySlot value = m_Slots[slot];
        if (!IsVersusMode || (!IsSlotHost(slot) && value.Ready))
            return;
        value.Team = (byte)Mathf.Clamp(team, 0, 1);
        m_Slots[slot] = value;
    }

    public void SendChat(string message)
    {
        if (!IsSpawned || string.IsNullOrWhiteSpace(message))
            return;
        message = message.Trim();
        if (message.Length > 50)
            message = message.Substring(0, 50);
        SendChatRpc(message);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void SendChatRpc(FixedString128Bytes message, RpcParams rpc = default)
    {
        ulong sender = rpc.Receive.SenderClientId;
        int slot = FindSlotByClient(sender);
        if (slot < 0 || !m_Slots[slot].Occupied)
            return;
        if (!AllowChatFrom(sender))
            return;   // 도배 차단 — 방 전체로 중계하지 않는다
        FixedString32Bytes nickname = m_Slots[slot].Nickname;
        BroadcastChatRpc(nickname, message);
    }

    // ── 채팅 도배 방지(서버 최종 판정) ──
    // 클라(LobbyPanel)에도 같은 규칙이 있지만 그쪽은 변조될 수 있으므로 서버가 한 번 더 막는다.
    // 연속 ChatBurstLimit개까지 통과, 그 뒤엔 ChatCooldownSeconds 대기. 대기가 지나면 카운터 초기화.
    private const int ChatBurstLimit = 3;
    private const float ChatCooldownSeconds = 3f;

    private struct ChatQuota
    {
        public int BurstCount;
        public float LastSentAt;
    }

    // 슬롯 인덱스가 아니라 clientId로 기록한다. 슬롯은 비었다가 다른 사람이 앉을 수 있어
    // 새로 들어온 플레이어가 앞사람의 쿨타임을 물려받는 문제를 피한다.
    private readonly Dictionary<ulong, ChatQuota> m_ChatQuotas = new();

    private bool AllowChatFrom(ulong clientId)
    {
        m_ChatQuotas.TryGetValue(clientId, out ChatQuota quota);
        float now = Time.unscaledTime;
        if (now - quota.LastSentAt >= ChatCooldownSeconds)
            quota.BurstCount = 0;
        else if (quota.BurstCount >= ChatBurstLimit)
            return false;   // 막힌 전송은 LastSentAt을 갱신하지 않는다 — 마지막 '성공' 기준으로 3초를 센다

        quota.BurstCount++;
        quota.LastSentAt = now;
        m_ChatQuotas[clientId] = quota;
        return true;
    }

    [Rpc(SendTo.Everyone)]
    private void BroadcastChatRpc(FixedString32Bytes nickname, FixedString128Bytes message)
    {
        ChatMessageReceived?.Invoke(nickname.ToString(), message.ToString());
    }

    /// <summary>카탈로그 개수 범위로 인덱스를 순환 보정한다. '랜덤' 센티널은 실제 맵이 아니므로 보정하지 않고 그대로 둔다.</summary>
    private static int WrapMapIndex(int index)
    {
        if (index == GridSystem.MapCatalog.RandomMapIndex) return index;
        int n = GridSystem.MapCatalog.Instance != null ? GridSystem.MapCatalog.Instance.Count : 1;
        if (n <= 0) n = 1;
        return ((index % n) + n) % n;
    }

    /// <summary>index가 고를 수 없는 맵(공터·튜토리얼)이면 dir 방향으로 다음 일반 맵까지 넘긴다(전부 그렇다면 그대로).
    /// '랜덤' 센티널은 언제나 유효한 선택이므로 그대로 통과.</summary>
    private static int SkipUnselectable(int index, int dir)
    {
        if (index == GridSystem.MapCatalog.RandomMapIndex) return index;
        var catalog = GridSystem.MapCatalog.Instance;
        if (catalog == null || catalog.Count == 0) return index;
        for (int step = 0; step < catalog.Count; step++)
        {
            if (catalog.IsSelectable(index)) return index;
            index = WrapMapIndex(index + dir);
        }
        return index;
    }

    private void OnMapChanged(int _, int now)
    {
        GridSystem.GameLoopManager.HostSelectedMap = now;   // 게임 씬 진입 시 서버가 이 값을 복제(호스트 외 클라에선 미사용)
        MapChanged?.Invoke(now);
        StateChanged?.Invoke();
    }

    public override void OnNetworkSpawn()
    {
        m_ReadyClients.Clear();
        m_ChatQuotas.Clear();
        m_IsLocallyReady = false;

        if (IsServer)
        {
            // 로비(재)진입 — 게임 시작 잠금을 풀고 다시 입장을 받는다.
            SessionJoinGate.ResetForLobby();
            m_JoinInProgress.Value = false;
            m_JoiningSinceUnscaled = -1f;
            m_MaxPlayers.Value = RoomCapacity;
            // 방 생성 시 고른 맵으로 초기화(기본 0 대신). 로비에서 방장이 ◀▶로 계속 바꿀 수 있다.
            m_MapIndex.Value = WrapMapIndex(GridSystem.GameLoopManager.HostSelectedMap);
            // 생성 화면에서 고른 모드/날씨를 로비 네트워크 변수 초기값으로.
            m_LobbyMode.Value = DeriveLobbyMode();
            m_WeatherOn.Value = GridSystem.GameLoopManager.HostWeatherEnabled;
            m_SeasonSelectionMode.Value = (int)GridSystem.GameLoopManager.HostSeasonSelectionMode;
            m_FixedSeason.Value = (int)GridSystem.GameLoopManager.HostFixedSeason;

            // 슬롯을 정원만큼 빈 상태로 초기화하고, 방장(호스트) 자리부터 채운다.
            m_Slots.Clear();
            for (int i = 0; i < RoomCapacity; i++)
                m_Slots.Add(new LobbySlot { Occupied = false });

            ulong hostId = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0;
            m_HostClientId.Value = hostId;
            OccupySlotForClient(hostId);
        }

        // 네트워크 변수 값이 변경될 때 실행할 이벤트 연결
        m_IsAllReady.OnValueChanged += OnAllReadyStatusChanged;
        m_ReadyCount.OnValueChanged += OnReadyCountChanged;
        m_TargetReadyCount.OnValueChanged += OnReadyCountChanged;
        m_ConnectedCount.OnValueChanged += OnReadyCountChanged;
        m_JoinInProgress.OnValueChanged += OnJoinInProgressChanged;
        m_MapIndex.OnValueChanged += OnMapChanged;
        m_LobbyMode.OnValueChanged += OnLobbyModeChanged;
        m_WeatherOn.OnValueChanged += OnWeatherChanged;
        m_SeasonSelectionMode.OnValueChanged += OnSeasonSettingsChanged;
        m_FixedSeason.OnValueChanged += OnSeasonSettingsChanged;
        m_Slots.OnListChanged += OnSlotsChanged;
        OnMapChanged(0, m_MapIndex.Value);   // 늦참자 초기 반영

        if (IsHost)
        {
            // 방장(호스트) 세팅
            if (readyButton != null)
                readyButton.gameObject.SetActive(false); // 호스트는 준비할 필요 없음
            if (startButton != null)
            {
                startButton.gameObject.SetActive(true);
                startButton.interactable = false;       // 처음엔 버튼 비활성화
                startButton.onClick.RemoveAllListeners();
                startButton.onClick.AddListener(OnStartGameButtonClicked);
            }
            
            // 혹시 도중에 누군가 나갔을 때를 대비한 탈주 감지 이벤트
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            CheckAllPlayersReady();
        }
        else
        {
            // 게스트(클라이언트) 세팅
            if (readyButton != null)
                readyButton.gameObject.SetActive(true);
            if (startButton != null)
                startButton.gameObject.SetActive(false); // 클라이언트에겐 시작 버튼을 숨김
            
            if (readyButton != null)
            {
                readyButton.onClick.RemoveAllListeners();
                readyButton.onClick.AddListener(ToggleReadyState);
            }
        }

        // 내 닉네임 + 착용 캐릭터를 서버 슬롯에 등록(전원 복제).
        SubmitSlotInfo();

        // 게임씬 로드 시작 감지 → 로딩 화면 선표시(호스트·클라 공통, 씬 전환 내내 유지).
        // 주의: 클라는 Load 이벤트가 오기 '전에' 로비 씬 언로드로 이 오브젝트가 despawn된다
        // (NGO OnClientSceneLoadingEvent가 씬 정리 후 이벤트를 쏨). 그래서 인스턴스가 아닌
        // static 핸들러를 걸고 despawn 때도 해제하지 않는다(-= 후 += 로 중복 구독만 방지).
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.SceneManager != null)
        {
            NetworkManager.Singleton.SceneManager.OnSceneEvent -= OnNetSceneEvent;
            NetworkManager.Singleton.SceneManager.OnSceneEvent += OnNetSceneEvent;
        }

        // 방에 갓 진입했을 때의 초기 UI 갱신
        UpdateUI(m_IsAllReady.Value);
        StateChanged?.Invoke();
    }

    public override void OnNetworkDespawn()
    {
        m_IsAllReady.OnValueChanged -= OnAllReadyStatusChanged;
        m_ReadyCount.OnValueChanged -= OnReadyCountChanged;
        m_TargetReadyCount.OnValueChanged -= OnReadyCountChanged;
        m_ConnectedCount.OnValueChanged -= OnReadyCountChanged;
        m_JoinInProgress.OnValueChanged -= OnJoinInProgressChanged;
        m_MapIndex.OnValueChanged -= OnMapChanged;
        m_LobbyMode.OnValueChanged -= OnLobbyModeChanged;
        m_WeatherOn.OnValueChanged -= OnWeatherChanged;
        m_SeasonSelectionMode.OnValueChanged -= OnSeasonSettingsChanged;
        m_FixedSeason.OnValueChanged -= OnSeasonSettingsChanged;
        m_Slots.OnListChanged -= OnSlotsChanged;
        // OnSceneEvent 구독은 여기서 해제하지 않는다 — 클라는 게임씬 Load 이벤트보다 despawn이 먼저라
        // 해제하면 로딩 화면 선표시를 못 받는다. static 핸들러라 인스턴스 누수 없음(스폰 시 중복 방지).
        if (IsHost && NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
        }
    }

    /// <summary>
    /// [클라이언트] 준비 버튼을 누를 때마다 상태 토글
    /// </summary>
    public void ToggleReadyState()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
            return;

        if (IsHost)
            return;

        m_IsLocallyReady = !m_IsLocallyReady;
        StateChanged?.Invoke();
        
        // 내 버튼 텍스트 변경
        if (readyButton != null)
        {
            var textText = readyButton.GetComponentInChildren<TMP_Text>();
            if (textText != null) textText.text = "준비";
        }

        // 서버(방장)에게 내 무전(ServerRpc)으로 준비 상태를 전송
        SetReadyStatusServerRpc(NetworkManager.Singleton.LocalClientId, m_IsLocallyReady);
    }

    /// <summary>
    /// [서버 RPC] 클라이언트가 보낸 상태를 방장 서버가 받아서 리스트에 추가/삭제
    /// </summary>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void SetReadyStatusServerRpc(ulong clientId, bool isReady)
    {
        if (isReady)
            m_ReadyClients.Add(clientId);
        else
            m_ReadyClients.Remove(clientId);

        // 슬롯에도 준비 상태 반영(전원 복제 → 슬롯별 '준비 완료' 표시).
        int slot = FindSlotByClient(clientId);
        if (slot >= 0)
        {
            var s = m_Slots[slot];
            s.Ready = isReady;
            m_Slots[slot] = s;
        }

        // 모든 클라이언트가 준비 상태인지 검사 시작
        CheckAllPlayersReady();
    }

    // ────────────────────────── 슬롯 로스터(서버 전용) ──────────────────────────

    private int FindSlotByClient(ulong clientId)
    {
        for (int i = 0; i < m_Slots.Count; i++)
            if (m_Slots[i].Occupied && m_Slots[i].ClientId == clientId)
                return i;
        return -1;
    }

    private int FindFirstEmptySlot()
    {
        for (int i = 0; i < m_Slots.Count; i++)
            if (!m_Slots[i].Occupied)
                return i;
        return -1;
    }

    /// <summary>[서버] 가장 왼쪽 빈 슬롯에 clientId를 앉힌다(빈칸=구멍은 그대로 유지, 재정렬 없음).</summary>
    private void OccupySlotForClient(ulong clientId)
    {
        if (!IsServer)
            return;
        if (FindSlotByClient(clientId) >= 0)
            return;

        int idx = FindFirstEmptySlot();
        if (idx < 0)
            return;

        m_Slots[idx] = new LobbySlot
        {
            Occupied = true,
            ClientId = clientId,
            Nickname = default,
            CharacterId = default,
            Ready = false,
            Team = (byte)(IsVersusMode ? BalancedTeamForNewPlayer(clientId) : 0)
        };
    }

    // 슬롯 위치 기본 팀: 0,1번(왼쪽)=레드(1), 2,3번(오른쪽)=블루(0).
    private static byte DefaultTeamForSlot(int slotIndex) => (byte)(slotIndex <= 1 ? 1 : 0);

    /// <summary>[서버] 모든 점유 슬롯을 위치 기본 팀으로 재배정(2vs2 진입 시).</summary>
    private void AssignDefaultTeamsByPosition()
    {
        if (!IsServer) return;
        for (int i = 0; i < m_Slots.Count; i++)
        {
            if (!m_Slots[i].Occupied) continue;
            byte team = DefaultTeamForSlot(i);
            if (m_Slots[i].Team != team)
            {
                var s = m_Slots[i];
                s.Team = team;
                m_Slots[i] = s;
            }
        }
    }

    /// <summary>[서버] 나간 clientId의 슬롯을 비운다. 위치는 그대로 두어 중간에 구멍이 남는다.</summary>
    private void ClearSlotForClient(ulong clientId)
    {
        if (!IsServer)
            return;

        int idx = FindSlotByClient(clientId);
        if (idx < 0)
            return;

        m_Slots[idx] = new LobbySlot { Occupied = false };
    }

    /// <summary>각 클라가 스폰 시 자기 닉네임(PlayerPrefs) + 착용 캐릭터ID를 서버로 제출.</summary>
    private void SubmitSlotInfo()
    {
        string nick = PlayerPrefs.GetString("PlayerNickname", "");
        if (string.IsNullOrEmpty(nick))
            nick = $"플레이어{(NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0)}";
        if (nick.Length > 12) nick = nick.Substring(0, 12);
        while (System.Text.Encoding.UTF8.GetByteCount(nick) > 28 && nick.Length > 0)
            nick = nick.Substring(0, nick.Length - 1);

        string charId = SaveService.EquippedCharacter ?? "";
        string outfitId = SaveService.EquippedOutfit ?? "";

        SubmitSlotInfoRpc(nick, charId, outfitId);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void SubmitSlotInfoRpc(FixedString32Bytes nick, FixedString64Bytes charId, FixedString64Bytes outfitId, RpcParams rpc = default)
    {
        ulong sender = rpc.Receive.SenderClientId;

        int idx = FindSlotByClient(sender);
        if (idx < 0)
        {
            OccupySlotForClient(sender);   // 접속 콜백보다 RPC가 먼저 도착한 경우 대비
            idx = FindSlotByClient(sender);
        }
        if (idx < 0)
            return;

        var s = m_Slots[idx];
        s.Nickname = nick;
        s.CharacterId = charId;
        s.OutfitId = outfitId;
        m_Slots[idx] = s;
    }

    // ────────────────────────── 팀 선택 (2vs2) ──────────────────────────

    /// <summary>[클라이언트] 로컬 플레이어가 자기 팀 선택(0=파랑, 1=빨강).</summary>
    public void RequestSetTeam(int team)
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening) return;
        SetTeamServerRpc((byte)Mathf.Clamp(team, 0, 1));
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void SetTeamServerRpc(byte team, RpcParams rpc = default)
    {
        int idx = FindSlotByClient(rpc.Receive.SenderClientId);
        if (idx < 0) return;
        var s = m_Slots[idx];
        s.Team = team;
        m_Slots[idx] = s;
        CheckAllPlayersReady();   // 팀 변경이 시작 조건(밸런스)에 영향
    }

    /// <summary>2vs2에서 양 팀이 동수(1v1 또는 2v2)이고 각 팀 최소 1명이면 true. 팀 모드가 아니면 항상 true.</summary>
    public bool TeamsBalancedForStart()
    {
        if (!IsVersusMode) return true;
        int blue = 0, red = 0;
        for (int i = 0; i < m_Slots.Count; i++)
        {
            if (!m_Slots[i].Occupied) continue;
            if (m_Slots[i].Team == 1) red++; else blue++;
        }
        return blue >= 1 && red >= 1 && blue == red;
    }

    /// <summary>
    /// [서버] 호스트를 제외한 모든 클라이언트가 준비를 완료했는지 판단
    /// </summary>
    private void CheckAllPlayersReady()
    {
        if (!IsServer) return;

        // 현재 서버에 접속한 총 인원수 (호스트 포함)
        int totalConnected = NetworkManager.Singleton != null ? NetworkManager.Singleton.ConnectedClients.Count : 1;
        m_ConnectedCount.Value = Mathf.Clamp(totalConnected, 1, RoomCapacity);

        // 더 이상 방 정원 기준으로 기다리지 않는다. "지금 방에 들어와 있는 팀원(호스트 제외)"이
        // 전부 준비를 누르면 시작 가능. 방장 혼자면 기다릴 팀원이 없으므로 바로 시작 가능.
        int target = Mathf.Max(0, totalConnected - 1);
        m_TargetReadyCount.Value = target;
        m_ReadyCount.Value = Mathf.Min(m_ReadyClients.Count, target);

        // 2vs2는 양 팀 인원이 같아야(1v1/2v2) 시작 가능 — 한쪽이 더 많으면 시작 불가.
        m_IsAllReady.Value = m_ReadyClients.Count >= target && TeamsBalancedForStart();
    }

    // ────────────────────────── 입장 진행 중 감지(시작 버튼 잠금) ──────────────────────────
    //
    // 팀원이 방에 들어오는 데엔 두 단계가 있다. ① UGS 세션 참가 ② 넷코드 연결.
    // ①만 끝난 사이에는 NetworkManager.ConnectedClients에 아직 없어서, 방장 화면에는
    // "기다릴 팀원 없음(target=0)"으로 보이고 시작 버튼이 열려 버린다. 그 상태로 시작하면
    // 넷코드가 뒤늦게 붙은 팀원을 게임 씬으로 동기화해 준비도 못 한 채 끌고 들어온다.
    // 그래서 "세션 인원 − 넷코드 접속 인원"으로 ①~② 사이 인원을 세어 시작을 막는다.
    // 넷코드 이벤트로는 잡히지 않는 변화라 서버에서 주기적으로 다시 판정한다.
    private const float JoinPollInterval = 0.25f;

    // 세션 인원 정보가 실제와 어긋난 채 굳으면(비정상 종료한 팀원이 UGS에서 늦게 정리되는 등)
    // 방장이 영영 시작을 못 하게 된다. 이 시간을 넘겨도 계속 '입장 중'이면 낡은 정보로 보고 잠금을 푼다.
    private const float JoinGraceSeconds = 12f;

    private float m_NextJoinPollAt;
    private float m_JoiningSinceUnscaled = -1f;

    private void Update()
    {
        if (!IsSpawned || !IsServer)
            return;
        if (Time.unscaledTime < m_NextJoinPollAt)
            return;
        m_NextJoinPollAt = Time.unscaledTime + JoinPollInterval;
        RefreshJoinInProgress();
    }

    private void RefreshJoinInProgress()
    {
        if (!IsServer)
            return;

        bool joining = CountJoiningPlayers() > 0;
        float now = Time.unscaledTime;

        if (!joining)
        {
            m_JoiningSinceUnscaled = -1f;
        }
        else
        {
            if (m_JoiningSinceUnscaled < 0f)
                m_JoiningSinceUnscaled = now;
            else if (now - m_JoiningSinceUnscaled > JoinGraceSeconds)
                joining = false;   // 유예 초과 — 세션 인원 정보가 낡은 것으로 보고 잠금 해제
        }

        // 값이 바뀌면 OnValueChanged가 서버 포함 전원에서 불려 UI가 갱신된다.
        m_JoinInProgress.Value = joining;
    }

    /// <summary>UGS 세션에는 들어왔지만 아직 넷코드에 붙지 않은 인원 수. 세션 정보가 없으면 0.</summary>
    private static int CountJoiningPlayers()
    {
        ISession session = JobsnailSessionManager.Instance.ActiveSession;
        if (session == null || session.Players == null)
            return 0;
        int connected = NetworkManager.Singleton != null ? NetworkManager.Singleton.ConnectedClients.Count : 1;
        return Mathf.Max(0, session.Players.Count - connected);
    }

    private void OnJoinInProgressChanged(bool previousValue, bool newValue)
    {
        UpdateUI(m_IsAllReady.Value);
    }

    private void OnClientConnected(ulong clientId)
    {
        OccupySlotForClient(clientId);   // 입장 순서대로 왼쪽 빈 슬롯 배정
        RefreshJoinInProgress();         // 방금 붙은 팀원만큼 '입장 중' 인원이 줄었다
        CheckAllPlayersReady();
    }

    /// <summary>
    /// [서버] 준비했던 클라이언트가 도중에 방에서 나가버렸을 때 예외 처리
    /// </summary>
    private void OnClientDisconnected(ulong clientId)
    {
        if (m_ReadyClients.Contains(clientId))
        {
            m_ReadyClients.Remove(clientId);
        }
        m_ChatQuotas.Remove(clientId);   // 나간 클라의 도배 기록은 남기지 않는다
        ClearSlotForClient(clientId);   // 슬롯을 비워 구멍을 남긴다(재정렬 없음)
        CheckAllPlayersReady();
    }

    // 💡 네트워크 변수(m_IsAllReady)가 바뀌면 호스트/클라이언트 모두에서 자동 실행됨
    private void OnAllReadyStatusChanged(bool previousValue, bool newValue)
    {
        UpdateUI(newValue);
    }

    private void OnReadyCountChanged(int previousValue, int newValue)
    {
        UpdateUI(m_IsAllReady.Value);
        StateChanged?.Invoke();
    }

    private void OnLobbyModeChanged(int previousValue, int newValue)
    {
        ApplyLobbyModeToGameLoop(newValue);
        if (IsServer)
            ApplyTeamMode(previousValue, newValue);
        StateChanged?.Invoke();
    }

    private void OnWeatherChanged(bool previousValue, bool newValue)
    {
        GridSystem.GameLoopManager.HostWeatherEnabled = newValue;
        StateChanged?.Invoke();
    }

    private void OnSeasonSettingsChanged(int previousValue, int newValue)
    {
        GridSystem.GameLoopManager.HostSeasonSelectionMode = SeasonSelectionMode;
        GridSystem.GameLoopManager.HostFixedSeason = FixedSeason;
        StateChanged?.Invoke();
    }

    private void OnSlotsChanged(NetworkListEvent<LobbySlot> changeEvent) => StateChanged?.Invoke();

    private void UpdateUI(bool isAllReady)
    {
        // 호스트라면: 모든 클라이언트가 준비되었고 입장 중인 팀원도 없을 때만 시작 버튼을 활성화
        if (IsHost && startButton != null)
        {
            startButton.interactable = isAllReady && !m_JoinInProgress.Value;
        }

        // 상태 안내 텍스트 변경 (선택 사항)
        if (readyStatusText != null)
        {
            if (JobsnailUiKit.TmpFont != null)
                readyStatusText.font = JobsnailUiKit.TmpFont;
            if (m_JoinInProgress.Value)
                readyStatusText.text = "팀원이 방에 들어오는 중입니다...";
            else if (m_TargetReadyCount.Value <= 0)
                readyStatusText.text = "바로 시작할 수 있어요. (대기 중인 팀원 없음)";
            else if (isAllReady)
                readyStatusText.text = "모든 플레이어가 준비되었습니다! 시작 가능.";
            else
                readyStatusText.text = $"다른 플레이어의 준비를 기다리는 중... ({m_ReadyCount.Value}/{m_TargetReadyCount.Value})";
        }
        StateChanged?.Invoke();
    }

    /// <summary>
    /// 호스트가 [게임 시작] 버튼을 누르면 인게임 씬으로 전환하는 함수
    /// </summary>
    public void OnStartGameButtonClicked()
    {
        if (!IsHost || !CanStartGame) return;

        Debug.Log("게임 시작! 인게임 씬으로 다 함께 이동합니다.");
        // 이 시점 이후에 도착하는 접속은 거절한다. 안 그러면 씬 전환 도중 붙은 팀원을
        // 넷코드가 게임 씬으로 그대로 동기화해 준비도 못 한 채 끌고 들어온다.
        SessionJoinGate.MarkGameStarting();
        MatchStartHUD.ShowLoadingEarly();   // 호스트: 씬 전환 전에 로딩 화면부터(전환 내내 유지)

        // 💡 넷코드 환경에서 다 함께 씬을 이동할 때는 NetworkSceneManager를 사용해야 해!
        if (NetworkManager.Singleton != null &&
            NetworkManager.Singleton.SceneManager != null &&
            NetworkManager.Singleton.NetworkConfig.EnableSceneManagement)
        {
            NetworkManager.Singleton.SceneManager.LoadScene(SceneNames.GameScene, LoadSceneMode.Single);
            return;
        }

        SceneManager.LoadScene(SceneNames.GameScene, LoadSceneMode.Single);
    }

    // 클라이언트: 서버가 게임씬 로드를 시작하면(SceneEventType.Load) 즉시 로딩 화면을 띄운다.
    // 씬 전환 전 로비 화면 위에서부터 보이고, DontDestroyOnLoad라 게임씬까지 이어진다.
    private static void OnNetSceneEvent(SceneEvent e)
    {
        if (e.SceneEventType == SceneEventType.Load && e.SceneName == SceneNames.GameScene)
        {
            Debug.Log($"[LobbyRoomNetController] 게임씬 로드 감지(clientId={e.ClientId}) → 로딩 화면 선표시.");
            MatchStartHUD.ShowLoadingEarly();
        }
    }

    private int BalancedTeamForNewPlayer(ulong clientId)
    {
        if (clientId == m_HostClientId.Value) return 0;
        int blue = 0, red = 0;
        for (int i = 0; i < m_Slots.Count; i++)
        {
            if (!m_Slots[i].Occupied) continue;
            if (m_Slots[i].Team == 0) blue++; else red++;
        }
        return blue <= red ? 0 : 1;
    }

    private void ApplyTeamMode(int previousMode, int newMode)
    {
        bool wasVersus = previousMode == 1 || previousMode == 2;
        bool nowVersus = newMode == 1 || newMode == 2;
        if (wasVersus == nowVersus) return;

        int blue = 0, red = 0;
        for (int i = 0; i < m_Slots.Count; i++)
        {
            LobbySlot slot = m_Slots[i];
            if (!slot.Occupied) continue;
            if (!nowVersus) slot.Team = 0;
            else if (slot.ClientId == m_HostClientId.Value) { slot.Team = 0; blue++; }
            else if (blue <= red) { slot.Team = 0; blue++; }
            else { slot.Team = 1; red++; }
            m_Slots[i] = slot;
        }
    }

    private bool HasValidVersusBalance()
    {
        int blue = 0, red = 0;
        for (int i = 0; i < m_Slots.Count; i++)
        {
            if (!m_Slots[i].Occupied) continue;
            if (m_Slots[i].Team == 0) blue++; else red++;
        }
        return (blue == 1 && red == 1) || (blue == 2 && red == 2);
    }
}
