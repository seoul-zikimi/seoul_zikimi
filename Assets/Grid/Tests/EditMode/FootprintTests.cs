using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace GridSystem.Tests
{
    public class FootprintTests
    {
        [Test]
        public void RotateXZ_Step0_Identity()
        {
            var c = new Vector3Int(2, 1, 3);
            Assert.AreEqual(c, GridFootprint.RotateXZ(c, 0));
        }

        [Test]
        public void RotateXZ_Step1_Matches_Unity_YRotation()
        {
            // (1,0,0) → (0,0,-1) : Quaternion.Euler(0,90,0) * Vector3.right 과 동일
            Assert.AreEqual(new Vector3Int(0, 0, -1), GridFootprint.RotateXZ(new Vector3Int(1, 0, 0), 1));
        }

        [Test]
        public void RotateXZ_TwiceStep1_EqualsStep2()
        {
            var c = new Vector3Int(2, 5, -3);
            Assert.AreEqual(
                GridFootprint.RotateXZ(c, 2),
                GridFootprint.RotateXZ(GridFootprint.RotateXZ(c, 1), 1));
        }

        [Test]
        public void RotateXZ_FourSteps_ReturnToOrigin()
        {
            var c = new Vector3Int(2, 5, -3);
            var r = c;
            for (int i = 0; i < 4; i++) r = GridFootprint.RotateXZ(r, 1);
            Assert.AreEqual(c, r);
        }

        [Test]
        public void Footprint_1x1x1_SingleCellAtAnchor()
        {
            var cells = GridFootprint.EnumerateFootprintCells(new Vector3Int(3, 0, 4), Vector3Int.one, 0);
            Assert.AreEqual(1, cells.Count);
            Assert.AreEqual(new Vector3Int(3, 0, 4), cells[0]);
        }

        [Test]
        public void Footprint_1x3x2_Step0_ExactCells()
        {
            var cells = GridFootprint.EnumerateFootprintCells(Vector3Int.zero, new Vector3Int(1, 3, 2), 0);
            var expected = new HashSet<Vector3Int>
            {
                new Vector3Int(0,0,0), new Vector3Int(0,0,1),
                new Vector3Int(0,1,0), new Vector3Int(0,1,1),
                new Vector3Int(0,2,0), new Vector3Int(0,2,1),
            };
            CollectionAssert.AreEquivalent(expected, cells);
        }

        [Test]
        public void Footprint_AllSteps_CountAndUnique()
        {
            var fp = new Vector3Int(1, 3, 2);
            for (int step = 0; step < 4; step++)
            {
                var cells = GridFootprint.EnumerateFootprintCells(new Vector3Int(5, 0, 5), fp, step);
                Assert.AreEqual(6, cells.Count, $"step {step} count");
                Assert.AreEqual(6, cells.Distinct().Count(), $"step {step} unique");
            }
        }

        [Test]
        public void Footprint_AnchorIsAlwaysMinCorner()
        {
            var fp = new Vector3Int(1, 3, 2);
            var anchor = new Vector3Int(2, 0, 7);
            for (int step = 0; step < 4; step++)
            {
                var cells = GridFootprint.EnumerateFootprintCells(anchor, fp, step);
                var min = new Vector3Int(cells.Min(c => c.x), cells.Min(c => c.y), cells.Min(c => c.z));
                Assert.AreEqual(anchor, min, $"step {step} min-corner");
            }
        }
    }
}
