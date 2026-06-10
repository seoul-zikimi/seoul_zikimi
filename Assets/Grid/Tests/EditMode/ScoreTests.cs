using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GridSystem.Tests
{
    public class ScoreTests
    {
        readonly List<Object> m_Created = new();

        [TearDown]
        public void Cleanup()
        {
            foreach (var o in m_Created) if (o != null) Object.DestroyImmediate(o);
            m_Created.Clear();
        }

        MaterialDef MakeDef(int id, params ProcessType[] req)
        {
            var def = ScriptableObject.CreateInstance<MaterialDef>();
            m_Created.Add(def);
            var so = new SerializedObject(def);
            so.FindProperty("m_Id").intValue = id;
            so.FindProperty("m_Footprint").vector3IntValue = Vector3Int.one;
            var list = so.FindProperty("m_RequiredProcesses");
            list.arraySize = req.Length;
            for (int i = 0; i < req.Length; i++)
                list.GetArrayElementAtIndex(i).intValue = (int)req[i];
            so.ApplyModifiedProperties();
            return def;
        }

        MaterialCatalog MakeCatalog(params MaterialDef[] defs)
        {
            var cat = ScriptableObject.CreateInstance<MaterialCatalog>();
            m_Created.Add(cat);
            var so = new SerializedObject(cat);
            var mats = so.FindProperty("m_Materials");
            mats.arraySize = defs.Length;
            for (int i = 0; i < defs.Length; i++)
                mats.GetArrayElementAtIndex(i).objectReferenceValue = defs[i];
            so.ApplyModifiedProperties();
            cat.RebuildLookup();
            return cat;
        }

        MapAnswerData MakeAnswer(params (Vector3Int cell, int mat, int rot)[] cells)
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
                el.FindPropertyRelative("materialId").intValue = cells[i].mat;
                el.FindPropertyRelative("rotationStep").intValue = cells[i].rot;
            }
            so.ApplyModifiedProperties();
            return data;
        }

        static RuntimeGrid Grid8() => new RuntimeGrid(new Vector3Int(8, 4, 8));

        [Test]
        public void PerfectBuild_Is100Percent()
        {
            var floor = MakeDef(0);                      // 공정 없음
            var pillar = MakeDef(1, ProcessType.Fixed);
            var cat = MakeCatalog(floor, pillar);
            var answer = MakeAnswer((new Vector3Int(0, 0, 0), 0, 0),
                                    (new Vector3Int(1, 0, 0), 1, 0));

            var g = Grid8();
            g.Place(new Vector3Int(0, 0, 0), floor, 0, 1);
            g.Place(new Vector3Int(1, 0, 0), pillar, 0, 2);
            g.TryApplyProcess(new Vector3Int(1, 0, 0), ProcessType.Fixed, pillar);

            var s = g.ScoreAgainst(answer, cat);
            Assert.AreEqual(600, s.maxScore);
            Assert.AreEqual(600, s.score);
            Assert.AreEqual(1f, s.Ratio);
        }

        [Test]
        public void MissingProcess_LosesProcessPoints()
        {
            var pillar = MakeDef(1, ProcessType.Fixed);
            var cat = MakeCatalog(pillar);
            var answer = MakeAnswer((new Vector3Int(0, 0, 0), 1, 0));

            var g = Grid8();
            g.Place(new Vector3Int(0, 0, 0), pillar, 0, 1);  // Fixed 미적용

            var s = g.ScoreAgainst(answer, cat);
            Assert.AreEqual(300, s.maxScore);
            Assert.AreEqual(200, s.score);
            Assert.AreEqual(0, s.processCorrect);
        }

        [Test]
        public void RotationIgnored_WhenCellsMatch()
        {
            // 1×1×1은 회전해도 같은 칸 → step이 달라도 채점됨(시각상 동일).
            // (비대칭 부재는 회전이 틀리면 점유 칸이 달라져 재료 불일치로 자동 감점)
            var pillar = MakeDef(1, ProcessType.Fixed);
            var cat = MakeCatalog(pillar);
            var answer = MakeAnswer((new Vector3Int(0, 0, 0), 1, 0));  // 정답 rot 0

            var g = Grid8();
            g.Place(new Vector3Int(0, 0, 0), pillar, 2, 1);  // rot 2로 배치(같은 칸)
            g.TryApplyProcess(new Vector3Int(0, 0, 0), ProcessType.Fixed, pillar);

            var s = g.ScoreAgainst(answer, cat);
            Assert.AreEqual(300, s.score);  // 회전 무시 → 배치+공정 만점
        }

        [Test]
        public void EmptyGrid_IsZero()
        {
            var floor = MakeDef(0);
            var cat = MakeCatalog(floor);
            var answer = MakeAnswer((new Vector3Int(0, 0, 0), 0, 0));

            var s = Grid8().ScoreAgainst(answer, cat);
            Assert.AreEqual(0, s.score);
            Assert.AreEqual(300, s.maxScore);
            Assert.AreEqual(0f, s.Ratio);
        }
    }
}
