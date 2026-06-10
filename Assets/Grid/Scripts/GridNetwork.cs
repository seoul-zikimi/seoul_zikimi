using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace GridSystem
{
    /// <summary>
    /// (B) 런타임 그리드의 네트워크 호스트(서버 권위). 서버가 RuntimeGrid로 검증/판정하고,
    /// 상태는 NetworkList&lt;CellEntry&gt;로 복제. 모든 클라이언트는 리스트 변경 시 비주얼을 재구성한다.
    /// 입력은 GridDebugController가 RequestXxx()로 보냄(클라 → 서버 RPC). 늦참은 NetworkList가 자동 복제.
    /// </summary>
    [RequireComponent(typeof(GridManager))]
    public class GridNetwork : NetworkBehaviour
    {
        private readonly NetworkList<CellEntry> m_Cells = new();
        private readonly NetworkVariable<float> m_ScorePercent =
            new(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public float ScorePercent => m_ScorePercent.Value;

        private GridManager m_Manager;
        private RuntimeGrid m_ServerGrid;     // 서버 전용 권위 상태
        private ulong m_OwnerCounter;         // 서버 전용 고유 ownerObjectId 발급
        private GameObject m_VisualRoot;      // 클라이언트 로컬 비주얼 부모

        private void Awake() => m_Manager = GetComponent<GridManager>();

        public override void OnNetworkSpawn()
        {
            if (IsServer)
                m_ServerGrid = new RuntimeGrid(m_Manager.GridSize);

            m_VisualRoot = new GameObject("~GridVisuals");
            m_Cells.OnListChanged += OnCellsChanged;
            RebuildVisuals();   // 늦참: 이미 복제된 리스트로 즉시 재구성
        }

        public override void OnNetworkDespawn()
        {
            m_Cells.OnListChanged -= OnCellsChanged;
            if (m_VisualRoot != null) Destroy(m_VisualRoot);
        }

        // ── 입력 진입점 (클라가 호출 → 서버로) ──────────────────────────────
        public void RequestPlace(Vector3Int anchor, int materialId, byte rot) => PlaceRpc(anchor, materialId, rot);
        public void RequestRemove(Vector3Int cell) => RemoveRpc(cell);
        public void RequestProcess(Vector3Int cell, int processBit, bool apply) => ProcessRpc(cell, processBit, apply);

        [Rpc(SendTo.Server)]
        private void PlaceRpc(Vector3Int anchor, int materialId, byte rot)
        {
            var mat = m_Manager.Catalog != null ? m_Manager.Catalog.GetById(materialId) : null;
            if (mat == null || !m_ServerGrid.CanPlace(anchor, mat, rot)) return;

            ulong owner = ++m_OwnerCounter;
            m_ServerGrid.Place(anchor, mat, rot, owner);
            foreach (var c in GridFootprint.EnumerateFootprintCells(anchor, mat.Footprint, rot))
                m_Cells.Add(new CellEntry
                {
                    cell = c, materialId = materialId, rotationStep = rot,
                    completedProcessMask = 0, ownerObjectId = owner
                });
        }

        [Rpc(SendTo.Server)]
        private void RemoveRpc(Vector3Int cell)
        {
            var cs = m_ServerGrid.GetCell(cell);
            if (!cs.occupied) return;
            ulong owner = cs.ownerObjectId;
            m_ServerGrid.Remove(cell);
            for (int i = m_Cells.Count - 1; i >= 0; i--)
                if (m_Cells[i].ownerObjectId == owner) m_Cells.RemoveAt(i);
        }

        [Rpc(SendTo.Server)]
        private void ProcessRpc(Vector3Int cell, int processBit, bool apply)
            => ApplyProcessServer(cell, (ProcessType)processBit, apply);

        public void RequestCancelLast(Vector3Int cell) => CancelLastRpc(cell);

        [Rpc(SendTo.Server)]
        private void CancelLastRpc(Vector3Int cell)
        {
            var cs = m_ServerGrid.GetCell(cell);
            if (!cs.occupied) return;
            // 역순으로 완료된 마지막 공정을 취소(서버가 상태를 알므로 클라는 셀만 지정)
            for (int i = ProcessOrder.Sequence.Length - 1; i >= 0; i--)
            {
                var p = ProcessOrder.Sequence[i];
                if ((cs.completedProcessMask & (int)p) != 0) { ApplyProcessServer(cell, p, false); return; }
            }
        }

        private void ApplyProcessServer(Vector3Int cell, ProcessType proc, bool apply)
        {
            var cs = m_ServerGrid.GetCell(cell);
            if (!cs.occupied) return;
            var mat = m_Manager.Catalog != null ? m_Manager.Catalog.GetById(cs.materialId) : null;

            bool ok = apply ? m_ServerGrid.TryApplyProcess(cell, proc, mat)
                            : m_ServerGrid.TryCancelProcess(cell, proc);
            if (!ok) return;

            ulong owner = cs.ownerObjectId;
            int newMask = m_ServerGrid.GetCell(cell).completedProcessMask;
            for (int i = 0; i < m_Cells.Count; i++)
                if (m_Cells[i].ownerObjectId == owner)
                {
                    var e = m_Cells[i];
                    e.completedProcessMask = newMask;
                    m_Cells[i] = e;   // 값 변경 → 복제
                }
        }

        // ── 비주얼 (모든 클라이언트가 리스트로 재구성) ───────────────────────
        private void OnCellsChanged(NetworkListEvent<CellEntry> _)
        {
            RebuildVisuals();
            if (IsServer) RecomputeScore();
        }

        private void RecomputeScore()
        {
            if (m_Manager.Answer == null) return;
            var s = m_ServerGrid.ScoreAgainst(m_Manager.Answer, m_Manager.Catalog);
            m_ScorePercent.Value = s.Ratio * 100f;
        }

        private void RebuildVisuals()
        {
            if (m_VisualRoot == null) return;
            foreach (Transform t in m_VisualRoot.transform) Destroy(t.gameObject);

            float u = GridContract.Unit;
            foreach (var e in m_Cells)
            {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.transform.SetParent(m_VisualRoot.transform, true);
                cube.transform.position = GridCoordinates.CellToWorld(e.cell) + Vector3.one * 0.5f * u;
                cube.transform.localScale = Vector3.one * (u * 0.95f);
                var col = cube.GetComponent<Collider>();
                if (col != null) Destroy(col);
                SetColor(cube, ColorForMask(e.completedProcessMask));
            }
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
