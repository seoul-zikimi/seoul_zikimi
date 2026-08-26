using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;

/// <summary>
/// 어느 씬·어느 진입 경로로 시작해도 EventSystem을 보장한다.
/// 기존엔 JobsnailMainMenu/IntroCutscene만 만들어서, 메인 메뉴를 안 거치는 흐름
/// (MPPM 가상 플레이어 자동 시작 등)에선 EventSystem이 없어 UI 클릭이 전부 죽었다.
/// </summary>
public static class EventSystemBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Init()
    {
        Ensure();
        SceneManager.sceneLoaded += (_, _) => Ensure();   // 씬 교체로 사라져도 복구
    }

    private static void Ensure()
    {
        if (EventSystem.current != null || Object.FindFirstObjectByType<EventSystem>() != null)
            return;
        var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        Object.DontDestroyOnLoad(es);
    }
}
