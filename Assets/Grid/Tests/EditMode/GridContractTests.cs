using NUnit.Framework;
using UnityEngine;

namespace GridSystem.Tests
{
    public class GridContractTests
    {
        [Test]
        public void Unit_Is_One()
        {
            Assert.AreEqual(1f,GridContract.Unit);
        }

        // Origin은 '고정 상수'가 아니라 씬의 GridManager 위치를 따라가는 기준점이다(맵 마커로 그리드가 옮겨감).
        // 그래서 값이 0인지가 아니라, 옮긴 만큼 좌표 변환이 따라가는지를 검사한다.
        [Test]
        public void Origin_Moves_Coordinates_With_It()
        {
            var saved = GridContract.Origin;
            try
            {
                GridContract.Origin = new Vector3(100f, 0f, -40f);
                Assert.AreEqual(new Vector3(102f, 1f, -37f), GridCoordinates.CellToWorld(new Vector3Int(2, 1, 3)));
                Assert.AreEqual(new Vector3Int(2, 1, 3), GridCoordinates.WorldToCell(new Vector3(102.5f, 1.5f, -36.5f)));
            }
            finally { GridContract.Origin = saved; }
        }
    }
}