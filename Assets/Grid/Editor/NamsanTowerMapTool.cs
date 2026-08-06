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
        private const string kPrefabPath  = "Assets/Map/Prefabs/MapBg_NamsanTower.prefab";
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
            new Part{ Name="남산_기반",           Id=12, Fp=new Vector3Int(5,1,5), Proc=ProcessType.Fixed,   Color=new Color(0.96f,0.75f,0.78f), MustFix=true },
            new Part{ Name="남산_하부기둥",       Id=13, Fp=new Vector3Int(1,2,1), Proc=ProcessType.None,    Color=new Color(0.98f,0.85f,0.55f), MustFix=true },
            new Part{ Name="남산_철제받침기둥",   Id=14, Fp=new Vector3Int(1,2,1), Proc=ProcessType.None,    Color=new Color(0.98f,0.66f,0.18f), MustFix=true },
            new Part{ Name="남산_철제전망대",     Id=15, Fp=new Vector3Int(3,2,3), Proc=ProcessType.Fixed,   Color=new Color(0.72f,0.90f,0.12f), MustFix=true },
            new Part{ Name="남산_전망대받침",     Id=16, Fp=new Vector3Int(3,1,3), Proc=ProcessType.None,    Color=new Color(0.80f,0.94f,0.45f), MustFix=true },
            new Part{ Name="남산_하부안테나_빨강", Id=17, Fp=new Vector3Int(1,2,1), Proc=ProcessType.Painted, Color=new Color(0.99f,0.45f,0.42f), MustFix=true },
            new Part{ Name="남산_하부안테나_하양", Id=18, Fp=new Vector3Int(1,2,1), Proc=ProcessType.Painted, Color=new Color(0.97f,0.97f,0.97f), MustFix=true },
            new Part{ Name="남산_상부안테나",     Id=19, Fp=new Vector3Int(1,2,1), Proc=ProcessType.Painted, Color=new Color(0.75f,0.05f,0.08f), MustFix=true },
            new Part{ Name="남산_최상부안테나",   Id=20, Fp=new Vector3Int(1,3,1), Proc=ProcessType.Painted, Color=new Color(0.80f,0.45f,0.95f), MustFix=false },
        };

        // ── 타워 조립(정답): (파츠 id, 앵커 셀). 하부기둥×4, 나머지×1 — 총 높이 23 ──
        private static readonly (int id, Vector3Int anchor)[] kTower =
        {
            (12, new Vector3Int(3, 0, 3)),    // 기반 5×5, y0
            (13, new Vector3Int(5, 1, 5)),    // 하부기둥 y1-2
            (13, new Vector3Int(5, 3, 5)),    //          y3-4
            (13, new Vector3Int(5, 5, 5)),    //          y5-6
            (13, new Vector3Int(5, 7, 5)),    //          y7-8
            (14, new Vector3Int(5, 9, 5)),    // 철제받침기둥 y9-10
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

            // ④ 실전 기믹 설정(기본값 = 기획 확정치: 전망대 y11~13, 밴드 10/15, 케이블카 3대…)
            var cfg = LoadOrCreate<NamsanGimmickConfig>(kConfigPath);
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

            // ⑦ 썸네일 + 맵 카탈로그
            var thumb = MapThumbnailUtil.Capture(prefab, kThumbPath);
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

        // ── 파츠 def + 색큐브 프리팹(피벗 min-corner, 규약 준수) ──
        private static MaterialDef EnsurePartDef(Part p)
        {
            // 프리팹: 루트(피벗=min-corner) + 자식 큐브(footprint 크기, 파츠 색)
            string prefabPath = $"{kDir}/{p.Name}.prefab";
            var rootGo = new GameObject(p.Name);
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "cube";
            cube.transform.SetParent(rootGo.transform, false);
            cube.transform.localPosition = new Vector3(p.Fp.x * 0.5f, p.Fp.y * 0.5f, p.Fp.z * 0.5f);
            cube.transform.localScale = new Vector3(p.Fp.x, p.Fp.y, p.Fp.z) * 0.97f;
            var mat = EnsureMaterial($"Mat_{p.Name}", p.Color);
            if (mat != null) cube.GetComponent<Renderer>().sharedMaterial = mat;
            var prefab = PrefabUtility.SaveAsPrefabAsset(rootGo, prefabPath);
            Object.DestroyImmediate(rootGo);

            var def = LoadOrCreate<MaterialDef>($"{kDir}/{p.Name}_Def.asset");
            var so = new SerializedObject(def);
            so.FindProperty("m_Id").intValue = p.Id;
            so.FindProperty("m_Footprint").vector3IntValue = p.Fp;
            so.FindProperty("m_Prefab").objectReferenceValue = prefab;
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
        // 좌표 기준: Spot_GridManager=(0,0,0), 그리드 x,z∈[0,11) 데크 위. 데크 상판 y=0, 땅 y=-2.
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

            // 나무 데크(윗동네) — 얇은 상판 + 아래는 뚫려 있어(로비 공간) 남쪽 가장자리로 들어갈 수 있다
            AddBox(root, "Deck", new Vector3(0f, -0.25f, 5f), new Vector3(36f, 0.5f, 22f), wood).isStatic = true;
            // 데크 지지 기둥(장식 겸 로비 느낌)
            AddBox(root, "DeckPost_W", new Vector3(-16f, -1.25f, 5f), new Vector3(1f, 1.5f, 20f), wood).isStatic = true;
            AddBox(root, "DeckPost_N", new Vector3(0f, -1.25f, 15f), new Vector3(34f, 1.5f, 1f), wood).isStatic = true;

            // 땅(아랫동네·산꼭대기 풀밭) — 데크 아래까지 깔린다
            AddBox(root, "Ground", new Vector3(0f, -2.5f, -4f), new Vector3(48f, 1f, 42f), grass).isStatic = true;

            // 계단(나무): 땅(-2) ↔ 데크(0), 서쪽
            AddBox(root, "Stair1", new Vector3(-12f, -1.75f, -6.7f), new Vector3(3.4f, 0.5f, 1.5f), wood).isStatic = true;
            AddBox(root, "Stair2", new Vector3(-12f, -1.25f, -7.6f), new Vector3(3.4f, 0.5f, 1.5f), wood).isStatic = true;
            AddBox(root, "Stair3", new Vector3(-12f, -0.75f, -8.5f), new Vector3(3.4f, 0.5f, 1.5f), wood).isStatic = true;

            // 사랑의 자물쇠 벽 + 하트동상(데크 서쪽)
            AddBox(root, "LockWall", new Vector3(-14f, 1f, 6f), new Vector3(0.5f, 2f, 7f), pinkW).isStatic = true;
            var heart = AddBox(root, "HeartStatue", new Vector3(-13.4f, 2.4f, 6f), new Vector3(0.9f, 0.9f, 0.5f), red);
            heart.transform.rotation = Quaternion.Euler(0f, 0f, 45f);

            // 전망대(배경 존, 데크 북서쪽 살짝 높은 단)
            AddBox(root, "ViewDeck", new Vector3(-13f, 0.4f, 13f), new Vector3(7f, 0.8f, 5f), wood).isStatic = true;

            // 팔각정(아랫동네) — 몸통 + 큰 지붕
            AddBox(root, "Palgak_Body", new Vector3(-4f, -1.5f, -16f), new Vector3(4.5f, 1.2f, 4.5f), stone).isStatic = true;
            AddBox(root, "Palgak_Roof", new Vector3(-4f, -0.6f, -16f), new Vector3(6f, 0.7f, 6f), green).isStatic = true;

            // 케이블카존(아랫동네 동쪽) — 하차장 앞 낮은 단 + 산 아래 출발 바위
            AddBox(root, "CableDock", new Vector3(12f, -2.1f, -12f), new Vector3(5f, 0.3f, 5f), stone).isStatic = true;
            AddBox(root, "CableRock", new Vector3(20f, -5f, -22f), new Vector3(6f, 4f, 6f), stone).isStatic = true;

            // 데크 밑 엘베 로비(케이블카존에서 오른쪽으로 꺾어 들어감)
            AddBox(root, "LobbyBuilding", new Vector3(12f, -1.2f, -3f), new Vector3(5f, 1.6f, 4f), dark).isStatic = true;

            // 엘베 상부 도착 발판(전망대 옆 공중 — 도착해서 설 자리, 받침(y13) 윗면과 같은 높이)
            AddBox(root, "UpperPlatform", new Vector3(8.7f, 13.75f, 5.5f), new Vector3(2.8f, 0.5f, 2.8f), wood).isStatic = true;

            // ── 마커 8종 ──
            AddSpot(root, "Spot_GridManager", new Vector3(0f, 0f, 0f));               // 짓는 곳(데크 동쪽)
            AddSpot(root, "Spot_PlayerSpawnPoint", new Vector3(2f, -2f, -12f));        // 아랫동네(팔각정·케이블카 사이)
            AddSpot(root, "Spot_HammerStation", new Vector3(-0.5f, -2f, -15f));        // 팔각정 옆(지상 가공)
            AddSpot(root, "Spot_PaintStation", new Vector3(-9f, 0f, 2f));              // 데크 위
            AddSpot(root, "Spot_CableCarStation", new Vector3(12f, -1.95f, -12f));     // 하차장(낮은 단 위)
            AddSpot(root, "Spot_CableCarOrigin", new Vector3(20f, -3f, -22f));         // 산 아래 출발점(바위 위)
            AddSpot(root, "Spot_ElevatorLower", new Vector3(12f, -2f, -5.8f));         // 로비 건물 앞(땅)
            AddSpot(root, "Spot_ElevatorUpper", new Vector3(8.7f, 14f, 5.5f));         // 공중 발판 위
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
                if (sh == null) { Debug.LogWarning("[남산타워] URP Lit 셰이더를 못 찾음"); return null; }
                mat = new Material(sh);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetColor("_BaseColor", color);
            EditorUtility.SetDirty(mat);
            return mat;
        }
    }
}
