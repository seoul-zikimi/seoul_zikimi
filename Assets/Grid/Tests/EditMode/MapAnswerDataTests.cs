using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GridSystem.Tests
{
    public class MapAnswerDataTests
    {
        readonly List<Object> m_Created = new();

        [TearDown]
        public void Cleanup()
        {
            foreach (var o in m_Created)
                if (o != null) Object.DestroyImmediate(o);
            m_Created.Clear();
        }

        MapAnswerData MakeData(params AnswerCell[] cells)
        {
            var data = ScriptableObject.CreateInstance<MapAnswerData>();
            m_Created.Add(data);

            var so = new SerializedObject(data);
            var arr = so.FindProperty("m_Cells");
            arr.arraySize = cells.Length;
            for (int i = 0; i < cells.Length; i++)
            {
                var el = arr.GetArrayElementAtIndex(i);
                el.FindPropertyRelative("cell").vector3IntValue = cells[i].cell;
                el.FindPropertyRelative("materialId").intValue = cells[i].materialId;
                el.FindPropertyRelative("rotationStep").intValue = cells[i].rotationStep;
            }
            so.ApplyModifiedProperties();
            return data;
        }

        [Test]
        public void Lookup_Rebuilds_From_Cells()
        {
            var data = MakeData(
                new AnswerCell { cell = new Vector3Int(1, 0, 2), materialId = 5, rotationStep = 1 },
                new AnswerCell { cell = new Vector3Int(0, 1, 0), materialId = 7, rotationStep = 3 });

            Assert.IsTrue(data.TryGet(new Vector3Int(1, 0, 2), out var a));
            Assert.AreEqual(5, a.materialId);
            Assert.AreEqual((byte)1, a.rotationStep);

            Assert.IsTrue(data.TryGet(new Vector3Int(0, 1, 0), out var b));
            Assert.AreEqual(7, b.materialId);
            Assert.AreEqual((byte)3, b.rotationStep);

            Assert.IsFalse(data.TryGet(new Vector3Int(9, 9, 9), out _));
            Assert.AreEqual(2, data.Cells.Count);
        }

        [Test]
        public void OnAfterDeserialize_Invalidates_And_Rebuilds()
        {
            var data = MakeData(
                new AnswerCell { cell = new Vector3Int(2, 2, 2), materialId = 1, rotationStep = 0 });

            Assert.IsTrue(data.TryGet(new Vector3Int(2, 2, 2), out _));  // 1차 빌드
            data.OnAfterDeserialize();                                   // 리로드 시뮬 → 캐시 무효화
            Assert.IsTrue(data.TryGet(new Vector3Int(2, 2, 2), out var c)); // 지연 재빌드
            Assert.AreEqual(1, c.materialId);
        }
    }
}
