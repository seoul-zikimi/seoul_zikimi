using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace GridSystem.Tests
{
    /// <summary>
    /// 기획자가 만든 맵 데이터가 가이드(Assets/Docs/MAP_ADD_GUIDE_기획자용.md)대로 세팅됐는지 검사.
    /// 코드 버그가 아니라 "데이터 실수"를 잡는 테스트 — 새 맵 추가 후 Test Runner 한 번 돌리면 끝.
    /// </summary>
    public class MapAuthoringValidationTests
    {
        // 모든 맵에 반드시 있어야 하는 마커 5종
        static readonly string[] kRequiredSpots =
        {
            "GridManager", "PaintStation", "HammerStation", "PlayerSpawnPoint", "DeliveryZone",
        };

        // 특정 맵에서만 쓰는 선택 마커(남산 기믹 등) — 없어도 되지만 이름은 정확해야 한다
        static readonly string[] kOptionalSpots =
        {
            "CableCarStation", "CableCarOrigin", "ElevatorLower", "ElevatorUpper",
        };

        // 인식되는 전체 마커 이름 — 여기 없는 이름은 오타로 간주(조용히 무시되면 못 찾음)
        static readonly string[] kKnownSpots = kRequiredSpots.Concat(kOptionalSpots).ToArray();

        static MapCatalog Catalog() => Resources.Load<MapCatalog>("MapCatalog");

        static IEnumerable<MapDef> Maps()
        {
            var c = Catalog();
            if (c == null) yield break;
            foreach (var m in c.Maps) if (m != null) yield return m;
        }

        [Test]
        public void 카탈로그가_있고_맵이_한개_이상이다()
        {
            var c = Catalog();
            Assert.IsNotNull(c, "Resources/MapCatalog.asset이 없음 — Tools ▸ Map ▸ Extract Background To Map을 한 번 실행하세요.");
            Assert.Greater(c.Count, 0, "맵 목록이 비었음 — Extract 툴로 맵을 등록하세요.");
        }

        [Test]
        public void 카탈로그에_빈칸이_없다()
        {
            var c = Catalog();
            if (c == null) Assert.Ignore("카탈로그 없음");
            for (int i = 0; i < c.Maps.Count; i++)
                Assert.IsNotNull(c.Maps[i], $"MapCatalog {i}번 칸이 비었음(None) — 지우거나 맵 카드를 넣으세요.");
        }

        [Test]
        public void 모든_맵에_배경_프리팹이_연결돼_있다()
        {
            foreach (var m in Maps())
                Assert.IsNotNull(m.BackgroundPrefab, $"[{m.name}] Background Prefab이 비었음 — 배경이 안 뜹니다.");
        }

        [Test]
        public void 맵이_둘_이상이면_각_맵에_정답이_들어있다()
        {
            var list = Maps().ToList();
            if (list.Count < 2) Assert.Ignore("맵이 1개뿐 — 공용 정답 목록으로 충분");

            foreach (var m in list)
                Assert.Greater(m.Answers.Count, 0,
                    $"[{m.name}] Answers가 비었음 — 공용 목록으로 떨어져서 다른 맵의 정답이 나올 수 있습니다.");
        }

        [Test]
        public void 정답_목록에_빈칸이나_빈_정답이_없다()
        {
            foreach (var m in Maps())
                for (int i = 0; i < m.Answers.Count; i++)
                {
                    var a = m.Answers[i];
                    Assert.IsNotNull(a, $"[{m.name}] Answers {i}번 칸이 비었음(None).");
                    Assert.Greater(a.Cells.Count, 0, $"[{m.name}] 정답 '{a.name}'에 블록이 하나도 없음.");
                }
        }

        [Test]
        public void 주문가능_재료_목록에_빈칸이_없다()
        {
            foreach (var m in Maps())
                for (int i = 0; i < m.AvailableMaterials.Count; i++)
                    Assert.IsNotNull(m.AvailableMaterials[i],
                        $"[{m.name}] Available Materials {i}번 칸이 비었음(None) — 지우거나 재료를 넣으세요. " +
                        "목록 전체를 비우면 카탈로그의 모든 재료가 주문 가능해집니다.");
        }

        [Test]
        public void 정답에_쓰인_재료가_주문가능_목록에_다_있다()
        {
            foreach (var m in Maps())
            {
                if (m.AvailableMaterials.Count == 0) continue;   // 비면 카탈로그 전체 주문 가능 → 검사 불필요

                var orderable = new HashSet<int>();
                foreach (var d in m.AvailableMaterials) if (d != null) orderable.Add(d.Id);

                foreach (var ans in m.Answers)
                {
                    if (ans == null) continue;
                    foreach (var cell in ans.Cells)
                        Assert.IsTrue(orderable.Contains(cell.materialId),
                            $"[{m.name}] 정답 '{ans.name}'이 재료 id {cell.materialId}를 쓰는데 Available Materials에 없음 " +
                            "— 그 맵은 정답을 완성할 수 없습니다.");
                }
            }
        }

        [Test]
        public void 정답이_맵_건축영역_안에_들어간다()
        {
            foreach (var m in Maps())
            {
                if (!m.HasGridSize) continue;   // 씬 기본 크기 사용 → 여기선 검사 불가
                var gs = m.GridSize;
                foreach (var ans in m.Answers)
                {
                    if (ans == null) continue;
                    foreach (var c in ans.Cells)
                        Assert.IsTrue(c.cell.x >= 0 && c.cell.x < gs.x &&
                                      c.cell.y >= 0 && c.cell.y < gs.y &&
                                      c.cell.z >= 0 && c.cell.z < gs.z,
                            $"[{m.name}] 정답 '{ans.name}'의 셀 {c.cell}이 Grid Size {gs} 밖 — 그 자리는 지을 수 없어 만점이 불가능합니다. " +
                            "Grid Size를 키우거나 정답을 옮기세요.");
                }
            }
        }

        [Test]
        public void 맵_표시이름이_서로_겹치지_않는다()
        {
            var dup = Maps().GroupBy(m => m.DisplayName).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            Assert.IsEmpty(dup, $"로비 표시 이름이 중복됨: {string.Join(", ", dup)} — 어느 맵인지 구분이 안 됩니다.");
        }

        [Test]
        public void 배경_프리팹의_Spot_마커_이름에_오타가_없다()
        {
            foreach (var m in Maps())
            {
                if (m.BackgroundPrefab == null) continue;
                foreach (var t in m.BackgroundPrefab.GetComponentsInChildren<Transform>(true))
                {
                    if (!t.name.StartsWith("Spot_")) continue;
                    string target = t.name.Substring("Spot_".Length);
                    Assert.Contains(target, kKnownSpots,
                        $"[{m.name}] 마커 '{t.name}' 은(는) 인식되지 않는 이름 — 오타면 아무 일도 안 일어납니다. " +
                        $"가능한 이름: {string.Join(", ", kKnownSpots.Select(s => "Spot_" + s))}");
                }
            }
        }

        [Test]
        public void 배경_프리팹에_게임_시스템_오브젝트가_섞여있지_않다()
        {
            foreach (var m in Maps())
            {
                if (m.BackgroundPrefab == null) continue;
                var bg = m.BackgroundPrefab;
                Assert.IsNull(bg.GetComponentInChildren<GridManager>(true),
                    $"[{m.name}] 배경 프리팹 안에 GridManager가 들어있음 — 배경엔 꾸미기용만, 위치는 Spot_GridManager 마커로 지정하세요.");
                Assert.IsNull(bg.GetComponentInChildren<MaterialDepot>(true),
                    $"[{m.name}] 배경 프리팹 안에 MaterialDepot이 들어있음 — 배송 위치는 DeliveryPoint 빈 오브젝트로 지정하세요.");
            }
        }

        [Test]
        public void 모든_맵에_Spot_마커_다섯종이_다_있다()
        {
            foreach (var m in Maps())
            {
                if (m.BackgroundPrefab == null) continue;
                var found = new HashSet<string>();
                foreach (var t in m.BackgroundPrefab.GetComponentsInChildren<Transform>(true))
                    if (t.name.StartsWith("Spot_")) found.Add(t.name.Substring("Spot_".Length));

                // 남산 기믹 맵은 배송 지점 대신 케이블카 하차장을 쓴다.
                bool namsan = m.NamsanGimmicks != null;
                foreach (var need in kRequiredSpots)
                {
                    if (namsan && need == "DeliveryZone") continue;
                    Assert.IsTrue(found.Contains(need),
                        $"[{m.name}] Spot_{need} 마커가 없음 — 이 맵에서는 해당 오브젝트가 안 나오거나 공용 위치에 남습니다.");
                }
                if (namsan)
                    Assert.IsTrue(found.Contains("CableCarStation"),
                        $"[{m.name}] 남산 기믹 맵인데 Spot_CableCarStation(케이블카 하차장) 마커가 없음 — 재료를 받을 곳이 없습니다.");
            }
        }

        [Test]
        public void 레거시_DeliveryPoint가_남아있지_않다()
        {
            foreach (var m in Maps())
            {
                if (m.BackgroundPrefab == null) continue;
                foreach (var t in m.BackgroundPrefab.GetComponentsInChildren<Transform>(true))
                    Assert.AreNotEqual("DeliveryPoint", t.name,
                        $"[{m.name}] 레거시 'DeliveryPoint'가 남아 있음 — 배송 지점은 {MaterialDepot.kSpotName} 마커로 지정합니다. " +
                        "Tools ▸ Map ▸ 레거시 DeliveryPoint 정리 로 지우세요.");
            }
        }
    }
}
