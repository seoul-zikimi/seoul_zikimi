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
    [InitializeOnLoad]
    public static class VersusPreviewTool
    {
        const string kRootName = "~VersusPreview";

        // 미리보기는 '멈춘 씬에서 눈으로 보는 용도'다. Play로 들어가면 샌드박스 블록 등과 겹쳐 헷갈리므로 자동으로 치운다.
        static VersusPreviewTool()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state != PlayModeStateChange.ExitingEditMode) return;
                if (GameObject.Find(kRootName) == null) return;
                Clear();
                Debug.Log("[2vs2 미리보기] Play 시작 — 미리보기를 정리했습니다(멈춘 상태에서만 보는 용도).");
            };
        }

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
        public static void PreviewDummy() => BuildPreview("더미 배경", _ => VersusPreviewBuilder.BuildDummyBackground(ZoneSize()));

        static Vector3Int ZoneSize()
        {
            var gm = Object.FindFirstObjectByType<GridManager>();
            return gm != null ? gm.ZoneSize : new Vector3Int(8, 6, 8);
        }

        static void BuildPreview(string label, System.Func<GameObject, GameObject> makeBackground)
        {
            Clear();

            var zone = ZoneSize();
            var effective = new Vector3Int(zone.x * 2, zone.y, zone.z);   // 2vs2 = X 2배

            var root = new GameObject(kRootName) { hideFlags = HideFlags.DontSave };   // 실수로 씬에 저장되지 않게
            var bg = makeBackground(root);
            bg.transform.SetParent(root.transform, true);

            bool authored = VersusBackground.IsAuthoredSymmetric(bg);
            if (!authored)
            {
                var mirror = VersusBackground.CreateMirror(bg, VersusBackground.MirrorPivot(zone, effective));
                if (mirror != null) mirror.transform.SetParent(root.transform, true);
            }

            VersusPreviewBuilder.BuildOverlay(zone, effective).transform.SetParent(root.transform, true);

            Selection.activeGameObject = root;
            SceneView.lastActiveSceneView?.FrameSelected();

            Debug.Log($"[2vs2 미리보기] {label} — {(authored ? "전용 대칭 맵(VersusSymmetric): 복제 없음" : "일반 맵: 180° 미러 복제본 생성")}. " +
                      "확인 후 Tools ▸ Test ▸ 2vs2 미리보기 지우기 로 정리하세요(씬 저장 금지).");
        }

        [MenuItem("Tools/Test/2vs2 미리보기 지우기")]
        public static void Clear()
        {
            var root = GameObject.Find(kRootName);
            if (root != null) Object.DestroyImmediate(root);
        }

    }
}
