using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace GridSystem.EditorTools
{
    /// <summary>
    /// ★ DDP(동대문디자인플라자) 맵 원클릭 생성 — 잔디지붕 데크 위 'DDP 본관' 건설 + 기믹 3종.
    ///
    /// · 파츠 MaterialDef 8종(id 31~38) + 색큐브 폴백 프리팹 → 전역 MaterialCatalog에 등록
    ///   (VARCO 모델이 나오면 {kDir}/{파츠이름}_Fit.prefab만 두고 재실행하면 자동 교체)
    /// · 정답 105칸(높이 9) DDP 본관: 기단 + 곡면익부 + 곡면본체 + 나선램프 + 유리커튼월 + 캐노피 + 패널 + LED장미
    /// · 배경 2층 구조 —
    ///     상층 '잔디지붕 데크'(y=0)  : 건축 그리드가 올라간다
    ///     하층 '어울림광장'(y=-4)    : 배송존·작업대·스폰
    ///     둘을 잇는 것은 ① 동쪽 곡면 나선램프(안전하지만 멀다) ② LED 장미 발판(빠르지만 타이밍)
    /// · 기믹 3종(DdpGimmickConfig) + 마커:
    ///     Spot_WaterChannel0~3 (이간수문 물길, 서→동)
    ///     Spot_DigSite0~3      (유구 발굴터 후보)
    ///     Spot_RosePad0~4      (LED 장미 발판 — 광장에서 데크로 오르는 공중 계단)
    ///
    /// 몇 번을 다시 실행해도 같은 결과(기존 에셋 덮어쓰기). 협동 전용.
    /// </summary>
    public static class DdpMapTool
    {
        private const string kDir         = "Assets/Prefabs/Map/4_Ddp";
        private const string kPrefabPath  = "Assets/Map/Prefabs/MapBg_Ddp.prefab";
        private const string kMapDefPath  = "Assets/Map/Maps/Map_Ddp.asset";
        private const string kAnswerPath  = kDir + "/Ans_Ddp.asset";
        private const string kConfigPath  = kDir + "/DdpGimmickConfig_Ddp.asset";
        private const string kThumbPath   = "Assets/Map/Maps/Thumb_Ddp.png";
        private const string kMatDir      = "Assets/Map/Materials";
        private const string kMapCatalogPath = "Assets/Resources/MapCatalog.asset";
        // ⚠ GameScene의 GridManager가 물고 있는 '전역 재료 카탈로그' — 여기 없는 재료는 주문이 무시된다.
        private const string kGlobalMaterialCatalogPath = "Assets/Prefabs/Map/1_KwangTongGyo/1_GwangTongGyo_MaterialCatalog.asset";

        private static readonly Vector3Int kGridSize = new Vector3Int(13, 12, 13);
        private const float kTimeLimitSeconds = 540f;   // 9분(105칸 — 롯데 91칸 8분보다 조금 여유)

        // ── 레벨 높이 ──
        private const float kDeckY  = 0f;     // 상층 잔디지붕 데크 상판(건축장)
        private const float kPlazaY = -4f;    // 하층 어울림광장 상판(배송·작업대)

        // ── 파츠 정의 : 이름, id(31~ — 광통교 1~8·튜토리얼 10~12·남산 12~20·롯데 21~28과 충돌 회피) ──
        private struct Part
        {
            public string Name; public int Id; public Vector3Int Fp;
            public ProcessType Proc; public Color Color; public bool MustFix;
        }

        private static readonly Part[] kParts =
        {
            // MustFix는 기단·본체만 — 나머지는 고정 강제 없이 쌓는다(남산·롯데와 같은 밸런스)
            new Part{ Name="DDP_기단",       Id=31, Fp=new Vector3Int(5,1,5), Proc=ProcessType.Fixed,   Color=new Color(0.80f,0.80f,0.82f), MustFix=true },
            new Part{ Name="DDP_곡면본체",   Id=32, Fp=new Vector3Int(3,3,3), Proc=ProcessType.Fixed,   Color=new Color(0.88f,0.89f,0.92f), MustFix=true },
            new Part{ Name="DDP_곡면익부",   Id=33, Fp=new Vector3Int(5,2,3), Proc=ProcessType.Fixed,   Color=new Color(0.84f,0.85f,0.88f), MustFix=false },
            new Part{ Name="DDP_은색패널",   Id=34, Fp=new Vector3Int(1,1,1), Proc=ProcessType.Painted, Color=new Color(0.92f,0.93f,0.95f), MustFix=false },
            new Part{ Name="DDP_나선램프",   Id=35, Fp=new Vector3Int(1,4,1), Proc=ProcessType.Fixed,   Color=new Color(0.90f,0.90f,0.88f), MustFix=false },
            new Part{ Name="DDP_유리커튼월", Id=36, Fp=new Vector3Int(1,2,1), Proc=ProcessType.Painted, Color=new Color(0.62f,0.80f,0.92f), MustFix=false },
            new Part{ Name="DDP_캐노피",     Id=37, Fp=new Vector3Int(3,1,3), Proc=ProcessType.None,    Color=new Color(0.86f,0.87f,0.90f), MustFix=false },
            new Part{ Name="DDP_LED장미",    Id=38, Fp=new Vector3Int(1,1,1), Proc=ProcessType.Painted, Color=new Color(0.98f,0.42f,0.68f), MustFix=false },
        };

        // ── DDP 본관 조립(정답): (파츠 id, 앵커 셀). 총 105칸, 높이 9(y0~8) ──
        // 실제 DDP처럼 '낮고 옆으로 흐르는 덩어리' — 롯데 매직캐슬(수직 첨탑형)과 실루엣이 겹치지 않게.
        private static readonly (int id, Vector3Int anchor)[] kBuilding =
        {
            (31, new Vector3Int(4, 0, 4)),    // 기단 5×1×5, y0 (25칸)
            (33, new Vector3Int(4, 1, 5)),    // 곡면익부 5×2×3, y1-2 (30칸) — 옆으로 긴 볼륨
            (35, new Vector3Int(4, 1, 4)),    // 나선램프 SW, y1-4 (4칸)
            (35, new Vector3Int(8, 1, 4)),    // 나선램프 SE, y1-4 (4칸)
            (36, new Vector3Int(4, 1, 8)),    // 유리커튼월 NW, y1-2 (2칸)
            (36, new Vector3Int(8, 1, 8)),    // 유리커튼월 NE, y1-2 (2칸)
            (32, new Vector3Int(5, 3, 5)),    // 곡면본체 3×3×3, y3-5 (27칸)
            (37, new Vector3Int(5, 6, 5)),    // 캐노피 3×1×3, y6 (9칸)
            (34, new Vector3Int(6, 7, 6)),    // 은색패널, y7 (1칸)
            (38, new Vector3Int(6, 8, 6)),    // LED장미, y8 → 총 높이 9 (1칸)
        };

        [MenuItem("Tools/Map/★ DDP 맵 생성 (실전)")]
        public static void Generate()
        {
            Directory.CreateDirectory(kDir);

            // ① 파츠 MaterialDef + 색큐브 프리팹
            var defs = new Dictionary<int, MaterialDef>();
            foreach (var p in kParts)
                defs[p.Id] = EnsurePartDef(p);

            // ② 전역 재료 카탈로그 등록(중복 없이 추가) — 없으면 주문이 조용히 무시된다!
            var matCatalog = AssetDatabase.LoadAssetAtPath<MaterialCatalog>(kGlobalMaterialCatalogPath);
            if (matCatalog == null) { Debug.LogError($"[DDP] 전역 MaterialCatalog이 없음: {kGlobalMaterialCatalogPath}"); return; }
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

            // ③ 정답(DDP 본관) — footprint대로 셀을 펼쳐 저장(익스포터와 동일 규칙)
            var answer = LoadOrCreate<MapAnswerData>(kAnswerPath);
            var cells = new List<(Vector3Int cell, int id)>();
            foreach (var (id, anchor) in kBuilding)
            {
                var fp = defs[id].Footprint;
                for (int dx = 0; dx < fp.x; dx++)
                for (int dy = 0; dy < fp.y; dy++)
                for (int dz = 0; dz < fp.z; dz++)
                    cells.Add((anchor + new Vector3Int(dx, dy, dz), id));
            }
            var ao = new SerializedObject(answer);
            ao.FindProperty("m_GridSize").vector3IntValue = kGridSize;
            ao.FindProperty("m_DisplayName").stringValue = "DDP 본관";
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

            // ④ 기믹 설정(기본값 = DdpGimmickConfig 필드 기본치)
            var cfg = LoadOrCreate<DdpGimmickConfig>(kConfigPath);
            EditorUtility.SetDirty(cfg);

            // ⑤ 그레이박스 배경 프리팹(데크·광장·물길·램프·원경)
            var root = BuildGreybox();
            Directory.CreateDirectory(Path.GetDirectoryName(kPrefabPath));
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, kPrefabPath, out bool ok);
            Object.DestroyImmediate(root);
            if (!ok) { Debug.LogError($"[DDP] 프리팹 저장 실패: {kPrefabPath}"); return; }

            // ⑥ 맵 카드
            var def2 = LoadOrCreate<MapDef>(kMapDefPath);
            var so = new SerializedObject(def2);
            so.FindProperty("m_DisplayName").stringValue = "DDP 동대문디자인플라자";
            so.FindProperty("m_BackgroundPrefab").objectReferenceValue = prefab;
            so.FindProperty("m_GridSize").vector3IntValue = kGridSize;
            so.FindProperty("m_DdpGimmicks").objectReferenceValue = cfg;
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
            if (mapCatalog == null) { Debug.LogError($"[DDP] MapCatalog이 없음: {kMapCatalogPath}"); return; }
            mapCatalog.EditorAdd(def2);
            EditorUtility.SetDirty(mapCatalog);

            AssetDatabase.SaveAssets();
            Selection.activeObject = def2;
            Debug.Log($"[DDP] 완료 ✔ 로비에서 'DDP 동대문디자인플라자'를 고르세요.\n" +
                      $"파츠 def 8종(id 31~38) {kDir} — VARCO 모델 나오면 {{파츠이름}}_Fit.prefab만 두고 재실행\n" +
                      $"정답 {cells.Count}칸(높이 9 DDP 본관) · 기믹 3종(물길·발굴터·장미발판) · 제한시간 {kTimeLimitSeconds / 60f:0.#}분");
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

        // ── 그레이박스 배경 ────────────────────────────────────────────────
        // 좌표 기준: Spot_GridManager=(0,0,0), 건축 그리드 x,z∈[0,13) 는 상층 데크 위.
        //
        //   북 ↑ z
        //        ┌────────────────────────────┐  z ≥ -4 : 잔디지붕 데크(y=0) — 건축장
        //        │        [건축 그리드]        │
        //        └──────┬──────────────┬──────┘  z=-4 : 레벨 차 옹벽(4m)
        //         장미발판↑            ↑곡면 나선램프(동)
        //        ┌────────────────────────────┐
        //        │   북쪽 광장 (램프 입구)      │  z ∈ [-12, -4]
        //        ╞════════ 이간수문 물길 ══════╡  z = -12 (서→동 흐름)
        //        │   남쪽 광장 (배송·작업대)    │  z ∈ [-24, -12]
        //        └────────────────────────────┘  어울림광장(y=-4)
        //
        // 핵심 동선: 배송존(남서) → 물길을 건너 → ① 동쪽 램프로 우회 또는 ② 장미 발판으로 직등 → 데크에서 건축.
        // 물길 급송: 재료를 서쪽 상류에 넣으면 동쪽 램프 입구까지 흘러간다(걷는 것보다 빠름).
        private static GameObject BuildGreybox()
        {
            var root = new GameObject("MapBg_Ddp");

            var deckMat  = EnsureMaterial("Mat_DdpDeck",   new Color(0.44f, 0.58f, 0.36f));  // 잔디지붕
            var plazaMat = EnsureMaterial("Mat_DdpPlaza",  new Color(0.80f, 0.79f, 0.76f));  // 광장 포장
            var silver   = EnsureMaterial("Mat_DdpSilver", new Color(0.78f, 0.80f, 0.83f));  // 알루미늄 패널
            var stone    = EnsureMaterial("Mat_DdpStone",  new Color(0.58f, 0.56f, 0.52f));  // 성곽 화강석
            var water    = EnsureMaterial("Mat_DdpWater",  new Color(0.28f, 0.60f, 0.85f));  // 수로 바닥
            var rose     = EnsureMaterial("Mat_DdpRose",   new Color(0.92f, 0.38f, 0.62f));  // LED 장미
            var soil     = EnsureMaterial("Mat_DdpSoil",   new Color(0.55f, 0.44f, 0.32f));  // 발굴터 흙

            // ── 상층: 잔디지붕 데크(건축장) ──
            AddBox(root, "Deck", new Vector3(6.5f, kDeckY - 0.5f, 8f), new Vector3(26f, 1f, 24f), deckMat).isStatic = true;
            // 데크 남쪽 옹벽(레벨 차 4m) — 광장에서 올려다보이는 면. 은색 패널 외피.
            AddBox(root, "DeckWall", new Vector3(6.5f, kDeckY - 2f, -4.4f), new Vector3(26f, 4f, 0.8f), silver).isStatic = true;

            // ── 하층: 어울림광장 ──
            AddBox(root, "Plaza", new Vector3(6.5f, kPlazaY - 0.5f, -14f), new Vector3(36f, 1f, 21f), plazaMat).isStatic = true;

            // ── 이간수문 물길(z=-12, 서→동) — 광장을 남북으로 가르는 수로 ──
            // 살짝 파인 홈. 물 자체는 WaterGateNetwork가 런타임에 띄운다(방류 중에만 보인다).
            AddBox(root, "WaterChannel", new Vector3(6.5f, kPlazaY - 0.35f, -12f), new Vector3(38f, 0.4f, 4.4f), water).isStatic = true;
            // 수로 양 둔치(석축)
            AddBox(root, "ChannelBank_S", new Vector3(6.5f, kPlazaY - 0.05f, -14.4f), new Vector3(38f, 0.3f, 0.6f), stone).isStatic = true;
            AddBox(root, "ChannelBank_N", new Vector3(6.5f, kPlazaY - 0.05f, -9.6f), new Vector3(38f, 0.3f, 0.6f), stone).isStatic = true;

            // 이간수문(수로 서쪽 상류 끝) — 2칸짜리 수문. VARCO 모델 우선, 없으면 석축 그레이박스.
            if (!TryPlaceProp(root, "DDP_이간수문", new Vector3(-13.5f, kPlazaY, -12f), 90f))
            {
                AddBox(root, "WaterGateWall", new Vector3(-13.5f, kPlazaY + 1.6f, -12f), new Vector3(1.2f, 3.2f, 6.4f), stone).isStatic = true;
                AddBox(root, "WaterGatePier", new Vector3(-13.5f, kPlazaY + 0.9f, -12f), new Vector3(1.4f, 1.8f, 0.7f), stone).isStatic = true;
                // 두 칸의 물구멍(어두운 안쪽)
                AddBox(root, "WaterGateHole_W", new Vector3(-13.5f, kPlazaY + 0.75f, -13.3f), new Vector3(1.5f, 1.5f, 1.7f), water);
                AddBox(root, "WaterGateHole_E", new Vector3(-13.5f, kPlazaY + 0.75f, -10.7f), new Vector3(1.5f, 1.5f, 1.7f), water);
            }

            // 한양도성 성곽 유구(수로 남쪽을 따라) — DDP 부지에서 실제 발굴된 성곽을 원경 장식으로
            for (int i = 0; i < 5; i++)
                AddBox(root, $"FortressWall{i}", new Vector3(-14f + i * 3.2f, kPlazaY + 0.45f, -17.5f),
                       new Vector3(3f, 0.9f, 1.1f), stone).isStatic = true;

            // ── 동쪽 곡면 나선램프: 광장(y=-4) → 데크(y=0) ──
            // DDP는 '계단 없는 건물'이라 지붕(잔디언덕)까지 외부 경사로로 걸어 올라간다. 그 동선을 그대로.
            BuildCurvedRamp(root, silver);

            // ── LED 장미정원(광장 북쪽 화단 장식) — 발판 자체는 LedRoseNetwork가 런타임 생성 ──
            for (int i = 0; i < 4; i++)
            {
                var p = new Vector3(-8f + i * 4.5f, kPlazaY + 0.15f, -6.5f);
                if (!TryPlaceProp(root, "DDP_장미화단", p))
                {
                    AddBox(root, $"RoseBed{i}", p + Vector3.up * 0.05f, new Vector3(3.6f, 0.4f, 1.6f), plazaMat).isStatic = true;
                    AddBox(root, $"RoseBloom{i}", p + Vector3.up * 0.3f, new Vector3(3.2f, 0.25f, 1.2f), rose).isStatic = true;
                }
            }

            // ── 발굴터 흙자국(런타임 발굴터가 이 자리에 뜬다 — 배경엔 흔적만) ──
            foreach (var (name, pos) in kDigSites)
                AddCylinder(root, $"DigSoil_{name}", pos + Vector3.down * 0.04f, new Vector3(3.8f, 0.06f, 3.8f), soil);

            // ── 원경: DDP 본관 실루엣(데크 북쪽 너머) ──
            if (!TryPlaceProp(root, "DDP_원경", new Vector3(6.5f, kDeckY, 30f), 0f, 1.4f))
            {
                AddBox(root, "SkylineBody", new Vector3(6.5f, kDeckY + 3.5f, 31f), new Vector3(30f, 7f, 10f), silver).isStatic = true;
                AddBox(root, "SkylineRoof", new Vector3(6.5f, kDeckY + 7.6f, 31f), new Vector3(24f, 1.4f, 8f), deckMat).isStatic = true;
            }

            // ── 마커: 필수 5종 + 기믹 마커 ──
            AddSpot(root, "Spot_GridManager", new Vector3(0f, kDeckY, 0f));                 // 짓는 곳(데크)
            AddSpot(root, "Spot_PlayerSpawnPoint", new Vector3(2f, kPlazaY + 0.1f, -16f));  // 광장 남쪽
            AddSpot(root, "Spot_DeliveryZone", new Vector3(-5f, kPlazaY + 0.1f, -17f));     // 광장 남서 — 재료는 여기로
            AddSpot(root, "Spot_PaintStation", new Vector3(-9f, kPlazaY + 0.3f, -20f));     // 광장 남서(0.3 — 바닥 파묻힘 방지)
            AddSpot(root, "Spot_HammerStation", new Vector3(1f, kPlazaY + 0.3f, -20f));     // 광장 남쪽(〃)

            // 이간수문 물길 경로(서→동 일직선, 수로 중앙 z=-12) — WaterGateNetwork가 0번부터 순서대로 잇는다.
            // 하류(동쪽)가 램프 입구 쪽이라, 재료를 상류에 넣으면 램프 앞까지 흘러간다.
            AddSpot(root, "Spot_WaterChannel0", new Vector3(-13f, kPlazaY, -12f));
            AddSpot(root, "Spot_WaterChannel1", new Vector3(0f, kPlazaY, -12f));
            AddSpot(root, "Spot_WaterChannel2", new Vector3(12f, kPlazaY, -12f));
            AddSpot(root, "Spot_WaterChannel3", new Vector3(24f, kPlazaY, -12f));

            // 유구 발굴터 후보(수로 양옆 — 물이 차면 잠긴다)
            for (int i = 0; i < kDigSites.Length; i++)
                AddSpot(root, $"Spot_DigSite{i}", kDigSites[i].pos);

            // LED 장미 발판 — 북쪽 광장(y=-4)에서 데크 남단(y=0)으로 오르는 공중 계단.
            // 램프보다 훨씬 짧지만, 피어 있는 동안에만 밟을 수 있어 타이밍을 봐야 한다.
            AddSpot(root, "Spot_RosePad0", new Vector3(6.5f, kPlazaY + 0.9f, -9.0f));
            AddSpot(root, "Spot_RosePad1", new Vector3(6.5f, kPlazaY + 1.8f, -7.6f));
            AddSpot(root, "Spot_RosePad2", new Vector3(6.5f, kPlazaY + 2.7f, -6.4f));
            AddSpot(root, "Spot_RosePad3", new Vector3(6.5f, kPlazaY + 3.5f, -5.4f));
            AddSpot(root, "Spot_RosePad4", new Vector3(6.5f, kDeckY + 0.2f, -3.2f));   // 데크 남단에 안착

            return root;
        }

        // 발굴터 후보 4곳 — 수로(z=-12) 양옆. 물이 차면 전부 잠긴다.
        private static readonly (string name, Vector3 pos)[] kDigSites =
        {
            ("A", new Vector3(-7f, kPlazaY + 0.05f, -9.0f)),
            ("B", new Vector3(4f,  kPlazaY + 0.05f, -15.0f)),
            ("C", new Vector3(14f, kPlazaY + 0.05f, -9.0f)),
            ("D", new Vector3(-2f, kPlazaY + 0.05f, -15.0f)),
        };

        // 곡면 나선램프(동쪽): 광장 y=-4 → 데크 y=0 을 원호로 잇는다.
        // 원호 중심 (12, ·, -7), 반지름 7, 각도 300°→60°(120° 스윕) = 호길이 ~14.7m, 상승 4m → 약 15° 경사.
        // 세그먼트 박스를 접선 방향으로 회전 + 피치를 줘 이어 붙인다(콜라이더 그대로 = 걸어 오를 수 있음).
        private static void BuildCurvedRamp(GameObject root, Material mat)
        {
            const int kSegments = 16;
            const float kCx = 12f, kCz = -7f, kRadius = 7f;
            const float kStartDeg = 300f, kEndDeg = 60f;
            const float kWidth = 3.4f;

            float arcLen = Mathf.Deg2Rad * (kEndDeg - kStartDeg) * kRadius;   // 총 호길이
            float rise = kDeckY - kPlazaY;
            float pitchDeg = -Mathf.Atan2(rise, arcLen) * Mathf.Rad2Deg;      // 위로 향하는 피치

            for (int i = 0; i < kSegments; i++)
            {
                float t0 = i / (float)kSegments;
                float t1 = (i + 1) / (float)kSegments;
                float a0 = Mathf.Lerp(kStartDeg, kEndDeg, t0) * Mathf.Deg2Rad;
                float a1 = Mathf.Lerp(kStartDeg, kEndDeg, t1) * Mathf.Deg2Rad;

                var p0 = new Vector3(kCx + kRadius * Mathf.Cos(a0), Mathf.Lerp(kPlazaY, kDeckY, t0), kCz + kRadius * Mathf.Sin(a0));
                var p1 = new Vector3(kCx + kRadius * Mathf.Cos(a1), Mathf.Lerp(kPlazaY, kDeckY, t1), kCz + kRadius * Mathf.Sin(a1));

                var mid = (p0 + p1) * 0.5f;
                var dir = p1 - p0;
                float segLen = dir.magnitude;

                var seg = AddBox(root, $"Ramp{i}", mid + Vector3.down * 0.15f,
                                 new Vector3(kWidth, 0.3f, segLen * 1.08f), mat);   // 1.08 = 이음새 겹침(틈 방지)
                var flat = new Vector3(dir.x, 0f, dir.z).normalized;
                seg.transform.localRotation = Quaternion.LookRotation(flat, Vector3.up) * Quaternion.Euler(pitchDeg, 0f, 0f);
                seg.isStatic = true;

                // 바깥쪽 난간(떨어짐 방지) — DDP 램프의 얇은 은색 핸드레일
                var outward = new Vector3(Mathf.Cos((a0 + a1) * 0.5f), 0f, Mathf.Sin((a0 + a1) * 0.5f));
                var rail = AddBox(root, $"RampRail{i}", mid + outward * (kWidth * 0.5f) + Vector3.up * 0.5f,
                                  new Vector3(0.16f, 1.0f, segLen * 1.08f), mat);
                rail.transform.localRotation = seg.transform.localRotation;
                rail.isStatic = true;
            }
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
                if (sh == null) { Debug.LogWarning("[DDP] URP Lit 셰이더를 못 찾음"); return null; }
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
