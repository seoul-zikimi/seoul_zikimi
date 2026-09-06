using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
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
        MaterialDepot.MaterialsChanged += OnDepotSpawned;   // 맵 로드로 목록이 확정되면 HUD 다시 구성
        MaterialDepot.OrdersChanged    += OnOrdersChanged;  // 주문 누적 복제 → 잔량 배지 갱신
    }

    // 게임 플레이 HUD 버튼에만 쫀득 효과를 붙인다. 이 드라이버는 DontDestroyOnLoad라
    // Lobby로 돌아간 뒤에도 살아 있으므로, 씬 제한이 없으면 UI_NEW 버튼에 효과를 다시 붙인다.
    private float m_JuicySweep;
    private GridNetwork m_Net;
    private GameLoopManager m_Loop;
    private void Update()
    {
        UpdateCompletion();
        UpdateOrderBlock();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (GameplayInputBlocker.Blocked) return;
        // [개발자 치트] 0 = 10배속 토글(타이머 빨리감기 등 테스트용). 릴리즈 빌드엔 미포함.
        // 주의: 멀티에선 호스트에서 눌러야 서버 타이머도 빨라짐(클라는 로컬 물리만 가속).
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null && kb.digit0Key.wasPressedThisFrame)
        {
            Time.timeScale = Time.timeScale >= 10f ? 1f : 10f;
            Debug.Log($"[DevCheat] timeScale = {Time.timeScale}x");
        }
        if (kb != null && kb.digit9Key.wasPressedThisFrame)   // 9 = 정답 즉시 100% 완성(완성 연출 테스트)
        {
            var net = FindFirstObjectByType<GridSystem.GridNetwork>();
            if (net != null) net.RequestCheatComplete();
            Debug.Log("[DevCheat] 정답 100% 완성");
        }
        if (kb != null && kb.digit8Key.wasPressedThisFrame)   // 8 = 1개 빼고 완성(≈99%) — 폭죽 오발화 검증
        {
            var net = FindFirstObjectByType<GridSystem.GridNetwork>();
            if (net != null) net.RequestCheatAlmost();
            Debug.Log("[DevCheat] 1개 빼고 완성(≈99%)");
        }
#endif

        if (SceneManager.GetActiveScene().name != SceneNames.GameScene)
            return;

        // 쫀득 버튼 스윕: 상시 1초 폴링 → '동적 UI가 생겼다'는 요청이 온 프레임 + 10초 안전망만.
        // 대부분의 생성처(JobsnailUiKit·AnswerPanelHUD 등)는 자체 Attach라 스윕은 빠뜨린 경로의 자가 치유용.
        m_JuicySweep -= Time.unscaledDeltaTime;
        if (!s_JuicySweepRequested && m_JuicySweep > 0f) return;
        s_JuicySweepRequested = false;
        m_JuicySweep = 10f;
        foreach (var b in FindObjectsByType<UnityEngine.UI.Button>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            JuicyButton.Attach(b);
    }

    private static bool s_JuicySweepRequested = true;   // 씬 진입 첫 프레임 1회
    /// <summary>동적으로 버튼을 만든 쪽이 호출 — 다음 프레임에 스윕 1회(UIManager가 HUD·팝업 생성 시 자동 호출).</summary>
    public static void RequestJuicySweep() => s_JuicySweepRequested = true;
    private void OnDisable()
    {
        MaterialDepot.Spawned   -= OnDepotSpawned;
        MaterialDepot.Despawned -= OnDepotDespawned;
        MaterialDepot.MaterialsChanged -= OnDepotSpawned;
        MaterialDepot.OrdersChanged    -= OnOrdersChanged;
    }

    // 폰 '현재 완성도 : N%' — 2vs2 는 우리 팀 점수, 협동은 공용 점수
    private void UpdateCompletion()
    {
        if (m_OrderHud == null) return;
        if (m_Net == null)  m_Net  = FindFirstObjectByType<GridNetwork>();
        if (m_Loop == null) m_Loop = FindFirstObjectByType<GameLoopManager>();
        if (m_Net == null) return;
        if (m_Loop != null && m_Loop.IsFreeBuild) return;   // 자유 건축: 정답·채점 없음 — 배지는 모드 라벨
        float pct = (m_Loop != null && m_Loop.IsVersus) ? m_Net.ScoreFor(Mathf.Max(0, m_Loop.LocalTeam)).Percent : m_Net.ScorePercent;
        m_OrderHud.SetCompletion(Mathf.RoundToInt(pct));
    }

    // 상대의 '주문 해킹' — 서버가 주문을 막는 동안 폰에 이유와 남은 초를 띄우고 [주문!]을 잠근다.
    private void UpdateOrderBlock()
    {
        if (m_OrderHud == null) return;
        if (m_OrderHud.OrderBlockIcon == null)   // 배너 아이콘 주입(AnswerPanelHUD는 GridSystem을 모른다)
            m_OrderHud.OrderBlockIcon = HeldItemBubble.LoadIcon(SeoulZikimi.Gameplay.CompetitiveItemKind.OrderHack);
        m_OrderHud.SetOrderBlocked(ItemNetwork.LocalOrderBlockedRemaining());
    }

    private AnswerPanelHUD m_OrderHud;   // '시공도면 폰'(정답+주문 통합). 잔량 배지 갱신용 참조 유지
    private readonly List<MapDef> m_PageMaps = new();   // 자유 건축: 폰 건물 페이지 ↔ 맵(3D 뷰 전환용)

    private static AnswerPanelHUD.OrderEntry ToEntry(MaterialDef d) => new AnswerPanelHUD.OrderEntry
    {
        Id = d.Id, Name = d.name, Prefab = d.Prefab, Limit = d.MaxSpawnCount,
        Sub = AnswerHudDriver.ProcLine(d),
    };

    private void OnDepotSpawned(MaterialDepot depot)
    {
        if (UIManager.Instance == null || depot.Catalog == null) return;
        if (m_Loop == null) m_Loop = FindFirstObjectByType<GameLoopManager>();

        m_OrderHud = UIManager.Instance.ShowHUDUI<AnswerPanelHUD>();
        m_OrderHud.PageChanged -= OnOrderPageChanged;   // 재구독(중복 방지)
        m_OrderHud.PageChanged += OnOrderPageChanged;

        bool freeBuild = m_Loop != null && m_Loop.IsFreeBuild;
        m_PageMaps.Clear();
        if (freeBuild && BuildFreeBuildPages(depot, out var pages))
        {
            // 자유 건축: 건물(맵)마다 한 페이지 — 그 건물의 파츠만. 처음 페이지는 지금 서 있는 맵.
            int start = 0;
            var current = MapCatalog.Instance != null ? MapCatalog.Instance.Get(m_Loop.MapIndex) : null;
            for (int i = 0; i < m_PageMaps.Count; i++) if (m_PageMaps[i] == current) { start = i; break; }
            m_OrderHud.SetFreeBuildLook(true);
            m_OrderHud.BuildOrderPages(pages, depot.RequestOrder, start);   // PageChanged → OnOrderPageChanged(3D 뷰 전환)
        }
        else
        {
            // 맵이 정한 주문 목록(MapDef.AvailableMaterials). 비어 있으면 카탈로그 전체가 온다.
            var items = new List<AnswerPanelHUD.OrderEntry>();
            foreach (var d in depot.OrderableMaterials)
                if (d != null) items.Add(ToEntry(d));
            m_OrderHud.SetFreeBuildLook(freeBuild);
            m_OrderHud.BuildOrders(items, depot.RequestOrder);
        }
        OnOrdersChanged(depot);   // 재접속/맵 교체 시 이미 복제된 누적치 즉시 반영
    }

    // 자유 건축 페이지: 고를 수 있는 맵(공터·튜토리얼 제외) 순서대로, 각 맵의 AvailableMaterials 중 실제 주문 가능한 것만.
    // (목록을 비워 둔 맵은 카탈로그 전체 = 창고 목록 전체를 그 페이지에 싣는다.)
    private bool BuildFreeBuildPages(MaterialDepot depot, out List<AnswerPanelHUD.OrderPage> pages)
    {
        pages = new List<AnswerPanelHUD.OrderPage>();
        var catalog = MapCatalog.Instance;
        if (catalog == null) return false;
        var orderable = new HashSet<int>();
        foreach (var d in depot.OrderableMaterials) if (d != null) orderable.Add(d.Id);

        for (int i = 0; i < catalog.Count; i++)
        {
            if (!catalog.IsSelectable(i)) continue;
            var def = catalog.Get(i);
            var source = (def.AvailableMaterials != null && def.AvailableMaterials.Count > 0)
                ? def.AvailableMaterials : depot.OrderableMaterials;
            var items = new List<AnswerPanelHUD.OrderEntry>();
            foreach (var d in source)
                if (d != null && orderable.Contains(d.Id)) items.Add(ToEntry(d));
            if (items.Count == 0) continue;
            pages.Add(new AnswerPanelHUD.OrderPage { Title = def.DisplayName, Items = items });
            m_PageMaps.Add(def);
        }
        return pages.Count > 0;
    }

    // 건물 페이지가 바뀌면 폰 3D 뷰(AnswerPreview 미니씬)를 그 건물의 정답으로 바꾼다.
    private void OnOrderPageChanged(int page)
    {
        if (page < 0 || page >= m_PageMaps.Count) return;
        var preview = FindFirstObjectByType<AnswerPreview>();
        if (preview != null) preview.ShowMapAnswer(m_PageMaps[page]);
    }

    private void OnOrdersChanged(MaterialDepot depot)
    {
        if (m_OrderHud == null) return;
        foreach (var d in depot.OrderableMaterials)
            if (d != null && d.MaxSpawnCount >= 0)
                m_OrderHud.SetRemaining(d.Id, depot.RemainingFor(d.Id));
    }

    private void OnDepotDespawned(MaterialDepot depot)
    {
        if (m_OrderHud != null) m_OrderHud.PageChanged -= OnOrderPageChanged;
        m_PageMaps.Clear();
        m_OrderHud = null;
        if (UIManager.Instance != null) UIManager.Instance.HideHUDUI<AnswerPanelHUD>();
    }
}
