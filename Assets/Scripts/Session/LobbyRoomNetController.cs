using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using TMPro;

public class LobbyRoomNet : NetworkBehaviour
{
    [Header("UI 연결")]
    public Button readyButton;      // 클라이언트 화면에만 뜰 [준비] 버튼
    public Button startButton;      // 호스트 화면에만 뜰 [게임 시작] 버튼
    public TMP_Text readyStatusText; // (선택) "모든 플레이어가 준비하길 기다리는 중..." 등을 띄울 텍스트

    // 💡 [핵심] 모든 클라이언트가 준비 완료되었는지 서버가 체크해서 동기화하는 네트워크 변수
    private NetworkVariable<bool> m_IsAllReady = new NetworkVariable<bool>(
        false, 
        NetworkVariableReadPermission.Everyone, 
        NetworkVariableWritePermission.Server
    );

    // 서버(호스트)의 메모리에서만 관리할 '준비 완료된 클라이언트 ID' 목록
    private HashSet<ulong> m_ReadyClients = new HashSet<ulong>();
    private bool m_IsLocallyReady = false;

    public override void OnNetworkSpawn()
    {
        // 네트워크 변수 값이 변경될 때 실행할 이벤트 연결
        m_IsAllReady.OnValueChanged += OnAllReadyStatusChanged;

        if (IsHost)
        {
            // 방장(호스트) 세팅
            readyButton.gameObject.SetActive(false); // 호스트는 준비할 필요 없음
            startButton.gameObject.SetActive(true);  
            startButton.interactable = false;       // 처음엔 버튼 비활성화
            
            // 혹시 도중에 누군가 나갔을 때를 대비한 탈주 감지 이벤트
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        }
        else
        {
            // 게스트(클라이언트) 세팅
            readyButton.gameObject.SetActive(true);
            startButton.gameObject.SetActive(false); // 클라이언트에겐 시작 버튼을 숨김
            
            readyButton.onClick.RemoveAllListeners();
            readyButton.onClick.AddListener(ToggleReadyState);
        }

        // 방에 갓 진입했을 때의 초기 UI 갱신
        UpdateUI(m_IsAllReady.Value);
    }

    public override void OnNetworkDespawn()
    {
        m_IsAllReady.OnValueChanged -= OnAllReadyStatusChanged;
        if (IsHost && NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }
    }

    /// <summary>
    /// [클라이언트] 준비 버튼을 누를 때마다 상태 토글
    /// </summary>
    private void ToggleReadyState()
    {
        m_IsLocallyReady = !m_IsLocallyReady;
        
        // 내 버튼 텍스트 변경
        var textText = readyButton.GetComponentInChildren<TMP_Text>();
        if (textText != null) textText.text = m_IsLocallyReady ? "준비 취소" : "준비 완료";

        // 서버(방장)에게 내 무전(ServerRpc)으로 준비 상태를 전송
        SetReadyStatusServerRpc(NetworkManager.Singleton.LocalClientId, m_IsLocallyReady);
    }

    /// <summary>
    /// [서버 RPC] 클라이언트가 보낸 상태를 방장 서버가 받아서 리스트에 추가/삭제
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    private void SetReadyStatusServerRpc(ulong clientId, bool isReady)
    {
        if (isReady)
            m_ReadyClients.Add(clientId);
        else
            m_ReadyClients.Remove(clientId);

        // 모든 클라이언트가 준비 상태인지 검사 시작
        CheckAllPlayersReady();
    }

    /// <summary>
    /// [서버] 호스트를 제외한 모든 클라이언트가 준비를 완료했는지 판단
    /// </summary>
    private void CheckAllPlayersReady()
    {
        if (!IsServer) return;

        // 현재 서버에 접속한 총 인원수 (호스트 포함)
        int totalConnected = NetworkManager.Singleton.ConnectedClients.Count;
        
        // 목표 준비 인원은 (총 인원 - 호스트 1명)
        int targetCount = totalConnected - 1;

        if (targetCount <= 0)
        {
            m_IsAllReady.Value = false; // 혼자 있을 때는 시작 불가
            return;
        }

        // 실제로 준비 완료 버튼을 누른 인원수와 목표 인원수가 같으면 true가 됨!
        m_IsAllReady.Value = m_ReadyClients.Count == targetCount;
    }

    /// <summary>
    /// [서버] 준비했던 클라이언트가 도중에 방에서 나가버렸을 때 예외 처리
    /// </summary>
    private void OnClientDisconnected(ulong clientId)
    {
        if (m_ReadyClients.Contains(clientId))
        {
            m_ReadyClients.Remove(clientId);
        }
        CheckAllPlayersReady();
    }

    // 💡 네트워크 변수(m_IsAllReady)가 바뀌면 호스트/클라이언트 모두에서 자동 실행됨
    private void OnAllReadyStatusChanged(bool previousValue, bool newValue)
    {
        UpdateUI(newValue);
    }

    private void UpdateUI(bool isAllReady)
    {
        // 호스트라면: 모든 클라이언트가 준비되었을 때만 시작 버튼을 활성화
        if (IsHost && startButton != null)
        {
            startButton.interactable = isAllReady;
        }

        // 상태 안내 텍스트 변경 (선택 사항)
        if (readyStatusText != null)
        {
            readyStatusText.text = isAllReady 
                ? "모든 플레이어가 준비되었습니다! 시작 가능." 
                : "다른 플레이어의 준비를 기다리는 중...";
        }
    }

    /// <summary>
    /// 호스트가 [게임 시작] 버튼을 누르면 인게임 씬으로 전환하는 함수
    /// </summary>
    public void OnStartGameButtonClicked()
    {
        if (!IsHost || !m_IsAllReady.Value) return;

        Debug.Log("게임 시작! 인게임 씬으로 다 함께 이동합니다.");
        // 💡 넷코드 환경에서 다 함께 씬을 이동할 때는 NetworkSceneManager를 사용해야 해!
        // NetworkManager.Singleton.SceneManager.LoadScene("YourInGameSceneName", UnityEngine.SceneManagement.LoadSceneMode.Single);
    }
}