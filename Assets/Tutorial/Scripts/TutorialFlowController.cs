using System.Collections;
using GridSystem;
using SeoulZikimi.Gameplay;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;

/// <summary>
/// 튜토리얼 진입 흐름: Lobby 최초 진입 시 팝업 표시(+ "다시 보지 않기" PlayerPrefs) → 예 클릭 시
/// 혼자 호스트 세션을 시작해 튜토리얼 맵으로 곧장 이동. 기존 이동/캐릭터/그리드 스크립트는 건드리지 않고
/// LobbyRoomNet/GameLoopManager의 이미 공개된 진입점만 호출한다.
/// 프리팹이 필요: Jobsnail ▸ UI ▸ Generate Tutorial UI Prefabs 실행 후 사용.
/// </summary>
public class TutorialFlowController : MonoBehaviour
{
    private const string kDismissedKey = "TutorialPopupDismissed";
    private const string kTutorialMapDisplayName = "튜토리얼";
    // 0 = OS가 비어있는 포트를 알아서 골라줌. 7777 등 고정 포트는 이전 세션이 안 놓아주면
    // "address already in use"로 호스트 시작 자체가 실패함 — 솔로 튜토리얼은 굳이 고정 포트가 필요 없음.
    private const ushort kLocalHostPort = 0;

    private static TutorialFlowController s_Instance;
    private static bool s_TutorialPending;
    private static int s_PreTutorialMode = (int)GameModeKind.TimeAttack;   // 튜토리얼 진입 전 로비 모드 — 복귀 시 되돌려줌

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        EnsureInstance();
        if (scene.name != SceneNames.Lobby)
            return;
        if (PlayerPrefs.GetInt(kDismissedKey, 0) != 0)
            return;
        ShowPopup();
    }

    private static void EnsureInstance()
    {
        if (s_Instance != null)
            return;
        var go = new GameObject("~TutorialFlowController");
        Object.DontDestroyOnLoad(go);
        s_Instance = go.AddComponent<TutorialFlowController>();
    }

    // Lobby/BootstrapScene은 UIManager 프레임워크를 쓰지 않는 자체 UI 방식(JobsnailUiKit)이라
    // UIManager/EventSystem이 아직 없을 수 있음 — GameLoopHUD의 GameScene 부트스트랩과 동일하게 방어적으로 보장.
    private static void EnsureUiFoundation()
    {
        if (EventSystem.current == null)
        {
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            Object.DontDestroyOnLoad(es);
        }
        if (UIManager.Instance == null)
            new GameObject("UIManager").AddComponent<UIManager>();
    }

    private static void ShowPopup()
    {
        EnsureUiFoundation();
        if (Resources.Load<GameObject>("UI/Popup/ConfirmPopup") == null)
        {
            Debug.LogWarning("[TutorialFlowController] ConfirmPopup 프리팹이 없습니다 — Jobsnail ▸ UI ▸ Generate Tutorial UI Prefabs 실행하세요.");
            return;
        }

        var popup = UIManager.Instance.ShowPopupUI<ConfirmPopup>();
        popup.Setup(
            "처음이시군요!\n조작법을 익힌 후 플레이하는 것을 권장합니다.\n튜토리얼을 플레이하시겠습니까?",
            onYes: BeginTutorial,
            onNo: null,
            showCheckbox: true,
            onCheckboxChanged: dontShowAgain =>
            {
                if (!dontShowAgain) return;
                PlayerPrefs.SetInt(kDismissedKey, 1);
                PlayerPrefs.Save();
            });
    }

    /// <summary>재플레이 진입점(메인 메뉴 설정 팝업의 "튜토리얼 다시 보기" 버튼 등).</summary>
    public static void ReplayTutorial() => BeginTutorial();

    private static void BeginTutorial()
    {
        EnsureInstance();
        s_Instance.StartCoroutine(s_Instance.StartTutorialRoutine());
    }

    /// <summary>GameScene 로드 시 TutorialQuestSequence가 1회 소비 — 이후 일반 게임에는 영향 없음.</summary>
    public static bool ConsumeTutorialFlag()
    {
        if (!s_TutorialPending) return false;
        s_TutorialPending = false;
        return true;
    }

    /// <summary>GameLoopManager.OnNetworkSpawn이 자유 모드를 이미 반영한 뒤(=m_Loop.IsSpawned 확인 후) 호출.
    /// 다음에 로비에서 여는 일반 게임까지 자유 모드로 남지 않도록 원래 모드로 되돌린다.</summary>
    public static void RestorePreTutorialMode() => GameLoopManager.HostSelectedMode = s_PreTutorialMode;

    private IEnumerator StartTutorialRoutine()
    {
        if (NetworkManager.Singleton == null)
        {
            Debug.LogWarning("[TutorialFlowController] NetworkManager가 없어 튜토리얼을 시작할 수 없습니다.");
            yield break;
        }

        if (NetworkManager.Singleton.IsListening)
        {
            NetworkManager.Singleton.Shutdown();
            while (NetworkManager.Singleton.ShutdownInProgress)
                yield return null;
            yield return null;
        }

        // 재플레이 버튼은 BootstrapScene(메인 메뉴)에서도 호출될 수 있음 — LobbyRoomNet은 Lobby 씬 오브젝트이므로 먼저 이동.
        if (SceneManager.GetActiveScene().name != SceneNames.Lobby)
        {
            SceneManager.LoadScene(SceneNames.Lobby);
            while (SceneManager.GetActiveScene().name != SceneNames.Lobby)
                yield return null;
            yield return null;
        }

        LobbyRoomNet.RequiredTotalPlayers = 1;

        int mapIndex = ResolveTutorialMapIndex();
        GameLoopManager.HostSelectedMap = mapIndex;

        // 튜토리얼은 시간제한이 없어야 한다(기획서 "시간제한 X") — 자유 모드(Unlimited)로 강제.
        // 로비가 마지막으로 골랐던 모드는 TutorialQuestSequence가 시작될 때 되돌려 놓는다.
        s_PreTutorialMode = GameLoopManager.HostSelectedMode;
        GameLoopManager.HostSelectedMode = (int)GameModeKind.FreeBuild;

        // 이전 세션의 Relay 할당 정보가 남아있어도 순수 로컬 호스트로 강제(오프라인에서도 항상 동작).
        var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        if (transport != null)
        {
            transport.SetConnectionData("127.0.0.1", kLocalHostPort);
            NetworkManager.Singleton.NetworkConfig.NetworkTransport = transport;
        }

        s_TutorialPending = true;
        NetworkManager.Singleton.StartHost();
        Debug.Log("[TutorialFlowController] StartHost 호출됨 — 호스트 준비를 기다리는 중...");

        // LobbyRoomNet.OnStartGameButtonClicked()은 m_IsAllReady 등 방 상태에 의존해서 타이밍에 취약함.
        // 실제 게임의 '즉시 혼자 시작' 경로(JobsnailLobbySkinner.StartGameFromLobby)와 동일하게,
        // 호스트가 확실히 떴는지만 확인하고 바로 씬을 로드한다(더 단순하고, 이미 검증된 방식).
        float timeout = Time.unscaledTime + 6f;
        while (Time.unscaledTime < timeout)
        {
            if (NetworkManager.Singleton.IsListening && NetworkManager.Singleton.IsServer)
                break;
            yield return null;
        }

        if (!NetworkManager.Singleton.IsListening || !NetworkManager.Singleton.IsServer)
        {
            Debug.LogWarning("[TutorialFlowController] 호스트 시작을 확인하지 못해 튜토리얼 세션을 시작하지 못했습니다.");
            s_TutorialPending = false;
            yield break;
        }

        // 씬 배치 오브젝트(LobbyRoomNet 등) 스폰이 안정될 시간을 한 프레임 더 준다.
        yield return null;
        yield return null;

        // LobbyRoomNet.OnNetworkSpawn()이 자기 자신의(기본값=0번) 맵 인덱스를 GameLoopManager.HostSelectedMap에
        // 덮어쓴다(늦참자 초기 반영 로직) — 그래서 위에서 설정한 값이 스폰 직후 조용히 사라진다.
        // LobbyRoomNet에도 우리 맵을 반영하고, GameLoopManager.HostSelectedMap을 씬 로드 직전에 다시 한번 확정한다.
        var readyNet = FindFirstObjectByType<LobbyRoomNet>(FindObjectsInactive.Include);
        if (readyNet != null) readyNet.HostSelectMap(mapIndex);
        GameLoopManager.HostSelectedMap = mapIndex;

        Debug.Log($"[TutorialFlowController] GameScene 로드 시도 (mapIndex={mapIndex}, readyNet 찾음={readyNet != null})");
        if (NetworkManager.Singleton.SceneManager != null && NetworkManager.Singleton.NetworkConfig.EnableSceneManagement)
            NetworkManager.Singleton.SceneManager.LoadScene(SceneNames.GameScene, LoadSceneMode.Single);
        else
            SceneManager.LoadScene(SceneNames.GameScene, LoadSceneMode.Single);
    }

    private static int ResolveTutorialMapIndex()
    {
        var catalog = MapCatalog.Instance;
        if (catalog == null)
            return 0;
        for (int i = 0; i < catalog.Count; i++)
        {
            var def = catalog.Get(i);
            if (def != null && def.DisplayName == kTutorialMapDisplayName)
                return i;
        }
        Debug.LogWarning($"[TutorialFlowController] MapCatalog에서 '{kTutorialMapDisplayName}' 맵을 찾지 못해 0번 맵으로 대체합니다.");
        return 0;
    }
}
