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
