using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace GridSystem
{
    /// <summary>
    /// 정답 안내(브리프 §6). 플레이 중 '어디에 뭘 지을지' 보여준다:
    ///  ① 실제 그리드 위 반투명 고스트(색=요구공정), ② 2D 정답 이미지(있으면), ③ 우하단 3D 미리보기, ④ 색 범례.
    /// TAB으로 전체 토글. 건축 종료(채점 화면)에선 자동으로 숨긴다. GridManager 와 같은 오브젝트.
    /// </summary>
    [RequireComponent(typeof(GridManager))]
    public class AnswerPreview : MonoBehaviour
    {
        [SerializeField] private int m_BoxSize = 240;
        [SerializeField] private Vector3 m_Offset = new Vector3(500f, 500f, 500f);

        private GridManager m_Manager;
        private GameLoopManager m_Loop;
        private Camera m_Cam;
        private RenderTexture m_RT;
        private GameObject m_Root;        // 미리보기 렌더용(멀리 떨어진 미니씬)
        private GameObject m_GhostRoot;   // 실제 그리드 위 반투명 고스트
        private GUIStyle m_LabelStyle;
        private bool m_Visible = true;
        private bool m_Built;

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
            if (m_RT != null) { m_RT.Release(); m_RT = null; }
            m_Cam = null;
            m_Built = false;
            Build();
        }

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame)
                m_Visible = !m_Visible;

            if (m_GhostRoot != null) m_GhostRoot.SetActive(Show());
        }

        private bool Building() => m_Loop == null || m_Loop.IsBuilding;
        private bool Show() => m_Visible && m_Built && Building();

        private void Build()
        {
            var answer = m_Manager.Answer;
            if (answer == null || answer.Cells.Count == 0) return;
            var catalog = m_Manager.Catalog;

            // ① 실제 그리드 위 반투명 고스트
            var ghostMatBase = MakeTransparentMaterial();
            m_GhostRoot = new GameObject("~AnswerGhost");
            float u = GridContract.Unit;
            foreach (var c in answer.Cells)
            {
                var def = catalog != null ? catalog.GetById(c.materialId) : null;
                Color col = ColorForMask(def != null ? def.RequiredMask : 0);
                col.a = 0.22f;

                var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
                g.name = "~Ghost";
                g.transform.SetParent(m_GhostRoot.transform, true);
                g.transform.position = GridCoordinates.CellToWorld(c.cell) + Vector3.one * 0.5f * u;
                g.transform.localScale = Vector3.one * (u * 1.0f);
                var col0 = g.GetComponent<Collider>();
                if (col0 != null) Destroy(col0);
                var rend = g.GetComponent<Renderer>();
                if (ghostMatBase != null) rend.sharedMaterial = ghostMatBase;
                var mpb = new MaterialPropertyBlock();
                rend.GetPropertyBlock(mpb);
                mpb.SetColor(s_BaseColor, col);
                mpb.SetColor(s_Color, col);
                rend.SetPropertyBlock(mpb);
            }

            // ② 우하단 3D 미리보기(멀리 떨어진 미니씬 → RenderTexture)
            m_Root = new GameObject("~AnswerPreview");
            Bounds b = default; bool first = true;
            foreach (var c in answer.Cells)
            {
                Vector3 p = m_Offset + GridCoordinates.CellToWorld(c.cell) + Vector3.one * 0.5f;
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.transform.SetParent(m_Root.transform, true);
                cube.transform.position = p;
                cube.transform.localScale = Vector3.one * 0.92f;
                var col = cube.GetComponent<Collider>();
                if (col != null) Destroy(col);
                var def = catalog != null ? catalog.GetById(c.materialId) : null;
                SetColor(cube, ColorForMask(def != null ? def.RequiredMask : 0));
                if (first) { b = new Bounds(p, Vector3.one); first = false; }
                else b.Encapsulate(new Bounds(p, Vector3.one));
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
            Vector3 dir = new Vector3(0.8f, 0.9f, -0.8f).normalized;
            m_Cam.transform.position = b.center + dir * radius * 2.2f;
            m_Cam.transform.LookAt(b.center);

            m_Built = true;
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

            // ④ 우하단 3D 미리보기 + 색 범례
            int s = m_BoxSize;
            var rect = new Rect(Screen.width - s - 14, Screen.height - s - 14, s, s);
            Box(new Rect(rect.x - 5, rect.y - 24, rect.width + 10, rect.height + 70), 0.65f);
            GUI.Label(new Rect(rect.x, rect.y - 22, rect.width, 20), "정답 (TAB 토글)", m_LabelStyle);
            if (m_RT != null) GUI.DrawTexture(rect, m_RT, ScaleMode.ScaleToFit, false);
            Legend(new Rect(rect.x, rect.y + rect.height + 4, rect.width, 40));
        }

        private void Legend(Rect r)
        {
            float x = r.x;
            Swatch(ref x, r.y, new Color(0.72f, 0.72f, 0.72f), "배치");
            Swatch(ref x, r.y, new Color(0.35f, 0.60f, 1.00f), "고정");
            Swatch(ref x, r.y, new Color(0.30f, 0.85f, 0.40f), "페인트");
        }

        private void Swatch(ref float x, float y, Color c, string label)
        {
            var prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(new Rect(x, y + 2, 14, 14), Texture2D.whiteTexture);
            GUI.color = prev;
            GUI.Label(new Rect(x + 17, y, 60, 18), label, m_LabelStyle);
            x += 78;
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

        private static readonly int s_BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int s_Color = Shader.PropertyToID("_Color");
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
