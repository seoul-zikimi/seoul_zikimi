using UnityEngine;

namespace GridSystem
{
    /// <summary>
    /// 셀 좌표 ↔ 월드 좌표 변환. GridContract(Unit/Origin)에만 의존.
    /// CellToWorld 는 Autotiles3D.ToWorldPoint(identity, Unit=1)와 동일한 값을 내야 한다(A/B 정합).
    /// </summary>
    public static class GridCoordinates
    {
        /// <summary>셀의 기준점(min-corner) 월드 좌표.</summary>
        public static Vector3 CellToWorld(Vector3Int cell)
            => GridContract.Origin + (Vector3)cell * GridContract.Unit;

        /// <summary>월드 좌표가 속한 셀(min-corner 기준, [c, c+1) 범위).</summary>
        public static Vector3Int WorldToCell(Vector3 world)
        {
            Vector3 local = (world - GridContract.Origin) / GridContract.Unit;
            return new Vector3Int(
                Mathf.FloorToInt(local.x),
                Mathf.FloorToInt(local.y),
                Mathf.FloorToInt(local.z));
        }
    }
}
