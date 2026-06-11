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
        private readonly NetworkVariable<ScoreSnapshot> m_Score =
            new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public ScoreSnapshot Score => m_Score.Value;
        public float ScorePercent => m_Score.Value.Percent;

        /// <summary>복제된 상태 기준 해당 셀이 비어있는지(클라이언트도 호출 가능). 배치 전 사전 검사용.</summary>
        public bool IsCellFree(Vector3Int cell)
        {
            foreach (var e in m_Cells)
                if (e.cell == cell) return false;
            return true;
        }

        /// <summary>복제된 상태에서 셀의 재료 id·완료 공정 비트를 읽는다(클라도 호출 — E 공정 다음단계 판단용).</summary>
        public bool TryGetCell(Vector3Int cell, out int materialId, out int completedMask)
        {
            foreach (var e in m_Cells)
                if (e.cell == cell) { materialId = e.materialId; completedMask = e.completedProcessMask; return true; }
            materialId = -1; completedMask = 0;
            return false;
        }

        private GridManager m_Manager;
        private MaterialDropField m_DropField; // 같은 오브젝트(붕괴/철거 재료를 바닥에 떨굼)
        private RuntimeGrid m_ServerGrid;     // 서버 전용 권위 상태
        private ulong m_OwnerCounter;         // 서버 전용 고유 ownerObjectId 발급
        private GameObject m_VisualRoot;      // 클라이언트 로컬 비주얼 부모

        private void Awake()
        {
            m_Manager = GetComponent<GridManager>();
            m_DropField = GetComponent<MaterialDropField>();
        }

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
        public void RequestShock(Vector3Int cell) => ShockRpc(cell);   // 트리거①: 외부충격(플레이어 부딪힘)

        [Rpc(SendTo.Server)]
        private void PlaceRpc(Vector3Int anchor, int materialId, byte rot)
        {
            var mat = m_Manager.Catalog != null ? m_Manager.Catalog.GetById(materialId) : null;
            if (mat == null || !m_ServerGrid.CanPlace(anchor, mat, rot)) return;
            if (!m_ServerGrid.WouldBeSupported(anchor, mat, rot)) return;   // 허공(지지 없음) 배치 거부

            ulong owner = ++m_OwnerCounter;
            m_ServerGrid.Place(anchor, mat, rot, owner);
            foreach (var c in GridFootprint.EnumerateFootprintCells(anchor, mat.Footprint, rot))
                m_Cells.Add(new CellEntry
                {
                    cell = c, materialId = materialId, rotationStep = rot,
                    completedProcessMask = 0, ownerObjectId = owner
                });

            // 트리거②: 미고정 오브젝트 위에 놓임 → 그 미고정 지지물(+연쇄) 무너짐
            foreach (var t in m_ServerGrid.FindUnfixedSupportsUnder(owner))
                foreach (var co in m_ServerGrid.Collapse(t))
                    RemoveCollapsed(co);
        }

        [Rpc(SendTo.Server)]
        private void RemoveRpc(Vector3Int cell)
        {
            var cs = m_ServerGrid.GetCell(cell);
            if (!cs.occupied) return;
            ulong owner = cs.ownerObjectId;
            int materialId = cs.materialId;
            m_ServerGrid.Remove(cell);

            Vector3 from = default; bool have = false;
            for (int i = m_Cells.Count - 1; i >= 0; i--)
                if (m_Cells[i].ownerObjectId == owner)
                {
                    if (!have) { from = CellWorld(m_Cells[i].cell); have = true; }
                    m_Cells.RemoveAt(i);
                }
            if (have && m_DropField != null) m_DropField.ServerDrop(materialId, from);   // 철거 재료를 바닥에 떨굼

            foreach (var co in m_ServerGrid.SettleUnsupported())     // 받침 사라짐 → 위 미고정 블록 연쇄
                RemoveCollapsed(co);
        }

        [Rpc(SendTo.Server)]
        private void ShockRpc(Vector3Int cell)
        {
            var cs = m_ServerGrid.GetCell(cell);
            if (!cs.occupied) return;
            var mat = m_Manager.Catalog != null ? m_Manager.Catalog.GetById(cs.materialId) : null;
            if (mat == null || !mat.MustBeFixed) return;   // 하중부재(기둥/벽)만 충격에 무너짐 — 바닥은 밟아도 OK
            foreach (var co in m_ServerGrid.Collapse(cell)) RemoveCollapsed(co);
        }

        /// <summary>무너진 오브젝트를 복제 리스트에서 제거하고 재료를 바닥에 떨군다(주워서 재배치 가능).</summary>
        private void RemoveCollapsed(CollapsedObject co)
        {
            Vector3 from = default; bool have = false;
            for (int i = m_Cells.Count - 1; i >= 0; i--)
                if (m_Cells[i].ownerObjectId == co.ownerObjectId)
                {
                    if (!have) { from = CellWorld(m_Cells[i].cell); have = true; }
                    m_Cells.RemoveAt(i);
                }
            if (have && m_DropField != null) m_DropField.ServerDrop(co.materialId, from);
        }

        private static Vector3 CellWorld(Vector3Int cell)
            => GridCoordinates.CellToWorld(cell) + Vector3.one * 0.5f * GridContract.Unit;

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
            m_Score.Value = new ScoreSnapshot
            {
                score = s.score, maxScore = s.maxScore, answerCells = s.answerCellCount,
                placedCorrect = s.placedCorrect, processCorrect = s.processCorrect,
            };
        }

        /// <summary>게임 재시작용: 서버 그리드·복제 리스트를 비운다(→ 비주얼/점수 자동 0 갱신).</summary>
        public void ServerResetGrid()
        {
            if (!IsServer) return;
            m_ServerGrid = new RuntimeGrid(m_Manager.GridSize);
            m_OwnerCounter = 0;
            for (int i = m_Cells.Count - 1; i >= 0; i--) m_Cells.RemoveAt(i);
            if (m_DropField != null) m_DropField.ServerReset();   // 바닥 재료도 정리
        }

        private void RebuildVisuals()
        {
            if (m_VisualRoot == null) return;
            foreach (Transform t in m_VisualRoot.transform) Destroy(t.gameObject);

            float u = GridContract.Unit;
            var catalog = m_Manager.Catalog;

            // 오브젝트(owner)별 점유 셀의 min-corner(프리팹 정렬 기준)
            var minCell = new Dictionary<ulong, Vector3Int>();
            foreach (var e in m_Cells)
                minCell[e.ownerObjectId] = minCell.TryGetValue(e.ownerObjectId, out var mn)
                    ? Vector3Int.Min(mn, e.cell) : e.cell;

            var done = new HashSet<ulong>();
            foreach (var e in m_Cells)
            {
                var def = catalog != null ? catalog.GetById(e.materialId) : null;
                if (def != null && def.Prefab != null)
                {
                    if (!done.Add(e.ownerObjectId)) continue;   // 오브젝트당 프리팹 1개
                    SpawnPrefabVisual(def, e.rotationStep, minCell[e.ownerObjectId]);
                }
                else
                {
                    // 프리팹 없음 → 칸마다 색칠 큐브(공정 색)
                    var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    cube.transform.SetParent(m_VisualRoot.transform, true);
                    cube.transform.position = GridCoordinates.CellToWorld(e.cell) + Vector3.one * 0.5f * u;
                    cube.transform.localScale = Vector3.one * (u * 0.95f);
                    var col = cube.GetComponent<Collider>();
                    if (col != null) Destroy(col);
                    SetColor(cube, ColorForMask(e.completedProcessMask));
                }
            }
        }

        // 진짜 블록 프리팹을 점유 칸에 맞춰 1개 인스턴스. 피벗=min-corner + Y회전 정렬.
        private void SpawnPrefabVisual(MaterialDef def, int rot, Vector3Int minCell)
        {
            var fp = def.Footprint;
            var r = Quaternion.Euler(0f, 90f * rot, 0f);

            // footprint XZ 사각형 모서리를 회전 → 회전된 박스의 min-corner offset
            float minX = float.MaxValue, minZ = float.MaxValue;
            for (int cx = 0; cx <= 1; cx++)
            for (int cz = 0; cz <= 1; cz++)
            {
                var p = r * new Vector3(cx * fp.x, 0f, cz * fp.z);
                if (p.x < minX) minX = p.x;
                if (p.z < minZ) minZ = p.z;
            }

            var go = Instantiate(def.Prefab, m_VisualRoot.transform);
            go.transform.rotation = r;
            go.transform.position = GridCoordinates.CellToWorld(minCell) - new Vector3(minX, 0f, minZ);
            foreach (var c in go.GetComponentsInChildren<Collider>()) Destroy(c);   // 비주얼만(통과 유지)
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

    /// <summary>채점 분해 스냅샷(복제용). RuntimeGrid.GridScore를 네트워크로 노출한다.</summary>
    public struct ScoreSnapshot : INetworkSerializable, System.IEquatable<ScoreSnapshot>
    {
        public int score, maxScore, answerCells, placedCorrect, processCorrect;

        public float Percent => maxScore > 0 ? (float)score / maxScore * 100f : 0f;

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref score);
            s.SerializeValue(ref maxScore);
            s.SerializeValue(ref answerCells);
            s.SerializeValue(ref placedCorrect);
            s.SerializeValue(ref processCorrect);
        }

        public bool Equals(ScoreSnapshot o)
            => score == o.score && maxScore == o.maxScore && answerCells == o.answerCells
            && placedCorrect == o.placedCorrect && processCorrect == o.processCorrect;
    }
}
