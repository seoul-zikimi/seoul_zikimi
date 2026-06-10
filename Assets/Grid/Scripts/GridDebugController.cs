using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GridSystem
{
    /// <summary>
    /// 디버그용 그리드 조작 컨트롤러(임시). 실제 들기/도구 상호작용으로 대체 예정.
    /// 타게팅/하이라이트(G3.2) + 배치/회전/제거(G3.3) + 공정 적용/취소·상태 색(G3.4).
    /// 입력(새 Input System):
    ///   1/2/3 재료 · R 회전 · Q/E 층 · 좌클릭 배치 · 우클릭 제거 · F 고정 · G 페인트 · C 취소
    /// </summary>
    [RequireComponent(typeof(GridManager))]
    public class GridDebugController : MonoBehaviour
    {
        [SerializeField] private MaterialDef[] m_Palette;
        [SerializeField] private int m_BuildHeight = 0;

        private static readonly Key[] s_DigitKeys =
            { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5 };
        private static readonly int s_BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int s_Color = Shader.PropertyToID("_Color");

        private GridManager m_Manager;
        private Camera m_Cam;
        private int m_Selected;
        private int m_Rotation;
        private ulong m_OwnerCounter;
        private readonly Dictionary<ulong, GameObject> m_Visuals = new();
        private GridScore m_Score;

        public Vector3Int TargetCell { get; private set; }
        public bool HasTarget { get; private set; }

        private void Awake()
        {
            m_Manager = GetComponent<GridManager>();
            m_Manager.EnsureGrid();
            m_Cam = Camera.main;
        }

        private void Update()
        {
            if (m_Cam == null) m_Cam = Camera.main;
            if (m_Cam == null || Mouse.current == null) return;

            HandleInput();
            UpdateTarget();
            HandlePlaceRemove();
            HandleProcess();

            if (m_Manager.Answer != null && m_Manager.Grid != null)
                m_Score = m_Manager.Grid.ScoreAgainst(m_Manager.Answer, m_Manager.Catalog);
        }

        // ── 입력 ──────────────────────────────────────────────────────────
        private void HandleInput()
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            if (m_Palette != null)
                for (int i = 0; i < m_Palette.Length && i < s_DigitKeys.Length; i++)
                    if (kb[s_DigitKeys[i]].wasPressedThisFrame) m_Selected = i;

            if (kb.rKey.wasPressedThisFrame) m_Rotation = (m_Rotation + 1) & 3;
            if (kb.qKey.wasPressedThisFrame) m_BuildHeight = Mathf.Max(0, m_BuildHeight - 1);
            if (kb.eKey.wasPressedThisFrame) m_BuildHeight = Mathf.Min(m_Manager.GridSize.y - 1, m_BuildHeight + 1);
        }

        private void UpdateTarget()
        {
            float planeY = GridContract.Origin.y + m_BuildHeight * GridContract.Unit;
            var plane = new Plane(Vector3.up, new Vector3(0f, planeY, 0f));
            Ray ray = m_Cam.ScreenPointToRay(Mouse.current.position.ReadValue());

            if (plane.Raycast(ray, out float dist))
            {
                var cell = GridCoordinates.WorldToCell(ray.GetPoint(dist));
                cell.y = m_BuildHeight;
                var size = m_Manager.GridSize;
                TargetCell = cell;
                HasTarget = cell.x >= 0 && cell.x < size.x
                         && cell.z >= 0 && cell.z < size.z
                         && m_BuildHeight >= 0 && m_BuildHeight < size.y;
            }
            else HasTarget = false;
        }

        // ── 배치/제거 ─────────────────────────────────────────────────────
        private void HandlePlaceRemove()
        {
            if (!HasTarget) return;
            var grid = m_Manager.Grid;
            if (grid == null) return;

            if (Mouse.current.leftButton.wasPressedThisFrame) TryPlace(grid);
            else if (Mouse.current.rightButton.wasPressedThisFrame) TryRemove(grid);
        }

        private void TryPlace(RuntimeGrid grid)
        {
            var mat = CurrentMaterial();
            if (mat == null) return;
            if (!grid.CanPlace(TargetCell, mat, m_Rotation))
            {
                Debug.Log($"[Grid] 배치 불가 @ {TargetCell} (겹침/범위초과)");
                return;
            }
            ulong owner = ++m_OwnerCounter;
            grid.Place(TargetCell, mat, m_Rotation, owner);
            SpawnVisual(owner, mat);
        }

        private void TryRemove(RuntimeGrid grid)
        {
            var cell = grid.GetCell(TargetCell);
            if (!cell.occupied) return;
            ulong owner = cell.ownerObjectId;
            if (grid.Remove(TargetCell) && m_Visuals.TryGetValue(owner, out var root))
            {
                Destroy(root);
                m_Visuals.Remove(owner);
            }
        }

        // ── 공정 (G3.4) ───────────────────────────────────────────────────
        private void HandleProcess()
        {
            if (!HasTarget) return;
            var grid = m_Manager.Grid;
            if (grid == null) return;

            var cell = grid.GetCell(TargetCell);
            if (!cell.occupied) return;

            var kb = Keyboard.current;
            if (kb == null) return;
            var mat = FindMaterial(cell.materialId);

            bool changed = false;
            if (kb.fKey.wasPressedThisFrame)      changed = grid.TryApplyProcess(TargetCell, ProcessType.Fixed, mat);
            else if (kb.gKey.wasPressedThisFrame) changed = grid.TryApplyProcess(TargetCell, ProcessType.Painted, mat);
            else if (kb.cKey.wasPressedThisFrame) changed = CancelLast(grid, TargetCell);

            if (changed)
            {
                var updated = grid.GetCell(TargetCell);
                RecolorObject(updated.ownerObjectId, updated.completedProcessMask);
            }
        }

        private bool CancelLast(RuntimeGrid grid, Vector3Int cell)
        {
            int mask = grid.GetCell(cell).completedProcessMask;
            for (int i = ProcessOrder.Sequence.Length - 1; i >= 0; i--)
            {
                var p = ProcessOrder.Sequence[i];
                if ((mask & (int)p) != 0) return grid.TryCancelProcess(cell, p);
            }
            return false;
        }

        // ── 비주얼 ─────────────────────────────────────────────────────────
        private void SpawnVisual(ulong owner, MaterialDef mat)
        {
            var root = new GameObject($"Block_{owner}_mat{mat.Id}");
            root.transform.SetParent(transform, true);
            float u = GridContract.Unit;

            foreach (var c in GridFootprint.EnumerateFootprintCells(TargetCell, mat.Footprint, m_Rotation))
            {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.transform.SetParent(root.transform, true);
                cube.transform.position = GridCoordinates.CellToWorld(c) + Vector3.one * 0.5f * u;
                cube.transform.localScale = Vector3.one * (u * 0.95f);
                var col = cube.GetComponent<Collider>();
                if (col != null) Destroy(col);
            }
            m_Visuals[owner] = root;
            SetColor(root, ColorForMask(0)); // 놓임 = 회색
        }

        private void RecolorObject(ulong owner, int mask)
        {
            if (m_Visuals.TryGetValue(owner, out var root) && root != null)
                SetColor(root, ColorForMask(mask));
        }

        private static Color ColorForMask(int mask)
        {
            if ((mask & (int)ProcessType.Painted) != 0) return new Color(0.30f, 0.85f, 0.40f); // 페인트=초록
            if ((mask & (int)ProcessType.Fixed) != 0)   return new Color(0.35f, 0.60f, 1.00f); // 고정=파랑
            return new Color(0.72f, 0.72f, 0.72f);                                             // 놓임=회색
        }

        private static void SetColor(GameObject root, Color c)
        {
            var mpb = new MaterialPropertyBlock();
            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                r.GetPropertyBlock(mpb);
                mpb.SetColor(s_BaseColor, c); // URP
                mpb.SetColor(s_Color, c);     // Built-in 대비
                r.SetPropertyBlock(mpb);
            }
        }

        // ── 헬퍼 / HUD ─────────────────────────────────────────────────────
        private MaterialDef CurrentMaterial()
            => (m_Palette != null && m_Selected >= 0 && m_Selected < m_Palette.Length) ? m_Palette[m_Selected] : null;

        private MaterialDef FindMaterial(int id)
        {
            if (m_Palette == null) return null;
            foreach (var d in m_Palette) if (d != null && d.Id == id) return d;
            return null;
        }

        private void OnDrawGizmos()
        {
            if (!Application.isPlaying || !HasTarget) return;
            float u = GridContract.Unit;
            var mat = CurrentMaterial();
            Gizmos.color = Color.yellow;

            if (mat != null)
                foreach (var c in GridFootprint.EnumerateFootprintCells(TargetCell, mat.Footprint, m_Rotation))
                    Gizmos.DrawWireCube(GridCoordinates.CellToWorld(c) + Vector3.one * 0.5f * u, Vector3.one * u * 1.02f);
            else
                Gizmos.DrawWireCube(GridCoordinates.CellToWorld(TargetCell) + Vector3.one * 0.5f * u, Vector3.one * u * 1.02f);
        }

        private GUIStyle m_HudStyle;

        private void OnGUI()
        {
            if (!Application.isPlaying) return;
            if (m_HudStyle == null)
                m_HudStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, normal = { textColor = Color.white } };

            var mat = CurrentMaterial();
            string matName = mat != null ? $"id{mat.Id} footprint{mat.Footprint}" : "(없음)";
            string score = m_Manager.Answer != null
                ? $"\n점수 {m_Score.score}/{m_Score.maxScore}  ({m_Score.Ratio * 100f:F0}%)"
                : "\n(GridManager에 Answer 미연결)";
            string text =
                $"재료[{m_Selected}] {matName}\n" +
                $"회전 step {m_Rotation}  (R)\n" +
                $"배치 층 Y = {m_BuildHeight}  (Q/E)\n" +
                $"대상 셀 {(HasTarget ? TargetCell.ToString() : "-")}\n" +
                $"공정: F 고정 · G 페인트 · C 취소" + score;

            var box = new Rect(10, 10, 580, 158);
            Color prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);   // 어두운 배경
            GUI.color = prev;
            GUI.Label(new Rect(box.x + 8, box.y + 6, box.width - 16, box.height - 12), text, m_HudStyle);
        }
    }
}
