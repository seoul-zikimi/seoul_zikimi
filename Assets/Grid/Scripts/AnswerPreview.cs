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
        private readonly List<GameObject> m_SelOutlines = new();
        private readonly List<Material> m_GhostMats = new();      // 고스트 반투명 머티리얼 사본(정리용)
        private readonly List<(GameObject go, int baseY, List<Vector3Int> cells, int materialId, int reqMask)> m_GhostFloors = new();   // 인월드 고스트 + 기준층 + 정답 셀(완료 판정용)
        private readonly List<bool> m_GhostDone = new();   // 블록별 '알맞게 완료' 캐시(스로틀 갱신)
        private float m_NextDoneCheck;
        private GridNetwork m_Net;
        private bool m_Visible = true;
        private bool m_Built;

        /// <summary>모바일 눈 버튼: 폰(TAB)이 닫혀 있어도 인월드 고스트를 계속 보여줄지. 데스크톱은 건드리지 않는다(false).</summary>
        public static bool GhostPinned;
        private bool m_LastShow;          // Show() 변화 감지 → VisibilityChanged 1회 발화
        private const int kPreviewLayer = 30;   // 정답 미리보기 전용 레이어(메인 씬과 분리)
        private bool m_MainExcluded;             // 메인 카메라 cullingMask에서 1회 제외

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
            m_HoverGo = null; m_HoverOutlines.Clear();   // m_Root 자식이라 같이 파괴됨
            m_SelectedDef = null; m_SelOutlines.Clear();
            m_GhostFloors.Clear();
            m_GhostDone.Clear();
            foreach (var m in m_GhostMats) if (m != null) Destroy(m);
            m_GhostMats.Clear();
            if (m_RT != null) { m_RT.Release(); m_RT = null; }
            m_Cam = null;
            m_Built = false;
            Build();
        }

        private void Update()
        {
            bool show = Show();

            // 2vs2: 팀B의 인월드 고스트는 자기 구역(x+구역폭)에 보여야 한다 — 채점 오프셋(GridNetwork.ScoreAgainst)과 동일 기준.
            // 팀 배정(NetworkList)이 Build보다 늦게 복제될 수 있어 재생성 대신 루트 이동으로 매 프레임 반영한다.
            if (m_GhostRoot != null)
                m_GhostRoot.transform.position =
                    (m_Loop != null && m_Loop.IsVersus && m_Loop.LocalTeam == 1)
                        ? new Vector3(m_Manager.ZoneSize.x * GridContract.Unit, 0f, 0f)
                        : Vector3.zero;

            bool ghost = show || (GhostPinned && m_Built && Building());
            if (m_GhostRoot != null) m_GhostRoot.SetActive(ghost);
            if (ghost)
            {
                RefreshGhostDone();   // 이미 알맞게 지은 블록은 고스트 숨김(시선 정리) — 0.25s 스로틀
                int f = GridContract.LocalBuildFloor;   // 내가 선 층만 → 층끼리 겹쳐 헷갈리던 것 해소(미니 미리보기는 전체 유지)
                for (int i = 0; i < m_GhostFloors.Count; i++)
                    if (m_GhostFloors[i].go != null)
                        m_GhostFloors[i].go.SetActive(m_GhostFloors[i].baseY == f && !(i < m_GhostDone.Count && m_GhostDone[i]));

                float ga = 0.16f + 0.05f * Mathf.Abs(Mathf.Sin(Time.time * 2.2f));   // 더 은은하게(커서 프리뷰가 주인공) + 숨쉬기
                for (int i = 0; i < m_GhostMats.Count; i++)
                    if (m_GhostMats[i] != null)
                    {
                        var c = m_GhostMats[i].GetColor(s_BaseColor); c.a = ga;
                        m_GhostMats[i].SetColor(s_BaseColor, c);
                        m_GhostMats[i].SetColor(s_Color, c);
                    }
            }
            if (show != m_LastShow) { m_LastShow = show; VisibilityChanged?.Invoke(show); }

            if (!m_MainExcluded && Camera.main != null)   // 메인 뷰에서 미니씬 누출 방지(타이밍 안전)
            {
                Camera.main.cullingMask &= ~(1 << kPreviewLayer);
                m_MainExcluded = true;
            }
        }

        private bool Building() => m_Loop == null || m_Loop.IsBuilding;
        private bool Show() => m_Visible && m_Built && Building();

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
                        if (answer.IsPreset(c)) continue;   // 기본 제공 블럭 칸은 지을 필요 없음
                        if (!m_Net.TryGetCell(c + offset, out int matId, out int completedMask)
                            || matId != it.materialId
                            || (completedMask & it.reqMask) != it.reqMask) { done = false; break; }
                    }
                m_GhostDone[i] = done;
            }
        }

        /// <summary>키보드·패드·모바일 주문 버튼이 공통으로 호출하는 UI 비의존 토글.</summary>
        public void ToggleVisibility() => m_Visible = !m_Visible;

        private void Build()
        {
            var answer = m_Manager.Answer;
            if (answer == null || answer.Cells.Count == 0) return;
            var catalog = m_Manager.Catalog;

            float u = GridContract.Unit;
            var objects = GroupAnswer(answer, catalog);   // 펼쳐 저장된 칸 → 오브젝트(프리팹) 단위 재구성

            // ① 실제 그리드 위 = 진짜 블록 프리팹의 '반투명 고스트'(공정색 X) + 공정 숫자 라벨
            m_GhostRoot = new GameObject("~AnswerGhost");
            m_GhostFloors.Clear();
            bool useWhole = MapDefOrNull()?.CompletedModel != null;
            foreach (var o in objects)
            {
                Vector3 pos = GridCoordinates.CellToWorld(o.minCell);
                Quaternion rot = Quaternion.Euler(0f, 90f * o.rot, 0f);
                var go = MakeBlockVisual(o, m_GhostRoot.transform, pos, rot, u, ghost: true);
                if (useWhole) HideRenderers(go);        // 통짜 고스트를 대신 세운다(아래) — 층 필터 파편화 방지
                var fcells = GridFootprint.EnumerateFootprintCells(o.minCell, o.def != null ? o.def.Footprint : Vector3Int.one, o.rot);
                m_GhostFloors.Add((go, o.minCell.y, fcells, o.def != null ? o.def.Id : -1,
                                   o.def != null ? o.def.RequiredMask : 0));   // 기준층 = 그 블록을 놓을 때 플레이어가 서는 층
            }
            // 완성체가 있는 맵은 배치 가이드도 통짜 반투명 하나로. 층 필터를 안 타므로 항상 온전히 보인다.
            ShowCompletedModelInstead(m_GhostRoot.transform, ghost: true);

            // ② 우하단 3D 미리보기 = 진짜 블록 프리팹 솔리드(멀리 떨어진 미니씬 → RenderTexture)
            m_Root = new GameObject("~AnswerPreview");
            Bounds b = default; bool first = true;
            foreach (var o in objects)
            {
                Vector3 pos = m_Offset + GridCoordinates.CellToWorld(o.minCell);
                Quaternion rot = Quaternion.Euler(0f, 90f * o.rot, 0f);
                var go = MakeBlockVisual(o, m_Root.transform, pos, rot, u, ghost: false);
                var bb = new Bounds(pos + Vector3.up * (0.5f * o.dims.y * u), new Vector3(o.dims.x, o.dims.y, o.dims.z) * u);
                if (first) { b = bb; first = false; } else b.Encapsulate(bb);
                m_PickTargets.Add((o.def, RendererBounds(go, bb), go));   // 픽킹은 렌더러 실측 AABB(피벗 규약 무관)
            }

            // 완성체가 지정된 맵(DDP처럼 통짜를 잘라 짓는 맵)은 계획도를 '자르기 전 원본'으로 보여준다.
            // 조각을 그대로 그리면 잘린 단면이 드러나 완성 모습이 매끈하게 안 보이기 때문.
            // 조각 오브젝트는 렌더러만 끄고 남겨 둔다 — 픽킹 AABB와 호버 테두리가 그걸 쓰기 때문.
            if (ShowCompletedModelInstead(m_Root.transform, ghost: false) != null)
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
        public bool IsVisible => Show();

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
            SetHoverOutline(m_PickTargets[best].go);
            return true;
        }

        public void ClearHover()
        {
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
        private const float kGhostAlpha = 0.4f;                                                // 프리팹 고스트 투명도
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
        private GameObject ShowCompletedModelInstead(Transform parent, bool ghost)
        {
            var mapDef = MapDefOrNull();
            if (mapDef == null || mapDef.CompletedModel == null) return null;

            var whole = Instantiate(mapDef.CompletedModel, parent);
            whole.name = ghost ? "~CompletedGhost" : "~CompletedPreview";
            whole.transform.SetPositionAndRotation(
                m_Offset + GridCoordinates.CellToWorld(mapDef.CompletedModelAnchor), Quaternion.identity);
            foreach (var col in whole.GetComponentsInChildren<Collider>()) Destroy(col);
            if (ghost) MakeTransparent(whole, kGhostAlpha);
            return whole;
        }

        /// <summary>완성체를 쓰는 맵에서 조각 비주얼의 렌더러만 끈다(픽킹 AABB·테두리는 그대로 살려 둔다).</summary>
        private static void HideRenderers(GameObject go)
        {
            if (go == null) return;
            foreach (var r in go.GetComponentsInChildren<Renderer>()) r.enabled = false;
        }

        // 오브젝트 1개 비주얼. 프리팹 있으면 진짜 블록(고스트=반투명), 없으면 footprint 박스(중립색).
        // 배치는 GridNetwork.SpawnPrefabVisual과 동일: pos = CellToWorld(minCell)+(dims.x,0,dims.z)*0.5u (피벗=바닥), rot = Euler(0,90·step,0).
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
                if (ghost) MakeTransparent(go, kGhostAlpha);
            }
            else   // 프리팹 없는 재료(Floor/Pillar/Wall 등) → footprint 모양 박스, 공정색 대신 중립색
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.transform.SetParent(parent, true);
                go.transform.position = pos + Vector3.up * (0.5f * o.dims.y * u);   // 큐브=중심 피벗 → 셀 바닥에 안착하도록 올림
                go.transform.localScale = new Vector3(o.dims.x, o.dims.y, o.dims.z) * (u * (ghost ? 1f : 0.96f));
                var col = go.GetComponent<Collider>(); if (col != null) Destroy(col);
                if (ghost)
                {
                    var m = MakeTransparentMaterial();
                    if (m != null) { m.SetColor(s_BaseColor, kNoPrefabGhost); m.SetColor(s_Color, kNoPrefabGhost); m_GhostMats.Add(m); }
                    go.GetComponent<Renderer>().sharedMaterial = m;
                }
                else SetColor(go, kNoPrefabSolid);
            }
            return go;
        }

        // 고스트 전용. 원본 셰이더가 투명을 지원 안 해도 항상 반투명이 되도록,
        // '확실히 반투명한' URP Lit 머티리얼을 새로 만들고 원본 텍스처(_BaseMap)+색만 옮긴다. 사본은 m_GhostMats로 정리.
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
                    m_GhostMats.Add(m);

                    Color tint = Color.white;
                    if (src[i] != null)
                    {
                        if      (src[i].HasProperty(s_BaseMap)) m.SetTexture(s_BaseMap, src[i].GetTexture(s_BaseMap));
                        else if (src[i].HasProperty(s_MainTex)) m.SetTexture(s_BaseMap, src[i].GetTexture(s_MainTex));
                        if      (src[i].HasProperty(s_BaseColor)) tint = src[i].GetColor(s_BaseColor);
                        else if (src[i].HasProperty(s_Color))     tint = src[i].GetColor(s_Color);
                    }
                    tint.a = alpha;
                    m.SetColor(s_BaseColor, tint);
                    m.SetColor(s_Color, tint);
                    dst[i] = m;
                }
                r.sharedMaterials = dst;
            }
        }

        private static readonly int s_BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int s_Color = Shader.PropertyToID("_Color");
        private static readonly int s_BaseMap = Shader.PropertyToID("_BaseMap");
        private static readonly int s_MainTex = Shader.PropertyToID("_MainTex");
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
