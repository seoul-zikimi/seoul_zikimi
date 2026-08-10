using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using TMPro;
using UnityEngine.SceneManagement;

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

    public bool IsAllReady => m_IsAllReady.Value;
    public bool IsLocallyReady => m_IsLocallyReady;
    public int ReadyCount => m_ReadyCount.Value;
    public int TargetReadyCount => m_TargetReadyCount.Value;
    public int ConnectedCount => m_ConnectedCount.Value;
    public int MaxPlayers => m_MaxPlayers.Value;

    // ── 맵 선택(방장이 고르면 방 전원에게 동기화, 게임 시작 시 GameLoopManager로 전달) ──
    private NetworkVariable<int> m_MapIndex = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public int SelectedMap => m_MapIndex.Value;
    public event System.Action<int> MapChanged;   // 로비 UI 갱신용

    /// <summary>방장 전용: 맵 선택(카탈로그 인덱스, 순환). 클라가 부르면 무시.</summary>
    public void HostSelectMap(int index)
    {
        if (!IsServer) return;
        m_MapIndex.Value = WrapMapIndex(index);
    }

    /// <summary>카탈로그 개수 범위로 인덱스를 순환 보정한다.</summary>
    private static int WrapMapIndex(int index)
    {
        int n = GridSystem.MapCatalog.Instance != null ? GridSystem.MapCatalog.Instance.Count : 1;
        if (n <= 0) n = 1;
        return ((index % n) + n) % n;
    }

    private void OnMapChanged(int _, int now)
    {
        GridSystem.GameLoopManager.HostSelectedMap = now;   // 게임 씬 진입 시 서버가 이 값을 복제(호스트 외 클라에선 미사용)
        MapChanged?.Invoke(now);
    }

    public override void OnNetworkSpawn()
    {
        m_ReadyClients.Clear();
        m_IsLocallyReady = false;

        if (IsServer)
        {
            m_MaxPlayers.Value = RoomCapacity;
            // 방 생성 시 고른 맵으로 초기화(기본 0 대신). 로비에서 방장이 ◀▶로 계속 바꿀 수 있다.
            m_MapIndex.Value = WrapMapIndex(GridSystem.GameLoopManager.HostSelectedMap);
        }

        // 네트워크 변수 값이 변경될 때 실행할 이벤트 연결
        m_IsAllReady.OnValueChanged += OnAllReadyStatusChanged;
        m_ReadyCount.OnValueChanged += OnReadyCountChanged;
        m_TargetReadyCount.OnValueChanged += OnReadyCountChanged;
        m_ConnectedCount.OnValueChanged += OnReadyCountChanged;
        m_MapIndex.OnValueChanged += OnMapChanged;
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

        // 방에 갓 진입했을 때의 초기 UI 갱신
        UpdateUI(m_IsAllReady.Value);
    }

    public override void OnNetworkDespawn()
    {
        m_IsAllReady.OnValueChanged -= OnAllReadyStatusChanged;
        m_ReadyCount.OnValueChanged -= OnReadyCountChanged;
        m_TargetReadyCount.OnValueChanged -= OnReadyCountChanged;
        m_ConnectedCount.OnValueChanged -= OnReadyCountChanged;
        m_MapIndex.OnValueChanged -= OnMapChanged;
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

        // 모든 클라이언트가 준비 상태인지 검사 시작
        CheckAllPlayersReady();
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

        m_IsAllReady.Value = m_ReadyClients.Count >= target;
    }

    private void OnClientConnected(ulong clientId)
    {
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
    }

    private void UpdateUI(bool isAllReady)
    {
        // 호스트라면: 모든 클라이언트가 준비되었을 때만 시작 버튼을 활성화
        if (IsHost && startButton != null)
        {
            startButton.interactable = isAllReady;
        }

        // 상태 안내 텍스트 변경 (선택 사항)
        if (readyStatusText != null)
        {
            if (JobsnailUiKit.TmpFont != null)
                readyStatusText.font = JobsnailUiKit.TmpFont;
            if (m_TargetReadyCount.Value <= 0)
                readyStatusText.text = "바로 시작할 수 있어요. (대기 중인 팀원 없음)";
            else if (isAllReady)
                readyStatusText.text = "모든 플레이어가 준비되었습니다! 시작 가능.";
            else
                readyStatusText.text = $"다른 플레이어의 준비를 기다리는 중... ({m_ReadyCount.Value}/{m_TargetReadyCount.Value})";
        }
    }

    /// <summary>
    /// 호스트가 [게임 시작] 버튼을 누르면 인게임 씬으로 전환하는 함수
    /// </summary>
    public void OnStartGameButtonClicked()
    {
        if (!IsHost || !m_IsAllReady.Value) return;

        Debug.Log("게임 시작! 인게임 씬으로 다 함께 이동합니다.");
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
}
