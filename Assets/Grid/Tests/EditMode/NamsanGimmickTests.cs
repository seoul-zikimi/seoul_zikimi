using NUnit.Framework;
using UnityEngine;

namespace GridSystem.Tests
{
    /// <summary>
    /// 남산 기믹의 순수 계산 검증 — 돌풍 높이 밴드, 케이블카 경로 보간, 설정 정합.
    /// 네트워크·씬이 필요한 부분은 샌드박스/플레이 테스트로 검증한다(TEST_GUIDE 참조).
    /// </summary>
    public class NamsanGimmickTests
    {
        private static NamsanGimmickConfig DefaultConfig()
        {
            var c = ScriptableObject.CreateInstance<NamsanGimmickConfig>();
            // 에셋 기본값 그대로(기획 확정 수치): 무풍<10, 약풍 10~14, 강풍 15+
            return c;
        }

        [Test]
        public void 돌풍_높이밴드_무풍_약풍_강풍_경계가_맞다()
        {
            var c = DefaultConfig();
            Assert.AreEqual(0f, c.PushCellsAtHeight(0f), "지상은 무풍이어야 한다");
            Assert.AreEqual(0f, c.PushCellsAtHeight(c.WeakWindMinHeight - 0.01f), "약풍 경계 바로 아래는 무풍");
            Assert.AreEqual(c.WeakPushCells, c.PushCellsAtHeight(c.WeakWindMinHeight), "약풍 시작 높이");
            Assert.AreEqual(c.WeakPushCells, c.PushCellsAtHeight(c.StrongWindMinHeight - 0.01f), "강풍 경계 바로 아래는 약풍");
            Assert.AreEqual(c.StrongPushCells, c.PushCellsAtHeight(c.StrongWindMinHeight), "강풍 시작 높이");
            Assert.AreEqual(c.StrongPushCells, c.PushCellsAtHeight(999f), "꼭대기는 강풍");
        }

        [Test]
        public void 돌풍_설정_기본값이_기획_확정치와_같다()
        {
            var c = DefaultConfig();
            Assert.AreEqual(10, c.WeakWindMinHeight);
            Assert.AreEqual(15, c.StrongWindMinHeight);
            // 8/12 남산 복구 커밋(56d8426a)에서 밀림 세기를 2/4 → 3/6칸으로 튜닝 — 확정치를 그 값으로 갱신(옛 초안값이 남아 있었음)
            Assert.AreEqual(3f, c.WeakPushCells);
            Assert.AreEqual(6f, c.StrongPushCells);
            Assert.AreEqual(3f, c.GustWarnSeconds);
            Assert.AreEqual(2f, c.StunSeconds);
            Assert.LessOrEqual(c.GustMinInterval, c.GustMaxInterval, "돌풍 주기 min ≤ max");
        }

        [Test]
        public void 케이블카_설정_기본값이_기획_확정치와_같다()
        {
            var c = DefaultConfig();
            Assert.AreEqual(3, c.CarCount);
            Assert.AreEqual(5f, c.CarFetchSeconds);
            Assert.AreEqual(3f, c.CarGapSeconds);
            Assert.AreEqual(2f, c.CarCloseSeconds);
            Assert.AreEqual(20f, c.CarTimeoutSeconds);
        }

        [Test]
        public void 케이블카_경로비율_페이즈별로_올바르다()
        {
            const float fetch = 5f, gap = 3f, ret = 3.5f;

            // 상행: 0 → 대기점, 시간에 따라 단조 증가
            float t0 = CableCarNetwork.PathT(CableCarNetwork.CarPhase.Inbound, 0f, fetch, gap, ret, 0);
            float tHalf = CableCarNetwork.PathT(CableCarNetwork.CarPhase.Inbound, fetch * 0.5f, fetch, gap, ret, 0);
            float tEnd = CableCarNetwork.PathT(CableCarNetwork.CarPhase.Inbound, fetch, fetch, gap, ret, 0);
            Assert.AreEqual(0f, t0, 1e-4f);
            Assert.Greater(tHalf, t0);
            Assert.Greater(tEnd, tHalf);

            // 도킹 완료·문 닫는 중 = 하차장(1)
            Assert.AreEqual(1f, CableCarNetwork.PathT(CableCarNetwork.CarPhase.Docked, 0f, fetch, gap, ret, 0), 1e-4f);
            Assert.AreEqual(1f, CableCarNetwork.PathT(CableCarNetwork.CarPhase.Closing, 1f, fetch, gap, ret, 0), 1e-4f);

            // 하행: 1 → 0
            Assert.AreEqual(1f, CableCarNetwork.PathT(CableCarNetwork.CarPhase.Returning, 0f, fetch, gap, ret, 0), 1e-4f);
            Assert.AreEqual(0f, CableCarNetwork.PathT(CableCarNetwork.CarPhase.Returning, ret, fetch, gap, ret, 0), 1e-4f);

            // 차고 대기 = 출발점(0)
            Assert.AreEqual(0f, CableCarNetwork.PathT(CableCarNetwork.CarPhase.AtBase, 99f, fetch, gap, ret, 0), 1e-4f);
        }

        [Test]
        public void 케이블카_위치보간_양끝이_마커와_일치한다()
        {
            var basePos = new Vector3(-12f, -1f, -8f);
            var station = new Vector3(-2f, 0f, -4f);

            var p0 = CableCarNetwork.CarPosAt(basePos, station, 0f);
            var p1 = CableCarNetwork.CarPosAt(basePos, station, 1f);

            // 수평(XZ)은 마커와 일치, 높이는 와이어-매달림 오프셋만큼 위
            Assert.AreEqual(basePos.x, p0.x, 1e-4f);
            Assert.AreEqual(basePos.z, p0.z, 1e-4f);
            Assert.AreEqual(station.x, p1.x, 1e-4f);
            Assert.AreEqual(station.z, p1.z, 1e-4f);
            Assert.Greater(p0.y, basePos.y, "곤돌라는 마커보다 위(와이어에 매달림)");
            Assert.Greater(p1.y, station.y);

            // t 범위 밖은 클램프
            var pn = CableCarNetwork.CarPosAt(basePos, station, -1f);
            Assert.AreEqual(p0, pn);
        }
    }
}
