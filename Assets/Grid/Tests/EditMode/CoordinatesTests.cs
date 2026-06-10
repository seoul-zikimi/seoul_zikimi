using NUnit.Framework;
using UnityEngine;

namespace GridSystem.Tests
{
    public class CoordinatesTests
    {
        [Test]
        public void CellToWorld_MatchesContract()
        {
            // Unit=1, Origin=0 → 셀 좌표가 곧 월드 좌표 (= Autotiles3D.ToWorldPoint)
            Assert.AreEqual(new Vector3(2, 1, 3), GridCoordinates.CellToWorld(new Vector3Int(2, 1, 3)));
            Assert.AreEqual(Vector3.zero, GridCoordinates.CellToWorld(Vector3Int.zero));
        }

        [Test]
        public void WorldToCell_FloorsIntoCell()
        {
            Assert.AreEqual(new Vector3Int(2, 0, 1),  GridCoordinates.WorldToCell(new Vector3(2.3f, 0.9f, 1.7f)));
            Assert.AreEqual(new Vector3Int(-1, 0, -2), GridCoordinates.WorldToCell(new Vector3(-0.5f, 0.1f, -1.2f)));
        }

        [Test]
        public void RoundTrip_Cell_World_Cell()
        {
            var samples = new[]
            {
                new Vector3Int(0, 0, 0),
                new Vector3Int(5, 2, 9),
                new Vector3Int(-3, 1, -7),
            };
            foreach (var c in samples)
                Assert.AreEqual(c, GridCoordinates.WorldToCell(GridCoordinates.CellToWorld(c)));
        }
    }
}
