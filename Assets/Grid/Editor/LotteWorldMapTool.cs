using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace GridSystem.EditorTools
{
    /// <summary>
    /// ★ 롯데월드 맵 원클릭 생성 — 석촌호수 매직아일랜드 위 '매직캐슬' 건설 + 퍼레이드 기믹.
    /// · 파츠 MaterialDef 8종(id 21~28) + 색큐브 폴백 프리팹 → 전역 MaterialCatalog에 등록
    ///   (VARCO 모델이 나오면 {kDir}/{파츠이름}_Fit.prefab만 두면 자동 교체)
    /// · 정답 91칸(높이 9) 매직캐슬: 기반 + 본체 + 코너타워 4 + 파랑 지붕 + 첨탑 + 게이트 + 깃발
    /// · 배경: 호수(석촌호수) + 섬 + 광장 + 퍼레이드 길(배송·작업대와 건축장 사이를 가로지름)
    ///   + 원경(롯데월드타워·자이로드롭·회전목마·대관람차·돔) — 전부 그레이박스 폴백, VARCO _Fit 우선
    /// · 퍼레이드 기믹(LotteGimmickConfig + Spot_ParadePoint0~3) + 맵 카드 + 카탈로그 등록
    /// 몇 번을 다시 실행해도 같은 결과(기존 에셋 덮어쓰기).
    /// 튜토리얼(8칸)·남산(71칸)보다 복잡한 91칸 — 협동 전용.
    /// </summary>
    public static class LotteWorldMapTool
    {
        private const string kDir         = "Assets/Prefabs/Map/3_LotteWorld";
        private const string kPrefabPath  = "Assets/Map/Prefabs/MapBg_LotteWorld.prefab";
        private const string kMapDefPath  = "Assets/Map/Maps/Map_LotteWorld.asset";
        private const string kAnswerPath  = kDir + "/Ans_LotteWorld.asset";
        private const string kConfigPath  = kDir + "/LotteGimmickConfig_LotteWorld.asset";
        private const string kThumbPath   = "Assets/Map/Maps/Thumb_LotteWorld.png";
        private const string kMatDir      = "Assets/Map/Materials";
        private const string kMapCatalogPath = "Assets/Resources/MapCatalog.asset";
        // ⚠ GameScene의 GridManager가 물고 있는 '전역 재료 카탈로그' — 여기 없는 재료는 주문이 무시된다.
        private const string kGlobalMaterialCatalogPath = "Assets/Prefabs/Map/1_KwangTongGyo/1_GwangTongGyo_MaterialCatalog.asset";

        private static readonly Vector3Int kGridSize = new Vector3Int(13, 12, 13);
        private const float kTimeLimitSeconds = 480f;   // 8분(91칸 — 남산 71칸 7분보다 조금 여유)

        // ── 파츠 정의 : 이름, id(21~ — 광통교 1~8·튜토리얼 10~12·남산 12~20과 충돌 회피), footprint, 공정, 색, 하중부재 ──
        private struct Part
        {
            public string Name; public int Id; public Vector3Int Fp;
            public ProcessType Proc; public Color Color; public bool MustFix;
        }

        private static readonly Part[] kParts =
        {
            // MustFix는 기반·본체만 — 나머지는 고정 강제 없이 쌓는다(남산과 같은 밸런스)
            new Part{ Name="롯데_성기반",     Id=21, Fp=new Vector3Int(5,1,5), Proc=ProcessType.Fixed,   Color=new Color(0.90f,0.87f,0.82f), MustFix=true },
            new Part{ Name="롯데_성본체",     Id=22, Fp=new Vector3Int(3,3,3), Proc=ProcessType.Fixed,   Color=new Color(0.98f,0.95f,0.88f), MustFix=true },
            new Part{ Name="롯데_성상단",     Id=23, Fp=new Vector3Int(3,1,3), Proc=ProcessType.None,    Color=new Color(0.99f,0.98f,0.94f), MustFix=false },
            new Part{ Name="롯데_중앙첨탑",   Id=24, Fp=new Vector3Int(1,3,1), Proc=ProcessType.Painted, Color=new Color(0.25f,0.45f,0.85f), MustFix=false },
            new Part{ Name="롯데_코너타워",   Id=25, Fp=new Vector3Int(1,4,1), Proc=ProcessType.Fixed,   Color=new Color(0.96f,0.92f,0.80f), MustFix=false },
            new Part{ Name="롯데_타워지붕",   Id=26, Fp=new Vector3Int(1,2,1), Proc=ProcessType.Painted, Color=new Color(0.30f,0.55f,0.90f), MustFix=false },
            new Part{ Name="롯데_정문게이트", Id=27, Fp=new Vector3Int(1,2,1), Proc=ProcessType.Painted, Color=new Color(0.85f,0.65f,0.25f), MustFix=false },
            new Part{ Name="롯데_깃발",       Id=28, Fp=new Vector3Int(1,1,1), Proc=ProcessType.Painted, Color=new Color(0.95f,0.35f,0.30f), MustFix=false },
        };

        // ── 매직캐슬 조립(정답): (파츠 id, 앵커 셀). 총 91칸, 높이 9(y0~8) ──
        private static readonly (int id, Vector3Int anchor)[] kCastle =
        {
            (21, new Vector3Int(4, 0, 4)),    // 성 기반 5×5, y0 (25칸)
            (25, new Vector3Int(4, 1, 4)),    // 코너타워 SW, y1-4 (4칸)
            (25, new Vector3Int(4, 1, 8)),    // 코너타워 NW
            (25, new Vector3Int(8, 1, 4)),    // 코너타워 SE
            (25, new Vector3Int(8, 1, 8)),    // 코너타워 NE
            (22, new Vector3Int(5, 1, 5)),    // 성 본체 3×3×3, y1-3 (27칸)
            (27, new Vector3Int(6, 1, 4)),    // 정문 게이트, y1-2 (본체 남쪽 정면)
            (23, new Vector3Int(5, 4, 5)),    // 성 상단 3×1×3, y4 (9칸)
            (26, new Vector3Int(4, 5, 4)),    // 파랑 타워지붕 SW, y5-6
            (26, new Vector3Int(4, 5, 8)),    // 파랑 타워지붕 NW
            (26, new Vector3Int(8, 5, 4)),    // 파랑 타워지붕 SE
            (26, new Vector3Int(8, 5, 8)),    // 파랑 타워지붕 NE
            (24, new Vector3Int(6, 5, 6)),    // 중앙 첨탑, y5-7
            (28, new Vector3Int(6, 8, 6)),    // 깃발, y8 → 총 높이 9
        };

        [MenuItem("Tools/Map/★ 롯데월드 맵 생성 (실전)")]
        public static void Generate()
        {
            Directory.CreateDirectory(kDir);

            // ① 파츠 MaterialDef + 색큐브 프리팹
            var defs = new Dictionary<int, MaterialDef>();
            foreach (var p in kParts)
                defs[p.Id] = EnsurePartDef(p);

            // ② 전역 재료 카탈로그 등록(중복 없이 추가) — 없으면 주문이 조용히 무시된다!
            var matCatalog = AssetDatabase.LoadAssetAtPath<MaterialCatalog>(kGlobalMaterialCatalogPath);
            if (matCatalog == null) { Debug.LogError($"[롯데월드] 전역 MaterialCatalog이 없음: {kGlobalMaterialCatalogPath}"); return; }
            var mc = new SerializedObject(matCatalog);
            var list = mc.FindProperty("m_Materials");
            foreach (var d in defs.Values)
            {
                bool exists = false;
                for (int i = 0; i < list.arraySize; i++)
                    if (list.GetArrayElementAtIndex(i).objectReferenceValue == d) { exists = true; break; }
                if (!exists)
                {
                    list.arraySize++;
                    list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = d;
                }
            }
            mc.ApplyModifiedPropertiesWithoutUndo();
            matCatalog.RebuildLookup();
            EditorUtility.SetDirty(matCatalog);

            // ③ 정답(매직캐슬) — footprint대로 셀을 펼쳐 저장(익스포터와 동일 규칙)
            var answer = LoadOrCreate<MapAnswerData>(kAnswerPath);
            var cells = new List<(Vector3Int cell, int id)>();
            foreach (var (id, anchor) in kCastle)
            {
                var fp = defs[id].Footprint;
                for (int dx = 0; dx < fp.x; dx++)
                for (int dy = 0; dy < fp.y; dy++)
                for (int dz = 0; dz < fp.z; dz++)
                    cells.Add((anchor + new Vector3Int(dx, dy, dz), id));
            }
            var ao = new SerializedObject(answer);
            ao.FindProperty("m_GridSize").vector3IntValue = kGridSize;
            ao.FindProperty("m_DisplayName").stringValue = "롯데월드 매직캐슬";
            ao.FindProperty("m_TimeLimitSeconds").floatValue = kTimeLimitSeconds;
            var cp = ao.FindProperty("m_Cells");
            cp.arraySize = cells.Count;
            for (int i = 0; i < cells.Count; i++)
            {
                var e = cp.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("cell").vector3IntValue = cells[i].cell;
                e.FindPropertyRelative("materialId").intValue = cells[i].id;
                e.FindPropertyRelative("rotationStep").intValue = 0;
            }
            ao.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(answer);

            // ④ 퍼레이드 기믹 설정(기본값 = LotteGimmickConfig 필드 기본치)
            var cfg = LoadOrCreate<LotteGimmickConfig>(kConfigPath);
            EditorUtility.SetDirty(cfg);

            // ⑤ 그레이박스 배경 프리팹(호수·섬·광장·퍼레이드 길·원경)
            var root = BuildGreybox();
            Directory.CreateDirectory(Path.GetDirectoryName(kPrefabPath));
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, kPrefabPath, out bool ok);
            Object.DestroyImmediate(root);
            if (!ok) { Debug.LogError($"[롯데월드] 프리팹 저장 실패: {kPrefabPath}"); return; }

            // ⑥ 맵 카드
            var def2 = LoadOrCreate<MapDef>(kMapDefPath);
            var so = new SerializedObject(def2);
            so.FindProperty("m_DisplayName").stringValue = "롯데월드";
            so.FindProperty("m_BackgroundPrefab").objectReferenceValue = prefab;
            so.FindProperty("m_GridSize").vector3IntValue = kGridSize;
            so.FindProperty("m_LotteGimmicks").objectReferenceValue = cfg;
            var answers = so.FindProperty("m_Answers");
            answers.arraySize = 1;
            answers.GetArrayElementAtIndex(0).objectReferenceValue = answer;
            var mats = so.FindProperty("m_AvailableMaterials");
            mats.arraySize = kParts.Length;
            for (int i = 0; i < kParts.Length; i++)
                mats.GetArrayElementAtIndex(i).objectReferenceValue = defs[kParts[i].Id];
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(def2);

            // ⑦ 썸네일 + 맵 카탈로그
            var thumb = MapThumbnailUtil.Capture(prefab, kThumbPath);
            if (thumb != null)
            {
                so.Update();
                so.FindProperty("m_Thumbnail").objectReferenceValue = thumb;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            var mapCatalog = AssetDatabase.LoadAssetAtPath<MapCatalog>(kMapCatalogPath);
            if (mapCatalog == null) { Debug.LogError($"[롯데월드] MapCatalog이 없음: {kMapCatalogPath}"); return; }
            mapCatalog.EditorAdd(def2);
            EditorUtility.SetDirty(mapCatalog);

            AssetDatabase.SaveAssets();
            Selection.activeObject = def2;
            Debug.Log($"[롯데월드] 완료 ✔ 로비에서 '롯데월드'를 고르세요.\n" +
                      $"파츠 def 8종(id 21~28) {kDir} — VARCO 모델 나오면 {{파츠이름}}_Fit.prefab만 두고 재실행\n" +
                      $"정답 {cells.Count}칸(높이 9 매직캐슬) · 퍼레이드 기믹(치이면 스턴+재료 드롭) · 제한시간 {kTimeLimitSeconds / 60f:0}분");
        }

        // ── 파츠 def + 색큐브 프리팹(피벗 min-corner, 규약 준수) ──
        private static MaterialDef EnsurePartDef(Part p)
        {
            string prefabPath = $"{kDir}/{p.Name}.prefab";
            var rootGo = new GameObject(p.Name);
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "cube";
            cube.transform.SetParent(rootGo.transform, false);
            cube.transform.localPosition = new Vector3(p.Fp.x * 0.5f, p.Fp.y * 0.5f, p.Fp.z * 0.5f);
            // 규약(MaterialPrefabContractTests): 비주얼 크기 = footprint와 정확히 일치(오차 0.05).
            // 0.97 축소는 다층 파츠에서 오차 초과로 테스트가 깨진다 — 칸을 꽉 채운다.
            cube.transform.localScale = new Vector3(p.Fp.x, p.Fp.y, p.Fp.z);
            var mat = EnsureMaterial($"Mat_{p.Name}", p.Color);
            if (mat != null) cube.GetComponent<Renderer>().sharedMaterial = mat;
            var prefab = PrefabUtility.SaveAsPrefabAsset(rootGo, prefabPath);
            Object.DestroyImmediate(rootGo);

            // VARCO 모델(_Fit)이 이미 적용돼 있으면 그걸 유지 — 색큐브는 모델 없을 때의 폴백일 뿐.
            var fitPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{kDir}/{p.Name}_Fit.prefab");

            var def = LoadOrCreate<MaterialDef>($"{kDir}/{p.Name}_Def.asset");
            var so = new SerializedObject(def);
            so.FindProperty("m_Id").intValue = p.Id;
            so.FindProperty("m_Footprint").vector3IntValue = p.Fp;
            so.FindProperty("m_Prefab").objectReferenceValue = fitPrefab != null ? fitPrefab : prefab;
            var procs = so.FindProperty("m_RequiredProcesses");
            if (p.Proc == ProcessType.None) procs.arraySize = 0;
            else
            {
                procs.arraySize = 1;
                procs.GetArrayElementAtIndex(0).intValue = (int)p.Proc;
            }
            so.FindProperty("m_MustBeFixed").boolValue = p.MustFix;
            so.FindProperty("m_Walkable").boolValue = false;
            so.FindProperty("m_IsBreakable").boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(def);
            return def;
        }

        // ── 그레이박스 배경: 석촌호수 + 매직아일랜드(건축장) + 남쪽 광장(배송·작업대) + 그 사이 퍼레이드 길 ──
        // 좌표 기준: Spot_GridManager=(0,0,0), 그리드 x,z∈[0,13) 섬 위. 섬 상판 y=0, 호수 수면 y=-0.6.
        // 핵심 동선: 광장(남, z≈-6~-10)에서 재료를 받아 → 퍼레이드 길(z≈-3)을 건너 → 섬(북)에서 짓는다.
        private static GameObject BuildGreybox()
        {
            var root = new GameObject("MapBg_LotteWorld");

            var water  = EnsureMaterial("Mat_LotteLake",    new Color(0.20f, 0.47f, 0.74f));
            var island = EnsureMaterial("Mat_LotteIsland",  new Color(0.68f, 0.80f, 0.52f));
            var stoneW = EnsureMaterial("Mat_LotteStoneWhite", new Color(0.92f, 0.91f, 0.87f));
            var road   = EnsureMaterial("Mat_LotteParade",  new Color(0.98f, 0.88f, 0.55f));
            var plaza  = EnsureMaterial("Mat_LottePlaza",   new Color(0.86f, 0.83f, 0.80f));
            var steel  = EnsureMaterial("Mat_LotteSteel",   new Color(0.62f, 0.64f, 0.70f));
            var glass  = EnsureMaterial("Mat_LotteGlass",   new Color(0.70f, 0.83f, 0.92f));

            // 석촌호수 — 거대한 수면(비주얼 겸 낙하 방지 바닥)
            AddBox(root, "Lake", new Vector3(6.5f, -0.75f, 2f), new Vector3(160f, 0.3f, 160f), water).isStatic = true;

            // 매직아일랜드(북) — 건축 그리드가 올라가는 섬. 상판 y=0.
            // 성(그리드 x0~13, z0~13) 주변은 비워 두고, 어트랙션은 섬 양끝 모서리로 밀어낸다.
            AddBox(root, "Island", new Vector3(6.5f, -0.5f, 9f), new Vector3(38f, 1f, 22f), island).isStatic = true;
            // 섬 가장자리 흰 석재 테두리(호수와 경계가 또렷하게)
            AddBox(root, "IslandRim_S", new Vector3(6.5f, -0.12f, -1.9f), new Vector3(38f, 0.28f, 0.7f), stoneW).isStatic = true;
            AddBox(root, "IslandRim_W", new Vector3(-12.3f, -0.12f, 9f), new Vector3(0.7f, 0.28f, 22f), stoneW).isStatic = true;
            AddBox(root, "IslandRim_E", new Vector3(25.3f, -0.12f, 9f), new Vector3(0.7f, 0.28f, 22f), stoneW).isStatic = true;
            AddBox(root, "IslandRim_N", new Vector3(6.5f, -0.12f, 19.9f), new Vector3(38f, 0.28f, 0.7f), stoneW).isStatic = true;

            // 광장(남) — 배송존·작업대·스폰. 상판 y=0.
            AddBox(root, "Plaza", new Vector3(6.5f, -0.5f, -8f), new Vector3(26f, 1f, 8f), plaza).isStatic = true;

            // 퍼레이드 길 — 섬과 광장 사이(z=-3)를 동서로 관통. 양쪽 어디서든 건널 수 있는 평지.
            AddBox(root, "ParadeRoad", new Vector3(6.5f, -0.485f, -3.2f), new Vector3(40f, 1.03f, 2.6f), road).isStatic = true;

            // 다리(광장 남쪽 → 호수 건너) — 흰 석재 배경 장식
            AddBox(root, "Bridge", new Vector3(6.5f, -0.35f, -17f), new Vector3(4f, 0.4f, 10f), stoneW).isStatic = true;

            // ── 원경 프롭: VARCO _Fit 우선, 없으면 그레이박스 폴백 ──

            // 롯데월드타워(북동 원경, 555m 랜드마크) — 성과 겹쳐 보이지 않게 호수 건너 멀리
            if (!TryPlaceProp(root, "롯데_롯데월드타워", new Vector3(38f, -0.6f, 48f)))
            {
                AddBox(root, "TowerBase", new Vector3(38f, 7f, 48f), new Vector3(7f, 16f, 7f), glass).isStatic = true;
                AddBox(root, "TowerMid",  new Vector3(38f, 20f, 48f), new Vector3(5f, 10f, 5f), glass).isStatic = true;
                AddBox(root, "TowerTop",  new Vector3(38f, 28.5f, 48f), new Vector3(2.6f, 7f, 2.6f), glass).isStatic = true;
            }

            // ── 어트랙션: 성(그리드)에서 떨어진 섬 양끝 모서리에, 전용 받침 패드와 함께 ──

            // 자이로드롭(섬 북동쪽 끝) — 패드 + 모델(없으면 그레이박스 기둥)
            AddCylinder(root, "GyroPad", new Vector3(21f, -0.05f, 16.5f), new Vector3(6.5f, 0.12f, 6.5f), stoneW);
            if (!TryPlaceProp(root, "롯데_자이로드롭", new Vector3(21f, 0f, 16.5f)))
            {
                AddCylinder(root, "GyroPole", new Vector3(21f, 5.5f, 16.5f), new Vector3(0.8f, 5.5f, 0.8f), steel);
                AddCylinder(root, "GyroRing", new Vector3(21f, 7.5f, 16.5f), new Vector3(2.6f, 0.5f, 2.6f), stoneW);
            }

            // 회전목마(섬 북서쪽 끝) — 패드 + 모델(없으면 그레이박스 원통)
            AddCylinder(root, "CarouselPad", new Vector3(-8f, -0.05f, 15.5f), new Vector3(9f, 0.12f, 9f), stoneW);
            if (!TryPlaceProp(root, "롯데_회전목마", new Vector3(-8f, 0f, 15.5f)))
            {
                AddCylinder(root, "CarouselBody", new Vector3(-8f, 1f, 15.5f), new Vector3(3.4f, 1f, 3.4f), stoneW);
                AddCylinder(root, "CarouselRoof", new Vector3(-8f, 2.6f, 15.5f), new Vector3(4f, 0.6f, 4f), glass);
            }

            // 대관람차(서쪽 호수 건너 원경) — 모델 있을 때만(그레이박스 원판은 정체불명으로 보여서 생략)
            TryPlaceProp(root, "롯데_대관람차", new Vector3(-26f, -0.6f, 24f), 90f);

            // 열기구 풍선(호수 상공 장식) — 모델(롯데_풍선_Fit) 있을 때만 배치. 폴백 없음(색 구체는 오히려 어색).
            (Vector3 p, float rot, float sc)[] balloonSpots =
            {
                (new Vector3(-18f, 5.5f, 6f), 20f, 1.0f),
                (new Vector3(-15f, 7.5f, 26f), 140f, 0.85f),
                (new Vector3(30f, 6.5f, 26f), 250f, 1.1f),
                (new Vector3(33f, 8f, 4f), 70f, 0.9f),
            };
            foreach (var (p, rot, sc) in balloonSpots)
                TryPlaceProp(root, "롯데_풍선", p, rot, sc);

            // ── 마커: 필수 5종 + 퍼레이드 경로 4점 ──
            AddSpot(root, "Spot_GridManager", new Vector3(0f, 0f, 0f));                // 짓는 곳(섬)
            AddSpot(root, "Spot_PlayerSpawnPoint", new Vector3(6.5f, 0.1f, -6f));      // 광장 북쪽(길 바로 앞)
            AddSpot(root, "Spot_DeliveryZone", new Vector3(6.5f, 0.1f, -9f));          // 광장 중앙 남쪽 — 재료는 여기로 떨어진다
            AddSpot(root, "Spot_HammerStation", new Vector3(11.5f, 0.3f, -7.5f));      // 광장 동쪽(0.3 — 바닥에 파묻힘 방지)
            AddSpot(root, "Spot_PaintStation", new Vector3(1.5f, 0.3f, -7.5f));        // 광장 서쪽(〃)
            // 퍼레이드 경로(서→동 일직선, 길 중앙 z=-3.2) — ParadeNetwork가 0번부터 순서대로 잇는다
            AddSpot(root, "Spot_ParadePoint0", new Vector3(-13f, 0.1f, -3.2f));
            AddSpot(root, "Spot_ParadePoint1", new Vector3(0f, 0.1f, -3.2f));
            AddSpot(root, "Spot_ParadePoint2", new Vector3(13f, 0.1f, -3.2f));
            AddSpot(root, "Spot_ParadePoint3", new Vector3(26f, 0.1f, -3.2f));
            return root;
        }

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<T>();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                AssetDatabase.CreateAsset(asset, path);
            }
            return asset;
        }

        // VARCO 배경 소품(_Fit 프리팹, 바닥 피벗) 배치 시도 — 모델을 적용해둔 경우에만 true.
        private static bool TryPlaceProp(GameObject root, string name, Vector3 groundPos, float yRot = 0f, float scale = 1f)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{kDir}/{name}_Fit.prefab");
            if (prefab == null) return false;
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
            inst.transform.localPosition = groundPos;
            inst.transform.localRotation = Quaternion.Euler(0f, yRot, 0f);
            if (scale != 1f) inst.transform.localScale *= scale;
            // 서 있을 수 있게 콜라이더 보장(모델엔 보통 없음) — 바운즈 기준 박스 하나
            if (inst.GetComponentInChildren<Collider>() == null)
            {
                var rends = inst.GetComponentsInChildren<Renderer>();
                if (rends.Length > 0)
                {
                    var b = rends[0].bounds;
                    foreach (var r in rends) b.Encapsulate(r.bounds);
                    var bc = inst.AddComponent<BoxCollider>();
                    bc.center = inst.transform.InverseTransformPoint(b.center);
                    bc.size = Vector3.Scale(b.size, new Vector3(
                        1f / inst.transform.lossyScale.x, 1f / inst.transform.lossyScale.y, 1f / inst.transform.lossyScale.z));
                }
            }
            return true;
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

        private static GameObject AddCylinder(GameObject root, string name, Vector3 pos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            if (mat != null) go.GetComponent<Renderer>().sharedMaterial = mat;
            go.isStatic = true;
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
                if (sh == null) { Debug.LogWarning("[롯데월드] URP Lit 셰이더를 못 찾음"); return null; }
                mat = new Material(sh);
                AssetDatabase.CreateAsset(mat, path);
            }
            // 텍스처가 입혀진 머티리얼(모델 적용 툴)은 색을 덮지 않는다 — 재실행해도 텍스처 유지
            if (mat.GetTexture("_BaseMap") == null) mat.SetColor("_BaseColor", color);
            EditorUtility.SetDirty(mat);
            return mat;
        }
    }
}
