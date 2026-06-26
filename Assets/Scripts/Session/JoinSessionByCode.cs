using Blocks.Sessions.Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class JoinSessionByCode : MonoBehaviour
{
    public TMP_InputField inputField;
    public Button joinButton;

    private JoinSessionByCodeVM _viewModel;

    [SerializeField] private SessionSettings sessionSettings;

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

    void Start()
    {
        _viewModel = new JoinSessionByCodeVM(SessionSettings?.sessionType);
        _viewModel.PropertyChanged += OnViewModelChanged;

        if (inputField != null)
            inputField.onValueChanged.AddListener(OnInputFieldValueChanged);
        if (joinButton != null)
            joinButton.onClick.AddListener(JoinSession);

    }

    void JoinSession()
    {
        if (!_viewModel.AreMultiplayerServicesInitialized())
        {
            Debug.LogError(
                "Multiplayer Services are not initialized. You can initialize them with default settings by adding a Servicesinitialization and PlayerAuthentication components in your scene.");
            return;
        }

        _ = _viewModel.JoinSessionByCodeAsync(sessionSettings.ToJoinSessionOptions());
    }

    private void OnViewModelChanged(string property = null)
    {

    }

    private void OnInputFieldValueChanged(string newText)
    {
        // View → ViewModel
        _viewModel.SetSessionCode(newText);
    }

}
