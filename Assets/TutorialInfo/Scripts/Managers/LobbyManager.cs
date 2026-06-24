using System;
using System.Threading.Tasks;
using Unity.Services.Multiplayer;
using UnityEngine;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core; // 💡 InputField 컴포넌트 접근을 위해 추가

public class LobbyManager : MonoBehaviour
{
    [Header("UI HUD")]
    public GameObject startHUD;
    public GameObject createSessionHUD;
    public GameObject joinCodeHUD;
    public GameObject joinByCodeHUD;
    public GameObject sessionListHUD;
    public GameObject LobbyRoomHUD;

    [Header("Join By Code HUD Elements")]
    // 💡 CreateSessionHUD 오브젝트 내부에 있는 인풋필드와 버튼을 인펙터에서 연결해 주세요!
    public TMP_InputField joinByCodeInputField; 
    public UnityEngine.UI.Button joinByCodeButton;

    // 💡 현재 리스트에서 선택된 비밀방의 ID를 임시 저장하는 변수 (null이면 일반 초대코드 모드)
    private string m_SelectedSessionId = null;
    
    async void Start()
    {
        try
        {
            // 💡 2. [가장 중요] 유니티 멀티플레이어 서비스를 백그라운드에서 깨우는 치트키 코드!
            if (UnityServices.State == ServicesInitializationState.Uninitialized)
            {
                Debug.Log("[LobbyManager] 유니티 서비스 초기화 중...");
                await UnityServices.InitializeAsync();
                Debug.Log("[LobbyManager] 유니티 서비스 초기화 완료!");
            }

            // 💡 3. 초기화 직후, 멀티플레이어를 쓰기 위해 익명 로그인도 함께 처리해 줍니다.
            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
                string playerId = AuthenticationService.Instance.PlayerId ?? "";
                string shortId = playerId.Length <= 5 ? playerId : playerId.Substring(0, 5);
                if (string.IsNullOrEmpty(shortId))
                    shortId = UnityEngine.Random.Range(10000, 99999).ToString();
                string savedName = PlayerPrefs.GetString("PlayerNickname", "").Trim();
                string myName = string.IsNullOrEmpty(savedName) ? $"Guest{shortId}" : savedName;

                await AuthenticationService.Instance.UpdatePlayerNameAsync(myName);
                Debug.Log($"[LobbyManager] 익명 로그인 성공! PlayerID: {AuthenticationService.Instance.PlayerId}");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[LobbyManager] 서비스 초기화 실패: {e.Message}");
            return; // 초기화 실패 시 아래 이벤트 연결을 진행하지 않고 멈춤
        }
        
        startHUD.GetComponent<StartUGUI>().CreateBtnOnClick -= OnActiveCreateSessionHUD;
        startHUD.GetComponent<StartUGUI>().CreateBtnOnClick += OnActiveCreateSessionHUD;
        startHUD.GetComponent<StartUGUI>().JoinBtnOnClick -= OnActiveSessionListHUD;
        startHUD.GetComponent<StartUGUI>().JoinBtnOnClick += OnActiveSessionListHUD;

        CreateSession cs = createSessionHUD.GetComponent<CreateSession>();
        cs.CreateSessioinBtnOnClick -= OnActiveJoinCodeHUD;
        cs.CreateSessioinBtnOnClick += OnActiveJoinCodeHUD;
        joinCodeHUD.GetComponent<ShowJoinCode>().OnDisableJoinCode -= cs.DestroyCurrSession;
        joinCodeHUD.GetComponent<ShowJoinCode>().OnDisableJoinCode += cs.DestroyCurrSession;
        
        // 💡 [새로 연결] 세션 리스트에서 방이 선택되었을 때의 이벤트 연결
        SessionBrowser sb = sessionListHUD.GetComponent<SessionBrowser>();
        sb.OnSessionSelected -= OnRoomSelectedFromList;
        sb.OnSessionSelected += OnRoomSelectedFromList;

        // 💡 [새로 연결] joinByCodeHUD의 확인 버튼 클릭 이벤트를 매니저가 직접 가로챕니다.
        if (joinByCodeButton != null)
        {
            joinByCodeButton.onClick.RemoveAllListeners();
            joinByCodeButton.onClick.AddListener(OnJoinByCodeSubmitClicked);
        }
    }

    /// <summary>
    /// 방 목록에서 방을 선택했을 때 실행되는 함수
    /// </summary>
    private async void OnRoomSelectedFromList(string sessionId, bool hasPassword)
    {
        if (hasPassword)
        {
            // 1. 비밀방인 경우: ID를 기억하고, joinByCodeHUD로 넘겨서 패스워드를 입력받음
            m_SelectedSessionId = sessionId;
            if (joinByCodeInputField != null) joinByCodeInputField.text = ""; // 기존 입력칸 비우기
            
            OnActiveJoinByCodeHUD(true);
        }
        else
        {
            // 2. 공개방인 경우: 묻지도 따지지도 않고 리스트에서 누르자마자 즉시 입장!
            m_SelectedSessionId = null;
            await TryJoinSessionByIdAsync(sessionId, null);
        }
    }

    /// <summary>
    /// joinByCodeHUD의 [입장/확인] 버튼을 눌렀을 때 실행되는 통합 함수
    /// </summary>
    private async void OnJoinByCodeSubmitClicked()
    {
        if (joinByCodeInputField == null) return;
        string inputText = joinByCodeInputField.text;

        // 💡 현재 저장된 세션 ID가 있다면 '비밀방 패스워드 입력 모드'로 작동합니다.
        if (!string.IsNullOrEmpty(m_SelectedSessionId))
        {
            await TryJoinSessionByIdAsync(m_SelectedSessionId, inputText);
        }
        else
        {
            // 💡 저장된 세션 ID가 없다면 원래 기능인 '일반 초대 코드 입력 모드'로 작동합니다.
            await TryJoinSessionByCodeAsync(inputText);
        }
    }

    /// <summary>
    /// [서버 통신] 방의 고유 ID와 패스워드를 이용해 방에 들어가는 함수
    /// </summary>
    private async Task TryJoinSessionByIdAsync(string sessionId, string password)
    {
        try
        {
            Debug.Log($"[LobbyManager] 방 ID로 입장 시도 중... (Password: {password})");
            var sessionBrowser = sessionListHUD.GetComponent<SessionBrowser>();
            
            var joinOptions = new JoinSessionOptions
            {
                Type = sessionBrowser?.SessionSettings?.sessionType,
                Password = string.IsNullOrEmpty(password) ? null : password
            };

            ISession session = await MultiplayerService.Instance.JoinSessionByIdAsync(sessionId, joinOptions);
            if (session != null)
            {
                Debug.Log($"방 ID 입장 성공! 방 이름: {session.Name}");
                m_SelectedSessionId = null; // 모드 초기화
                
                // 전반적인 로비 HUD 비활성화 (인게임 진입 준비)
                OnActiveStartHUD(false); 
                EnterLobbyRoom(session);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"방 ID 입장 실패: {ex.Message}");
            // 필요 시 UI에 "비밀번호가 틀렸습니다" 등의 메시지 처리 가능
        }
    }

    private void EnterLobbyRoom(ISession session)
    {
        OnActiveStartHUD(false);
        
        if (NetworkManager.Singleton != null)
        {
            // 1. 오브젝트에 붙어있는 UnityTransport 컴포넌트를 직접 찾아옵니다.
            UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        
            if (transport != null)
            {
                // 2. NetworkManager의 무전기 칸에 이 컴포넌트를 강제로 꽂아버립니다.
                NetworkManager.Singleton.NetworkConfig.NetworkTransport = transport;
                Debug.Log("[LobbyManager] 코드로 UnityTransport 강제 연결 완료!");
            }
            else
            {
                // 만약 진짜로 컴포넌트가 없다면 에러를 띄웁니다.
                Debug.LogError("[LobbyManager] NetworkManager 오브젝트에 UnityTransport 컴포넌트가 아예 없습니다!");
            }
        }

        if (session.IsHost)
        {
            if (NetworkManager.Singleton != null && !NetworkManager.Singleton.IsListening)
                NetworkManager.Singleton.StartHost();
            else if (NetworkManager.Singleton == null)
                Debug.LogWarning("[LobbyManager] NetworkManager가 없어서 StartHost를 건너뜁니다.");
            
        }
        else
        {
            if (NetworkManager.Singleton != null && !NetworkManager.Singleton.IsListening)
                NetworkManager.Singleton.StartClient();
            else if (NetworkManager.Singleton == null)
                Debug.LogWarning("[LobbyManager] NetworkManager가 없어서 StartClient를 건너뜁니다.");
        }

        if (LobbyRoomHUD != null)
            LobbyRoomHUD.SetActive(true);
    }

    /// <summary>
    /// [서버 통신] 순수 초대 코드를 이용해 방에 들어가는 기존 함수
    /// </summary>
    private async Task TryJoinSessionByCodeAsync(string joinCode)
    {
        try
        {
            Debug.Log($"[LobbyManager] 초대 코드로 입장 시도 중... (Code: {joinCode})");
            var sessionBrowser = sessionListHUD.GetComponent<SessionBrowser>();
            
            var joinOptions = new JoinSessionOptions
            {
                Type = sessionBrowser?.SessionSettings?.sessionType
            };

            ISession session = await MultiplayerService.Instance.JoinSessionByCodeAsync(joinCode, joinOptions);
            if (session != null)
            {
                Debug.Log($"초대 코드 입장 성공! 방 이름: {session.Name}");
                //OnActiveStartHUD(false);

                EnterLobbyRoom(session);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"초대 코드 입장 실패: {ex.Message}");
        }
    }

    private void OnActiveStartHUD(bool active)
    {
        startHUD.SetActive(active);
        createSessionHUD.SetActive(false);
        joinCodeHUD.SetActive(false);
        joinByCodeHUD.SetActive(false);
        sessionListHUD.SetActive(false);
    }

    private void OnActiveCreateSessionHUD(bool active)
    {
        createSessionHUD.SetActive(active);
        startHUD.SetActive(false);
        joinCodeHUD.SetActive(false);
        joinByCodeHUD.SetActive(false);
        sessionListHUD.SetActive(false);
    }
    
    private void OnActiveJoinCodeHUD(bool active)
    {
        if (active)
        {
            if (NetworkManager.Singleton != null && !NetworkManager.Singleton.IsListening)
                NetworkManager.Singleton.StartHost();
            else if (NetworkManager.Singleton == null)
                Debug.LogWarning("[LobbyManager] NetworkManager가 없어서 StartHost를 건너뜁니다.");
            
            if (joinCodeHUD != null) joinCodeHUD.SetActive(false);
            if (LobbyRoomHUD != null) LobbyRoomHUD.SetActive(true);
            if (startHUD != null) startHUD.SetActive(false);
            if (createSessionHUD != null) createSessionHUD.SetActive(false);
            if (joinByCodeHUD != null) joinByCodeHUD.SetActive(false);
            if (sessionListHUD != null) sessionListHUD.SetActive(false);
        }
        
    }

    private void OnActiveJoinByCodeHUD(bool active)
    {
        joinByCodeHUD.SetActive(active);
        startHUD.SetActive(false);
        createSessionHUD.SetActive(false);
        joinCodeHUD.SetActive(false);
        sessionListHUD.SetActive(false);
    }

    private void OnActiveSessionListHUD(bool active)
    {
        // 💡 방 목록 화면으로 오거나 돌아갈 때는 무조건 비밀방 모드를 초기화해 줍니다.
        if (active) m_SelectedSessionId = null;

        sessionListHUD.SetActive(active);
        startHUD.SetActive(false);
        createSessionHUD.SetActive(false);
        joinCodeHUD.SetActive(false);
        joinByCodeHUD.SetActive(false);
    }
}
