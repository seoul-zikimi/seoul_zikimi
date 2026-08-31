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
        private const string kPrefabPath = "Assets/Resources/MapPrefabs/MapBg_VersusField.prefab";   // MapDef 지연 로드 규약(Resources 필수)
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

            // 바닥을 울타리 밖까지 넓혀 장식 소품을 놓을 앞치마(apron) 확보(56x40 → 64x52).
            var ground = AddBox(root, "Ground", new Vector3(16f, -0.5f, 8f), new Vector3(64f, 1f, 52f),
                EnsureMaterial("Mat_FieldGround", new Color(0.72f, 0.66f, 0.52f)));   // 흙바닥 톤
            ground.isStatic = true;

            // 울타리 — 밖으로 떨어지지 않게 낮은 경계벽(피벗 점대칭 배치).
            // 골판 철판+나무 지지대 텍스처(Tex_FieldFence, 16:9)를 씌운다.
            // 1타일 폭 = 벽 높이 x 16/9 — 벽 길이가 달라 N/S와 E/W 머티리얼 분리.
            const float kFenceTileWidth = 1.5f * 16f / 9f;
            var fenceNS = EnsureFenceMaterial("Mat_FieldFenceNS", Mathf.Round(56f / kFenceTileWidth));
            var fenceEW = EnsureFenceMaterial("Mat_FieldFenceEW", Mathf.Round(39f / kFenceTileWidth));
            AddBox(root, "Fence_N", new Vector3(16f, 0.75f, 27.75f), new Vector3(56f, 1.5f, 0.08f), fenceNS).isStatic = true;
            AddBox(root, "Fence_S", new Vector3(16f, 0.75f, -11.75f), new Vector3(56f, 1.5f, 0.08f), fenceNS).isStatic = true;
            AddBox(root, "Fence_W", new Vector3(-11.75f, 0.75f, 8f), new Vector3(0.08f, 1.5f, 39f), fenceEW).isStatic = true;
            AddBox(root, "Fence_E", new Vector3(43.75f, 0.75f, 8f), new Vector3(0.08f, 1.5f, 39f), fenceEW).isStatic = true;
            AddFenceWoodwork(root);   // 나무 기둥·레일은 텍스처가 아닌 실제 지오메트리(양감)

            // 중앙 분할 — 외곽과 같은 골판벽(높이 동일) + 전 구간 투명 차단 콜라이더.
            // 런타임 ~VersusWall(GridManager)은 그리드 구간만 덮어 양 끝으로 돌아갈 수 있으므로
            // 울타리 안쪽 전체(z 39m)를 막는다. 차단벽은 렌더러 없음(투명), 높이 8m(점프 방지).
            AddBox(root, "Divider", new Vector3(16f, 0.75f, 8f), new Vector3(0.08f, 1.5f, 39f), fenceEW).isStatic = true;
            AddDividerWoodwork(root);   // 외곽과 같은 나무 기둥·레일(통일감)

            // 투명 차단벽 5장(중앙 + 외곽 4면, 높이 100m) — 인게임 검수로 확정한 좌표(펜스보다 살짝 안쪽).
            void Blocker(string name, Vector3 pos, Vector3 size)
            {
                var go = new GameObject(name);
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = pos;
                go.AddComponent<BoxCollider>().size = size;
                go.isStatic = true;
            }
            Blocker("DividerBlocker",       new Vector3(16f, 4f, 8f),      new Vector3(0.3f, 100f, 39f));
            Blocker("DividerBlocker_Front", new Vector3(43.33f, 4f, 8f),   new Vector3(0.3f, 100f, 39f));
            Blocker("DividerBlocker_Back",  new Vector3(-11.5f, 4f, 8f),   new Vector3(0.3f, 100f, 39f));
            Blocker("DividerBlocker_Right", new Vector3(16f, 4f, 27.5f),   new Vector3(100f, 100f, 0.3f));
            Blocker("DividerBlocker_Left",  new Vector3(16f, 4f, -11.4f),  new Vector3(100f, 100f, 0.3f));

            // Spot 마커 5종 — 팀A 쪽에만 두면 작업대는 반대편에 자동 생성, 배송은 팀별 점대칭 배송(MaterialDepot).
            AddSpot(root, "Spot_GridManager", new Vector3(0f, 0f, 0f));          // 그리드 시작(팀A 왼쪽 아래)
            AddSpot(root, "Spot_PlayerSpawnPoint", new Vector3(8f, 0f, -4f));    // 협동 모드용(2vs2는 구역 중앙 자동)
            AddSpot(root, "Spot_DeliveryZone", new Vector3(4f, 0f, -5f));        // 재료 배송 자리(그리드 앞)
            AddSpot(root, "Spot_HammerStation", new Vector3(-4f, 0f, 5f));       // 그리드 왼쪽 옆(마커 Y = 접지점)
            AddSpot(root, "Spot_PaintStation", new Vector3(-4f, 0f, 11f));       // 〃

            DecorateProps(root);   // 공사장 소품(울타리 밖 앞치마) — 코스메틱, 콜라이더 없음
            AddSunsetHorizon(root);   // 원경(노을 마을 카드 링 + 대형 바닥) + 노을 하늘 전환기
            return root;
        }

        // ── 공사장 장식 소품 배치 ─────────────────────────────────────
        // 전부 울타리 '밖' 앞치마 위(플레이 동선 영향 0). 분할벽 피벗(16, *, 8) 기준
        // 점대칭 쌍으로 놓아 양 팀 시야가 공평하게 보이도록 한다.
        private const string kPropsDir = "Assets/Map/03_VersusField/Props";

        private static void DecorateProps(GameObject root)
        {
            var deco = new GameObject("Decorations");
            deco.transform.SetParent(root.transform, false);

            // (프롭, 위치 x/z, y회전, 목표 크기 m) — 대칭 사본은 자동 생성.
            // 목표 크기 = 가장 긴 수평 변 길이. FBX 임포트 단위가 들쭉날쭉해서(cm 등)
            // 배치 시 렌더러 바운드를 재서 그 크기로 정규화한다.
            // 레퍼런스처럼 '클러스터'로 뭉쳐 빼곡하게 — 북쪽 스트립(z 29~33) / 남쪽은 대칭 자동.
            PlacePair(deco, "Prop_PalletBuilt.prefab", new Vector2(-10f, 30.5f),  15f, 3.2f);
            PlacePair(deco, "Prop_PalletBuilt.prefab", new Vector2(-7.2f, 32.5f), -40f, 2.6f);
            PlacePair(deco, "Prop_BlockPile.fbx",      new Vector2(-3.5f, 31f),  -10f, 3.4f);
            PlacePair(deco, "Prop_BlockPile.fbx",      new Vector2(-1f, 33f),     70f, 2.6f);
            PlacePair(deco, "Prop_Cone.fbx",           new Vector2(-5.5f, 29.3f),  0f, 1.4f);
            PlacePair(deco, "Prop_Barricade.fbx",      new Vector2(4f, 30f),       5f, 3.0f);
            PlacePair(deco, "Prop_Cone.fbx",           new Vector2(6.5f, 29.2f),   0f, 1.4f);
            PlacePair(deco, "Prop_PalletBuilt.prefab", new Vector2(10f, 31.5f),   80f, 3.0f);
            PlacePair(deco, "Prop_BlockPile.fbx",      new Vector2(14f, 30f),     20f, 3.0f);
            PlacePair(deco, "Prop_Cone.fbx",           new Vector2(16f, 32.7f),    0f, 1.4f);
            PlacePair(deco, "Prop_Barricade.fbx",      new Vector2(20f, 31f),    -12f, 3.0f);
            PlacePair(deco, "Prop_PalletBuilt.prefab", new Vector2(25f, 30.4f),   10f, 3.2f);
            PlacePair(deco, "Prop_BlockPile.fbx",      new Vector2(29f, 32f),    -35f, 3.2f);
            PlacePair(deco, "Prop_Cone.fbx",           new Vector2(31f, 29.5f),    0f, 1.4f);
            PlacePair(deco, "Prop_PalletBuilt.prefab", new Vector2(38f, 31f),    -20f, 3.2f);
            PlacePair(deco, "Prop_BlockPile.fbx",      new Vector2(42f, 30f),     55f, 3.0f);
            PlacePair(deco, "Prop_Cone.fbx",           new Vector2(44f, 32.5f),    0f, 1.4f);

            // 동·서 사이드 스트립(x -16.5~-13 / 대칭 자동)
            PlacePair(deco, "Prop_Cone.fbx",           new Vector2(-14f, 22f),    30f, 1.4f);
            PlacePair(deco, "Prop_PalletBuilt.prefab", new Vector2(-15f, 17f),    95f, 2.8f);
            PlacePair(deco, "Prop_Barricade.fbx",      new Vector2(-14.2f, 12f),  90f, 3.0f);
            PlacePair(deco, "Prop_BlockPile.fbx",      new Vector2(-15f, 6f),     50f, 3.2f);
            PlacePair(deco, "Prop_PalletBuilt.prefab", new Vector2(-14.8f, 0f),   85f, 2.6f);
            PlacePair(deco, "Prop_Cone.fbx",           new Vector2(-13.8f, -4f),   0f, 1.4f);
            PlacePair(deco, "Prop_BlockPile.fbx",      new Vector2(-15f, -8.5f), -25f, 2.8f);

            PlacePair(deco, "Prop_Excavator.fbx",      new Vector2(-14f, 32f),    55f, 6.5f);   // 북서 코너(반대편 대칭 자동)

            // 케이블 스풀 — 클러스터 사이사이(레퍼런스처럼 큼직하게)
            PlacePair(deco, "Prop_CableSpool.fbx",     new Vector2(-15.5f, 26f),  20f, 2.4f);
            PlacePair(deco, "Prop_CableSpool.fbx",     new Vector2(34f, 31.5f),  -60f, 2.6f);
            PlacePair(deco, "Prop_CableSpool.fbx",     new Vector2(-14.5f, -3f), 110f, 2.2f);

            // 컨테이너 사무실 — 북쪽 뒤편(대칭으로 남쪽에도 한 채)
            PlacePair(deco, "Prop_Container.fbx",      new Vector2(6f, 33.5f),   170f, 5.5f);
        }

        // 프롭 1개 + 분할벽 피벗(16, *, 8) 점대칭 사본 1개를 함께 배치.
        private static void PlacePair(GameObject parent, string file, Vector2 xz, float yaw, float targetSize)
        {
            Place(parent, file, xz, yaw, targetSize);
            Place(parent, file, new Vector2(32f - xz.x, 16f - xz.y), yaw + 180f, targetSize);
        }

        private static void Place(GameObject parent, string file, Vector2 xz, float yaw, float targetSize)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{kPropsDir}/{file}");
            if (prefab == null)
            {
                Debug.LogWarning($"[VersusField] 장식 프롭 없음: {kPropsDir}/{file} — 건너뜀");
                return;
            }
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            inst.transform.SetParent(parent.transform, false);
            inst.transform.localPosition = new Vector3(xz.x, 0f, xz.y);
            // 곱셈 유지 — FBX 루트엔 축 보정 회전(-90° 등)이 베이크돼 있을 수 있어 덮어쓰면 눕는다.
            inst.transform.localRotation = Quaternion.Euler(0f, yaw, 0f) * inst.transform.localRotation;

            // FBX 모델 프리팹을 중첩 저장하면 File Scale이 재적용돼 크기가 틀어진다 —
            // 언팩해서 일반 메시 오브젝트로 굽는다(메시/머티리얼 에셋 참조는 유지됨).
            if (PrefabUtility.IsPartOfPrefabInstance(inst))
                PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

            // 임포트 단위 무관하게 '가장 긴 수평 변 = targetSize(m)'가 되도록 정규화
            var rends = inst.GetComponentsInChildren<Renderer>(true);
            if (rends.Length > 0)
            {
                var b = rends[0].bounds;
                for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
                float extent = Mathf.Max(b.size.x, b.size.z);
                if (extent > 0.0001f)
                    // 곱셈 유지 필수 — FBX 루트는 File Scale 보정(예: 100배)이 이미 걸려 있어
                    // 덮어쓰면 100분의 1로 쪼그라든다.
                    inst.transform.localScale *= targetSize / extent;
                // 정규화 후 바닥에 접지(피벗이 바닥이 아닌 프롭 대비)
                b = rends[0].bounds;
                for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
                var p = inst.transform.position;
                p.y += parent.transform.position.y - b.min.y;
                inst.transform.position = p;
            }

            foreach (var t in inst.GetComponentsInChildren<Transform>(true))
                t.gameObject.isStatic = true;
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

        // 울타리 나무 보강재 — 기둥(4m 간격)과 가로 레일을 실제 박스로 세운다.
        // 벽(0.08m)보다 두꺼워 양면으로 돌출되고, 기둥은 벽 위로 살짝 솟아 실루엣이 산다.
        // ── 원경(노을 마을) + 노을 하늘 ─────────────────────────────
        // 프로젝트 관례(MapVisualPolishTool의 ~Horizon 카드 링)를 따르되 이 맵 전용으로 깐다:
        // 키잉된 스카이라인 PNG를 언릿 알파컷 카드 8장으로 맵 둘레에 세우고, 그 밑을 대형 흙바닥으로 받친다.
        // 하늘·안개·앰비언트·태양은 SunsetAmbience가 맵 스폰 시에만 노을 톤으로 전환(언로드 시 원복).
        private static void AddSunsetHorizon(GameObject root)
        {
            var group = new GameObject("~Horizon");
            group.transform.SetParent(root.transform, false);

            // 대형 바닥(1km) — 카드 밑이 비어 보이지 않게. 콜라이더 불필요.
            var bigGround = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(bigGround.GetComponent<Collider>());
            bigGround.name = "HorizonGround";
            bigGround.transform.SetParent(group.transform, false);
            bigGround.transform.localPosition = new Vector3(16f, -0.55f, 8f);
            bigGround.transform.localScale = new Vector3(1000f, 1f, 1000f);
            bigGround.GetComponent<Renderer>().sharedMaterial = EnsureMaterial("Mat_VersusGrass", new Color(0.55f, 0.68f, 0.42f));   // 펜스 밖 = 풀밭(공사장 흙바닥과 대비)
            bigGround.isStatic = true;

            // 원경 = 넓은 흙바닥 + 울타리 밖 나무 벨트(빌보드 산포) — 실루엣/그림 카드는 부자연스러워 폐기.
            AddTreeBelt(group);

            // 하늘 = 노을 그라데이션
            var sky = EnsureSunsetGradientSky();
            var ambience = root.AddComponent<SunsetAmbience>();
            var so = new SerializedObject(ambience);
            so.FindProperty("m_SunsetSky").objectReferenceValue = sky;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // 울타리 밖 나무 벨트 — 기존 Horizon 나무 텍스처(TreeA_*.png)를 십자 빌보드로 산포.
        // 결정적 시드(재실행 동일), 플레이 영역(반경 42m) 안쪽은 비운다.
        private static void AddTreeBelt(GameObject parent)
        {
            var texs = new System.Collections.Generic.List<Texture2D>();
            foreach (var g in AssetDatabase.FindAssets("TreeA_ t:Texture2D", new[] { "Assets/Map/Horizon/Trees" }))
            {
                var t = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(g));
                if (t != null) texs.Add(t);
            }
            if (texs.Count == 0) { Debug.LogWarning("[VersusField] 나무 텍스처 없음 — 나무 벨트 생략"); return; }

            var mats = new Material[texs.Count];
            for (int i = 0; i < texs.Count; i++)
            {
                string matPath = $"{kMatDir}/Mat_VersusTree_{i}.mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (m == null)
                {
                    m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                    AssetDatabase.CreateAsset(m, matPath);
                }
                m.SetTexture("_BaseMap", texs[i]);
                m.SetColor("_BaseColor", new Color(0.62f, 0.52f, 0.40f));   // 노을 역광 톤
                m.SetFloat("_AlphaClip", 1f);
                m.SetFloat("_Cutoff", 0.5f);
                m.EnableKeyword("_ALPHATEST_ON");
                m.SetFloat("_Cull", 0f);   // 양면(빌보드 뒷면도 보이게)
                EditorUtility.SetDirty(m);
                mats[i] = m;
            }

            var beltRoot = new GameObject("TreeBelt");
            beltRoot.transform.SetParent(parent.transform, false);

            void Tree(float x, float z, float yaw, float w, float h, int matIdx)
            {
                var tree = new GameObject("Tree");
                tree.transform.SetParent(beltRoot.transform, false);
                tree.transform.localPosition = new Vector3(x, 0f, z);
                for (int q = 0; q < 2; q++)   // 십자 빌보드(2장 교차)
                {
                    var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    Object.DestroyImmediate(quad.GetComponent<Collider>());
                    quad.name = "Q" + q;
                    quad.transform.SetParent(tree.transform, false);
                    quad.transform.localPosition = new Vector3(0f, h * 0.5f - 0.2f, 0f);
                    quad.transform.localScale = new Vector3(w, h, 1f);
                    quad.transform.localRotation = Quaternion.Euler(0f, yaw + q * 90f, 0f);
                    quad.GetComponent<Renderer>().sharedMaterial = mats[Mathf.Clamp(matIdx, 0, mats.Length - 1)];
                    quad.isStatic = true;
                }
                tree.isStatic = true;
            }

            // 손으로 다듬은 배치가 있으면(TreeBelt.csv: x,z,yaw,width,height,matIdx) 그대로 재현 —
            // 툴 재실행이 수작업을 덮어쓰지 않게 하는 스냅샷. 지우면 아래 절차 배치로 새로 깐다.
            string csvPath = "Assets/Map/03_VersusField/TreeBelt.csv";
            if (System.IO.File.Exists(csvPath))
            {
                foreach (string line in System.IO.File.ReadAllLines(csvPath))
                {
                    var p = line.Split(',');
                    if (p.Length < 6) continue;
                    Tree(float.Parse(p[0]), float.Parse(p[1]), float.Parse(p[2]),
                         float.Parse(p[3]), float.Parse(p[4]), int.Parse(p[5]));
                }
                return;
            }

            // (폴백) 절차 배치 — 펜스 근처 집중, 펜스 안쪽 금지
            var center = new Vector3(16f, 0f, 8f);
            var rng = new System.Random(123);
            const int kTrees = 340;
            int placed = 0, guard = 0;
            while (placed < kTrees && guard++ < kTrees * 10)
            {
                float ang = (float)(rng.NextDouble() * Mathf.PI * 2f);
                float r = 26f + 64f * Mathf.Pow((float)rng.NextDouble(), 1.5f);
                var pos = center + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * r;
                if (Mathf.Abs(pos.x - 16f) < 31f && Mathf.Abs(pos.z - 8f) < 23f)
                    continue;
                placed++;
                float h = 5f + (float)rng.NextDouble() * 6f + r * 0.01f;
                Tree(pos.x, pos.z, (float)(rng.NextDouble() * 180f), h * 0.9f, h, rng.Next(mats.Length));
            }
        }

        // 실루엣 PNG(흰색+알파, 가로 타일링)를 코드로 그린다. 이미 있으면 재사용(손그림 교체 가능).
        // hills=true: 완만한 능선 / false: 낮은 집들 + 타워크레인 몇 대(공사장 마을 윤곽).
        private static Texture2D EnsureSilhouette(string name, bool hills)
        {
            System.IO.Directory.CreateDirectory("Assets/Map/Horizon");
            string path = $"Assets/Map/Horizon/{name}.png";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null) return existing;

            const int W = 2048, H = 512;
            var heights = new float[W];
            var rng = new System.Random(hills ? 7 : 41);

            if (hills)
            {
                // 사인파 3겹 능선 — 양끝 연속(타일링)
                for (int x = 0; x < W; x++)
                {
                    float t = (float)x / W * Mathf.PI * 2f;
                    heights[x] = 0.35f + 0.18f * Mathf.Sin(t * 2f) + 0.10f * Mathf.Sin(t * 5f + 1.3f) + 0.06f * Mathf.Sin(t * 9f + 4.1f);
                }
            }
            else
            {
                // 낮은 집들(폭 30~90px, 높이 0.08~0.28) — 일부 지붕 경사
                int x = 0;
                while (x < W)
                {
                    int bw = 30 + rng.Next(60);
                    float bh = 0.08f + (float)rng.NextDouble() * 0.20f;
                    bool roof = rng.Next(2) == 0;
                    for (int i = 0; i < bw && x + i < W; i++)
                    {
                        float h = bh;
                        if (roof)   // 가운데가 솟은 맞배지붕
                        {
                            float c = Mathf.Abs(i - bw * 0.5f) / (bw * 0.5f);
                            h = bh + (1f - c) * 0.06f;
                        }
                        heights[x + i] = h;
                    }
                    x += bw + 2 + rng.Next(8);
                }
                for (int i = 0; i < 24; i++) heights[i] = heights[W - 24 + i] = heights[24];   // 이음새

                // 타워크레인 4대 — 마스트 + 지브(가로 팔) + 카운터지브
                foreach (int cx in new[] { 260, 780, 1300, 1800 })
                {
                    float mastH = 0.55f + (float)rng.NextDouble() * 0.15f;
                    for (int i = -4; i <= 4; i++)
                        if (cx + i >= 0 && cx + i < W) heights[cx + i] = Mathf.Max(heights[cx + i], mastH);
                }
            }

            var px = new Color32[W * H];
            for (int x = 0; x < W; x++)
            {
                int hPix = Mathf.RoundToInt(Mathf.Clamp01(heights[x]) * H);
                for (int y = 0; y < H; y++)
                    px[y * W + x] = y < hPix ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
            }
            // 크레인 지브(수평 팔) — 마을 실루엣에만, 마스트 상단에 가로선 덧그림
            if (!hills)
            {
                foreach (int cx in new[] { 260, 780, 1300, 1800 })
                {
                    int topY = Mathf.RoundToInt(0.55f * H);
                    for (int i = -30; i <= 110; i++)
                    {
                        int xi = cx + i;
                        if (xi < 0 || xi >= W) continue;
                        for (int t = 0; t < 6; t++)
                        {
                            int yi = topY + t;
                            if (yi < H) px[yi * W + xi] = new Color32(255, 255, 255, 255);
                        }
                        if (i == 100)   // 후크 줄
                            for (int d = 1; d < 26; d++)
                                if (topY - d >= 0) px[(topY - d) * W + xi] = new Color32(255, 255, 255, 255);
                    }
                }
            }

            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            tex.SetPixels32(px);
            tex.Apply();
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            ti.alphaIsTransparency = true;
            ti.wrapMode = TextureWrapMode.Repeat;
            ti.mipmapEnabled = true;
            ti.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // 실루엣 카드 링 — 언릿 알파컷, 텍스처 가로 1/3씩 3종 오프셋(옆 카드와 무늬 반복 회피).
        private static void BuildCardRing(GameObject parent, Texture2D tex, string label, float radius, float height, Color tint, int queue)
        {
            if (tex == null) return;
            var mats = new Material[3];
            for (int k = 0; k < 3; k++)
            {
                string matPath = $"{kMatDir}/Mat_Versus{label}_{k}.mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (m == null)
                {
                    m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                    AssetDatabase.CreateAsset(m, matPath);
                }
                m.SetTexture("_BaseMap", tex);
                m.SetColor("_BaseColor", tint);
                m.SetFloat("_AlphaClip", 1f);
                m.SetFloat("_Cutoff", 0.5f);
                m.EnableKeyword("_ALPHATEST_ON");
                m.renderQueue = queue;
                m.SetTextureScale("_BaseMap", new Vector2(1f / 3f, 1f));
                m.SetTextureOffset("_BaseMap", new Vector2(k / 3f, 0f));
                EditorUtility.SetDirty(m);
                mats[k] = m;
            }

            var center = new Vector3(16f, 0f, 8f);
            const int kCards = 12;
            float cardW = 2f * radius * Mathf.Sin(Mathf.PI / kCards) * 1.06f;   // 12분할 현보다 살짝 넓게(틈 방지)
            for (int i = 0; i < kCards; i++)
            {
                float ang = i * Mathf.PI * 2f / kCards;
                var pos = center + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * radius;
                var card = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Object.DestroyImmediate(card.GetComponent<Collider>());
                card.name = label + "Card";
                card.transform.SetParent(parent.transform, false);
                card.transform.localPosition = new Vector3(pos.x, height * 0.5f - 1f, pos.z);
                card.transform.localScale = new Vector3(cardW, height, 1f);
                card.transform.rotation = Quaternion.LookRotation(pos - center);
                card.GetComponent<Renderer>().sharedMaterial = mats[i % 3];
                card.isStatic = true;
            }
        }

        // 노을 그라데이션 스카이박스(마을 없음) — Pano_VersusSunset.png를 그라데이션으로 다시 굽는다.
        private static Material EnsureSunsetGradientSky()
        {
            const int W = 64, H = 1024;   // 가로 균일 그라데이션이라 가로 해상도는 최소로
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            var rows = new Color[H];
            var horizon = new Color(1.00f, 0.80f, 0.42f);   // 지평선: 밝은 노랑주황
            var mid     = new Color(0.98f, 0.55f, 0.28f);   // 중간: 주황
            var upper   = new Color(0.72f, 0.38f, 0.46f);   // 위: 분홍보라
            var top     = new Color(0.47f, 0.31f, 0.52f);   // 꼭대기: 보라
            for (int y = 0; y < H; y++)
            {
                float v = (float)y / H;   // 0=아래(지평선 밑), 1=천정
                Color c;
                if (v < 0.5f)      c = horizon;                                        // 지평선 아래는 지평선색 유지
                else if (v < 0.62f) c = Color.Lerp(horizon, mid, (v - 0.5f) / 0.12f);
                else if (v < 0.8f)  c = Color.Lerp(mid, upper, (v - 0.62f) / 0.18f);
                else                c = Color.Lerp(upper, top, (v - 0.8f) / 0.2f);
                rows[y] = c;
            }
            var px = new Color[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++) px[y * W + x] = rows[y];
            tex.SetPixels(px);
            tex.Apply();

            string texPath = "Assets/Map/Horizon/Pano_VersusSunset.png";
            System.IO.File.WriteAllBytes(texPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(texPath);

            string matPath = $"{kMatDir}/Sky_VersusPano.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Skybox/Panoramic"));
                AssetDatabase.CreateAsset(mat, matPath);
            }
            mat.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(texPath));
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // 중앙 분할벽용 나무 보강재 — 외곽 울타리와 같은 규격(기둥 4m 간격 + 관통 레일).
        private static void AddDividerWoodwork(GameObject root)
        {
            var wood = EnsureMaterial("Mat_FenceWood", new Color(0.48f, 0.35f, 0.25f));
            var parent = new GameObject("DividerWood");
            parent.transform.SetParent(root.transform, false);

            for (float z = -10f; z <= 26f; z += 4f)
                AddBox(parent, "Post", new Vector3(16f, 0.8f, z), new Vector3(0.18f, 1.6f, 0.18f), wood).isStatic = true;
            AddBox(parent, "Rail", new Vector3(16f, 0.95f, 8f), new Vector3(0.3f, 0.16f, 39f), wood).isStatic = true;
        }

        private static void AddFenceWoodwork(GameObject root)
        {
            var wood = EnsureMaterial("Mat_FenceWood", new Color(0.48f, 0.35f, 0.25f));
            var parent = new GameObject("FenceWood");
            parent.transform.SetParent(root.transform, false);

            void Post(float x, float z) =>
                AddBox(parent, "Post", new Vector3(x, 0.8f, z), new Vector3(0.18f, 1.6f, 0.18f), wood).isStatic = true;

            for (float x = -10f; x <= 42f; x += 4f) { Post(x, 27.75f); Post(x, -11.75f); }   // N/S 기둥
            for (float z = -8f; z <= 26f; z += 4f) { Post(-11.75f, z); Post(43.75f, z); }    // W/E 기둥

            // 가로 레일(벽 관통 배치 — 양면 돌출)
            AddBox(parent, "Rail_N", new Vector3(16f, 0.95f, 27.75f), new Vector3(56f, 0.16f, 0.3f), wood).isStatic = true;
            AddBox(parent, "Rail_S", new Vector3(16f, 0.95f, -11.75f), new Vector3(56f, 0.16f, 0.3f), wood).isStatic = true;
            AddBox(parent, "Rail_W", new Vector3(-11.75f, 0.95f, 8f), new Vector3(0.3f, 0.16f, 39f), wood).isStatic = true;
            AddBox(parent, "Rail_E", new Vector3(43.75f, 0.95f, 8f), new Vector3(0.3f, 0.16f, 39f), wood).isStatic = true;
        }

        // 울타리용 텍스처 머티리얼 — Tex_FieldFence를 X방향으로 타일링(1타일 = 벽 높이 기준 정사각).
        private static Material EnsureFenceMaterial(string name, float tilesX)
        {
            var mat = EnsureMaterial(name, Color.white);
            if (mat == null) return null;
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{kMatDir}/Tex_FieldFence.png");
            if (tex == null)
                Debug.LogWarning("[VersusField] Tex_FieldFence.png 없음 — 울타리가 단색으로 나갑니다.");
            mat.SetTexture("_BaseMap", tex);
            mat.SetTextureScale("_BaseMap", new Vector2(tilesX, 1f));
            mat.SetFloat("_Smoothness", 0.05f);
            EditorUtility.SetDirty(mat);
            return mat;
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
                if (camGo != null)
                {
                    var c = camGo.GetComponent<Camera>();
                    if (c != null) c.targetTexture = null;   // 해제 전에 카메라에서 분리(에러 방지)
                }
                if (rt != null) { rt.Release(); Object.DestroyImmediate(rt); }
                if (camGo != null) Object.DestroyImmediate(camGo);
                if (inst != null) Object.DestroyImmediate(inst);
            }
        }
    }
}
