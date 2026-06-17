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
            if (d != null) items.Add(new OrderHUD.Entry(d.Id, d.name));

        UIManager.Instance.ShowHUDUI<OrderHUD>().Build(items, depot.RequestOrder);
    }

    private void OnDepotDespawned(MaterialDepot depot)
    {
        if (UIManager.Instance != null) UIManager.Instance.HideHUDUI<OrderHUD>();
    }
}
