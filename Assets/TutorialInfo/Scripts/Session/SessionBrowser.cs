using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Blocks.Sessions;
using Blocks.Sessions.Common;
using Unity.Properties;
using UnityEngine;
using UnityEngine.UIElements;
using Button = UnityEngine.UI.Button;
using Unity.Multiplayer.Center.Common;
using Unity.Services.Authentication;

public class SessionBrowser : MonoBehaviour
{
    [Header("UI")]
    public Transform sessionListParent;    
    public GameObject sessionItemPrefab;   
    public Button refreshButton;
    
    private SessionBrowserVM viewModel;
    private List<GameObject> createdItems = new();

    // 💡 [변경] 매니저에게 방 ID와 비밀번호 설정 여부를 넘겨줄 이벤트
    public event Action<string, bool> OnSessionSelected;

    [SerializeField]
    private SessionSettings sessionSettings;
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

    private void Awake()
    {
        viewModel = new SessionBrowserVM(SessionSettings?.sessionType);
        viewModel.PropertyChanged += OnViewModelChanged;

        refreshButton.onClick.AddListener(OnRefreshButtonClicked);
    }

    private void OnEnable()
    {
        OnRefreshButtonClicked();
    }

    private async void OnRefreshButtonClicked()
    {
        await viewModel.UpdateSessionListAsync(20);
        RefreshListUI();
    }

    /// <summary>
    /// 각 방 눌렀을 때 실행되는 함수
    /// </summary>
    private void OnSessionButtonClicked(int index)
    {
        viewModel.SelectedSessionIndex = index;
        Debug.Log($"Selected: {index}, Session Name: {viewModel.Sessions[index].Name}");
        
        if (!viewModel.SelectedAndAvailable)
        {
            Debug.LogError("Selected session is no longer selected.");
            return;
        }
        
        // 💡 [핵심] 선택된 방의 ID와 비밀번호가 걸려있는지 여부를 뽑아냅니다.
        string sessionId = viewModel.Sessions[index].Id;
        bool hasPassword = viewModel.Sessions[index].HasPassword;

        // 💡 중앙 매니저(LobbyManager)에게 이 정보를 전달합니다.
        OnSessionSelected?.Invoke(sessionId, hasPassword);
    }
    
    void OnViewModelChanged(string propertyName)
    {
        switch (propertyName)
        {
            case nameof(SessionBrowserVM.Sessions):
                RefreshListUI();
                break;

            case nameof(SessionBrowserVM.CanRefresh):
                refreshButton.interactable = viewModel.CanRefresh;
                break;
        }
    }
    
    void RefreshListUI()
    {
        foreach (var go in createdItems)
            Destroy(go);

        createdItems.Clear();

        for (int i = 0; i < viewModel.Sessions.Count; i++)
        {
            var item = Instantiate(sessionItemPrefab, sessionListParent);
            createdItems.Add(item);

            var controller = item.GetComponent<SessionItem>();
            controller.SetData(viewModel.Sessions[i]);

            int index = i;
            Button button = item.GetComponent<Button>();
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => OnSessionButtonClicked(index));
        }
    }

    private void OnDestroy()
    {
        viewModel?.Dispose();
    }
}