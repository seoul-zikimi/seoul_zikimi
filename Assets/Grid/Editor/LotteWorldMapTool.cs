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
        private const string kPrefabPath  = "Assets/Resources/MapPrefabs/MapBg_LotteWorld.prefab";
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
        // Overfill/Freeform = '비주얼 규약 예외'(LotteModelApplyTool이 그 규격으로 _Fit을 굽는다).
        // 여기서 def에 매번 다시 써 준다 — def를 지웠다 다시 만들어도 예외가 살아 있어야
        // 에디터 로드 때 MaterialPrefabFitTool.FitAll이 _Fit을 도로 칸 크기로 되돌리지 않는다.
        private struct Part
        {
            public string Name; public int Id; public Vector3Int Fp;
            public ProcessType Proc; public Color Color; public bool MustFix;
            public bool Overfill;   // 비주얼이 일부러 칸을 벗어남(첨탑 밑동 연장)
            public bool Freeform;   // 비주얼이 칸을 꽉 채우지 않음(깃발 — 깃대 축 정렬)
        }

        private static readonly Part[] kParts =
        {
            // MustFix는 기반·본체만 — 나머지는 고정 강제 없이 쌓는다(남산과 같은 밸런스)
            new Part{ Name="롯데_성기반",     Id=21, Fp=new Vector3Int(5,1,5), Proc=ProcessType.Fixed,   Color=new Color(0.90f,0.87f,0.82f), MustFix=true },
            new Part{ Name="롯데_성본체",     Id=22, Fp=new Vector3Int(3,3,3), Proc=ProcessType.Fixed,   Color=new Color(0.98f,0.95f,0.88f), MustFix=true },
            new Part{ Name="롯데_성상단",     Id=23, Fp=new Vector3Int(3,1,3), Proc=ProcessType.None,    Color=new Color(0.99f,0.98f,0.94f), MustFix=false },
            new Part{ Name="롯데_중앙첨탑",   Id=24, Fp=new Vector3Int(1,3,1), Proc=ProcessType.Painted, Color=new Color(0.25f,0.45f,0.85f), MustFix=false, Overfill=true },
            new Part{ Name="롯데_코너타워",   Id=25, Fp=new Vector3Int(1,4,1), Proc=ProcessType.Fixed,   Color=new Color(0.96f,0.92f,0.80f), MustFix=false },
            new Part{ Name="롯데_타워지붕",   Id=26, Fp=new Vector3Int(1,2,1), Proc=ProcessType.Painted, Color=new Color(0.30f,0.55f,0.90f), MustFix=false },
            new Part{ Name="롯데_정문게이트", Id=27, Fp=new Vector3Int(1,2,1), Proc=ProcessType.Painted, Color=new Color(0.85f,0.65f,0.25f), MustFix=false },
            new Part{ Name="롯데_깃발",       Id=28, Fp=new Vector3Int(1,1,1), Proc=ProcessType.Painted, Color=new Color(0.95f,0.35f,0.30f), MustFix=false, Freeform=true },
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
            // 모노레일 기둥 마커: 기존 프리팹에 사용자가 옮겨 둔 Spot_MonoPillar*가 있으면 그 위치를 그대로 쓴다(재생성에도 유지)
            var monoPillars = LoadExistingMonoPillars();
            var propTweaks = CaptureUserPropTweaks();   // 재생성 전에 사용자 손조정(타워·첨탑 스케일 등) 실측
            var root = BuildGreybox(monoPillars);
            ApplyUserPropTweaks(root, propTweaks);      // 재생성 후 같은 이름·근접 소품에 그대로 재적용
            Directory.CreateDirectory(Path.GetDirectoryName(kPrefabPath));

            // ── 멱등 가드: 새로 조립한 결과가 기존 프리팹과 '의미상' 같으면 저장을 건너뛴다.
            // SaveAsPrefabAsset은 실행마다 모든 fileID를 새로 발급해, 내용이 같아도 파일 텍스트가
            // 통째로 달라진다 — 브랜치마다 재생성이 돌 때마다 MapBg/Thumb 머지 충돌이 났던 원인.
            bool prefabChanged = !SameAsExistingPrefab(root, kPrefabPath);
            GameObject prefab;
            if (prefabChanged)
            {
                prefab = PrefabUtility.SaveAsPrefabAsset(root, kPrefabPath, out bool ok);
                Object.DestroyImmediate(root);
                if (!ok) { Debug.LogError($"[롯데월드] 프리팹 저장 실패: {kPrefabPath}"); return; }

                // BuildGreybox가 프리팹을 매번 처음부터 새로 굽기 때문에, 비주얼 정리 툴이 깔아 둔
                // ~Horizon(원경)이 재생성 때마다 통째로 사라졌다(QA "horizon 존나 계속 누락") —
                // 여기서 곧바로 다시 깔아 누락 자체를 없앤다.
                MapVisualPolishTool.ApplyHorizonFor(kPrefabPath);
            }
            else
            {
                Object.DestroyImmediate(root);
                prefab = AssetDatabase.LoadAssetAtPath<GameObject>(kPrefabPath);
                Debug.Log("[롯데월드] 배경 프리팹이 기존과 동일 — 저장·썸네일 재생성 생략(머지 충돌 방지)");
            }

            // ⑥ 맵 카드
            var def2 = LoadOrCreate<MapDef>(kMapDefPath);
            var so = new SerializedObject(def2);
            so.FindProperty("m_DisplayName").stringValue = "롯데월드";
            so.FindProperty("m_BackgroundPrefab").objectReferenceValue = prefab;
            so.FindProperty("m_GridSize").vector3IntValue = kGridSize;
            so.FindProperty("m_LotteGimmicks").objectReferenceValue = cfg;
            // 바닥·모델이 둘 다 밝아 기본 고스트(알파 0.16 원색)가 묻힌다 — 알파를 올리고 색을 어둡게 깎는다.
            so.FindProperty("m_GhostAlpha").floatValue = 0.32f;
            so.FindProperty("m_GhostTintMul").floatValue = 0.75f;
            var answers = so.FindProperty("m_Answers");
            answers.arraySize = 1;
            answers.GetArrayElementAtIndex(0).objectReferenceValue = answer;
            var mats = so.FindProperty("m_AvailableMaterials");
            mats.arraySize = kParts.Length;
            for (int i = 0; i < kParts.Length; i++)
                mats.GetArrayElementAtIndex(i).objectReferenceValue = defs[kParts[i].Id];
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(def2);

            // ⑦ 썸네일 + 맵 카탈로그 — 프리팹이 실제로 바뀐 경우(또는 썸네일이 없을 때)만 재렌더.
            // 렌더 결과 PNG는 기기·GPU마다 바이트가 미세하게 달라, 불필요 재렌더 자체가 머지 충돌원이다.
            // 재렌더할 때는 일괄 촬영 툴과 같은 '완성 건물 중심' 규격(실패 시에만 배경 통짜 샷 폴백).
            Sprite thumb;
            if (prefabChanged || !File.Exists(kThumbPath))
            {
                thumb = MapAnswerThumbnailTool.CaptureAnswerCentered(def2);
                if (thumb == null) thumb = MapThumbnailUtil.Capture(prefab, kThumbPath);
            }
            else thumb = AssetDatabase.LoadAssetAtPath<Sprite>(kThumbPath);
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

        // ── 재생성 멱등 비교: 이름 경로·트랜스폼·메시·머티리얼·그림자 설정이 전부 같으면 '같은 프리팹'으로 본다.
        // fileID는 비교하지 않는다(재생성마다 달라지는 게 정상). '~'로 시작하는 오브젝트(~Horizon 등)는
        // 저장 뒤 후처리 툴이 심는 장식이라 새 조립본엔 없으므로 양쪽 다 비교에서 제외한다.
        private static bool SameAsExistingPrefab(GameObject newRoot, string prefabPath)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (existing == null) return false;
            return HierarchySignature(newRoot.transform) == HierarchySignature(existing.transform);
        }

        private static string HierarchySignature(Transform root)
        {
            var lines = new List<string>();
            void Walk(Transform t, string path)
            {
                if (t.name.Length > 0 && t.name[0] == '~') return;
                var sb = new System.Text.StringBuilder(path);
                sb.Append('|').Append(t.gameObject.layer)
                  .Append('|').Append(t.gameObject.activeSelf ? 1 : 0)
                  .Append('|').Append(Fmt(t.localPosition))
                  .Append('|').Append(Fmt(t.localRotation))
                  .Append('|').Append(Fmt(t.localScale));
                if (t.TryGetComponent(out MeshFilter mf) && mf.sharedMesh != null)
                    sb.Append("|m:").Append(AssetDatabase.GetAssetPath(mf.sharedMesh)).Append(':').Append(mf.sharedMesh.name);
                if (t.TryGetComponent(out Renderer r))
                {
                    sb.Append("|s:").Append((int)r.shadowCastingMode);
                    foreach (var mat in r.sharedMaterials)
                        sb.Append("|M:").Append(mat != null ? AssetDatabase.GetAssetPath(mat) + ":" + mat.name : "null");
                }
                lines.Add(sb.ToString());
                for (int i = 0; i < t.childCount; i++)
                {
                    var c = t.GetChild(i);
                    Walk(c, path + "/" + c.name);   // 형제 순서는 비교하지 않는다(후처리 삽입으로 인덱스가 밀릴 수 있음)
                }
            }
            Walk(root, root.name);
            lines.Sort(System.StringComparer.Ordinal);
            return string.Join("\n", lines);
        }

        private static string Fmt(Vector3 v) => $"{v.x:F4},{v.y:F4},{v.z:F4}";
        private static string Fmt(Quaternion q) => $"{q.x:F4},{q.y:F4},{q.z:F4},{q.w:F4}";

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
            so.FindProperty("m_FreeformVisual").boolValue = p.Freeform;
            so.FindProperty("m_IntentionalOverfill").boolValue = p.Overfill;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(def);
            return def;
        }

        // ── 그레이박스 배경: 석촌호수 + 매직아일랜드(건축장) + 남쪽 광장(배송·작업대) + 그 사이 퍼레이드 길 ──
        // 좌표 기준: Spot_GridManager=(0,0,0), 그리드 x,z∈[0,13) 섬 위. 섬 상판 y=0, 호수 수면 y=-0.6.
        // 핵심 동선: 광장(남, z≈-6~-10)에서 재료를 받아 → 퍼레이드 길(z≈-3)을 건너 → 섬(북)에서 짓는다.
        private static GameObject BuildGreybox(Vector3[] monoPillars)
        {
            var root = new GameObject("MapBg_LotteWorld");

            var water  = EnsureLakeWater();   // Toon Water 셰이더(있으면) — 민짜 청록판은 '블럭'으로 보였음
            var stoneW = EnsureMaterial("Mat_LotteStoneWhite", new Color(0.92f, 0.91f, 0.87f));
            var road   = EnsureMaterial("Mat_LotteParade",  new Color(0.93f, 0.90f, 0.84f));   // [08/31·6차] 연노랑 → 아이보리(텍스처_퍼레이드길 훅)
            var roadEdge = EnsureMaterial("Mat_LotteParadeEdge", new Color(0.80f, 0.68f, 0.52f));   // 길 구분용 가장자리 벽돌 띠
            var plaza  = EnsureMaterial("Mat_LottePlaza",   new Color(0.86f, 0.83f, 0.80f));
            var steel  = EnsureMaterial("Mat_LotteSteel",   new Color(0.62f, 0.64f, 0.70f));
            var glass  = EnsureMaterial("Mat_LotteGlass",   new Color(0.70f, 0.83f, 0.92f));

            // 석촌호수 — 원형 수면(비주얼 겸 낙하 방지 바닥). 네모 수면은 인공 풀장처럼 보여 실제처럼 둥글게.
            var lake = AddCylinder(root, "Lake", new Vector3(6.5f, -0.75f, 2f), new Vector3(kLakeD, 0.15f, kLakeD), water);
            // 납작 원기둥의 캡슐 콜라이더는 가운데가 불룩 — 낙하 방지는 예전 네모 수면처럼 평평한 박스가 맡는다
            Object.DestroyImmediate(lake.GetComponent<Collider>());
            lake.AddComponent<BoxCollider>();

            // 매직아일랜드(북) — 건축 그리드가 올라가는 섬. 상판 y=0. 남산 정도 크기(30×20)로 압축.
            // 실제 매직아일랜드처럼 상판은 잔디가 아니라 '광장과 같은 타일 포장'.
            // VARCO 섬 지형이 있으면 석축 옆면은 지형 메시가, 상판은 타일 데크가, 충돌은 투명 박스가 담당.
            var islandBox = AddBox(root, "Island", new Vector3(6.5f, -0.5f, 8f), new Vector3(30f, 1f, 20f), plaza);
            islandBox.isStatic = true;
            // 상판을 살짝 낮춰(잔디 윗면 숨김) 그 위에 타일 데크를 얹는다
            bool hasIslandMesh = TryPlaceProp(root, "롯데_섬지형", new Vector3(6.5f, -3.08f, 8f));
            if (hasIslandMesh)
            {
                islandBox.GetComponent<MeshRenderer>().enabled = false;   // 충돌체만 남긴다
                AddBox(root, "IslandDeck", new Vector3(6.5f, -0.05f, 8f), new Vector3(30f, 0.1f, 20f), plaza).isStatic = true;
            }
            else
            {
                // 폴백: 흰 석재 테두리(호수와 경계) — 지형 메시가 오면 자동 제거
                AddBox(root, "IslandRim_S", new Vector3(6.5f, -0.12f, -1.9f), new Vector3(30f, 0.28f, 0.7f), stoneW).isStatic = true;
                AddBox(root, "IslandRim_W", new Vector3(-8.3f, -0.12f, 8f), new Vector3(0.7f, 0.28f, 20f), stoneW).isStatic = true;
                AddBox(root, "IslandRim_E", new Vector3(21.3f, -0.12f, 8f), new Vector3(0.7f, 0.28f, 20f), stoneW).isStatic = true;
                AddBox(root, "IslandRim_N", new Vector3(6.5f, -0.12f, 17.9f), new Vector3(30f, 0.28f, 0.7f), stoneW).isStatic = true;
            }

            // [08/31] 섬 마감 — 물에서 올라오는 타원 석축 받침(박스 섬이 수면에 '덩그러니' 뜨는 문제).
            //  윗면(-0.45)이 수면(-0.6) 위로 살짝 드러나 섬 둘레에 돌 기슭 띠가 생긴다.
            var revet = EnsureMaterial("Mat_LotteRevetment", new Color(0.62f, 0.62f, 0.64f));   // 석축 전용(텍스처_석축.png 훅)
            var islandBase = AddCylinder(root, "IslandBase", new Vector3(6.5f, -0.95f, 8f), new Vector3(36f, 0.5f, 26f), revet);
            Object.DestroyImmediate(islandBase.GetComponent<Collider>());

            // [08/31·3차] 울타리 → 낮은 성벽(총안 흉벽) — '섬이 덩그러니' 피드백 3번째. 매직캐슬 테마에 맞는
            //  크림색 성벽 + 네 모서리 파랑 고깔 탑으로 섬을 두른다. 투명 벽(BuildGuardWalls)과 같은 선, 순회로 입구만 뚫는다.
            var castleStone = EnsureMaterial("Mat_LotteWall", new Color(0.90f, 0.87f, 0.80f));
            var fence = new GameObject("IslandWall");
            fence.transform.SetParent(root.transform, false);
            BuildCastleWallRun(fence, castleStone, new Vector3(-8.5f, 0f, 18f),  new Vector3(3.5f, 0f, 18f));    // 북서(둑길 입구까지)
            BuildCastleWallRun(fence, castleStone, new Vector3(9.5f, 0f, 18f),   new Vector3(21.5f, 0f, 18f));   // 북동(둑길 입구부터)
            BuildCastleWallRun(fence, castleStone, new Vector3(-8.5f, 0f, -2f),  new Vector3(-8.5f, 0f, 18f));   // 서
            BuildCastleWallRun(fence, castleStone, new Vector3(21.5f, 0f, -2f),  new Vector3(21.5f, 0f, 18f));   // 동
            BuildCastleWallRun(fence, castleStone, new Vector3(-8.5f, 0f, -2f),  new Vector3(-7.1f, 0f, -2f));   // 남서 토막
            BuildCastleWallRun(fence, castleStone, new Vector3(16.1f, 0f, -2f),  new Vector3(21.5f, 0f, -2f));   // 남동 토막
            // 네 모서리 고깔 탑(다리탑 재활용) — 0.7배는 성벽에 파묻혀 보여 1.3배로(4차 피드백)
            foreach (var c in new[] { new Vector3(-8.5f, 0f, 18f), new Vector3(21.5f, 0f, 18f), new Vector3(-8.5f, 0f, -2f), new Vector3(21.5f, 0f, -2f) })
                TryPlaceProp(root, "롯데_다리탑", c, 0f, 1.3f);

            // [08/31·4차] 광장 둘레에도 같은 성벽 — "저 부분은 왜 벽이 없냐"(스샷 2). 순회로 입구·둑길만 연다.
            BuildCastleWallRun(fence, castleStone, new Vector3(-6.5f, 0f, -12f), new Vector3(19.5f, 0f, -12f));   // 광장 남
            BuildCastleWallRun(fence, castleStone, new Vector3(-6.5f, 0f, -12f), new Vector3(-6.5f, 0f, -4f));    // 광장 서
            BuildCastleWallRun(fence, castleStone, new Vector3(19.5f, 0f, -12f), new Vector3(19.5f, 0f, -4f));    // 광장 동
            BuildCastleWallRun(fence, castleStone, new Vector3(16.1f, 0f, -4f),  new Vector3(19.5f, 0f, -4f));    // 광장 북동 잔여
            BuildCastleWallRun(fence, castleStone, new Vector3(-7.1f, 0f, -4.5f), new Vector3(-7.1f, 0f, -1.9f)); // 순회로 서쪽 끝막이
            BuildCastleWallRun(fence, castleStone, new Vector3(16.1f, 0f, -4.5f), new Vector3(16.1f, 0f, -1.9f)); // 순회로 동쪽 끝막이

            // 광장(남) — 배송존·작업대·스폰. 상판 y=0.
            AddBox(root, "Plaza", new Vector3(6.5f, -0.5f, -8f), new Vector3(26f, 1f, 8f), plaza).isStatic = true;

            // 퍼레이드 길 — 섬과 광장 사이(z=-3)를 동서로 관통. 양쪽 어디서든 건널 수 있는 평지.
            // [08/31] 길이 36→23.2 — 순회로 폭에 맞춤(예전엔 섬 밖 허공까지 뻗어 그리로 걸어나가 호수에 빠졌음)
            AddBox(root, "ParadeRoad", new Vector3(4.5f, -0.485f, -3.2f), new Vector3(23.2f, 1.03f, 2.6f), road).isStatic = true;
            // [08/31] 퍼레이드 순회로 — 건축 그리드를 사각으로 도는 길 3면(남면은 위 ParadeRoad가 겸함).
            //  섬 상판(y0) 위 3cm 띠. 동측 x14.8 = 자이로 패드(x15.85~)와 안 겹치는 최대치.
            AddBox(root, "ParadeRoad_E", new Vector3(14.2f, 0.015f, 5.5f), new Vector3(2.6f, 0.03f, 20f), road).isStatic = true;
            AddBox(root, "ParadeRoad_N", new Vector3(4.5f, 0.015f, 14.2f), new Vector3(22f, 0.03f, 2.6f), road).isStatic = true;
            AddBox(root, "ParadeRoad_W", new Vector3(-5.2f, 0.015f, 5.5f), new Vector3(2.6f, 0.03f, 20f), road).isStatic = true;
            // [08/31·6차] 길 구분용 가장자리 벽돌 띠 — 남(광장 길)·동·북·서 레그 양옆
            void RoadEdges(string name, Vector3 c, Vector3 size, bool alongX)
            {
                var off = alongX ? new Vector3(0f, 0f, size.z * 0.5f + 0.18f) : new Vector3(size.x * 0.5f + 0.18f, 0f, 0f);
                var es  = alongX ? new Vector3(size.x, 0.035f, 0.35f) : new Vector3(0.35f, 0.035f, size.z);
                foreach (var sgn in new[] { -1f, 1f })
                {
                    var e = AddBox(root, $"{name}_Edge{(sgn < 0 ? "A" : "B")}", c + off * sgn + new Vector3(0f, 0.02f, 0f), es, roadEdge);
                    Object.DestroyImmediate(e.GetComponent<Collider>());
                    e.isStatic = true;
                }
            }
            RoadEdges("ParadeRoad",   new Vector3(4.5f, 0.015f, -3.2f), new Vector3(23.2f, 0.03f, 2.6f), true);
            RoadEdges("ParadeRoad_E", new Vector3(14.2f, 0.015f, 5.5f), new Vector3(2.6f, 0.03f, 20f), false);
            RoadEdges("ParadeRoad_N", new Vector3(4.5f, 0.015f, 14.2f), new Vector3(22f, 0.03f, 2.6f), true);
            RoadEdges("ParadeRoad_W", new Vector3(-5.2f, 0.015f, 5.5f), new Vector3(2.6f, 0.03f, 20f), false);

            // [08/31·4차] 다리를 북쪽으로 이설 — 실제 롯데월드: 매직아일랜드는 '본관'과 다리로 이어진다.
            //  남쪽 반도(분수·로티 구역)는 실존하지 않는 지형이라 통째로 폐기(사용자 확정).
            //  섬 북단(z18) → 본관 앞(z44)을 잇는 석재 둑길: 보행면은 섬 높이(0.03) 그대로 평평.
            var causeway = AddBox(root, "Causeway", new Vector3(6.5f, -0.485f, 31f), new Vector3(6f, 1.03f, 26f),
                                  EnsureMaterial("Mat_LotteCauseway", new Color(0.88f, 0.85f, 0.80f)));   // 광장과 같은 벽돌 텍스처 훅
            causeway.isStatic = true;
            // VARCO 아치 다리는 둑길 남측(섬 쪽)에 장식으로 겹친다. y -1.35는 물에 묻혔음 → -0.75(+0.6 상향)
            // 아치가 둑길 평면 위로 솟아 몸이 파묻혔음 → 메시 콜라이더로 실제 곡면을 걷게(둑길 박스와 공존)
            PlaceProp(root, "롯데_다리", new Vector3(6.5f, -0.75f, 24f), 0f, 1f);
            bool turretL = TryPlaceProp(root, "롯데_다리탑", new Vector3(2.85f, 0f, 18.6f));    // 섬 북문 게이트 성탑
            bool turretR = TryPlaceProp(root, "롯데_다리탑", new Vector3(10.15f, 0f, 18.6f));
            float[] lampZ = { 22f, 27f, 32f, 37f, 42f };   // 둑길 양옆 가로등 행렬
            foreach (float lz in lampZ)
            {
                TryPlaceProp(root, "롯데_가로등", new Vector3(3.9f, 0.03f, lz));
                TryPlaceProp(root, "롯데_가로등", new Vector3(9.1f, 0.03f, lz));
            }
            if (!turretL || !turretR)
            {
                PlaceGateTower(root, new Vector3(2.6f, 0f, 17f));
                PlaceGateTower(root, new Vector3(10.4f, 0f, 17f));
            }

            // ── 원경 프롭: VARCO _Fit 우선, 없으면 그레이박스 폴백 ──

            // (호안 뗏목 3종 폐기 — 둥근 호수 위에 네모 잔디판이 떠 보였음. 이제 육지는 호수 '밖' ~Horizon 도시 지면(-0.95)이 맡고,
            //  랜드마크는 전부 물 건너 진짜 땅 위에 선다. 사진 구도와 동일.)

            // 석촌호수 둘레길 — 수변 나무데크 링 + 벚꽃 가로수 2열(빽빽하게). 실제 석촌호수 산책로 재현.
            BuildLakesideLoop(root);

            // [08/31] 투명 가드 벽 — 호수는 콜라이더가 있어 '물 위를 걸을 수' 있었음. 섬·광장·다리·남쪽 반도 둘레를
            //  높이 30m 보이지 않는 벽으로 막는다(점프 탈출 불가). 통로는 순회로 입구·다리 목만 연다.
            BuildGuardWalls(root);

            // [08/31·3차] 구경꾼 NPC(광통교 Onlooker 재활용) — idle 애니 오리·개구리가 데크·광장에 서 있다. '인조적' 피드백 대응.
            BuildOnlookers(root);

            // [08/31] 놀이공원 소품 — VARCO _Fit 있을 때만(폴백 없음).
            // [08/31·2차] 호수 위 분수 2기는 '떠 있는 돌대야'로 보여 폐기 — 남쪽 반도 로터리(뭍)로 1기만
            // [08/31·4차] 분수대·마스코트석상 배치 폐기 — 남쪽 반도가 사라지면서 자리도 없어짐("원래 없잖아")
            TryPlaceProp(root, "롯데_스낵바", new Vector3(18f, 0f, -5.5f), -60f);   // 망치 작업대가 동쪽으로 와서 북동 코너로 비켜줌             // 광장 남동 스낵 키오스크
            TryPlaceProp(root, "롯데_풍선노점", new Vector3(19f, 0f, 8f), -60f);                 // 섬 동측(망치 작업대와 간격 6.5m)
            TryPlaceProp(root, "롯데_백조보트", new Vector3(-18f, -0.68f, -10f), 40f);           // 호수 백조 보트 3척([09/01] 사용자 조정 반영)
            TryPlaceProp(root, "롯데_백조보트", new Vector3(28.16f, -0.68f, 9.31f), 215f);
            TryPlaceProp(root, "롯데_백조보트", new Vector3(26.48f, -0.68f, 12.53f), 150f);

            // 롯데월드타워(북동, 호수 건너 — 랜드마크가 화면에 늘 보이게). y -0.9 = ~Horizon 도시 지면 위
            if (!TryPlaceProp(root, "롯데_롯데월드타워", new Vector3(22.1f, -0.9f, 66f)))   // [09/01] 사용자 조정 — 본관 뒤 북동
            {
                AddBox(root, "TowerBase", new Vector3(40f, 7f, 46f), new Vector3(7f, 16f, 7f), glass).isStatic = true;
                AddBox(root, "TowerMid",  new Vector3(40f, 20f, 46f), new Vector3(5f, 10f, 5f), glass).isStatic = true;
                AddBox(root, "TowerTop",  new Vector3(40f, 28.5f, 46f), new Vector3(2.6f, 7f, 2.6f), glass).isStatic = true;
            }

            // ── 어트랙션: 성(그리드 x0~13, z0~13) 북쪽 라인에 아기자기하게 — 간격 압축 ──

            // 배치 컨셉: 정답(성) = 중앙, 회전목마 = 왼쪽 아래(배송존 근처), 자이로드롭 = 오른쪽 뒤 — 일렬 배치 회피.

            // 자이로드롭(성 오른쪽 뒤, 동쪽 플랭크에서 북쪽으로) — 패드 + 모델(없으면 그레이박스 기둥)
            // 신형: 기둥/원반 분리 — 원반이 기둥을 타고 오르내린다(LotteAmbientRides). 구형 통짜 → 그레이박스 순 폴백.
            // [08/31] x 18→18.6 — 퍼레이드 순회로 동측 레그(x14.8)와 패드가 겹치지 않게 반 발짝 동쪽으로
            AddCylinder(root, "GyroPad", new Vector3(18.6f, -0.05f, 12.5f), new Vector3(5.5f, 0.12f, 5.5f), stoneW);
            Transform gyroDisc = null;
            var gyroPole = PlaceProp(root, "롯데_자이로기둥", new Vector3(18.6f, 0f, 12.5f), 0f, 1f, addCollider: false);
            if (gyroPole != null)
            {
                // 통짜 바운즈 콜라이더는 패드 절반을 막는다 — 기둥 몸통만 슬림하게 막는다
                var bc = gyroPole.AddComponent<BoxCollider>();
                bc.center = new Vector3(0f, 7f, 0f);
                bc.size = new Vector3(1.6f, 14f, 1.6f);
                var disc = PlaceProp(root, "롯데_자이로원반", new Vector3(18.6f, 1.1f, 12.5f), 0f, 1f, addCollider: false);
                if (disc != null) { disc.name = "~GyroDisc"; gyroDisc = disc.transform; }
            }
            else if (!TryPlaceProp(root, "롯데_자이로드롭", new Vector3(18.6f, 0f, 12.5f)))
            {
                AddCylinder(root, "GyroPole", new Vector3(18.6f, 5.5f, 12.5f), new Vector3(0.8f, 5.5f, 0.8f), steel);
                AddCylinder(root, "GyroRing", new Vector3(18.6f, 7.5f, 12.5f), new Vector3(2.6f, 0.5f, 2.6f), stoneW);
            }

            // [08/31·9차] 모노레일 대개편 — 통짜 타원 링 폐기. 기둥 마커(Spot_MonoPillar0~N) 위치에 기둥을 세우고
            // 마커 사이를 직선 빔으로 이어 닫힌 궤도를 만든다. 마커는 프리팹에서 옮기면 다음 '맵 생성' 때 그대로 유지된다.
            var beamPath = BuildMonorail(root, monoPillars, out Transform[] monoTrains);

            // 앰비언트 연출 연결(원반 승강 + 열차 순환) — 대상이 없으면 컴포넌트도 안 붙인다
            if (gyroDisc != null || monoTrains.Length > 0)
            {
                var rides = root.AddComponent<LotteAmbientRides>();
                var ro = new SerializedObject(rides);
                ro.FindProperty("m_GyroDisc").objectReferenceValue = gyroDisc;
                ro.FindProperty("m_GyroBottomY").floatValue = 1.1f;
                ro.FindProperty("m_GyroTopY").floatValue = 10.6f;
                var trainsProp = ro.FindProperty("m_Trains");
                trainsProp.arraySize = monoTrains.Length;
                for (int i = 0; i < monoTrains.Length; i++)
                    trainsProp.GetArrayElementAtIndex(i).objectReferenceValue = monoTrains[i];
                var pathProp = ro.FindProperty("m_TrainPath");
                pathProp.arraySize = beamPath.Length;
                for (int i = 0; i < beamPath.Length; i++)
                    pathProp.GetArrayElementAtIndex(i).vector3Value = beamPath[i];
                ro.FindProperty("m_TrainSpeed").floatValue = 2.2f;
                ro.FindProperty("m_TrainYawOffsetDeg").floatValue = -90f;   // 열차 장축(x) → 진행방향 정렬
                ro.ApplyModifiedPropertiesWithoutUndo();
            }

            // 회전목마(광장 왼쪽 '끝 모서리' — 자이로드롭처럼 구석에 딱) — 패드 + 모델(없으면 그레이박스 원통)
            AddCylinder(root, "CarouselPad", new Vector3(-2.1f, -0.05f, -7.95f), new Vector3(6f, 0.12f, 6f), stoneW);   // [09/01] 사용자 조정
            if (!TryPlaceProp(root, "롯데_회전목마", new Vector3(-2.1f, 0f, -7.95f)))
            {
                AddCylinder(root, "CarouselBody", new Vector3(-3.4f, 1f, -8.9f), new Vector3(3.4f, 1f, 3.4f), stoneW);
                AddCylinder(root, "CarouselRoof", new Vector3(-3.4f, 2.6f, -8.9f), new Vector3(4f, 0.6f, 4f), glass);
            }

            // 열기구 풍선(섬 주변 상공 장식) — 모델(롯데_풍선_Fit) 있을 때만 배치. 폴백 없음(색 구체는 오히려 어색).
            (Vector3 p, float rot, float sc)[] balloonSpots =
            {
                (new Vector3(-9f, 4.5f, 3f), 20f, 1.0f),
                (new Vector3(22f, 5.5f, 6f), 250f, 0.9f),
                (new Vector3(15f, 6f, 20f), 140f, 1.05f),
                (new Vector3(-2f, 5f, -13f), 70f, 0.85f),   // 다리 옆 호수 위
            };
            foreach (var (p, rot, sc) in balloonSpots)
                TryPlaceProp(root, "롯데_풍선", p, rot, sc);

            // ── 원경(사진 재현): 호수 건너 어드벤처 본관·청록 돔 요새·아파트 스카이라인 ──
            // 전부 모델 있을 때만 배치(폴백 없음 — 그레이박스 박스는 오히려 어색).

            // 위치는 전부 호수 밖(반경 32 너머) 가로수 링 뒤 — y -0.9 = ~Horizon 도시 지면 위.
            // 어드벤처 본관(북쪽 기슭 — 유리 돔이 섬 뒤 배경을 채운다)
            TryPlaceProp(root, "롯데_어드벤처돔", new Vector3(6.5f, -0.9f, 50f), 180f);   // 본관 — 둑길(z44)이 정문에 닿는다. 아케이드 정면이 남쪽(맵)을 보게 180°
            // 청록 돔 석조 요새(동쪽 기슭 — 항공사진 오른편의 그 건물)
            // [08/31·2차] 돔요새(아틀란티스)는 도심이 아니라 매직아일랜드 부속 — 섬 북동에 전용 석축 패드를 띄워 붙인다(실제 배치)
            var atlantisBase = AddCylinder(root, "AtlantisBase", new Vector3(28.5f, -0.95f, -2f), new Vector3(15f, 0.5f, 15f),   // [09/01] 사용자 조정
                                           EnsureMaterial("Mat_LotteRevetment", new Color(0.62f, 0.62f, 0.64f)));
            Object.DestroyImmediate(atlantisBase.GetComponent<Collider>());
            TryPlaceProp(root, "롯데_돔요새", new Vector3(28.5f, -0.45f, -2f), 315f);   // 섬 남동 부속 패드 위
            // 원경 아파트/타워(북서~서쪽 기슭 스카이라인, 잠실 아파트 숲)
            TryPlaceProp(root, "롯데_원경아파트", new Vector3(-22f, -0.9f, 46f), 15f);
            TryPlaceProp(root, "롯데_원경아파트", new Vector3(-36f, -0.9f, 28f), 75f, 0.85f);
            TryPlaceProp(root, "롯데_원경빌딩", new Vector3(26f, -0.9f, 50f), 10f);

            // 호안 벚꽃(석촌호수 봄) — 광장 양끝·다리 입구·섬 모서리
            (Vector3 p, float rot, float sc)[] cherrySpots =
            {
                (new Vector3(-8.5f, 0f, -11f), 0f, 1.0f),
                (new Vector3(-6.5f, 0f, -5.5f), 70f, 0.9f),
                (new Vector3(19f, 0f, -11f), 140f, 1.05f),
                (new Vector3(18.5f, 0f, -5.5f), 210f, 0.95f),
                (new Vector3(2.9f, 0f, -11.6f), 30f, 0.85f),    // 다리 입구 왼쪽(광장 남단)
                (new Vector3(10.1f, 0f, -11.6f), 300f, 0.85f),  // 다리 입구 오른쪽(광장 남단)
                (new Vector3(-7f, 0f, 16.5f), 45f, 1.1f),       // 섬 북서 모서리
                (new Vector3(20f, 0f, 16.5f), 260f, 1.0f),      // 섬 북동 모서리
            };
            foreach (var (p, rot, sc) in cherrySpots)
                TryPlaceProp(root, "롯데_벚나무", p, rot, sc);

            // ── 마커: 필수 5종 + 퍼레이드 경로 4점 ──
            AddSpot(root, "Spot_GridManager", new Vector3(0f, 0f, 0f));                // 짓는 곳(섬)
            AddSpot(root, "Spot_PlayerSpawnPoint", new Vector3(6.5f, 0.1f, -6f));      // 광장 북쪽(길 바로 앞)
            AddSpot(root, "Spot_DeliveryZone", new Vector3(6.5f, 0.1f, -8.5f));        // 광장 중앙 — 재료는 여기로 떨어진다
            AddSpot(root, "Spot_HammerStation", new Vector3(17.3f, 0f, 2.7f));        // [09/01] 사용자 프리팹 조정 반영(마커 Y = 접지점)
            AddSpot(root, "Spot_PaintStation", new Vector3(-2.85f, 0f, 16f));         // [09/01] 사용자 프리팹 조정 반영
            // 퍼레이드 경로 — ParadeNetwork가 0번부터 순서대로 잇는 폴리라인.
            // [08/31·4차] 본관에서 출발: 북쪽 둑길로 입장 → 건축 그리드 한 바퀴(사각 순회) → 둑길로 퇴장(실제 퍼레이드 동선).
            AddSpot(root, "Spot_ParadePoint0", new Vector3(6.5f, 0.1f, 46f));     // 본관 앞(입장)
            AddSpot(root, "Spot_ParadePoint1", new Vector3(6.5f, 0.1f, 14.2f));   // 섬 북문 → 북측 순회로 합류
            AddSpot(root, "Spot_ParadePoint2", new Vector3(-5.2f, 0.1f, 14.2f));  // 북서 코너
            AddSpot(root, "Spot_ParadePoint3", new Vector3(-5.2f, 0.1f, -3.2f));  // 남서 코너
            AddSpot(root, "Spot_ParadePoint4", new Vector3(14.2f, 0.1f, -3.2f));  // 남동 코너
            AddSpot(root, "Spot_ParadePoint5", new Vector3(14.2f, 0.1f, 14.2f));  // 북동 코너
            AddSpot(root, "Spot_ParadePoint6", new Vector3(6.5f, 0.1f, 14.2f));   // 북문 복귀(한 바퀴 완성)
            AddSpot(root, "Spot_ParadePoint7", new Vector3(6.5f, 0.1f, 46f));     // 둑길로 퇴장
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

        // 다리 입구 게이트 타워: 코너타워_Fit(1×4×1)만 세운다(지붕 없음 — 기획 확정).
        // 파츠 _Fit은 min-corner 피벗이라 중심 배치 시 절반만큼 당긴다. 모델이 아직 없으면 조용히 통과(폴백 없음).
        // ── 석촌호수 둘레길 ──────────────────────────────────────────────
        private const float kLakeD = 76f;   // 호수 지름. 140=섬이 좁쌀, 64=아틀란티스가 데크와 겹침 → 76(4차 확정)
        private static readonly Vector3 kLakeCenter = new Vector3(6.5f, 0f, 2f);

        /// <summary>호수 가장자리를 따라 나무데크 판자 링 + 둘레 가로수. 판자는 두 톤을 번갈아 깔아 널빤지 느낌.
        /// 나무는 VARCO 벚나무/활엽수를 번갈아 심고(결정적 난수), 모델이 없으면 그레이박스 기둥+구 폴백.
        /// 나무 발밑(y-0.9)은 ~Horizon 도시 지면(-0.95) 기준 — 원경을 깔아야 떠 보이지 않는다.</summary>
        private static void BuildLakesideLoop(GameObject root)
        {
            float lakeR = kLakeD * 0.5f;
            var deckA = EnsureMaterial("Mat_LotteBoardwalk",     new Color(0.58f, 0.42f, 0.27f));
            var deckB = EnsureMaterial("Mat_LotteBoardwalkDark", new Color(0.50f, 0.36f, 0.23f));
            var grp = new GameObject("LakesideLoop");
            grp.transform.SetParent(root.transform, false);

            // ① 물가 잔디 띠: 수면(-0.6) 바로 위에 걸치는 초록 둔치 — 데크 '안쪽' 수목·덤불이 설 자리(실제 석촌호수 재현)
            var grassMat = EnsureMaterial("Mat_LotteShore", new Color(0.55f, 0.72f, 0.42f));
            float grassR = lakeR - 0.7f;
            RingOfBoxes(grp, "ShoreStrip", grassR, 3.2f, -0.52f, 0.12f, grassMat, grassMat);

            // ② 데크: 잔디 띠 바깥(뭍쪽) — 두 톤 널빤지, 몸통을 도시 지면(-0.95)까지 내려 접지(떠 보임 방지)
            float deckR = lakeR + 1.8f;
            RingOfBoxes(grp, "Boardwalk", deckR, 2.6f, -0.6f, 0.7f, deckA, deckB);

            // ②' 데크 가로등 — ~13m 간격으로 산책로에 검정 주철 램프(다리 목·남쪽 반도 구간 제외). 밤 분위기 + 디테일 밀도
            int lamps = Mathf.CeilToInt(2f * Mathf.PI * deckR / 13f);
            for (int i = 0; i < lamps; i++)
            {
                float ang = i / (float)lamps * Mathf.PI * 2f;
                var pos = kLakeCenter + new Vector3(Mathf.Cos(ang) * deckR, -0.25f, Mathf.Sin(ang) * deckR);
                if (pos.z > 16f && Mathf.Abs(pos.x - 6.5f) < 7f) continue;   // 북쪽 둑길 회랑
                TryPlaceProp(grp, "롯데_가로등", pos, (float)(-(ang * Mathf.Rad2Deg) + 90f));
            }

            // ② 가로수: 항공사진의 '끊김 없는 벚꽃 띠' — 2열 지그재그, ~4m 간격, 벚나무 위주(2/3).
            //    벚나무 변형·활엽수·남산 나무를 섞어 반복 티를 줄인다. 남쪽 반도 위는 발밑을 지면(0)으로 올리고, 다리 목은 비운다.
            var trunkMat = EnsureMaterial("Mat_LotteTrunk", new Color(0.42f, 0.30f, 0.20f));
            var leafMat  = EnsureMaterial("Mat_LotteLeaf",  new Color(0.48f, 0.66f, 0.38f));
            var rng = new System.Random(2026);
            var namsanTree = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Map/2_NamsanTower/남산_나무_Fit.prefab");
            bool hasCherry2 = AssetDatabase.LoadAssetAtPath<GameObject>($"{kDir}/롯데_벚나무2_Fit.prefab") != null;
            // [08/31·2차] 더 빽빽하게 — 간격 축소 + 3열째 추가(뒤로 갈수록 성기게). 실제 석촌호수는 벚꽃 '벽'이다.
            var rows = new (float r, float step)[] { (lakeR + 4.5f, 3.2f), (lakeR + 7.5f, 3.4f), (lakeR + 10.5f, 4.5f) };
            int made = 0;
            for (int row = 0; row < rows.Length; row++)
            {
                int n = Mathf.CeilToInt(2f * Mathf.PI * rows[row].r / rows[row].step);
                for (int i = 0; i < n; i++)
                {
                    float ang = (i + row * 0.5f + (float)rng.NextDouble() * 0.35f) / n * Mathf.PI * 2f;
                    float r = rows[row].r + (float)rng.NextDouble() * 1.6f;
                    var pos = kLakeCenter + new Vector3(Mathf.Cos(ang) * r, -0.9f, Mathf.Sin(ang) * r);
                    if (pos.z > 16f && Mathf.Abs(pos.x - 6.5f) < 6.5f) continue;                    // 북쪽 둑길 회랑
                    float sc = Mathf.Lerp(0.8f, 1.25f, (float)rng.NextDouble());
                    int kind = rng.Next(6);   // 0~3 벚나무(변형 있으면 반씩), 4 활엽수, 5 남산 나무
                    GameObject t;
                    if (kind == 5 && namsanTree != null)
                    {
                        t = (GameObject)PrefabUtility.InstantiatePrefab(namsanTree, grp.transform);
                        t.transform.localPosition = pos;
                        t.transform.localRotation = Quaternion.Euler(0f, rng.Next(360), 0f);
                        t.transform.localScale *= sc;
                    }
                    else
                    {
                        string prop = kind >= 4 ? "롯데_나무" : (hasCherry2 && kind % 2 == 1 ? "롯데_벚나무2" : "롯데_벚나무");
                        t = PlaceProp(grp, prop, pos, rng.Next(360), sc, addCollider: false);
                    }
                    if (t != null) { t.name = $"Tree{++made}"; continue; }
                    // 그레이박스 폴백(모델이 하나도 없을 때만 의미 있음)
                    var trunk = AddCylinder(grp, $"TreeTrunk{++made}", pos + new Vector3(0f, 1.1f, 0f), new Vector3(0.35f, 1.1f, 0.35f), trunkMat);
                    Object.DestroyImmediate(trunk.GetComponent<Collider>());
                    var crown = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    crown.name = $"TreeCrown{made}";
                    crown.transform.SetParent(grp.transform, false);
                    crown.transform.localPosition = pos + new Vector3(0f, 3.1f, 0f);
                    crown.transform.localScale = new Vector3(2.6f, 2.2f, 2.6f) * sc;
                    crown.GetComponent<Renderer>().sharedMaterial = leafMat;
                    Object.DestroyImmediate(crown.GetComponent<Collider>());
                    crown.isStatic = true;
                }
            }

            // ④ 잔디 띠(데크 안쪽) 수풀: 덤불 촘촘 + 작은 벚나무 드문드문 — 실제 석촌호수의 물가 수풀.
            //    덤불은 VARCO(롯데_덤불_Fit) 우선, 없으면 납작 초록/분홍 구 폴백.
            var bushA = EnsureMaterial("Mat_LotteBush",     new Color(0.40f, 0.58f, 0.32f));
            var bushB = EnsureMaterial("Mat_LotteBushPink", new Color(0.93f, 0.66f, 0.76f));
            int nIn = Mathf.CeilToInt(2f * Mathf.PI * grassR / 2.2f);   // [08/31] 3.6→2.2m 간격 — "여전히 휑함" 피드백, 수풀을 띠처럼 촘촘히
            for (int i = 0; i < nIn; i++)
            {
                float ang = (i + (float)rng.NextDouble() * 0.4f) / nIn * Mathf.PI * 2f;
                float r = grassR + ((float)rng.NextDouble() - 0.5f) * 2.4f;
                var pos = kLakeCenter + new Vector3(Mathf.Cos(ang) * r, -0.46f, Mathf.Sin(ang) * r);
                if (pos.z > 16f && Mathf.Abs(pos.x - 6.5f) < 6.5f) continue;                     // 북쪽 둑길 회랑
                if (i % 5 == 0)   // 다섯에 하나는 안쪽 열 작은 벚나무
                {
                    float tsc = Mathf.Lerp(0.6f, 0.85f, (float)rng.NextDouble());
                    var inner = PlaceProp(grp, hasCherry2 && i % 2 == 1 ? "롯데_벚나무2" : "롯데_벚나무", pos, rng.Next(360), tsc, addCollider: false);
                    if (inner != null) { inner.name = $"TreeIn{i}"; continue; }
                }
                var bush = PlaceProp(grp, "롯데_덤불", pos, rng.Next(360), Mathf.Lerp(0.8f, 1.3f, (float)rng.NextDouble()), addCollider: false);
                if (bush != null) { bush.name = $"Bush{i}"; continue; }
                var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);   // 폴백
                ball.name = $"Bush{i}";
                ball.transform.SetParent(grp.transform, false);
                float bs = Mathf.Lerp(0.9f, 1.5f, (float)rng.NextDouble());
                ball.transform.localPosition = pos + new Vector3(0f, 0.3f, 0f);
                ball.transform.localScale = new Vector3(bs * 1.5f, bs * 0.8f, bs * 1.5f);
                ball.GetComponent<Renderer>().sharedMaterial = rng.Next(4) == 0 ? bushB : bushA;
                Object.DestroyImmediate(ball.GetComponent<Collider>());
                ball.isStatic = true;
            }
        }

        // ── 모노레일(기둥 마커 방식) ──────────────────────────────────
        /// <summary>기본 기둥 배치(시계 방향 8점) — 순회로·둑길·스테이션·자이로 패드를 전부 피해 검증한 좌표.</summary>
        private static readonly Vector3[] kDefaultMonoPillars =   // [09/01] 사용자 설계 10기둥 — 섬을 돌고 둑길 따라 본관 앞까지 다녀온다
        {
            new Vector3(-3.12f, 0f, 0f),   new Vector3(2.94f, 0f, -9.04f),   new Vector3(11f, 0f, -9.64f),  new Vector3(17f, 0f, -0.8f),
            new Vector3(17.41f, 0f, 6.29f), new Vector3(13.81f, 0f, 16.69f), new Vector3(10.04f, 0f, 34.66f), new Vector3(1.42f, 0f, 35.61f),
            new Vector3(0.83f, 0f, 16.15f), new Vector3(-3.27f, 0f, 7.82f),
        };
        private const float kBeamY = 6f;     // [09/01] 빔 중심 높이 3.8→6 — 더 높이 달리는 그림(하단 5.75, 퍼레이드 카 2.7 여유)

        /// <summary>기존 프리팹의 Spot_MonoPillar0~N 위치를 읽는다(사용자 조정 유지). 없으면 기본 배치.</summary>
        private static Vector3[] LoadExistingMonoPillars()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(kPrefabPath);
            if (prefab == null) return kDefaultMonoPillars;
            var pts = new List<Vector3>();
            for (int i = 0; ; i++)
            {
                var t = prefab.transform.Find($"Spot_MonoPillar{i}");
                if (t == null) break;
                pts.Add(t.localPosition);
            }
            return pts.Count >= 3 ? pts.ToArray() : kDefaultMonoPillars;
        }

        /// <summary>기둥 마커 폴리라인으로 모노레일을 짓는다: 마커(AddSpot) + 기둥 + 마커 사이 직선 빔(닫힌 루프) + 열차.
        /// 반환값 = 빔 상면 높이의 열차 경로(LotteAmbientRides.m_TrainPath에 기록).
        /// 기둥은 VARCO(롯데_모노기둥_Fit) 우선, 없으면 흰 원기둥 그레이박스. 빔은 길이 가변이라 항상 그레이박스.</summary>
        private static Vector3[] BuildMonorail(GameObject root, Vector3[] pillars, out Transform[] trains)
        {
            var grp = new GameObject("Monorail");
            grp.transform.SetParent(root.transform, false);
            var beamMat   = EnsureMaterial("Mat_LotteMonoBeam",   new Color(0.45f, 0.72f, 0.88f));   // 하늘색 빔
            var pillarMat = EnsureMaterial("Mat_LotteMonoPillar", new Color(0.94f, 0.94f, 0.96f));   // 흰 기둥

            var path = new Vector3[pillars.Length];
            for (int i = 0; i < pillars.Length; i++)
            {
                var g = pillars[i];
                AddSpot(root, $"Spot_MonoPillar{i}", g);   // ← 프리팹에서 이 마커를 옮기면 다음 재생성 때 그대로 반영
                // 기둥 상단 U자 받침이 레일 진행방향을 보게 회전(이웃 마커 기준 접선)
                var tan = pillars[(i + 1) % pillars.Length] - pillars[(i - 1 + pillars.Length) % pillars.Length];
                float pillarYaw = Mathf.Atan2(tan.x, tan.z) * Mathf.Rad2Deg;
                if (PlaceProp(grp, "롯데_모노기둥", g, pillarYaw, 1f, addCollider: false) == null)
                {
                    var post = AddCylinder(grp, $"MonoPillar{i}", g + new Vector3(0f, kBeamY * 0.5f, 0f),
                                           new Vector3(0.8f, kBeamY * 0.5f, 0.8f), pillarMat);
                    Object.DestroyImmediate(post.GetComponent<Collider>());
                    var foot = AddCylinder(grp, $"MonoPillarFoot{i}", g + new Vector3(0f, 0.15f, 0f),
                                           new Vector3(1.3f, 0.15f, 1.3f), pillarMat);
                    Object.DestroyImmediate(foot.GetComponent<Collider>());
                }
                path[i] = new Vector3(g.x, kBeamY + 0.4f, g.z);   // 빔 상면 + 열차 접지
            }
            // [09/01] 빔: 마커를 지나는 '닫힌 캣멀롬 스플라인'을 따라 단면을 압출한 프로시저럴 메시 — 곡선 레일.
            //  (VARCO 직선 빔 방식 폐기: 꺾인 폴리라인은 코너가 각졌고, 곡선엔 스플라인 압출이 정답)
            var railMat = EnsureMaterial("Mat_LotteMonoRail", new Color(0.96f, 0.96f, 0.98f));   // 상단 흰 레일 2줄
            var samples = SampleClosedSpline(pillars, 16);   // 마커당 16분할 — 로우폴리 톤에 충분히 매끈
            var trackMesh = BuildTrackMesh(samples);
            AssetDatabase.DeleteAsset($"{kDir}/MonorailTrack.asset");
            AssetDatabase.CreateAsset(trackMesh, $"{kDir}/MonorailTrack.asset");   // 메모리 메시는 프리팹 저장 시 사라짐 — 에셋 필수
            var track = new GameObject("MonoTrack");
            track.transform.SetParent(grp.transform, false);
            track.AddComponent<MeshFilter>().sharedMesh = trackMesh;
            var tmr = track.AddComponent<MeshRenderer>();
            tmr.sharedMaterials = new[] { beamMat, railMat };
            tmr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            track.isStatic = true;

            // 열차 경로 = 같은 스플라인(4칸마다 1점, 레일 상면 높이) — 곡선을 부드럽게 탄다
            var trainPath = new List<Vector3>();
            for (int i = 0; i < samples.Length; i += 4)
                trainPath.Add(samples[i] + new Vector3(0f, kBeamY + 0.45f, 0f));
            path = trainPath.ToArray();

            // [09/01] 열차 2대 편성 — 경로 반 바퀴 간격으로 마주 보며 순환(LotteAmbientRides가 균등 간격 유지)
            var list = new List<Transform>();
            for (int k = 0; k < 2; k++)
            {
                var t = PlaceProp(grp, "롯데_모노레일열차", path[path.Length * k / 2 % path.Length], 0f, 1f, addCollider: false);
                if (t != null) { t.name = $"~MonorailTrain{k + 1}"; list.Add(t.transform); }
            }
            trains = list.ToArray();
            return path;
        }

        /// <summary>마커들을 지나는 닫힌 캣멀롬 스플라인을 표본화(수평면, y는 무시하고 0으로). 마커당 divisions 분할.</summary>
        private static Vector3[] SampleClosedSpline(Vector3[] pts, int divisions)
        {
            int n = pts.Length;
            var outPts = new Vector3[n * divisions];
            for (int i = 0; i < n; i++)
            {
                Vector3 p0 = Flat(pts[(i - 1 + n) % n]), p1 = Flat(pts[i]), p2 = Flat(pts[(i + 1) % n]), p3 = Flat(pts[(i + 2) % n]);
                for (int k = 0; k < divisions; k++)
                {
                    float u = k / (float)divisions;
                    outPts[i * divisions + k] = 0.5f * ((2f * p1) + (-p0 + p2) * u
                        + (2f * p0 - 5f * p1 + 4f * p2 - p3) * (u * u)
                        + (-p0 + 3f * p1 - 3f * p2 + p3) * (u * u * u));
                }
            }
            return outPts;
            static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
        }

        /// <summary>스플라인을 따라 빔 단면을 압출한 트랙 메시. 서브메시 0 = 파란 거더 몸통, 1 = 상단 흰 레일 2줄.
        /// 단면은 진행방향 기준 (right, up) 평면에 CCW로 정의 — 닫힌 루프라 마감 캡 불필요.</summary>
        private static Mesh BuildTrackMesh(Vector3[] center)
        {
            // 단면 루프(CCW, x=right·y=빔 중심 기준 상대높이): 몸통 1개 + 레일 2개
            var loops = new[]
            {
                new[] { new Vector2(0.28f, -0.25f), new Vector2(0.28f, 0.25f), new Vector2(-0.28f, 0.25f), new Vector2(-0.28f, -0.25f) },
                new[] { new Vector2(0.30f, 0.25f), new Vector2(0.30f, 0.37f), new Vector2(0.16f, 0.37f), new Vector2(0.16f, 0.25f) },
                new[] { new Vector2(-0.16f, 0.25f), new Vector2(-0.16f, 0.37f), new Vector2(-0.30f, 0.37f), new Vector2(-0.30f, 0.25f) },
            };
            int n = center.Length;
            int vertsPerRing = 0;
            foreach (var l in loops) vertsPerRing += l.Length;

            var verts = new List<Vector3>(n * vertsPerRing);
            var uvs = new List<Vector2>(n * vertsPerRing);
            float dist = 0f;
            for (int i = 0; i < n; i++)
            {
                var prev = center[(i - 1 + n) % n];
                var next = center[(i + 1) % n];
                var fwd = (next - prev); fwd.y = 0f; fwd = fwd.normalized;
                var right = Vector3.Cross(Vector3.up, fwd);
                if (i > 0) dist += Vector3.Distance(center[i], center[i - 1]);
                var basePos = center[i] + new Vector3(0f, kBeamY, 0f);
                int vj = 0;
                foreach (var loop in loops)
                    foreach (var p in loop)
                    {
                        verts.Add(basePos + right * p.x + Vector3.up * p.y);
                        uvs.Add(new Vector2(dist * 0.5f, vj++ / (float)vertsPerRing));
                    }
            }

            var subs = new[] { new List<int>(), new List<int>() };
            for (int i = 0; i < n; i++)
            {
                int ri = i * vertsPerRing, rn = ((i + 1) % n) * vertsPerRing;
                int off = 0;
                for (int li = 0; li < loops.Length; li++)
                {
                    var tris = subs[li == 0 ? 0 : 1];
                    int L = loops[li].Length;
                    for (int j = 0; j < L; j++)
                    {
                        int a = ri + off + j, b = ri + off + (j + 1) % L;
                        int c = rn + off + (j + 1) % L, d = rn + off + j;
                        tris.Add(a); tris.Add(b); tris.Add(c);
                        tris.Add(a); tris.Add(c); tris.Add(d);
                    }
                    off += L;
                }
            }

            var mesh = new Mesh { name = "MonorailTrack" };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(subs[0], 0);
            mesh.SetTriangles(subs[1], 1);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        // ── 구경꾼 NPC(생동감) ────────────────────────────────────────
        /// <summary>광통교 Onlooker 프리팹(idle 애니 오리·개구리)을 데크 8자리 + 광장 2 + 반도 2에 세운다.
        /// 데크 구경꾼은 호수를 바라본다. 프리팹이 없으면 조용히 생략.</summary>
        private static void BuildOnlookers(GameObject root)
        {
            var pool = new List<GameObject>();
            foreach (var n in new[] { "Onlooker_Duck", "Onlooker_Frog" })
            {
                var p = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Map/01_GwangTongGyo/Props/{n}.prefab");
                if (p != null) pool.Add(p);
            }
            if (pool.Count == 0) return;
            var rng = new System.Random(505);
            var grp = new GameObject("Onlookers");
            grp.transform.SetParent(root.transform, false);

            void Spawn(Vector3 pos, float yaw)
            {
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(pool[rng.Next(pool.Count)], grp.transform);
                inst.transform.localPosition = pos;
                inst.transform.localRotation = Quaternion.Euler(0f, yaw + (float)rng.NextDouble() * 20f - 10f, 0f);
            }

            float r = kLakeD * 0.5f + 1.8f;   // 데크 중심선
            const int kDeckSpots = 8;
            for (int i = 0; i < kDeckSpots; i++)
            {
                float ang = (i + 0.5f) / kDeckSpots * Mathf.PI * 2f;
                var pos = kLakeCenter + new Vector3(Mathf.Cos(ang) * r, -0.25f, Mathf.Sin(ang) * r);
                if (pos.z > 16f && Mathf.Abs(pos.x - 6.5f) < 7f) continue;   // 북쪽 둑길 회랑
                Spawn(pos, Mathf.Atan2(kLakeCenter.x - pos.x, kLakeCenter.z - pos.z) * Mathf.Rad2Deg);   // 호수 쪽 보기
            }
            Spawn(new Vector3(-4.5f, 0f, -5.2f), 140f);    // 광장 서측(회전목마 앞)
            Spawn(new Vector3(15.8f, 0f, -5.6f), 220f);    // 광장 동측
            Spawn(new Vector3(4.2f, 0.03f, 35f), 100f);    // 둑길 서측(퍼레이드 마중)
            Spawn(new Vector3(8.8f, 0.03f, 39f), 260f);    // 둑길 동측
        }

        // ── 투명 가드 벽 ──────────────────────────────────────────────
        /// <summary>렌더러 없는 BoxCollider 벽. 높이 30m — 점프 스택으로도 못 넘는다.</summary>
        private static void AddInvisibleWall(GameObject root, string name, Vector3 center, Vector3 size, float yawDeg = 0f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = center;
            go.transform.localRotation = Quaternion.Euler(0f, yawDeg, 0f);
            go.layer = LayerMask.NameToLayer("Ignore Raycast");   // 시야가림 페이드·클릭 레이캐스트에 안 걸리게(물리 충돌은 그대로)
            go.AddComponent<BoxCollider>().size = size;
            go.isStatic = true;
        }

        /// <summary>플레이 가능 지면(섬+순회로+광장+다리+남쪽 반도)의 바깥 윤곽을 투명 벽으로 두른다.
        /// 열린 곳: 섬 남쪽 순회로 입구(x -7.1~16.1), 광장 남쪽 다리 목(x 3.5~9.5), 반도 북쪽 다리 접점.</summary>
        private static void BuildGuardWalls(GameObject root)
        {
            var grp = new GameObject("~GuardWalls");
            grp.transform.SetParent(root.transform, false);
            const float H = 30f, Y = 15f;
            // 섬 — 북면은 둑길 입구(x 3.5~9.5)만 열고 2토막
            AddInvisibleWall(grp, "Wall_IslandNW", new Vector3(-2.75f, Y, 18.5f), new Vector3(12.5f, H, 1f));
            AddInvisibleWall(grp, "Wall_IslandNE", new Vector3(15.75f, Y, 18.5f), new Vector3(12.5f, H, 1f));
            AddInvisibleWall(grp, "Wall_IslandW",  new Vector3(-9f, Y, 8f),      new Vector3(1f, H, 21f));
            AddInvisibleWall(grp, "Wall_IslandE",  new Vector3(22f, Y, 8f),      new Vector3(1f, H, 21f));
            AddInvisibleWall(grp, "Wall_IslandSW", new Vector3(-7.95f, Y, -2f),  new Vector3(1.7f, H, 1f));
            AddInvisibleWall(grp, "Wall_IslandSE", new Vector3(19.05f, Y, -2f),  new Vector3(5.9f, H, 1f));
            // 순회로 서·동 끝(섬-광장 사이 틈)
            AddInvisibleWall(grp, "Wall_RoadW", new Vector3(-7.6f, Y, -3.2f), new Vector3(1f, H, 3.4f));
            AddInvisibleWall(grp, "Wall_RoadE", new Vector3(16.6f, Y, -3.2f), new Vector3(1f, H, 3.4f));
            // 광장 — 남면은 이제 다리가 없으니 전부 막는다
            AddInvisibleWall(grp, "Wall_PlazaW",  new Vector3(-7f, Y, -8f),     new Vector3(1f, H, 9f));
            AddInvisibleWall(grp, "Wall_PlazaE",  new Vector3(20f, Y, -8f),     new Vector3(1f, H, 9f));
            AddInvisibleWall(grp, "Wall_PlazaNE", new Vector3(17.8f, Y, -4f),   new Vector3(3.4f, H, 1f));
            AddInvisibleWall(grp, "Wall_PlazaS",  new Vector3(6.5f, Y, -12.5f), new Vector3(27f, H, 1f));
            // 북쪽 둑길 양옆 + 본관 앞 끝막이(본관 뒤 허공 추락 방지)
            // [08/31·8차] 섬 성문~다리 접합부에 구멍 — 구간을 z15~46으로 늘리고 날개(아치다리 석재 어깨) 차단 스텁 추가
            AddInvisibleWall(grp, "Wall_CausewayW", new Vector3(3f, Y, 30.5f),  new Vector3(1f, H, 31f));
            AddInvisibleWall(grp, "Wall_CausewayE", new Vector3(10f, Y, 30.5f), new Vector3(1f, H, 31f));
            AddInvisibleWall(grp, "Wall_CausewayN", new Vector3(6.5f, Y, 44.5f), new Vector3(9f, H, 1f));
            AddInvisibleWall(grp, "Wall_BridgeWingW", new Vector3(0.9f, Y, 21f), new Vector3(5.2f, H, 1f));   // 서쪽 어깨(성문 밖 x -1.7~3.5)
            AddInvisibleWall(grp, "Wall_BridgeWingE", new Vector3(12.1f, Y, 21f), new Vector3(5.2f, H, 1f));  // 동쪽 어깨(x 9.5~14.7)
        }

        /// <summary>호수 수면 머티리얼: ThirdParty Toon Water URP가 있으면 그 사본(Mat_LotteLakeWater)을 쓰고,
        /// 없으면 민짜 청록 폴백. 사본이라 물빛·거품 수치는 에셋에서 자유롭게 조절 가능.</summary>
        private static Material EnsureLakeWater()
        {
            var src = AssetDatabase.LoadAssetAtPath<Material>("Assets/ThirdParty/Toon Water URP/Toon Water Material 1.mat");
            if (src == null)
                return EnsureMaterial("Mat_LotteLake", new Color(0.14f, 0.47f, 0.43f));
            string path = $"{kMatDir}/Mat_LotteLakeWater.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(src);
                Directory.CreateDirectory(kMatDir);
                AssetDatabase.CreateAsset(mat, path);
            }
            return mat;
        }

        /// <summary>두 점 사이 낮은 성벽: 몸통(높이 1.0, 두께 0.5) + 총안 흉벽(0.5³ 블록, 1.2m 간격). 콜라이더 없음(투명 벽이 담당).</summary>
        private static void BuildCastleWallRun(GameObject parent, Material mat, Vector3 from, Vector3 to)
        {
            var dir = to - from;
            float len = dir.magnitude;
            if (len < 0.5f) return;
            dir /= len;
            var mid = (from + to) * 0.5f;
            float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;

            var body = AddBox(parent, $"Wall_{mid.x:0}_{mid.z:0}", mid + new Vector3(0f, 0.5f, 0f), new Vector3(0.5f, 1f, len), mat);
            body.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            Object.DestroyImmediate(body.GetComponent<Collider>());
            body.isStatic = true;

            int merlons = Mathf.Max(2, Mathf.FloorToInt(len / 1.2f) + 1);
            for (int i = 0; i < merlons; i++)
            {
                var p = Vector3.Lerp(from, to, i / (float)(merlons - 1));
                var m = AddBox(parent, $"Merlon_{p.x:0}_{p.z:0}_{i}", p + new Vector3(0f, 1.2f, 0f), new Vector3(0.55f, 0.4f, 0.55f), mat);
                Object.DestroyImmediate(m.GetComponent<Collider>());
                m.isStatic = true;
            }
        }

        /// <summary>두 점 사이에 공원 울타리: 흰 기둥(3m 간격, 1m 높이) + 가로대 상(0.92)·중(0.5) 2단. 콜라이더 없음(투명 벽이 담당).</summary>
        private static void BuildFenceRun(GameObject parent, Material mat, Vector3 from, Vector3 to)
        {
            var dir = to - from;
            float len = dir.magnitude;
            if (len < 0.5f) return;
            dir /= len;
            var mid = (from + to) * 0.5f;
            float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;

            // 가로대 2단 — 구간 하나로 쭉
            foreach (var (ry, name) in new[] { (0.92f, "RailTop"), (0.5f, "RailMid") })
            {
                var rail = AddBox(parent, $"{name}_{mid.x:0}_{mid.z:0}", mid + new Vector3(0f, ry, 0f), new Vector3(0.08f, 0.1f, len), mat);
                rail.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
                Object.DestroyImmediate(rail.GetComponent<Collider>());
                rail.isStatic = true;
            }
            // 기둥 — 3m 간격(양 끝 포함), 머리 살짝 도톰
            int posts = Mathf.Max(2, Mathf.RoundToInt(len / 3f) + 1);
            for (int i = 0; i < posts; i++)
            {
                var p = Vector3.Lerp(from, to, i / (float)(posts - 1));
                var post = AddBox(parent, $"Post_{p.x:0}_{p.z:0}", p + new Vector3(0f, 0.5f, 0f), new Vector3(0.22f, 1f, 0.22f), mat);
                Object.DestroyImmediate(post.GetComponent<Collider>());
                post.isStatic = true;
                var cap = AddBox(parent, $"PostCap_{p.x:0}_{p.z:0}", p + new Vector3(0f, 1.04f, 0f), new Vector3(0.3f, 0.08f, 0.3f), mat);
                Object.DestroyImmediate(cap.GetComponent<Collider>());
                cap.isStatic = true;
            }
        }

        /// <summary>둘레 링을 짧은 박스 세그먼트(접선 정렬, 5% 겹침)로 두른다. 콜라이더 없음 — 순수 비주얼.</summary>
        private static void RingOfBoxes(GameObject grp, string prefix, float radius, float width, float y, float height, Material a, Material b)
        {
            int segs = Mathf.CeilToInt(2f * Mathf.PI * radius / 4.4f);
            for (int i = 0; i < segs; i++)
            {
                float ang = i / (float)segs * Mathf.PI * 2f;
                var pos = kLakeCenter + new Vector3(Mathf.Cos(ang) * radius, y, Mathf.Sin(ang) * radius);
                var seg = AddBox(grp, $"{prefix}{i}", pos, new Vector3(4.7f, height, width), i % 2 == 0 ? a : b);
                seg.transform.localRotation = Quaternion.Euler(0f, -(ang * Mathf.Rad2Deg + 90f), 0f);   // 접선 정렬
                Object.DestroyImmediate(seg.GetComponent<Collider>());
                seg.isStatic = true;
            }
        }

        private static void PlaceGateTower(GameObject root, Vector3 centerPos, float scale = 1.3f)
        {
            var tower = AssetDatabase.LoadAssetAtPath<GameObject>($"{kDir}/롯데_코너타워_Fit.prefab");
            if (tower == null) return;

            var half = 0.5f * scale;
            var t = (GameObject)PrefabUtility.InstantiatePrefab(tower, root.transform);
            t.name = "GateTower";
            t.transform.localPosition = centerPos + new Vector3(-half, 0f, -half);
            t.transform.localScale *= scale;

            // 기대 서기용 콜라이더(몸통만) — 다리 옆 장식이지만 부딪히면 통과 안 되게
            var bc = t.AddComponent<BoxCollider>();
            bc.center = new Vector3(0.5f, 2f, 0.5f);
            bc.size = new Vector3(1f, 4f, 1f);
        }

        // VARCO 배경 소품(_Fit 프리팹, 바닥 피벗) 배치 시도 — 모델을 적용해둔 경우에만 true.
        private static bool TryPlaceProp(GameObject root, string name, Vector3 groundPos, float yRot = 0f, float scale = 1f)
            => PlaceProp(root, name, groundPos, yRot, scale) != null;

        // 인스턴스가 필요한 경우(앰비언트 연출 연결 등)용 — 없으면 null.
        // addCollider=false는 '통과 가능해야 하는' 큰 소품(모노레일 링·다리·승강 원반)에 쓴다:
        // 바운즈 박스 콜라이더가 통짜로 잡히면 맵 절반을 막아버린다.
        // ── 사용자 프리팹 손조정 보존 ──
        // Generate는 배경을 매번 처음부터 새로 굽기 때문에, 사용자가 프리팹에서 직접 키운
        // 롯데월드타워·모서리 고깔탑 스케일 같은 조정이 재생성 때마다 사라졌다(QA "자꾸 누락").
        // 재생성 전에 기존 프리팹의 '롯데_*' 소품 TRS를 실측해 두고, 재생성 후 같은 이름의
        // 최근접(XZ 6m) 소품에 그대로 재적용한다 — 모노레일 기둥 마커 보존과 같은 어법.
        private struct PropTweak { public string Name; public Vector3 Pos; public Quaternion Rot; public Vector3 Scale; public bool Used; }

        private static List<PropTweak> CaptureUserPropTweaks()
        {
            var list = new List<PropTweak>();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(kPrefabPath);
            if (prefab == null) return list;
            foreach (Transform t in prefab.transform)
                if (t.name.StartsWith("롯데_"))
                    list.Add(new PropTweak { Name = t.name, Pos = t.localPosition, Rot = t.localRotation, Scale = t.localScale });
            return list;
        }

        private static void ApplyUserPropTweaks(GameObject root, List<PropTweak> tweaks)
        {
            if (tweaks.Count == 0) return;
            int applied = 0;
            foreach (Transform t in root.transform)
            {
                if (!t.name.StartsWith("롯데_")) continue;
                int best = -1; float bestD = 6f;
                for (int i = 0; i < tweaks.Count; i++)
                {
                    if (tweaks[i].Used || tweaks[i].Name != t.name) continue;
                    float d = Vector2.Distance(new Vector2(t.localPosition.x, t.localPosition.z),
                                               new Vector2(tweaks[i].Pos.x, tweaks[i].Pos.z));
                    if (d < bestD) { bestD = d; best = i; }
                }
                if (best < 0) continue;
                var tw = tweaks[best]; tw.Used = true; tweaks[best] = tw;
                t.localPosition = tw.Pos;
                t.localRotation = tw.Rot;
                t.localScale = tw.Scale;
                applied++;
            }
            if (applied > 0) Debug.Log($"[롯데월드] 사용자 프리팹 손조정 보존 — 소품 {applied}개 TRS 재적용");
        }

        private static GameObject PlaceProp(GameObject root, string name, Vector3 groundPos,
                                            float yRot = 0f, float scale = 1f, bool addCollider = true)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{kDir}/{name}_Fit.prefab");
            if (prefab == null) return null;
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
            inst.transform.localPosition = groundPos;
            inst.transform.localRotation = Quaternion.Euler(0f, yRot, 0f);
            if (scale != 1f) inst.transform.localScale *= scale;
            // 서 있을 수 있게 콜라이더 보장(모델엔 보통 없음) — 모양 그대로 메시 콜라이더.
            // 바운즈 박스는 나무·곡면 모델에서 모양 밖 허공까지 막는 투명벽이 된다(DDP와 같은 처리).
            if (addCollider && inst.GetComponentInChildren<Collider>() == null)
                foreach (var mf in inst.GetComponentsInChildren<MeshFilter>())
                    if (mf.sharedMesh != null)
                        mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
            return inst;
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
