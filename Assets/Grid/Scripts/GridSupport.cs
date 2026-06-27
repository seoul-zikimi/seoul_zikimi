using System;
using System.Collections.Generic;
using UnityEngine;

namespace GridSystem
{
    /// <summary>
    /// 배치 지지 판정(서버·클라 공용). footprint가 바닥(y=0)에 닿거나 아래에 점유 셀이 있으면 지지됨.
    /// 서버는 RuntimeGrid, 클라는 복제된 NetworkList로 '같은 규칙'을 적용 → 판정 불일치(헛배치/재료 손실) 방지.
    /// </summary>
    public static class GridSupport
    {
        /// <param name="externalBelow">그리드 외 솔리드(환경 바닥·스캐폴드 등)가 그 셀을 받치는지. null이면 그리드만 본다.</param>
        public static bool WouldBeSupported(IEnumerable<Vector3Int> footprintCells, Func<Vector3Int, bool> isOccupied,
                                            Func<Vector3Int, bool> externalBelow = null)
        {
            var cells = footprintCells as HashSet<Vector3Int> ?? new HashSet<Vector3Int>(footprintCells);
            foreach (var c in cells)
            {
                if (c.y == 0) return true;                       // 바닥
                var below = new Vector3Int(c.x, c.y - 1, c.z);
                if (cells.Contains(below)) continue;             // 같은 오브젝트 셀은 지지로 안 침
                if (isOccupied(below)) return true;              // 다른 오브젝트(그리드 블록)가 받쳐줌
                if (externalBelow != null && externalBelow(below)) return true;   // 환경/스캐폴드 같은 솔리드가 받쳐줌
            }
            return false;
        }

        // 그리드 '바깥' 솔리드 콜라이더가 이 셀을 채우고 있나(환경 바닥·스캐폴드 등).
        // 트리거·플레이어·경계벽·그리드 내부 콜라이더(~Solid/~Ground)는 지지로 안 친다.
        public static bool ExternalSolidAt(Vector3Int cell, float unit)
        {
            Vector3 center = GridCoordinates.CellToWorld(cell) + Vector3.one * (0.5f * unit);
            var hits = Physics.OverlapBox(center, Vector3.one * (0.45f * unit), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
            foreach (var h in hits)
            {
                if (h.CompareTag("Player") || h.CompareTag("Boundary")) continue;
                var n = h.name;
                if (n == "~Solid" || n == "~Ground") continue;   // 그리드 내부는 그리드 로직이 따로 처리
                return true;                                      // 환경/스캐폴드 등 솔리드가 받침
            }
            return false;
        }
    }
}
