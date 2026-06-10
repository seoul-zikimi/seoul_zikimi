using NUnit.Framework;
using UnityEngine;

namespace GridSystem.Tests
{
    public class CellEntryTests
    {
        static CellEntry Sample() => new CellEntry
        {
            cell = new Vector3Int(1, 2, 3),
            materialId = 5,
            rotationStep = 1,
            completedProcessMask = 3,
            ownerObjectId = 9,
        };

        [Test]
        public void Equals_SameValues_True()
        {
            Assert.IsTrue(Sample().Equals(Sample()));
        }

        [Test]
        public void Equals_DifferentMask_False()
        {
            var a = Sample();
            var b = Sample();
            b.completedProcessMask = 1;
            Assert.IsFalse(a.Equals(b));
        }

        [Test]
        public void Equals_DifferentCell_False()
        {
            var a = Sample();
            var b = Sample();
            b.cell = new Vector3Int(9, 9, 9);
            Assert.IsFalse(a.Equals(b));
        }
    }
}
