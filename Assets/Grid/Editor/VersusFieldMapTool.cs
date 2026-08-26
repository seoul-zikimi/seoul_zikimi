using System.IO;
using UnityEditor;
using UnityEngine;

namespace GridSystem.EditorTools
{
    /// <summary>
    /// 공터(평지) 2vs2 맵 원클릭 생성 — 배경 신경 안 쓰고 대결만 하는 경기장.
    /// 평평한 바닥 + 울타리 + Spot 마커 5종 + VersusSymmetric(자동 복제 끔)을 프리팹으로 만들고
    /// MapDef 생성·정답(광통교) 연결·썸네일 촬영·카탈로그 등록까지 한 번에 한다.
    /// 몇 번을 다시 실행해도 같은 결과(기존 에셋 덮어쓰기) — 배치를 고친 뒤 재실행해도 안전.
    /// </summary>
    public static class VersusFieldMapTool
    {
        private const string kPrefabPath = "Assets/Map/Prefabs/MapBg_VersusField.prefab";
        private const string kMapDefPath = "Assets/Map/Maps/Map_VersusField.asset";
        private const string kThumbPath  = "Assets/Map/Maps/Thumb_VersusField.png";
        private const string kMatDir     = "Assets/Map/Materials";
        private const string kCatalogPath = "Assets/Resources/MapCatalog.asset";
        private const string kAnswerPath  = "Assets/Grid/Data/1_광통교_Answer.asset";
        private const string kSourceDefPath = "Assets/Map/Maps/Map_GwangTongGyo.asset";   // 주문 재료 목록 재사용 원본

        // 한 팀 구역 크기 — 광통교 정답(16×8×16)이 꼭 맞게 들어가는 크기. 2vs2에선 X가 2배(32)가 된다.
        private static readonly Vector3Int kZone = new Vector3Int(16, 8, 16);

        [MenuItem("Tools/Map/★ 공터 2vs2 맵 생성 (광통교 정답)")]
        public static void Generate()
        {
            var answer = AssetDatabase.LoadAssetAtPath<MapAnswerData>(kAnswerPath);
            if (answer == null) { Debug.LogError($"[VersusField] 정답 에셋이 없음: {kAnswerPath}"); return; }

            // ① 배경 프리팹(바닥+울타리+마커) — 임시로 씬에 조립해 프리팹으로 저장 후 제거
            var root = BuildFieldRoot();
            Directory.CreateDirectory(Path.GetDirectoryName(kPrefabPath));
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, kPrefabPath, out bool ok);
            Object.DestroyImmediate(root);
            if (!ok) { Debug.LogError($"[VersusField] 프리팹 저장 실패: {kPrefabPath}"); return; }

            // ② MapDef — 정답·재료는 광통교 것을 그대로 재사용(공터는 배경만 다른 맵)
            Directory.CreateDirectory(Path.GetDirectoryName(kMapDefPath));
            var def = AssetDatabase.LoadAssetAtPath<MapDef>(kMapDefPath);
            if (def == null)
            {
                def = ScriptableObject.CreateInstance<MapDef>();
                AssetDatabase.CreateAsset(def, kMapDefPath);
            }
            var so = new SerializedObject(def);
            so.FindProperty("m_DisplayName").stringValue = "공터 대결장";
            so.FindProperty("m_BackgroundPrefab").objectReferenceValue = prefab;
            so.FindProperty("m_GridSize").vector3IntValue = kZone;

            var answers = so.FindProperty("m_Answers");
            answers.arraySize = 1;
            answers.GetArrayElementAtIndex(0).objectReferenceValue = answer;

            // 주문 가능 재료 = 광통교 맵과 동일(같은 정답이니 같은 재료가 필요하다)
            var srcDef = AssetDatabase.LoadAssetAtPath<MapDef>(kSourceDefPath);
            var mats = so.FindProperty("m_AvailableMaterials");
            if (srcDef != null && srcDef.AvailableMaterials.Count > 0)
            {
                mats.arraySize = srcDef.AvailableMaterials.Count;
                for (int i = 0; i < srcDef.AvailableMaterials.Count; i++)
                    mats.GetArrayElementAtIndex(i).objectReferenceValue = srcDef.AvailableMaterials[i];
            }
            else mats.arraySize = 0;   // 원본이 없으면 카탈로그 전체 주문 가능

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(def);

            // ②-b 썸네일(로비 맵 카드용) — 매 실행마다 재촬영(배치를 바꿔 재실행해도 최신 유지)
            var thumb = CaptureThumbnail(prefab, kThumbPath);
            if (thumb != null)
            {
                so.Update();
                so.FindProperty("m_Thumbnail").objectReferenceValue = thumb;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            // ③ 카탈로그 등록(이미 있으면 그대로)
            var catalog = AssetDatabase.LoadAssetAtPath<MapCatalog>(kCatalogPath);
            if (catalog == null) { Debug.LogError($"[VersusField] MapCatalog이 없음: {kCatalogPath}"); return; }
            catalog.EditorAdd(def);
            EditorUtility.SetDirty(catalog);

            AssetDatabase.SaveAssets();
            Selection.activeObject = def;
            Debug.Log($"[VersusField] 완료 ✔\n프리팹: {kPrefabPath}\nMapDef: {kMapDefPath} (구역 {kZone.x}×{kZone.y}×{kZone.z}, 2vs2 전체 가로 {kZone.x * 2})\n정답: {kAnswerPath}\n카탈로그 등록 완료 — 로비에서 '공터 대결장'을 고르면 됩니다.");
        }

        // 공터 배경 조립. 좌표 기준: Spot_GridManager = (0,0,0) → 2vs2 그리드가 x∈[0,32), z∈[0,16)을 덮는다.
        // 분할벽 중심(점대칭 피벗)은 (16, *, 8) — 바닥·울타리를 이 점 기준 대칭으로 두어 VersusSymmetric 규약을 지킨다.
        private static GameObject BuildFieldRoot()
        {
            var root = new GameObject("MapBg_VersusField");

            // "이미 대칭으로 만든 전용 맵" 표시 — 런타임 자동 복제(180° 회전 사본)를 끈다.
            new GameObject(VersusBackground.kSymmetricMarker).transform.SetParent(root.transform, false);

            var ground = AddBox(root, "Ground", new Vector3(16f, -0.5f, 8f), new Vector3(56f, 1f, 40f),
                EnsureMaterial("Mat_FieldGround", new Color(0.72f, 0.66f, 0.52f)));   // 흙바닥 톤
            ground.isStatic = true;

            // 울타리 — 밖으로 떨어지지 않게 낮은 경계벽(피벗 점대칭 배치)
            var fenceMat = EnsureMaterial("Mat_FieldFence", new Color(0.45f, 0.40f, 0.35f));
            AddBox(root, "Fence_N", new Vector3(16f, 0.75f, 27.75f), new Vector3(56f, 1.5f, 0.5f), fenceMat).isStatic = true;
            AddBox(root, "Fence_S", new Vector3(16f, 0.75f, -11.75f), new Vector3(56f, 1.5f, 0.5f), fenceMat).isStatic = true;
            AddBox(root, "Fence_W", new Vector3(-11.75f, 0.75f, 8f), new Vector3(0.5f, 1.5f, 39f), fenceMat).isStatic = true;
            AddBox(root, "Fence_E", new Vector3(43.75f, 0.75f, 8f), new Vector3(0.5f, 1.5f, 39f), fenceMat).isStatic = true;

            // Spot 마커 5종 — 팀A 쪽에만 두면 작업대는 반대편에 자동 생성, 배송은 팀별 점대칭 배송(MaterialDepot).
            AddSpot(root, "Spot_GridManager", new Vector3(0f, 0f, 0f));          // 그리드 시작(팀A 왼쪽 아래)
            AddSpot(root, "Spot_PlayerSpawnPoint", new Vector3(8f, 0f, -4f));    // 협동 모드용(2vs2는 구역 중앙 자동)
            AddSpot(root, "Spot_DeliveryZone", new Vector3(4f, 0f, -5f));        // 재료 배송 자리(그리드 앞)
            AddSpot(root, "Spot_HammerStation", new Vector3(-4f, 0f, 5f));       // 그리드 왼쪽 옆(마커 Y = 접지점)
            AddSpot(root, "Spot_PaintStation", new Vector3(-4f, 0f, 11f));       // 〃
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

        // URP Lit 단색 머티리얼 에셋(없으면 생성, 있으면 색만 갱신) — 프리팹이 런타임 생성 머티리얼에 의존하지 않게.
        private static Material EnsureMaterial(string name, Color color)
        {
            Directory.CreateDirectory(kMatDir);
            string path = $"{kMatDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                var sh = Shader.Find("Universal Render Pipeline/Lit");
                if (sh == null) { Debug.LogWarning("[VersusField] URP Lit 셰이더를 못 찾음 — 기본 머티리얼 사용"); return null; }
                mat = new Material(sh);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetColor("_BaseColor", color);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // MapExtractTool.CaptureThumbnail과 같은 방식(프리팹을 지하에 세워 촬영) — 그쪽은 private라 최소 복제.
        private static Sprite CaptureThumbnail(GameObject prefab, string pngPath)
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
                imp.spriteImportMode = SpriteImportMode.Single;   // Multiple로 잡히면 스프라이트 서브에셋이 안 생긴다
                imp.SaveAndReimport();
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(pngPath);
                if (sprite == null)   // 배치 모드에선 리임포트 직후 메인 로드가 빌 수 있다 — 서브에셋에서 직접 찾는다
                    foreach (var a in AssetDatabase.LoadAllAssetsAtPath(pngPath))
                        if (a is Sprite s) { sprite = s; break; }
                if (sprite == null) Debug.LogWarning($"[VersusField] 썸네일 스프라이트 로드 실패: {pngPath} — MapDef에 수동 연결 필요");
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
