using UnityEditor;
using UnityEngine;

namespace GridSystem.EditorTools
{
    /// <summary>
    /// 2vs2 배경을 플레이 없이 씬 뷰에서 확인하는 미리보기.
    /// [메뉴] Tools ▸ Test ▸ 2vs2 배경 미리보기 — 맵 카드(MapDef)를 고르고 있으면 그 맵, 아니면 카탈로그 첫 맵.
    /// 배경 인스턴스 + (전용 대칭 맵이 아니면) 180° 미러 복제 + 가운데 투명벽 + 팀 구역 표시를 만든다.
    /// [메뉴] ... ▸ 미리보기 지우기 로 전부 제거(씬에 저장하지 말 것).
    /// </summary>
    public static class VersusPreviewTool
    {
        const string kRootName = "~VersusPreview";

        [MenuItem("Tools/Test/2vs2 배경 미리보기")]
        public static void Preview()
        {
            var def = Selection.activeObject as MapDef;
            if (def == null)
            {
                var catalog = Resources.Load<MapCatalog>("MapCatalog");
                def = catalog != null ? catalog.Get(0) : null;
            }
            if (def == null || def.BackgroundPrefab == null)
            {
                EditorUtility.DisplayDialog("2vs2 미리보기", "맵 카드(MapDef)나 배경 프리팹을 찾지 못했어요.\n" +
                    "Project에서 Map_이름 에셋을 선택한 뒤 다시 실행하세요.", "확인");
                return;
            }
            BuildPreview(def.DisplayName, root => (GameObject)PrefabUtility.InstantiatePrefab(def.BackgroundPrefab));
        }

        /// <summary>실제 맵 없이 임시 블록으로 만든 배경으로 대칭 규칙만 확인한다(맵 제작 전 검증용).</summary>
        [MenuItem("Tools/Test/2vs2 더미 배경 미리보기")]
        public static void PreviewDummy() => BuildPreview("더미 배경", _ => MakeDummyBackground());

        static void BuildPreview(string label, System.Func<GameObject, GameObject> makeBackground)
        {
            Clear();

            var gm = Object.FindFirstObjectByType<GridManager>();
            var zone = gm != null ? gm.ZoneSize : new Vector3Int(8, 6, 8);
            var effective = new Vector3Int(zone.x * 2, zone.y, zone.z);   // 2vs2 = X 2배
            float u = GridContract.Unit;

            var root = new GameObject(kRootName);
            var bg = makeBackground(root);
            bg.transform.SetParent(root.transform, true);

            bool authored = VersusBackground.IsAuthoredSymmetric(bg);
            if (!authored)
            {
                var mirror = VersusBackground.CreateMirror(bg, VersusBackground.MirrorPivot(zone, effective));
                if (mirror != null) mirror.transform.SetParent(root.transform, true);
            }

            // 가운데 투명벽(실제 런타임 벽과 같은 자리)
            Vector3 baseW = GridCoordinates.CellToWorld(Vector3Int.zero);
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "~CenterWallPreview";
            wall.transform.SetParent(root.transform, true);
            wall.transform.position = new Vector3(baseW.x + zone.x * u, baseW.y + zone.y * u * 0.5f,
                                                  baseW.z + effective.z * 0.5f * u);
            wall.transform.localScale = new Vector3(0.2f, zone.y * u, effective.z * u);
            Transparent(wall, new Color(0.7f, 0.85f, 1f, 0.15f));

            // 팀 구역 바닥 표시
            Zone(root, "~ZoneA", new Vector3(baseW.x + zone.x * u * 0.5f, baseW.y + 0.02f, baseW.z + effective.z * u * 0.5f),
                 new Vector3(zone.x * u, 0.02f, effective.z * u), new Color(0.35f, 0.6f, 1f, 0.25f));
            Zone(root, "~ZoneB", new Vector3(baseW.x + zone.x * u * 1.5f, baseW.y + 0.02f, baseW.z + effective.z * u * 0.5f),
                 new Vector3(zone.x * u, 0.02f, effective.z * u), new Color(1f, 0.5f, 0.35f, 0.25f));

            Selection.activeGameObject = root;
            SceneView.lastActiveSceneView?.FrameSelected();

            Debug.Log($"[2vs2 미리보기] {label} — {(authored ? "전용 대칭 맵(VersusSymmetric): 복제 없음" : "일반 맵: 180° 미러 복제본 생성")}. " +
                      "확인 후 Tools ▸ Test ▸ 2vs2 미리보기 지우기 로 정리하세요(씬 저장 금지).");
        }

        // ── 더미 배경: 실제 맵 없이도 대칭·마커 규칙을 눈으로 확인할 수 있는 임시 구조물 ──
        // 좌우 구분이 되도록 일부러 비대칭으로 만든다(미러가 제대로 돌았는지 바로 보임).
        static GameObject MakeDummyBackground()
        {
            var gm = Object.FindFirstObjectByType<GridManager>();
            var zone = gm != null ? gm.ZoneSize : new Vector3Int(8, 6, 8);
            float u = GridContract.Unit;
            Vector3 o = GridCoordinates.CellToWorld(Vector3Int.zero);

            var bg = new GameObject("DummyBackground");
            Vector3 At(float cx, float cz, float y = 0f) => new Vector3(o.x + cx * u, o.y + y, o.z + cz * u);

            // 팀A 구역 주변 랜드마크(비대칭 배치)
            Block(bg, "Dummy_Tower", At(-2f, 2f, 2f), new Vector3(1.2f, 4f, 1.2f), new Color(0.55f, 0.6f, 0.7f));
            Block(bg, "Dummy_LongWall", At(zone.x * 0.5f, -2f, 0.75f), new Vector3(zone.x * u, 1.5f, 0.6f), new Color(0.7f, 0.65f, 0.55f));
            Block(bg, "Dummy_Step", At(1f, zone.z + 1.5f, 0.4f), new Vector3(2f, 0.8f, 1f), new Color(0.5f, 0.7f, 0.55f));
            Block(bg, "Dummy_Marker_A", At(-3.5f, zone.z * 0.5f, 0.6f), new Vector3(0.8f, 1.2f, 0.8f), new Color(0.9f, 0.5f, 0.3f));

            // Spot 마커 5종 — 실제 맵에도 전부 있어야 하는 구성(눈에 보이게 작은 표식으로 그린다)
            Spot(bg, "Spot_GridManager", At(0f, 0f));
            Spot(bg, "Spot_PaintStation", At(-2f, zone.z - 1f, 0.5f));
            Spot(bg, "Spot_HammerStation", At(-2f, 1f, 0.5f));
            Spot(bg, "Spot_PlayerSpawnPoint", At(zone.x * 0.5f, -1f, 0.5f));
            Spot(bg, "Spot_DeliveryZone", At(zone.x + 1.5f, zone.z * 0.5f));

            return bg;
        }

        static void Block(GameObject parent, string name, Vector3 pos, Vector3 scale, Color col)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent.transform, true);
            go.transform.position = pos + Vector3.up * (scale.y * 0.5f);
            go.transform.localScale = scale;
            var c = go.GetComponent<Collider>(); if (c != null) Object.DestroyImmediate(c);
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh != null) go.GetComponent<Renderer>().sharedMaterial = new Material(sh) { color = col };
        }

        // 마커는 빈 오브젝트지만, 미리보기에서는 어디 있는지 보이도록 작은 표식을 자식으로 붙인다.
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
            var c = pin.GetComponent<Collider>(); if (c != null) Object.DestroyImmediate(c);
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh != null) pin.GetComponent<Renderer>().sharedMaterial = new Material(sh) { color = new Color(1f, 0.9f, 0.25f) };
        }

        [MenuItem("Tools/Test/2vs2 미리보기 지우기")]
        public static void Clear()
        {
            var root = GameObject.Find(kRootName);
            if (root != null) Object.DestroyImmediate(root);
        }

        static void Zone(GameObject root, string name, Vector3 pos, Vector3 scale, Color col)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(root.transform, true);
            go.transform.position = pos;
            go.transform.localScale = scale;
            Transparent(go, col);
        }

        static void Transparent(GameObject go, Color col)
        {
            var c = go.GetComponent<Collider>();
            if (c != null) Object.DestroyImmediate(c);
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) return;
            var m = new Material(sh) { color = col };
            m.SetFloat("_Surface", 1f);
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            go.GetComponent<Renderer>().sharedMaterial = m;
        }
    }
}
