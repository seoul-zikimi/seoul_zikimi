using System.Collections.Generic;
using UnityEngine;

namespace GridSystem
{
    /// <summary>
    /// (B) 런타임 그리드 — 순수 C# 상태 컨테이너. Unity 런타임 의존 없음 → EditMode 유닛테스트 가능.
    /// GridManager(싱글)·GridNetwork(멀티)가 이걸 감싸서 입력/네트워크를 붙인다.
    /// </summary>
    public class RuntimeGrid
    {
        private readonly Dictionary<Vector3Int, CellState> m_Cells = new();

        public Vector3Int Size { get; }
        public IEnumerable<KeyValuePair<Vector3Int, CellState>> Cells => m_Cells;

        public RuntimeGrid(Vector3Int size) => Size = size;

        /// <summary>그리드 외(환경 바닥·스캐폴드 등) 솔리드가 그 셀을 받치는지 묻는 훅. Unity 물리 의존이라
        /// 호스트(GridNetwork)가 주입한다. null이면 순수 그리드 규칙만(유닛테스트 기본). 배치·무너짐이 함께 본다.</summary>
        public System.Func<Vector3Int, bool> ExternalSupportBelow;

        public bool IsInBounds(Vector3Int cell)
            => cell.x >= 0 && cell.x < Size.x
            && cell.y >= 0 && cell.y < Size.y
            && cell.z >= 0 && cell.z < Size.z;

        public CellState GetCell(Vector3Int cell)
            => m_Cells.TryGetValue(cell, out var s) ? s : CellState.Empty;

        public bool IsOccupied(Vector3Int cell)
            => m_Cells.TryGetValue(cell, out var s) && s.occupied;

        /// <summary>footprint 전 셀이 범위 내 + 비어있는지.</summary>
        public bool CanPlace(Vector3Int anchor, MaterialDef material, int rotationStep)
        {
            if (material == null) return false;
            foreach (var cell in GridFootprint.EnumerateFootprintCells(anchor, material.Footprint, rotationStep))
            {
                if (!IsInBounds(cell)) return false;
                if (IsOccupied(cell)) return false;
            }
            return true;
        }

        /// <summary>
        /// 배치. footprint 전 셀에 같은 ownerObjectId로 기록. 성공 시 true.
        /// ownerObjectId는 오브젝트마다 고유해야 한다(네트워크=NetworkObjectId, 싱글=카운터). 0 사용 금지.
        /// </summary>
        public bool Place(Vector3Int anchor, MaterialDef material, int rotationStep, ulong ownerObjectId)
        {
            if (!CanPlace(anchor, material, rotationStep)) return false;
            foreach (var cell in GridFootprint.EnumerateFootprintCells(anchor, material.Footprint, rotationStep))
            {
                m_Cells[cell] = new CellState
                {
                    occupied = true,
                    materialId = material.Id,
                    rotationStep = rotationStep,
                    completedProcessMask = 0,
                    ownerObjectId = ownerObjectId,
                };
            }
            return true;
        }

        /// <summary>주어진 셀이 속한 오브젝트(같은 ownerObjectId)의 모든 셀을 제거.</summary>
        public bool Remove(Vector3Int cell)
        {
            if (!m_Cells.TryGetValue(cell, out var a) || !a.occupied) return false;

            ulong owner = a.ownerObjectId;
            var toRemove = new List<Vector3Int>();
            foreach (var kv in m_Cells)
                if (kv.Value.occupied && kv.Value.ownerObjectId == owner)
                    toRemove.Add(kv.Key);

            foreach (var c in toRemove) m_Cells.Remove(c);
            return toRemove.Count > 0;
        }

        // ── 공정 (G2.3) ───────────────────────────────────────────────────
        // 공정은 '오브젝트 단위'(같은 ownerObjectId의 모든 셀)로 적용/취소된다.

        /// <summary>
        /// 공정 적용. cell이 속한 오브젝트 전 셀에 process 비트를 켠다.
        /// 순차 규칙: canonical 순서상 process보다 앞이며 이 재료가 '요구'하는 공정이 모두 완료돼야 한다.
        /// (요구되지 않는 공정 적용은 허용 — 감점 없음, 점수 이득도 없음)
        /// </summary>
        public bool TryApplyProcess(Vector3Int cell, ProcessType process, MaterialDef material)
        {
            if (process == ProcessType.None) return false;
            if (!m_Cells.TryGetValue(cell, out var s) || !s.occupied) return false;

            int bit = (int)process;
            if ((s.completedProcessMask & bit) != 0) return false; // 이미 완료

            if (material != null)
            {
                int reqMask = material.RequiredMask;
                foreach (var p in ProcessOrder.Sequence)
                {
                    if (p == process) break;
                    int pb = (int)p;
                    if ((reqMask & pb) != 0 && (s.completedProcessMask & pb) == 0)
                        return false; // 앞선 필수 공정 미완료
                }
            }

            ApplyMaskToObject(s.ownerObjectId, m => m | bit);
            return true;
        }

        /// <summary>
        /// 공정 취소. cell이 속한 오브젝트 전 셀에서 process 비트를 끈다.
        /// 역순 규칙: canonical 순서상 process보다 뒤의 공정이 아직 완료 상태면 먼저 취소해야 한다.
        /// </summary>
        public bool TryCancelProcess(Vector3Int cell, ProcessType process)
        {
            if (process == ProcessType.None) return false;
            if (!m_Cells.TryGetValue(cell, out var s) || !s.occupied) return false;

            int bit = (int)process;
            if ((s.completedProcessMask & bit) == 0) return false; // 완료된 적 없음

            bool after = false;
            foreach (var p in ProcessOrder.Sequence)
            {
                if (p == process) { after = true; continue; }
                if (after && (s.completedProcessMask & (int)p) != 0)
                    return false; // 뒤 공정이 남아있음 → 먼저 취소해야
            }

            ApplyMaskToObject(s.ownerObjectId, m => m & ~bit);
            return true;
        }

        private void ApplyMaskToObject(ulong owner, System.Func<int, int> op)
        {
            var keys = new List<Vector3Int>();
            foreach (var kv in m_Cells)
                if (kv.Value.occupied && kv.Value.ownerObjectId == owner)
                    keys.Add(kv.Key);

            foreach (var k in keys)
            {
                var cs = m_Cells[k];
                cs.completedProcessMask = op(cs.completedProcessMask);
                m_Cells[k] = cs;
            }
        }

        // ── 무너짐 (F, 규칙 기반 연쇄) ─────────────────────────────────────
        // 고정됨(Fixed) = 앵커: 충격에 안 무너지고, 지지를 잃어도 떠 있으며 위 블록을 받쳐준다.
        // 미고정 = 지지(바닥 또는 다른 오브젝트가 바로 아래)를 잃으면 무너진다.

        private static bool IsFixed(CellState s)
            => (s.completedProcessMask & (int)ProcessType.Fixed) != 0;

        /// <summary>owner 오브젝트가 물리적으로 지지되는가: 셀이 바닥(y=0)에 닿거나, 바로 아래가 '다른' 오브젝트로 점유됨.</summary>
        private bool HasPhysicalSupport(ulong owner)
        {
            foreach (var kv in m_Cells)
            {
                if (kv.Value.ownerObjectId != owner) continue;
                var c = kv.Key;
                if (c.y == 0) return true;                          // 바닥
                var below = new Vector3Int(c.x, c.y - 1, c.z);
                if (m_Cells.TryGetValue(below, out var b) && b.occupied && b.ownerObjectId != owner)
                    return true;                                    // 다른 오브젝트가 받쳐줌
                if (ExternalSupportBelow != null && ExternalSupportBelow(below))
                    return true;                                    // 환경/스캐폴드 위에 얹힘
            }
            return false;
        }

        /// <summary>
        /// cell이 속한 '미고정' 오브젝트를 강제로 무너뜨리고, 지지를 잃은 미고정 오브젝트를 연쇄로 제거.
        /// 고정된 오브젝트는 앵커로 남는다. 제거된 오브젝트 목록을 반환(서버가 복제/환원에 사용).
        /// (지지 그래프가 아래로만 향하는 DAG라 제거 순서와 무관하게 결과 집합이 동일 — 결정론적)
        /// </summary>
        public List<CollapsedObject> Collapse(Vector3Int cell)
        {
            var removed = new List<CollapsedObject>();
            if (!m_Cells.TryGetValue(cell, out var s) || !s.occupied) return removed;
            if (IsFixed(s)) return removed;                         // 고정된 블록은 충격에 안 무너짐

            RemoveObjectInternal(s.ownerObjectId, s.materialId, removed);
            removed.AddRange(SettleUnsupported());                  // 트리거 블록 제거 후 연쇄 정착
            return removed;
        }

        /// <summary>지지를 잃은 '미고정' 오브젝트를 고정점 도달까지 반복 제거(연쇄 정착). 제거 목록 반환.
        /// 배치/철거/충격 등 구조 변경 직후 호출 → 떠 있는 미고정 블록을 무너뜨린다.</summary>
        public List<CollapsedObject> SettleUnsupported()
        {
            var removed = new List<CollapsedObject>();
            while (true)
            {
                ulong victim = 0; int victimMat = 0; bool found = false;
                foreach (var kv in m_Cells)
                {
                    var cs = kv.Value;
                    if (!cs.occupied || IsFixed(cs)) continue;
                    if (HasPhysicalSupport(cs.ownerObjectId)) continue;
                    victim = cs.ownerObjectId; victimMat = cs.materialId; found = true; break;
                }
                if (!found) break;
                RemoveObjectInternal(victim, victimMat, removed);
            }
            return removed;
        }

        /// <summary>이 자리에 놓으면 지지를 받는가(트리거 외 사전검사): footprint 셀이 바닥에 닿거나 아래에 다른 점유 셀이 있음.</summary>
        public bool WouldBeSupported(Vector3Int anchor, MaterialDef material, int rotationStep)
        {
            if (material == null) return false;
            return GridSupport.WouldBeSupported(
                GridFootprint.EnumerateFootprintCells(anchor, material.Footprint, rotationStep), IsOccupied, ExternalSupportBelow);
        }

        /// <summary>새로 놓인 오브젝트(owner) 바로 아래에 깔린 '미고정' 오브젝트들의 대표 셀(트리거②: 미고정 위에 놓기).</summary>
        public List<Vector3Int> FindUnfixedSupportsUnder(ulong owner)
        {
            var seen = new HashSet<ulong>();
            var result = new List<Vector3Int>();
            foreach (var kv in m_Cells)
            {
                if (kv.Value.ownerObjectId != owner) continue;
                var below = new Vector3Int(kv.Key.x, kv.Key.y - 1, kv.Key.z);
                if (m_Cells.TryGetValue(below, out var b) && b.occupied
                    && b.ownerObjectId != owner && !IsFixed(b) && seen.Add(b.ownerObjectId))
                    result.Add(below);
            }
            return result;
        }

        private void RemoveObjectInternal(ulong owner, int materialId, List<CollapsedObject> sink)
        {
            var keys = new List<Vector3Int>();
            foreach (var kv in m_Cells)
                if (kv.Value.ownerObjectId == owner) keys.Add(kv.Key);
            foreach (var k in keys) m_Cells.Remove(k);
            sink.Add(new CollapsedObject(owner, materialId));
        }

        // ── 채점 (G2.4) ───────────────────────────────────────────────────
        /// <summary>
        /// 정답과 셀 단위 비교. 정답 칸마다 배치(재료+회전) 일치 +200, 요구 공정 완료 +100.
        /// 요구 공정은 catalog의 MaterialDef.RequiredMask에서 파생(정답엔 미저장).
        /// </summary>
        public GridScore ScoreAgainst(MapAnswerData answer, MaterialCatalog catalog)
        {
            var result = new GridScore();
            if (answer == null) return result;

            foreach (var ans in answer.Cells)
            {
                result.answerCellCount++;
                result.maxScore += 300; // 배치 200 + 공정 100

                var def = catalog != null ? catalog.GetById(ans.materialId) : null;
                int reqMask = def != null ? def.RequiredMask : 0;

                var cell = GetCell(ans.cell);
                // 회전은 '점유 칸 형태'로 이미 검증됨(잘못 회전 → 다른 칸 → 재료 불일치로 감점).
                // 대칭 부재(1×1×3, 1×1×1 등)가 시각상 동일한데 step 숫자만 달라 감점되는 것 방지.
                bool placedOk = cell.occupied && cell.materialId == ans.materialId;
                if (!placedOk) continue;

                result.score += 200;
                result.placedCorrect++;

                if ((cell.completedProcessMask & reqMask) == reqMask)
                {
                    result.score += 100;
                    result.processCorrect++;
                }
            }
            return result;
        }
    }

    /// <summary>무너진 오브젝트 1개(복제 제거·재고 환원용).</summary>
    public readonly struct CollapsedObject
    {
        public readonly ulong ownerObjectId;
        public readonly int materialId;
        public CollapsedObject(ulong owner, int materialId)
        {
            ownerObjectId = owner;
            this.materialId = materialId;
        }
    }

    /// <summary>채점 결과 집계.</summary>
    public struct GridScore
    {
        public int score;
        public int maxScore;
        public int answerCellCount;
        public int placedCorrect;
        public int processCorrect;

        /// <summary>0~1 비율. 정답 셀이 없으면 1(만점 취급).</summary>
        public float Ratio => maxScore > 0 ? (float)score / maxScore : 1f;
    }
}
