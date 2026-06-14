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
            RecomputeScore();   // 새 정답 기준으로 점수 즉시 재계산(빈 그리드라 OnCellsChanged가 안 떠도)
        }

        private void RebuildVisuals()
        {
            if (m_VisualRoot == null) return;
            foreach (Transform t in m_VisualRoot.transform) Destroy(t.gameObject);

            float u = GridContract.Unit;
            var catalog = m_Manager.Catalog;

            // 오브젝트(owner)별 집계: min-corner(프리팹 정렬) + 중심·꼭대기(공정 마커 위치) + 재료/완료공정
            var agg = new Dictionary<ulong, OwnerAgg>();
            foreach (var e in m_Cells)
            {
                Vector3 center = GridCoordinates.CellToWorld(e.cell) + Vector3.one * 0.5f * u;
                float top = GridCoordinates.CellToWorld(e.cell).y + u;
                if (agg.TryGetValue(e.ownerObjectId, out var a))
                {
                    a.minCell = Vector3Int.Min(a.minCell, e.cell);
                    a.sumCenter += center; a.count++;
                    a.topY = Mathf.Max(a.topY, top);
                    agg[e.ownerObjectId] = a;
                }
                else agg[e.ownerObjectId] = new OwnerAgg
                {
                    minCell = e.cell, sumCenter = center, count = 1, topY = top,
                    materialId = e.materialId, completedMask = e.completedProcessMask,
                };
            }

            var done = new HashSet<ulong>();
            foreach (var e in m_Cells)
            {
                var def = catalog != null ? catalog.GetById(e.materialId) : null;
                if (def != null && def.Prefab != null)
                {
                    if (!done.Add(e.ownerObjectId)) continue;   // 오브젝트당 프리팹 1개
                    SpawnPrefabVisual(def, e.rotationStep, agg[e.ownerObjectId].minCell);
                }
                else
                {
                    // 프리팹 없음 → 칸마다 색칠 큐브(완료 공정 색)
                    var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    cube.transform.SetParent(m_VisualRoot.transform, true);
                    cube.transform.position = GridCoordinates.CellToWorld(e.cell) + Vector3.one * 0.5f * u;
                    cube.transform.localScale = Vector3.one * (u * 0.95f);
                    var col = cube.GetComponent<Collider>();
                    if (col != null) Destroy(col);
                    SetColor(cube, ColorForMask(e.completedProcessMask));
                }
            }

            // 공정 마커: 아직 할 공정이 남은 블록 위에 색 점(파랑=고정 필요 / 초록=페인트 필요). 다 되면 안 띄움.
            foreach (var a in agg.Values)
            {
                var def = catalog != null ? catalog.GetById(a.materialId) : null;
                var next = NextNeeded(def != null ? def.RequiredMask : 0, a.completedMask);
                if (next == ProcessType.None) continue;
                var pos = new Vector3(a.sumCenter.x / a.count, a.topY + 0.35f, a.sumCenter.z / a.count);
                SpawnProcessMarker(pos, next);
            }

            // 단단함: 미고정 하중부재(공정 전)만 통과(부딪혀 무너뜨림). 그 외(바닥·물·공정완료 전부)는 막음.
            // 플레이어는 중력+캡슐 → 막힌 블록 '위에 서고' '옆을 못 지나감'. (Walkable은 Y고정 시절 잔재 — 더는 통과시키지 않음)
            foreach (var e in m_Cells)
            {
                var def = catalog != null ? catalog.GetById(e.materialId) : null;
                if (def == null) continue;
                if (def.MustBeFixed && (e.completedProcessMask & (int)ProcessType.Fixed) == 0) continue;   // 미고정 하중부재 → 통과(무너뜨림)
                AddCellCollider(e.cell, u);                                                                 // 그 외 전부 → 막음
            }
        }

        private struct OwnerAgg
        {
            public Vector3Int minCell;
            public Vector3 sumCenter;
            public int count;
            public float topY;
            public int materialId;
            public int completedMask;
        }

        // 진짜 블록 프리팹을 점유 칸에 맞춰 1개 인스턴스. 피벗=min-corner + Y회전 정렬.
        private void SpawnPrefabVisual(MaterialDef def, int rot, Vector3Int minCell)
        {
            var fp = def.Footprint;
            var r = Quaternion.Euler(0f, 90f * rot, 0f);
            float u = GridContract.Unit;

            // 프리팹은 '중심 피벗'(자식 메쉬가 로컬 0 중심). 점유칸 월드 AABB의 '중심'에 둬야
            // 색칠 큐브·~Solid 콜라이더(둘 다 셀 중심 기준)와 정렬된다. 90°/270° 회전이면 x/z 치수 스왑.
            bool swap = (((((rot % 4) + 4) % 4) % 2) == 1);
            var dims = new Vector3(swap ? fp.z : fp.x, fp.y, swap ? fp.x : fp.z);

            var go = Instantiate(def.Prefab, m_VisualRoot.transform);
            go.transform.rotation = r;
            go.transform.position = GridCoordinates.CellToWorld(minCell) + dims * (0.5f * u);
            foreach (var c in go.GetComponentsInChildren<Collider>()) Destroy(c);   // 비주얼만(~Solid가 막음)
        }

        // 칸 하나를 막는 보이지 않는 BoxCollider(렌더러 없음). 칸 실제 크기 = 중력 플레이어가 위에 정확히 서고 옆을 못 지나감.
        private void AddCellCollider(Vector3Int cell, float u)
        {
            var go = new GameObject("~Solid");
            go.transform.SetParent(m_VisualRoot.transform, true);
            go.transform.position = GridCoordinates.CellToWorld(cell) + Vector3.one * 0.5f * u;   // 칸 중심
            go.AddComponent<BoxCollider>().size = Vector3.one * u;                                 // 칸 크기
        }

        // 공정이 더 필요한 블록 위에 띄우는 색 점(다음 필요 공정 색 = 도구·HUD 색과 일치). 충돌 없음.
        private void SpawnProcessMarker(Vector3 pos, ProcessType next)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "~ProcMarker";
            go.transform.SetParent(m_VisualRoot.transform, true);
            go.transform.position = pos;
            go.transform.localScale = Vector3.one * 0.35f;
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            SetColor(go, ColorForMask((int)next));
        }

        // 고정 → 페인트 순서로 첫 미완료 필수 공정(없으면 None).
        private static ProcessType NextNeeded(int reqMask, int completedMask)
        {
            foreach (var p in ProcessOrder.Sequence)
            {
                int pb = (int)p;
                if ((reqMask & pb) != 0 && (completedMask & pb) == 0) return p;
            }
            return ProcessType.None;
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
