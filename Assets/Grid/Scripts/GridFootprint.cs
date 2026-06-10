using System.Collections.Generic;
using UnityEngine;

namespace GridSystem
{
    /// <summary>
    /// footprint(점유 칸) 계산 — 익스포터(A)와 런타임 배치(B)가 공유하는 단일 경로.
    /// 회전은 Quaternion.Euler(0, step*90, 0)과 같은 +Y 회전으로 정의(런타임 비주얼과 정합).
    /// </summary>
    public static class GridFootprint
    {
        /// <summary>로컬 칸 오프셋을 +Y축 기준 step*90°로 회전. (Quaternion.Euler(0,step*90,0)와 동일)</summary>
        public static Vector3Int RotateXZ(Vector3Int c, int step)
        {
            switch (step & 3)
            {
                case 1:  return new Vector3Int( c.z, c.y, -c.x);  // 90°
                case 2:  return new Vector3Int(-c.x, c.y, -c.z);  // 180°
                case 3:  return new Vector3Int(-c.z, c.y,  c.x);  // 270°
                default: return c;                                 // 0°
            }
        }

        /// <summary>
        /// 앵커(min-corner) + footprint + 회전 → 점유하는 모든 셀 목록.
        /// 회전 후 min-corner를 anchor에 맞춰 재정규화 → 앵커는 4스텝 모두 min-corner를 유지.
        /// </summary>
        public static List<Vector3Int> EnumerateFootprintCells(Vector3Int anchor, Vector3Int footprint, int step)
        {
            int count = Mathf.Max(0, footprint.x) * Mathf.Max(0, footprint.y) * Mathf.Max(0, footprint.z);
            var rotated = new List<Vector3Int>(count);

            for (int x = 0; x < footprint.x; x++)
            for (int y = 0; y < footprint.y; y++)
            for (int z = 0; z < footprint.z; z++)
                rotated.Add(RotateXZ(new Vector3Int(x, y, z), step));

            int minX = int.MaxValue, minY = int.MaxValue, minZ = int.MaxValue;
            foreach (var r in rotated)
            {
                if (r.x < minX) minX = r.x;
                if (r.y < minY) minY = r.y;
                if (r.z < minZ) minZ = r.z;
            }

            var result = new List<Vector3Int>(rotated.Count);
            foreach (var r in rotated)
                result.Add(anchor + new Vector3Int(r.x - minX, r.y - minY, r.z - minZ));
            return result;
        }
    }
}
