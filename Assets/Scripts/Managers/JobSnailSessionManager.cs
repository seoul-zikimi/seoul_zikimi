using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Services.Multiplayer; // 💡 패키지에 따라 Unity.Services.Lobbies 일 수 있음
using UnityEngine;
using UnityEngine.SceneManagement;

public class JobsnailSessionManager
{
    public static JobsnailSessionManager Instance { get; } = new JobsnailSessionManager();

    private ISession m_ActiveSession;
    private string m_CachedSessionId; // 🔑 방의 고유 ID를 문자열로 안전하게 보관할 변수
    private string m_CachedHostId;
    private bool m_IsHost;             // 내가 방장이었는지 여부 기억
    private bool m_IsLeaving;

    /// <summary>씬이 바뀌어도 유지되는 현재 UGS 세션. 로비 UI 상태 복원에 사용한다.</summary>
    public ISession ActiveSession => m_ActiveSession;
    public bool HasActiveSession => m_ActiveSession != null || !string.IsNullOrEmpty(m_CachedSessionId);

    private JobsnailSessionManager() { }

    // 🔄 로비(Skinner)에서 세션이 잡힐 때 이 ID들을 확실하게 백업해 둡니다.
    public void RegisterActiveSession(ISession session)
    {
        if (ReferenceEquals(m_ActiveSession, session))
        {
            CacheSession(session);
            return;
        }

        UnsubscribeSessionEvents();
        m_ActiveSession = session;
        m_CachedSessionId = null;
        m_CachedHostId = null;
        m_IsHost = false;
        if (session != null)
        {
            CacheSession(session);
            m_ActiveSession.SessionHostChanged += OnSessionHostChanged;
            m_ActiveSession.Changed += OnSessionChanged;
            Debug.Log($"[JobsnailSessionManager] 세션 등록 완료! ID: {m_CachedSessionId}, 방장여부: {m_IsHost}");
        }
    }

    private void CacheSession(ISession session)
    {
        if (session == null)
            return;

        m_CachedSessionId = session.Id;
        if (string.IsNullOrEmpty(m_CachedHostId))
            m_CachedHostId = session.Host;
        m_IsHost = session.IsHost;
    }

    private void UnsubscribeSessionEvents()
    {
        if (m_ActiveSession == null)
            return;

        m_ActiveSession.SessionHostChanged -= OnSessionHostChanged;
        m_ActiveSession.Changed -= OnSessionChanged;
    }

    private void OnSessionChanged()
    {
        if (m_ActiveSession == null || m_IsLeaving)
            return;

        string latestHostId = m_ActiveSession.Host;
        if (!string.IsNullOrEmpty(m_CachedHostId) &&
            !string.IsNullOrEmpty(latestHostId) &&
            latestHostId != m_CachedHostId)
        {
            _ = EndSessionBecauseHostLeftAsync($"세션 방장이 변경됨: {m_CachedHostId} -> {latestHostId}");
            return;
        }

        m_CachedSessionId = m_ActiveSession.Id;
        m_IsHost = m_ActiveSession.IsHost;
    }

    private void OnSessionHostChanged(string newHostId)
    {
        if (m_IsLeaving)
            return;

        if (!string.IsNullOrEmpty(m_CachedHostId) &&
            !string.IsNullOrEmpty(newHostId) &&
            newHostId != m_CachedHostId)
        {
            _ = EndSessionBecauseHostLeftAsync($"세션 방장 이탈 감지: {m_CachedHostId} -> {newHostId}");
            return;
        }

        m_CachedHostId = newHostId;
        if (m_ActiveSession != null)
            m_IsHost = m_ActiveSession.IsHost;
    }

    public async Task LeaveLobbyRoomSecurelyAsync(bool showMainMenu = true)
    {
        Debug.Log($"[JobsnailSessionManager] 세션 안전 퇴장 및 파괴 요청 처리 시작. (저장된 방 ID: {m_CachedSessionId})");
        if (m_IsLeaving)
            return;

        m_IsLeaving = true;

        // 1. 넷코드 무조건 종료
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.Shutdown();
        }

        try
        {
            // 2. 🔥 [핵심 수정] 세션 객체가 null이더라도, 캐싱된 ID가 있다면 UGS 서비스에 직접 명령을 내립니다!
            if (!string.IsNullOrEmpty(m_CachedSessionId))
            {
                if (m_IsHost)
                {
                    Debug.Log($"[JobsnailSessionManager] 💣 방장 권한으로 UGS 서버에서 방({m_CachedSessionId})을 무조건 폭파합니다.");
                    
                    // ✨ 세션 객체 없이 서비스 인스턴스를 통해 직접 ID로 방을 삭제하는 치트키 함수야!
                    await Unity.Services.Lobbies.LobbyService.Instance.DeleteLobbyAsync(m_CachedSessionId);
                }
                else
                {
                    Debug.Log($"[JobsnailSessionManager] 🚪 팀원 권한으로 방({m_CachedSessionId})에서 퇴장합니다.");
                    // 만약 객체가 살아있다면 정석 퇴장, 죽었다면 패스
                    if (m_ActiveSession != null) await m_ActiveSession.LeaveAsync();
                }
            }
            else
            {
                Debug.LogWarning("[JobsnailSessionManager] 저장된 세션 ID가 없습니다! 백엔드 삭제를 건너뜁니다.");
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[JobsnailSessionManager] UGS 백엔드 방 삭제 중 예외 발생 (이미 지워졌을 수 있음): {ex.Message}");
        }
        finally
        {
            // 3. 변수들 말끔히 초기화 후 메인 이동
            UnsubscribeSessionEvents();
            m_ActiveSession = null;
            m_CachedSessionId = null;
            m_CachedHostId = null;
            m_IsHost = false;
            m_IsLeaving = false;

            if (showMainMenu)
                ShowMainMenuScene();
        }
    }

    public async Task EndSessionBecauseHostLeftAsync(string reason)
    {
        if (m_IsLeaving)
            return;

        m_IsLeaving = true;
        Debug.LogWarning($"[JobsnailSessionManager] 방장 이탈로 세션을 종료합니다. {reason}");

        try
        {
            if (NetworkManager.Singleton != null)
                NetworkManager.Singleton.Shutdown();

            if (m_ActiveSession != null)
            {
                try
                {
                    // UGS가 방장 이탈 직후 다른 클라이언트를 새 host로 선출할 수 있다.
                    // 우리 게임 정책은 "방장 이탈 = 세션 삭제"이므로, 내가 새 host가 된 경우에도
                    // 이어받지 않고 즉시 lobby를 삭제한다.
                    if (m_ActiveSession.IsHost && !string.IsNullOrEmpty(m_ActiveSession.Id))
                    {
                        Debug.LogWarning($"[JobsnailSessionManager] 방장 이탈 후 새 host 권한으로 방({m_ActiveSession.Id})을 삭제합니다.");
                        await Unity.Services.Lobbies.LobbyService.Instance.DeleteLobbyAsync(m_ActiveSession.Id);
                    }
                    else
                    {
                        await m_ActiveSession.LeaveAsync();
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[JobsnailSessionManager] 방장 이탈 후 세션 퇴장 중 예외: {ex.Message}");
                }
            }
        }
        finally
        {
            UnsubscribeSessionEvents();
            m_ActiveSession = null;
            m_CachedSessionId = null;
            m_CachedHostId = null;
            m_IsHost = false;
            m_IsLeaving = false;
            // 방이 터진 팀원은 메인 메뉴가 아니라 세션 목록으로 돌아가야 한다(QA 요구).
            SeoulZikimi.UI.New.UiNewRoomClosedNotice.ShowOnRoomList();   // 목록 화면 위 안내 팝업 예약
            ShowRoomListScene();
        }
    }

    public void HandleNetcodeDisconnected(ulong clientId)
    {
        if (m_IsLeaving || NetworkManager.Singleton == null)
            return;

        // 서버는 일반 팀원 이탈도 이 콜백으로 받는다. 서버/호스트는 게임을 유지해야 한다.
        if (NetworkManager.Singleton.IsServer)
            return;

        // 클라이언트에서 서버 연결이 끊긴 것은 방장 이탈/세션 종료로 처리한다.
        if (clientId == NetworkManager.Singleton.LocalClientId || !NetworkManager.Singleton.IsListening)
            _ = EndSessionBecauseHostLeftAsync($"넷코드 서버 연결 끊김(clientId={clientId})");
    }

    /// <summary>세션(방) 목록 화면이 있는 Lobby 씬으로 이동한다. 이미 그 씬이면 다시 로드하지 않는다.</summary>
    private static void ShowRoomListScene()
    {
        if (SceneManager.GetActiveScene().name != SceneNames.Lobby)
            SceneManager.LoadScene(SceneNames.Lobby);
    }

    private static void ShowMainMenuScene()
    {
        if (SceneManager.GetActiveScene().name != SceneNames.BootstrapScene)
        {
            SceneManager.LoadScene(SceneNames.BootstrapScene);
            return;
        }

        JobsnailMainMenu.Show();
    }
}

public sealed class JobsnailSessionDisconnectWatcher : MonoBehaviour
{
    private NetworkManager m_SubscribedNetworkManager;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        var go = new GameObject("@JobsnailSessionDisconnectWatcher");
        UnityEngine.Object.DontDestroyOnLoad(go);
        go.AddComponent<JobsnailSessionDisconnectWatcher>();
    }

    private void Update()
    {
        var nm = NetworkManager.Singleton;
        if (ReferenceEquals(m_SubscribedNetworkManager, nm))
            return;

        if (m_SubscribedNetworkManager != null)
            m_SubscribedNetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;

        m_SubscribedNetworkManager = nm;

        if (m_SubscribedNetworkManager != null)
            m_SubscribedNetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
    }

    private void OnDestroy()
    {
        if (m_SubscribedNetworkManager != null)
            m_SubscribedNetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
    }

    private void OnApplicationQuit()
    {
        _ = JobsnailSessionManager.Instance.LeaveLobbyRoomSecurelyAsync(false);
    }

    private void OnClientDisconnected(ulong clientId)
    {
        JobsnailSessionManager.Instance.HandleNetcodeDisconnected(clientId);
    }
}
