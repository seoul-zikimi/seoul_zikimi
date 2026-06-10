using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GridSystem
{
    /// <summary>
    /// 디버그 그리드 조작(임시). 싱글 = GridManager.Grid에 직접, 멀티 = GridNetwork로 RPC 라우팅.
    /// 1/2/3 재료 · R 회전 · Q/E 층 · 좌클릭 배치 · 우클릭 제거 · F 고정 · G 페인트 · C 취소.
    /// </summary>
    [RequireComponent(typeof(GridManager))]
    public class GridDebugController : MonoBehaviour
    {
        [SerializeField] private MaterialDef[] m_Palette;
        [SerializeField] private int m_BuildHeight = 0;

        private static readonly Key[] s_DigitKeys = { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5 };
        private static readonly int s_BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int s_Color = Shader.PropertyToID("_Color");

        private GridManager m_Manager;
        private GridNetwork m_Net;
        private Camera m_Cam;
        private int m_Selected;
        private int m_Rotation;
        private ulong m_OwnerCounter;
        private readonly Dictionary<ulong, GameObject> m_Visuals = new();
        private GridScore m_Score;
        private GUIStyle m_HudStyle;

        public Vector3Int TargetCell { get; private set; }
        public bool HasTarget { get; private set; }

        private bool Networked => m_Net != null && m_Net.IsSpawned;

        private void Awake()
        {
            m_Manager = GetComponent<GridManager>();
            m_Net = GetComponent<GridNetwork>();
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

            if (!Networked && m_Manager.Answer != null && m_Manager.Grid != null)
                m_Score = m_Manager.Grid.ScoreAgainst(m_Manager.Answer, m_Manager.Catalog);
        }

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
            if (Mouse.current.leftButton.wasPressedThisFrame) TryPlace();
            else if (Mouse.current.rightButton.wasPressedThisFrame) TryRemove();
        }

        private void TryPlace()
        {
            var mat = CurrentMaterial();
            if (mat == null) return;

            if (Networked) { m_Net.RequestPlace(TargetCell, mat.Id, (byte)m_Rotation); return; }

            var grid = m_Manager.Grid;
            if (!grid.CanPlace(TargetCell, mat, m_Rotation))
            {
                Debug.Log($"[Grid] 배치 불가 @ {TargetCell} (겹침/범위초과)");
                return;
            }
            ulong owner = ++m_OwnerCounter;
            grid.Place(TargetCell, mat, m_Rotation, owner);
            SpawnVisual(owner, mat);
        }

        private void TryRemove()
        {
            if (Networked) { m_Net.RequestRemove(TargetCell); return; }

            var grid = m_Manager.Grid;
            var cell = grid.GetCell(TargetCell);
            if (!cell.occupied) return;
            ulong owner = cell.ownerObjectId;
            if (grid.Remove(TargetCell) && m_Visuals.TryGetValue(owner, out var root))
            {
                Destroy(root);
                m_Visuals.Remove(owner);
            }
        }

        // ── 공정 ───────────────────────────────────────────────────────────
        private void HandleProcess()
        {
            if (!HasTarget) return;
            var kb = Keyboard.current;
            if (kb == null) return;

            if (Networked)
            {
                if (kb.fKey.wasPressedThisFrame)      m_Net.RequestProcess(TargetCell, (int)ProcessType.Fixed, true);
                else if (kb.gKey.wasPressedThisFrame) m_Net.RequestProcess(TargetCell, (int)ProcessType.Painted, true);
                else if (kb.cKey.wasPressedThisFrame) m_Net.RequestCancelLast(TargetCell);
                return;
            }

            var grid = m_Manager.Grid;
            if (grid == null) return;
            var cell = grid.GetCell(TargetCell);
            if (!cell.occupied) return;
            var mat = FindMaterial(cell.materialId);

            bool changed = false;
            if (kb.fKey.wasPressedThisFrame)      changed = grid.TryApplyProcess(TargetCell, ProcessType.Fixed, mat);
            else if (kb.gKey.wasPressedThisFrame) changed = grid.TryApplyProcess(TargetCell, ProcessType.Painted, mat);
            else if (kb.cKey.wasPressedThisFrame) changed = CancelLast(grid, TargetCell);

            if (changed)
            {
                var u = grid.GetCell(TargetCell);
                RecolorObject(u.ownerObjectId, u.completedProcessMask);
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

        // ── 로컬 비주얼(싱글 전용; 멀티는 GridNetwork가 그림) ──────────────
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
            SetColor(root, ColorForMask(0));
        }

        private void RecolorObject(ulong owner, int mask)
        {
            if (m_Visuals.TryGetValue(owner, out var root) && root != null)
                SetColor(root, ColorForMask(mask));
        }

        private static Color ColorForMask(int mask)
        {
            if ((mask & (int)ProcessType.Painted) != 0) return new Color(0.30f, 0.85f, 0.40f);
            if ((mask & (int)ProcessType.Fixed) != 0)   return new Color(0.35f, 0.60f, 1.00f);
            return new Color(0.72f, 0.72f, 0.72f);
        }

        private static void SetColor(GameObject root, Color c)
        {
            var mpb = new MaterialPropertyBlock();
            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                r.GetPropertyBlock(mpb);
                mpb.SetColor(s_BaseColor, c);
                mpb.SetColor(s_Color, c);
                r.SetPropertyBlock(mpb);
            }
        }

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

        private void OnGUI()
        {
            if (!Application.isPlaying) return;
            if (m_HudStyle == null)
                m_HudStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, normal = { textColor = Color.white } };

            var mat = CurrentMaterial();
            string matName = mat != null ? $"id{mat.Id} footprint{mat.Footprint}" : "(없음)";
            string scoreLine = Networked
                ? $"\n점수 {m_Net.ScorePercent:F0}% (서버 채점)"
                : m_Manager.Answer != null
                    ? $"\n점수 {m_Score.score}/{m_Score.maxScore}  ({m_Score.Ratio * 100f:F0}%)"
                    : "\n(Answer 미연결)";
            string text =
                $"[{(Networked ? "MULTI" : "SINGLE")}] 재료[{m_Selected}] {matName}\n" +
                $"회전 step {m_Rotation}  (R)\n" +
                $"배치 층 Y = {m_BuildHeight}  (Q/E)\n" +
                $"대상 셀 {(HasTarget ? TargetCell.ToString() : "-")}\n" +
                $"공정: F 고정 · G 페인트 · C 취소" + scoreLine;

            var box = new Rect(10, 10, 580, 158);
            Color prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = prev;
            GUI.Label(new Rect(box.x + 8, box.y + 6, box.width - 16, box.height - 12), text, m_HudStyle);
        }
    }
}
