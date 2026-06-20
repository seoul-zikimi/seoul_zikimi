using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Unity.Properties;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.UIElements;

public class JoinSessionByCodeVM : IDisposable
{
    const string k_ValidSessionCodeCharacters = "6789BCDFGHJKLMNPQRTWbcdfghjklmnpqrtw";

    SessionObserver m_SessionObserver;
    ISession m_Session;
    
    public bool CanJoinSession
    {
        get => m_CanJoinSession;
        private set
        {
            var canJoin = value;
            if(canJoin && m_Session != null)
                canJoin = false;

            if (m_CanJoinSession == canJoin)
                return;

            m_CanJoinSession = canJoin;
            Notify();
        }
    }
    bool m_CanJoinSession;
    
    public string SessionCode
    {
        get => m_SessionCode;
        private set
        {
            if (m_SessionCode == value)
                return;

            m_SessionCode = value;
            CanJoinSession = CheckIsSessionCodeFormatValid(m_SessionCode);
            
            Notify();
        }
    }
    string m_SessionCode;

    public void SetSessionCode(string newCode)
    {
        if (SessionCode == newCode)
            return;

        SessionCode = newCode;
        Notify();
    }
    
    public JoinSessionByCodeVM(string sessionType)
    {
        m_SessionObserver = new SessionObserver(sessionType);

        m_SessionObserver.SessionAdded += OnSessionAdded;

        if (m_SessionObserver.Session != null)
        {
            OnSessionAdded(m_SessionObserver.Session);
        }
    }

    static bool CheckIsSessionCodeFormatValid(string str)
    {
        if (string.IsNullOrEmpty(str) || str.Length is < 6 or > 8)
            return false;

        foreach (var c in str)
        {
            if (!k_ValidSessionCodeCharacters.Contains(c))
                return false;
        }
        return true;
    }

    void OnSessionAdded(ISession session)
    {
        m_Session = session;
        m_Session.RemovedFromSession += OnSessionRemoved;
        m_Session.Deleted += OnSessionRemoved;
        CanJoinSession = false;
    }

    void OnSessionRemoved()
    {
        m_Session.RemovedFromSession -= OnSessionRemoved;
        m_Session.Deleted -= OnSessionRemoved;
        m_Session = null;
        CanJoinSession = CheckIsSessionCodeFormatValid(SessionCode);
    }

    public bool AreMultiplayerServicesInitialized()
    {
        return MultiplayerService.Instance != null;
    }

    public async Task<ISession> JoinSessionByCodeAsync(JoinSessionOptions joinSessionOptions)
    {
        return await MultiplayerService.Instance.JoinSessionByCodeAsync(SessionCode, joinSessionOptions);
    }

    public void Dispose()
    {
        if (m_SessionObserver != null)
        {
            m_SessionObserver.SessionAdded -= OnSessionAdded;
            m_SessionObserver.Dispose();
            m_SessionObserver = null;
        }
        if (m_Session != null)
        {
            m_Session.RemovedFromSession -= OnSessionRemoved;
            m_Session.Deleted -= OnSessionRemoved;
            m_Session = null;
        }
    }

    /// <summary>
    /// 값 변경 시 호출되는 이벤트
    /// </summary>
    public event Action<string> PropertyChanged;

    private void Notify(string propertyName = null)
    {
        PropertyChanged?.Invoke(propertyName);
    }
}
