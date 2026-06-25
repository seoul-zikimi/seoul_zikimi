using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Unity.Properties;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.UIElements;

public class SessionBrowserVM : IDisposable
{
    private SessionObserver m_SessionObserver;
    private ServiceObserver<IMultiplayerService> m_ServiceObserver;
    
    private bool m_SelectedAndAvailable;
    private bool m_CanRefresh;
    private int m_SelectedSessionIndex;

    private ISession m_Session;
    private List<SessionInfoVM> m_Sessions;
    private string m_SessionType; // 💡 방 종류를 기억하기 위해 추가된 필드
    
    public List<SessionInfoVM> Sessions
    {
        get => m_Sessions;
        set
        {
            if (m_Sessions == value) return;
            m_Sessions = value;
            Notify();
        }
    }
    
    public int SelectedSessionIndex
    {
        get => m_SelectedSessionIndex;
        set
        {
            if (value >= 0 && value < Sessions.Count)
            {
                m_SelectedSessionIndex = value;
                SelectedAndAvailable = true;
            }
            else
            {
                SelectedAndAvailable = false;
            }
        }
    }

    public string GetSelectedSessionId()
    {
        if (SelectedSessionIndex >= 0 && SelectedSessionIndex < Sessions.Count)
        {
            return Sessions[SelectedSessionIndex].Id;
        }
        return null;
    }
    
    public bool SelectedAndAvailable
    {
        get => m_SelectedAndAvailable;
        set
        {
            var newValue = value;
            if (value && m_Session != null && m_Session.Id == GetSelectedSessionId())
            {
                newValue = false;
            }

            if (m_SelectedAndAvailable != newValue)
            {
                m_SelectedAndAvailable = newValue;
                Notify();
            }
        }
    }
    
    public bool CanRefresh
    {
        get => m_CanRefresh;
        set
        {
            if (m_CanRefresh == value) return;
            m_CanRefresh = value;
            Notify();
        }
    }

    public SessionBrowserVM(string sessionType)
    {
        m_SessionType = sessionType; // 💡 생성자에서 세션 타입 저장
        Sessions = new List<SessionInfoVM>();

        m_SessionObserver = new SessionObserver(sessionType);
        m_SessionObserver.SessionAdded += OnSessionAdded;

        if (m_SessionObserver.Session != null)
        {
            OnSessionAdded(m_SessionObserver.Session);
        }
        
        if (UnityServices.Instance != null)
        {
            m_ServiceObserver = new ServiceObserver<IMultiplayerService>();
            if (m_ServiceObserver.Service != null)
            {
                CanRefresh = true;
            }
            else
            {
                CanRefresh = false;
                m_ServiceObserver.Initialized += OnServicesInitialized;
            }
        }
    }

    // 🔥 [핵심 추가] 선택된 방의 고유 ID와 패스워드로 서버에 접속을 요청하는 함수
    public async Task<ISession> JoinSelectedSessionAsync(string password = null)
    {
        string sessionId = GetSelectedSessionId();
        if (string.IsNullOrEmpty(sessionId))
        {
            Debug.LogError("선택된 세션 ID가 없습니다.");
            return null;
        }

        var joinOptions = new JoinSessionOptions
        {
            Type = m_SessionType,
            Password = string.IsNullOrEmpty(password) ? null : password // 빈 문자열이면 null 처리
        };

        // 대시보드(UGS) 설정이나 필요에 따라 이름을 추가하고 싶다면 활성화 가능
        // joinOptions.WithPlayerName();

        // 💡 초대 코드가 아닌 방의 고유 ID로 접속 시도!
        return await MultiplayerService.Instance.JoinSessionByIdAsync(sessionId, joinOptions);
    }

    void OnServicesInitialized(IMultiplayerService service)
    {
        m_ServiceObserver.Initialized -= OnServicesInitialized;
        CanRefresh = true;
    }

    internal async Task UpdateSessionListAsync(int numberOfMaxSessions)
    {
        if (!CanRefresh)
        {
            Debug.LogWarning("Cannot refresh session list. Multiplayer Services are not initialized.");
            return;
        }

        try
        {
            CanRefresh = false;
            var queryResult = await MultiplayerService.Instance
                .QuerySessionsAsync(new QuerySessionsOptions
                {
                    SortOptions = new List<SortOption> { new (SortOrder.Descending, SortField.Name) }
                });

            foreach (var session in Sessions)
            {
                session.Dispose();
            }

            Sessions.Clear();
            for (var i = 0; (i < Math.Min(queryResult.Sessions.Count, numberOfMaxSessions)); i++)
            {
                Sessions.Add(new SessionInfoVM(queryResult.Sessions[i]));
            }
            
            CanRefresh = true;
            SelectedSessionIndex = -1;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to update session list: {ex.Message}");
        }
    }

    private void OnSessionAdded(ISession newSession)
    {
        m_Session = newSession;
        m_Session.RemovedFromSession += OnSessionRemoved;
        m_Session.Deleted += OnSessionRemoved;
        if (m_Session.Id == GetSelectedSessionId())
        {
            SelectedAndAvailable = false;
        }
    }

    private void OnSessionRemoved()
    {
        var lastSessionId = m_Session.Id;
        CleanupSession();
        if (lastSessionId == GetSelectedSessionId())
        {
            SelectedAndAvailable = true;
        }
    }

    private void CleanupSession()
    {
        m_Session.RemovedFromSession -= OnSessionRemoved;
        m_Session.Deleted -= OnSessionRemoved;
        m_Session = null;
    }

    public void Dispose()
    {
        if (m_SessionObserver != null)
        {
            m_SessionObserver.SessionAdded -= OnSessionAdded;
            m_SessionObserver.Dispose();
            m_SessionObserver = null;
        }
        if (m_ServiceObserver != null)
        {
            m_ServiceObserver.Dispose();
            m_ServiceObserver = null;
        }
        if (m_Session != null)
        {
            CleanupSession();
        }
    }

    public event Action<string> PropertyChanged;
    private void Notify(string propertyName = null)
    {
        PropertyChanged?.Invoke(propertyName);
    }
}