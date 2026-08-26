using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace GridSystem.EditorTools
{
    /// <summary>
    /// ★ 경복궁 근정전 테스트 맵 원클릭 생성 — 기획 시트(08/26, 8칸 상한·칸모듈 통짜안) 기반.
    /// · 파츠 MaterialDef 11종(id 30~40) + 색큐브 폴백 프리팹 → 전역 MaterialCatalog에 등록
    ///   (VARCO 모델이 나오면 각 def의 Prefab만 교체하면 됨)
    /// · 중층 전각 정답 52블록: 1층 칸모듈 14 → 하층 기와 10 → 마루 6 → 2층 칸모듈 10 → 상층 기와 10 → 지붕 2
    ///   (원 기획 98블록 대비 절반 수준 — 실루엣 동일. 블록 수 감이 목적인 테스트 맵)
    /// · 마루·지붕은 아래층 기와 안쪽 테두리에 1칸 걸치게 넓혀서 '허공 배치 거부' 규칙을 통과시킨다
    ///   (걸침 없이 구멍 크기 그대로면 WouldBeSupported=false → 플레이어가 놓을 수 없음)
    /// · 그레이박스 배경: 박석 마당 + 회랑 + 근정문 + 서쪽 시공 비계(계단·발판) + 북악산 원경 + 마커 5종
    /// 몇 번을 다시 실행해도 같은 결과(기존 에셋 덮어쓰기).
    /// </summary>
    public static class GyeongbokgungMapTool
    {
        private const string kDir        = "Assets/Prefabs/Map/3_Gyeongbokgung";
        private const string kPrefabPath = "Assets/Map/Prefabs/MapBg_Gyeongbokgung.prefab";
        private const string kMapDefPath = "Assets/Map/Maps/Map_Gyeongbokgung.asset";
        private const string kAnswerPath = kDir + "/Ans_Gyeongbokgung.asset";
        private const string kThumbPath  = "Assets/Map/Maps/Thumb_Gyeongbokgung.png";
        private const string kMatDir     = "Assets/Map/Materials";
        private const string kMapCatalogPath = "Assets/Resources/MapCatalog.asset";
        // ⚠ GameScene의 GridManager가 물고 있는 '전역 재료 카탈로그' — 여기 없는 재료는 주문이 무시된다.
        private const string kGlobalMaterialCatalogPath = "Assets/Prefabs/Map/1_KwangTongGyo/1_GwangTongGyo_MaterialCatalog.asset";

        private static readonly Vector3Int kGridSize = new Vector3Int(30, 15, 20);
        private const float kTimeLimitSeconds = 600f;   // 10분 — 최종전 후보. 밸런스는 플레이 테스트로

        // ── 파츠 정의(칸모듈 통짜안) : 이름, id, footprint(가로,높이,세로), 공정, 색, 하중부재, 무거움 ──
        // 가로/세로 변형은 별도 def로 둔다. 회전 배치는 모서리기와만 사용(kCornerRots — 4모서리가 바깥을 본다).
        private struct Part
        {
            public string Name; public int Id; public Vector3Int Fp;
            public ProcessType[] Procs; public Color Color; public bool MustFix; public bool Heavy;
        }

        private static readonly ProcessType[] kNone    = { };
        private static readonly ProcessType[] kFix     = { ProcessType.Fixed };
        private static readonly ProcessType[] kFixPaint = { ProcessType.Fixed, ProcessType.Painted };

        private static readonly Part[] kParts =
        {
            // 1층 몸체 — 기둥+창호지벽 일체 칸(間) 모듈. 기둥 포함이라 하중부재.
            new Part{ Name="경복궁_벽모듈",        Id=30, Fp=new Vector3Int(4,3,1), Procs=kFix,      Color=new Color(0.63f,0.36f,0.22f), MustFix=true  },
            new Part{ Name="경복궁_벽모듈_측면",   Id=31, Fp=new Vector3Int(1,3,5), Procs=kFix,      Color=new Color(0.55f,0.31f,0.19f), MustFix=true  },
            new Part{ Name="경복궁_문모듈",        Id=32, Fp=new Vector3Int(4,3,1), Procs=kFix,      Color=new Color(0.78f,0.68f,0.52f), MustFix=true  },
            // 기와 — 모서리(귀마루) + 직선 통짜. 크고 무거움 → 2인 운반.
            new Part{ Name="경복궁_모서리기와",     Id=33, Fp=new Vector3Int(3,3,3), Procs=kFix,      Color=new Color(0.33f,0.38f,0.45f), Heavy=true },
            new Part{ Name="경복궁_직선기와_장",   Id=34, Fp=new Vector3Int(8,3,3), Procs=kFix,      Color=new Color(0.47f,0.53f,0.62f), Heavy=true },
            new Part{ Name="경복궁_직선기와_단",   Id=35, Fp=new Vector3Int(6,3,3), Procs=kFix,      Color=new Color(0.56f,0.61f,0.70f), Heavy=true },
            new Part{ Name="경복궁_직선기와_장세로", Id=36, Fp=new Vector3Int(3,3,8), Procs=kFix,     Color=new Color(0.42f,0.48f,0.57f), Heavy=true },
            new Part{ Name="경복궁_직선기와_단세로", Id=37, Fp=new Vector3Int(3,3,6), Procs=kFix,     Color=new Color(0.51f,0.56f,0.65f), Heavy=true },
            // 2층 몸체 — 망치질+페인트칠(단청) 2공정. 최종전 작업량은 여기서 나온다.
            new Part{ Name="경복궁_2층벽모듈",     Id=38, Fp=new Vector3Int(4,2,1), Procs=kFixPaint, Color=new Color(0.87f,0.55f,0.25f), MustFix=true },
            new Part{ Name="경복궁_2층벽모듈_측면", Id=39, Fp=new Vector3Int(1,2,6), Procs=kFixPaint, Color=new Color(0.80f,0.48f,0.20f), MustFix=true },
            // 지붕 — 좌우 두 덩어리. 상층 기와 안쪽 테두리에 1칸 걸치는 크기(지지 확보).
            new Part{ Name="경복궁_지붕",          Id=40, Fp=new Vector3Int(8,3,8), Procs=kFix,      Color=new Color(0.30f,0.42f,0.72f), Heavy=true },
            // 2층 마루 — 하층 기와 안쪽 테두리에 1칸 걸치는 바닥판. 공정 없음(놓으면 자동 고정), 가벼움.
            new Part{ Name="경복궁_마루",          Id=41, Fp=new Vector3Int(6,1,5), Procs=kNone,     Color=new Color(0.76f,0.60f,0.38f) },
        };

        // ── 근정전 조립(정답): (파츠 id, 앵커 셀=min-corner). 총 52블록, 높이 15 ──
        // 층 구성: 1층 벽 y0-2 → 하층 기와 y3-5 → 마루 y6 → 2층 벽 y7-8 → 상층 기와 y9-11 → 지붕 y12-14
        private static readonly (int id, Vector3Int anchor)[] kPalace =
        {
            // 1층 벽 링 x5..24, z4..15 (문은 앞뒤 중앙)
            (30, new Vector3Int( 5, 0,  4)), (30, new Vector3Int( 9, 0,  4)), (32, new Vector3Int(13, 0,  4)), (30, new Vector3Int(17, 0,  4)), (30, new Vector3Int(21, 0,  4)),
            (30, new Vector3Int( 5, 0, 15)), (30, new Vector3Int( 9, 0, 15)), (32, new Vector3Int(13, 0, 15)), (30, new Vector3Int(17, 0, 15)), (30, new Vector3Int(21, 0, 15)),
            (31, new Vector3Int( 5, 0,  5)), (31, new Vector3Int( 5, 0, 10)),
            (31, new Vector3Int(24, 0,  5)), (31, new Vector3Int(24, 0, 10)),
            // 하층 기와 링 x4..25, z3..16 (벽보다 1~2칸 돌출한 처마)
            (33, new Vector3Int( 4, 3,  3)), (33, new Vector3Int(23, 3,  3)), (33, new Vector3Int( 4, 3, 14)), (33, new Vector3Int(23, 3, 14)),
            (34, new Vector3Int( 7, 3,  3)), (34, new Vector3Int(15, 3,  3)),
            (34, new Vector3Int( 7, 3, 14)), (34, new Vector3Int(15, 3, 14)),
            (36, new Vector3Int( 4, 3,  6)), (36, new Vector3Int(23, 3,  6)),
            // 마루 y6 — x6..23, z5..14 (하층 기와 안쪽 테두리에 1칸 걸침 → 지지 OK, 2층의 바닥)
            (41, new Vector3Int( 6, 6,  5)), (41, new Vector3Int(12, 6,  5)), (41, new Vector3Int(18, 6,  5)),
            (41, new Vector3Int( 6, 6, 10)), (41, new Vector3Int(12, 6, 10)), (41, new Vector3Int(18, 6, 10)),
            // 2층 벽 링 x7..22, z6..13 (마루 위, 안쪽으로 후퇴)
            (38, new Vector3Int( 7, 7,  6)), (38, new Vector3Int(11, 7,  6)), (38, new Vector3Int(15, 7,  6)), (38, new Vector3Int(19, 7,  6)),
            (38, new Vector3Int( 7, 7, 13)), (38, new Vector3Int(11, 7, 13)), (38, new Vector3Int(15, 7, 13)), (38, new Vector3Int(19, 7, 13)),
            (39, new Vector3Int( 7, 7,  7)), (39, new Vector3Int(22, 7,  7)),
            // 상층 기와 링 x5..24, z4..15
            (33, new Vector3Int( 5, 9,  4)), (33, new Vector3Int(22, 9,  4)), (33, new Vector3Int( 5, 9, 13)), (33, new Vector3Int(22, 9, 13)),
            (34, new Vector3Int( 8, 9,  4)), (35, new Vector3Int(16, 9,  4)),
            (34, new Vector3Int( 8, 9, 13)), (35, new Vector3Int(16, 9, 13)),
            (37, new Vector3Int( 5, 9,  7)), (37, new Vector3Int(22, 9,  7)),
            // 지붕 x7..22, z6..13 (좌우 두 덩어리 — 상층 기와 안쪽 테두리에 1칸 걸침)
            (40, new Vector3Int( 7, 12, 6)), (40, new Vector3Int(15, 12, 6)),
        };

        // ── 모서리기와 회전: 각 모서리가 바깥을 보도록 시계방향(위에서 볼 때 +Y 회전)으로 90°씩.
        // 남서(정면 왼쪽) = 기준 0. 모델의 기본 방향이 어긋나 있으면 kCornerRotOffset 하나만 조절해 전체 보정.
        private const int kCornerRotOffset = 0;
        private static readonly Dictionary<Vector3Int, int> kCornerRots = new Dictionary<Vector3Int, int>
        {
            // 하층 기와 (y3)
            { new Vector3Int( 4, 3,  3), 0 },   // 남서
            { new Vector3Int( 4, 3, 14), 1 },   // 북서
            { new Vector3Int(23, 3, 14), 2 },   // 북동
            { new Vector3Int(23, 3,  3), 3 },   // 남동
            // 상층 기와 (y9)
            { new Vector3Int( 5, 9,  4), 0 },   // 남서
            { new Vector3Int( 5, 9, 13), 1 },   // 북서
            { new Vector3Int(22, 9, 13), 2 },   // 북동
            { new Vector3Int(22, 9,  4), 3 },   // 남동
        };

        // ── 기본 제공(preset) 블록 앵커 — 라운드 시작 시 완성 상태로 미리 깔림(채점 제외).
        // 1층 서쪽 측면 + 뒷벽 전부(문 포함) + 동쪽 측면 뒤 1 + 그 위 하층 기와 줄 + 2층 약간.
        // "벽이 서 있는 곳 위엔 기와도 있다" — 미리 지어진 구간이 완성 단면의 본보기가 된다.
        // 마루는 전부 플레이어가 직접 깐다(프리셋 없음).
        private static readonly HashSet<Vector3Int> kPresetAnchors = new HashSet<Vector3Int>
        {
            new Vector3Int( 5, 0,  5), new Vector3Int( 5, 0, 10),   // 1층 서쪽 측면 벽 2
            new Vector3Int( 5, 0, 15), new Vector3Int( 9, 0, 15),   // 1층 뒷벽 왼편 2
            new Vector3Int(13, 0, 15),                               // 1층 뒷문
            new Vector3Int(17, 0, 15), new Vector3Int(21, 0, 15),   // 1층 뒷벽 오른편 2
            new Vector3Int(24, 0, 10),                               // 1층 동쪽 측면 뒤 1
            new Vector3Int( 4, 3,  6),                               // 하층 기와: 서쪽 세로
            // 동쪽 세로 기와(23,3,6)는 제외 — 동쪽 벽이 뒤 1칸만 프리셋이라 기와 절반이 허공에 뜬다
            new Vector3Int( 4, 3, 14), new Vector3Int(23, 3, 14),   // 하층 기와: 뒤 모서리 2
            new Vector3Int( 7, 3, 14), new Vector3Int(15, 3, 14),   // 하층 기와: 뒤 직선 2
            new Vector3Int( 7, 7,  7), new Vector3Int( 7, 7, 13),   // 2층: 서쪽 측면 + 뒤 왼쪽 모듈
        };

        [MenuItem("Tools/Map/★ 경복궁 맵 생성 (테스트)")]
        public static void Generate()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(kPrefabPath) != null &&
                !EditorUtility.DisplayDialog("경복궁 맵 재생성",
                    "배경 프리팹(MapBg_Gyeongbokgung)을 처음부터 다시 만듭니다.\n" +
                    "직접 꾸민 배치·소품·마커 이동이 있으면 사라져요!\n(파츠 def·정답은 안전)",
                    "재생성", "취소"))
                return;

            Directory.CreateDirectory(kDir);

            // ① 파츠 MaterialDef + 색큐브 프리팹
            var defs = new Dictionary<int, MaterialDef>();
            foreach (var p in kParts)
                defs[p.Id] = EnsurePartDef(p);

            // ② 전역 재료 카탈로그 등록(중복 없이 추가) — 없으면 주문이 조용히 무시된다!
            var matCatalog = AssetDatabase.LoadAssetAtPath<MaterialCatalog>(kGlobalMaterialCatalogPath);
            if (matCatalog == null) { Debug.LogError($"[경복궁] 전역 MaterialCatalog이 없음: {kGlobalMaterialCatalogPath}"); return; }
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

            // ③ 정답 — footprint대로 셀을 펼쳐 저장(익스포터와 동일 규칙) + 범위/겹침 검증
            var cells = new List<(Vector3Int cell, int id, int rot)>();
            var presetCells = new List<Vector3Int>();
            var seen = new HashSet<Vector3Int>();
            foreach (var (id, anchor) in kPalace)
            {
                bool preset = kPresetAnchors.Contains(anchor);
                int rot = kCornerRots.TryGetValue(anchor, out int r) ? (r + kCornerRotOffset) & 3 : 0;
                foreach (var c in GridFootprint.EnumerateFootprintCells(anchor, defs[id].Footprint, rot))
                {
                    if (c.x < 0 || c.y < 0 || c.z < 0 || c.x >= kGridSize.x || c.y >= kGridSize.y || c.z >= kGridSize.z)
                    { Debug.LogError($"[경복궁] 셀 범위 밖: {c} (파츠 {id}, 앵커 {anchor})"); return; }
                    if (!seen.Add(c))
                    { Debug.LogError($"[경복궁] 셀 겹침: {c} (파츠 {id}, 앵커 {anchor})"); return; }
                    cells.Add((c, id, rot));
                    if (preset) presetCells.Add(c);
                }
            }
            var answer = LoadOrCreate<MapAnswerData>(kAnswerPath);
            var ao = new SerializedObject(answer);
            ao.FindProperty("m_GridSize").vector3IntValue = kGridSize;
            ao.FindProperty("m_DisplayName").stringValue = "경복궁 근정전";
            ao.FindProperty("m_TimeLimitSeconds").floatValue = kTimeLimitSeconds;
            ao.FindProperty("m_SpawnPresetBlocks").boolValue = true;   // preset을 진짜 블록으로 스폰(GridNetwork)
            var pc = ao.FindProperty("m_PresetCells");
            pc.arraySize = presetCells.Count;
            for (int i = 0; i < presetCells.Count; i++)
                pc.GetArrayElementAtIndex(i).vector3IntValue = presetCells[i];
            var cp = ao.FindProperty("m_Cells");
            cp.arraySize = cells.Count;
            for (int i = 0; i < cells.Count; i++)
            {
                var e = cp.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("cell").vector3IntValue = cells[i].cell;
                e.FindPropertyRelative("materialId").intValue = cells[i].id;
                e.FindPropertyRelative("rotationStep").intValue = cells[i].rot;
            }
            ao.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(answer);

            // ④ 그레이박스 배경 프리팹
            var root = BuildGreybox();
            Directory.CreateDirectory(Path.GetDirectoryName(kPrefabPath));
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, kPrefabPath, out bool ok);
            Object.DestroyImmediate(root);
            if (!ok) { Debug.LogError($"[경복궁] 프리팹 저장 실패: {kPrefabPath}"); return; }

            // ⑤ 맵 카드
            var def2 = LoadOrCreate<MapDef>(kMapDefPath);
            var so = new SerializedObject(def2);
            so.FindProperty("m_DisplayName").stringValue = "경복궁 (테스트)";
            so.FindProperty("m_BackgroundPrefab").objectReferenceValue = prefab;
            so.FindProperty("m_GridSize").vector3IntValue = kGridSize;
            var answers = so.FindProperty("m_Answers");
            answers.arraySize = 1;
            answers.GetArrayElementAtIndex(0).objectReferenceValue = answer;
            var mats = so.FindProperty("m_AvailableMaterials");
            mats.arraySize = kParts.Length;
            for (int i = 0; i < kParts.Length; i++)
                mats.GetArrayElementAtIndex(i).objectReferenceValue = defs[kParts[i].Id];
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(def2);

            // ⑥ 썸네일 + 맵 카탈로그
            var thumb = MapThumbnailUtil.Capture(prefab, kThumbPath);
            if (thumb != null)
            {
                so.Update();
                so.FindProperty("m_Thumbnail").objectReferenceValue = thumb;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            var mapCatalog = AssetDatabase.LoadAssetAtPath<MapCatalog>(kMapCatalogPath);
            if (mapCatalog == null) { Debug.LogError($"[경복궁] MapCatalog이 없음: {kMapCatalogPath}"); return; }
            mapCatalog.EditorAdd(def2);
            EditorUtility.SetDirty(mapCatalog);

            AssetDatabase.SaveAssets();
            Selection.activeObject = def2;
            Debug.Log($"[경복궁] 완료 ✔ 로비에서 '경복궁 (테스트)'를 고르세요.\n" +
                      $"파츠 def 11종(id 30~40) {kDir} — VARCO 모델 나오면 각 def의 Prefab만 교체\n" +
                      $"정답 {kPalace.Length}블록/{cells.Count}칸(높이 14) · 기본 제공 {kPresetAnchors.Count}블록/{presetCells.Count}칸(서쪽 뒤 모서리, 시작 시 완성 상태) · " +
                      $"기와/지붕 IsHeavy(2인 운반) · 2층 벽 2공정(망치질+단청) · 제한시간 {kTimeLimitSeconds / 60f:0}분");
        }

        // ── 파츠 def + 프리팹(피벗 min-corner, 규약 준수) ──
        private static MaterialDef EnsurePartDef(Part p)
        {
            var fitPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{kDir}/{p.Name}_Fit.prefab");

            GameObject prefab = null;
            if (fitPrefab == null)
            {
                string prefabPath = $"{kDir}/{p.Name}.prefab";
                var rootGo = new GameObject(p.Name);
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = "cube";
                cube.transform.SetParent(rootGo.transform, false);
                cube.transform.localPosition = new Vector3(p.Fp.x * 0.5f, p.Fp.y * 0.5f, p.Fp.z * 0.5f);
                cube.transform.localScale = new Vector3(p.Fp.x, p.Fp.y, p.Fp.z) * 0.97f;
                var mat = EnsureMaterial($"Mat_{p.Name}", p.Color);
                if (mat != null) cube.GetComponent<Renderer>().sharedMaterial = mat;
                prefab = PrefabUtility.SaveAsPrefabAsset(rootGo, prefabPath);
                Object.DestroyImmediate(rootGo);
            }

            var def = LoadOrCreate<MaterialDef>($"{kDir}/{p.Name}_Def.asset");
            var so = new SerializedObject(def);
            so.FindProperty("m_Id").intValue = p.Id;
            so.FindProperty("m_Footprint").vector3IntValue = p.Fp;
            so.FindProperty("m_Prefab").objectReferenceValue = fitPrefab != null ? fitPrefab : prefab;
            var procs = so.FindProperty("m_RequiredProcesses");
            procs.arraySize = p.Procs.Length;
            for (int i = 0; i < p.Procs.Length; i++)
                procs.GetArrayElementAtIndex(i).intValue = (int)p.Procs[i];
            so.FindProperty("m_MustBeFixed").boolValue = p.MustFix;
            so.FindProperty("m_Walkable").boolValue = false;
            so.FindProperty("m_IsBreakable").boolValue = false;
            so.FindProperty("m_IsHeavy").boolValue = p.Heavy;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(def);
            return def;
        }

        // ── 그레이박스: 박석 마당 + 회랑 + 근정문 + 서쪽 비계 + 2층 마루 발판 + 북악산 + 마커 5종 ──
        // 좌표 기준: Spot_GridManager=(0,0,0), 그리드 x∈[0,30), z∈[0,20). 마당 지면 y=0.
        private static GameObject BuildGreybox()
        {
            var root = new GameObject("MapBg_Gyeongbokgung");

            var stone   = EnsureMaterial("Mat_GbkCourt",    new Color(0.72f, 0.69f, 0.63f));   // 박석 마당
            var wood    = EnsureMaterial("Mat_GbkWood",     new Color(0.62f, 0.45f, 0.28f));   // 비계·마루
            var redCol  = EnsureMaterial("Mat_GbkRedWall",  new Color(0.62f, 0.20f, 0.16f));   // 회랑 벽(적색)
            var darkTile= EnsureMaterial("Mat_GbkRoofTile", new Color(0.30f, 0.33f, 0.38f));   // 회랑·문 지붕
            var baseSt  = EnsureMaterial("Mat_GbkStoneBase",new Color(0.58f, 0.56f, 0.52f));   // 기단석
            var mtn     = EnsureMaterial("Mat_GbkMountain", new Color(0.38f, 0.50f, 0.38f));   // 북악산

            // 박석 마당 — 그리드 전체 + 회랑 안쪽까지 넉넉하게. 상판 y=0.
            AddBox(root, "Courtyard", new Vector3(15f, -0.5f, 10f), new Vector3(56f, 1f, 46f), stone).isStatic = true;

            // 건물 기단(월대 느낌) — 정답 footprint보다 살짝 넓은 얇은 단(장식, 상판 y=0.12)
            AddBox(root, "StoneBase", new Vector3(15f, 0.06f, 9.5f), new Vector3(24f, 0.12f, 15f), baseSt).isStatic = true;

            // (2층 마루는 이제 플레이어가 직접 까는 블록 — 배경 발판 없음)

            // 서쪽 시공 비계: 계단(0.5씩 12단, 남→북으로 오르며 y6까지) + 상부 발판(하층 기와 옆에 붙는다)
            for (int s = 0; s < 12; s++)
            {
                AddBox(root, $"Scaffold_Stair{s + 1}",
                    new Vector3(2f, 0.25f + 0.5f * s, 16.4f - 0.8f * s),
                    new Vector3(3f, 0.5f, 1.6f), wood).isStatic = true;
            }
            AddBox(root, "Scaffold_Top", new Vector3(2f, 5.8f, 5.2f), new Vector3(3f, 0.4f, 3.6f), wood).isStatic = true;
            // 중간 발판(y3) — 1층 벽 상단·하층 기와 서쪽면 작업용
            AddBox(root, "Scaffold_Mid", new Vector3(2f, 2.8f, 10.8f), new Vector3(3f, 0.4f, 2.4f), wood).isStatic = true;

            // 회랑(행각) — 마당 사방을 두르는 낮은 복도 건물(벽+지붕). 남쪽은 근정문 자리를 비운다.
            void Cloister(string name, Vector3 c, Vector3 size)
            {
                AddBox(root, name + "_Wall", new Vector3(c.x, 1.1f, c.z), new Vector3(size.x, 2.2f, size.z), redCol).isStatic = true;
                AddBox(root, name + "_Roof", new Vector3(c.x, 2.5f, c.z), new Vector3(size.x + 1.2f, 0.6f, size.z + 1.2f), darkTile).isStatic = true;
            }
            Cloister("Cloister_N", new Vector3(15f, 0f, 24f), new Vector3(52f, 0f, 2f));
            Cloister("Cloister_W", new Vector3(-9f, 0f, 10f), new Vector3(2f, 0f, 30f));
            Cloister("Cloister_E", new Vector3(39f, 0f, 10f), new Vector3(2f, 0f, 30f));
            Cloister("Cloister_S1", new Vector3(2f, 0f, -4f), new Vector3(24f, 0f, 2f));
            Cloister("Cloister_S2", new Vector3(28f, 0f, -4f), new Vector3(24f, 0f, 2f));

            // 근정문(남쪽 중앙) — 몸체 + 큰 지붕
            AddBox(root, "Gate_Body", new Vector3(15f, 1.8f, -4f), new Vector3(8f, 3.6f, 3f), redCol).isStatic = true;
            AddBox(root, "Gate_Roof", new Vector3(15f, 4.1f, -4f), new Vector3(10f, 1f, 4.5f), darkTile).isStatic = true;

            // 북악산 원경(북쪽) — 큰 경사 박스 두 장
            var m1 = AddBox(root, "Mountain_1", new Vector3(5f, 4f, 48f), new Vector3(50f, 22f, 16f), mtn);
            m1.transform.rotation = Quaternion.Euler(-38f, 0f, 0f); m1.isStatic = true;
            var m2 = AddBox(root, "Mountain_2", new Vector3(34f, 2f, 52f), new Vector3(44f, 18f, 14f), mtn);
            m2.transform.rotation = Quaternion.Euler(-42f, 8f, 0f); m2.isStatic = true;

            // ── 마커 5종 ──
            AddSpot(root, "Spot_GridManager", new Vector3(0f, 0f, 0f));
            AddSpot(root, "Spot_PlayerSpawnPoint", new Vector3(15f, 0f, -1f));   // 근정문 앞
            AddSpot(root, "Spot_HammerStation", new Vector3(8f, 0f, 1f));        // 마당 남서
            AddSpot(root, "Spot_PaintStation", new Vector3(22f, 0f, 1f));        // 마당 남동
            AddSpot(root, "Spot_DeliveryZone", new Vector3(28f, 0f, 10f));       // 마당 동쪽(자재 하역)
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
                if (sh == null) { Debug.LogWarning("[경복궁] URP Lit 셰이더를 못 찾음"); return null; }
                mat = new Material(sh);
                AssetDatabase.CreateAsset(mat, path);
            }
            if (mat.GetTexture("_BaseMap") == null) mat.SetColor("_BaseColor", color);
            EditorUtility.SetDirty(mat);
            return mat;
        }
    }
}
