using System.IO;
using UnityEditor;
using UnityEngine;

namespace GridSystem.EditorTools
{
    /// <summary>
    /// 남산 기믹 테스트 맵 원클릭 생성 — 배경 아트 없이 케이블카·엘리베이터·돌풍을 바로 플레이 테스트.
    /// 평평한 공터 + 마커(케이블카 하차장/출발점, 엘리베이터 상/하부 문) + 낮은 테스트 타워 정답 +
    /// 테스트용 NamsanGimmickConfig(낮은 높이 밴드)를 만들어 카탈로그에 등록한다.
    /// 몇 번을 다시 실행해도 같은 결과(기존 에셋 덮어쓰기) — VersusFieldMapTool과 같은 관례.
    ///
    /// 진짜 남산 맵을 만들 땐: 이 맵이 아니라 새 배경 프리팹에 같은 마커들을 배치하고,
    /// 실전 수치의 NamsanGimmickConfig를 맵 카드(Namsan Gimmicks 칸)에 꽂으면 된다.
    /// </summary>
    public static class NamsanTestMapTool
    {
        private const string kPrefabPath  = "Assets/Map/Prefabs/MapBg_NamsanTest.prefab";
        private const string kMapDefPath  = "Assets/Map/Maps/Map_NamsanTest.asset";
        private const string kAnswerPath  = "Assets/Map/Maps/Ans_NamsanTest.asset";
        private const string kConfigPath  = "Assets/Map/Maps/NamsanGimmickConfig_Test.asset";
        private const string kThumbPath   = "Assets/Map/Maps/Thumb_NamsanTest.png";
        private const string kMatDir      = "Assets/Map/Materials";
        private const string kCatalogPath = "Assets/Resources/MapCatalog.asset";
        // ⚠ 반드시 GameScene GridManager가 물고 있는 '전역 MaterialCatalog'에 든 재료여야 한다.
        //   (주문 검증이 전역 카탈로그 기준 — 목록 밖 재료는 주문이 조용히 무시된다)
        private const string kTowerMatPath = "Assets/Prefabs/Map/1_KwangTongGyo/8_ShortPost.asset";   // id 8, 1×1×1, 망치 공정

        private static readonly Vector3Int kGridSize = new Vector3Int(9, 14, 9);
        private const int kTowerHeight = 10;   // 정답: (4, 0..9, 4) 세로 기둥
        private const int kObservatoryMinY = 4, kObservatoryMaxY = 5;   // y 4~5 완성 → 엘베 개통
        private const int kWeakY = 3, kStrongY = 7;                     // 낮은 높이 밴드(테스트 전용)

        [MenuItem("Tools/Map/★ 남산 기믹 테스트 맵 생성")]
        public static void Generate()
        {
            var towerMat = AssetDatabase.LoadAssetAtPath<MaterialDef>(kTowerMatPath);
            if (towerMat == null) { Debug.LogError($"[NamsanTest] 재료 에셋이 없음: {kTowerMatPath}"); return; }

            // ① 테스트용 기믹 설정 — 낮은 타워에서도 밴드·개통이 다 보이게 수치를 낮춘다
            var cfg = AssetDatabase.LoadAssetAtPath<NamsanGimmickConfig>(kConfigPath);
            if (cfg == null)
            {
                cfg = ScriptableObject.CreateInstance<NamsanGimmickConfig>();
                Directory.CreateDirectory(Path.GetDirectoryName(kConfigPath));
                AssetDatabase.CreateAsset(cfg, kConfigPath);
            }
            cfg.ObservatoryMinY = kObservatoryMinY;
            cfg.ObservatoryMaxY = kObservatoryMaxY;
            cfg.WeakWindMinHeight = kWeakY;
            cfg.StrongWindMinHeight = kStrongY;
            EditorUtility.SetDirty(cfg);

            // ② 테스트 타워 정답(가운데 1×1 기둥 10칸)
            var answer = AssetDatabase.LoadAssetAtPath<MapAnswerData>(kAnswerPath);
            if (answer == null)
            {
                answer = ScriptableObject.CreateInstance<MapAnswerData>();
                Directory.CreateDirectory(Path.GetDirectoryName(kAnswerPath));
                AssetDatabase.CreateAsset(answer, kAnswerPath);
            }
            var ao = new SerializedObject(answer);
            ao.FindProperty("m_GridSize").vector3IntValue = kGridSize;
            ao.FindProperty("m_DisplayName").stringValue = "남산 테스트 타워";
            ao.FindProperty("m_TimeLimitSeconds").floatValue = 300f;
            var cells = ao.FindProperty("m_Cells");
            cells.arraySize = kTowerHeight;
            for (int y = 0; y < kTowerHeight; y++)
            {
                var e = cells.GetArrayElementAtIndex(y);
                e.FindPropertyRelative("cell").vector3IntValue = new Vector3Int(4, y, 4);
                e.FindPropertyRelative("materialId").intValue = towerMat.Id;
                e.FindPropertyRelative("rotationStep").intValue = 0;
            }
            ao.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(answer);

            // ③ 배경 프리팹(바닥 + 마커 + 상부 문 발판)
            var root = BuildRoot();
            Directory.CreateDirectory(Path.GetDirectoryName(kPrefabPath));
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, kPrefabPath, out bool ok);
            Object.DestroyImmediate(root);
            if (!ok) { Debug.LogError($"[NamsanTest] 프리팹 저장 실패: {kPrefabPath}"); return; }

            // ④ MapDef
            var def = AssetDatabase.LoadAssetAtPath<MapDef>(kMapDefPath);
            if (def == null)
            {
                def = ScriptableObject.CreateInstance<MapDef>();
                Directory.CreateDirectory(Path.GetDirectoryName(kMapDefPath));
                AssetDatabase.CreateAsset(def, kMapDefPath);
            }
            var so = new SerializedObject(def);
            so.FindProperty("m_DisplayName").stringValue = "남산 기믹 테스트";
            so.FindProperty("m_BackgroundPrefab").objectReferenceValue = prefab;
            so.FindProperty("m_GridSize").vector3IntValue = kGridSize;
            so.FindProperty("m_NamsanGimmicks").objectReferenceValue = cfg;
            var answers = so.FindProperty("m_Answers");
            answers.arraySize = 1;
            answers.GetArrayElementAtIndex(0).objectReferenceValue = answer;
            var mats = so.FindProperty("m_AvailableMaterials");
            mats.arraySize = 1;
            mats.GetArrayElementAtIndex(0).objectReferenceValue = towerMat;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(def);

            // ⑤ 썸네일 + 카탈로그 등록
            var thumb = MapThumbnailUtil.Capture(prefab, kThumbPath);
            if (thumb != null)
            {
                so.Update();
                so.FindProperty("m_Thumbnail").objectReferenceValue = thumb;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            var catalog = AssetDatabase.LoadAssetAtPath<MapCatalog>(kCatalogPath);
            if (catalog == null) { Debug.LogError($"[NamsanTest] MapCatalog이 없음: {kCatalogPath}"); return; }
            catalog.EditorAdd(def);
            EditorUtility.SetDirty(catalog);

            AssetDatabase.SaveAssets();
            Selection.activeObject = def;
            Debug.Log($"[NamsanTest] 완료 ✔ 로비에서 '남산 기믹 테스트'를 고르세요.\n" +
                      $"· 케이블카: 주문하면 곤돌라가 좌측 아래에서 올라옴(하차장 그리드 앞)\n" +
                      $"· 엘리베이터: 타워 y{kObservatoryMinY}~{kObservatoryMaxY} 완성 시 개통(오른쪽 문 ↔ 공중 발판)\n" +
                      $"· 돌풍: y{kWeakY}+ 약풍 / y{kStrongY}+ 강풍, 수치는 {kConfigPath}에서 조정");
        }

        // 좌표 기준: Spot_GridManager = (0,0,0) → 그리드가 x,z∈[0,9)를 덮는다.
        private static GameObject BuildRoot()
        {
            var root = new GameObject("MapBg_NamsanTest");

            var ground = AddBox(root, "Ground", new Vector3(4.5f, -0.5f, 4.5f), new Vector3(44f, 1f, 36f),
                EnsureMaterial("Mat_NamsanTestGround", new Color(0.55f, 0.68f, 0.5f)));   // 산꼭대기 풀밭 톤
            ground.isStatic = true;

            // 케이블카 출발점(산 아래) 쪽 낮은 단차 — 와이어가 기울어져 보이게
            var slope = AddBox(root, "LowerHill", new Vector3(-12f, -3f, -8f), new Vector3(10f, 4f, 10f),
                EnsureMaterial("Mat_NamsanTestRock", new Color(0.5f, 0.46f, 0.42f)));
            slope.isStatic = true;

            // 엘리베이터 상부 문 발판(공중) — 순간이동 도착 후 설 자리
            var deck = AddBox(root, "UpperDeck", new Vector3(12f, 6f, 4.5f), new Vector3(4f, 0.4f, 4f),
                EnsureMaterial("Mat_NamsanTestDeck", new Color(0.65f, 0.5f, 0.35f)));
            deck.isStatic = true;

            // 필수 마커(남산 맵은 DeliveryZone 대신 CableCarStation)
            AddSpot(root, "Spot_GridManager", new Vector3(0f, 0f, 0f));
            AddSpot(root, "Spot_PlayerSpawnPoint", new Vector3(4.5f, 0f, -3f));
            AddSpot(root, "Spot_HammerStation", new Vector3(-3f, 0f, 2f));
            AddSpot(root, "Spot_PaintStation", new Vector3(-3f, 0f, 6f));

            // 남산 기믹 마커
            AddSpot(root, "Spot_CableCarStation", new Vector3(-2f, 0f, -4f));      // 하차장(그리드 앞)
            AddSpot(root, "Spot_CableCarOrigin", new Vector3(-12f, -1f, -8f));     // 산 아래 출발점(단차 위)
            AddSpot(root, "Spot_ElevatorLower", new Vector3(12f, 0f, 2f));         // 지상 문
            AddSpot(root, "Spot_ElevatorUpper", new Vector3(12f, 6.2f, 4.5f));     // 공중 발판 위 문
            return root;
        }

        private static GameObject AddBox(GameObject root, string name, Vector3 pos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            if (mat != null) go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        private static void AddSpot(GameObject root, string name, Vector3 pos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = pos;
        }

        private static Material EnsureMaterial(string name, Color color)
        {
            Directory.CreateDirectory(kMatDir);
            string path = $"{kMatDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                var sh = Shader.Find("Universal Render Pipeline/Lit");
                if (sh == null) { Debug.LogWarning("[NamsanTest] URP Lit 셰이더를 못 찾음 — 기본 머티리얼 사용"); return null; }
                mat = new Material(sh);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetColor("_BaseColor", color);
            EditorUtility.SetDirty(mat);
            return mat;
        }
    }

    /// <summary>맵 썸네일 촬영(프리팹을 지하에 세워 촬영) — VersusFieldMapTool의 것과 동일 로직 공용화.</summary>
    public static class MapThumbnailUtil
    {
        public static Sprite Capture(GameObject prefab, string pngPath)
        {
            GameObject inst = null, camGo = null;
            RenderTexture rt = null;
            try
            {
                inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                inst.transform.position = new Vector3(0f, -5000f, 0f);

                var rends = inst.GetComponentsInChildren<Renderer>();
                if (rends.Length == 0) return null;
                var b = rends[0].bounds;
                foreach (var r in rends) b.Encapsulate(r.bounds);

                camGo = new GameObject("~ThumbCam");
                var cam = camGo.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.Skybox;
                cam.fieldOfView = 40f;
                float dist = b.size.magnitude * 0.75f;
                cam.transform.position = b.center + new Vector3(1f, 0.8f, -1f).normalized * dist;
                cam.transform.LookAt(b.center);
                cam.nearClipPlane = 0.1f;
                cam.farClipPlane = dist * 4f;

                rt = new RenderTexture(512, 512, 24);
                cam.targetTexture = rt;
                cam.Render();

                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(512, 512, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, 512, 512), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;

                File.WriteAllBytes(pngPath, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(pngPath);
                var imp = (TextureImporter)AssetImporter.GetAtPath(pngPath);
                imp.textureType = TextureImporterType.Sprite;
                imp.spriteImportMode = SpriteImportMode.Single;
                imp.SaveAndReimport();
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(pngPath);
                if (sprite == null)
                    foreach (var a in AssetDatabase.LoadAllAssetsAtPath(pngPath))
                        if (a is Sprite s) { sprite = s; break; }
                return sprite;
            }
            finally
            {
                if (rt != null) { rt.Release(); Object.DestroyImmediate(rt); }
                if (camGo != null) Object.DestroyImmediate(camGo);
                if (inst != null) Object.DestroyImmediate(inst);
            }
        }
    }
}
