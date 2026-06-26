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

        /// <summary>
        /// 피벗=min-corner 프리팹을 step·90° 회전하면 회전된 박스의 min-corner가 -방향으로 밀린다.
        /// CellToWorld(minCell)에 이 오프셋을 더하면 회전 프리팹이 점유칸 AABB(EnumerateFootprintCells)와 정합된다.
        /// </summary>
        public static Vector3 RotatedPivotOffset(Vector3Int footprint, int step, float unit)
        {
            var r = Quaternion.Euler(0f, 90f * step, 0f);
            Vector3 cx = r * new Vector3(footprint.x * unit, 0f, 0f);
            Vector3 cz = r * new Vector3(0f, 0f, footprint.z * unit);
            Vector3 minc = Vector3.Min(Vector3.Min(Vector3.zero, cx), Vector3.Min(cz, cx + cz));
            return -minc;
        }

        /// <summary>
        /// 회전 프리팹(피벗=min-corner)을 점유칸에 정확히 안착시킨다.
        /// + 메시가 footprint와 90° 다르게 모델링된 경우(렌더러 XZ 장축이 footprint 장축과 수직)
        ///   자동으로 추가 90° 보정 → 셀/프리뷰/콜라이더와 정합(놓는·고스트 공용).
        /// </summary>
        public static void PlaceRotatedPrefab(GameObject go, Vector3 cellWorldMin, Vector3Int footprint, int rotStep, float unit)
        {
            int modelYaw = 0;
            var rends = go.GetComponentsInChildren<Renderer>();
            if (rends.Length > 0)
            {
                Bounds b = rends[0].bounds;
                for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
                bool meshXLong = b.size.x > b.size.z * 1.05f;
                bool meshZLong = b.size.z > b.size.x * 1.05f;
                bool fpXLong = footprint.x > footprint.z;
                bool fpZLong = footprint.z > footprint.x;
                if ((meshXLong && fpZLong) || (meshZLong && fpXLong)) modelYaw = 1;   // 메시가 footprint와 90° 어긋남
            }

            int eff = rotStep - modelYaw;
            bool myOdd = (((modelYaw % 2) + 2) % 2) == 1;
            Vector3Int meshFp = myOdd ? new Vector3Int(footprint.z, footprint.y, footprint.x) : footprint;
            go.transform.rotation = Quaternion.Euler(0f, 90f * eff, 0f);
            go.transform.position = cellWorldMin + RotatedPivotOffset(meshFp, eff, unit);
        }
    }
}
