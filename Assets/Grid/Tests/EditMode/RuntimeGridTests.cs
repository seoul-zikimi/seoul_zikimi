using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GridSystem.Tests
{
    public class RuntimeGridTests
    {
        readonly List<Object> m_Created = new();

        [TearDown]
        public void Cleanup()
        {
            foreach (var o in m_Created) if (o != null) Object.DestroyImmediate(o);
            m_Created.Clear();
        }

        MaterialDef MakeDef(int id, Vector3Int footprint)
        {
            var def = ScriptableObject.CreateInstance<MaterialDef>();
            m_Created.Add(def);
            var so = new SerializedObject(def);
            so.FindProperty("m_Id").intValue = id;
            so.FindProperty("m_Footprint").vector3IntValue = footprint;
            so.ApplyModifiedProperties();
            return def;
        }

        static RuntimeGrid Grid8() => new RuntimeGrid(new Vector3Int(8, 4, 8));

        [Test]
        public void Empty_GetCell_ReturnsEmpty()
        {
            var g = Grid8();
            var c = g.GetCell(new Vector3Int(1, 1, 1));
            Assert.IsFalse(c.occupied);
            Assert.AreEqual(MaterialCatalog.NoMaterial, c.materialId);
        }

        [Test]
        public void Place_1x1x1_OccupiesOneCell()
        {
            var g = Grid8();
            var def = MakeDef(0, Vector3Int.one);
            Assert.IsTrue(g.Place(new Vector3Int(2, 0, 2), def, 0, 1));
            Assert.IsTrue(g.IsOccupied(new Vector3Int(2, 0, 2)));
            Assert.AreEqual(0, g.GetCell(new Vector3Int(2, 0, 2)).materialId);
        }

        [Test]
        public void Place_1x3x2_OccupiesAllFootprintCells()
        {
            var g = Grid8();
            var wall = MakeDef(2, new Vector3Int(1, 3, 2));
            Assert.IsTrue(g.Place(Vector3Int.zero, wall, 0, 7));
            foreach (var cell in GridFootprint.EnumerateFootprintCells(Vector3Int.zero, new Vector3Int(1, 3, 2), 0))
                Assert.IsTrue(g.IsOccupied(cell), $"{cell} 점유돼야 함");
        }

        [Test]
        public void CanPlace_RejectsOverlap()
        {
            var g = Grid8();
            var def = MakeDef(0, Vector3Int.one);
            g.Place(new Vector3Int(1, 0, 1), def, 0, 1);
            Assert.IsFalse(g.CanPlace(new Vector3Int(1, 0, 1), def, 0));
        }

        [Test]
        public void CanPlace_RejectsOutOfBounds()
        {
            var g = Grid8();
            var pillar = MakeDef(1, new Vector3Int(1, 1, 3));
            // anchor z=7 → 점유 z=7,8,9 → 범위(8) 초과
            Assert.IsFalse(g.CanPlace(new Vector3Int(0, 0, 7), pillar, 0));
        }

        [Test]
        public void Place_OutOfBounds_ReturnsFalse_AndNoChange()
        {
            var g = Grid8();
            var pillar = MakeDef(1, new Vector3Int(1, 1, 3));
            Assert.IsFalse(g.Place(new Vector3Int(0, 0, 7), pillar, 0, 5));
            Assert.IsFalse(g.IsOccupied(new Vector3Int(0, 0, 7)));
        }

        [Test]
        public void Remove_ClearsAllFootprintCells()
        {
            var g = Grid8();
            var wall = MakeDef(2, new Vector3Int(1, 3, 2));
            g.Place(Vector3Int.zero, wall, 0, 42);
            Assert.IsTrue(g.Remove(Vector3Int.zero));
            foreach (var cell in GridFootprint.EnumerateFootprintCells(Vector3Int.zero, new Vector3Int(1, 3, 2), 0))
                Assert.IsFalse(g.IsOccupied(cell), $"{cell} 비워져야 함");
        }
    }
}
