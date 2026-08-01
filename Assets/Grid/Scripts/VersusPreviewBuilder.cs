using UnityEngine;

namespace GridSystem
{
    /// <summary>
    /// 2vs2 확인용 임시 구조물 생성기(에디터 미리보기·샌드박스 Play 공용).
    /// 실제 맵 없이 대칭 규칙만 눈으로 보려고 쓰는 물건이라 전부 비주얼 전용(콜라이더 없음).
    /// </summary>
    public static class VersusPreviewBuilder
    {
        /// <summary>일부러 한쪽으로 치우친 더미 배경 + Spot_ 마커 5종(핀으로 표시).
        /// 미러 복제가 제대로 돌았는지 좌우를 비교해 바로 알 수 있다.</summary>
        public static GameObject BuildDummyBackground(Vector3Int zone)
        {
            float u = GridContract.Unit;
            Vector3 o = GridCoordinates.CellToWorld(Vector3Int.zero);
            Vector3 At(float cx, float cz, float y = 0f) => new Vector3(o.x + cx * u, o.y + y, o.z + cz * u);

            var bg = new GameObject("DummyBackground");

            Block(bg, "Dummy_Tower", At(-2f, 2f), new Vector3(1.2f, 4f, 1.2f), new Color(0.55f, 0.6f, 0.7f));
            Block(bg, "Dummy_LongWall", At(zone.x * 0.5f, -2f), new Vector3(zone.x * u, 1.5f, 0.6f), new Color(0.7f, 0.65f, 0.55f));
            Block(bg, "Dummy_Step", At(1f, zone.z + 1.5f), new Vector3(2f, 0.8f, 1f), new Color(0.5f, 0.7f, 0.55f));
            Block(bg, "Dummy_Corner", At(-3.5f, zone.z * 0.5f), new Vector3(0.8f, 1.2f, 0.8f), new Color(0.9f, 0.5f, 0.3f));

            Spot(bg, "Spot_GridManager", At(0f, 0f));
            Spot(bg, "Spot_PaintStation", At(-2f, zone.z - 1f));
            Spot(bg, "Spot_HammerStation", At(-2f, 1f));
            Spot(bg, "Spot_PlayerSpawnPoint", At(zone.x * 0.5f, -1f));
            Spot(bg, "Spot_DeliveryZone", At(zone.x + 1.5f, zone.z * 0.5f));

            return bg;
        }

        /// <summary>가운데 투명벽 + 팀 구역 바닥 표시(실제 런타임 벽과 같은 자리).</summary>
        public static GameObject BuildOverlay(Vector3Int zone, Vector3Int effective)
        {
            float u = GridContract.Unit;
            Vector3 baseW = GridCoordinates.CellToWorld(Vector3Int.zero);
            var root = new GameObject("~VersusOverlay");

            var wall = Prim(root, "~CenterWall",
                new Vector3(baseW.x + zone.x * u, baseW.y + zone.y * u * 0.5f, baseW.z + effective.z * 0.5f * u),
                new Vector3(0.2f, zone.y * u, effective.z * u));
            Paint(wall, new Color(0.7f, 0.85f, 1f, 0.15f), true);

            var a = Prim(root, "~ZoneA",
                new Vector3(baseW.x + zone.x * u * 0.5f, baseW.y + 0.02f, baseW.z + effective.z * u * 0.5f),
                new Vector3(zone.x * u, 0.02f, effective.z * u));
            Paint(a, new Color(0.35f, 0.6f, 1f, 0.25f), true);

            var b = Prim(root, "~ZoneB",
                new Vector3(baseW.x + zone.x * u * 1.5f, baseW.y + 0.02f, baseW.z + effective.z * u * 0.5f),
                new Vector3(zone.x * u, 0.02f, effective.z * u));
            Paint(b, new Color(1f, 0.5f, 0.35f, 0.25f), true);

            GridLines(root, baseW, effective, u);
            return root;
        }

        // 건축 그리드 격자 — 그리드는 하나지만 2vs2에서 X로 2배 늘어나 양 팀 구역을 모두 덮는다.
        // (그래서 GridManager를 팀마다 두지 않는다) 그 사실이 눈에 보이도록 셀 선을 그린다.
        static void GridLines(GameObject root, Vector3 baseW, Vector3Int effective, float u)
        {
            var col = new Color(1f, 1f, 1f, 0.35f);
            float w = effective.x * u, d = effective.z * u;

            for (int x = 0; x <= effective.x; x++)
            {
                var line = Prim(root, $"~GridLineX{x}",
                    new Vector3(baseW.x + x * u, baseW.y + 0.04f, baseW.z + d * 0.5f),
                    new Vector3(0.04f, 0.02f, d));
                Paint(line, col, true);
            }
            for (int z = 0; z <= effective.z; z++)
            {
                var line = Prim(root, $"~GridLineZ{z}",
                    new Vector3(baseW.x + w * 0.5f, baseW.y + 0.04f, baseW.z + z * u),
                    new Vector3(w, 0.02f, 0.04f));
                Paint(line, col, true);
            }
        }

        // ── 헬퍼 ────────────────────────────────────────────────
        static void Block(GameObject parent, string name, Vector3 pos, Vector3 scale, Color col)
        {
            var go = Prim(parent, name, pos + Vector3.up * (scale.y * 0.5f), scale);
            Paint(go, col, false);
        }

        // 마커는 빈 오브젝트지만 어디 있는지 보이도록 핀을 자식으로 붙인다.
        static void Spot(GameObject parent, string name, Vector3 pos)
        {
            var spot = new GameObject(name);
            spot.transform.SetParent(parent.transform, true);
            spot.transform.position = pos;

            var pin = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pin.name = "~pin";
            pin.transform.SetParent(spot.transform, false);
            pin.transform.localPosition = Vector3.up * 0.6f;
            pin.transform.localScale = new Vector3(0.25f, 0.6f, 0.25f);
            KillCollider(pin);
            Paint(pin, new Color(1f, 0.9f, 0.25f), false);
        }

        static GameObject Prim(GameObject parent, string name, Vector3 pos, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent.transform, true);
            go.transform.position = pos;
            go.transform.localScale = scale;
            KillCollider(go);
            return go;
        }

        static void KillCollider(GameObject go)
        {
            var c = go.GetComponent<Collider>();
            if (c == null) return;
            if (Application.isPlaying) Object.Destroy(c); else Object.DestroyImmediate(c);
        }

        static void Paint(GameObject go, Color col, bool transparent)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) return;
            var m = new Material(sh) { color = col };
            if (transparent)
            {
                m.SetFloat("_Surface", 1f);
                m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.SetInt("_ZWrite", 0);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }
            go.GetComponent<Renderer>().sharedMaterial = m;
        }
    }
}
