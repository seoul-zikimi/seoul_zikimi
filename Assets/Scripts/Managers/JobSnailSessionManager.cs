using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Services.Multiplayer; // 💡 패키지에 따라 Unity.Services.Lobbies 일 수 있음
using UnityEngine;

public class JobsnailSessionManager
{
    public static JobsnailSessionManager Instance { get; } = new JobsnailSessionManager();

    private ISession m_ActiveSession;
    private string m_CachedSessionId; // 🔑 방의 고유 ID를 문자열로 안전하게 보관할 변수
    private bool m_IsHost;             // 내가 방장이었는지 여부 기억

    private JobsnailSessionManager() { }

    // 🔄 로비(Skinner)에서 세션이 잡힐 때 이 ID들을 확실하게 백업해 둡니다.
    public void RegisterActiveSession(ISession session)
    {
        m_ActiveSession = session;
        if (session != null)
        {
            m_CachedSessionId = session.Id;
            m_IsHost = session.IsHost;
            Debug.Log($"[JobsnailSessionManager] 세션 등록 완료! ID: {m_CachedSessionId}, 방장여부: {m_IsHost}");
        }
    }

    public async Task LeaveLobbyRoomSecurelyAsync()
    {
        Debug.Log($"[JobsnailSessionManager] 세션 안전 퇴장 및 파괴 요청 처리 시작. (저장된 방 ID: {m_CachedSessionId})");

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
            m_ActiveSession = null;
            m_CachedSessionId = null;
            m_IsHost = false;
            
            JobsnailMainMenu.Show();
        }
    }
}