using UnityEngine;
using UnityEngine.InputSystem;

namespace GridSystem
{
    /// <summary>
    /// 정답 미리보기(디버그). 정답 셀을 멀리서 큐브로 만들어 전용 카메라로 RenderTexture에 렌더 →
    /// 화면 우하단 박스로 표시. Tab 토글. 색 = 목표 상태(회색=놓임 / 파랑=고정 / 초록=페인트).
    /// (정식 게임 UI는 추후 UIManager HUD 패널로 이관)
    /// </summary>
    [RequireComponent(typeof(GridManager))]
    public class AnswerPreview : MonoBehaviour
    {
        [SerializeField] private int m_BoxSize = 280;
        [SerializeField] private Vector3 m_Offset = new Vector3(500f, 500f, 500f);

        private GridManager m_Manager;
        private Camera m_Cam;
        private RenderTexture m_RT;
        private GameObject m_Root;
        private GUIStyle m_LabelStyle;
        private bool m_Visible = true;
        private bool m_Built;

        private void Awake() => m_Manager = GetComponent<GridManager>();
        private void Start() => Build();

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame)
                m_Visible = !m_Visible;
        }

        private void Build()
        {
            var answer = m_Manager.Answer;
            if (answer == null || answer.Cells.Count == 0) return;
            var catalog = m_Manager.Catalog;

            m_Root = new GameObject("~AnswerPreview");
            Bounds b = default;
            bool first = true;

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
            if (!Application.isPlaying || !m_Built || !m_Visible || m_RT == null) return;
            if (m_LabelStyle == null)
                m_LabelStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, normal = { textColor = Color.white } };

            int s = m_BoxSize;
            var rect = new Rect(Screen.width - s - 14, Screen.height - s - 14, s, s);

            GUI.color = new Color(0f, 0f, 0f, 0.65f);
            GUI.DrawTexture(new Rect(rect.x - 5, rect.y - 24, rect.width + 10, rect.height + 29), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(rect.x, rect.y - 22, rect.width, 20), "정답 (Tab 토글)", m_LabelStyle);
            GUI.DrawTexture(rect, m_RT, ScaleMode.ScaleToFit, false);
        }

        // 실제 그리드 위에 정답 고스트(반투명 색)를 그려 '어디에' 지을지 보여준다.
        private void OnDrawGizmos()
        {
            if (!Application.isPlaying || m_Manager == null) return;
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
            if (m_RT != null) m_RT.Release();
            if (m_Root != null) Destroy(m_Root);
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
