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
        public static bool WouldBeSupported(IEnumerable<Vector3Int> footprintCells, Func<Vector3Int, bool> isOccupied)
        {
            var cells = footprintCells as HashSet<Vector3Int> ?? new HashSet<Vector3Int>(footprintCells);
            foreach (var c in cells)
            {
                if (c.y == 0) return true;                       // 바닥
                var below = new Vector3Int(c.x, c.y - 1, c.z);
                if (cells.Contains(below)) continue;             // 같은 오브젝트 셀은 지지로 안 침
                if (isOccupied(below)) return true;              // 다른 오브젝트가 받쳐줌
            }
            return false;
        }
    }
}
