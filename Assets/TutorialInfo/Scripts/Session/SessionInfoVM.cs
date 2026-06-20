using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Properties;
using Unity.Services.Multiplayer;
using UnityEngine.UIElements;

public class SessionInfoVM : ISessionInfo, IDisposable
{
    private const string k_Unavailalble = "N/A";

    private ISession _session;
    private ISessionInfo _sessionInfo;

    public SessionInfoVM(ISessionInfo sessionInfo)
    {
        _sessionInfo = sessionInfo;
    }

    public SessionInfoVM(ISession session)
    {
        _session = session;

        _session.Changed += OnSessionChanged;
        _session.SessionHostChanged += OnSessionHostChanged;
        _session.SessionPropertiesChanged += OnSessionPropertiesChanged;
    }
    
    public string Name
        => _sessionInfo?.Name ?? _session?.Name;
    
    public string Id
        => _sessionInfo?.Id ?? _session?.Id;
    
    public string Upid
        => _sessionInfo?.Upid ?? k_Unavailalble;
    
    public string HostId
        => _sessionInfo?.HostId ?? _session?.Host;
    
    public int AvailableSlots
        => _sessionInfo?.AvailableSlots ?? _session?.AvailableSlots ?? 0;
    
    public int MaxPlayers
        => _sessionInfo?.MaxPlayers ?? _session?.MaxPlayers ?? 0;
    
    public bool IsLocked
        => _sessionInfo?.IsLocked ?? _session?.IsLocked ?? true;
    
    public bool HasPassword
        => _sessionInfo?.HasPassword ?? _session?.HasPassword ?? true;
    
    public DateTime LastUpdated
        => _sessionInfo?.LastUpdated ?? DateTime.UnixEpoch;
    
    public DateTime Created
        => _sessionInfo?.Created ?? DateTime.UnixEpoch;
    
    public IReadOnlyDictionary<string, SessionProperty> Properties
        => _sessionInfo?.Properties ?? _session?.Properties;

    private void OnSessionHostChanged(string obj)
    {
        Notify(nameof(HostId));
    }

    private void OnSessionPropertiesChanged()
    {
        Notify(nameof(Properties));
    }

    private void OnSessionChanged()
    {
        Notify(nameof(Name));
        Notify(nameof(LastUpdated));
        Notify(nameof(HasPassword));
        Notify(nameof(IsLocked));
        Notify(nameof(MaxPlayers));
        Notify(nameof(AvailableSlots));
    }

    public void Dispose()
    {
        if (_session != null)
        {
            _session.Changed -= OnSessionChanged;
            _session.SessionHostChanged -= OnSessionHostChanged;
            _session.SessionPropertiesChanged -= OnSessionPropertiesChanged;
        }

        _session = null;
        _sessionInfo = null;
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
