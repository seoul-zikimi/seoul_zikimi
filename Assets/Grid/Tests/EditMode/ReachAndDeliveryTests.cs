using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace GridSystem.Tests
{
    /// <summary>사거리(손 닿는 거리)와 배송 좌표 클램프 회귀 테스트.</summary>
    public class ReachAndDeliveryTests
    {
        const float U = 1f;
        static readonly Vector3 kZero = Vector3.zero;

        static List<Vector3Int> Square(int x0, int z0, int w, int d)
        {
            var l = new List<Vector3Int>();
            for (int x = 0; x < w; x++)
                for (int z = 0; z < d; z++)
                    l.Add(new Vector3Int(x0 + x, 0, z0 + z));
            return l;
        }

        [Test]
        public void 셀_안에_서_있으면_거리는_0()
        {
            float d = GridReach.DistanceToCell(new Vector3(3.5f, 0f, 2.5f), new Vector3Int(3, 0, 2), kZero, U);
            Assert.AreEqual(0f, d, 1e-4f);
        }

        [Test]
        public void 가까운_칸은_닿고_먼_칸은_안_닿는다()
        {
            var player = new Vector3(0.5f, 0f, 0.5f);   // (0,0,0)칸 한가운데
            Assert.IsTrue(GridReach.InReach(player, Square(2, 0, 1, 1), kZero, U, 2f), "2칸 거리는 닿아야 함");
            Assert.IsFalse(GridReach.InReach(player, Square(5, 0, 1, 1), kZero, U, 2f), "5칸 거리는 안 닿아야 함");
        }

        [Test]
        public void 큰_블록은_중심이_멀어도_가장자리에_서면_닿는다()
        {
            // 9x9 블록(중심은 4.5칸 밖) 바로 옆에 선 상황 — 중심 기준 판정이면 영원히 못 닿던 케이스
            var big = Square(1, 0, 9, 9);
            var player = new Vector3(0.5f, 0f, 0.5f);   // 블록 왼쪽 칸에 인접

            var center = new Vector3(1f + 4.5f, 0f, 4.5f);
            Assert.Greater(Vector3.Distance(player, center), 2f * U, "전제: 중심까지는 사거리 밖");

            Assert.IsTrue(GridReach.InReach(player, big, kZero, U, 2f), "인접해 있으면 닿아야 함");
        }

        [Test]
        public void 큰_블록도_충분히_멀면_안_닿는다()
        {
            var big = Square(10, 0, 9, 9);
            Assert.IsFalse(GridReach.InReach(new Vector3(0.5f, 0f, 0.5f), big, kZero, U, 2f));
        }

        [Test]
        public void 그리드_원점이_옮겨져도_사거리가_같이_따라간다()
        {
            var origin = new Vector3(100f, 0f, -40f);   // 맵 마커(Spot_GridManager)로 그리드 이동한 상황
            var player = origin + new Vector3(0.5f, 0f, 0.5f);
            Assert.IsTrue(GridReach.InReach(player, Square(1, 0, 1, 1), origin, U, 2f));
            Assert.IsFalse(GridReach.InReach(player, Square(9, 0, 1, 1), origin, U, 2f));
        }

        [Test]
        public void 멀리_옮긴_배송지점이_잘려나가지_않는다()
        {
            // 회귀: 예전엔 셀 개수를 월드 좌표처럼 클램프해서, 원점 밖 그리드에선 배송 지점이 통째로 잘렸다.
            var origin = new Vector3(120f, 0f, 80f);
            var size = new Vector3Int(8, 6, 8);
            var wanted = new Vector3(140f, 0.5f, 95f);   // 그리드에서 좀 떨어진 배송 지점

            var got = MaterialDropField.ClampToFloorWorld(wanted, size, origin, U, 60f);
            Assert.AreEqual(wanted.x, got.x, 1e-3f);
            Assert.AreEqual(wanted.z, got.z, 1e-3f);
        }

        [Test]
        public void 킥은_그리드_주변으로_제한된다()
        {
            var origin = new Vector3(120f, 0f, 80f);
            var size = new Vector3Int(8, 6, 8);
            var far = new Vector3(500f, 0.5f, -500f);

            var got = MaterialDropField.ClampToFloorWorld(far, size, origin, U, 6f);
            Assert.LessOrEqual(got.x, origin.x + size.x * U + 6f + 1e-3f);
            Assert.GreaterOrEqual(got.z, origin.z - 6f - 1e-3f);
        }
    }
}
