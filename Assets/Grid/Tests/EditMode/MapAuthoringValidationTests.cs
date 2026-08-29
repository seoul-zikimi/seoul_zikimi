using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
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

        static IEnumerable<MaterialCatalog> MaterialCatalogs()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:MaterialCatalog"))
            {
                var c = AssetDatabase.LoadAssetAtPath<MaterialCatalog>(AssetDatabase.GUIDToAssetPath(guid));
                if (c != null) yield return c;
            }
        }

        /// <summary>한 카탈로그 안에서 MaterialId는 유일해야 한다.
        ///
        /// <para>MaterialCatalog.RebuildLookup은 m_ById[def.Id] = def 라 <b>뒤에 온 것이 이긴다</b> —
        /// 중복이 있으면 목록 순서가 곧 동작이 되어, 항목을 옮기기만 해도 게임이 달라진다.</para>
        ///
        /// <para>실제 사고: 구버전 DDP 절단 조각(03~09)과 신버전(10~30)이 id 43~49로 겹친 채 둘 다 등록돼,
        /// id 43·44가 엉뚱한 옛 조각으로 해석됐다. 배달·배치되는 조각 모양이 정답과 달라 100% 완성이 불가능했다.</para></summary>
        [Test]
        public void 재료_카탈로그에_id가_중복되지_않는다()
        {
            foreach (var cat in MaterialCatalogs())
            {
                var byId = new Dictionary<int, MaterialDef>();
                foreach (var d in cat.Materials)
                {
                    if (d == null) continue;
                    Assert.IsFalse(byId.TryGetValue(d.Id, out var prev),
                        $"[{cat.name}] MaterialId {d.Id}가 중복됨: '{(prev != null ? prev.name : "?")}' vs '{d.name}' — " +
                        "뒤에 온 것이 이겨서 엉뚱한 재료가 배달·배치됩니다. 안 쓰는 옛 정의를 카탈로그에서 빼세요.");
                    byId[d.Id] = d;
                }
            }
        }

        /// <summary>맵이 들고 있는 재료 정의와, 그 Id로 카탈로그가 되돌려주는 정의가 같아야 한다.
        ///
        /// <para>게임은 이 둘을 섞어 쓴다 — 주문 검증은 맵의 AvailableMaterials(참조)를 보지만,
        /// 실제 배달(MaterialDropField)과 배치(GridNetwork)는 Catalog.GetById(id)로 다시 찾는다.
        /// 둘이 어긋나면 "주문은 되는데 다른 물건이 온다".</para></summary>
        [Test]
        public void 주문가능_재료가_카탈로그에서_같은_정의로_되찾아진다()
        {
            var cats = MaterialCatalogs().ToList();
            if (cats.Count == 0) Assert.Ignore("MaterialCatalog 없음");

            foreach (var m in Maps())
                foreach (var d in m.AvailableMaterials)
                {
                    if (d == null) continue;
                    foreach (var cat in cats)
                    {
                        if (!cat.Materials.Contains(d)) continue;   // 그 재료를 담은 카탈로그만 검사
                        cat.RebuildLookup();
                        Assert.AreSame(d, cat.GetById(d.Id),
                            $"[{m.name}] 재료 '{d.name}'(id {d.Id})를 카탈로그 '{cat.name}'에서 되찾으면 " +
                            $"'{(cat.GetById(d.Id) != null ? cat.GetById(d.Id).name : "null")}'이 나옵니다 — " +
                            "id가 겹쳤습니다. 주문한 것과 다른 재료가 배달·배치됩니다.");
                    }
                }
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
                    if (LotteSpots.IsMarkerOnly(t.name)) continue;   // 퍼레이드 웨이포인트(Spot_ParadePoint0…N) — 번호가 붙어 접두사로 판정
                    if (DdpSpots.IsMarkerOnly(t.name)) continue;    // DDP 물길·발굴터·장미 발판(Spot_WaterChannel0…N 등) — 〃
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

        // ── 접지 보정(QA: "맵마다 망치박스가 땅에 쳐박혀있어요") ─────────────────────────
        // Spot_ 마커 Y = 오브젝트가 놓일 '바닥 높이'. 작업대는 1×1×1 큐브라 마커 자리에 중심을 놓으면
        // 아래 절반이 묻힌다 → MapLoader.GroundedSpotPosition이 반높이만큼 올려준다.
        // 맵툴마다 +0.3 같은 보정값을 손으로 넣던 관행이 다시 새어 들어오지 않게 계산만 못 박아 둔다.

        static GameObject Station(Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);   // 작업대 프리팹과 같은 구성(콜라이더 있는 1×1×1 큐브)
            go.transform.localScale = scale;
            return go;
        }

        static float BottomY(GameObject go, Vector3 placed) => placed.y - 0.5f * go.transform.lossyScale.y;

        [Test]
        public void 스테이션_마커에_놓으면_하단이_지면에_닿는다()
        {
            var go = Station(Vector3.one);
            try
            {
                const float ground = -4f;   // DDP 어울림광장처럼 지면이 0이 아닌 맵도 같아야 한다
                var placed = MapLoader.GroundedSpotPosition(go, new Vector3(1f, ground, -20f));

                Assert.GreaterOrEqual(BottomY(go, placed), ground - 0.001f,
                    "작업대 하단이 지면보다 아래 — 마커 Y를 '접지점'이 아니라 '중심'으로 쓰고 있습니다(땅에 파묻힘).");
                Assert.AreEqual(ground, BottomY(go, placed), 0.001f, "하단이 지면에 정확히 닿아야 합니다(뜨지도 묻히지도 않게).");
                Assert.AreEqual(1f, placed.x, 0.001f, "X/Z는 마커 그대로여야 합니다.");
                Assert.AreEqual(-20f, placed.z, 0.001f, "X/Z는 마커 그대로여야 합니다.");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void 스테이션_크기를_바꿔도_하단이_지면에_닿는다()
        {
            var go = Station(new Vector3(1f, 2.4f, 1f));
            try
            {
                var placed = MapLoader.GroundedSpotPosition(go, new Vector3(0f, 0f, 0f));
                Assert.AreEqual(0f, BottomY(go, placed), 0.001f,
                    "스케일을 키우면 그만큼 더 올라가야 합니다 — 반높이가 하드코딩되면 큰 작업대가 다시 묻힙니다.");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void 콜라이더_없는_마커는_보정하지_않는다()
        {
            var go = new GameObject("PlayerSpawnPoint");   // 스폰·배송 마커는 위치만 쓰는 빈 오브젝트
            try
            {
                var spot = new Vector3(2f, -2.5f, -10f);
                Assert.AreEqual(spot, MapLoader.GroundedSpotPosition(go, spot),
                    "빈 마커까지 올리면 플레이어 스폰이 공중에 뜹니다 — 콜라이더가 있는 대상만 보정합니다.");
            }
            finally { Object.DestroyImmediate(go); }
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
