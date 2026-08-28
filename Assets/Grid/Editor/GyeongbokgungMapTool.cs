using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace GridSystem.EditorTools
{
    /// <summary>
    /// ★ 경복궁 근정전 테스트 맵 원클릭 생성 — 기획 시트(08/26, 8칸 상한·칸모듈 통짜안) 기반.
    /// · 파츠 MaterialDef 9종(id 30~32, 34~35, 38~41) + 색큐브 폴백 프리팹 → 전역 MaterialCatalog에 등록
    ///   (VARCO 모델이 나오면 각 def의 Prefab만 교체하면 됨. 모서리기와 id33·세로기와 id36/37은 08/28 폐기)
    /// · 중층 전각 정답 41블록: 1층 칸모듈 14 → 하층 기와 6 → 마루 4(테두리보다 1칸 낮게) → 2층 칸모듈 10 → 상층 기와 6 → 지붕 1
    ///   (기와 링의 네 모서리 3×3은 의도적으로 비어 있다 — 모서리기와 폐기 후 뚫린 코너가 디자인)
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

        private static readonly Vector3Int kGridSize = new Vector3Int(30, 13, 20);
        private const float kTimeLimitSeconds = 600f;   // 10분 — 최종전 후보. 밸런스는 플레이 테스트로

        // ── 파츠 정의(칸모듈 통짜안) : 이름, id, footprint(가로,높이,세로), 공정, 색, 하중부재, 무거움 ──
        // [08/28] 재료 종류 최소화: 기와는 장·단 2종뿐, 세로 자리는 회전 배치(kRotatedAnchors)로 해결.
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
            // 기와 — 직선 2종뿐(모서리기와·세로 변형 전부 08/28 폐기 — 재료 종류 최소화).
            // 세로 자리는 같은 기와를 R로 90° 돌려 배치한다(채점은 회전 무시 — 점유 칸+재료만 비교).
            new Part{ Name="경복궁_직선기와_장",   Id=34, Fp=new Vector3Int(8,3,3), Procs=kFix,      Color=new Color(0.47f,0.53f,0.62f), Heavy=true },
            new Part{ Name="경복궁_직선기와_단",   Id=35, Fp=new Vector3Int(6,3,3), Procs=kFix,      Color=new Color(0.56f,0.61f,0.70f), Heavy=true },
            // 2층 몸체 — 망치질+페인트칠(단청) 2공정. 최종전 작업량은 여기서 나온다.
            new Part{ Name="경복궁_2층벽모듈",     Id=38, Fp=new Vector3Int(4,2,1), Procs=kFixPaint, Color=new Color(0.87f,0.55f,0.25f), MustFix=true },
            new Part{ Name="경복궁_2층벽모듈_측면", Id=39, Fp=new Vector3Int(1,2,6), Procs=kFixPaint, Color=new Color(0.80f,0.48f,0.20f), MustFix=true },
            // 지붕 — 삼각 왕관 한 덩어리. 8칸 상한의 유일한 예외(마지막에 다 같이 올리는 클라이맥스 블록).
            // 반쪽 2개 잇기는 폐기(08/27) — 슬라이스 프리팹의 머티리얼 참조가 glb 교체마다 깨져서 투명화 사고.
            new Part{ Name="경복궁_지붕",          Id=40, Fp=new Vector3Int(16,3,8), Procs=kFix,     Color=new Color(0.30f,0.42f,0.72f), Heavy=true },
            // 2층 마루 — 하층 기와와 '같은 층'에서 안쪽 구멍(16×8)을 메우는 바닥판. 윗면이 기와 윗면과 평평.
            // 아래는 배경 프리팹의 1층 천장(Ceiling1F)이 받친다. 공정 없음(놓으면 자동 고정), 가벼움.
            new Part{ Name="경복궁_마루",          Id=41, Fp=new Vector3Int(8,1,4), Procs=kNone,     Color=new Color(0.76f,0.60f,0.38f) },
        };

        // ── 근정전 조립(정답): (파츠 id, 앵커 셀=min-corner). 총 49블록, 높이 13 ──
        // 층 구성: 1층 벽 y0-2 → 하층 기와 y3-5 + 마루 y4(기와 테두리보다 1칸 낮게 움푹) →
        //          2층 벽 y5-6(아랫단이 기와 뒤에 가려짐 — 실제 근정전 프로파일) → 상층 기와 y7-9 → 지붕 y10-12
        private static readonly (int id, Vector3Int anchor)[] kPalace =
        {
            // 1층 벽 링 x5..24, z4..15 (문은 앞뒤 중앙)
            (30, new Vector3Int( 5, 0,  4)), (30, new Vector3Int( 9, 0,  4)), (32, new Vector3Int(13, 0,  4)), (30, new Vector3Int(17, 0,  4)), (30, new Vector3Int(21, 0,  4)),
            (30, new Vector3Int( 5, 0, 15)), (30, new Vector3Int( 9, 0, 15)), (32, new Vector3Int(13, 0, 15)), (30, new Vector3Int(17, 0, 15)), (30, new Vector3Int(21, 0, 15)),
            (31, new Vector3Int( 5, 0,  5)), (31, new Vector3Int( 5, 0, 10)),
            (31, new Vector3Int(24, 0,  5)), (31, new Vector3Int(24, 0, 10)),
            // 하층 기와 링 x4..25, z3..16 — 직선 기와만, 네 모서리(3×3)는 의도적으로 비움(08/28: 코너 모델 폐기, 뚫린 게 낫다)
            // 세로 자리 2곳은 장기와를 rot1(90°)로 — kRotatedAnchors 참조
            (34, new Vector3Int( 7, 3,  3)), (34, new Vector3Int(15, 3,  3)),
            (34, new Vector3Int( 7, 3, 14)), (34, new Vector3Int(15, 3, 14)),
            (34, new Vector3Int( 4, 3,  6)), (34, new Vector3Int(23, 3,  6)),
            // 마루 y4 — 하층 기와 안쪽 구멍(x7..22, z6..13)을 테두리보다 1칸 낮게 메움(움푹 들어간 2층 바닥)
            (41, new Vector3Int( 7, 4,  6)), (41, new Vector3Int(15, 4,  6)),
            (41, new Vector3Int( 7, 4, 10)), (41, new Vector3Int(15, 4, 10)),
            // 2층 벽 링 x7..22, z6..13 (마루 위 — 아랫단 1칸이 기와 테두리 뒤에 가려진다)
            (38, new Vector3Int( 7, 5,  6)), (38, new Vector3Int(11, 5,  6)), (38, new Vector3Int(15, 5,  6)), (38, new Vector3Int(19, 5,  6)),
            (38, new Vector3Int( 7, 5, 13)), (38, new Vector3Int(11, 5, 13)), (38, new Vector3Int(15, 5, 13)), (38, new Vector3Int(19, 5, 13)),
            (39, new Vector3Int( 7, 5,  7)), (39, new Vector3Int(22, 5,  7)),
            // 상층 기와 링 x5..24, z4..15 — 직선 기와만, 네 모서리(3×3)는 의도적으로 비움. 세로 자리는 단기와 rot1
            (34, new Vector3Int( 8, 7,  4)), (35, new Vector3Int(16, 7,  4)),
            (34, new Vector3Int( 8, 7, 13)), (35, new Vector3Int(16, 7, 13)),
            (35, new Vector3Int( 5, 7,  7)), (35, new Vector3Int(22, 7,  7)),
            // 지붕 x7..22, z6..13 (한 덩어리 — 상층 기와 안쪽 테두리에 1칸 걸침)
            (40, new Vector3Int( 7, 10, 6)),
        };

        // 세로 자리(측면 처마) — 가로 기와를 90° 돌려 배치하는 앵커들. 세로 전용 def(구 id36·37)는 폐기.
        // 채점은 회전을 안 보므로(점유 칸+재료 비교) 플레이어는 R로 돌려 모양만 맞추면 된다.
        private static readonly HashSet<Vector3Int> kRotatedAnchors = new HashSet<Vector3Int>
        {
            new Vector3Int( 4, 3,  6), new Vector3Int(23, 3,  6),   // 하층 세로(장기와 rot1)
            new Vector3Int( 5, 7,  7), new Vector3Int(22, 7,  7),   // 상층 세로(단기와 rot1)
        };

        // ── 사방신 석상 재료 4종(id 50~53) — 기믹 전용. 주문 목록(MapDef.AvailableMaterials)에는 안 넣는다.
        // IsHeavy(2인 운반), 공정 없음. GuardianNetwork가 진행도 문턱마다 ServerDeliver로 낙하시킨다.
        private static readonly Part[] kStatues =
        {
            new Part{ Name="경복궁_석상_청룡", Id=50, Fp=new Vector3Int(2,2,2), Procs=kNone, Color=new Color(0.30f,0.55f,1.00f), Heavy=true },
            new Part{ Name="경복궁_석상_백호", Id=51, Fp=new Vector3Int(2,2,2), Procs=kNone, Color=new Color(0.92f,0.92f,0.95f), Heavy=true },
            new Part{ Name="경복궁_석상_주작", Id=52, Fp=new Vector3Int(2,2,2), Procs=kNone, Color=new Color(1.00f,0.35f,0.30f), Heavy=true },
            new Part{ Name="경복궁_석상_현무", Id=53, Fp=new Vector3Int(2,2,2), Procs=kNone, Color=new Color(0.25f,0.22f,0.35f), Heavy=true },
        };

        // ── 기본 제공(preset) 블록 앵커 — 라운드 시작 시 완성 상태로 미리 깔림(채점 제외).
        // [08/28] 초반이 지루하다는 피드백 → 1층을 거의 다 지어놓고 시작한다:
        // 1층 벽은 앞면 2칸만 비우고 전부 + 하층 기와는 앞 직선 2개만 비우고 전부 + 마루 전부 + 2층 본보기 2.
        // 플레이어 몫은 앞면 마감(벽 2·기와 2)과 2층 전체 — 대신 화마가 조금 더 자주 온다(GimmickConfig).
        private static readonly HashSet<Vector3Int> kPresetAnchors = new HashSet<Vector3Int>
        {
            // 1층 벽 — 앞면 (9,0,4)·(17,0,4) 2칸만 플레이어 몫
            new Vector3Int( 5, 0,  4), new Vector3Int(13, 0,  4), new Vector3Int(21, 0,  4),   // 앞벽 좌·문·우
            new Vector3Int( 5, 0, 15), new Vector3Int( 9, 0, 15), new Vector3Int(13, 0, 15),
            new Vector3Int(17, 0, 15), new Vector3Int(21, 0, 15),                               // 뒷벽 전부(문 포함)
            new Vector3Int( 5, 0,  5), new Vector3Int( 5, 0, 10),                               // 서쪽 측면
            new Vector3Int(24, 0,  5), new Vector3Int(24, 0, 10),                               // 동쪽 측면
            // 하층 기와 — 앞줄 2개만 플레이어 몫(뒷줄·세로는 프리셋)
            new Vector3Int( 7, 3, 14), new Vector3Int(15, 3, 14),                               // 뒷줄 2
            new Vector3Int( 4, 3,  6), new Vector3Int(23, 3,  6),                               // 세로 2
            // 마루 — 전부 깔린 채로 시작(까치발도 처음부터 나와 있다)
            new Vector3Int( 7, 4,  6), new Vector3Int(15, 4,  6),
            new Vector3Int( 7, 4, 10), new Vector3Int(15, 4, 10),
            // 2층 본보기 — 서쪽 측면 + 뒤 왼쪽 모듈
            new Vector3Int( 7, 5,  7), new Vector3Int( 7, 5, 13),
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

            // ① 파츠 MaterialDef + 색큐브 프리팹 (+ 사방신 석상 4종 — 주문 목록엔 제외, 카탈로그엔 등록)
            var defs = new Dictionary<int, MaterialDef>();
            foreach (var p in kParts)
                defs[p.Id] = EnsurePartDef(p);
            var statueDefs = new Dictionary<int, MaterialDef>();
            foreach (var p in kStatues)
                statueDefs[p.Id] = EnsurePartDef(p);

            // 화재/진화 이펙트 사본 — CFXR 무료팩 프리팹을 Resources/Fx로 복사(기존 GroundHit 관행. 멱등)
            EnsureFxCopy("Assets/ThirdParty/JMO Assets/Cartoon FX Remaster/CFXR Prefabs/Fire/CFXR Fire.prefab", "Assets/Resources/Fx/Fire.prefab");
            EnsureFxCopy("Assets/ThirdParty/JMO Assets/Cartoon FX Remaster/CFXR Prefabs/Liquids/CFXR Water Splash (Smaller).prefab", "Assets/Resources/Fx/WaterSplash.prefab");

            // ② 전역 재료 카탈로그 등록(중복 없이 추가) — 없으면 주문이 조용히 무시된다!
            var matCatalog = AssetDatabase.LoadAssetAtPath<MaterialCatalog>(kGlobalMaterialCatalogPath);
            if (matCatalog == null) { Debug.LogError($"[경복궁] 전역 MaterialCatalog이 없음: {kGlobalMaterialCatalogPath}"); return; }
            var mc = new SerializedObject(matCatalog);
            var list = mc.FindProperty("m_Materials");
            foreach (var d in statueDefs.Values)
            {
                bool exists0 = false;
                for (int i = 0; i < list.arraySize; i++)
                    if (list.GetArrayElementAtIndex(i).objectReferenceValue == d) { exists0 = true; break; }
                if (!exists0)
                {
                    list.arraySize++;
                    list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = d;
                }
            }
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
                int rot = kRotatedAnchors.Contains(anchor) ? 1 : 0;   // 세로 처마 자리만 90°
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

            // ④.5 기믹 설정(화마·사방신) — 기본값 = 기획서 확정치
            var gimmick = LoadOrCreate<GyeongbokgungGimmickConfig>(kDir + "/GyeongbokgungGimmickConfig_Gbk.asset");
            EditorUtility.SetDirty(gimmick);

            // ⑤ 맵 카드
            var def2 = LoadOrCreate<MapDef>(kMapDefPath);
            var so = new SerializedObject(def2);
            so.FindProperty("m_DisplayName").stringValue = "경복궁 (테스트)";
            so.FindProperty("m_BackgroundPrefab").objectReferenceValue = prefab;
            so.FindProperty("m_GridSize").vector3IntValue = kGridSize;
            so.FindProperty("m_GyeongbokgungGimmicks").objectReferenceValue = gimmick;
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
                      $"파츠 def 9종(기와는 장·단 2종 + 회전 배치) {kDir} — VARCO 모델 나오면 각 def의 Prefab만 교체\n" +
                      $"정답 {kPalace.Length}블록/{cells.Count}칸(높이 14) · 기본 제공 {kPresetAnchors.Count}블록/{presetCells.Count}칸(1층 대부분+마루, 시작 시 완성 상태) · " +
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

            var stone   = EnsureMaterial("Mat_GbkCourt",    new Color(0.74f, 0.73f, 0.70f));   // 박석 돌 광장(실물 화강암 톤)
            var wood    = EnsureMaterial("Mat_GbkWood",     new Color(0.62f, 0.45f, 0.28f));   // 비계·마루
            var redCol  = EnsureMaterial("Mat_GbkRedWall",  new Color(0.62f, 0.20f, 0.16f));   // 회랑 벽(적색)
            var darkTile= EnsureMaterial("Mat_GbkRoofTile", new Color(0.30f, 0.33f, 0.38f));   // 회랑·문 지붕
            var baseSt  = EnsureMaterial("Mat_GbkStoneBase",new Color(0.58f, 0.56f, 0.52f));   // 기단석
            var mtn     = EnsureMaterial("Mat_GbkMountain", new Color(0.38f, 0.50f, 0.38f));   // 북악산

            // 박석 마당 — 실제 근정전처럼 '개큰' 돌 광장(08/27 피드백). 회랑은 저 바깥에서 두른다. 상판 y=0.
            // [08/28] 회랑 링을 6칸 더 밀면서 마당도 같이 확장(회랑 밑까지 덮게).
            AddBox(root, "Courtyard", new Vector3(15f, -0.5f, 10f), new Vector3(94f, 1f, 74f), stone).isStatic = true;

            // 건물 기단(월대 느낌) — 정답 footprint보다 살짝 넓은 얇은 단(장식, 상판 y=0.12)
            AddBox(root, "StoneBase", new Vector3(15f, 0.06f, 9.5f), new Vector3(24f, 0.12f, 15f), baseSt).isStatic = true;

            // 마루 받침 까치발 — 긴 들보 대신 앞뒤 벽 안쪽 면에 붙는 짧은 받침목 8개(08/26 피드백: 통 들보 보기 싫음).
            // [08/28] 마루가 전부 프리셋으로 깔린 채 시작하므로 까치발도 처음부터 활성(공중부양 방지).
            // GyeongbokgungCorbels 컴포넌트는 유지 — 이미 활성이면 첫 Update에서 스스로 꺼진다(프리셋을 되돌릴 때 대비).
            // 앞 스터브는 아래칸 z6, 뒤 스터브는 z13을 덮어 마루 4장 전부의 지지(ExternalSupportBelow)를 보장한다.
            var beamMat = EnsureMaterial("Mat_GbkCeiling", new Color(0.33f, 0.26f, 0.20f));
            var stubRoot = new GameObject("MaruCorbels");
            stubRoot.transform.SetParent(root.transform, false);
            foreach (float bx in new[] { 8.5f, 12.5f, 16.5f, 20.5f })
            {
                AddBox(stubRoot, $"Corbel_F_x{bx}", new Vector3(bx, 3.7f, 5.75f), new Vector3(0.6f, 0.5f, 1.5f), beamMat).isStatic = true;
                AddBox(stubRoot, $"Corbel_B_x{bx}", new Vector3(bx, 3.7f, 14.25f), new Vector3(0.6f, 0.5f, 1.5f), beamMat).isStatic = true;
            }
            stubRoot.SetActive(true);
            root.AddComponent<GyeongbokgungCorbels>().StubRoot = stubRoot;

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

            // 회랑(행각) — 마당 사방을 두르는 낮은 복도 건물. 남쪽은 근정문 자리를 비운다.
            // VARCO 회랑 세그먼트(경복궁_회랑_Fit)가 있으면 그레이박스 렌더러를 끄고 8칸짜리 세그먼트를 줄지어 세운다
            // (돌울타리 SegRow와 같은 방식 — 박스는 충돌 담당으로 유지). 없으면 기존 그레이박스 그대로.
            var cloisterSeg = AssetDatabase.LoadAssetAtPath<GameObject>($"{kDir}/경복궁_회랑_Fit.prefab");
            void Cloister(string name, Vector3 c, Vector3 size)
            {
                var wall = AddBox(root, name + "_Wall", new Vector3(c.x, 1.1f, c.z), new Vector3(size.x, 2.2f, size.z), redCol);
                var roof = AddBox(root, name + "_Roof", new Vector3(c.x, 2.5f, c.z), new Vector3(size.x + 1.2f, 0.6f, size.z + 1.2f), darkTile);
                wall.isStatic = roof.isStatic = true;
                if (cloisterSeg == null) return;
                wall.GetComponent<Renderer>().enabled = false;
                roof.GetComponent<Renderer>().enabled = false;
                bool alongX = size.x >= size.z;
                float len = alongX ? size.x : size.z;
                int n = Mathf.Max(1, Mathf.RoundToInt(len / 8f));
                float step = len / n;
                for (int i = 0; i < n; i++)
                {
                    var s = (GameObject)PrefabUtility.InstantiatePrefab(cloisterSeg, root.transform);
                    s.name = name + "_Seg";
                    float a = (alongX ? c.x : c.z) - len * 0.5f + step * (i + 0.5f);
                    s.transform.localPosition = alongX ? new Vector3(a, 0f, c.z) : new Vector3(c.x, 0f, a);
                    s.transform.localRotation = Quaternion.Euler(0f, alongX ? 0f : 90f, 0f);
                    s.transform.localScale = new Vector3(step / 8f, 1f, 1f);   // 줄 길이에 딱 맞게 미세 스케일
                }
            }
            // [08/28] 회랑 링을 사방 6칸 더 바깥으로 — 광장이 한층 넓어진다(울타리 확장과 세트)
            Cloister("Cloister_N", new Vector3(15f, 0f, 42f), new Vector3(86f, 0f, 2f));
            Cloister("Cloister_W", new Vector3(-27f, 0f, 11f), new Vector3(2f, 0f, 64f));
            Cloister("Cloister_E", new Vector3(57f, 0f, 11f), new Vector3(2f, 0f, 64f));
            Cloister("Cloister_S1", new Vector3(-8f, 0f, -20f), new Vector3(38f, 0f, 2f));
            Cloister("Cloister_S2", new Vector3(38f, 0f, -20f), new Vector3(38f, 0f, 2f));

            // 근정문(남쪽 중앙) — 몸체 + 큰 지붕(회랑 링과 같은 줄). VARCO 근정문 모델(_Fit)이 있으면
            // 그레이박스 렌더러를 끄고 모델을 세운다(박스는 충돌 담당 유지 — 회랑·울타리와 같은 방식).
            var gateBody = AddBox(root, "Gate_Body", new Vector3(15f, 1.8f, -20f), new Vector3(8f, 3.6f, 3f), redCol);
            var gateRoof = AddBox(root, "Gate_Roof", new Vector3(15f, 4.1f, -20f), new Vector3(10f, 1f, 4.5f), darkTile);
            gateBody.isStatic = gateRoof.isStatic = true;
            if (PlaceProp(root, "경복궁_근정문", "Gate", new Vector3(15f, 0f, -20f)) != null)
            {
                gateBody.GetComponent<Renderer>().enabled = false;
                gateRoof.GetComponent<Renderer>().enabled = false;
            }

            // ── 돌 울타리(월대 난간 느낌) — 광장을 두르는 낮은 화강암 담. 남쪽 중앙은 어도(정문 통로)로 비움 ──
            // [08/28] 반경 확장: 그리드에 딱 붙어 답답하다 → 사방 6칸씩 밀어 건축 지역(체감 활동 공간)을 넓힌다.
            var fenceMat = EnsureMaterial("Mat_GbkFence", new Color(0.66f, 0.65f, 0.62f));
            const float fh = 0.9f, ft = 0.5f;          // 높이·두께
            const float fxMin = -8f, fxMax = 37f, fzMin = -8f, fzMax = 27f;
            // 벽은 충돌 담당. VARCO 돌울타리 모델(_Fit)이 있으면 박스 렌더러를 끄고 세그먼트를 줄지어 세운다.
            var fenceSeg = AssetDatabase.LoadAssetAtPath<GameObject>($"{kDir}/경복궁_돌울타리_Fit.prefab");
            GameObject FenceWall(string name, Vector3 c, Vector3 size)
            {
                var b = AddBox(root, name, c, size, fenceMat);
                b.isStatic = true;
                if (fenceSeg != null) b.GetComponent<Renderer>().enabled = false;
                return b;
            }
            // 남쪽: 중앙 x11.5~18.5 비움(어도)
            FenceWall("Fence_S_L", new Vector3((fxMin + 11.5f) * 0.5f, fh * 0.5f, fzMin), new Vector3(11.5f - fxMin, fh, ft));
            FenceWall("Fence_S_R", new Vector3((18.5f + fxMax) * 0.5f, fh * 0.5f, fzMin), new Vector3(fxMax - 18.5f, fh, ft));
            // 북쪽: 중앙 x13~17 비움(북문)
            FenceWall("Fence_N_L", new Vector3((fxMin + 13f) * 0.5f, fh * 0.5f, fzMax), new Vector3(13f - fxMin, fh, ft));
            FenceWall("Fence_N_R", new Vector3((17f + fxMax) * 0.5f, fh * 0.5f, fzMax), new Vector3(fxMax - 17f, fh, ft));
            // 동·서쪽: 통짜
            FenceWall("Fence_E", new Vector3(fxMax, fh * 0.5f, (fzMin + fzMax) * 0.5f), new Vector3(ft, fh, fzMax - fzMin));
            FenceWall("Fence_W", new Vector3(fxMin, fh * 0.5f, (fzMin + fzMax) * 0.5f), new Vector3(ft, fh, fzMax - fzMin));
            if (fenceSeg != null)
            {
                void SegRow(float a0, float a1, float fixedCoord, bool alongX)
                {
                    int n = Mathf.Max(1, Mathf.RoundToInt((a1 - a0) / 2.1f));
                    float step = (a1 - a0) / n;
                    for (int i = 0; i < n; i++)
                    {
                        var s = (GameObject)PrefabUtility.InstantiatePrefab(fenceSeg, root.transform);
                        s.name = "FenceSeg";
                        float a = a0 + step * (i + 0.5f);
                        s.transform.localPosition = alongX ? new Vector3(a, 0f, fixedCoord) : new Vector3(fixedCoord, 0f, a);
                        s.transform.localRotation = Quaternion.Euler(0f, alongX ? 0f : 90f, 0f);
                        s.transform.localScale = new Vector3(step / 2.1f, 1f, 1f) ;   // 줄 길이에 딱 맞게 미세 스케일
                    }
                }
                SegRow(fxMin, 11.5f, fzMin, true); SegRow(18.5f, fxMax, fzMin, true);
                SegRow(fxMin, 13f, fzMax, true); SegRow(17f, fxMax, fzMax, true);
                SegRow(fzMin, fzMax, fxMin, false); SegRow(fzMin, fzMax, fxMax, false);
            }
            // 울타리 기둥(모서리 4 + 게이트 양옆) — 난간 느낌 보강
            foreach (var (px, pz) in new[] { (fxMin, fzMin), (fxMax, fzMin), (fxMin, fzMax), (fxMax, fzMax), (11.5f, fzMin), (18.5f, fzMin) })
                AddBox(root, $"FencePost_{px}_{pz}", new Vector3(px, 0.65f, pz), new Vector3(0.8f, 1.3f, 0.8f), fenceMat).isStatic = true;

            // ── 사방신 석상 받침대 4종 — 동청룡(靑)·서백호(白)·남주작(赤)·북현무(黑). 이름으로 기믹이 찾는다 ──
            // VARCO 받침대 모델(_Fit)이 있으면 그걸 쓰고, 색 발광 판(Top)은 방위 신호로 항상 유지한다.
            void Pedestal(string name, int index, Vector3 pos, Color tint)
            {
                var baseMat = EnsureMaterial($"Mat_GbkPedestal_{name}", tint);
                var g = new GameObject($"Pedestal_{name}");
                g.transform.SetParent(root.transform, false);
                g.transform.localPosition = pos;
                g.AddComponent<GuardianPedestal>().Index = index;   // 클릭 배치 인식용(PlayerCarry 레이캐스트)
                var prop = PlaceProp(g, "경복궁_받침대", "Model", Vector3.zero);
                if (prop == null)
                    AddBox(g, "Base", new Vector3(0f, 0.25f, 0f), new Vector3(2.4f, 0.5f, 2.4f), baseMat).isStatic = true;
                AddBox(g, "Top", new Vector3(0f, prop != null ? 0.95f : 0.7f, 0f), new Vector3(1.7f, 0.25f, 1.7f), baseMat).isStatic = true;
            }
            Pedestal("East",  0, new Vector3(28.5f, 0f,  9.5f), new Color(0.30f, 0.45f, 0.75f));   // 청룡
            Pedestal("West",  1, new Vector3(-0.5f, 0f,  3.0f), new Color(0.88f, 0.88f, 0.90f));   // 백호
            Pedestal("South", 2, new Vector3(21f,   0f, -0.5f), new Color(0.72f, 0.28f, 0.25f));   // 주작
            Pedestal("North", 3, new Vector3(15f,   0f, 19.5f), new Color(0.18f, 0.18f, 0.22f));   // 현무

            // ── 드므(방화수 항아리) 4개 — 건물 네 귀퉁이. 양동이 물 리필 지점(기믹이 이름으로 찾는다) ──
            var bronze = EnsureMaterial("Mat_GbkBronze", new Color(0.42f, 0.33f, 0.20f));
            foreach (var (dx, dz, i) in new[] { (3f, 1.8f, 1), (26f, 1.8f, 2), (3f, 17.2f, 3), (26f, 17.2f, 4) })
                if (PlaceProp(root, "경복궁_드므", $"Deumeu_{i}", new Vector3(dx, 0f, dz)) == null)
                    AddCylinder(root, $"Deumeu_{i}", new Vector3(dx, 0.45f, dz), new Vector3(1.2f, 0.45f, 1.2f), bronze);

            // 돌계단(어도 문턱, 장식) — 모델 있을 때만. 남쪽 울타리 어도 앞에 붙인다(울타리 반경과 연동).
            PlaceProp(root, "경복궁_돌계단", "EntranceSteps", new Vector3(15f, 0f, fzMin - 0.1f));

            // 석상 낙하 지점(광장 중앙, 근정전 정면 앞) — 기믹이 이름으로 찾는 빈 마커
            AddSpotless(root, "GuardianDropPoint", new Vector3(15f, 0f, 1f));

            // 북악산 원경(북쪽) — 회랑이 밀려난 만큼 산도 뒤로
            var m1 = AddBox(root, "Mountain_1", new Vector3(5f, 4f, 60f), new Vector3(56f, 24f, 16f), mtn);
            m1.transform.rotation = Quaternion.Euler(-38f, 0f, 0f); m1.isStatic = true;
            var m2 = AddBox(root, "Mountain_2", new Vector3(36f, 2f, 64f), new Vector3(48f, 20f, 14f), mtn);
            m2.transform.rotation = Quaternion.Euler(-42f, 8f, 0f); m2.isStatic = true;

            // ── 마커 5종 ──
            AddSpot(root, "Spot_GridManager", new Vector3(0f, 0f, 0f));
            AddSpot(root, "Spot_PlayerSpawnPoint", new Vector3(15f, 0f, -1f));   // 근정문 앞
            AddSpot(root, "Spot_HammerStation", new Vector3(8f, 0f, 1f));        // 마당 남서
            AddSpot(root, "Spot_PaintStation", new Vector3(22f, 0f, 1f));        // 마당 남동
            AddSpot(root, "Spot_BucketStation", new Vector3(11.5f, 0f, 1f));     // 양동이 도구함(화마 진화 — 경복궁 전용)
            AddSpot(root, "Spot_DeliveryZone", new Vector3(28.5f, 0f, 15.5f));   // 마당 동북(자재 하역 — 동쪽 받침대와 간섭 없게)
            return root;
        }

        // CFXR 등 서드파티 프리팹을 Resources로 사본 복사(런타임 Resources.Load용 — GroundHit 관행). 이미 있으면 통과.
        private static void EnsureFxCopy(string srcPath, string dstPath)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(dstPath) != null) return;
            if (AssetDatabase.LoadAssetAtPath<GameObject>(srcPath) == null)
            { Debug.LogWarning($"[경복궁] FX 원본이 없음: {srcPath}"); return; }
            Directory.CreateDirectory(Path.GetDirectoryName(dstPath));
            if (!AssetDatabase.CopyAsset(srcPath, dstPath))
                Debug.LogWarning($"[경복궁] FX 사본 실패: {srcPath} → {dstPath}");
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

        // Spot_ 접두사 없는 빈 마커 — MapLoader의 시스템 오브젝트 스폰 규약을 타지 않는 기믹 전용 지점.
        private static void AddSpotless(GameObject root, string name, Vector3 pos) => AddSpot(root, name, pos);

        // VARCO 배경 소품(_Fit, 바닥 피벗) 배치 — 모델 적용 툴이 만들어둔 경우에만. 서 있을 수 있게 콜라이더 보장.
        private static GameObject PlaceProp(GameObject root, string fitName, string instanceName, Vector3 pos, float yRot = 0f)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{kDir}/{fitName}_Fit.prefab");
            if (prefab == null) return null;
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
            inst.name = instanceName;
            inst.transform.localPosition = pos;
            inst.transform.localRotation = Quaternion.Euler(0f, yRot, 0f);
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
            return inst;
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
