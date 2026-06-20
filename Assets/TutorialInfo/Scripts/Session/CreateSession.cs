using System;
using UnityEngine;
using Blocks.Sessions.Common;
using TMPro;
using Unity.Netcode;
using Unity.Properties;
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

    void Start()
    {
        _viewModel = new CreateSessionVM(SessionSettings?.sessionType);

        // UI → ViewModel
        sessionNameField.onValueChanged.AddListener(OnSessionNameChanged);
        createButton.onClick.AddListener(CreateNewSession);
        isPrivate.onValueChanged.AddListener(OnIsPrivateChanged);
        sessionPasswordField.onValueChanged.AddListener(OnPasswordChanged);

        // ViewModel → UI
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        
        RefreshUI();
    }

    void OnDestroy()
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.Dispose();
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
        /*if(_viewModel.SessionCode != null)
            joinCode.text = $"Code: {_viewModel.SessionCode}";*/
        
        // 버튼 활성화 여부
        createButton.interactable = 
            _viewModel.CanRegisterSession && _viewModel.HasSessionName && _viewModel.HasValidPassword;

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
        if (!SessionSettings)
        {
            Debug.LogError("SessionSettings is null, it needs to be assigned in the uxml.");
            return;
        }
        
        if (!_viewModel.AreMultiplayerServicesInitialized())
        {
            Debug.LogError("Multiplayer Services not initialized.");
            return;
        }

        IHostSession session = await _viewModel.CreateSessionAsync(SessionSettings.ToSessionOptions());
        
        CreateSessioinBtnOnClick?.Invoke(true);
    }

    /// <summary>
    /// 세션 삭제
    /// </summary>
    public void DestroyCurrSession()
    {
        _viewModel.TryDeleteSessionAsync();
    }
}
