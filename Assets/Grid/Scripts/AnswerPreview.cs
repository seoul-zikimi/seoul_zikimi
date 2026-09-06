using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GridSystem
{
    /// <summary>
    /// 정답 안내(브리프 §6). 플레이 중 '어디에 뭘 지을지' 보여준다:
    ///  ① 실제 그리드 위 진짜 블록 프리팹 반투명 고스트 + 공정 숫자, ② 2D 정답 이미지(있으면), ③ 우하단 3D 미리보기(진짜 블록).
    /// TAB으로 전체 토글. 건축 종료(채점 화면)에선 자동으로 숨긴다. GridManager 와 같은 오브젝트.
    /// </summary>
    [RequireComponent(typeof(GridManager))]
    public class AnswerPreview : MonoBehaviour
    {
        [SerializeField] private Vector3 m_Offset = new Vector3(500f, 500f, 500f);

        private GridManager m_Manager;
        private GameLoopManager m_Loop;
        private Camera m_Cam;
        private RenderTexture m_RT;
        private CameraOrbit m_Orbit;       // 정답 카메라 오빗(플레이어와 동일 로직)
        private Vector3 m_PivotCenter;     // 오빗 중심 = 모델 바운드 중심(팬으로 이동 가능)
        private Vector3 m_HomeCenter;      // 팬 기준점(모델 중심) — 팬 반경 제한용
        private float m_PanLimit;          // 팬 최대 반경
        private GameObject m_Root;        // 미리보기 렌더용(멀리 떨어진 미니씬)
        private GameObject m_GhostRoot;   // 실제 그리드 위 반투명 고스트

        // 미니 프리뷰 호버 픽킹: 오브젝트 단위 AABB(미니씬 월드 좌표) + 재료 정의 + 대상 오브젝트.
        // 콜라이더 없이 수학 픽킹(Bounds.IntersectRay) — 물리 레이캐스트에 안 걸려 게임플레이 무간섭.
        private readonly List<(MaterialDef def, Bounds bounds, GameObject go)> m_PickTargets = new();
        // 호버 테두리: 게임 집기 하이라이트(OutlineHighlight)와 같은 인버티드 헐 셰이더 재사용.
        // 반투명 박스는 RT 알파를 깎아 패널 뒤 월드가 비쳐 보였음 — 불투명 실루엣이라 그 문제가 없다.
        private GameObject m_HoverGo;                                // 현재 테두리 대상(미니씬)
        private readonly List<GameObject> m_HoverOutlines = new();
        private static Material s_OutlineMat;                        // 전 인스턴스 공유 — 누수 없음

        // 선택(클릭) 테두리: 호버와 별개로 유지. 같은 재료의 모든 인스턴스를 감싸 개수 파악을 돕는다.
        private MaterialDef m_SelectedDef;
        private MaterialDef m_HoverDef;      // 미니 프리뷰 호버 중인 재료 — 맵 고스트 강조용
        private readonly List<GameObject> m_SelOutlines = new();
        private readonly List<Material> m_GhostMats = new();      // 고스트 반투명 머티리얼 사본(정리용)
        private readonly List<(GameObject go, int baseY, List<Vector3Int> cells, int materialId, int reqMask, int matStart, int matCount)> m_GhostFloors = new();   // 인월드 고스트 + 기준층 + 정답 셀(완료 판정) + 고스트 머티리얼 구간(강조용)
        private readonly List<bool> m_GhostDone = new();   // 블록별 '알맞게 완료' 캐시(스로틀 갱신)
        private readonly List<Color> m_GhostMatCols = new();   // m_GhostMats와 1:1 — 원본 색(강조 초록에서 복귀용)
        private static readonly Color kGhostHighlight = new Color(1f, 1f, 1f);   // 강조: 흰색. 초록은 커서 프리뷰의 '정답' 색과 겹쳐 헷갈렸다
        private float m_NextDoneCheck;
        // 프리셋 블럭이 실제 그리드에 깔린 걸 한 번이라도 확인했는지(RefreshGhostDone).
        // 프리셋을 스폰하지 않는 맵(SpawnPresetBlocks=false, 광통교류)에선 계속 false라 기존 동작 그대로.
        private bool m_PresetSpawnSeen;
        private GridNetwork m_Net;
        private bool m_Visible = true;
        private bool m_Built;

        // 고스트 재도색 게이트: 숨쉬기 알파를 0.005 단위로 양자화해 값이 실제로 변한 프레임에만
        // 전 머티리얼 SetColor를 돈다(초당 60회 → ~22회). 강조(hl) 변화 프레임엔 원색 복귀를 위해 강제 재도색.
        private int m_LastGaQ = -1;
        private int m_LastHlId = int.MinValue;
        private readonly List<bool> m_GhostActive = new();   // 블록별 SetActive 직전 상태 캐시
        private readonly List<bool> m_GhostShown = new();    // 통짜 맵 전용 — 블록별 Renderer.enabled 직전 상태 캐시
        private bool m_UseWholeGhost;        // 완성체 통짜 고스트 맵(DDP류) — 조각 머티리얼은 렌더러가 꺼져 있음
        private int m_GhostPieceMatCount;    // m_GhostMats에서 조각(비통짜) 구간의 끝
        private float m_GhostAlphaBase = kGhostAlphaBase;   // 이 맵의 고스트 기본 알파(MapDef.GhostAlpha가 있으면 그 값)
        private float m_GhostTintMul = 1f;                  // 이 맵의 고스트 감광 계수(MapDef.GhostTintMul, 1이면 원색)

        /// <summary>모바일 눈 버튼: 폰(TAB)이 닫혀 있어도 인월드 고스트를 계속 보여줄지. 데스크톱은 건드리지 않는다(false).</summary>
        public static bool GhostPinned;
        /// <summary>true면 고스트 표시를 GhostPinned가 단독 결정(모바일 눈 버튼 모드) — MobileControlsHUD가 켠다.
        /// false(데스크톱)면 기존대로 m_Visible(TAB 토글)이 결정. m_Visible이 초기값 true로 남는 모바일에서
        /// (m_Visible || GhostPinned)가 항상 참이 되어 눈 버튼이 무력화되던 버그의 해소.</summary>
        public static bool GhostPinControlled;
        /// <summary>정답 폰 패널이 실제로 펼쳐져 RT가 화면에 보이는지 — AnswerPanelHUD가 접기/펴기 때 넣어준다.
        /// 접혀 있으면 미니씬 카메라를 꺼서 매 프레임 512² 렌더 패스를 없앤다(펴면 즉시 재개).</summary>
        public static bool PanelOpen = true;
        /// <summary>선택 재료가 전부 알맞게 지어져 자동 해제됐을 때(HUD 카드 선택 해제 연동용).</summary>
        public event System.Action SelectionAutoCleared;
        private bool m_LastShow;          // Show() 변화 감지 → VisibilityChanged 1회 발화
        private const int kPreviewLayer = 30;   // 정답 미리보기 전용 레이어(메인 씬과 분리)
        private bool m_MainExcluded;             // 메인 카메라 cullingMask에서 1회 제외

        // 자유 건축 모드: 폰 '건물 페이지'가 고른 맵(현재 맵과 다를 수 있음). null이면 현재 맵의 정답을 그린다.
        // 이 모드는 정답이 없으므로 인월드 고스트는 만들지 않고 미니씬(폰 3D 뷰)만 페이지 건물로 채운다.
        private MapDef m_PageDef;
        private bool m_BuiltWithGhost;           // 마지막 Build가 인월드 고스트를 만들었는지(모드 확정 전 지은 고스트 정리용)
        private bool FreeBuild => m_Loop != null && m_Loop.IsFreeBuild;

        private void Awake()
        {
            m_Manager = GetComponent<GridManager>();
            m_Loop = GetComponent<GameLoopManager>();
        }

        private void Start()
        {
            if (m_Manager != null) m_Manager.OnAnswerChanged += Rebuild;
            Rebuild();
        }

        // 랜덤 정답 선택/재시작으로 정답이 바뀌면 미리보기·고스트를 새로 만든다.
        private void Rebuild()
        {
            if (m_Root != null) { Destroy(m_Root); m_Root = null; }
            if (m_GhostRoot != null) { Destroy(m_GhostRoot); m_GhostRoot = null; }
            m_PickTargets.Clear();
            m_HoverGo = null; m_HoverDef = null; m_HoverOutlines.Clear();   // m_Root 자식이라 같이 파괴됨
            m_SelectedDef = null; m_SelOutlines.Clear();
            m_GhostFloors.Clear();
            m_GhostDone.Clear();
            m_PresetSpawnSeen = false;   // 라운드 재시작 = 프리셋 재스폰 — 다시 확인부터
            m_GhostActive.Clear();
            m_GhostShown.Clear();
            m_LastGaQ = -1; m_LastHlId = int.MinValue;   // 재구성 후 첫 프레임 강제 재도색
            foreach (var m in m_GhostMats) if (m != null) Destroy(m);
            m_GhostMats.Clear();
            m_GhostMatCols.Clear();
            if (m_RT != null) { m_RT.Release(); m_RT = null; }
            m_Cam = null;
            m_Built = false;
            Build();
        }

        private void Update()
        {
            bool show = HudVisible;

            // Start()의 첫 Build는 GameLoopManager 스폰(모드 복제) 전에 돌 수 있다 — 자유 건축으로 확정된 뒤
            // 고스트가 남아 있으면 한 번 다시 짓는다(정답 인덱스가 0→0이면 OnAnswerChanged가 안 와서 스스로 정리).
            if (m_Built && m_BuiltWithGhost && FreeBuild) Rebuild();

            // 2vs2: 팀B의 인월드 고스트는 자기 구역(x+구역폭)에 보여야 한다 — 채점 오프셋(GridNetwork.ScoreAgainst)과 동일 기준.
            // 팀 배정(NetworkList)이 Build보다 늦게 복제될 수 있어 재생성 대신 루트 이동으로 매 프레임 반영한다.
            if (m_GhostRoot != null)
                m_GhostRoot.transform.position =
                    (m_Loop != null && m_Loop.IsVersus && m_Loop.LocalTeam == 1)
                        ? new Vector3(m_Manager.ZoneSize.x * GridContract.Unit, 0f, 0f)
                        : Vector3.zero;

            bool ghost = (GhostPinControlled ? GhostPinned : m_Visible) && m_Built && Building();
            if (m_GhostRoot != null) m_GhostRoot.SetActive(ghost);
            if (ghost)
            {
                RefreshGhostDone();   // 이미 알맞게 지은 블록은 고스트 숨김(시선 정리) — 0.25s 스로틀
                int f = GridContract.LocalBuildFloor;   // 내가 선 층만 → 층끼리 겹쳐 헷갈리던 것 해소(미니 미리보기는 전체 유지)
                // 강조 기준 우선순위: 손에 든 재료 → 미니 프리뷰 호버 → 선택.
                // 재료를 들면 '그 재료를 어디에 놓아야 하는지'가 유일한 관심사라 손이 이긴다.
                // 빈손(또는 도구)일 때만 기존대로 호버/선택 재료를 강조한다.
                int hlId = LocalPlayerHands.HeldMaterialId;
                if (hlId == int.MinValue)
                {
                    var hlDef = m_HoverDef != null ? m_HoverDef : m_SelectedDef;
                    hlId = hlDef != null ? hlDef.Id : int.MinValue;
                }

                float ga = m_GhostAlphaBase + kGhostAlphaPulse * Mathf.Abs(Mathf.Sin(Time.time * 2.2f));   // 은은하게(커서 프리뷰가 주인공) + 숨쉬기
                float ha = 0.45f + 0.15f * Mathf.Abs(Mathf.Sin(Time.time * 5f));     // 강조: 밝고 빠른 펄스
                int gaQ = Mathf.RoundToInt(ga * 200f);                                // 0.005 스텝 — 시각 차 없음
                bool hlChanged = hlId != m_LastHlId;
                if (hlChanged || gaQ != m_LastGaQ)
                {
                    m_LastGaQ = gaQ; m_LastHlId = hlId;
                    float a = gaQ / 200f;
                    // 통짜 고스트 맵은 평소 조각 렌더러가 꺼져 있어 조각 구간 재도색이 순수 낭비 — 통짜 구간만 칠한다.
                    // 단 강조 대상이 바뀐 프레임엔 조각도 칠해야 직전 강조 블록에 남은 초록이 원색으로 돌아간다.
                    int start = (m_UseWholeGhost && !hlChanged) ? m_GhostPieceMatCount : 0;
                    for (int i = start; i < m_GhostMats.Count; i++)
                        if (m_GhostMats[i] != null)
                        {
                            var c = i < m_GhostMatCols.Count ? m_GhostMatCols[i] : m_GhostMats[i].GetColor(s_BaseColor);
                            c.a = a;   // 원본 색으로 복귀(강조 흰색이 남지 않게) + 숨쉬기 알파
                            m_GhostMats[i].SetColor(s_BaseColor, c);
                            m_GhostMats[i].SetColor(s_Color, c);
                        }
                }
                for (int i = 0; i < m_GhostFloors.Count; i++)
                {
                    var it = m_GhostFloors[i];
                    if (it.go == null) continue;
                    bool hl = it.materialId == hlId;
                    // 강조 중엔 층 필터·완료 숨김을 무시하고 보여준다(다른 층·이미 지은 곳도 위치 확인용).
                    bool want = hl || (it.baseY == f && !(i < m_GhostDone.Count && m_GhostDone[i]));
                    if (i >= m_GhostActive.Count) { m_GhostActive.Add(!want); }        // 첫 프레임 강제 적용
                    if (m_GhostActive[i] != want) { m_GhostActive[i] = want; it.go.SetActive(want); }
                    // 통짜 맵(DDP류)은 Build에서 조각 렌더러를 전부 껐다. 강조 대상만 예외로 다시 켜야
                    // 강조가 보인다(강조가 풀리면 도로 꺼서 통짜 한 덩어리 그림을 유지).
                    if (m_UseWholeGhost)
                    {
                        if (i >= m_GhostShown.Count) m_GhostShown.Add(!hl);   // 첫 프레임 강제 적용
                        if (m_GhostShown[i] != hl) { m_GhostShown[i] = hl; SetRenderersEnabled(it.go, hl); }
                    }
                    if (hl)
                        for (int k = it.matStart; k < it.matStart + it.matCount && k < m_GhostMats.Count; k++)
                            if (m_GhostMats[k] != null)
                            {
                                var c = kGhostHighlight; c.a = ha;   // 원색 대신 흰색 — 커서 프리뷰의 초록/빨강과 역할 구분
                                m_GhostMats[k].SetColor(s_BaseColor, c);
                                m_GhostMats[k].SetColor(s_Color, c);
                            }
                }
            }

            // 미니씬 카메라는 폰 패널이 실제로 보일 때만 렌더 — 접힘/정산 중 512² 풀 패스 제거
            if (m_Cam != null)
            {
                bool live = m_Built && Building() && PanelOpen;
                if (m_Cam.enabled != live) m_Cam.enabled = live;
            }
            // 폰 HUD 가시성은 TAB과 무관 — 건축 중이면 표시(접기/펴기는 AnswerPanelHUD의 탭 버튼 담당)
            if (show != m_LastShow) { m_LastShow = show; VisibilityChanged?.Invoke(show); }

            if (!m_MainExcluded && Camera.main != null)   // 메인 뷰에서 미니씬 누출 방지(타이밍 안전)
            {
                Camera.main.cullingMask &= ~(1 << kPreviewLayer);
                m_MainExcluded = true;
            }
        }

        private bool Building() => m_Loop == null || m_Loop.IsBuilding;
        /// <summary>정답 폰 HUD를 띄울 상황인가(건축 중 · 미리보기 준비됨). TAB(고스트 토글)과 무관.</summary>
        private bool HudVisible => m_Built && Building();

        // 블록별 '배치+요구 공정까지 알맞게 완료' 여부 캐시. 채점(ScoreAgainst)과 동일 기준(재료 일치 + RequiredMask 충족).
        private void RefreshGhostDone()
        {
            if (Time.time < m_NextDoneCheck) return;
            m_NextDoneCheck = Time.time + 0.25f;
            if (m_Net == null) m_Net = GetComponent<GridNetwork>();   // 복제 상태(클라 포함)에서 읽는다 — m_Manager.Grid는 서버 전용
            var answer = m_Manager != null ? m_Manager.Answer : null;
            while (m_GhostDone.Count < m_GhostFloors.Count) m_GhostDone.Add(false);
            if (m_Net == null || answer == null) return;
            // 2vs2 팀B: 고스트 루트와 같은 오프셋으로 실제 칸을 조회(채점 오프셋과 동일 기준)
            var offset = (m_Loop != null && m_Loop.IsVersus && m_Loop.LocalTeam == 1)
                ? new Vector3Int(m_Manager.ZoneSize.x, 0, 0) : Vector3Int.zero;
            for (int i = 0; i < m_GhostFloors.Count; i++)
            {
                var it = m_GhostFloors[i];
                bool done = it.cells != null && it.cells.Count > 0;
                if (done)
                    foreach (var c in it.cells)
                    {
                        bool preset = answer.IsPreset(c);
                        if (!m_Net.TryGetCell(c + offset, out int matId, out int completedMask))
                        {
                            // 빈 칸. 프리셋 칸은 '아직 안 깔림(라운드 시작 직후)'과 '불타 사라짐'을 구분해야 한다 —
                            // 한 번이라도 깔린 걸 본 뒤에만 소실로 보고 고스트를 되살린다(경복궁 화마 복구 안내).
                            if (preset && !m_PresetSpawnSeen) continue;
                            done = false; break;
                        }
                        if (preset) { m_PresetSpawnSeen = true; continue; }   // 기본 제공 블럭 칸은 지을 필요 없음
                        if (matId != it.materialId
                            || (completedMask & it.reqMask) != it.reqMask) { done = false; break; }
                    }
                m_GhostDone[i] = done;
            }

            // 편의성: 선택한 재료의 모든 블록이 알맞게 완료되면 선택(흰색 강조)을 자동 해제 — 다 지었는데 계속 반짝이는 것 방지.
            if (m_SelectedDef != null)
            {
                bool any = false, all = true;
                for (int i = 0; i < m_GhostFloors.Count; i++)
                    if (m_GhostFloors[i].materialId == m_SelectedDef.Id)
                    {
                        any = true;
                        if (!(i < m_GhostDone.Count && m_GhostDone[i])) { all = false; break; }
                    }
                if (any && all)
                {
                    ClearSelection();
                    SelectionAutoCleared?.Invoke();
                }
            }
        }

        /// <summary>TAB — 인월드 정답 고스트만 켜고 끈다(폰 HUD는 별도 탭 버튼).</summary>
        public void ToggleVisibility() => m_Visible = !m_Visible;

        /// <summary>자유 건축 모드 — 폰 3D 뷰에 보여줄 건물(맵)을 바꾼다. 폰의 건물 페이지 넘김이 호출(GameHudDriver 경유).
        /// null = 현재 맵. 미니씬만 다시 짓는다(인월드 고스트는 이 모드에서 애초에 없음).</summary>
        public void ShowMapAnswer(MapDef def)
        {
            if (m_PageDef == def) return;
            m_PageDef = def;
            Rebuild();
        }

        /// <summary>맵의 대표 정답(첫 번째 세트). 자유 건축 페이지 뷰는 세트가 여럿이어도 첫 것만 보여준다.</summary>
        private static MapAnswerData FirstAnswer(MapDef def)
            => (def != null && def.Answers != null && def.Answers.Count > 0) ? def.Answers[0] : null;

        private void Build()
        {
            // 자유 건축: 폰 페이지가 고른 맵의 대표 정답을 미니씬에만 그린다(인월드 고스트 없음).
            // 그 외: 현재 맵의 선택된 정답 + 인월드 고스트.
            bool freeBuild = FreeBuild;
            var currentDef = MapDefOrNull();
            var mapDef = (freeBuild && m_PageDef != null) ? m_PageDef : currentDef;
            var answer = (freeBuild && m_PageDef != null) ? FirstAnswer(m_PageDef) : m_Manager.Answer;
            if (answer == null || answer.Cells.Count == 0) return;
            var catalog = m_Manager.Catalog;

            float u = GridContract.Unit;
            var objects = GroupAnswer(answer, catalog);   // 펼쳐 저장된 칸 → 오브젝트(프리팹) 단위 재구성

            // 맵별 고스트 가시성 설정(미설정 = 0이면 공통 기본값). 바닥이 밝은 맵만 여기서 올린다.
            m_GhostAlphaBase = (mapDef != null && mapDef.GhostAlpha > 0f) ? mapDef.GhostAlpha : kGhostAlphaBase;
            m_GhostTintMul   = (mapDef != null && mapDef.GhostTintMul > 0f) ? mapDef.GhostTintMul : 1f;

            // 완성체 통짜 모델은 '현재 맵'일 때만 — 다른 맵의 완성체는 빌드에서 Resources 지연 로드라
            // 페이지를 넘길 때마다 수 십 MB 모델을 끌어오게 된다(iOS 메모리). 다른 맵은 조각 그대로.
            bool useWhole = mapDef != null && mapDef == currentDef && mapDef.CompletedModel != null;

            // ① 실제 그리드 위 = 진짜 블록 프리팹의 '반투명 고스트'(공정색 X) + 공정 숫자 라벨 — 자유 건축은 생략
            m_GhostFloors.Clear();
            m_BuiltWithGhost = !freeBuild;
            if (!freeBuild)
            {
                m_GhostRoot = new GameObject("~AnswerGhost");
                foreach (var o in objects)
                {
                    Vector3 pos = GridCoordinates.CellToWorld(o.minCell);
                    Quaternion rot = Quaternion.Euler(0f, 90f * o.rot, 0f);
                    int matStart = m_GhostMats.Count;
                    var go = MakeBlockVisual(o, m_GhostRoot.transform, pos, rot, u, ghost: true);
                    if (useWhole) HideRenderers(go);        // 통짜 고스트를 대신 세운다(아래) — 층 필터 파편화 방지
                    var fcells = GridFootprint.EnumerateFootprintCells(o.minCell, o.def != null ? o.def.Footprint : Vector3Int.one, o.rot);
                    m_GhostFloors.Add((go, o.minCell.y, fcells, o.def != null ? o.def.Id : -1,
                                       o.def != null ? o.def.RequiredMask : 0,
                                       matStart, m_GhostMats.Count - matStart));   // 기준층 = 그 블록을 놓을 때 플레이어가 서는 층
                }
                // 완성체가 있는 맵은 배치 가이드도 통짜 반투명 하나로. 층 필터를 안 타므로 항상 온전히 보인다.
                m_UseWholeGhost = useWhole;
                m_GhostPieceMatCount = m_GhostMats.Count;   // 여기까지가 조각 구간 — 통짜 머티리얼은 이 뒤에 붙는다
                if (useWhole) ShowCompletedModelInstead(mapDef, m_GhostRoot.transform, ghost: true);
            }
            else { m_UseWholeGhost = false; m_GhostPieceMatCount = 0; }

            // ② 우하단 3D 미리보기 = 진짜 블록 프리팹 솔리드(멀리 떨어진 미니씬 → RenderTexture)
            m_Root = new GameObject("~AnswerPreview");
            Bounds b = default; bool first = true;
            foreach (var o in objects)
            {
                Vector3 pos = m_Offset + GridCoordinates.CellToWorld(o.minCell);
                Quaternion rot = Quaternion.Euler(0f, 90f * o.rot, 0f);
                var go = MakeBlockVisual(o, m_Root.transform, pos, rot, u, ghost: false);
                // pos = 셀 min-corner. Bounds 첫 인자는 '중심'이라 세 축 모두 반칸씩 올려야 한다
                // (X/Z 보정을 빼먹으면 박스가 -dims/2만큼 밀려 카메라 프레이밍이 통째로 치우친다).
                Vector3 half = new Vector3(o.dims.x, o.dims.y, o.dims.z) * (0.5f * u);
                var bb = new Bounds(pos + half, new Vector3(o.dims.x, o.dims.y, o.dims.z) * u);
                if (first) { b = bb; first = false; } else b.Encapsulate(bb);
                m_PickTargets.Add((o.def, RendererBounds(go, bb), go));   // 픽킹은 렌더러 실측 AABB(피벗 규약 무관)
            }

            // 완성체가 지정된 맵(DDP처럼 통짜를 잘라 짓는 맵)은 계획도를 '자르기 전 원본'으로 보여준다.
            // 조각을 그대로 그리면 잘린 단면이 드러나 완성 모습이 매끈하게 안 보이기 때문.
            // 조각 오브젝트는 렌더러만 끄고 남겨 둔다 — 픽킹 AABB와 호버 테두리가 그걸 쓰기 때문.
            if (useWhole && ShowCompletedModelInstead(mapDef, m_Root.transform, ghost: false) != null)
                foreach (var t in m_PickTargets) HideRenderers(t.go);

            m_RT = new RenderTexture(512, 512, 16);
            var camGO = new GameObject("~AnswerPreviewCam");
            camGO.transform.SetParent(m_Root.transform, true);
            m_Cam = camGO.AddComponent<Camera>();
            m_Cam.targetTexture = m_RT;
            // 배경: 검정 단색 대신 맵과 같은 하늘(씬 스카이박스) + 모델 밑 잔디 바닥 — 폰 화면이 어둡지 않게
            m_Cam.clearFlags = CameraClearFlags.Skybox;
            m_Cam.backgroundColor = new Color(0.62f, 0.78f, 0.92f, 1f);   // 스카이박스 없을 때 폴백 하늘색
            m_Cam.fieldOfView = 40f;
            float radius = Mathf.Max(1.5f, b.extents.magnitude + 1f);
            MakeGround(b, radius);
            Vector3 dir = new Vector3(0.8f, 0.9f, -0.8f).normalized;   // 기준 쿼터뷰 방향
            m_PivotCenter = b.center;
            m_HomeCenter = b.center;
            m_PanLimit = radius * 1.2f;
            m_Orbit = new CameraOrbit
            {
                Pitch    = Mathf.Asin(dir.y) * Mathf.Rad2Deg,           // ≈38.5°
                Yaw      = Mathf.Atan2(-dir.x, -dir.z) * Mathf.Rad2Deg, // ≈-45° (Unity Y회전 부호)
                Distance = radius * 2.2f,                               // 기존 정적뷰와 동일 거리
                DistMin  = radius * 1.2f, DistMax = radius * 4f,        // 모델 바운드 기준 줌 한계
                PitchMin = 10f, PitchMax = 85f,
                RotateSpeed = 0.3f, ZoomSpeed = 0.5f,                  // 플레이어와 동일 감도
            };
            RepositionCam();   // 시드 위치 = 기존 정적뷰 재현

            SetLayerRecursive(m_Root, kPreviewLayer);   // 미니씬 전용 레이어
            m_Cam.cullingMask = 1 << kPreviewLayer;      // 정답 카메라는 그 레이어만 렌더(외부 누출 차단)

            m_Built = true;
            Ready?.Invoke(this);   // RT 준비됨 → HUD가 RawImage.texture 갱신
        }

        // 미니씬 바닥(잔디 톤 평면) — 모델 바운드 바닥 높이에 깔고 줌 한계보다 넉넉히 넓게
        private void MakeGround(Bounds b, float radius)
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "~AnswerGround";
            ground.transform.SetParent(m_Root.transform, true);
            ground.transform.position = new Vector3(b.center.x, b.min.y - 0.02f, b.center.z);
            ground.transform.localScale = Vector3.one * Mathf.Max(2f, radius * 1.6f);   // Plane 원형 10유닛
            var col = ground.GetComponent<Collider>();
            if (col != null) Destroy(col);                                               // 픽킹은 바운드 기반 — 콜라이더 불필요
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader != null)
            {
                var mat = new Material(shader);
                var grass = new Color(0.60f, 0.76f, 0.44f, 1f);
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", grass);
                if (mat.HasProperty("_Color")) mat.SetColor("_Color", grass);
                if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0f);
                ground.GetComponent<Renderer>().sharedMaterial = mat;
            }
        }

        // ── HUD 브리지(Assembly-CSharp 드라이버가 구독) ──
        public static event System.Action<AnswerPreview> Ready;      // Build 끝마다(=RT 최신화)
        public static event System.Action<bool> VisibilityChanged;   // 표시/숨김 전환
        public bool IsVisible => HudVisible;

        // ── 인터랙티브 오빗(로컬) — 정답 패널 라우터(Assembly-CSharp)가 호출 ──
        public RenderTexture RT => m_RT;

        /// <summary>미니 프리뷰 배경색 — 모바일 흰색 폰 화면에선 밝은 회색으로 맞춘다.</summary>
        public void SetBackground(Color color)
        {
            if (m_Cam != null) m_Cam.backgroundColor = color;
        }
        public void DriveOrbit(Vector2 rotDelta, float zoom)
        {
            if (!m_Built) return;
            m_Orbit.Integrate(rotDelta, zoom);
            RepositionCam();
        }

        /// <summary>좌클릭 드래그 = 상하좌우 이동(팬). 화면 축 기준으로 시점 중심을 옮긴다(줌 비례 감도).</summary>
        public void DrivePan(Vector2 pixelDelta)
        {
            if (!m_Built || m_Cam == null) return;
            float k = m_Orbit.Distance * 0.0016f;   // 픽셀 → 월드 이동량(멀리서 볼수록 크게)
            Vector3 move = (-m_Cam.transform.right * pixelDelta.x - m_Cam.transform.up * pixelDelta.y) * k;
            // 모델에서 너무 멀어지지 않게 홈 중심 기준 반경 제한
            m_PivotCenter = m_HomeCenter + Vector3.ClampMagnitude(m_PivotCenter + move - m_HomeCenter, m_PanLimit);
            RepositionCam();
        }

        private void RepositionCam()
        {
            if (m_Cam == null) return;
            m_Cam.transform.position = m_Orbit.WorldPosition(m_PivotCenter);
            m_Cam.transform.LookAt(m_PivotCenter);
        }

        // ── 호버 픽킹(로컬) — 정답 패널 라우터가 커서 위치(패널 뷰포트 0~1)로 호출 ──

        /// <summary>커서 아래 블록을 찾아 하이라이트. 잡히면 true + 재료 def(프리팹 없는 블록은 null일 수 있음).</summary>
        public bool TryHover(Vector2 viewportUV, out MaterialDef def)
        {
            def = null;
            if (!m_Built || m_Cam == null) { ClearHover(); return false; }

            var ray = m_Cam.ViewportPointToRay(viewportUV);
            int best = -1; float bestD = float.MaxValue;
            for (int i = 0; i < m_PickTargets.Count; i++)
                if (m_PickTargets[i].bounds.IntersectRay(ray, out float d) && d < bestD)
                { bestD = d; best = i; }

            if (best < 0) { ClearHover(); return false; }

            def = m_PickTargets[best].def;
            m_HoverDef = def;
            SetHoverOutline(m_PickTargets[best].go);
            return true;
        }

        public void ClearHover()
        {
            m_HoverDef = null;
            m_HoverGo = null;
            for (int i = 0; i < m_HoverOutlines.Count; i++)
                if (m_HoverOutlines[i] != null) Destroy(m_HoverOutlines[i]);
            m_HoverOutlines.Clear();
        }

        // 게임 집기 테두리(OutlineHighlight)와 동일한 인버티드 헐: 메쉬를 법선으로 살짝 키워 실루엣만 그린다.
        // OutlineHighlight는 Player 어셈블리(역참조 불가)라 같은 셰이더로 패턴만 복제.
        private void SetHoverOutline(GameObject go)
        {
            if (go == m_HoverGo) return;
            ClearHover();
            m_HoverGo = go;
            if (go != null) AddOutlines(go, m_HoverOutlines);
        }

        // ── 선택(클릭) — HUD 카드/화면 클릭에서 호출. 같은 재료 전체에 테두리 유지 ──

        /// <summary>커서 아래 블록을 선택. 잡히고 재료 정의가 있으면 true(같은 재료 전체 테두리).</summary>
        public bool TrySelectAt(Vector2 viewportUV, out MaterialDef def)
        {
            def = null;
            if (!m_Built || m_Cam == null) return false;
            var ray = m_Cam.ViewportPointToRay(viewportUV);
            int best = -1; float bestD = float.MaxValue;
            for (int i = 0; i < m_PickTargets.Count; i++)
                if (m_PickTargets[i].bounds.IntersectRay(ray, out float d) && d < bestD)
                { bestD = d; best = i; }
            if (best < 0 || m_PickTargets[best].def == null) return false;
            def = m_PickTargets[best].def;
            SelectMaterial(def);
            return true;
        }

        /// <summary>재료 ID로 선택(HUD 카드 클릭 → 드라이버 경유). 없는 ID면 해제.</summary>
        public void SelectMaterialById(int id)
        {
            for (int i = 0; i < m_PickTargets.Count; i++)
                if (m_PickTargets[i].def != null && m_PickTargets[i].def.Id == id)
                { SelectMaterial(m_PickTargets[i].def); return; }
            ClearSelection();
        }

        private void SelectMaterial(MaterialDef def)
        {
            if (def == m_SelectedDef) return;
            ClearSelection();
            m_SelectedDef = def;
            for (int i = 0; i < m_PickTargets.Count; i++)
                if (m_PickTargets[i].def == def) AddOutlines(m_PickTargets[i].go, m_SelOutlines);
        }

        public void ClearSelection()
        {
            m_SelectedDef = null;
            for (int i = 0; i < m_SelOutlines.Count; i++)
                if (m_SelOutlines[i] != null) Destroy(m_SelOutlines[i]);
            m_SelOutlines.Clear();
        }

        private void AddOutlines(GameObject go, List<GameObject> into)
        {
            if (s_OutlineMat == null)
            {
                var sh = Shader.Find("Hidden/PickupOutline");
                if (sh == null) return;
                s_OutlineMat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            }
            foreach (var f in go.GetComponentsInChildren<MeshFilter>())
            {
                if (f.sharedMesh == null) continue;
                if (f.gameObject.name.StartsWith("~")) continue;   // 기존 테두리 헐을 다시 헐 뜨는 것 방지
                var o = new GameObject("~Outline") { layer = kPreviewLayer };   // 미니씬 카메라만 렌더
                o.transform.SetParent(f.transform, false);   // 부모 메쉬에 정확히 겹침
                o.AddComponent<MeshFilter>().sharedMesh = f.sharedMesh;
                o.AddComponent<MeshRenderer>().sharedMaterial = s_OutlineMat;
                into.Add(o);
            }
        }

        private static Bounds RendererBounds(GameObject go, Bounds fallback)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return fallback;
            var b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            return b;
        }

        private static void SetLayerRecursive(GameObject go, int layer)
        {
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        }

        // 에디터 Scene 뷰용(플레이 중엔 위 ① 고스트가 대체)
        private void OnDrawGizmos()
        {
            if (Application.isPlaying || m_Manager == null) return;
            var answer = m_Manager.Answer;
            if (answer == null) return;
            var catalog = m_Manager.Catalog;
            float u = GridContract.Unit;
            foreach (var c in answer.Cells)
            {
                var def = catalog != null ? catalog.GetById(c.materialId) : null;
                Color col = ColorForMask(def != null ? def.RequiredMask : 0);
                Vector3 center = GridCoordinates.CellToWorld(c.cell) + Vector3.one * 0.5f * u;
                Gizmos.color = new Color(col.r, col.g, col.b, 0.18f);
                Gizmos.DrawCube(center, Vector3.one * u * 0.98f);
                Gizmos.color = col;
                Gizmos.DrawWireCube(center, Vector3.one * u * 0.98f);
            }
        }

        private void OnDestroy()
        {
            if (m_Manager != null) m_Manager.OnAnswerChanged -= Rebuild;
            if (m_RT != null) m_RT.Release();
            if (m_Root != null) Destroy(m_Root);
            if (m_GhostRoot != null) Destroy(m_GhostRoot);
            foreach (var m in m_GhostMats) if (m != null) Destroy(m);
        }

        // 런타임 반투명(URP) 머티리얼. 셰이더 없으면 null → 고스트는 불투명 폴백.
        private static Material MakeTransparentMaterial()
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            if (sh == null) return null;
            var m = new Material(sh);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.DisableKeyword("_SURFACE_TYPE_OPAQUE");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
            return m;
        }

        private static Color ColorForMask(int mask)
        {
            if ((mask & (int)ProcessType.Painted) != 0) return new Color(0.30f, 0.85f, 0.40f);
            if ((mask & (int)ProcessType.Fixed) != 0)   return new Color(0.35f, 0.60f, 1.00f);
            return new Color(0.72f, 0.72f, 0.72f);
        }

        // ── 정답 오브젝트(진짜 블록 프리팹) ──
        // 인월드 고스트 알파는 Update()의 숨쉬기(m_GhostAlphaBase + 진폭)가 매 프레임 덮어쓴다.
        // 여기 값들은 그 기준선 — 맵이 밝아 고스트가 묻히면 MapDef.GhostAlpha/GhostTintMul로 맵별로 올린다.
        private const float kGhostAlphaBase = 0.16f;                                           // 고스트 기본 알파(맵 미설정 시)
        private const float kGhostAlphaPulse = 0.05f;                                          // 숨쉬기 진폭
        private static readonly Color kNoPrefabSolid = new Color(0.85f, 0.83f, 0.75f);          // 프리팹 없는 블록(패널)
        private static readonly Color kNoPrefabGhost = new Color(0.85f, 0.83f, 0.75f, 0.30f);   // 프리팹 없는 블록(고스트)

        // 정답 칸(칸 단위로 펼쳐 저장됨)을 footprint로 오브젝트 단위 재구성.
        // EnumerateFootprintCells가 anchor를 항상 min-corner로 정규화 → lex 첫 미점유 셀 = anchor.
        private struct AnsObject { public MaterialDef def; public int rot; public Vector3Int minCell; public Vector3 dims; }

        private static List<AnsObject> GroupAnswer(MapAnswerData answer, MaterialCatalog catalog)
        {
            var objs = new List<AnsObject>();
            var cells = new List<AnswerCell>(answer.Cells);
            cells.Sort((a, c) =>
            {
                if (a.cell.x != c.cell.x) return a.cell.x - c.cell.x;
                if (a.cell.y != c.cell.y) return a.cell.y - c.cell.y;
                return a.cell.z - c.cell.z;
            });
            var claimed = new HashSet<Vector3Int>();
            foreach (var c in cells)
            {
                if (claimed.Contains(c.cell)) continue;
                var def = catalog != null ? catalog.GetById(c.materialId) : null;
                var fp  = def != null ? def.Footprint : Vector3Int.one;
                int rot = c.rotationStep;
                var fcells = GridFootprint.EnumerateFootprintCells(c.cell, fp, rot);

                bool ok = true;
                foreach (var fc in fcells)
                    if (claimed.Contains(fc) || !answer.TryGet(fc, out var ac)
                        || ac.materialId != c.materialId || ac.rotationStep != rot)
                    { ok = false; break; }

                Vector3 dims;
                if (ok)
                {
                    foreach (var fc in fcells) claimed.Add(fc);
                    bool swap = ((((rot % 4) + 4) % 4) % 2) == 1;            // 90°/270° → x/z 치수 스왑
                    dims = new Vector3(swap ? fp.z : fp.x, fp.y, swap ? fp.x : fp.z);
                }
                else { claimed.Add(c.cell); dims = Vector3.one; }            // 데이터 불일치 → 1칸 폴백

                objs.Add(new AnsObject { def = def, rot = rot, minCell = c.cell, dims = dims });
            }
            return objs;
        }

        /// <summary>이 맵의 '완성체 통짜 모델'(없으면 null).</summary>
        private MapDef MapDefOrNull()
        {
            var cat = MapCatalog.Instance;
            var loop = m_Loop != null ? m_Loop : FindFirstObjectByType<GameLoopManager>();
            return (cat != null && loop != null) ? cat.Get(loop.MapIndex) : null;
        }

        /// <summary>맵에 '완성체 통짜 모델'이 지정돼 있으면, 조각 렌더러를 끄고 원본 하나를 대신 세운다.
        /// 지정 안 된 맵(대부분)은 아무것도 하지 않는다.
        ///
        /// <para><paramref name="ghost"/>=true면 인월드 배치 가이드용 반투명. 조각을 그대로 두면
        /// '내가 선 층'만 골라 보여주는 층 필터(m_GhostFloors) 때문에 곡면 껍데기가 파편처럼 보인다 —
        /// 통짜 하나로 세우면 어디에 무엇을 짓는지가 한눈에 들어온다.</para></summary>
        private GameObject ShowCompletedModelInstead(MapDef mapDef, Transform parent, bool ghost)
        {
            if (mapDef == null || mapDef.CompletedModel == null) return null;

            var whole = Instantiate(mapDef.CompletedModel, parent);
            whole.name = ghost ? "~CompletedGhost" : "~CompletedPreview";
            // m_Offset은 미니씬(우하단 3D 뷰) 전용 이동량이다. 인월드 고스트는 실제 그리드 위에 서야 하므로
            // 오프셋을 더하면 안 된다 — 더하면 건축장이 아니라 (500,500,500) 근처 허공에 생긴다.
            Vector3 anchor = GridCoordinates.CellToWorld(mapDef.CompletedModelAnchor);
            whole.transform.SetPositionAndRotation(
                ghost ? anchor : m_Offset + anchor, Quaternion.identity);
            foreach (var col in whole.GetComponentsInChildren<Collider>()) Destroy(col);
            if (ghost) MakeTransparent(whole, m_GhostAlphaBase);
            return whole;
        }

        /// <summary>완성체를 쓰는 맵에서 조각 비주얼의 렌더러만 끈다(픽킹 AABB·테두리는 그대로 살려 둔다).</summary>
        private static void HideRenderers(GameObject go) => SetRenderersEnabled(go, false);

        /// <summary>조각 비주얼의 렌더러 on/off. 통짜 고스트 맵에서 강조 블록만 잠깐 되살릴 때 쓴다.</summary>
        private static void SetRenderersEnabled(GameObject go, bool on)
        {
            if (go == null) return;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.enabled = on;
        }

        // 오브젝트 1개 비주얼. 프리팹 있으면 진짜 블록(고스트=반투명), 없으면 footprint 박스(중립색).
        // 배치는 GridNetwork.SpawnPrefabVisual과 동일: pos = CellToWorld(minCell) = 셀 min-corner(프리팹 피벗=바닥),
        // rot = Euler(0,90·step,0). 중심 피벗을 쓰는 폴백 큐브만 세 축 반칸을 더한다(GridNetwork.cs의 큐브 폴백과 동일).
        private GameObject MakeBlockVisual(AnsObject o, Transform parent, Vector3 pos, Quaternion rot, float u, bool ghost)
        {
            GameObject go;
            if (o.def != null && o.def.Prefab != null)
            {
                go = Instantiate(o.def.Prefab, parent);
                // min-corner + 메시 90° 어긋남 자동 보정. 자유 형상(잘라낸 곡면 조각)은 보정을 끈다 —
                // 조각마다 제멋대로 90° 돌아가면 완공 계획도에서 곡면이 산산조각 나 보인다.
                GridFootprint.PlaceRotatedPrefab(go, pos, o.def.Footprint, o.rot, u, autoYaw: !o.def.FreeformVisual);
                foreach (var col in go.GetComponentsInChildren<Collider>()) Destroy(col);
                if (ghost) MakeTransparent(go, m_GhostAlphaBase);
            }
            else   // 프리팹 없는 재료(Floor/Pillar/Wall 등) → footprint 모양 박스, 공정색 대신 중립색
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.transform.SetParent(parent, true);
                // 큐브=중심 피벗, pos=셀 min-corner → 세 축 모두 반칸 올려야 칸에 정확히 들어앉는다
                go.transform.position = pos + new Vector3(o.dims.x, o.dims.y, o.dims.z) * (0.5f * u);
                go.transform.localScale = new Vector3(o.dims.x, o.dims.y, o.dims.z) * (u * (ghost ? 1f : 0.96f));
                var col = go.GetComponent<Collider>(); if (col != null) Destroy(col);
                if (ghost)
                {
                    var gc = GhostTint(kNoPrefabGhost, m_GhostAlphaBase);
                    var m = MakeTransparentMaterial();
                    if (m != null) { m.SetColor(s_BaseColor, gc); m.SetColor(s_Color, gc); m_GhostMats.Add(m); m_GhostMatCols.Add(gc); }
                    go.GetComponent<Renderer>().sharedMaterial = m;
                }
                else SetColor(go, kNoPrefabSolid);
            }
            return go;
        }

        // 고스트 전용. 원본 셰이더가 투명을 지원 안 해도 항상 반투명이 되도록,
        // '확실히 반투명한' URP Lit 머티리얼을 새로 만들고 원본 텍스처(_BaseMap)+색만 옮긴다. 사본은 m_GhostMats로 정리.
        // 고스트 색: 맵 감광 계수(m_GhostTintMul)를 곱해 어둡게 + 알파 지정.
        // 롯데월드처럼 바닥·모델이 둘 다 밝은 맵은 원색 그대로면 배경에 묻혀 고스트가 안 보인다.
        private Color GhostTint(Color tint, float alpha)
            => new Color(tint.r * m_GhostTintMul, tint.g * m_GhostTintMul, tint.b * m_GhostTintMul, alpha);

        private void MakeTransparent(GameObject go, float alpha)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                var src = r.sharedMaterials;
                var dst = new Material[src.Length];
                for (int i = 0; i < src.Length; i++)
                {
                    var m = MakeTransparentMaterial();
                    if (m == null) { dst[i] = src[i]; continue; }   // 셰이더 없으면 원본 유지
                    Color tint = Color.white;
                    if (src[i] != null)
                    {
                        if      (src[i].HasProperty(s_BaseMap) && src[i].GetTexture(s_BaseMap) != null) m.SetTexture(s_BaseMap, src[i].GetTexture(s_BaseMap));
                        else if (src[i].HasProperty(s_MainTex) && src[i].GetTexture(s_MainTex) != null) m.SetTexture(s_BaseMap, src[i].GetTexture(s_MainTex));
                        else if (src[i].HasProperty(s_GltfMap) && src[i].GetTexture(s_GltfMap) != null) m.SetTexture(s_BaseMap, src[i].GetTexture(s_GltfMap));
                        if      (src[i].HasProperty(s_BaseColor)) tint = src[i].GetColor(s_BaseColor);
                        else if (src[i].HasProperty(s_Color))     tint = src[i].GetColor(s_Color);
                        else if (src[i].HasProperty(s_GltfCol))   tint = src[i].GetColor(s_GltfCol);
                    }
                    tint = GhostTint(tint, alpha);
                    m.SetColor(s_BaseColor, tint);
                    m.SetColor(s_Color, tint);
                    m_GhostMats.Add(m); m_GhostMatCols.Add(tint);
                    dst[i] = m;
                }
                r.sharedMaterials = dst;
            }
        }

        private static readonly int s_BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int s_Color = Shader.PropertyToID("_Color");
        private static readonly int s_BaseMap = Shader.PropertyToID("_BaseMap");
        private static readonly int s_MainTex = Shader.PropertyToID("_MainTex");
        private static readonly int s_GltfMap = Shader.PropertyToID("baseColorTexture");    // glTFast(.glb) 임포트 셰이더
        private static readonly int s_GltfCol = Shader.PropertyToID("baseColorFactor");
        private static void SetColor(GameObject go, Color c)
        {
            var r = go.GetComponent<Renderer>();
            if (r == null) return;
            var mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            mpb.SetColor(s_BaseColor, c);
            mpb.SetColor(s_Color, c);
            r.SetPropertyBlock(mpb);
        }
    }
}
