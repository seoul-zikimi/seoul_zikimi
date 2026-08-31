using System.IO;
using UnityEditor;
using UnityEngine;

namespace GridSystem.EditorTools
{
    /// <summary>
    /// ★ 롯데월드 VARCO 모델 적용 — Models 폴더의 GLB를 파츠 규격(footprint)에 맞춰 래핑하고
    /// 파츠 def의 Prefab을 교체한다. 색큐브 → 진짜 모델 전환이 원클릭(남산 툴과 동일 체계).
    ///
    /// 사용법: VARCO에서 뽑은 GLB를 Assets/Prefabs/Map/3_LotteWorld/Models/&lt;이름&gt;.glb 로 넣고 실행.
    /// · 파츠 8종: &lt;이름&gt;_Fit.prefab 생성(footprint 크기로 스케일, 피벗 min-corner) → def.Prefab 교체
    ///   예외 2종(QA "에셋 크기/위치조정 필요"):
    ///   - 중앙첨탑: 밑동을 칸 아래로 0.5칸 연장(아래 성상단의 파인 상단을 메운다) — def.IntentionalOverfill
    ///   - 깃발: 축별 스케일 대신 균등 스케일 + 깃대 축을 칸 중심에 정렬 — def.FreeformVisual
    /// · 롯데_퍼레이드카.glb: Resources/LotteWorld/ParadeCar.prefab (퍼레이드 카 비주얼 — ParadeNetwork가 로드)
    /// · 배경 소품(롯데월드타워·자이로드롭·회전목마·대관람차·어드벤처돔): _Fit.prefab 생성
    ///   (적용 후 Tools ▸ Map ▸ ★ 롯데월드 맵 생성을 다시 실행하면 배경에 반영됨)
    /// 없는 GLB는 건너뛴다(부분 적용 가능). 몇 번을 다시 실행해도 같은 결과.
    /// </summary>
    public static class LotteModelApplyTool
    {
        private const string kDir      = "Assets/Prefabs/Map/3_LotteWorld";
        private const string kModelDir = kDir + "/Models";
        private const string kParadeCarPrefabPath = "Assets/Resources/LotteWorld/ParadeCar.prefab";

        // 파츠 이름 → footprint, 밑동 연장(skirt) (footprint는 LotteWorldMapTool.kParts와 동일해야 한다)
        // ⚠ 규약(MaterialPrefabContractTests): 비주얼 크기 = footprint와 정확히(오차 0.05) —
        //   비율 보정(visXZ)·0.97 여백은 테스트를 깨뜨려서 쓰지 않는다.
        //
        // skirt > 0 = 비주얼만 칸 아래로 그만큼 더 내린다(점유 칸은 footprint 그대로).
        //   중앙첨탑: 바로 아래 성상단의 상단이 움푹 파여 있어, 밑동이 칸 바닥(y5)에서 끊기면
        //   첨탑이 파인 자리 위에 붕 뜬 것처럼 보인다(QA). 밑동을 반 칸 늘려 파인 곳에 꽂는다.
        //   ⚠ 이 파츠의 def는 IntentionalOverfill이 켜져 있어야 한다 —
        //     안 켜면 에디터 로드 때 MaterialPrefabFitTool.FitAll이 도로 칸 크기로 쪼그라뜨린다.
        private const float kSpireSkirt = 0.5f;

        private static readonly (string name, Vector3Int fp, float skirt)[] kParts =
        {
            ("롯데_성기반",     new Vector3Int(5, 1, 5), 0f),
            ("롯데_성본체",     new Vector3Int(3, 3, 3), 0f),
            ("롯데_성상단",     new Vector3Int(3, 1, 3), 0f),
            ("롯데_중앙첨탑",   new Vector3Int(1, 3, 1), kSpireSkirt),
            ("롯데_코너타워",   new Vector3Int(1, 4, 1), 0f),
            ("롯데_타워지붕",   new Vector3Int(1, 2, 1), 0f),
            ("롯데_정문게이트", new Vector3Int(1, 2, 1), 0f),
            ("롯데_깃발",       new Vector3Int(1, 1, 1), 0f),   // 깃대 정렬 전용 경로(BuildFlagFitPrefab)
        };

        // 깃발은 '깃대 축을 칸 중심에' 맞추는 전용 래핑을 쓴다(아래 BuildFlagFitPrefab 주석 참고).
        private const string kFlagPart = "롯데_깃발";

        [MenuItem("Tools/Map/★ 롯데월드 VARCO 모델 적용")]
        public static void Apply()
        {
            int applied = 0, skipped = 0;

            // ① 파츠 8종 → _Fit 래핑 + def.Prefab 교체
            foreach (var (name, fp, skirt) in kParts)
            {
                var model = LoadModel(name);
                if (model == null) { skipped++; continue; }

                GameObject fit;
                if (name == kFlagPart)
                {
                    fit = BuildFlagFitPrefab(model, $"{kDir}/{name}_Fit.prefab", fp);
                }
                else
                {
                    // 칸을 정확히 채운다(규약) — 여백을 주면 MaterialPrefabContractTests가 깨진다.
                    // skirt가 있는 파츠만 '밑동'을 칸 아래로 연장한다(의도적 오버필 — def 플래그로 면제).
                    var target = new Vector3(fp.x, fp.y + skirt, fp.z);
                    fit = BuildFitPrefab(model, $"{kDir}/{name}_Fit.prefab",
                        target, minCornerPivot: true, cellSize: target,
                        extraOffset: new Vector3(0f, -skirt, 0f));
                }
                if (fit == null) { skipped++; continue; }

                var def = AssetDatabase.LoadAssetAtPath<MaterialDef>($"{kDir}/{name}_Def.asset");
                if (def == null) { Debug.LogWarning($"[롯데모델] def가 없음: {name}_Def.asset — 먼저 '★ 롯데월드 맵 생성'을 실행하세요."); skipped++; continue; }
                var so = new SerializedObject(def);
                so.FindProperty("m_Prefab").objectReferenceValue = fit;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(def);
                applied++;
                string extra = name == kFlagPart ? " · 깃대를 칸 중심에 정렬"
                             : skirt > 0f ? $" · 밑동 {skirt:0.##}칸 연장" : "";
                Debug.Log($"[롯데모델] 적용: {name} → {name}_Fit.prefab (footprint {fp.x}×{fp.y}×{fp.z}{extra})");
            }

            // ② 퍼레이드 카(런타임 로드용 — 바닥 피벗, 카 몸통 크기) — ParadeNetwork가 Resources에서 로드
            {
                var model = LoadModel("롯데_퍼레이드카");
                if (model != null)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(kParadeCarPrefabPath));
                    var fit = BuildFitPrefab(model, kParadeCarPrefabPath, new Vector3(2.0f, 2.6f, 3.2f), minCornerPivot: false, groundPivot: true, uniform: true);
                    if (fit != null) { applied++; Debug.Log("[롯데모델] 퍼레이드 카 적용 — 다음 플레이부터 퍼레이드가 이 모델로 보입니다."); }
                }
                else skipped++;
                // 변형 카(롯데_퍼레이드카2, 3…) — ParadeNetwork가 ParadeCar2, 3…을 순서대로 돌려 쓴다(행렬에 다양성).
                // yawFix: 3/4뷰 이미지→3D라 정면이 +z가 아닌 모델 교정(카2 백조가 옆으로 감. 반대로 돌면 부호만 뒤집어라).
                foreach (var (v, yawFix) in new[] { (2, 90f), (3, 0f), (4, 0f) })   // VARCO 정면 축이 모델마다 달라 개별 실측값 — 돌아가 보이면 해당 숫자에 ±90
                {
                    var vm = LoadModel($"롯데_퍼레이드카{v}");
                    if (vm == null) continue;
                    var vfit = BuildFitPrefab(vm, $"Assets/Resources/LotteWorld/ParadeCar{v}.prefab",
                        new Vector3(2.0f, 2.6f, 3.2f), minCornerPivot: false, groundPivot: true, uniform: true, yawFix: yawFix);
                    if (vfit != null) { applied++; Debug.Log($"[롯데모델] 퍼레이드 카 변형 {v} 적용(yaw {yawFix}°)"); }
                }
            }

            // ③ 배경 소품 — _Fit만 만들어두면 맵 생성 툴이 그레이박스 대신 쓴다
            foreach (var (name, size, uniform) in BackgroundProps())
            {
                var model = LoadModel(name);
                if (model == null) { skipped++; continue; }
                if (BuildFitPrefab(model, $"{kDir}/{name}_Fit.prefab", size, minCornerPivot: false, groundPivot: true, uniform: uniform) != null)
                    applied++;
            }

            // ④ [08/31·4차] AI 타일 텍스처 전면 롤백 — "바닥 텍스처 구리다, 기존이 낫다"(사용자 확정).
            //  민무늬 원색으로 강제 복원(멱등). Models/텍스처_*.png는 남아 있어도 더 이상 쓰지 않는다.
            ResetMat("Mat_LotteIsland",   null);                                  // 섬 잔디(원색 유지, 텍스처만 제거)
            ResetMat("Mat_LottePlaza",    new Color(0.86f, 0.83f, 0.80f));
            ResetMat("Mat_LotteParade",   new Color(0.93f, 0.90f, 0.84f));   // 아이보리(6차)
            ResetMat("Mat_LotteShore",    new Color(0.55f, 0.72f, 0.42f));
            ResetMat("Mat_LotteBoardwalk",     new Color(0.58f, 0.42f, 0.27f));
            ResetMat("Mat_LotteBoardwalkDark", new Color(0.50f, 0.36f, 0.23f));
            ResetMat("Mat_LotteRevetment",     new Color(0.62f, 0.62f, 0.64f));
            // [08/31·5차] 벽돌 포석 부활(광장) + [6차] 성벽·둑길·퍼레이드길 전용 타일(전부 VARCO, 파일 없으면 민무늬 유지)
            ApplyTexture("텍스처_광장바닥",   "Assets/Map/Materials/Mat_LottePlaza.mat", new Vector2(6f, 3f));
            ApplyTexture("텍스처_성벽",       "Assets/Map/Materials/Mat_LotteWall.mat", new Vector2(3f, 1f));
            ApplyTexture("텍스처_둑길포석",   "Assets/Map/Materials/Mat_LotteCauseway.mat", new Vector2(2f, 8f));
            ApplyTexture("텍스처_퍼레이드길", "Assets/Map/Materials/Mat_LotteParade.mat", new Vector2(6f, 1f));

            AssetDatabase.SaveAssets();
            Debug.Log($"[롯데모델] 완료 ✔ 적용 {applied}건 / 건너뜀 {skipped}건 (GLB 없음 등)\n" +
                      $"배경 소품을 적용했다면 Tools ▸ Map ▸ ★ 롯데월드 맵 생성 을 다시 실행해 배경에 반영하세요.");
        }

        /// <summary>맵 생성 툴이 참조하는 배경 소품 정의: (이름, 목표 크기, 비율 유지 여부).
        /// uniform=true면 가장 긴 축만 목표에 맞추고 나머지는 비율 그대로 — 나무·카 같은 유기물이 납작해지지 않는다(DDP와 동일 규칙).
        /// 건축물·링·지형은 배치 치수가 우선이라 축별 채움(false) 유지.</summary>
        public static (string name, Vector3 size, bool uniform)[] BackgroundProps() => new[]
        {
            ("롯데_롯데월드타워", new Vector3(9f, 33f, 9f), false),    // 북동 원경 랜드마크
            ("롯데_자이로드롭",   new Vector3(5f, 11f, 5f), false),    // 섬 동쪽(구형 통짜 — 기둥/원반 분리형이 있으면 그쪽 우선)
            ("롯데_자이로기둥",   new Vector3(3.6f, 14f, 3.6f), false),    // 자이로드롭 기둥(원반과 분리 — 원반이 승강)
            ("롯데_자이로원반",   new Vector3(4.8f, 1.5f, 4.8f), false),   // 자이로드롭 원반(노랑 곤돌라 링)
            ("롯데_회전목마",     new Vector3(8f, 5f, 8f), false),     // 섬 서쪽
            ("롯데_대관람차",     new Vector3(14f, 15f, 4f), false),   // 섬 북서쪽(회전목마 옆)
            ("롯데_풍선",         new Vector3(2.4f, 3.4f, 2.4f), true),    // 호수 상공 열기구(장식)
            ("롯데_섬지형",       new Vector3(32f, 3f, 22f), false),   // 매직아일랜드 지형(잔디 상판+석재 호안) — 윗면 평평, 박스는 투명 충돌체로
            ("롯데_나무",         new Vector3(2.4f, 3.2f, 2.4f), true),    // 정원수(섬·광장 곳곳)
            ("롯데_다리",         new Vector3(12.5f, 2.2f, 6.2f), false),  // 입구 다리(장축 x로 뽑아 90° 돌려 남북 배치, 12칸 스팬)
            ("롯데_가로등",       new Vector3(0.45f, 1.7f, 0.45f), true),  // 다리 난간 가로등(사진의 검정 주철 램프)
            ("롯데_다리탑",       new Vector3(1.7f, 5f, 1.7f), true),      // 다리 중간 쌍둥이 성탑(파랑 고깔 지붕)
            // (롯데_모노레일 통짜 링 폐기 — 기둥 마커 방식으로 대체. _Fit이 남아 있어도 맵 생성 툴이 더 이상 안 쓴다)
            ("롯데_모노기둥",     new Vector3(0.9f, 6f, 0.9f), false),     // 마커 기둥 — 높이 = 빔 중심(kBeamY)과 동기(레일 상향 시 같이)
            ("롯데_모노레일열차", new Vector3(3.2f, 1.4f, 1.3f), false),   // 궤도 위를 도는 열차(장축 x)
            ("롯데_어드벤처돔",   new Vector3(26f, 12f, 12f), false),      // 북쪽 원경 — 어드벤처 본관(유리 돔)
            ("롯데_돔요새",       new Vector3(12f, 10f, 12f), false),      // 동쪽 원경 — 청록 돔 석조 요새
            ("롯데_벚나무",       new Vector3(3.2f, 3.6f, 3.2f), true),    // 호안 벚꽃(석촌호수 봄)
            ("롯데_벚나무2",      new Vector3(3.4f, 3.8f, 3.4f), true),    // 벚나무 변형(둘레길 2열이 빽빽해져 반복 티 방지용)
            ("롯데_덤불",         new Vector3(1.8f, 1.1f, 1.8f), true),    // 물가 잔디 띠(데크 안쪽) 꽃덤불
            ("롯데_분수대",       new Vector3(5f, 3.5f, 5f), true),        // 호수 분수(다리 좌우 수면 위)
            ("롯데_스낵바",       new Vector3(3.5f, 3f, 3.5f), true),      // 광장 스낵 키오스크
            ("롯데_풍선노점",     new Vector3(2.2f, 3.2f, 2.2f), true),    // 풍선 수레(섬 동측)
            ("롯데_마스코트석상", new Vector3(3.2f, 7f, 3.2f), true),      // 마스코트 지구본 모뉴먼트(남쪽 반도 입구 로터리)
            ("롯데_백조보트",     new Vector3(2.4f, 2.4f, 3.2f), true),    // 호수 백조 보트
            ("롯데_원경아파트",   new Vector3(14f, 22f, 9f), false),       // 원경 아파트 단지
            ("롯데_원경빌딩",     new Vector3(9f, 24f, 9f), false),        // 원경 적갈색 타워
        };

        /// <summary>머티리얼을 민무늬 원색으로 되돌린다(텍스처 제거 + 색 복원). color가 null이면 색은 그대로 둔다.</summary>
        private static void ResetMat(string matName, Color? color)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>($"Assets/Map/Materials/{matName}.mat");
            if (mat == null) return;
            mat.SetTexture("_BaseMap", null);
            if (color.HasValue) mat.SetColor("_BaseColor", color.Value);
            EditorUtility.SetDirty(mat);
        }

        // 타일 텍스처를 머티리얼에 입힌다(기본: 색은 흰색으로 — 텍스처 원색 유지. keepColor면 기존 틴트 유지). 파일 없으면 조용히 통과.
        private static void ApplyTexture(string texName, string matPath, Vector2 tiling, bool keepColor = false)
        {
            Texture2D tex = null;
            foreach (var ext in new[] { "png", "jpg" })
            {
                tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{kModelDir}/{texName}.{ext}");
                if (tex != null) break;
            }
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (tex == null || mat == null) return;
            mat.SetTexture("_BaseMap", tex);
            mat.SetTextureScale("_BaseMap", tiling);
            if (!keepColor) mat.SetColor("_BaseColor", Color.white);
            EditorUtility.SetDirty(mat);
            Debug.Log($"[롯데모델] 텍스처 적용: {texName} → {Path.GetFileName(matPath)}");
        }

        private static GameObject LoadModel(string name)
        {
            foreach (var ext in new[] { "glb", "fbx", "obj" })
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>($"{kModelDir}/{name}.{ext}");
                if (go != null) return go;
            }
            return null;
        }

        /// <summary>
        /// 깃발 전용 래핑 — GLB 피벗이 '메시 바운즈 중앙'이라 칸에 그냥 맞추면 깃대가
        /// 칸 중심(=바로 아래 중앙첨탑의 축)에서 밀린다(QA: "맨 위 깃발을 왼쪽으로 옮겨야").
        /// 그래서 바운즈가 아니라 <b>깃대 축</b>을 칸 중심에 맞춘다.
        /// · 깃대 축 = 바닥에 닿는 정점들(아래 8% 밴드)의 XZ 중심 — 바닥까지 내려오는 건 깃대뿐이다.
        /// · 스케일은 축별이 아니라 균등 — 축별로 늘리면(깃발 GLB는 x1.48/z6.90) 깃대가 납작한 판이 된다.
        /// · 천이 옆 칸까지 새지 않게, 깃대를 중심에 놓았을 때의 좌우 뻗음으로 스케일을 한 번 더 조인다.
        /// ⚠ 이 프리팹은 칸을 꽉 채우지 않는다 — def의 FreeformVisual이 켜져 있어야
        ///   MaterialPrefabFitTool.FitAll이 도로 칸 크기로 늘려 깃대를 다시 밀어내지 않는다.
        /// (근본 해결은 GLB를 깃대 중심 피벗으로 다시 뽑는 것 — 그때는 이 경로가 그대로 no-op에 가깝다.)
        /// </summary>
        private static GameObject BuildFlagFitPrefab(GameObject model, string prefabPath, Vector3Int fp)
        {
            const float kSlack = 0.10f;   // 칸 밖 허용치(자유 형상 규약은 0.15까지 — 여유를 남긴다)

            var root = new GameObject(Path.GetFileNameWithoutExtension(prefabPath));
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);

            if (!MeasureFlag(root, inst, out var b, out var axis))
            {
                Debug.LogWarning($"[롯데모델] 메시를 못 읽음: {model.name} — 일반 규격으로 래핑");
                Object.DestroyImmediate(root);
                var box = new Vector3(fp.x, fp.y, fp.z);
                return BuildFitPrefab(model, prefabPath, box, minCornerPivot: true, cellSize: box);
            }

            // 칸 높이에 균등하게 맞추고, 깃대 기준 좌우 뻗음이 칸(+여유)을 넘지 않게 더 조인다
            float s = fp.y / b.size.y;
            float reachX = Mathf.Max(axis.x - b.min.x, b.max.x - axis.x);
            float reachZ = Mathf.Max(axis.y - b.min.z, b.max.z - axis.y);
            if (reachX > 1e-4f) s = Mathf.Min(s, (0.5f * fp.x + kSlack) / reachX);
            if (reachZ > 1e-4f) s = Mathf.Min(s, (0.5f * fp.z + kSlack) / reachZ);
            inst.transform.localScale *= s;

            // 스케일 반영 후 다시 재서(부모 스케일·임포터 보정 포함) 깃대 축을 칸 중심에, 바닥을 칸 바닥에
            if (!MeasureFlag(root, inst, out b, out axis)) { Object.DestroyImmediate(root); return null; }
            inst.transform.localPosition += new Vector3(0.5f * fp.x - axis.x, -b.min.y, 0.5f * fp.z - axis.y);

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        // 깃발 메시의 바운즈와 '깃대 축'(바닥 밴드 정점들의 XZ 중심)을 래퍼 루트 기준으로 잰다.
        private static bool MeasureFlag(GameObject root, GameObject inst, out Bounds bounds, out Vector2 poleAxis)
        {
            bounds = default;
            poleAxis = Vector2.zero;

            var verts = new System.Collections.Generic.List<Vector3>();
            foreach (var mf in inst.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                var toRoot = root.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                foreach (var v in mf.sharedMesh.vertices) verts.Add(toRoot.MultiplyPoint3x4(v));
            }
            if (verts.Count == 0) return false;

            var b = new Bounds(verts[0], Vector3.zero);
            foreach (var v in verts) b.Encapsulate(v);
            if (b.size.x < 1e-4f || b.size.y < 1e-4f || b.size.z < 1e-4f) return false;

            // 바닥에 닿는 부분 = 깃대. 밴드가 비면(있을 수 없지만) 바운즈 중심으로 폴백.
            float bandTop = b.min.y + 0.08f * b.size.y;
            bool any = false;
            var pole = new Bounds();
            foreach (var v in verts)
            {
                if (v.y > bandTop) continue;
                if (!any) { pole = new Bounds(v, Vector3.zero); any = true; }
                else pole.Encapsulate(v);
            }
            bounds = b;
            poleAxis = any ? new Vector2(pole.center.x, pole.center.z) : new Vector2(b.center.x, b.center.z);
            return true;
        }

        // 모델을 목표 크기 상자에 맞춰(축별 스케일) 래핑한 프리팹 생성. (NamsanModelApplyTool과 동일 규칙)
        // extraOffset: 피벗 정렬이 끝난 뒤 더할 오프셋 — 밑동을 칸 아래로 내리는 skirt에 쓴다.
        private static GameObject BuildFitPrefab(GameObject model, string prefabPath, Vector3 targetSize,
                                                 bool minCornerPivot, bool groundPivot = false, Vector3 cellSize = default,
                                                 bool uniform = false, float yawFix = 0f, Vector3 extraOffset = default)
        {
            var root = new GameObject(Path.GetFileNameWithoutExtension(prefabPath));
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
            if (yawFix != 0f) inst.transform.localRotation = Quaternion.Euler(0f, yawFix, 0f);   // 정면 교정(바운즈 계산 전에)

            var rends = inst.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0)
            {
                Debug.LogWarning($"[롯데모델] 렌더러가 없음: {model.name} — 건너뜀");
                Object.DestroyImmediate(root);
                return null;
            }
            var b = rends[0].bounds;
            foreach (var r in rends) b.Encapsulate(r.bounds);
            if (b.size.x < 1e-4f || b.size.y < 1e-4f || b.size.z < 1e-4f)
            {
                Object.DestroyImmediate(root);
                return null;
            }

            // 파츠는 축별 스케일로 칸을 꽉 채우고(실루엣 우선), 유기물 소품(uniform)은 비율 유지 —
            // 축별로 누르면 나무가 납작해지고 카가 삐뚤어진다(DDP 이간수문에서 배운 교훈).
            var s = uniform
                ? Vector3.one * Mathf.Min(targetSize.x / b.size.x, Mathf.Min(targetSize.y / b.size.y, targetSize.z / b.size.z))
                : new Vector3(targetSize.x / b.size.x, targetSize.y / b.size.y, targetSize.z / b.size.z);
            inst.transform.localScale = Vector3.Scale(inst.transform.localScale, s);

            // 스케일 반영된 바운즈로 피벗 정렬
            b = rends[0].bounds;
            foreach (var r in rends) b.Encapsulate(r.bounds);
            if (minCornerPivot)
            {
                // min-corner가 (여백/2) 지점에 오도록 — 블록이 [0..fp] 칸 중앙에 앉는다(얇은 파츠도 칸 중앙 정렬)
                var cell = cellSize == default ? targetSize : cellSize;
                var margin = (cell - targetSize) * 0.5f;
                inst.transform.localPosition -= b.min - margin;
            }
            else if (groundPivot)
            {
                var c = b.center;
                inst.transform.localPosition -= new Vector3(c.x, b.min.y, c.z);
            }
            else
            {
                inst.transform.localPosition -= b.center;
            }
            inst.transform.localPosition += extraOffset;

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }
    }
}
