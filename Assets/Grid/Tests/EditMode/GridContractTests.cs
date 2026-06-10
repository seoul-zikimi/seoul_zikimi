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

        [Test]
        public void Origin_Is_World_Zero()
        {
            Assert.AreEqual(Vector3.zero, GridContract.Origin);
        }
    }
}