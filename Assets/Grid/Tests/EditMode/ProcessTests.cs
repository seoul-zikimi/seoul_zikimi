using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GridSystem.Tests
{
    public class ProcessTests
    {
        readonly List<Object> m_Created = new();

        [TearDown]
        public void Cleanup()
        {
            foreach (var o in m_Created) if (o != null) Object.DestroyImmediate(o);
            m_Created.Clear();
        }

        MaterialDef MakeDef(int id, Vector3Int fp, params ProcessType[] required)
        {
            var def = ScriptableObject.CreateInstance<MaterialDef>();
            m_Created.Add(def);
            var so = new SerializedObject(def);
            so.FindProperty("m_Id").intValue = id;
            so.FindProperty("m_Footprint").vector3IntValue = fp;
            var list = so.FindProperty("m_RequiredProcesses");
            list.arraySize = required.Length;
            for (int i = 0; i < required.Length; i++)
                list.GetArrayElementAtIndex(i).intValue = (int)required[i];
            so.ApplyModifiedProperties();
            return def;
        }

        static RuntimeGrid Grid8() => new RuntimeGrid(new Vector3Int(8, 4, 8));

        MaterialDef Wall() => MakeDef(2, new Vector3Int(1, 3, 2), ProcessType.Fixed, ProcessType.Painted);

        [Test]
        public void Paint_BeforeFix_Rejected()
        {
            var g = Grid8();
            var wall = Wall();
            g.Place(Vector3Int.zero, wall, 0, 1);
            Assert.IsFalse(g.TryApplyProcess(Vector3Int.zero, ProcessType.Painted, wall));
        }

        [Test]
        public void Sequential_FixThenPaint_AppliesToAllCells()
        {
            var g = Grid8();
            var wall = Wall();
            g.Place(Vector3Int.zero, wall, 0, 1);

            Assert.IsTrue(g.TryApplyProcess(Vector3Int.zero, ProcessType.Fixed, wall));
            Assert.IsTrue(g.TryApplyProcess(Vector3Int.zero, ProcessType.Painted, wall));

            int full = (int)(ProcessType.Fixed | ProcessType.Painted);
            foreach (var cell in GridFootprint.EnumerateFootprintCells(Vector3Int.zero, new Vector3Int(1, 3, 2), 0))
                Assert.AreEqual(full, g.GetCell(cell).completedProcessMask, $"{cell} 공정 마스크");
        }

        [Test]
        public void Apply_AlreadyDone_Rejected()
        {
            var g = Grid8();
            var wall = Wall();
            g.Place(Vector3Int.zero, wall, 0, 1);
            Assert.IsTrue(g.TryApplyProcess(Vector3Int.zero, ProcessType.Fixed, wall));
            Assert.IsFalse(g.TryApplyProcess(Vector3Int.zero, ProcessType.Fixed, wall));
        }

        [Test]
        public void Cancel_ReverseOrder_Enforced()
        {
            var g = Grid8();
            var wall = Wall();
            g.Place(Vector3Int.zero, wall, 0, 1);
            g.TryApplyProcess(Vector3Int.zero, ProcessType.Fixed, wall);
            g.TryApplyProcess(Vector3Int.zero, ProcessType.Painted, wall);

            Assert.IsFalse(g.TryCancelProcess(Vector3Int.zero, ProcessType.Fixed));   // 페인트 남아있음 → 거부
            Assert.IsTrue(g.TryCancelProcess(Vector3Int.zero, ProcessType.Painted));  // 뒤부터
            Assert.IsTrue(g.TryCancelProcess(Vector3Int.zero, ProcessType.Fixed));
            Assert.AreEqual(0, g.GetCell(Vector3Int.zero).completedProcessMask);
        }

        [Test]
        public void Apply_OnEmptyCell_Rejected()
        {
            var g = Grid8();
            var wall = Wall();
            Assert.IsFalse(g.TryApplyProcess(new Vector3Int(5, 0, 5), ProcessType.Fixed, wall));
        }

        [Test]
        public void Apply_NonRequiredProcess_Allowed()
        {
            // Floor(요구 공정 없음)에 Fixed 적용 — 허용(감점 없음 모델), 앞선 필수 공정이 없으므로 통과
            var g = Grid8();
            var floor = MakeDef(0, Vector3Int.one);
            g.Place(Vector3Int.zero, floor, 0, 1);
            Assert.IsTrue(g.TryApplyProcess(Vector3Int.zero, ProcessType.Fixed, floor));
        }
    }
}
