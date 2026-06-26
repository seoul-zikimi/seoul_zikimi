using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
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
        private Vector3 m_PivotCenter;     // 오빗 중심 = 모델 바운드 중심
        private GameObject m_Root;        // 미리보기 렌더용(멀리 떨어진 미니씬)
        private GameObject m_GhostRoot;   // 실제 그리드 위 반투명 고스트
        private readonly List<Material> m_GhostMats = new();      // 고스트 반투명 머티리얼 사본(정리용)
        private GUIStyle m_LabelStyle;
        private bool m_Visible = true;
        private bool m_Built;
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
            foreach (var m in m_GhostMats) if (m != null) Destroy(m);
            m_GhostMats.Clear();
            if (m_RT != null) { m_RT.Release(); m_RT = null; }
            m_Cam = null;
            m_Built = false;
            Build();
        }

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame)
                m_Visible = !m_Visible;

            bool show = Show();
            if (m_GhostRoot != null) m_GhostRoot.SetActive(show);
            if (show != m_LastShow) { m_LastShow = show; VisibilityChanged?.Invoke(show); }

            if (!m_MainExcluded && Camera.main != null)   // 메인 뷰에서 미니씬 누출 방지(타이밍 안전)
            {
                Camera.main.cullingMask &= ~(1 << kPreviewLayer);
                m_MainExcluded = true;
            }
        }

        private bool Building() => m_Loop == null || m_Loop.IsBuilding;
        private bool Show() => m_Visible && m_Built && Building();

        private void Build()
        {
            var answer = m_Manager.Answer;
            if (answer == null || answer.Cells.Count == 0) return;
            var catalog = m_Manager.Catalog;

            float u = GridContract.Unit;
            var objects = GroupAnswer(answer, catalog);   // 펼쳐 저장된 칸 → 오브젝트(프리팹) 단위 재구성

            // ① 실제 그리드 위 = 진짜 블록 프리팹의 '반투명 고스트'(공정색 X) + 공정 숫자 라벨
            m_GhostRoot = new GameObject("~AnswerGhost");
            foreach (var o in objects)
            {
                Vector3 pos = GridCoordinates.CellToWorld(o.minCell);
                Quaternion rot = Quaternion.Euler(0f, 90f * o.rot, 0f);
                MakeBlockVisual(o, m_GhostRoot.transform, pos, rot, u, ghost: true);
            }

            // ② 우하단 3D 미리보기 = 진짜 블록 프리팹 솔리드(멀리 떨어진 미니씬 → RenderTexture)
            m_Root = new GameObject("~AnswerPreview");
            Bounds b = default; bool first = true;
            foreach (var o in objects)
            {
                Vector3 pos = m_Offset + GridCoordinates.CellToWorld(o.minCell);
                Quaternion rot = Quaternion.Euler(0f, 90f * o.rot, 0f);
                MakeBlockVisual(o, m_Root.transform, pos, rot, u, ghost: false);
                var bb = new Bounds(pos + Vector3.up * (0.5f * o.dims.y * u), new Vector3(o.dims.x, o.dims.y, o.dims.z) * u);
                if (first) { b = bb; first = false; } else b.Encapsulate(bb);
            }

            m_RT = new RenderTexture(512, 512, 16);
            var camGO = new GameObject("~AnswerPreviewCam");
            camGO.transform.SetParent(m_Root.transform, true);
            m_Cam = camGO.AddComponent<Camera>();
            m_Cam.targetTexture = m_RT;
            m_Cam.clearFlags = CameraClearFlags.SolidColor;
            m_Cam.backgroundColor = new Color(0.08f, 0.09f, 0.12f, 1f);
            m_Cam.fieldOfView = 40f;
            float radius = Mathf.Max(1.5f, b.extents.magnitude + 1f);
            Vector3 dir = new Vector3(0.8f, 0.9f, -0.8f).normalized;   // 기준 쿼터뷰 방향
            m_PivotCenter = b.center;
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

        // ── HUD 브리지(Assembly-CSharp 드라이버가 구독) ──
        public static event System.Action<AnswerPreview> Ready;      // Build 끝마다(=RT 최신화)
        public static event System.Action<bool> VisibilityChanged;   // 표시/숨김 전환
        public bool IsVisible => Show();

        // ── 인터랙티브 오빗(로컬) — 정답 패널 라우터(Assembly-CSharp)가 호출 ──
        public RenderTexture RT => m_RT;
        public void DriveOrbit(Vector2 rotDelta, float zoom)
        {
            if (!m_Built) return;
            m_Orbit.Integrate(rotDelta, zoom);
            RepositionCam();
        }

        private void RepositionCam()
        {
            if (m_Cam == null) return;
            m_Cam.transform.position = m_Orbit.WorldPosition(m_PivotCenter);
            m_Cam.transform.LookAt(m_PivotCenter);
        }

        private static void SetLayerRecursive(GameObject go, int layer)
        {
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        }

        private void OnGUI()
        {
            if (!Application.isPlaying || !Show()) return;
            if (m_LabelStyle == null)
                m_LabelStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, normal = { textColor = Color.white } };

            // ③ 2D 정답 이미지(있으면) — 좌상단
            var img = m_Manager.Answer != null ? m_Manager.Answer.AnswerImage : null;
            if (img != null && img.texture != null)
            {
                var ir = new Rect(12, 12, 180, 180);
                Box(new Rect(ir.x - 5, ir.y - 24, ir.width + 10, ir.height + 29), 0.6f);
                GUI.Label(new Rect(ir.x, ir.y - 22, ir.width, 20), "정답 이미지", m_LabelStyle);
                GUI.DrawTexture(ir, img.texture, ScaleMode.ScaleToFit, true);
            }

            // ④ 3D 미리보기·범례는 AnswerPanelHUD(uGUI)로 이전됨.
        }

        private static void Box(Rect r, float a)
        {
            var prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, a);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = prev;
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

        // 오브젝트 1개 비주얼. 프리팹 있으면 진짜 블록(고스트=반투명), 없으면 footprint 박스(중립색).
        // 배치는 GridNetwork.SpawnPrefabVisual과 동일: pos = CellToWorld(minCell)+(dims.x,0,dims.z)*0.5u (피벗=바닥), rot = Euler(0,90·step,0).
        private GameObject MakeBlockVisual(AnsObject o, Transform parent, Vector3 pos, Quaternion rot, float u, bool ghost)
        {
            GameObject go;
            if (o.def != null && o.def.Prefab != null)
            {
                go = Instantiate(o.def.Prefab, parent);
                go.transform.SetPositionAndRotation(pos, rot);
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
