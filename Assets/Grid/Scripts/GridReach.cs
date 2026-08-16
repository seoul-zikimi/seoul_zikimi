using System.Collections.Generic;
using UnityEngine;

namespace GridSystem
{
    /// <summary>
    /// 사거리(손 닿는 거리) 판정. 블록의 '중심점'이 아니라 블록이 실제로 차지한 셀들 중
    /// 가장 가까운 셀까지의 거리로 잰다 — 큰 블록도 가장자리에 서면 닿는다.
    /// 순수 계산(모노비헤이비어 없음)이라 EditMode 테스트로 검증한다.
    /// </summary>
    public static class GridReach
    {
        /// <summary>플레이어 발밑(XZ)에서 셀 한 칸(정사각 영역)까지의 수평 거리. 셀 안에 서 있으면 0.</summary>
        public static float DistanceToCell(Vector3 playerPos, Vector3Int cell, Vector3 origin, float unit)
        {
            float minX = origin.x + cell.x * unit, maxX = minX + unit;
            float minZ = origin.z + cell.z * unit, maxZ = minZ + unit;
            float dx = Mathf.Max(minX - playerPos.x, 0f, playerPos.x - maxX);
            float dz = Mathf.Max(minZ - playerPos.z, 0f, playerPos.z - maxZ);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>셀 묶음(블록 풋프린트) 중 가장 가까운 셀까지의 수평 거리. 비어 있으면 무한대.</summary>
        public static float DistanceToCells(Vector3 playerPos, IReadOnlyList<Vector3Int> cells, Vector3 origin, float unit)
        {
            float best = float.MaxValue;
            for (int i = 0; i < cells.Count; i++)
            {
                float d = DistanceToCell(playerPos, cells[i], origin, unit);
                if (d < best) best = d;
            }
            return cells.Count == 0 ? float.MaxValue : best;
        }

        /// <summary>사거리 안인가. reachCells = 칸 수(예: 2칸).</summary>
        public static bool InReach(Vector3 playerPos, IReadOnlyList<Vector3Int> cells, Vector3 origin, float unit, float reachCells)
            => DistanceToCells(playerPos, cells, origin, unit) <= reachCells * unit;
    }
}
