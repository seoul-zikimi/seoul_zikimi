using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using GridSystem;

/// <summary>
/// 게임플레이 HUD 구동기. GridSystem(별도 어셈블리)은 UIManager를 참조 못 하므로,
/// 매니저들이 쏘는 정적 이벤트를 Assembly-CSharp 쪽인 여기서 받아 UIManager HUD로 연결.
/// RuntimeInitialize로 자동 생성·영속 → 씬에 배치 불필요. (1단계: 주문 HUD)
/// </summary>
public class GameHudDriver : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        var go = new GameObject("~GameHudDriver");
        DontDestroyOnLoad(go);
        go.AddComponent<GameHudDriver>();
        EnsureEventSystem();
        EnsureUIManager();
    }

    // Bootstrap에 UIManager가 있으면 그게 영속(DontDestroyOnLoad). 없으면(씬 직접 Play 등) 폴백 생성.
    // Singleton이 중복을 알아서 파괴하므로 안전.
    private static void EnsureUIManager()
    {
        if (UIManager.Instance != null) return;
        new GameObject("UIManager").AddComponent<UIManager>();
    }

    // uGUI 버튼 클릭엔 EventSystem 필요. 신규 입력시스템이라 InputSystemUIInputModule 사용.
    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;
        var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        DontDestroyOnLoad(es);
    }

    private void OnEnable()
    {
        MaterialDepot.Spawned   += OnDepotSpawned;
        MaterialDepot.Despawned += OnDepotDespawned;
    }

    // 모든 버튼 쫀득 통일: 씬 배치·프리팹·코드빌드 가리지 않고 1초마다 훑어 JuicyButton 부착.
    // (Attach는 중복 방지라 멱등 — 새로 생긴 버튼만 실제로 붙음)
    private float m_JuicySweep;
    private void Update()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // [개발자 치트] 0 = 10배속 토글(타이머 빨리감기 등 테스트용). 릴리즈 빌드엔 미포함.
        // 주의: 멀티에선 호스트에서 눌러야 서버 타이머도 빨라짐(클라는 로컬 물리만 가속).
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null && kb.digit0Key.wasPressedThisFrame)
        {
            Time.timeScale = Time.timeScale >= 10f ? 1f : 10f;
            Debug.Log($"[DevCheat] timeScale = {Time.timeScale}x");
        }
#endif

        m_JuicySweep -= Time.unscaledDeltaTime;
        if (m_JuicySweep > 0f) return;
        m_JuicySweep = 1f;
        foreach (var b in FindObjectsByType<UnityEngine.UI.Button>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            JuicyButton.Attach(b);
    }
    private void OnDisable()
    {
        MaterialDepot.Spawned   -= OnDepotSpawned;
        MaterialDepot.Despawned -= OnDepotDespawned;
    }

    private void OnDepotSpawned(MaterialDepot depot)
    {
        if (UIManager.Instance == null || depot.Catalog == null) return;

        var items = new List<OrderHUD.Entry>();
        foreach (var d in depot.Catalog.Materials)
            if (d != null) items.Add(new OrderHUD.Entry(d.Id, d.name, d.Prefab));

        UIManager.Instance.ShowHUDUI<OrderHUD>().Build(items, depot.RequestOrder);
    }

    private void OnDepotDespawned(MaterialDepot depot)
    {
        if (UIManager.Instance != null) UIManager.Instance.HideHUDUI<OrderHUD>();
    }
}
