using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace GridSystem.EditorTools
{
    /// <summary>
    /// ★ 남산타워 실전 맵 원클릭 생성 — 기획서(08/06) 그대로.
    /// · 파츠 MaterialDef 9종(id 12~20) + 색큐브 폴백 프리팹 → 전역 MaterialCatalog에 등록
    ///   (VARCO 모델이 나오면 각 def의 Prefab만 교체하면 됨)
    /// · 높이 23 남산타워 정답(하부기둥×4, 나머지×1 — 전망대 y11~13 = 엘베 개통 구간)
    /// · Frame1 배치도 그레이박스 배경: 나무 데크(단차) + 계단 + 자물쇠벽·하트 + 팔각정 +
    ///   케이블카존 + 데크 밑 엘베 로비 + 마커 8종
    /// · 실전 수치 NamsanGimmickConfig + 맵 카드 + 카탈로그 등록
    /// 몇 번을 다시 실행해도 같은 결과(기존 에셋 덮어쓰기).
    /// </summary>
    public static class NamsanTowerMapTool
    {
        private const string kDir         = "Assets/Prefabs/Map/2_NamsanTower";
        private const string kPrefabPath  = "Assets/Resources/MapPrefabs/MapBg_NamsanTower.prefab";
        private const string kMapDefPath  = "Assets/Map/Maps/Map_NamsanTower.asset";
        private const string kAnswerPath  = kDir + "/Ans_NamsanTower.asset";
        private const string kConfigPath  = kDir + "/NamsanGimmickConfig_Namsan.asset";
        private const string kThumbPath   = "Assets/Map/Maps/Thumb_NamsanTower.png";
        private const string kMatDir      = "Assets/Map/Materials";
        private const string kMapCatalogPath = "Assets/Resources/MapCatalog.asset";
        // ⚠ GameScene의 GridManager가 물고 있는 '전역 재료 카탈로그' — 여기 없는 재료는 주문이 무시된다.
        private const string kGlobalMaterialCatalogPath = "Assets/Prefabs/Map/1_KwangTongGyo/1_GwangTongGyo_MaterialCatalog.asset";

        private static readonly Vector3Int kGridSize = new Vector3Int(11, 26, 11);
        private const float kTimeLimitSeconds = 420f;   // 7분 — 밸런스는 플레이 테스트로 조정

        // ── 파츠 정의(기획서 표 + Frame2 색) : 이름, id, footprint(가로,높이,세로), 공정, 색, 하중부재 ──
        private struct Part
        {
            public string Name; public int Id; public Vector3Int Fp;
            public ProcessType Proc; public Color Color; public bool MustFix;
        }

        private static readonly Part[] kParts =
        {
            // MustFix는 기반·철제전망대만 — 나머지는 고정 강제 없이 쌓는다(기획 확정)
            new Part{ Name="남산_기반",           Id=12, Fp=new Vector3Int(5,1,5), Proc=ProcessType.Fixed,   Color=new Color(0.96f,0.75f,0.78f), MustFix=true },
            new Part{ Name="남산_하부기둥",       Id=13, Fp=new Vector3Int(1,2,1), Proc=ProcessType.None,    Color=new Color(0.98f,0.85f,0.55f), MustFix=false },
            new Part{ Name="남산_철제받침기둥",   Id=14, Fp=new Vector3Int(1,2,1), Proc=ProcessType.None,    Color=new Color(0.98f,0.66f,0.18f), MustFix=false },
            new Part{ Name="남산_철제전망대",     Id=15, Fp=new Vector3Int(3,2,3), Proc=ProcessType.Fixed,   Color=new Color(0.72f,0.90f,0.12f), MustFix=true },
            new Part{ Name="남산_전망대받침",     Id=16, Fp=new Vector3Int(3,1,3), Proc=ProcessType.None,    Color=new Color(0.80f,0.94f,0.45f), MustFix=false },
            new Part{ Name="남산_하부안테나_빨강", Id=17, Fp=new Vector3Int(1,2,1), Proc=ProcessType.Painted, Color=new Color(0.99f,0.45f,0.42f), MustFix=false },
            new Part{ Name="남산_하부안테나_하양", Id=18, Fp=new Vector3Int(1,2,1), Proc=ProcessType.Painted, Color=new Color(0.97f,0.97f,0.97f), MustFix=false },
            new Part{ Name="남산_상부안테나",     Id=19, Fp=new Vector3Int(1,2,1), Proc=ProcessType.Painted, Color=new Color(0.75f,0.05f,0.08f), MustFix=false },
            new Part{ Name="남산_최상부안테나",   Id=20, Fp=new Vector3Int(1,3,1), Proc=ProcessType.Painted, Color=new Color(0.80f,0.45f,0.95f), MustFix=false },
        };

        // ── 타워 조립(정답): (파츠 id, 앵커 셀). 하부기둥×4, 나머지×1 — 총 높이 23 ──
        // 기둥 순서는 실물처럼 하부-철제받침-하부-하부-하부 (철제 링이 아래쪽에 온다)
        private static readonly (int id, Vector3Int anchor)[] kTower =
        {
            (12, new Vector3Int(3, 0, 3)),    // 기반 5×5, y0
            (13, new Vector3Int(5, 1, 5)),    // 하부기둥 y1-2
            (14, new Vector3Int(5, 3, 5)),    // 철제받침기둥 y3-4
            (13, new Vector3Int(5, 5, 5)),    // 하부기둥 y5-6
            (13, new Vector3Int(5, 7, 5)),    //          y7-8
            (13, new Vector3Int(5, 9, 5)),    //          y9-10
            (15, new Vector3Int(4, 11, 4)),   // 철제전망대 3×3, y11-12 ← 엘베 판정 구간 시작
            (16, new Vector3Int(4, 13, 4)),   // 전망대받침 3×3, y13   ← 엘베 판정 구간 끝
            (17, new Vector3Int(5, 14, 5)),   // 하부안테나(빨강) y14-15
            (18, new Vector3Int(5, 16, 5)),   // 하부안테나(하양) y16-17
            (19, new Vector3Int(5, 18, 5)),   // 상부안테나 y18-19
            (20, new Vector3Int(5, 20, 5)),   // 최상부안테나 y20-22 → 총 높이 23
        };

        [MenuItem("Tools/Map/★ 남산타워 맵 생성 (실전)")]
        public static void Generate()
        {
            // 배경 프리팹을 처음부터 다시 만든다 — 기획자가 직접 꾸민 배치가 있으면 날아가므로 반드시 확인.
            if (AssetDatabase.LoadAssetAtPath<GameObject>(kPrefabPath) != null &&
                !EditorUtility.DisplayDialog("남산타워 맵 재생성",
                    "배경 프리팹(MapBg_NamsanTower)을 처음부터 다시 만듭니다.\n" +
                    "직접 꾸민 배치·소품·마커 이동이 있으면 사라져요!\n(파츠 def·정답·기믹 설정은 안전)",
                    "재생성", "취소"))
                return;

            Directory.CreateDirectory(kDir);

            // ① 파츠 MaterialDef + 색큐브 프리팹
            var defs = new Dictionary<int, MaterialDef>();
            foreach (var p in kParts)
                defs[p.Id] = EnsurePartDef(p);

            // ② 전역 재료 카탈로그 등록(중복 없이 추가) — 없으면 주문이 조용히 무시된다!
            var matCatalog = AssetDatabase.LoadAssetAtPath<MaterialCatalog>(kGlobalMaterialCatalogPath);
            if (matCatalog == null) { Debug.LogError($"[남산타워] 전역 MaterialCatalog이 없음: {kGlobalMaterialCatalogPath}"); return; }
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

            // ③ 정답(높이 23 타워) — footprint대로 셀을 펼쳐 저장(익스포터와 동일 규칙)
            var answer = LoadOrCreate<MapAnswerData>(kAnswerPath);
            var cells = new List<(Vector3Int cell, int id)>();
            foreach (var (id, anchor) in kTower)
            {
                var fp = defs[id].Footprint;
                for (int dx = 0; dx < fp.x; dx++)
                for (int dy = 0; dy < fp.y; dy++)
                for (int dz = 0; dz < fp.z; dz++)
                    cells.Add((anchor + new Vector3Int(dx, dy, dz), id));
            }
            var ao = new SerializedObject(answer);
            ao.FindProperty("m_GridSize").vector3IntValue = kGridSize;
            ao.FindProperty("m_DisplayName").stringValue = "남산타워";
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

            // ④ 실전 기믹 설정(기본값 = 기획 확정치: 밴드 10/15, 케이블카 3대…)
            var cfg = LoadOrCreate<NamsanGimmickConfig>(kConfigPath);
            cfg.ObservatoryMinY = 11;   // 개통 조건 = 철제전망대(4번 블록, y11~12)만 — 받침(y13)은 무관
            cfg.ObservatoryMaxY = 12;
            EditorUtility.SetDirty(cfg);

            // ⑤ 그레이박스 배경 프리팹(Frame1 배치도)
            var root = BuildGreybox();
            Directory.CreateDirectory(Path.GetDirectoryName(kPrefabPath));
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, kPrefabPath, out bool ok);
            Object.DestroyImmediate(root);
            if (!ok) { Debug.LogError($"[남산타워] 프리팹 저장 실패: {kPrefabPath}"); return; }

            // ⑥ 맵 카드
            var def2 = LoadOrCreate<MapDef>(kMapDefPath);
            var so = new SerializedObject(def2);
            so.FindProperty("m_DisplayName").stringValue = "남산타워";
            so.FindProperty("m_BackgroundPrefab").objectReferenceValue = prefab;
            so.FindProperty("m_GridSize").vector3IntValue = kGridSize;
            so.FindProperty("m_NamsanGimmicks").objectReferenceValue = cfg;
            var answers = so.FindProperty("m_Answers");
            answers.arraySize = 1;
            answers.GetArrayElementAtIndex(0).objectReferenceValue = answer;
            var mats = so.FindProperty("m_AvailableMaterials");
            mats.arraySize = kParts.Length;
            for (int i = 0; i < kParts.Length; i++)
                mats.GetArrayElementAtIndex(i).objectReferenceValue = defs[kParts[i].Id];
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(def2);

            // ⑦ 썸네일 + 맵 카탈로그 — 일괄 촬영 툴과 같은 '완성 건물 중심' 규격(배경 통짜 샷 금지)
            var thumb = MapAnswerThumbnailTool.CaptureAnswerCentered(def2);
            if (thumb == null) thumb = MapThumbnailUtil.Capture(prefab, kThumbPath);
            if (thumb != null)
            {
                so.Update();
                so.FindProperty("m_Thumbnail").objectReferenceValue = thumb;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            var mapCatalog = AssetDatabase.LoadAssetAtPath<MapCatalog>(kMapCatalogPath);
            if (mapCatalog == null) { Debug.LogError($"[남산타워] MapCatalog이 없음: {kMapCatalogPath}"); return; }
            mapCatalog.EditorAdd(def2);
            EditorUtility.SetDirty(mapCatalog);

            AssetDatabase.SaveAssets();
            Selection.activeObject = def2;
            Debug.Log($"[남산타워] 완료 ✔ 로비에서 '남산타워'를 고르세요.\n" +
                      $"파츠 def 9종(id 12~20) {kDir} — VARCO 모델 나오면 각 def의 Prefab만 교체\n" +
                      $"정답 {cells.Count}칸(높이 23) · 전망대 y11~13 완성 시 엘베 개통 · 제한시간 {kTimeLimitSeconds / 60f:0}분");
        }

        // ── 파츠 def + 프리팹(피벗 min-corner, 규약 준수) ──
        // VARCO 모델(_Fit)이 있으면 그것만 쓰고, 없을 때만 색큐브 폴백을 만든다(불필요 에셋 방지).
        private static MaterialDef EnsurePartDef(Part p)
        {
            var fitPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{kDir}/{p.Name}_Fit.prefab");

            GameObject prefab = null;
            if (fitPrefab == null)
            {
                // 폴백 색큐브: 루트(피벗=min-corner) + 자식 큐브(footprint 크기, 파츠 색)
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

        // ── Frame1 그레이박스: 데크(윗동네) + 땅(아랫동네) + 계단 + 자물쇠벽 + 팔각정 + 케이블카존 + 데크 밑 로비 ──
        // 좌표 기준: Spot_GridManager=(0,0,0), 그리드 x,z∈[0,11) 데크 위. 데크 상판 y=0, 땅 y=-3.5(단차 크게 — 산 위 느낌).
        private const float kGroundY = -2.5f;   // 아랫동네 지면 높이(한 칸 올림 — 단차 2.5)
        private static GameObject BuildGreybox()
        {
            var root = new GameObject("MapBg_NamsanTower");

            var wood   = EnsureMaterial("Mat_NamsanDeck",   new Color(0.62f, 0.45f, 0.28f));
            var grass  = EnsureMaterial("Mat_NamsanGround", new Color(0.55f, 0.72f, 0.42f));
            var stone  = EnsureMaterial("Mat_NamsanStone",  new Color(0.52f, 0.50f, 0.48f));
            var dark   = EnsureMaterial("Mat_NamsanLobby",  new Color(0.32f, 0.33f, 0.38f));
            var pinkW  = EnsureMaterial("Mat_NamsanWall",   new Color(0.94f, 0.72f, 0.75f));
            var red    = EnsureMaterial("Mat_NamsanHeart",  new Color(0.92f, 0.12f, 0.15f));
            var green  = EnsureMaterial("Mat_NamsanPalgak", new Color(0.25f, 0.68f, 0.32f));

            // 나무 데크(윗동네) — 얇은 상판, 아래는 로비 공간. 전체적으로 이전보다 축소.
            AddBox(root, "Deck", new Vector3(0f, -0.25f, 5f), new Vector3(28f, 0.5f, 18f), wood).isStatic = true;
            // 데크 지지 기둥(장식 겸 로비 느낌 — 단차만큼 길어짐)
            AddBox(root, "DeckPost_W", new Vector3(-13.5f, -1.5f, 5f), new Vector3(1f, 2f, 16f), wood).isStatic = true;
            AddBox(root, "DeckPost_N", new Vector3(0f, -1.5f, 13.5f), new Vector3(26f, 2f, 1f), wood).isStatic = true;

            // 땅(아랫동네·산꼭대기 풀밭) — 평평한 정상부(충돌 담당). 산 모델이 있으면 비주얼은 산이 대신한다.
            var mountainPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{kDir}/남산_산_Fit.prefab");
            var ground = AddBox(root, "Ground", new Vector3(0f, kGroundY - 0.5f, -6f), new Vector3(30f, 1f, 22f), grass);
            ground.isStatic = true;

            if (mountainPrefab != null)
            {
                // VARCO 산(윗면 평평) — 정상부 윗면이 평지 높이에 오게 배치. 평지 박스는 투명 충돌체로만.
                ground.GetComponent<MeshRenderer>().enabled = false;
                var mt = (GameObject)PrefabUtility.InstantiatePrefab(mountainPrefab, root.transform);
                mt.transform.localPosition = new Vector3(0f, kGroundY - 10f, -6f);   // _Fit 높이 10, 바닥 피벗
            }
            else
            {
                // 폴백: 산비탈 박스(모델 나오면 자동 대체). 평지보다 낮게 붙여 위로 솟지 않게.
                var slopeMat = EnsureMaterial("Mat_NamsanSlope", new Color(0.42f, 0.58f, 0.34f));
                var sS = AddBox(root, "Slope_S", new Vector3(0f, kGroundY - 3.7f, -22f), new Vector3(38f, 1f, 12f), slopeMat);
                sS.transform.rotation = Quaternion.Euler(-28f, 0f, 0f); sS.isStatic = true;
                var sE = AddBox(root, "Slope_E", new Vector3(20f, kGroundY - 3.7f, -6f), new Vector3(12f, 1f, 26f), slopeMat);
                sE.transform.rotation = Quaternion.Euler(0f, 0f, -28f); sE.isStatic = true;
                var sW = AddBox(root, "Slope_W", new Vector3(-20f, kGroundY - 3.7f, -6f), new Vector3(12f, 1f, 26f), slopeMat);
                sW.transform.rotation = Quaternion.Euler(0f, 0f, 28f); sW.isStatic = true;
            }

            // 계단(나무): 땅 ↔ 데크(0), 서쪽 — 0.5 간격 4단(마지막 단→땅도 0.5).
            // 계단 모델이 있으면 박스는 투명 충돌체로 두고 모델이 비주얼 담당(걷기 판정 안전).
            bool hasStairModel = AssetDatabase.LoadAssetAtPath<GameObject>($"{kDir}/남산_계단_Fit.prefab") != null;
            for (int s = 0; s < 4; s++)
            {
                var step = AddBox(root, $"Stair{s + 1}",
                    new Vector3(-10f, -0.75f - 0.5f * s, -6.55f - 0.8f * s),
                    new Vector3(3.4f, 0.5f, 1.6f), wood);
                step.isStatic = true;
                if (hasStairModel) step.GetComponent<MeshRenderer>().enabled = false;
            }
            if (hasStairModel)
                TryPlaceProp(root, "남산_계단", new Vector3(-10f, kGroundY, -8.1f), 180f);   // 위쪽(+z 데크)으로 오르는 방향

            // 사랑의 자물쇠 벽 + 하트동상(데크 서쪽) — VARCO 모델 우선, 없으면 그레이박스
            if (!TryPlaceProp(root, "남산_자물쇠벽", new Vector3(-11.5f, 0f, 6f), 90f))
                AddBox(root, "LockWall", new Vector3(-11.5f, 1f, 6f), new Vector3(0.5f, 2f, 6f), pinkW).isStatic = true;
            if (!TryPlaceProp(root, "남산_하트동상", new Vector3(-10.6f, 0f, 9.5f)))
            {
                var heart = AddBox(root, "HeartStatue", new Vector3(-11f, 2.4f, 6f), new Vector3(0.9f, 0.9f, 0.5f), red);
                heart.transform.rotation = Quaternion.Euler(0f, 0f, 45f);
            }

            // 전망대(배경 존, 데크 북서쪽 살짝 높은 단)
            AddBox(root, "ViewDeck", new Vector3(-10.5f, 0.4f, 11.5f), new Vector3(6f, 0.8f, 4f), wood).isStatic = true;

            // 팔각정(아랫동네) — VARCO 모델(_Fit)이 있으면 그걸로, 없으면 그레이박스(몸통+지붕)
            if (!TryPlaceProp(root, "남산_팔각정", new Vector3(-4f, kGroundY, -13f)))
            {
                AddBox(root, "Palgak_Body", new Vector3(-4f, kGroundY + 0.6f, -13f), new Vector3(4.5f, 1.2f, 4.5f), stone).isStatic = true;
                AddBox(root, "Palgak_Roof", new Vector3(-4f, kGroundY + 1.5f, -13f), new Vector3(6f, 0.7f, 6f), green).isStatic = true;
            }

            // 케이블카존(아랫동네 동쪽) — 하차장 낮은 단. 출발점은 맵 가장자리 너머 저 아래(밑에서 올라오는 그림).
            AddBox(root, "CableDock", new Vector3(10f, kGroundY - 0.1f, -11f), new Vector3(4.5f, 0.3f, 4.5f), stone).isStatic = true;

            // 케이블카 철탑(모델 있으면): 와이어 '옆'에 비켜 세운다 — 경로 위에 세우면 곤돌라가 뚫고 지나감
            TryPlaceProp(root, "남산_철탑", new Vector3(21.3f, kGroundY - 5.5f, -22.5f), 50f);       // 경로 중간(사면 위), 옆으로 비킴
            TryPlaceProp(root, "남산_철탑", new Vector3(32.5f, kGroundY - 11.9f, -34.5f), 50f);      // 도시 터미널 옆

            // 하부 터미널: 저 아래 도시 평원에 승강장 건물 — 케이블카가 진짜 시내에서 올라오는 그림
            AddBox(root, "CableTerminal", new Vector3(30f, kGroundY - 10.6f, -36f), new Vector3(5f, 2.6f, 5f), dark).isStatic = true;

            // 나무 심기(모델 있으면): 아랫동네·데크 가장자리 — 휑함 방지. 위치·크기 고정 시드(재실행 동일)
            var treePrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{kDir}/남산_나무_Fit.prefab");
            if (treePrefab != null)
            {
                (Vector3 p, float sc, float rot)[] trees =
                {
                    (new Vector3(-12f, kGroundY, -15f), 1.1f, 20f),  (new Vector3(-7f, kGroundY, -16.5f), 0.9f, 130f),
                    (new Vector3(3f, kGroundY, -16.5f), 1.2f, 75f),  (new Vector3(14f, kGroundY, -15f), 0.85f, 300f),
                    (new Vector3(-14f, kGroundY, -9f), 1.0f, 220f),  (new Vector3(14.5f, kGroundY, -8f), 0.95f, 45f),
                    (new Vector3(-13f, 0f, 0.5f), 0.8f, 0f),         (new Vector3(-6f, 0f, 12.7f), 0.9f, 160f),
                    (new Vector3(3f, 0f, 12.7f), 1.05f, 260f),       (new Vector3(12.5f, 0f, 12.7f), 0.85f, 95f),
                    // 추가 식수(휑함 방지)
                    (new Vector3(5f, kGroundY, -15.8f), 1.0f, 15f),  (new Vector3(-2f, kGroundY, -16.8f), 0.9f, 200f),
                    (new Vector3(-15.5f, kGroundY, -12f), 1.15f, 80f), (new Vector3(-15f, kGroundY, -4.5f), 0.85f, 310f),
                    (new Vector3(14.7f, kGroundY, -11.8f), 0.95f, 140f),
                    (new Vector3(-13.2f, 0f, 10.5f), 0.9f, 55f),     (new Vector3(13.2f, 0f, 7f), 0.8f, 230f),
                    (new Vector3(13.2f, 0f, 1.5f), 1.0f, 350f),
                };
                for (int t = 0; t < trees.Length; t++)
                {
                    var tr = (GameObject)PrefabUtility.InstantiatePrefab(treePrefab, root.transform);
                    tr.name = $"Tree{t + 1}";
                    tr.transform.localPosition = trees[t].p;
                    tr.transform.localRotation = Quaternion.Euler(0f, trees[t].rot, 0f);
                    tr.transform.localScale *= trees[t].sc;
                }
            }

            // 원경(모델 있으면): 능선 2열(가까이+더 멀리 크게) + 저 아래 도시 평원 + 남쪽 스카이라인
            TryPlaceProp(root, "남산_산맥", new Vector3(0f, kGroundY - 9f, 55f));
            TryPlaceProp(root, "남산_산맥", new Vector3(-60f, kGroundY - 8f, 5f), 90f);
            TryPlaceProp(root, "남산_산맥", new Vector3(60f, kGroundY - 8f, -5f), -90f);
            TryPlaceProp(root, "남산_산맥", new Vector3(25f, kGroundY - 10f, 85f), 15f, 1.6f);
            TryPlaceProp(root, "남산_산맥", new Vector3(-85f, kGroundY - 9f, 35f), 75f, 1.5f);
            TryPlaceProp(root, "남산_산맥", new Vector3(90f, kGroundY - 9f, -35f), -75f, 1.5f);

            var cityMat = EnsureMaterial("Mat_NamsanCity", new Color(0.75f, 0.76f, 0.8f));
            AddBox(root, "CityPlain", new Vector3(0f, kGroundY - 12f, -30f), new Vector3(260f, 0.2f, 260f), cityMat).isStatic = true;

            // FarBldg 박스 스카이라인은 폐기(09/03 "회색 박스 제거" 지시) — 비주얼 정리 툴의
            // 텍스처 빌딩(VARCO/파사드)이 원경 도시를 담당한다. 기존 프리팹의 잔재는 MapTouchupAutoSetup v3가 지운다.

            // 데크 밑 엘베 로비 — 단차가 커진 만큼 건물도 커짐(땅에서 데크 밑면까지 꽉 채움)
            if (!TryPlaceProp(root, "남산_로비건물", new Vector3(10f, kGroundY, -3f)))
                AddBox(root, "LobbyBuilding", new Vector3(10f, kGroundY + 1f, -3f), new Vector3(6f, 2f, 5f), dark).isStatic = true;

            // ── 마커 8종 ──
            AddSpot(root, "Spot_GridManager", new Vector3(0f, 0f, 0f));                 // 짓는 곳(데크 동쪽)
            AddSpot(root, "Spot_PlayerSpawnPoint", new Vector3(2f, kGroundY, -10f));     // 아랫동네(팔각정·케이블카 사이)
            AddSpot(root, "Spot_HammerStation", new Vector3(-1f, kGroundY, -12f));       // 팔각정 옆 지상(마커 Y = 접지점)
            AddSpot(root, "Spot_PaintStation", new Vector3(-7f, 0f, 2f));                // 데크 위(〃 — 데크 상판 y=0)
            AddSpot(root, "Spot_CableCarStation", new Vector3(10f, kGroundY + 0.1f, -11f));   // 하차장(낮은 단 위)
            AddSpot(root, "Spot_CableCarOrigin", new Vector3(30f, kGroundY - 11.8f, -36f));   // 저 아래 도시 터미널에서 올라온다
            AddSpot(root, "Spot_ElevatorLower", new Vector3(10f, kGroundY, -6.2f));      // 로비 건물 정면(땅)

            // 상부 문 = 전망대(4번 블록) '정면 가운데'(남쪽 벽면 앞, 바닥 y11) — 진짜 건물 엘베처럼 벽에 붙는다.
            // 발판은 상시 제공(도착해서 설 자리). 전망대 남쪽 벽 = z4 평면, 타워 중심 x=5.5.
            // 발판은 개통 전엔 ElevatorNetwork가 숨긴다(전망대 완성 시 등장). 크기는 딱 문 앞 한 걸음.
            AddBox(root, "UpperDoorPlatform", new Vector3(5.5f, 10.8f, 3.2f), new Vector3(1.8f, 0.4f, 1.6f), wood).isStatic = true;
            AddSpot(root, "Spot_ElevatorUpper", new Vector3(5.5f, 11f, 3.75f));
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

        // VARCO 배경 소품(_Fit 프리팹, 바닥 피벗) 배치 시도 — NamsanModelApplyTool이 만들어둔 경우에만 true.
        private static bool TryPlaceProp(GameObject root, string name, Vector3 groundPos, float yRot = 0f, float scale = 1f)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{kDir}/{name}_Fit.prefab");
            if (prefab == null) return false;
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
            inst.transform.localPosition = groundPos;
            inst.transform.localRotation = Quaternion.Euler(0f, yRot, 0f);
            if (scale != 1f) inst.transform.localScale *= scale;
            // 서 있을 수 있게 콜라이더 보장(모델엔 보통 없음) — 모양 그대로 메시 콜라이더.
            // 바운즈 박스는 나무·곡면 모델에서 모양 밖 허공까지 막는 투명벽이 된다(DDP와 같은 처리).
            if (inst.GetComponentInChildren<Collider>() == null)
                foreach (var mf in inst.GetComponentsInChildren<MeshFilter>())
                    if (mf.sharedMesh != null)
                        mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
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
                if (sh == null) { Debug.LogWarning("[남산타워] URP Lit 셰이더를 못 찾음"); return null; }
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
