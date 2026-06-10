using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GridSystem.Tests
{
    public class CatalogTests
    {
        // 테스트에서 만든 SO들 — 끝나면 파괴(메모리 누수 경고 방지)
        readonly List<Object> m_Created = new();

        [TearDown]
        public void Cleanup()
        {
            foreach (var o in m_Created)
                if (o != null) Object.DestroyImmediate(o);
            m_Created.Clear();
        }

        MaterialDef MakeDef(int id)
        {
            var def = ScriptableObject.CreateInstance<MaterialDef>();
            m_Created.Add(def);
            var so = new SerializedObject(def);
            so.FindProperty("m_Id").intValue = id;   // private [SerializeField] 세팅
            so.ApplyModifiedProperties();
            return def;
        }

        MaterialCatalog MakeCatalog()
        {
            var cat = ScriptableObject.CreateInstance<MaterialCatalog>();
            m_Created.Add(cat);
            return cat;
        }

        [Test]
        public void GetById_Returns_Def_Or_Null()
        {
            var cat = MakeCatalog();
            var def = MakeDef(7);

            var so = new SerializedObject(cat);
            var mats = so.FindProperty("m_Materials");
            mats.arraySize = 1;
            mats.GetArrayElementAtIndex(0).objectReferenceValue = def;
            so.ApplyModifiedProperties();
            cat.RebuildLookup();

            Assert.AreSame(def, cat.GetById(7));
            Assert.IsNull(cat.GetById(999));
        }

        [Test]
        public void TileIdToMaterialId_Maps_Or_Sentinel()
        {
            var cat = MakeCatalog();

            var so = new SerializedObject(cat);
            var map = so.FindProperty("m_TileIdMap");
            map.arraySize = 1;
            var e0 = map.GetArrayElementAtIndex(0);
            e0.FindPropertyRelative("autotilesTileId").intValue = 42;
            e0.FindPropertyRelative("materialId").intValue = 2;
            so.ApplyModifiedProperties();
            cat.RebuildLookup();

            Assert.AreEqual(2, cat.TileIdToMaterialId(42));
            Assert.AreEqual(MaterialCatalog.NoMaterial, cat.TileIdToMaterialId(999));
        }
    }
}
