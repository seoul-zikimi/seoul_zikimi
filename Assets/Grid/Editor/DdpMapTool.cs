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
    ///     둘을 잇는 것은 곡면 나선램프(배송존 옆에서 시작해 물길 위를 다리로 감아 오른다)
    /// · 기믹 1종(DdpGimmickConfig) + 마커:
    ///     Spot_WaterChannel0~3 (이간수문 물길, 서→동)
    ///     (유구 발굴터·LED 장미 발판 기믹은 뺐다 — 08/31 기획 결정, 물길 하나로 충분)
    /// · ★ 야경 컨셉 — 실제 DDP는 밤이 본체다(은색 패널 라이트업 + LED 장미 25,550송이가 밤 명물):
    ///     MapNightAmbience(밤 하늘·안개·앰비언트·달빛 — 씬은 낮 그대로, 이 맵 로드 때만 오버라이드)
    ///     가로등(광장·데크 둘레) + 불빛 웅덩이(가산 쿼드 — 진짜 라이트 예산 0)
    ///     작업대·배송존·램프 입구엔 진짜 포인트 라이트(소수만 — 모바일 예산)
    ///     데크 옹벽 LED 미디어 스트립 2줄(시안·마젠타), 나선램프 난간 조명
    ///     NightBuildGlow — 지은 블록·LED 장미가 밤에 자체 발광(미디어 파사드)
    ///
    /// 몇 번을 다시 실행해도 같은 결과(기존 에셋 덮어쓰기). 협동 전용.
    /// </summary>
    public static class DdpMapTool
    {
        private const string kDir         = "Assets/Prefabs/Map/4_Ddp";
        // 배경 프리팹은 빌드가 Resources 경로로 지연 로드한다(MapDef.m_BackgroundPrefabPath — iOS 로비 메모리 보호).
        // 예전 경로(Assets/Map/Prefabs)에 저장하면 재생성 때마다 맵 카드가 Resources 밖을 가리키게 회귀한다.
        private const string kPrefabPath  = "Assets/Resources/MapPrefabs/MapBg_Ddp.prefab";
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

            // ⑦ 썸네일 + 맵 카탈로그 — 썸네일도 밤으로 찍는다(로비 카드에서 야경 컨셉이 보여야 한다)
            var thumb = CaptureNightThumbnail(prefab);
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
                      $"정답 {cells.Count}칸(낮고 넓은 DDP 본관) · 기믹 1종(이간수문 물길) · 제한시간 {kTimeLimitSeconds / 60f:0.#}분\n" +
                      $"★ 야경 컨셉 — 밤 톤은 프리팹의 MapNightAmbience, 밤하늘은 {kNightSkyPath}, 블록 발광은 NightBuildGlow에서 조절");
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
        //        └───────────────────┬────────┘  z=-4 : 레벨 차 옹벽(4m). ┬ = 나선램프 출구(16,-4)
        //        ┌──────────────────/╮────────┐
        //        │   북쪽 광장     램프│        │  z ∈ [-12, -4]
        //        ╞════════ 이간수문 물│길 ═════╡  z = -12 (서→동 흐름, 램프는 상공 ~1.5m로 건넘)
        //        │   남쪽 광장    ╰──╯        │  z ∈ [-24, -12]
        //        │  (배송·작업대) ↑램프 입구(2,-15.2~) — 물길 남쪽, 배송존 옆
        //        └────────────────────────────┘  어울림광장(y=-4)
        //
        // 핵심 동선: 배송존(남서) → 바로 옆 나선램프 입구 → 동쪽으로 감아 오르며 물길을 상공 다리로 넘어 → 데크에서 건축.
        // 물길 급송: 재료를 서쪽 상류에 넣으면 동쪽 하류까지 흘러간다(걷는 것보다 빠름).
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

            // 데크 하부 은색 마감(서·동·북) — 예전엔 비주얼정리 툴의 '초록 스커트'(찌부 스피어)가 이 밑을
            // 메웠는데, 남쪽으로 부풀어 광장의 LED 장미밭까지 초록 언덕으로 덮어버렸다(08/31 발견).
            // 스커트는 껐고(MapVisualPolishTool의 DDP 프로필), 대신 실물처럼 은색 패널로 옆면을 닫는다.
            AddBox(root, "DeckSkirt_W", new Vector3(-6.7f, kDeckY - 3f, 8f),  new Vector3(0.5f, 5.2f, 24.4f), silver).isStatic = true;
            AddBox(root, "DeckSkirt_E", new Vector3(19.7f, kDeckY - 3f, 8f),  new Vector3(0.5f, 5.2f, 24.4f), silver).isStatic = true;
            AddBox(root, "DeckSkirt_N", new Vector3(6.5f,  kDeckY - 3f, 20.2f), new Vector3(26.9f, 5.2f, 0.5f), silver).isStatic = true;

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

            // ⚠ 성곽 유구(FortressWall) 그레이박스는 두지 않는다 — 배송존(Spot_DeliveryZone, z=-17)과
            // 같은 자리(z=-17.5)에 깔려 있어, 주문한 재료가 회색 블록들 사이·뒤로 떨어져
            // 가려지고 줍기도 불편했다. 성곽 유구 연출이 필요하면 배송 동선 밖에서 다시 설계할 것.

            // ── 나선 램프: 배송존 옆 광장(y=-4)에서 동쪽으로 감아 올라 물길 위를 지나 데크(y=0)로 ──
            // DDP는 '계단 없는 건물'이라 지붕(잔디언덕)까지 외부 경사로로 걸어 올라간다. 그 동선을 그대로.
            var lampGlow = EnsureEmissiveMaterial("Mat_DdpLampGlow", new Color(1.00f, 0.90f, 0.70f), new Color(1.00f, 0.82f, 0.45f) * 3.0f);   // 야경 강화: 2.4 → 3.0
            BuildCurvedRamp(root, silver, lampGlow);

            // ── LED 장미정원(광장 북쪽) — 한 송이씩 심는다 ──
            var roseGroup = BuildRoseGarden(root, stone, soil, rose);

            // ── ★ 야경 — 가로등·LED 스트립·포인트 라이트 + 밤 환경/발광 컴포넌트 ──
            BuildNightScape(root, lampGlow, roseGroup);

            // ── 투명 경계벽 — 유저가 맵 밖(광장 가장자리·데크 모서리)으로 떨어져 못 돌아오는 사고 방지 ──
            BuildBoundaryWalls(root);

            // (유구 발굴터 연출·프롭은 기믹 제거(08/31)와 함께 완전히 뺐다)

            // ⚠ 원경(DDP_원경 실루엣)은 두지 않는다 — 완성된 DDP 원본이 공중에 떠 보이는 데다,
            // '지어야 할 정답'(인월드 고스트·완공 계획도)과 겹쳐 어느 쪽이 목표인지 헷갈렸다.
            // 완성형 DDP는 정답 UI(완공 계획도)와 다 지었을 때의 완성체 교체로만 보여준다.

            // ── 마커: 필수 5종 + 기믹 마커 ──
            AddSpot(root, "Spot_GridManager", new Vector3(0f, kDeckY, 0f));                 // 짓는 곳(데크)
            AddSpot(root, "Spot_PlayerSpawnPoint", new Vector3(-1f, kPlazaY + 0.1f, -16.5f));  // 광장 남쪽(나선램프 입구 살짝 서쪽 — 입구 턱 위 스폰 방지)
            AddSpot(root, "Spot_DeliveryZone", new Vector3(-5f, kPlazaY + 0.1f, -17f));     // 광장 남서 — 재료는 여기로
            AddSpot(root, "Spot_PaintStation", new Vector3(-9f, kPlazaY, -20f));            // 광장 남서(마커 Y = 접지점 — MapLoader가 반높이 올린다)
            AddSpot(root, "Spot_HammerStation", new Vector3(1f, kPlazaY, -20f));            // 광장 남쪽(〃)

            // 이간수문 물길 경로(서→동 일직선, 수로 중앙 z=-12) — WaterGateNetwork가 0번부터 순서대로 잇는다.
            // 재료를 상류(서쪽)에 넣으면 동쪽 하류까지 흘러간다. 나선램프가 x 10~17 부근 상공을 다리로 지난다.
            AddSpot(root, "Spot_WaterChannel0", new Vector3(-11f, kPlazaY, -12f));
            AddSpot(root, "Spot_WaterChannel1", new Vector3(0f, kPlazaY, -12f));
            AddSpot(root, "Spot_WaterChannel2", new Vector3(12f, kPlazaY, -12f));
            AddSpot(root, "Spot_WaterChannel3", new Vector3(24f, kPlazaY, -12f));

            // ⚠ Spot_DigSite* 는 더 이상 만들지 않는다 — '유구 발굴터' 기믹을 뺐다(08/31).
            // 물길 하나로 충분하고, 발굴은 손이 많이 가는데 재미 대비 효과가 작았다.
            // 마커가 없어도 ExcavationNetwork는 어차피 GameLoopManager가 부착을 끊어 잠잔다.

            // ⚠ Spot_RosePad* 는 더 이상 만들지 않는다 — 'LED 장미 발판' 기믹을 뺐다.
            // 광장 위에 분홍 원판이 둥둥 떠 있는 그림이 보기 싫고 동선에도 도움이 안 됐다.
            // 마커가 없으면 LedRoseNetwork는 스스로 잠잔다. 광장(y=-4) → 데크(y=0)는 나선램프 하나로 간다.

            return root;
        }

        // ── LED 장미정원: 화단 '덩어리' 대신 한 송이씩 심는다 ─────────────────
        // 실제 DDP 장미정원은 가느다란 줄기 위 LED 장미 25,550송이가 촘촘히 박힌 밭이다.
        // 예전엔 화단 프롭 4덩이를 띄엄띄엄 놨더니 조잡하고 밭처럼 안 보였다.
        // 밟는 발판(Spot_RosePad*)은 여기와 별개로 LedRoseNetwork가 런타임에 만든다.
        // 간격 1.1 → 0.85, 동쪽 끝 2.5 → 10.0 (48 → ~100송이) — "꽃 더" 요청.
        // 동쪽 확장 한계는 나선램프 원호: 밭 동북 모서리 (10,-8.8)의 원호 중심(2,-4) 거리 9.3 < 내반경 11.2 → 램프 밑 아님.
        private const float kRoseSpacing = 0.85f;                       // 송이 간격(m)
        private const float kGardenX0 = -10.5f, kGardenX1 = 10.0f;      // 밭 범위(광장 북쪽, 램프 원호 안쪽까지)
        private const float kGardenZ0 = -8.8f,  kGardenZ1 = -4.8f;

        private static Transform BuildRoseGarden(GameObject root, Material curbMat, Material soilMat, Material roseMat)
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

            // 송이들만 담는 그룹 — NightBuildGlow가 이 그룹만 발광시킨다(흙·연석은 빛나면 안 되니 루트에 남긴다).
            var roses = new GameObject("~Roses");
            roses.transform.SetParent(root.transform, false);

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
                PlantRose(roses, rosePrefab, roseMat, p, planted++);
            }

            Random.state = prevRandom;
            Debug.Log($"[DDP] LED 장미정원: {planted}송이 식재{(rosePrefab == null ? " (모델 없음 — 그레이박스 폴백)" : "")}");
            return roses.transform;
        }

        // 장미 한 송이. 콜라이더는 붙이지 않는다 — 밭을 지나갈 때 걸리면 안 되고, ~100개나 되니 물리도 아깝다.
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

        // (kDigSites — 발굴터 후보 좌표 — 는 기믹 제거(08/31)와 함께 삭제. 필요하면 git 히스토리에.)

        // 곡면 나선램프: 광장 y=-4 → 데크 y=0 을 원호로 잇는다(DDP다운 나선 동선).
        //
        // 기하는 '시작점이 물길에 닿던' 옛 배치의 교정판:
        //   · 원호 중심을 데크 남단 라인(z=-4)에 두고 270°→360°(90° 스윕)로 감는다.
        //   · 입구(270°) = 원호의 정남쪽 = (2, -15.2~-20.8) — 배송존(-5,-17) 바로 옆,
        //     물길 남쪽 둔치(z=-14.7)에서 0.5m 이상 떨어져 시작점이 물길에 전혀 닿지 않는다.
        //   · 물길(z=-12)은 스윕 중반, 지면 위 ~1.5m 상공에서 다리로 건넌다 —
        //     방류로 물이 차도 발이 안 닿는다(재료 수급 → 램프 → 데크가 물을 전혀 안 밟는 동선).
        //   · 끝(360°) = (16, -4)에서 정확히 y=0, 북진 방향으로 데크 모서리에 접속 —
        //     원호가 z>-4로 넘어가지 않으므로 옛날처럼 끝자락이 잔디 밑에 파묻혀
        //     '박힌 하얀 패널'처럼 보이는 구간이 없다.
        //
        // 폭 5.6m — 위층으로 가는 길이 여기 하나뿐이라, 4인이 재료를 들고
        // 서로 비켜갈 수 있어야 병목이 안 생긴다(예전 3.4m는 둘이 마주치면 막혔다).
        //
        // 형태: 이어붙인 리본이 아니라 '낱장 패널이 한 단씩 떠 있는 나선 계단'.
        //   · 패널마다 수평(피치 0)으로 두고 다음 패널을 0.25m씩 올린다 —
        //     연속 경사면일 때 "평면적인 나선"으로 보이던 걸, 층이 지는 계단 실루엣으로.
        //   · 패널 사이 틈 ~0.27m + 단차 0.25m는 점프 없이 걸어서 자연히 넘는 크기
        //     (플레이어 캡슐 반지름·스텝 오프셋보다 작다).
        private static void BuildCurvedRamp(GameObject root, Material mat, Material glowMat)
        {
            const int kSteps = 16;
            const float kCx = 2f, kCz = -4f, kRadius = 14f;
            const float kStartDeg = 270f, kEndDeg = 360f;
            const float kWidth = 5.6f;
            const float kPanelLen = 1.1f;    // 낱장 길이(호 방향). 단 간격 ~1.37 → 틈 ~0.27
            const float kThick = 0.18f;      // 얇은 패널 두께 — '판'으로 읽히게

            float stepRise = (kDeckY - kPlazaY) / kSteps;   // 0.25m

            for (int i = 0; i < kSteps; i++)
            {
                float tMid = (i + 0.5f) / kSteps;
                float a = Mathf.Lerp(kStartDeg, kEndDeg, tMid) * Mathf.Deg2Rad;

                float top = kPlazaY + (i + 1) * stepRise;   // 마지막 단 상면 = 데크 높이(y=0)와 정확히 일치
                var pos = new Vector3(kCx + kRadius * Mathf.Cos(a), top - kThick * 0.5f, kCz + kRadius * Mathf.Sin(a));
                var tangent = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a));   // 진행 방향(반시계 스윕)
                var rot = Quaternion.LookRotation(tangent, Vector3.up);

                var panel = AddBox(root, $"Ramp{i}", pos, new Vector3(kWidth, kThick, kPanelLen), mat);
                panel.transform.localRotation = rot;
                panel.isStatic = true;

                // 난간 — 물길 위 다리 구간이 있으니 안팎 양쪽. 패널마다 끊겨 점선처럼 이어진다(낱장 느낌 유지).
                var outward = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                for (int s = -1; s <= 1; s += 2)
                {
                    var rail = AddBox(root, s < 0 ? $"RampRailIn{i}" : $"RampRailOut{i}",
                                      pos + outward * (s * kWidth * 0.5f) + Vector3.up * (kThick * 0.5f + 0.4f),
                                      new Vector3(0.16f, 0.8f, kPanelLen), mat);
                    rail.transform.localRotation = rot;
                    rail.isStatic = true;
                }

                // 난간 조명(야경) — 바깥 난간 위, 4단마다 하나. 물길 위 다리 구간이 밤에 점선으로 떠 보인다.
                if (glowMat != null && i % 4 == 1)
                {
                    var orb = AddBox(root, $"RampLamp{i}",
                                     pos + outward * (kWidth * 0.5f) + Vector3.up * (kThick * 0.5f + 0.9f),
                                     new Vector3(0.22f, 0.22f, 0.22f), glowMat);
                    orb.transform.localRotation = rot;
                    Object.DestroyImmediate(orb.GetComponent<Collider>());   // 장식 — 걸리적거리면 안 됨
                    orb.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    orb.isStatic = true;
                }
            }
        }

        // ───────────────────────────── ★ 야경 ─────────────────────────────
        // 컨셉: 실제 DDP는 밤이 본체 — 은색 곡면이 조명으로 빛나고, LED 장미 25,550송이가 밤에만 핀다.
        // 낮 그레이박스 위에 '조명 레이어'만 얹는 구조라, 이 함수와 컴포넌트 3개를 빼면 그대로 낮 맵이다.
        //
        // 라이트 예산(모바일): 진짜 포인트 라이트는 동선 요지 6개뿐.
        // 나머지 가로등은 에미션 헤드 + 바닥 '불빛 웅덩이'(가산 쿼드)로 그린다 — 라이트 0개짜리 눈속임.
        private static void BuildNightScape(GameObject root, Material lampGlow, Transform roseGroup)
        {
            var poleMat = EnsureMaterial("Mat_DdpLampPole", new Color(0.16f, 0.17f, 0.20f));
            var poolMat = EnsureLampPoolMaterial();
            var stripA  = EnsureEmissiveMaterial("Mat_DdpLedStripA", new Color(0.55f, 0.95f, 1.00f), new Color(0.25f, 0.85f, 1.00f) * 3.0f);
            var stripB  = EnsureEmissiveMaterial("Mat_DdpLedStripB", new Color(1.00f, 0.55f, 0.85f), new Color(1.00f, 0.30f, 0.75f) * 3.0f);

            // 수로 바닥도 은은히 — 이간수문 방류가 밤에 빛나는 물길로 보인다(실물 DDP 수변 조명 느낌).
            var water = EnsureMaterial("Mat_DdpWater", new Color(0.28f, 0.60f, 0.85f));
            if (water != null)
            {
                water.EnableKeyword("_EMISSION");
                water.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                water.SetColor("_EmissionColor", new Color(0.04f, 0.14f, 0.26f));
                EditorUtility.SetDirty(water);
            }
            // 폴백 장미(그레이박스 봉오리)도 에셋에서 발광 — 모델 있는 장미는 NightBuildGlow가 런타임에 켠다.
            var rose = EnsureMaterial("Mat_DdpRose", new Color(0.92f, 0.38f, 0.62f));
            if (rose != null)
            {
                rose.EnableKeyword("_EMISSION");
                rose.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                rose.SetColor("_EmissionColor", new Color(1.00f, 0.30f, 0.60f) * 1.8f);
                EditorUtility.SetDirty(rose);
            }

            // ── 가로등: 광장(y=-4) 5주 + 데크(y=0) 둘레 6주 ──
            // 장미밭·수로·나선램프 원호(중심 (2,-4), r 11.2~16.8 남동 사분원)·작업대를 피해 배치.
            var lamps = new GameObject("~StreetLamps");
            lamps.transform.SetParent(root.transform, false);
            var plazaLamps = new Vector3[]
            {
                new Vector3(-10.8f, kPlazaY, -23.5f),   // 남서 — 작업대 뒤
                new Vector3(  7.0f, kPlazaY, -23.5f),   // 남중
                new Vector3( 23.5f, kPlazaY, -23.5f),   // 남동
                new Vector3( 23.5f, kPlazaY,  -6.5f),   // 북동 — 수로 하류 옆
                new Vector3( 11.0f, kPlazaY,  -8.8f),   // 램프 원호 안마당(반경 10.2 < 11.2 — 원호 밑이 아님)
            };
            var deckLamps = new Vector3[]
            {
                new Vector3(-5.5f, kDeckY, -3f), new Vector3(5.5f, kDeckY, -3f), new Vector3(19f, kDeckY, -2.5f),
                new Vector3(-5.5f, kDeckY, 19f), new Vector3(6.5f, kDeckY, 19.5f), new Vector3(19f, kDeckY, 19f),
            };
            int n = 0;
            foreach (var p in plazaLamps) BuildStreetLamp(lamps, $"Lamp{n++}", p, poleMat, lampGlow, poolMat);
            foreach (var p in deckLamps)  BuildStreetLamp(lamps, $"Lamp{n++}", p, poleMat, lampGlow, poolMat);

            // ── 데크 옹벽 LED 미디어 스트립 — DDP 미디어 파사드의 상징. 옹벽 전면(z=-4.8)에 2줄 ──
            // EmissionCycler가 런타임에 색을 순환시킨다(시안→보라→마젠타→파랑 물결).
            var stripHigh = AddBox(root, "LedStripHigh", new Vector3(6.5f, kDeckY - 0.9f, -4.85f), new Vector3(26f, 0.14f, 0.06f), stripA);
            var stripLow  = AddBox(root, "LedStripLow",  new Vector3(6.5f, kDeckY - 2.2f, -4.85f), new Vector3(26f, 0.14f, 0.06f), stripB);
            stripHigh.isStatic = true; stripLow.isStatic = true;

            // ── 수로 볼라드 조명 — 물길 양 둔치를 따라 낮은 발광 말뚝(방류 연출이 밤에 더 잘 읽힌다) ──
            BuildBollards(lamps, poleMat, lampGlow);

            // ── 전구 줄(스트링 라이트) — 가로등 사이를 잇는 축제 조명. 광장 남쪽 2스팬 + 데크 남쪽 2스팬 ──
            var bulbMat = EnsureEmissiveMaterial("Mat_DdpBulb", new Color(1.00f, 0.93f, 0.75f), new Color(1.00f, 0.78f, 0.40f) * 2.6f);
            BuildStringLights(lamps, bulbMat, plazaLamps[0], plazaLamps[1]);
            BuildStringLights(lamps, bulbMat, plazaLamps[1], plazaLamps[2]);
            BuildStringLights(lamps, bulbMat, deckLamps[0], deckLamps[1]);
            BuildStringLights(lamps, bulbMat, deckLamps[1], deckLamps[2]);

            // ── 나무 3그루 — 광장 빈 구석(수로·램프 원호를 피해서). VARCO 모델(DDP_나무) 있으면 교체 ──
            var trunkMat = EnsureMaterial("Mat_DdpTrunk", new Color(0.36f, 0.27f, 0.20f));
            var leafMat  = EnsureMaterial("Mat_DdpLeaf",  new Color(0.20f, 0.32f, 0.22f));
            BuildTree(root, "Tree0", new Vector3(20f,    kPlazaY, -20f),   trunkMat, leafMat, 20f);
            BuildTree(root, "Tree1", new Vector3(24f,    kPlazaY, -15f),   trunkMat, leafMat, 140f);
            BuildTree(root, "Tree2", new Vector3(-11.3f, kPlazaY, -17.5f), trunkMat, leafMat, 260f);
            // 데크 북쪽 구석에도 2그루 — 잔디지붕이 휑하다는 피드백(그리드 z<14 밖이라 건축 안 막음)
            BuildTree(root, "Tree3", new Vector3(17.5f, kDeckY, 18f),   trunkMat, leafMat, 80f);
            BuildTree(root, "Tree4", new Vector3(-4.5f, kDeckY, 17.5f), trunkMat, leafMat, 200f);

            // ── 미디어폴 6기 — 광장 가장자리의 세로 LED 기둥(EmissionCycler로 색이 물결친다) ──
            // VARCO 모델(DDP_미디어폴) 있으면 몸체 교체, 발광 슬리브는 그대로 얹는다.
            var poleGlowMat = EnsureEmissiveMaterial("Mat_DdpMediaPole", new Color(0.55f, 0.95f, 1.00f), new Color(0.25f, 0.85f, 1.00f) * 3.0f);
            var mediaPoleRenderers = new List<Renderer>();
            // ⚠ 수로 침수 구간(z -9.8~-14.2, 반경 2.2)은 피한다 — (23.8,-10.5)에 세웠다가 물속에 잠겼었다(09/01)
            var polePositions = new[]
            {
                new Vector3(23.8f,  kPlazaY, -6.5f),
                new Vector3(23.8f,  kPlazaY, -18.5f), new Vector3(23.8f, kPlazaY, -22.5f),
                new Vector3(-11.2f, kPlazaY, -5.8f),  new Vector3(-11.2f, kPlazaY, -20.5f),
            };
            for (int pi = 0; pi < polePositions.Length; pi++)
                mediaPoleRenderers.Add(BuildMediaPole(lamps, $"MediaPole{pi}", polePositions[pi], poleMat, poleGlowMat));

            // ── 곡면 벤치 3개 — 광장 쉼터(장식). VARCO 모델(DDP_벤치) 있으면 교체 ──
            BuildBench(root, "Bench0", new Vector3(12f,   kPlazaY, -22.8f), 0f,   silverLike: poleMat);
            BuildBench(root, "Bench1", new Vector3(-2.5f, kPlazaY, -22.8f), 0f,   silverLike: poleMat);
            BuildBench(root, "Bench2", new Vector3(19f,   kPlazaY, -7f),    90f,  silverLike: poleMat);   // 수로 밖으로 이사(09/01 — 물길 위에 놓여 있었다)

            // ── 서치라이트 2기 — 하늘로 뻗어 천천히 도는 빔(개장 축제 느낌). 야경의 스카이라인 포인트 ──
            BuildSearchlight(root, "Searchlight_W", new Vector3(-5.5f, kDeckY, 18.5f), 40f);
            BuildSearchlight(root, "Searchlight_E", new Vector3(18.5f, kDeckY, 18.5f), 220f);

            // ── 데크 윤곽 조명 — 건물 외곽선을 따라 도는 따뜻한 라인(야경 건물 단골 연출) ──
            var edgeMat = EnsureEmissiveMaterial("Mat_DdpEdgeLight", new Color(1.00f, 0.92f, 0.72f), new Color(1.00f, 0.85f, 0.50f) * 2.2f);
            void EdgeLine(string n, Vector3 c, Vector3 s)
            {
                var e = AddBox(root, n, c, s, edgeMat);
                Object.DestroyImmediate(e.GetComponent<Collider>());
                e.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                e.isStatic = true;
            }
            EdgeLine("DeckEdge_S", new Vector3(6.5f,  kDeckY + 0.03f, -3.95f), new Vector3(26.2f, 0.06f, 0.12f));
            EdgeLine("DeckEdge_W", new Vector3(-6.45f, kDeckY + 0.03f, 8f),    new Vector3(0.12f, 0.06f, 24.1f));
            EdgeLine("DeckEdge_E", new Vector3(19.45f, kDeckY + 0.03f, 8f),    new Vector3(0.12f, 0.06f, 24.1f));
            EdgeLine("DeckEdge_N", new Vector3(6.5f,  kDeckY + 0.03f, 19.95f), new Vector3(26.2f, 0.06f, 0.12f));

            // ── 데크 난간 — 실물 DDP 잔디지붕의 가느다란 금속 가드레일(허공에 뜬 느낌 완화, 09/01) ──
            // 서·동·북 3변만 — 남쪽(광장 절벽)은 뛰어내리는 지름길이라 막지 않는다. 콜라이더 없음(경계벽이 담당).
            BuildDeckRail(root);

            // ── 장미밭 반딧불 — 분홍 불씨가 밭 위를 떠다닌다(트윙클과 세트) ──
            BuildFireflies(root);

            // ⚠ 꼬리 동 그레이박스(BuildTailWing)는 더 이상 안 깐다 — 통짜 본관 롤백(08/31) 후로는
            // 원본 모델이 꼬리까지 갖고 있어 중복인 데다, 초록 뚜껑 박스들이 '흉한 블록 줄'로 보였다.

            // ── 진짜 포인트 라이트(8) — 플레이어·재료가 실제로 밝아야 하는 요지 + 본관 투광 2 ──
            AddPointLight(lamps, "Light_Delivery",  new Vector3(-5f, kPlazaY + 2.2f, -17f),   new Color(1.00f, 0.85f, 0.60f), 9f, 1.2f);
            AddPointLight(lamps, "Light_Paint",     new Vector3(-9f, kPlazaY + 2.2f, -20f),   new Color(1.00f, 0.85f, 0.60f), 8f, 1.1f);
            AddPointLight(lamps, "Light_Hammer",    new Vector3(1f,  kPlazaY + 2.2f, -20f),   new Color(1.00f, 0.85f, 0.60f), 8f, 1.1f);
            AddPointLight(lamps, "Light_RampEntry", new Vector3(2f,  kPlazaY + 2.2f, -16.5f), new Color(1.00f, 0.88f, 0.66f), 9f, 1.0f);
            AddPointLight(lamps, "Light_RoseBed",   new Vector3(-4f, kPlazaY + 1.6f, -6.8f),  new Color(1.00f, 0.45f, 0.70f), 8f, 0.9f);
            AddPointLight(lamps, "Light_Deck",      new Vector3(7f,  kDeckY + 5f,   7f),      new Color(0.78f, 0.85f, 1.00f), 19f, 1.05f);   // 09/01 밝기 업
            // 본관 전면(z=4 입구 쪽) 투광 — 지어지는 건물이 실물처럼 라이트업된다
            AddPointLight(lamps, "Light_FacadeW",   new Vector3(4f,  kDeckY + 2.5f, 2.5f),    new Color(0.80f, 0.87f, 1.00f), 9f, 1.1f);
            AddPointLight(lamps, "Light_FacadeE",   new Vector3(11f, kDeckY + 2.5f, 2.5f),    new Color(0.80f, 0.87f, 1.00f), 9f, 1.1f);

            // ── 밤 환경 + 발광 컴포넌트 ──
            var night = root.AddComponent<MapNightAmbience>();
            night.NightSky = EnsureNightSkyMaterial();

            var cycler = root.AddComponent<EmissionCycler>();      // 미디어 파사드 색 순환(스트립 + 미디어폴)
            var cycleTargets = new List<Renderer> { stripHigh.GetComponent<Renderer>(), stripLow.GetComponent<Renderer>() };
            foreach (var mp in mediaPoleRenderers) if (mp != null) cycleTargets.Add(mp);
            cycler.Targets = cycleTargets.ToArray();
            cycler.Intensity = 3.4f;   // 야경 강화 — 블룸이 확실히 물게

            var blockGlow = root.AddComponent<NightBuildGlow>();   // 지은 블록(~GridVisuals) — 라이트업
            blockGlow.Tint = new Color(0.80f, 0.88f, 1.00f);
            blockGlow.Intensity = 1.6f;   // 0.9는 '켜진 티'가 안 났다 — 블룸 문턱(1.1)을 넘겨 또렷하게

            var roseGlow = root.AddComponent<NightBuildGlow>();    // LED 장미 — 분홍으로 쨍하게(밤의 주인공)
            roseGlow.WatchRootName = "";
            roseGlow.ExtraTargets = new[] { roseGroup };
            roseGlow.Tint = new Color(1.00f, 0.45f, 0.75f);
            roseGlow.Intensity = 2.4f;   // 야경 강화: 2.0 → 2.4
            roseGlow.TwinkleAmount = 0.65f;   // ★ 반짝반짝 — 송이마다 위상이 달라 밭 전체가 별밭처럼
            roseGlow.TwinkleSpeed = 2.4f;
            // glTFast 장미가 '에미션 없는 셰이더 변형'으로 구워져 안 빛나던 문제(09/01) —
            // URP Lit 에미션 인스턴스로 강제 교체해서 발광·반짝임을 보장한다.
            roseGlow.ForceLitEmissive = true;

            root.AddComponent<NightHorizonTint>();   // 언릿 원경 카드(~Horizon)를 밤색으로 — 비주얼 정리 툴이 깔아둔 것
        }

        // 가로등 1주: 기둥 + 팔 + 에미션 헤드 + 바닥 불빛 웅덩이. 콜라이더는 기둥에만(가늘어서 안 걸리적거림).
        // VARCO 모델(DDP_가로등_Fit)이 있으면 그레이박스 대신 세우고 발광 헤드·웅덩이만 얹는다.
        private static void BuildStreetLamp(GameObject parent, string name, Vector3 groundPos,
                                            Material poleMat, Material glowMat, Material poolMat)
        {
            var lamp = new GameObject(name);
            lamp.transform.SetParent(parent.transform, false);
            lamp.transform.localPosition = groundPos;

            float headX = 0.72f;   // 불빛 웅덩이를 헤드 밑에 맞추기 위한 x 오프셋
            if (TryPlaceProp(lamp, "DDP_가로등", Vector3.zero))
            {
                headX = 0f;
                SlimCollider(lamp, new Vector3(0.3f, 3.0f, 0.3f));   // 바운즈 통짜 콜라이더 → 기둥만(옆을 지나다닐 수 있게)

                // 발광 헤드: 고정 좌표에 정육면체를 얹었더니 모델 머리 옆에 '묻은 큐브'로 보였다(08/31 피드백).
                // → 모델 바운즈 꼭대기에 맞춘 가산 헤일로(교차 쿼드 2 + 수평 쿼드 1)로 교체 —
                //   부드러운 빛망울이라 위치가 반 칸 어긋나도 어색하지 않고, 어느 각도에서도 보인다.
                var rends = lamp.GetComponentsInChildren<Renderer>();
                var hb = rends[0].bounds;
                foreach (var r0 in rends) hb.Encapsulate(r0.bounds);
                var haloLocal = lamp.transform.InverseTransformPoint(new Vector3(hb.center.x, hb.max.y - 0.18f, hb.center.z));
                for (int q = 0; q < 3; q++)
                {
                    var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    quad.name = $"halo{q}";
                    quad.transform.SetParent(lamp.transform, false);
                    quad.transform.localPosition = haloLocal;
                    quad.transform.localRotation = q < 2
                        ? Quaternion.Euler(0f, q * 90f, 0f)          // 세로 교차 2장
                        : Quaternion.Euler(90f, 0f, 0f);             // 수평 1장(위에서 볼 때)
                    quad.transform.localScale = new Vector3(1.15f, 1.15f, 1f);
                    Object.DestroyImmediate(quad.GetComponent<Collider>());
                    var qr = quad.GetComponent<Renderer>();
                    qr.sharedMaterial = poolMat;   // 가로등 웅덩이와 같은 가산 원형 그라데이션
                    qr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    qr.receiveShadows = false;
                    quad.isStatic = true;
                }
            }
            else
            {
                var pole = AddBox(lamp, "pole", new Vector3(0f, 1.5f, 0f), new Vector3(0.14f, 3.0f, 0.14f), poleMat);
                pole.isStatic = true;
                var arm = AddBox(lamp, "arm", new Vector3(0.35f, 2.95f, 0f), new Vector3(0.85f, 0.10f, 0.10f), poleMat);
                Object.DestroyImmediate(arm.GetComponent<Collider>());
                arm.isStatic = true;
                var head = AddBox(lamp, "head", new Vector3(0.72f, 2.83f, 0f), new Vector3(0.34f, 0.16f, 0.26f), glowMat);
                Object.DestroyImmediate(head.GetComponent<Collider>());
                head.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                head.isStatic = true;
            }

            // 불빛 웅덩이 — 헤드 바로 밑 바닥에 가산 원형 그라데이션(진짜 라이트 아님)
            AddLightPool(lamp, new Vector3(headX, 0.03f, 0f), 5.5f, poolMat);

            // 빛 원뿔 — 헤드와 웅덩이를 잇는 가산 콘. "위·아래에만 빛이 있고 중간이 비었다"(09/01) 교정:
            // 위 좁고 아래 넓은 교차 시트 메시에, 위가 밝고 아래로 사라지는 그라데이션을 입힌다.
            float headY = 2.7f;
            {
                var rends2 = lamp.GetComponentsInChildren<Renderer>();
                if (rends2.Length > 0)
                {
                    var cb = rends2[0].bounds;
                    foreach (var r2 in rends2) cb.Encapsulate(r2.bounds);
                    headY = Mathf.Max(2.2f, cb.max.y - lamp.transform.position.y - 0.25f);
                }
                var cone = new GameObject("lightCone");
                cone.transform.SetParent(lamp.transform, false);
                cone.transform.localPosition = new Vector3(headX, 0.02f, 0f);
                cone.transform.localScale = new Vector3(3.4f, headY, 3.4f);
                cone.AddComponent<MeshFilter>().sharedMesh = EnsureLampConeMesh();
                var cr = cone.AddComponent<MeshRenderer>();
                cr.sharedMaterial = EnsureLampConeMaterial();
                cr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                cr.receiveShadows = false;
                cone.isStatic = true;
            }
        }

        // 가로등 빛 원뿔 메시(공유 1개): 위 좁고(반폭 0.15) 아래 넓은(반폭 0.5) 교차 시트 2장, 높이 1.
        // v=0(텍스처 밝은 끝)이 위. 메모리 메시는 프리팹 저장 시 사라지므로 에셋으로 굽는다(BldgMeshes와 같은 이유).
        private static Mesh EnsureLampConeMesh()
        {
            const string kPath = "Assets/Map/Horizon/LampCone.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(kPath);
            if (mesh != null) return mesh;

            mesh = new Mesh { name = "LampCone" };
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            void Sheet(Vector3 right)
            {
                int i = v.Count;
                v.Add(-right * 0.15f + Vector3.up); v.Add(right * 0.15f + Vector3.up);   // 위(좁음)
                v.Add(right * 0.5f);                v.Add(-right * 0.5f);                 // 아래(넓음)
                uv.Add(new Vector2(0f, 0f)); uv.Add(new Vector2(1f, 0f));   // v0 = 밝은 끝(위)
                uv.Add(new Vector2(1f, 1f)); uv.Add(new Vector2(0f, 1f));
                t.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            }
            Sheet(Vector3.right);
            Sheet(Vector3.forward);
            mesh.SetVertices(v); mesh.SetUVs(0, uv); mesh.SetTriangles(t, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            Directory.CreateDirectory("Assets/Map/Horizon");
            AssetDatabase.CreateAsset(mesh, kPath);
            return mesh;
        }

        // 빛 원뿔 머티리얼 — 서치라이트 빔과 같은 레시피(가산·양면·세로 그라데이션), 색만 따뜻하게.
        private static Material EnsureLampConeMaterial()
        {
            string path = $"{kMatDir}/Mat_DdpLampCone.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                var sh = Shader.Find("Universal Render Pipeline/Unlit");
                if (sh == null) return null;
                mat = new Material(sh);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetTexture("_BaseMap", EnsureBeamTexture());
            mat.SetColor("_BaseColor", new Color(1.00f, 0.82f, 0.50f, 1f) * 0.55f);   // 가산이라 RGB 크기가 세기 — 은은하게
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 2f);
            mat.SetFloat("_AlphaClip", 0f);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            mat.SetFloat("_ZWrite", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // 가산 불빛 웅덩이 쿼드 한 장(위를 보게 눕힘)
        private static void AddLightPool(GameObject parent, Vector3 localPos, float size, Material poolMat)
        {
            var pool = GameObject.CreatePrimitive(PrimitiveType.Quad);
            pool.name = "pool";
            pool.transform.SetParent(parent.transform, false);
            pool.transform.localPosition = localPos;
            pool.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            pool.transform.localScale = new Vector3(size, size, 1f);
            Object.DestroyImmediate(pool.GetComponent<Collider>());
            var pr = pool.GetComponent<Renderer>();
            pr.sharedMaterial = poolMat;
            pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            pr.receiveShadows = false;
            pool.isStatic = true;
        }

        // 수로 볼라드: 양 둔치 바깥 라인에 낮은 발광 말뚝. 콜라이더 없음(재료 던지기·물살 동선에 안 걸리게).
        // 남쪽은 램프 입구(x 0~5)를, 북쪽은 장미밭(z ≥ -8.8)을 피한다.
        private static void BuildBollards(GameObject parent, Material poleMat, Material glowMat)
        {
            var south = new float[] { -9f, 15f, 23f };          // z = -15.0
            var north = new float[] { -9f, -1f, 7f, 15f };      // z = -9.3 (장미밭 연석 z=-8.95와 안 붙게)
            int i = 0;
            foreach (var x in south) BuildBollard(parent, $"Bollard{i++}", new Vector3(x, kPlazaY, -15.0f), poleMat, glowMat);
            foreach (var x in north) BuildBollard(parent, $"Bollard{i++}", new Vector3(x, kPlazaY, -9.3f), poleMat, glowMat);
        }

        private static void BuildBollard(GameObject parent, string name, Vector3 groundPos, Material poleMat, Material glowMat)
        {
            var b = new GameObject(name);
            b.transform.SetParent(parent.transform, false);
            b.transform.localPosition = groundPos;
            var pole = AddBox(b, "pole", new Vector3(0f, 0.25f, 0f), new Vector3(0.13f, 0.5f, 0.13f), poleMat);
            Object.DestroyImmediate(pole.GetComponent<Collider>());
            pole.isStatic = true;
            var cap = AddBox(b, "cap", new Vector3(0f, 0.56f, 0f), new Vector3(0.17f, 0.14f, 0.17f), glowMat);
            Object.DestroyImmediate(cap.GetComponent<Collider>());
            cap.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            cap.isStatic = true;
        }

        // 전구 줄: 두 가로등 헤드 높이(~2.85m) 사이를 살짝 늘어지는 곡선으로 잇는 작은 발광 전구들.
        private static void BuildStringLights(GameObject parent, Material bulbMat, Vector3 lampA, Vector3 lampB)
        {
            var a = lampA + Vector3.up * 2.85f;
            var b = lampB + Vector3.up * 2.85f;
            int count = Mathf.Max(6, Mathf.RoundToInt(Vector3.Distance(a, b) / 1.3f));
            var line = new GameObject($"String_{lampA.x:0}_{lampB.x:0}");
            line.transform.SetParent(parent.transform, false);
            for (int i = 1; i < count; i++)   // 양끝(헤드 자리)은 비운다
            {
                float t = i / (float)count;
                var p = Vector3.Lerp(a, b, t) + Vector3.down * (0.6f * Mathf.Sin(t * Mathf.PI));   // 늘어짐
                var bulb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                bulb.name = $"bulb{i}";
                bulb.transform.SetParent(line.transform, false);
                bulb.transform.localPosition = p;
                bulb.transform.localScale = Vector3.one * 0.13f;
                Object.DestroyImmediate(bulb.GetComponent<Collider>());
                var r = bulb.GetComponent<Renderer>();
                r.sharedMaterial = bulbMat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                bulb.isStatic = true;
            }
        }

        // 광장 나무: VARCO 모델(DDP_나무) 우선, 없으면 원기둥 줄기 + 구 2개 수관 그레이박스.
        private static void BuildTree(GameObject root, string name, Vector3 groundPos, Material trunkMat, Material leafMat, float yaw)
        {
            var tree = new GameObject(name);
            tree.transform.SetParent(root.transform, false);
            tree.transform.localPosition = groundPos;
            if (TryPlaceProp(tree, "DDP_나무", Vector3.zero, yaw))
            {
                SlimCollider(tree, new Vector3(0.5f, 2.2f, 0.5f));   // 수관 밑을 지나다닐 수 있게 줄기만 막는다
                return;
            }

            var trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            trunk.name = "trunk";
            trunk.transform.SetParent(tree.transform, false);
            trunk.transform.localPosition = new Vector3(0f, 0.8f, 0f);
            trunk.transform.localScale = new Vector3(0.22f, 0.8f, 0.22f);   // 원기둥 높이 = 스케일y × 2
            if (trunkMat != null) trunk.GetComponent<Renderer>().sharedMaterial = trunkMat;
            trunk.isStatic = true;
            foreach (var (y, s) in new[] { (2.0f, 1.9f), (2.9f, 1.4f) })
            {
                var canopy = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                canopy.name = "canopy";
                canopy.transform.SetParent(tree.transform, false);
                canopy.transform.localPosition = new Vector3(0f, y, 0f);
                canopy.transform.localScale = Vector3.one * s;
                Object.DestroyImmediate(canopy.GetComponent<Collider>());
                if (leafMat != null) canopy.GetComponent<Renderer>().sharedMaterial = leafMat;
                canopy.isStatic = true;
            }
        }

        // 꼬리 동: 실물 DDP의 북서쪽으로 길게 흐르는 꼬리(아트홀 너머) — 데크 북쪽 구석의 낮은 배경 매스.
        // 서쪽으로 갈수록 낮아지며 잔디지붕이 덮인다. 정답 셀(z 2~12)·건축 그리드(z<14)와 겹치지 않는다(z ≥ 14.4).
        private static void BuildTailWing(GameObject root)
        {
            var tailMat  = EnsureEmissiveMaterial("Mat_DdpTail", new Color(0.82f, 0.84f, 0.88f), new Color(0.28f, 0.34f, 0.48f) * 0.5f);
            var grassMat = EnsureMaterial("Mat_DdpDeck", new Color(0.44f, 0.58f, 0.36f));

            var wing = new GameObject("~TailWing");
            wing.transform.SetParent(root.transform, false);
            var segs = new (float cx, float cz, float w, float d, float h)[]
            {
                (13f,   16.3f, 6.5f, 3.8f, 3.2f),
                (8f,    17.0f, 6.0f, 3.6f, 2.6f),
                (3.5f,  17.7f, 5.5f, 3.4f, 2.1f),
                (-0.5f, 18.3f, 5.0f, 3.2f, 1.6f),
                (-4.2f, 18.6f, 4.2f, 2.6f, 1.2f),
            };
            int i = 0;
            foreach (var s in segs)
            {
                AddBox(wing, $"Tail{i}", new Vector3(s.cx, kDeckY + s.h * 0.5f, s.cz), new Vector3(s.w, s.h, s.d), tailMat).isStatic = true;
                var cap = AddBox(wing, $"TailGrass{i}", new Vector3(s.cx, kDeckY + s.h + 0.07f, s.cz), new Vector3(s.w * 0.85f, 0.14f, s.d * 0.85f), grassMat);
                Object.DestroyImmediate(cap.GetComponent<Collider>());
                cap.isStatic = true;
                i++;
            }
        }

        // 장미밭 위를 느리게 떠오르는 분홍 반딧불 파티클 — 에디터에서 구성해 프리팹에 그대로 저장된다.
        private static void BuildFireflies(GameObject root)
        {
            var go = new GameObject("~RoseFireflies");
            go.transform.SetParent(root.transform, false);
            float cx = (kGardenX0 + kGardenX1) * 0.5f, cz = (kGardenZ0 + kGardenZ1) * 0.5f;
            go.transform.localPosition = new Vector3(cx, kPlazaY + 0.5f, cz);

            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(3.5f, 6.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.16f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1f, 0.55f, 0.8f, 0.9f), new Color(1f, 0.85f, 0.95f, 0.8f));
            main.gravityModifier = -0.008f;   // 아주 살짝 떠오른다
            main.maxParticles = 80;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(kGardenX1 - kGardenX0 - 1f, 0.6f, kGardenZ1 - kGardenZ0 - 0.6f);

            var em = ps.emission;
            em.rateOverTime = 11f;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.6f, 0.85f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.9f, 0.25f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = EnsureFireflyMaterial();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        // 반딧불 머티리얼 — 가로등 웅덩이 텍스처(원형 그라데이션)를 가산으로 재활용. 에셋이라 프리팹에 살아남는다.
        private static Material EnsureFireflyMaterial()
        {
            string path = $"{kMatDir}/Mat_DdpFirefly.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
                if (sh == null) return null;
                mat = new Material(sh);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetTexture("_BaseMap", EnsureGlowTexture());
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 2f);
            mat.SetFloat("_AlphaClip", 0f);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);   // 가산 섞임 — 불씨 반짝임
            mat.SetFloat("_ZWrite", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // 미디어폴 1기: 어두운 기둥 + 세로 발광 슬리브. 발광 슬리브 렌더러를 돌려줘 EmissionCycler에 물린다.
        // VARCO 모델(DDP_미디어폴_Fit) 있으면 몸체를 교체하고 슬리브만 얹는다.
        private static Renderer BuildMediaPole(GameObject parent, string name, Vector3 groundPos,
                                               Material poleMat, Material glowMat)
        {
            var pole = new GameObject(name);
            pole.transform.SetParent(parent.transform, false);
            pole.transform.localPosition = groundPos;

            if (TryPlaceProp(pole, "DDP_미디어폴", Vector3.zero))
                SlimCollider(pole, new Vector3(0.4f, 4.5f, 0.4f));
            else
            {
                var body = AddBox(pole, "body", new Vector3(0f, 2.4f, 0f), new Vector3(0.38f, 4.8f, 0.38f), poleMat);
                body.isStatic = true;   // 몸체 콜라이더 유지(기둥이니 부딪히는 게 자연스럽다)
            }

            var sleeve = AddBox(pole, "glow", new Vector3(0f, 2.5f, 0f), new Vector3(0.22f, 4.2f, 0.22f), glowMat);
            Object.DestroyImmediate(sleeve.GetComponent<Collider>());
            var r = sleeve.GetComponent<Renderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            sleeve.isStatic = false;   // EmissionCycler가 인스턴스 머티리얼로 색을 돌린다
            return r;
        }

        // 곡면 벤치(장식): 낮은 좌판 + 등받이. VARCO 모델(DDP_벤치_Fit) 있으면 교체.
        private static void BuildBench(GameObject root, string name, Vector3 groundPos, float yaw, Material silverLike)
        {
            var bench = new GameObject(name);
            bench.transform.SetParent(root.transform, false);
            bench.transform.localPosition = groundPos;
            bench.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);

            if (TryPlaceProp(bench, "DDP_벤치", Vector3.zero))
            {
                SlimCollider(bench, new Vector3(2.2f, 0.9f, 0.9f));
                return;
            }
            var seat = AddBox(bench, "seat", new Vector3(0f, 0.42f, 0f), new Vector3(2.2f, 0.16f, 0.72f), silverLike);
            seat.isStatic = true;
            var back = AddBox(bench, "back", new Vector3(0f, 0.78f, -0.32f), new Vector3(2.2f, 0.6f, 0.1f), silverLike);
            Object.DestroyImmediate(back.GetComponent<Collider>());
            back.isStatic = true;
            foreach (var sx in new[] { -0.9f, 0.9f })
            {
                var leg = AddBox(bench, "leg", new Vector3(sx, 0.18f, 0f), new Vector3(0.14f, 0.36f, 0.6f), silverLike);
                Object.DestroyImmediate(leg.GetComponent<Collider>());
                leg.isStatic = true;
            }
        }

        // 서치라이트: 짧은 받침 + 하늘로 뻗는 가산 빔(교차 쿼드 2장) + SlowSpin.
        // 진짜 라이트 아님 — 빔은 위로 갈수록 사라지는 그라데이션 쿼드라 어느 각도에서도 싸게 보인다.
        private static void BuildSearchlight(GameObject root, string name, Vector3 groundPos, float startYaw)
        {
            var baseGo = new GameObject(name);
            baseGo.transform.SetParent(root.transform, false);
            baseGo.transform.localPosition = groundPos;
            baseGo.transform.localRotation = Quaternion.Euler(0f, startYaw, 0f);   // 두 기가 다른 위상으로 돌게

            var poleMat = EnsureMaterial("Mat_DdpLampPole", new Color(0.16f, 0.17f, 0.20f));
            var glowMat = EnsureEmissiveMaterial("Mat_DdpLampGlow", new Color(1.00f, 0.90f, 0.70f), new Color(1.00f, 0.82f, 0.45f) * 3.0f);

            // 받침: '꺼먼 정육면체'(09/01 피드백) 대신 작은 프로젝터 — 원기둥 받침 + 기울인 몸통 + 발광 렌즈
            var pedestal = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pedestal.name = "pedestal";
            pedestal.transform.SetParent(baseGo.transform, false);
            pedestal.transform.localPosition = new Vector3(0f, 0.22f, 0f);
            pedestal.transform.localScale = new Vector3(0.5f, 0.22f, 0.5f);
            if (poleMat != null) pedestal.GetComponent<Renderer>().sharedMaterial = poleMat;
            pedestal.isStatic = true;

            var spin = baseGo.AddComponent<SlowSpin>();
            spin.DegreesPerSecond = new Vector3(0f, 8f, 0f);

            // 빔 피벗 — 살짝 기울여(18°) 하늘을 쓴다. 몸통·렌즈·빔이 같이 돈다.
            var pivot = new GameObject("beamPivot");
            pivot.transform.SetParent(baseGo.transform, false);
            pivot.transform.localPosition = new Vector3(0f, 0.62f, 0f);
            pivot.transform.localRotation = Quaternion.Euler(18f, 0f, 0f);

            var housing = AddBox(pivot.gameObject, "housing", new Vector3(0f, 0.12f, 0f), new Vector3(0.42f, 0.35f, 0.42f), poleMat);
            Object.DestroyImmediate(housing.GetComponent<Collider>());
            housing.isStatic = true;
            var lens = AddBox(pivot.gameObject, "lens", new Vector3(0f, 0.31f, 0f), new Vector3(0.34f, 0.05f, 0.34f), glowMat);
            Object.DestroyImmediate(lens.GetComponent<Collider>());
            lens.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lens.isStatic = true;

            var beamMat = EnsureBeamMaterial();
            for (int i = 0; i < 2; i++)
            {
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = $"beam{i}";
                quad.transform.SetParent(pivot.transform, false);
                quad.transform.localPosition = new Vector3(0f, 17.5f, 0f);
                // 쿼드는 기본이 '세로 판'(XY 평면) — 그대로 두고 yaw로만 90° 교차시킨다.
                // (예전엔 x축으로 -90° 더 돌려서 빔이 하늘 17m에 '수평으로 누워' 안 보였다 — 09/01 수정)
                quad.transform.localRotation = Quaternion.Euler(0f, i * 90f, 0f);
                quad.transform.localScale = new Vector3(2.6f, 35f, 1f);
                Object.DestroyImmediate(quad.GetComponent<Collider>());
                var qr = quad.GetComponent<Renderer>();
                qr.sharedMaterial = beamMat;
                qr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                qr.receiveShadows = false;
            }
        }

        // 데크 난간: 실물 DDP 잔디지붕의 가느다란 금속 가드레일 — 기둥(2.2m 간격) + 가로대 2단.
        // 서·동·북 3변. 콜라이더 없음(막기는 투명 경계벽 담당, 이건 순수 비주얼).
        private static void BuildDeckRail(GameObject root)
        {
            var mat = EnsureMaterial("Mat_DdpSilver", new Color(0.78f, 0.80f, 0.83f));   // 은색 패널과 같은 에셋
            var group = new GameObject("~DeckRail");
            group.transform.SetParent(root.transform, false);

            void RailRun(Vector3 from, Vector3 to)
            {
                var dir = (to - from).normalized;
                float len = Vector3.Distance(from, to);
                var rot = Quaternion.LookRotation(dir, Vector3.up);

                // 가로대 2단(통짜 얇은 봉)
                foreach (var h in new[] { 0.5f, 0.95f })
                {
                    var rail = AddBox(group, "rail", (from + to) * 0.5f + Vector3.up * h, new Vector3(0.05f, 0.05f, len), mat);
                    rail.transform.localRotation = rot;
                    Object.DestroyImmediate(rail.GetComponent<Collider>());
                    rail.isStatic = true;
                }
                // 기둥
                int posts = Mathf.Max(2, Mathf.RoundToInt(len / 2.2f) + 1);
                for (int i = 0; i < posts; i++)
                {
                    var p = Vector3.Lerp(from, to, i / (float)(posts - 1));
                    var post = AddBox(group, "post", p + Vector3.up * 0.5f, new Vector3(0.06f, 1.0f, 0.06f), mat);
                    Object.DestroyImmediate(post.GetComponent<Collider>());
                    post.isStatic = true;
                }
            }

            RailRun(new Vector3(-6.4f, kDeckY, -3.6f), new Vector3(-6.4f, kDeckY, 19.8f));   // 서
            RailRun(new Vector3(19.4f, kDeckY, -3.6f), new Vector3(19.4f, kDeckY, 19.8f));   // 동
            RailRun(new Vector3(-6.4f, kDeckY, 19.8f), new Vector3(19.4f, kDeckY, 19.8f));   // 북
        }

        // 서치라이트 빔 머티리얼 — 가산 블렌드, 아래(빔 뿌리)가 밝고 위로 사라지는 세로 그라데이션.
        private static Material EnsureBeamMaterial()
        {
            string path = $"{kMatDir}/Mat_DdpBeam.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                var sh = Shader.Find("Universal Render Pipeline/Unlit");
                if (sh == null) return null;
                mat = new Material(sh);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetTexture("_BaseMap", EnsureBeamTexture());
            mat.SetColor("_BaseColor", new Color(0.55f, 0.75f, 1.00f, 1f));
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 2f);   // Additive — URP 검증이 리셋하지 않게 명시(가로등 웅덩이와 동일 교훈)
            mat.SetFloat("_AlphaClip", 0f);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            mat.SetFloat("_ZWrite", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            // 양면 — 빔은 어느 쪽에서 봐도 보여야 한다
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // 세로 그라데이션(아래 밝음 → 위 소멸) + 가장자리 페이드. 수학 산출물이라 매번 다시 굽는다.
        private static Texture2D EnsureBeamTexture()
        {
            const string kDirTex = "Assets/Map/Horizon";
            string path = $"{kDirTex}/BeamGlow.png";

            const int W = 64, H = 256;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            var px = new Color32[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float v = Mathf.Pow(1f - y / (float)(H - 1), 1.7f);                       // 위로 갈수록 소멸
                    float edge = 1f - Mathf.Abs(x - (W - 1) * 0.5f) / ((W - 1) * 0.5f);       // 가장자리 페이드
                    byte b = (byte)(v * Mathf.Pow(edge, 0.8f) * 235f);
                    px[y * W + x] = new Color32(b, b, b, b);
                }
            tex.SetPixels32(px);
            tex.Apply();
            Directory.CreateDirectory(kDirTex);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp != null && (imp.wrapMode != TextureWrapMode.Clamp || !imp.alphaIsTransparency))
            {
                imp.wrapMode = TextureWrapMode.Clamp;
                imp.alphaIsTransparency = true;
                imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ── 투명 경계벽: 광장·데크 바깥 둘레를 보이지 않는 콜라이더로 막는다(맵 이탈 방지) ──
        // 렌더러 없음 — 밤이라 티도 안 난다. 램프·수로·작업대는 전부 안쪽이라 동선 영향 없음.
        private static void BuildBoundaryWalls(GameObject root)
        {
            var group = new GameObject("~BoundaryWalls");
            group.transform.SetParent(root.transform, false);

            void Wall(string name, Vector3 center, Vector3 size)
            {
                var go = new GameObject(name);
                go.transform.SetParent(group.transform, false);
                go.transform.localPosition = center;
                go.AddComponent<BoxCollider>().size = size;
            }

            const float h = 9f;   // 점프·낙하로 못 넘는 높이
            float wy = kPlazaY + h * 0.5f;           // 광장 벽 중심
            float dy = kDeckY + h * 0.5f;            // 데크 벽 중심

            // 광장 둘레(서·동·남) — x∈[-11.5,24.5], z∈[-24.5,-3.5]
            Wall("Plaza_W", new Vector3(-11.8f, wy, -14f),   new Vector3(0.6f, h, 22f));
            Wall("Plaza_E", new Vector3(24.8f,  wy, -14f),   new Vector3(0.6f, h, 22f));
            Wall("Plaza_S", new Vector3(6.5f,   wy, -24.8f), new Vector3(37.5f, h, 0.6f));
            // 광장 북쪽 중 데크가 없는 양끝 구간(데크는 x -6.5~19.5만 차지)
            Wall("Plaza_NW", new Vector3(-9f,   wy, -3.4f),  new Vector3(5.8f, h, 0.6f));
            Wall("Plaza_NE", new Vector3(22f,   wy, -3.4f),  new Vector3(5.8f, h, 0.6f));

            // 데크 둘레(서·동·북) — x∈[-6.5,19.5], z∈[-4,20]. 남쪽은 광장으로 이어지는 절벽(옹벽)이라 그대로.
            Wall("Deck_W", new Vector3(-6.8f, dy, 8f),    new Vector3(0.6f, h, 24.6f));
            Wall("Deck_E", new Vector3(19.8f, dy, 8f),    new Vector3(0.6f, h, 24.6f));
            Wall("Deck_N", new Vector3(6.5f,  dy, 20.3f), new Vector3(27.2f, h, 0.6f));
        }

        // 프롭 아래의 모든 콜라이더(TryPlaceProp이 붙인 바운즈 통짜 포함)를 지우고 슬림 박스 하나로 교체.
        private static void SlimCollider(GameObject go, Vector3 size)
        {
            foreach (var c in go.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            var bc = go.AddComponent<BoxCollider>();
            bc.center = new Vector3(0f, size.y * 0.5f, 0f);
            bc.size = size;
        }

        private static void AddPointLight(GameObject parent, string name, Vector3 pos, Color color, float range, float intensity)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = pos;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = color;
            l.range = range;
            l.intensity = intensity;
            l.shadows = LightShadows.None;   // 모바일 — 포인트 그림자는 사치
        }

        private static Material EnsureEmissiveMaterial(string name, Color baseColor, Color emission)
        {
            var mat = EnsureMaterial(name, baseColor);
            if (mat == null) return null;
            mat.EnableKeyword("_EMISSION");   // 에셋에서 켜 둬야 빌드에 _EMISSION 변형이 실린다(NightBuildGlow의 런타임 켜기도 이 덕에 산다)
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            mat.SetColor("_EmissionColor", emission);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // 가산 블렌드 언릿 원형 그라데이션 — 라이트 없이 '불빛 웅덩이'만 그린다.
        private static Material EnsureLampPoolMaterial()
        {
            string path = $"{kMatDir}/Mat_DdpLampPool.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                var sh = Shader.Find("Universal Render Pipeline/Unlit");
                if (sh == null) { Debug.LogWarning("[DDP] URP Unlit 셰이더를 못 찾음"); return null; }
                mat = new Material(sh);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetTexture("_BaseMap", EnsureGlowTexture());
            mat.SetColor("_BaseColor", new Color(1.00f, 0.80f, 0.48f, 1f));   // 가산이라 알파 대신 RGB 밝기가 세기
            mat.SetFloat("_Surface", 1f);   // Transparent
            // ★ _Blend=Additive(2)를 반드시 같이 박는다 — URP 머티리얼 검증이 _Surface/_Blend 값으로
            //   Src/DstBlend를 '다시 계산'하는데, _Blend를 Alpha(0)로 두면 아래 One/One이 SrcAlpha/OneMinus로
            //   리셋된다. 그 상태 + 알파 255 통짜 텍스처 = 가로등 밑 '불투명 검은 사각형' 버그의 정체.
            mat.SetFloat("_Blend", 2f);
            mat.SetFloat("_AlphaClip", 0f);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);   // 가산
            mat.SetFloat("_ZWrite", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // 중심이 밝고 가장자리로 부드럽게 사라지는 원형 그라데이션(128²).
        // 수학 산출물이라(아트 교체 대상 아님) 매번 다시 굽는다 — 생성 로직을 고치면 기존 파일도 즉시 갱신되게.
        // 알파에도 같은 폴오프를 넣는다: 머티리얼이 어떤 이유로든 알파 블렌드로 떨어져도 사각형이 안 보인다.
        private static Texture2D EnsureGlowTexture()
        {
            const string kDirTex = "Assets/Map/Horizon";
            string path = $"{kDirTex}/LampGlow.png";

            const int N = 128;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), new Vector2(N / 2f, N / 2f)) / (N / 2f);
                    float v = Mathf.Pow(Mathf.Clamp01(1f - d), 2.2f);   // 가장자리 완전 소멸(사각 티 방지)
                    byte b = (byte)(v * 255f);
                    px[y * N + x] = new Color32(b, b, b, b);
                }
            tex.SetPixels32(px);
            tex.Apply();
            Directory.CreateDirectory(kDirTex);
            var bytes = tex.EncodeToPNG();
            File.WriteAllBytes(path, bytes);
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);

            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp != null && (imp.wrapMode != TextureWrapMode.Clamp || !imp.alphaIsTransparency))
            {
                imp.wrapMode = TextureWrapMode.Clamp;   // Repeat면 UV 경계에서 반대편 픽셀이 배어 나온다
                imp.alphaIsTransparency = true;
                imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private const string kNightSkyPath = kMatDir + "/Sky_SeoulNight.mat";

        // 밤하늘(FastSky 기반) — 이미 있으면 그대로 둔다: 별·구름 수치는 에셋에서 튜닝(재실행이 안 덮음).
        private static Material EnsureNightSkyMaterial()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(kNightSkyPath);
            if (mat != null) return mat;

            var src = AssetDatabase.LoadAssetAtPath<Material>("Assets/Map/Materials/Sky_SeoulStylised.mat");
            if (src == null) src = AssetDatabase.LoadAssetAtPath<Material>("Assets/ThirdParty/FastSky/Materials/StylisedSky.mat");
            if (src == null)
            {
                Debug.LogWarning("[DDP] FastSky 머티리얼이 없음 — URP Procedural 밤하늘로 대체");
                mat = new Material(Shader.Find("Skybox/Procedural"));
                mat.SetFloat("_AtmosphereThickness", 0.35f);
                mat.SetColor("_SkyTint", new Color(0.10f, 0.14f, 0.30f));
                mat.SetColor("_GroundColor", new Color(0.06f, 0.08f, 0.14f));
                mat.SetFloat("_Exposure", 0.6f);
            }
            else
            {
                mat = new Material(src);
                // 달빛(MapNightAmbience.MoonEuler)이 고도 14°라 FastSky가 저녁~밤 구간으로 판정한다 —
                // 거기에 낮/저녁 색 자체를 남색으로 눌러 어느 각도든 밤처럼 보이게 이중 안전장치.
                SetSkyIf(mat, "_DayBrightness", 0.35f);
                SetSkyColorIf(mat, "_DayColour", new Color(0.07f, 0.10f, 0.20f));
                SetSkyColorIf(mat, "_EveningColour", new Color(0.10f, 0.09f, 0.18f));
                SetSkyIf(mat, "_EveningBrightness", 0.5f);
                SetSkyIf(mat, "_EveningScatterStrength", 0.5f);
                SetSkyIf(mat, "_Saturation", 0.55f);
                SetSkyIf(mat, "_SunSize", 0.08f);   // 태양 대신 조그만 달
                SetSkyColorIf(mat, "_SunColor", new Color(0.92f, 0.95f, 1.00f));
                SetSkyIf(mat, "_StarBrightness", 1.8f);
                SetSkyIf(mat, "_StarThreshold", 0.4f);   // 별이 일찍 뜨게
                SetSkyColorIf(mat, "_CloudColour", new Color(0.20f, 0.22f, 0.34f));
                SetSkyIf(mat, "_CloudBrightness", 0.3f);
                SetSkyIf(mat, "_CloudThickness", 0.3f);
            }
            AssetDatabase.CreateAsset(mat, kNightSkyPath);
            return mat;
        }

        private static void SetSkyIf(Material m, string prop, float v) { if (m.HasProperty(prop)) m.SetFloat(prop, v); }
        private static void SetSkyColorIf(Material m, string prop, Color c) { if (m.HasProperty(prop)) m.SetColor(prop, c); }

        // 에디터 씬의 낮 라이팅을 잠깐 밤(MapNightAmbience 기본값)으로 바꿔 찍고 원복한다.
        // 값을 컴포넌트 기본치에서 읽어 와 런타임 야경과 썸네일이 어긋나지 않는다.
        private static Sprite CaptureNightThumbnail(GameObject prefab)
        {
            var tmp = new GameObject("~NightDefaults");
            var na = tmp.AddComponent<MapNightAmbience>();

            var sky = RenderSettings.skybox;
            bool fogOn = RenderSettings.fog; var fogMode = RenderSettings.fogMode;
            var fogColor = RenderSettings.fogColor;
            float fogStart = RenderSettings.fogStartDistance, fogEnd = RenderSettings.fogEndDistance;
            var ambMode = RenderSettings.ambientMode;
            var ambSky = RenderSettings.ambientSkyColor; var ambEq = RenderSettings.ambientEquatorColor;
            var ambGround = RenderSettings.ambientGroundColor;
            var sun = RenderSettings.sun;
            Color sunColor = default; float sunIntensity = 0f; Quaternion sunRot = default;
            if (sun != null) { sunColor = sun.color; sunIntensity = sun.intensity; sunRot = sun.transform.rotation; }

            try
            {
                RenderSettings.skybox = EnsureNightSkyMaterial();
                RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear;
                RenderSettings.fogColor = na.FogColor;
                RenderSettings.fogStartDistance = na.FogStart; RenderSettings.fogEndDistance = na.FogEnd;
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = na.AmbientSky;
                RenderSettings.ambientEquatorColor = na.AmbientEquator;
                RenderSettings.ambientGroundColor = na.AmbientGround;
                if (sun != null)
                {
                    sun.color = na.MoonColor; sun.intensity = na.MoonIntensity;
                    sun.transform.rotation = Quaternion.Euler(na.MoonEuler);
                }
                return MapThumbnailUtil.Capture(prefab, kThumbPath);
            }
            finally
            {
                RenderSettings.skybox = sky;
                RenderSettings.fog = fogOn; RenderSettings.fogMode = fogMode;
                RenderSettings.fogColor = fogColor;
                RenderSettings.fogStartDistance = fogStart; RenderSettings.fogEndDistance = fogEnd;
                RenderSettings.ambientMode = ambMode;
                RenderSettings.ambientSkyColor = ambSky; RenderSettings.ambientEquatorColor = ambEq;
                RenderSettings.ambientGroundColor = ambGround;
                if (sun != null) { sun.color = sunColor; sun.intensity = sunIntensity; sun.transform.rotation = sunRot; }
                Object.DestroyImmediate(tmp);
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
