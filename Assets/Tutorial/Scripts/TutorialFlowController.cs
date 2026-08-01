using System.Collections;
using GridSystem;
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
    private const ushort kLocalHostPort = 7777;

    private static TutorialFlowController s_Instance;
    private static bool s_TutorialPending;

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

        // 이전 세션의 Relay 할당 정보가 남아있어도 순수 로컬 호스트로 강제(오프라인에서도 항상 동작).
        var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        if (transport != null)
        {
            transport.SetConnectionData("127.0.0.1", kLocalHostPort);
            NetworkManager.Singleton.NetworkConfig.NetworkTransport = transport;
        }

        s_TutorialPending = true;
        NetworkManager.Singleton.StartHost();

        LobbyRoomNet readyNet = null;
        float timeout = Time.unscaledTime + 6f;
        while (Time.unscaledTime < timeout)
        {
            readyNet = FindFirstObjectByType<LobbyRoomNet>(FindObjectsInactive.Include);
            if (readyNet != null && readyNet.IsSpawned)
                break;
            yield return null;
        }

        if (readyNet == null)
        {
            Debug.LogWarning("[TutorialFlowController] LobbyRoomNet을 찾지 못해 튜토리얼 세션을 시작하지 못했습니다.");
            s_TutorialPending = false;
            yield break;
        }

        // CheckAllPlayersReady가 접속 인원 갱신을 반영할 시간을 한 프레임 더 준다(1인방은 즉시 준비완료 처리됨).
        yield return null;
        yield return null;
        readyNet.OnStartGameButtonClicked();
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
