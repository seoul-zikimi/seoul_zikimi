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
    /// · 정답 125칸 DDP 본관(폭 12 × 깊이 4 × 높이 4) — 실제처럼 '낮고 옆으로 흐르는' 비대칭 덩어리
    /// · 배경 2층 구조 —
    ///     상층 '잔디지붕 데크'(y=0)  : 건축 그리드가 올라간다
    ///     하층 '어울림광장'(y=-4)    : 배송존·작업대·스폰
    ///     둘을 잇는 것은 동쪽 곡면 나선램프(넓게 뚫어 병목이 없다)
    /// · 기믹 2종(DdpGimmickConfig) + 마커:
    ///     Spot_WaterChannel0~3 (이간수문 물길, 서→동)
    ///     Spot_DigSite0~3      (유구 발굴터 후보)
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

        // 14×6×14 — 통짜 DDP 조각(DdpSliceTool.kSpan = 13×5×10)이 들어갈 만큼.
        // 높이 6인 건 정답이 낮고 넓은 덩어리라서 — 예전 12층 그리드는 빈 상자만 커 보였다.
        // 데크(x∈[-6.5,19.5], z∈[-4,20])가 이 그리드를 다 받쳐 준다.
        private static readonly Vector3Int kGridSize = new Vector3Int(14, 6, 14);
        private const float kTimeLimitSeconds = 600f;   // 10분(125칸 — 롯데 91칸 8분과 분당 난이도 비슷)

        // ── 레벨 높이 ──
        private const float kDeckY  = 0f;     // 상층 잔디지붕 데크 상판(건축장)
        private const float kPlazaY = -4f;    // 하층 어울림광장 상판(배송·작업대)

        // ── 파츠 정의 : 이름, id(31~ — 광통교 1~8·튜토리얼 10~12·남산 12~20·롯데 21~28과 충돌 회피) ──
        private struct Part
        {
            public string Name; public int Id; public Vector3Int Fp;
            public ProcessType Proc; public Color Color; public bool MustFix;
        }

        // footprint는 VARCO 원본 메시의 실측 비율을 보고 정했다 — 칸 비율이 메시 비율에서 멀수록 모델이 뭉개진다.
        // (예: 곡면익부 원본은 1 : 0.126 : 0.206 인 '길고 낮고 얇은' 덩어리.
        //      예전 5×2×3(=1:0.4:0.6)은 세로로 3.2배 부풀려 블록처럼 보였다 → 6×1×3(=1:0.167:0.5)로 교정)
        // ⚠ 바꾸면 DdpModelApplyTool.kParts도 같이 고쳐야 한다.
        private static readonly Part[] kParts =
        {
            // MustFix는 기단·본체만 — 나머지는 고정 강제 없이 쌓는다(남산·롯데와 같은 밸런스)
            new Part{ Name="DDP_기단",       Id=31, Fp=new Vector3Int(5,1,4), Proc=ProcessType.Fixed,   Color=new Color(0.80f,0.80f,0.82f), MustFix=true },
            new Part{ Name="DDP_곡면본체",   Id=32, Fp=new Vector3Int(4,2,3), Proc=ProcessType.Fixed,   Color=new Color(0.88f,0.89f,0.92f), MustFix=true },
            new Part{ Name="DDP_곡면익부",   Id=33, Fp=new Vector3Int(6,1,3), Proc=ProcessType.Fixed,   Color=new Color(0.84f,0.85f,0.88f), MustFix=false },
            new Part{ Name="DDP_은색패널",   Id=34, Fp=new Vector3Int(2,2,1), Proc=ProcessType.Painted, Color=new Color(0.92f,0.93f,0.95f), MustFix=false },
            new Part{ Name="DDP_나선램프",   Id=35, Fp=new Vector3Int(1,2,1), Proc=ProcessType.Fixed,   Color=new Color(0.90f,0.90f,0.88f), MustFix=false },
            new Part{ Name="DDP_유리커튼월", Id=36, Fp=new Vector3Int(1,2,1), Proc=ProcessType.Painted, Color=new Color(0.62f,0.80f,0.92f), MustFix=false },
            new Part{ Name="DDP_캐노피",     Id=37, Fp=new Vector3Int(3,1,3), Proc=ProcessType.None,    Color=new Color(0.86f,0.87f,0.90f), MustFix=false },
            new Part{ Name="DDP_LED장미",    Id=38, Fp=new Vector3Int(1,1,1), Proc=ProcessType.Painted, Color=new Color(0.98f,0.42f,0.68f), MustFix=false },
        };

        // ── DDP 본관 조립(정답): (파츠 id, 앵커 셀). 총 125칸, 폭 12 × 깊이 4 × 높이 4(y0~3) ──
        //
        // 실제 DDP는 '탑'이 아니라 땅에 낮게 깔려 옆으로 흐르는 비대칭 덩어리다.
        // 서쪽으로 갈수록 낮아지는 꼬리(아트홀), 동쪽에 크고 둥근 머리(뮤지엄·디자인랩),
        // 그 사이 전면에 파고든 입구(유리 + 원통 코어), 지붕은 잔디언덕처럼 덮인 셸.
        // LED 장미정원은 건물 위가 아니라 '옆 지면'에 있다 — 그래서 y0 양옆에 심는다.
        //
        //   y=3        ▓▓▓            머리 지붕(잔디언덕)
        //   y=2  ▒▒▒▒▒▒▓▓▓▓           꼬리 지붕 셸 + 머리
        //   y=1  ▒▒▒▒▒▒▓▓▓▓           익부(아트홀) + 본체(뮤지엄), 전면 z=4에 입구
        //   y=0 ●████████████●         기단 + 양옆 LED 장미
        //      x1                x12
        private static readonly (int id, Vector3Int anchor)[] kBuilding =
        {
            // 기단(광장 데크) — 폭 10 × 깊이 4
            (31, new Vector3Int(2,  0, 4)),   // 5×1×4  x2-6,  z4-7   (20칸)
            (31, new Vector3Int(7,  0, 4)),   // 5×1×4  x7-11, z4-7   (20칸)

            // 상부 매스는 기단보다 한 칸 물러나 앉는다(z5-7) — 전면 z4가 처마 밑 그늘이 된다.
            (33, new Vector3Int(2,  1, 5)),   // 곡면익부 6×1×3  x2-7,  y1,   z5-7 (18칸) — 낮게 흐르는 꼬리
            (32, new Vector3Int(8,  1, 5)),   // 곡면본체 4×2×3  x8-11, y1-2, z5-7 (24칸) — 크고 둥근 머리

            // 입구 전면(z=4): 원통 코어 2 + 그 사이 유리 + 꼬리 쪽 은색 패널
            (35, new Vector3Int(8,  1, 4)),   // 나선램프 1×2×1 (2칸)
            (35, new Vector3Int(11, 1, 4)),   // 나선램프 1×2×1 (2칸)
            (36, new Vector3Int(9,  1, 4)),   // 유리커튼월 1×2×1 (2칸)
            (36, new Vector3Int(10, 1, 4)),   // 유리커튼월 1×2×1 (2칸)
            (34, new Vector3Int(4,  1, 4)),   // 은색패널 2×2×1  x4-5, y1-2, z4 (4칸)

            // 지붕 셸 — 꼬리는 y2, 머리는 한 층 높은 y3
            (37, new Vector3Int(2,  2, 5)),   // 캐노피 3×1×3  x2-4,  z5-7 (9칸)
            (37, new Vector3Int(5,  2, 5)),   // 캐노피 3×1×3  x5-7,  z5-7 (9칸)
            (37, new Vector3Int(8,  3, 5)),   // 캐노피 3×1×3  x8-10, z5-7 (9칸) — 머리 잔디언덕

            // LED 장미정원 — 건물 양옆 지면(y0). 예전엔 꼭대기에 한 송이 꽂혀 있어 '케이크 촛불'처럼 보였다.
            (38, new Vector3Int(1,  0, 5)),
            (38, new Vector3Int(1,  0, 6)),
            (38, new Vector3Int(12, 0, 6)),
            (38, new Vector3Int(12, 0, 7)),
        };

        [MenuItem("Tools/Map/★ DDP 맵 생성 (실전)")]
        public static void Generate()
        {
            Directory.CreateDirectory(kDir);

            // ① 파츠 MaterialDef + 색큐브 프리팹
            var defs = new Dictionary<int, MaterialDef>();
            foreach (var p in kParts)
                defs[p.Id] = EnsurePartDef(p);

            // ①' 통짜 DDP를 격자로 자른 '고유 곡면 조각'이 있으면 그걸 정답으로 쓴다.
            //     직육면체 파츠를 쌓는 방식으로는 DDP 특유의 연속된 곡면이 절대 안 나온다 —
            //     조각들이 원래 한 몸이라 다 맞추면 실제 공사진 그대로 이어진다.
            //     통짜 GLB가 없으면 null이 와서 아래 kBuilding(블록 방식)으로 폴백한다.
            var sliced = DdpSliceTool.Slice();
            if (sliced != null)
                foreach (var s in sliced)
                    defs[s.Def.Id] = s.Def;

            // ② 전역 재료 카탈로그 등록(중복 없이 추가) — 없으면 주문이 조용히 무시된다!
            var matCatalog = AssetDatabase.LoadAssetAtPath<MaterialCatalog>(kGlobalMaterialCatalogPath);
            if (matCatalog == null) { Debug.LogError($"[DDP] 전역 MaterialCatalog이 없음: {kGlobalMaterialCatalogPath}"); return; }
            var mc = new SerializedObject(matCatalog);
            var list = mc.FindProperty("m_Materials");
            // 죽은 참조 정리 — 절단 조각은 재실행마다 새로 만들어지므로(옛 def은 삭제됨) 빈칸이 남는다.
            // 빈칸을 두면 MapAuthoringValidationTests의 '카탈로그에_빈칸이_없다'가 깨진다.
            //
            // 같이 치워야 하는 게 하나 더 있다: 이번에 만든 def과 '같은 Id를 쓰는 다른 def'.
            // 절단 스킴이 바뀌면(예: 조각_03~09 → 조각_10~30) 옛 def이 살아남아 Id가 겹치는데,
            // RebuildLookup은 뒤에 온 것이 이기므로 목록 순서에 따라 엉뚱한 조각이 배달·배치된다.
            // 실제로 이것 때문에 DDP id 43·44가 옛 조각으로 해석돼 정답 100%가 불가능했다.
            var claimed = new HashSet<int>(defs.Keys);
            for (int i = list.arraySize - 1; i >= 0; i--)
            {
                var cur = list.GetArrayElementAtIndex(i).objectReferenceValue;
                if (cur == null) { list.DeleteArrayElementAtIndex(i); continue; }
                if (cur is MaterialDef md && !defs.ContainsValue(md) && claimed.Contains(md.Id))
                {
                    Debug.Log($"[DDP] 카탈로그에서 Id 중복 def 제거: '{md.name}'(id {md.Id}) — 이번 세팅이 같은 Id를 씁니다.");
                    list.DeleteArrayElementAtIndex(i);
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

            // ③ 정답(DDP 본관) — footprint대로 셀을 펼쳐 저장(익스포터와 동일 규칙)
            var answer = LoadOrCreate<MapAnswerData>(kAnswerPath);
            var cells = new List<(Vector3Int cell, int id)>();
            var plan = new List<(int id, Vector3Int anchor)>();
            if (sliced != null)
                foreach (var s in sliced) plan.Add((s.Def.Id, s.Anchor));
            else
                plan.AddRange(kBuilding);

            foreach (var (id, anchor) in plan)
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
            // 완성체: 다 지으면 조각 대신 얹히고, 완공 계획도(정답 UI)도 이걸 쓴다.
            so.FindProperty("m_CompletedModel").objectReferenceValue = sliced != null ? DdpSliceTool.CompletedModel : null;
            so.FindProperty("m_CompletedModelAnchor").vector3IntValue = DdpSliceTool.CompletedAnchor;
            var answers = so.FindProperty("m_Answers");
            answers.arraySize = 1;
            answers.GetArrayElementAtIndex(0).objectReferenceValue = answer;
            // 주문 가능 재료 = 정답에 실제로 쓰인 것만. 절단 조각을 쓰는 경우 예전 블록 파츠는 목록에서 빠진다.
            var mats = so.FindProperty("m_AvailableMaterials");
            var orderable = new List<MaterialDef>();
            if (sliced != null) foreach (var s in sliced) orderable.Add(s.Def);
            else foreach (var p in kParts) orderable.Add(defs[p.Id]);
            mats.arraySize = orderable.Count;
            for (int i = 0; i < orderable.Count; i++)
                mats.GetArrayElementAtIndex(i).objectReferenceValue = orderable[i];
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
                      $"정답 {cells.Count}칸(낮고 넓은 DDP 본관) · 기믹 2종(물길·발굴터) · 제한시간 {kTimeLimitSeconds / 60f:0.#}분");
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
            // 폭 36 = 광장 폭과 동일 — 예전 38은 양옆으로 1m씩 허공에 튀어나와 있었다.
            AddBox(root, "WaterChannel", new Vector3(6.5f, kPlazaY - 0.35f, -12f), new Vector3(36f, 0.4f, 4.4f), water).isStatic = true;
            // 수로 양 둔치(석축)
            AddBox(root, "ChannelBank_S", new Vector3(6.5f, kPlazaY - 0.05f, -14.4f), new Vector3(36f, 0.3f, 0.6f), stone).isStatic = true;
            AddBox(root, "ChannelBank_N", new Vector3(6.5f, kPlazaY - 0.05f, -9.6f), new Vector3(36f, 0.3f, 0.6f), stone).isStatic = true;

            // 이간수문(수로 서쪽 상류 끝) — 2칸짜리 수문. VARCO 모델 우선, 없으면 석축 그레이박스.
            // 모델은 원본 비율대로 7.0(길이) × 3.5(높이) × 1.7(두께)로 맞춰져 있다 —
            // 90° 돌려 길이 7m가 수로(z)를 가로지르게 세운다. x=-9.5면 두께 1.7이 광장(x≥-11.5) 안에 온전히 앉는다.
            // (예전엔 x=-13.5에 축별로 늘어난 모델을 놔서, 7m 중 5.5m가 광장 밖 허공에 뜬 채
            //  석축이 판자로 뭉개져 '이끼 낀 블럭 더미'처럼 보였다)
            if (!TryPlaceProp(root, "DDP_이간수문", new Vector3(-9.5f, kPlazaY, -12f), 90f))
            {
                AddBox(root, "WaterGateWall", new Vector3(-9.5f, kPlazaY + 1.6f, -12f), new Vector3(1.2f, 3.2f, 6.4f), stone).isStatic = true;
                AddBox(root, "WaterGatePier", new Vector3(-9.5f, kPlazaY + 0.9f, -12f), new Vector3(1.4f, 1.8f, 0.7f), stone).isStatic = true;
                // 두 칸의 물구멍(어두운 안쪽)
                AddBox(root, "WaterGateHole_W", new Vector3(-9.5f, kPlazaY + 0.75f, -13.3f), new Vector3(1.5f, 1.5f, 1.7f), water);
                AddBox(root, "WaterGateHole_E", new Vector3(-9.5f, kPlazaY + 0.75f, -10.7f), new Vector3(1.5f, 1.5f, 1.7f), water);
            }

            // 한양도성 성곽 유구(수로 남쪽을 따라) — DDP 부지에서 실제 발굴된 성곽을 원경 장식으로.
            // x=-10부터 — 예전 -14는 첫 두 장이 광장(x≥-11.5) 밖 허공에 걸쳐 있었다.
            for (int i = 0; i < 5; i++)
                AddBox(root, $"FortressWall{i}", new Vector3(-10f + i * 3.2f, kPlazaY + 0.45f, -17.5f),
                       new Vector3(3f, 0.9f, 1.1f), stone).isStatic = true;

            // ── 동쪽 곡면 나선램프: 광장(y=-4) → 데크(y=0) ──
            // DDP는 '계단 없는 건물'이라 지붕(잔디언덕)까지 외부 경사로로 걸어 올라간다. 그 동선을 그대로.
            BuildCurvedRamp(root, silver);

            // ── LED 장미정원(광장 북쪽) — 한 송이씩 심는다 ──
            BuildRoseGarden(root, stone, soil, rose);

            // 발굴터는 배경에 아무것도 두지 않는다 — 평소엔 '묻혀 있어야' 하니까.
            // 런타임에 ExcavationNetwork가 Spot_DigSite* 중 한 곳에 표지 말뚝을 솟게 한다.
            // (예전엔 유구터 흙구덩이 프롭 4개가 상시로 깔려 있어 광장이 어수선했다)

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
            AddSpot(root, "Spot_WaterChannel0", new Vector3(-11f, kPlazaY, -12f));
            AddSpot(root, "Spot_WaterChannel1", new Vector3(0f, kPlazaY, -12f));
            AddSpot(root, "Spot_WaterChannel2", new Vector3(12f, kPlazaY, -12f));
            AddSpot(root, "Spot_WaterChannel3", new Vector3(24f, kPlazaY, -12f));

            // 유구 발굴터 후보(수로 양옆 — 물이 차면 잠긴다)
            for (int i = 0; i < kDigSites.Length; i++)
                AddSpot(root, $"Spot_DigSite{i}", kDigSites[i].pos);

            // ⚠ Spot_RosePad* 는 더 이상 만들지 않는다 — 'LED 장미 발판' 기믹을 뺐다.
            // 광장 위에 분홍 원판이 둥둥 떠 있는 그림이 보기 싫고 동선에도 도움이 안 됐다.
            // 마커가 없으면 LedRoseNetwork는 스스로 잠잔다. 광장(y=-4) → 데크(y=0)는 동쪽 나선램프 하나로 간다.

            return root;
        }

        // ── LED 장미정원: 화단 '덩어리' 대신 한 송이씩 심는다 ─────────────────
        // 실제 DDP 장미정원은 가느다란 줄기 위 LED 장미 25,550송이가 촘촘히 박힌 밭이다.
        // 예전엔 화단 프롭 4덩이를 띄엄띄엄 놨더니 조잡하고 밭처럼 안 보였다.
        // 밟는 발판(Spot_RosePad*)은 여기와 별개로 LedRoseNetwork가 런타임에 만든다.
        private const float kRoseSpacing = 1.1f;                        // 송이 간격(m)
        private const float kGardenX0 = -10.5f, kGardenX1 = 2.5f;       // 밭 범위(광장 북쪽, 램프·발판 동선을 피해서)
        private const float kGardenZ0 = -8.8f,  kGardenZ1 = -4.8f;

        private static void BuildRoseGarden(GameObject root, Material curbMat, Material soilMat, Material roseMat)
        {
            float cx = (kGardenX0 + kGardenX1) * 0.5f, cz = (kGardenZ0 + kGardenZ1) * 0.5f;
            float w = kGardenX1 - kGardenX0, d = kGardenZ1 - kGardenZ0;

            // 어두운 흙바닥 + 낮은 화강석 연석(밭의 테두리)
            AddBox(root, "RoseBedSoil", new Vector3(cx, kPlazaY + 0.03f, cz), new Vector3(w, 0.06f, d), soilMat).isStatic = true;
            AddBox(root, "RoseCurb_S", new Vector3(cx, kPlazaY + 0.09f, kGardenZ0), new Vector3(w + 0.3f, 0.18f, 0.3f), curbMat).isStatic = true;
            AddBox(root, "RoseCurb_N", new Vector3(cx, kPlazaY + 0.09f, kGardenZ1), new Vector3(w + 0.3f, 0.18f, 0.3f), curbMat).isStatic = true;
            AddBox(root, "RoseCurb_W", new Vector3(kGardenX0, kPlazaY + 0.09f, cz), new Vector3(0.3f, 0.18f, d), curbMat).isStatic = true;
            AddBox(root, "RoseCurb_E", new Vector3(kGardenX1, kPlazaY + 0.09f, cz), new Vector3(0.3f, 0.18f, d), curbMat).isStatic = true;

            var rosePrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{kDir}/DDP_장미한송이_Fit.prefab");

            // 시드 고정 — 흔들기가 있어도 몇 번을 재실행하든 같은 밭이 나온다(툴의 멱등성 유지).
            var prevRandom = Random.state;
            Random.InitState(20260822);

            int nx = Mathf.FloorToInt((w - 1f) / kRoseSpacing);
            int nz = Mathf.FloorToInt((d - 1f) / kRoseSpacing);
            int planted = 0;
            for (int iz = 0; iz <= nz; iz++)
            for (int ix = 0; ix <= nx; ix++)
            {
                // 지그재그(줄마다 반 칸 어긋나게) — 격자 티가 덜 난다
                float px = kGardenX0 + 0.5f + ix * kRoseSpacing + (iz % 2 == 0 ? 0f : kRoseSpacing * 0.5f);
                if (px > kGardenX1 - 0.4f) continue;
                float pz = kGardenZ0 + 0.5f + iz * kRoseSpacing;

                var p = new Vector3(px + Random.Range(-0.13f, 0.13f), kPlazaY + 0.06f, pz + Random.Range(-0.13f, 0.13f));
                PlantRose(root, rosePrefab, roseMat, p, planted++);
            }

            Random.state = prevRandom;
            Debug.Log($"[DDP] LED 장미정원: {planted}송이 식재{(rosePrefab == null ? " (모델 없음 — 그레이박스 폴백)" : "")}");
        }

        // 장미 한 송이. 콜라이더는 붙이지 않는다 — 밭을 지나갈 때 걸리면 안 되고, 48개나 되니 물리도 아깝다.
        private static void PlantRose(GameObject root, GameObject prefab, Material roseMat, Vector3 pos, int index)
        {
            GameObject go;
            if (prefab != null)
            {
                go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
                go.transform.localScale *= Random.Range(0.85f, 1.15f);   // 키가 조금씩 다르게
            }
            else
            {
                // 폴백: 가는 줄기 + 봉오리(모델이 없을 때도 밭처럼은 보이게)
                go = new GameObject($"Rose{index}");
                go.transform.SetParent(root.transform, false);
                var stem = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                stem.name = "stem";
                stem.transform.SetParent(go.transform, false);
                stem.transform.localPosition = Vector3.up * 0.3f;
                stem.transform.localScale = new Vector3(0.04f, 0.3f, 0.04f);
                Object.DestroyImmediate(stem.GetComponent<Collider>());
                var bloom = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                bloom.name = "bloom";
                bloom.transform.SetParent(go.transform, false);
                bloom.transform.localPosition = Vector3.up * 0.66f;
                bloom.transform.localScale = Vector3.one * 0.17f;
                Object.DestroyImmediate(bloom.GetComponent<Collider>());
                if (roseMat != null) bloom.GetComponent<Renderer>().sharedMaterial = roseMat;
            }
            // 콜라이더는 애초에 붙이지 않는다(GLB _Fit 프리팹엔 없고, 폴백은 위에서 지웠다).
            // 프리팹 인스턴스에서 컴포넌트를 지우면 Unity가 막으므로 여기서 일괄 제거는 하지 않는다.
            go.name = $"Rose{index}";
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            go.isStatic = true;
        }

        // 발굴터 후보 4곳 — 수로(z=-12) 양옆에 흩어놓는다. 물이 차면 전부 잠긴다.
        // 장미밭(x -10.5~2.5, z -8.8~-4.8)·작업대·램프와 겹치지 않게 배치했다.
        private static readonly (string name, Vector3 pos)[] kDigSites =
        {
            ("A", new Vector3(-6f, kPlazaY + 0.05f, -21.5f)),   // 남서(광장 안쪽 깊이)
            ("B", new Vector3(4f,  kPlazaY + 0.05f, -15.0f)),   // 남중 — 수로 바로 아래
            ("C", new Vector3(14f, kPlazaY + 0.05f, -9.0f)),    // 북동 — 램프 입구 쪽
            ("D", new Vector3(-2f, kPlazaY + 0.05f, -15.0f)),   // 남중서
        };

        // 곡면 나선램프(동쪽): 광장 y=-4 → 데크 y=0 을 원호로 잇는다.
        // 원호 중심 (12, ·, -7), 반지름 7, 각도 300°→60°(120° 스윕) = 호길이 ~14.7m, 상승 4m → 약 15° 경사.
        // 세그먼트 박스를 접선 방향으로 회전 + 피치를 줘 이어 붙인다(콜라이더 그대로 = 걸어 오를 수 있음).
        //
        // 폭 5.6m — 장미 발판을 뺀 뒤로 위층으로 가는 길이 여기 하나뿐이라, 4인이 재료를 들고
        // 서로 비켜갈 수 있어야 병목이 안 생긴다(예전 3.4m는 둘이 마주치면 막혔다).
        private static void BuildCurvedRamp(GameObject root, Material mat)
        {
            const int kSegments = 16;
            const float kCx = 12f, kCz = -7f, kRadius = 7f;
            const float kStartDeg = 300f, kEndDeg = 60f;
            const float kWidth = 5.6f;

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
