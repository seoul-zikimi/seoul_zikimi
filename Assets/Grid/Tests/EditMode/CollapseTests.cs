using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GridSystem.Tests
{
    /// <summary>무너짐(F) — 결정론적 지지 그래프 + 연쇄. 고정됨은 앵커.</summary>
    public class CollapseTests
    {
        readonly List<Object> m_Created = new();

        [TearDown]
        public void Cleanup()
        {
            foreach (var o in m_Created) if (o != null) Object.DestroyImmediate(o);
            m_Created.Clear();
        }

        MaterialDef MakeDef(int id, Vector3Int fp)
        {
            var def = ScriptableObject.CreateInstance<MaterialDef>();
            m_Created.Add(def);
            var so = new SerializedObject(def);
            so.FindProperty("m_Id").intValue = id;
            so.FindProperty("m_Footprint").vector3IntValue = fp;
            so.ApplyModifiedProperties();
            return def;
        }

        static RuntimeGrid Grid8() => new RuntimeGrid(new Vector3Int(8, 4, 8));

        // 1x1x1 블록을 owner로 배치. fix=true면 고정 공정까지 적용(앵커).
        MaterialDef PlaceCube(RuntimeGrid g, Vector3Int at, ulong owner, bool fix = false)
        {
            var def = MakeDef((int)owner, Vector3Int.one);
            g.Place(at, def, 0, owner);
            if (fix) g.TryApplyProcess(at, ProcessType.Fixed, def);
            return def;
        }

        static HashSet<ulong> Owners(List<CollapsedObject> r)
        {
            var s = new HashSet<ulong>();
            foreach (var c in r) s.Add(c.ownerObjectId);
            return s;
        }

        [Test]
        public void Shock_UnfixedGroundedBlock_Collapses()
        {
            var g = Grid8();
            PlaceCube(g, new Vector3Int(0, 0, 0), 1);          // 바닥에 있어도 외부충격이면 무너짐
            var r = g.Collapse(new Vector3Int(0, 0, 0));
            Assert.AreEqual(1, r.Count);
            Assert.IsFalse(g.IsOccupied(new Vector3Int(0, 0, 0)));
        }

        [Test]
        public void Shock_FixedBlock_DoesNotCollapse()
        {
            var g = Grid8();
            PlaceCube(g, new Vector3Int(0, 0, 0), 1, fix: true);
            var r = g.Collapse(new Vector3Int(0, 0, 0));
            Assert.AreEqual(0, r.Count);
            Assert.IsTrue(g.IsOccupied(new Vector3Int(0, 0, 0)));
        }

        [Test]
        public void Chain_UnfixedStack_AllFall()
        {
            var g = Grid8();
            PlaceCube(g, new Vector3Int(0, 0, 0), 1);          // base
            PlaceCube(g, new Vector3Int(0, 1, 0), 2);          // top (base가 받침)
            var r = g.Collapse(new Vector3Int(0, 0, 0));        // base에 충격
            Assert.AreEqual(new HashSet<ulong> { 1, 2 }, Owners(r));   // base + 위 블록 연쇄
        }

        [Test]
        public void Chain_FixedMiddle_StopsCascade()
        {
            var g = Grid8();
            PlaceCube(g, new Vector3Int(0, 0, 0), 1);              // base 미고정
            PlaceCube(g, new Vector3Int(0, 1, 0), 2, fix: true);  // middle 고정(앵커)
            PlaceCube(g, new Vector3Int(0, 2, 0), 3);             // top 미고정 (middle이 받침)
            var r = g.Collapse(new Vector3Int(0, 0, 0));

            Assert.AreEqual(new HashSet<ulong> { 1 }, Owners(r));   // base만 무너짐
            Assert.IsTrue(g.IsOccupied(new Vector3Int(0, 1, 0)), "고정 middle은 떠서 유지");
            Assert.IsTrue(g.IsOccupied(new Vector3Int(0, 2, 0)), "top은 middle이 받쳐 유지");
        }

        [Test]
        public void MultiSupport_LosingOneSupport_TopSurvives()
        {
            var g = Grid8();
            PlaceCube(g, new Vector3Int(0, 0, 0), 1);          // 기둥 A
            PlaceCube(g, new Vector3Int(2, 0, 0), 2);          // 기둥 B
            var beam = MakeDef(3, new Vector3Int(3, 1, 1));    // x=0,1,2 가로보
            g.Place(new Vector3Int(0, 1, 0), beam, 0, 3);

            var r = g.Collapse(new Vector3Int(0, 0, 0));        // A만 충격
            Assert.AreEqual(new HashSet<ulong> { 1 }, Owners(r));   // 보는 B가 여전히 받침
            Assert.IsTrue(g.IsOccupied(new Vector3Int(2, 1, 0)), "보 유지");
        }

        [Test]
        public void FindUnfixedSupportsUnder_ReportsUnfixedBase()
        {
            var g = Grid8();
            PlaceCube(g, new Vector3Int(0, 0, 0), 1);          // 미고정 기둥
            PlaceCube(g, new Vector3Int(0, 1, 0), 2);          // 위에 놓음
            var t = g.FindUnfixedSupportsUnder(2);
            Assert.AreEqual(1, t.Count);
            Assert.AreEqual(new Vector3Int(0, 0, 0), t[0]);
        }

        [Test]
        public void FindUnfixedSupportsUnder_FixedBase_Empty()
        {
            var g = Grid8();
            PlaceCube(g, new Vector3Int(0, 0, 0), 1, fix: true);   // 고정 기둥
            PlaceCube(g, new Vector3Int(0, 1, 0), 2);
            Assert.AreEqual(0, g.FindUnfixedSupportsUnder(2).Count);   // 고정 위는 트리거 X
        }

        // ── A. 하드닝 ──────────────────────────────────────────────────────
        [Test]
        public void WouldBeSupported_Ground_True()
        {
            var g = Grid8();
            var cube = MakeDef(0, Vector3Int.one);
            Assert.IsTrue(g.WouldBeSupported(new Vector3Int(3, 0, 3), cube, 0));   // y=0 바닥
        }

        [Test]
        public void WouldBeSupported_Floating_False()
        {
            var g = Grid8();
            var cube = MakeDef(0, Vector3Int.one);
            Assert.IsFalse(g.WouldBeSupported(new Vector3Int(3, 2, 3), cube, 0));  // 아래 빈 공중
        }

        [Test]
        public void WouldBeSupported_OnExisting_True()
        {
            var g = Grid8();
            PlaceCube(g, new Vector3Int(3, 0, 3), 1);             // 받침
            var cube = MakeDef(2, Vector3Int.one);
            Assert.IsTrue(g.WouldBeSupported(new Vector3Int(3, 1, 3), cube, 0));   // 위에 얹음
        }

        [Test]
        public void Settle_AfterBaseRemoved_TopFalls()
        {
            var g = Grid8();
            PlaceCube(g, new Vector3Int(0, 0, 0), 1);            // base
            PlaceCube(g, new Vector3Int(0, 1, 0), 2);            // top (base가 받침)
            g.Remove(new Vector3Int(0, 0, 0));                    // 받침 철거(수동)
            var r = g.SettleUnsupported();
            Assert.AreEqual(new HashSet<ulong> { 2 }, Owners(r));   // 지지 잃은 top 연쇄
            Assert.IsFalse(g.IsOccupied(new Vector3Int(0, 1, 0)));
        }

        [Test]
        public void Settle_FixedTop_StaysFloating()
        {
            var g = Grid8();
            PlaceCube(g, new Vector3Int(0, 0, 0), 1);            // base
            PlaceCube(g, new Vector3Int(0, 1, 0), 2, fix: true); // top 고정(앵커)
            g.Remove(new Vector3Int(0, 0, 0));
            Assert.AreEqual(0, g.SettleUnsupported().Count);       // 고정 top은 떠서 유지
            Assert.IsTrue(g.IsOccupied(new Vector3Int(0, 1, 0)));
        }
    }
}
