using GridSystem;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Player
{
    /// <summary>
    /// 들기 + 클릭 배치/공정(소유자). 한 번에 '재료' 또는 '도구' 하나만 든다(협동 제약).
    /// 1/2/3 재료 · 4 망치(고정) · 5 페인트통 · 좌클릭(재료=배치 / 도구=공정) · C 취소 · R 회전 · Q/E 층 · Space 내려놓기.
    /// (작업장에서 도구 줍기는 I3b. 지금은 키로 들기.)
    /// </summary>
    public class PlayerCarry : NetworkBehaviour
    {
        [SerializeField] private MaterialDef[] m_Palette;
        [SerializeField] private Vector3 m_HoldOffset = new Vector3(0f, 2.2f, 0f);

        private static readonly Key[] s_Digits = { Key.Digit1, Key.Digit2, Key.Digit3 };
        private int m_Rotation;
        private int m_BuildHeight;
        private MaterialDef m_HeldMaterial;
        private ProcessType m_HeldTool;     // None = 도구 없음
        private GameObject m_HeldVisual;

        private Camera m_Cam;
        private GridManager m_Grid;
        private GridNetwork m_Net;
        private Vector3Int m_Target;
        private bool m_HasTarget;
        private GUIStyle m_HudStyle;

        private bool HasMaterial => m_HeldMaterial != null;
        private bool HasTool => m_HeldTool != ProcessType.None;

        public override void OnNetworkSpawn()
        {
            if (!IsOwner) return;
            m_Cam = Camera.main;
        }

        private void Update()
        {
            if (!IsOwner) return;
            if (m_Cam == null) m_Cam = Camera.main;
            if (m_Grid == null) m_Grid = FindFirstObjectByType<GridManager>();   // 씬 전환 뒤 스폰되므로 재탐색
            if (m_Net == null) m_Net = FindFirstObjectByType<GridNetwork>();

            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null || mouse == null) return;

            // 들기 선택
            if (m_Palette != null)
                for (int i = 0; i < m_Palette.Length && i < s_Digits.Length; i++)
                    if (kb[s_Digits[i]].wasPressedThisFrame) HoldMaterial(i);
            if (kb.digit4Key.wasPressedThisFrame) HoldTool(ProcessType.Fixed);
            if (kb.digit5Key.wasPressedThisFrame) HoldTool(ProcessType.Painted);

            if (kb.rKey.wasPressedThisFrame) m_Rotation = (m_Rotation + 1) & 3;
            if (kb.qKey.wasPressedThisFrame) m_BuildHeight = Mathf.Max(0, m_BuildHeight - 1);
            if (kb.eKey.wasPressedThisFrame && m_Grid != null)
                m_BuildHeight = Mathf.Min(m_Grid.GridSize.y - 1, m_BuildHeight + 1);
            if (kb.spaceKey.wasPressedThisFrame) Drop();

            UpdateTarget();

            if (kb.cKey.wasPressedThisFrame && m_HasTarget && m_Net != null)
                m_Net.RequestCancelLast(m_Target);

            if (mouse.leftButton.wasPressedThisFrame)
            {
                if (HasMaterial) TryPlace();
                else if (HasTool) TryApplyTool();
            }

            if (m_HeldVisual != null)
                m_HeldVisual.transform.position = transform.position + m_HoldOffset;
        }

        private void UpdateTarget()
        {
            m_HasTarget = false;
            if (m_Cam == null || m_Grid == null) return;

            float planeY = GridContract.Origin.y + m_BuildHeight * GridContract.Unit;
            var plane = new Plane(Vector3.up, new Vector3(0f, planeY, 0f));
            var ray = m_Cam.ScreenPointToRay(Mouse.current.position.ReadValue());
            if (plane.Raycast(ray, out float d))
            {
                var c = GridCoordinates.WorldToCell(ray.GetPoint(d));
                c.y = m_BuildHeight;
                var s = m_Grid.GridSize;
                m_Target = c;
                m_HasTarget = c.x >= 0 && c.x < s.x && c.z >= 0 && c.z < s.z
                           && m_BuildHeight >= 0 && m_BuildHeight < s.y;
            }
        }

        private void HoldMaterial(int i)
        {
            if (m_Palette == null || i < 0 || i >= m_Palette.Length) return;
            Drop();
            m_HeldMaterial = m_Palette[i];

            m_HeldVisual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var fp = m_HeldMaterial.Footprint;
            m_HeldVisual.transform.localScale =
                new Vector3(Mathf.Max(1, fp.x), Mathf.Max(1, fp.y), Mathf.Max(1, fp.z)) * 0.35f;
            Paint(m_HeldVisual, ColorForMask(m_HeldMaterial.RequiredMask));
            StripCollider(m_HeldVisual);
        }

        private void HoldTool(ProcessType tool)
        {
            Drop();
            m_HeldTool = tool;

            m_HeldVisual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            m_HeldVisual.transform.localScale = Vector3.one * 0.4f;
            Paint(m_HeldVisual, ColorForMask((int)tool));   // 망치=파랑, 페인트=초록
            StripCollider(m_HeldVisual);
        }

        private void Drop()
        {
            if (m_HeldVisual != null) Destroy(m_HeldVisual);
            m_HeldVisual = null;
            m_HeldMaterial = null;
            m_HeldTool = ProcessType.None;
        }

        private void TryPlace()
        {
            if (!m_HasTarget || m_Net == null || m_Grid == null) return;
            var s = m_Grid.GridSize;
            foreach (var cell in GridFootprint.EnumerateFootprintCells(m_Target, m_HeldMaterial.Footprint, m_Rotation))
            {
                if (cell.x < 0 || cell.x >= s.x || cell.y < 0 || cell.y >= s.y || cell.z < 0 || cell.z >= s.z) return;
                if (!m_Net.IsCellFree(cell)) return;
            }
            m_Net.RequestPlace(m_Target, m_HeldMaterial.Id, (byte)m_Rotation);
        }

        private void TryApplyTool()
        {
            if (!m_HasTarget || m_Net == null) return;
            // 서버가 점유/순서 검증. 빈 칸이면 무시됨.
            m_Net.RequestProcess(m_Target, (int)m_HeldTool, true);
        }

        private static Color ColorForMask(int mask)
        {
            if ((mask & (int)ProcessType.Painted) != 0) return new Color(0.30f, 0.85f, 0.40f);
            if ((mask & (int)ProcessType.Fixed) != 0)   return new Color(0.35f, 0.60f, 1.00f);
            return new Color(0.72f, 0.72f, 0.72f);
        }

        private static void Paint(GameObject go, Color c)
        {
            var r = go.GetComponent<Renderer>();
            if (r == null) return;
            var mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            mpb.SetColor(Shader.PropertyToID("_BaseColor"), c);
            mpb.SetColor(Shader.PropertyToID("_Color"), c);
            r.SetPropertyBlock(mpb);
        }

        private static void StripCollider(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
        }

        private void OnDrawGizmos()
        {
            if (!IsOwner || !Application.isPlaying || !m_HasTarget) return;
            Gizmos.color = Color.cyan;
            if (HasMaterial)
                foreach (var c in GridFootprint.EnumerateFootprintCells(m_Target, m_HeldMaterial.Footprint, m_Rotation))
                    Gizmos.DrawWireCube(GridCoordinates.CellToWorld(c) + Vector3.one * 0.5f, Vector3.one * 1.02f);
            else if (HasTool)
                Gizmos.DrawWireCube(GridCoordinates.CellToWorld(m_Target) + Vector3.one * 0.5f, Vector3.one * 1.02f);
        }

        private void OnGUI()
        {
            if (!IsOwner || !Application.isPlaying) return;
            if (m_HudStyle == null)
                m_HudStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, normal = { textColor = Color.white } };

            string held = HasMaterial ? $"재료 id{m_HeldMaterial.Id} (R회전 {m_Rotation})"
                        : HasTool     ? (m_HeldTool == ProcessType.Fixed ? "망치(고정)" : "페인트통(페인트)")
                        :               "빈손 — 1/2/3 재료, 4 망치, 5 페인트";
            string tgt = m_HasTarget ? $"대상 {m_Target}" : "대상 -";
            string score = m_Net != null ? $"점수 {m_Net.ScorePercent:F0}%" : "";
            string text =
                $"[Carry] 들기: {held}\n" +
                $"좌클릭=배치/공정 · C 취소 · R 회전 · Q/E 층 {m_BuildHeight} · Space 버리기\n" +
                $"{tgt}    {score}\n" +
                $"진단: cam={m_Cam != null} grid={m_Grid != null} net={m_Net != null} 대상유효={m_HasTarget}";

            var box = new Rect(10, 174, 680, 100);
            var prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = prev;
            GUI.Label(new Rect(box.x + 8, box.y + 6, box.width - 16, box.height - 12), text, m_HudStyle);
        }
    }
}
