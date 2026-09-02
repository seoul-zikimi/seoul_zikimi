using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 방 입장 타이밍 사고를 막는 서버측 관문(정적).
///
/// 문제: 팀원이 방에 들어오는 중(UGS 세션 참가 ~ 넷코드 연결 완료 사이)에 방장이 [게임 시작]을 누르면,
/// 넷코드가 뒤늦게 붙은 클라이언트를 "서버의 현재 씬" = 게임 씬으로 그대로 동기화한다.
/// 그래서 준비를 누른 적도 없는 팀원이 게임 안으로 끌려 들어온다.
///
/// 해결: 게임 시작을 누른 뒤 도착하는 접속은 승인 단계(ConnectionApproval)에서 거절한다.
/// 승인 콜백은 NetworkManager 프리팹의 ConnectionApproval이 켜져 있어야 불리므로,
/// 다른 시스템이 그 설정을 꺼 버린 경우를 대비해 접속 완료 콜백에서 한 번 더 끊는 백업 경로를 둔다.
///
/// 거절당한 클라이언트는 DisconnectReason으로 <see cref="GameStartedReason"/>을 받아
/// "방장이 나감"이 아니라 "이미 시작한 방" 안내를 보게 된다.
/// </summary>
public static class SessionJoinGate
{
    /// <summary>거절 사유 문자열. 클라이언트가 DisconnectReason으로 받아 안내 문구를 고른다.</summary>
    public const string GameStartedReason = "SEOUL_ZIKIMI_GAME_ALREADY_STARTED";

    // 승인 콜백은 NetworkManager가 "하나만" 허용한다(여러 개 등록하면 setter가 예외를 던진다).
    // 그래서 += 로 붙이지 않고, 기존 콜백이 있으면 그것을 안에서 호출하는 방식으로 감싼다.
    private static readonly Action<NetworkManager.ConnectionApprovalRequest, NetworkManager.ConnectionApprovalResponse>
        s_Wrapper = Approve;

    private static Action<NetworkManager.ConnectionApprovalRequest, NetworkManager.ConnectionApprovalResponse> s_Inner;
    private static NetworkManager s_Manager;

    /// <summary>방장이 게임 시작을 눌러 씬 전환이 시작됐는지. true면 신규 접속을 받지 않는다.</summary>
    public static bool IsGameStarting { get; private set; }

    /// <summary>[서버] 게임 시작 직전에 호출. 이후 들어오는 접속은 모두 거절한다.</summary>
    public static void MarkGameStarting()
    {
        if (IsGameStarting)
            return;
        IsGameStarting = true;
        Debug.Log("[SessionJoinGate] 게임 시작 — 이후 신규 접속을 받지 않습니다.");
    }

    /// <summary>로비로 (되)돌아왔을 때 호출. 다시 입장을 받는다.</summary>
    public static void ResetForLobby()
    {
        if (!IsGameStarting)
            return;
        IsGameStarting = false;
        Debug.Log("[SessionJoinGate] 로비 복귀 — 신규 접속을 다시 받습니다.");
    }

    /// <summary>
    /// 현재 NetworkManager에 승인 콜백과 접속 콜백을 걸어 둔다. 매 프레임 불러도 싸다.
    /// 멀티플레이어 서비스가 세션을 시작하면서 승인 콜백을 덮어쓸 수 있어 매번 확인한다.
    /// </summary>
    public static void EnsureInstalled()
    {
        NetworkManager manager = NetworkManager.Singleton;
        if (manager == null)
        {
            s_Manager = null;
            s_Inner = null;
            return;
        }

        if (!ReferenceEquals(s_Manager, manager))
        {
            s_Manager = manager;
            s_Inner = null;
            manager.OnClientConnectedCallback -= OnClientConnected;
            manager.OnClientConnectedCallback += OnClientConnected;
            manager.OnServerStopped -= OnStopped;
            manager.OnServerStopped += OnStopped;
            manager.OnClientStopped -= OnStopped;
            manager.OnClientStopped += OnStopped;
        }

        if (!ReferenceEquals(manager.ConnectionApprovalCallback, s_Wrapper))
        {
            s_Inner = manager.ConnectionApprovalCallback;   // 남이 걸어 둔 승인 규칙은 그대로 살려 둔다
            manager.ConnectionApprovalCallback = s_Wrapper;
        }
    }

    private static void Approve(NetworkManager.ConnectionApprovalRequest request,
                                NetworkManager.ConnectionApprovalResponse response)
    {
        NetworkManager manager = NetworkManager.Singleton;

        if (s_Inner != null)
        {
            s_Inner(request, response);
        }
        else
        {
            // 승인 콜백을 쓰면 플레이어 오브젝트 생성 여부를 직접 정해야 한다(기본값이 false다).
            // 승인이 꺼져 있을 때 넷코드가 하던 판단 — PlayerPrefab이 있으면 생성 — 을 그대로 흉내 낸다.
            response.Approved = true;
            response.CreatePlayerObject = manager != null
                                          && manager.NetworkConfig != null
                                          && manager.NetworkConfig.PlayerPrefab != null;
        }

        if (!IsGameStarting || IsLocalConnection(manager, request.ClientNetworkId))
            return;

        response.Approved = false;
        response.CreatePlayerObject = false;
        response.Reason = GameStartedReason;
        response.Pending = false;
        Debug.LogWarning($"[SessionJoinGate] 게임이 이미 시작돼 접속을 거절합니다(clientId={request.ClientNetworkId}).");
    }

    // 호스트 자신도 StartHost 시 승인 검사를 거친다. 자기 자신은 절대 거절하지 않는다.
    private static bool IsLocalConnection(NetworkManager manager, ulong clientId)
    {
        if (manager == null)
            return false;
        if (clientId == manager.LocalClientId)
            return true;
        NetworkTransport transport = manager.NetworkConfig != null ? manager.NetworkConfig.NetworkTransport : null;
        return transport != null && clientId == transport.ServerClientId;
    }

    // 백업 경로: ConnectionApproval이 꺼져 있어 승인 단계에서 못 막은 경우,
    // 접속이 완료되는 즉시 서버가 내보낸다(게임 씬에 남겨 두지 않는다).
    private static void OnClientConnected(ulong clientId)
    {
        NetworkManager manager = NetworkManager.Singleton;
        if (manager == null || !manager.IsServer)
            return;
        if (!IsGameStarting || IsLocalConnection(manager, clientId))
            return;

        Debug.LogWarning($"[SessionJoinGate] 게임 시작 후 붙은 clientId={clientId}를 내보냅니다(승인 단계에서 못 막음).");
        manager.DisconnectClient(clientId, GameStartedReason);
    }

    private static void OnStopped(bool _)
    {
        IsGameStarting = false;
    }
}
