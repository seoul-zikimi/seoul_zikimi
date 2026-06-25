using System;
using UnityEngine;
using Blocks.Sessions.Common;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Properties;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine.UI;
using UnityEngine.UIElements;
using Button = UnityEngine.UI.Button;
using Random = System.Random;
using Toggle = UnityEngine.UI.Toggle;

public class CreateSession : MonoBehaviour
{
    [Header("UI")]
    public TMP_InputField sessionNameField;
    public Button createButton;
    public Toggle isPrivate;
    public TMP_InputField sessionPasswordField;
    //public TextMeshProUGUI joinCode;
    
    [SerializeField]
    private SessionSettings sessionSettings;
    
    public event Action<bool> CreateSessioinBtnOnClick;
    public event Action<string> CreateSessionFailed;
    
    public SessionSettings SessionSettings
    {
        get => sessionSettings;
        set
        {
            if (sessionSettings == value)
                return;

            sessionSettings = value;
        }
    }
    
    private CreateSessionVM _viewModel;
    private bool _initialized;
    private bool _isCreating;

    void Start()
    {
        Initialize();
    }

    private void Initialize()
    {
        if (_initialized)
            return;

        _viewModel = new CreateSessionVM(SessionSettings?.sessionType);

        // UI → ViewModel
        if (sessionNameField != null)
            sessionNameField.onValueChanged.AddListener(OnSessionNameChanged);
        if (createButton != null)
            createButton.onClick.AddListener(CreateNewSession);
        if (isPrivate != null)
            isPrivate.onValueChanged.AddListener(OnIsPrivateChanged);
        if (sessionPasswordField != null)
            sessionPasswordField.onValueChanged.AddListener(OnPasswordChanged);

        // ViewModel → UI
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        _initialized = true;
        RefreshUI();
    }

    void OnDestroy()
    {
        if (_viewModel != null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.Dispose();
        }
    }

    /// <summary>
    /// Input Field 입력값 반영
    /// </summary>
    /// <param name="newText"></param>
    private void OnSessionNameChanged(string newText)
    {
        // View → ViewModel
        _viewModel.SetSessionName(newText);
    }

    private void OnIsPrivateChanged(bool isPrivate)
    {
        _viewModel.SetIsPrivate(isPrivate);
    }

    private void OnPasswordChanged(string newPassword)
    {
        _viewModel.SetPassword(newPassword);
    }

    /// <summary>
    /// 변경된 값 UI에 반영
    /// </summary>
    /// <param name="property"></param>
    private void OnViewModelPropertyChanged(string property = null)
    {
        RefreshUI();
    }

    private void RefreshUI()
    {
        if (_viewModel == null)
            return;

        /*if(_viewModel.SessionCode != null)
            joinCode.text = $"Code: {_viewModel.SessionCode}";*/
        
        // 버튼 활성화 여부
        if (createButton != null)
        {
            createButton.interactable =
                _viewModel.CanRegisterSession && _viewModel.HasSessionName && _viewModel.HasValidPassword;
        }

        if (sessionPasswordField != null)
        {
            sessionPasswordField.gameObject.SetActive(_viewModel.IsPrivate);

            if (!_viewModel.IsPrivate)
            {
                sessionPasswordField.text = "";
                _viewModel.SetPassword("");
            }
        }
    }

    /// <summary>
    /// 방 생성 버튼 눌렀을 때 -> view model에서 세션 생성
    /// </summary>
    private async void CreateNewSession()
    {
        Initialize();

        if (_isCreating)
        {
            Debug.LogWarning("[CreateSession] 이미 방 생성 중이라 중복 클릭을 무시합니다.");
            return;
        }

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            CreateSessioinBtnOnClick?.Invoke(true);
            return;
        }

        if (!SessionSettings)
        {
            FailCreate("SessionSettings is null, it needs to be assigned in the uxml.");
            return;
        }

        _isCreating = true;
        try
        {
            if (UnityServices.State == ServicesInitializationState.Uninitialized)
                await UnityServices.InitializeAsync();

            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }
        catch (Exception ex)
        {
            FailCreate($"유니티 서비스 초기화 실패: {ex.Message}");
            _isCreating = false;
            return;
        }

        if (!_viewModel.AreMultiplayerServicesInitialized())
        {
            FailCreate("Multiplayer Services not initialized.");
            _isCreating = false;
            return;
        }

        IHostSession session;
        try
        {
            PrepareNetworkTransport();
            session = await _viewModel.CreateSessionAsync(SessionSettings.ToSessionOptions());
        }
        catch (Exception ex)
        {
            if (ex.Message.Contains("SessionTypeAlreadyExists", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("already registered", StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogWarning($"[CreateSession] 이미 등록된 세션 타입이 있어 기존 방 흐름으로 전환합니다: {ex.Message}");
                _isCreating = false;
                CreateSessioinBtnOnClick?.Invoke(true);
                return;
            }

            FailCreate($"세션 생성 실패: {ex.Message}");
            _isCreating = false;
            return;
        }

        if (session == null)
        {
            FailCreate("생성된 세션이 null입니다.");
            _isCreating = false;
            return;
        }

        try
        {
            CreateSessioinBtnOnClick?.Invoke(true);
        }
        catch (Exception ex)
        {
            Debug.LogError($"세션은 생성됐지만 로비 화면 전환 중 실패: {ex}");
        }
        finally
        {
            _isCreating = false;
        }
    }

    public void RequestCreateSession(string sessionName, bool privateRoom, string password, int maxPlayers)
    {
        Initialize();

        sessionName ??= "";
        password ??= "";

        if (SessionSettings != null)
            SessionSettings.maxPlayers = Mathf.Clamp(maxPlayers, 1, 4);

        if (sessionNameField != null)
            sessionNameField.text = sessionName;
        if (isPrivate != null)
            isPrivate.isOn = privateRoom;
        if (sessionPasswordField != null)
            sessionPasswordField.text = password;

        _viewModel.SetSessionName(sessionName);
        _viewModel.SetIsPrivate(privateRoom);
        _viewModel.SetPassword(privateRoom ? password : "");
        RefreshUI();

        CreateNewSession();
    }

    private static void PrepareNetworkTransport()
    {
        if (NetworkManager.Singleton == null)
        {
            Debug.LogWarning("[CreateSession] NetworkManager.Singleton이 없어서 네트워크 transport 준비를 건너뜁니다.");
            return;
        }

        var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        if (transport == null)
        {
            Debug.LogWarning("[CreateSession] NetworkManager에 UnityTransport가 없어서 transport 준비를 건너뜁니다.");
            return;
        }

        NetworkManager.Singleton.NetworkConfig.NetworkTransport = transport;
    }

    /// <summary>
    /// 세션 삭제
    /// </summary>
    public async void DestroyCurrSession()
    {
        if (_viewModel != null)
            await _viewModel.TryDeleteSessionAsync();
    }

    private void FailCreate(string message)
    {
        Debug.LogError($"세션 생성 실패: {message}");
        CreateSessionFailed?.Invoke(message);
    }
}
