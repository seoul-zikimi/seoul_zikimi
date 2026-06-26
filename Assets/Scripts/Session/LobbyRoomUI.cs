using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

// 💡 NetworkBehaviour 대신 MonoBehaviour를 사용해 스폰 의존성을 없앱니다.
public class LobbyRoomUI : MonoBehaviour
{
    [Header("UI Panels")]
    public GameObject hostPanel;    
    public GameObject clientPanel;  

    [Header("Host Controls")]
    public Button startGameButton; 

    // 💡 오브젝트가 SetActive(true) 되는 순간 무조건 실행됩니다.
    private void OnEnable()
    {
        if (hostPanel != null) hostPanel.SetActive(false);
        if (clientPanel != null) clientPanel.SetActive(false);

        // NetworkManager가 켜져 있는지 확인
        if (NetworkManager.Singleton == null) return;

        // 👑 내가 방장(Host / Server)일 때
        // NetworkManager.Singleton.IsServer(또는 IsHost)로 직접 판별합니다.
        if (NetworkManager.Singleton.IsServer)
        {
            if (hostPanel != null) hostPanel.SetActive(true);
            
            if (startGameButton != null)
            {
                var readyNet = FindFirstObjectByType<LobbyRoomNet>(FindObjectsInactive.Include);
                startGameButton.interactable = readyNet != null && readyNet.IsAllReady;
                startGameButton.onClick.RemoveAllListeners();
                startGameButton.onClick.AddListener(OnStartGameButtonClicked);
            }
            
            Debug.Log("[LobbyRoom] 방장 패널을 활성화합니다.");
        }
        // 👤 내가 클라이언트(Client)일 때
        else
        {
            if (clientPanel != null) clientPanel.SetActive(true);
            
            Debug.Log("[LobbyRoom] 클라이언트 대기 패널을 활성화합니다.");
        }
    }

    public void OnStartGameButtonClicked()
    {
        // 싱글톤으로 서버 검증
        if (!NetworkManager.Singleton.IsServer) return;

        var readyNet = FindFirstObjectByType<LobbyRoomNet>(FindObjectsInactive.Include);
        if (readyNet != null)
        {
            readyNet.OnStartGameButtonClicked();
            return;
        }
        
        if (startGameButton != null) 
            startGameButton.interactable = false;

        Debug.Log("[LobbyRoom] 게임 시작! 인게임 씬으로 이동합니다.");
        NetworkManager.Singleton.SceneManager.LoadScene(SceneNames.GameScene, UnityEngine.SceneManagement.LoadSceneMode.Single);
    }
}
