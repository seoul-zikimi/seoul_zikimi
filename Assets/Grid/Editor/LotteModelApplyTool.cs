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

        // 파츠 이름 → footprint (LotteWorldMapTool.kParts와 동일해야 한다)
        // ⚠ 규약(MaterialPrefabContractTests): 비주얼 크기 = footprint와 정확히(오차 0.05) —
        //   비율 보정(visXZ)·0.97 여백은 테스트를 깨뜨려서 쓰지 않는다.
        private static readonly (string name, Vector3Int fp)[] kParts =
        {
            ("롯데_성기반",     new Vector3Int(5, 1, 5)),
            ("롯데_성본체",     new Vector3Int(3, 3, 3)),
            ("롯데_성상단",     new Vector3Int(3, 1, 3)),
            ("롯데_중앙첨탑",   new Vector3Int(1, 3, 1)),
            ("롯데_코너타워",   new Vector3Int(1, 4, 1)),
            ("롯데_타워지붕",   new Vector3Int(1, 2, 1)),
            ("롯데_정문게이트", new Vector3Int(1, 2, 1)),
            ("롯데_깃발",       new Vector3Int(1, 1, 1)),
        };

        [MenuItem("Tools/Map/★ 롯데월드 VARCO 모델 적용")]
        public static void Apply()
        {
            int applied = 0, skipped = 0;

            // ① 파츠 8종 → _Fit 래핑 + def.Prefab 교체
            foreach (var (name, fp) in kParts)
            {
                var model = LoadModel(name);
                if (model == null) { skipped++; continue; }

                // 칸을 정확히 채운다(규약) — 여백을 주면 MaterialPrefabContractTests가 깨진다.
                var target = new Vector3(fp.x, fp.y, fp.z);
                var fit = BuildFitPrefab(model, $"{kDir}/{name}_Fit.prefab",
                    target, minCornerPivot: true, cellSize: new Vector3(fp.x, fp.y, fp.z));
                if (fit == null) { skipped++; continue; }

                var def = AssetDatabase.LoadAssetAtPath<MaterialDef>($"{kDir}/{name}_Def.asset");
                if (def == null) { Debug.LogWarning($"[롯데모델] def가 없음: {name}_Def.asset — 먼저 '★ 롯데월드 맵 생성'을 실행하세요."); skipped++; continue; }
                var so = new SerializedObject(def);
                so.FindProperty("m_Prefab").objectReferenceValue = fit;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(def);
                applied++;
                Debug.Log($"[롯데모델] 적용: {name} → {name}_Fit.prefab (footprint {fp.x}×{fp.y}×{fp.z})");
            }

            // ② 퍼레이드 카(런타임 로드용 — 바닥 피벗, 카 몸통 크기) — ParadeNetwork가 Resources에서 로드
            {
                var model = LoadModel("롯데_퍼레이드카");
                if (model != null)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(kParadeCarPrefabPath));
                    var fit = BuildFitPrefab(model, kParadeCarPrefabPath, new Vector3(2.0f, 2.6f, 3.2f), minCornerPivot: false, groundPivot: true);
                    if (fit != null) { applied++; Debug.Log("[롯데모델] 퍼레이드 카 적용 — 다음 플레이부터 퍼레이드가 이 모델로 보입니다."); }
                }
                else skipped++;
            }

            // ③ 배경 소품 — _Fit만 만들어두면 맵 생성 툴이 그레이박스 대신 쓴다
            foreach (var (name, size) in BackgroundProps())
            {
                var model = LoadModel(name);
                if (model == null) { skipped++; continue; }
                if (BuildFitPrefab(model, $"{kDir}/{name}_Fit.prefab", size, minCornerPivot: false, groundPivot: true) != null)
                    applied++;
            }

            // ④ 지형 텍스처(Models 폴더에 png가 있으면)
            ApplyTexture("텍스처_잔디섬", "Assets/Map/Materials/Mat_LotteIsland.mat", new Vector2(6f, 5f));
            ApplyTexture("텍스처_광장바닥", "Assets/Map/Materials/Mat_LottePlaza.mat", new Vector2(6f, 3f));
            ApplyTexture("텍스처_퍼레이드길", "Assets/Map/Materials/Mat_LotteParade.mat", new Vector2(10f, 1f));

            AssetDatabase.SaveAssets();
            Debug.Log($"[롯데모델] 완료 ✔ 적용 {applied}건 / 건너뜀 {skipped}건 (GLB 없음 등)\n" +
                      $"배경 소품을 적용했다면 Tools ▸ Map ▸ ★ 롯데월드 맵 생성 을 다시 실행해 배경에 반영하세요.");
        }

        /// <summary>맵 생성 툴이 참조하는 배경 소품 정의: (이름, 목표 크기).</summary>
        public static (string name, Vector3 size)[] BackgroundProps() => new[]
        {
            ("롯데_롯데월드타워", new Vector3(9f, 33f, 9f)),    // 북동 원경 랜드마크
            ("롯데_자이로드롭",   new Vector3(5f, 11f, 5f)),    // 섬 동쪽
            ("롯데_회전목마",     new Vector3(8f, 5f, 8f)),     // 섬 서쪽
            ("롯데_대관람차",     new Vector3(14f, 15f, 4f)),   // 섬 북서쪽(회전목마 옆)
            ("롯데_풍선",         new Vector3(2.4f, 3.4f, 2.4f)),   // 호수 상공 열기구(장식)
            ("롯데_섬지형",       new Vector3(32f, 3f, 22f)),   // 매직아일랜드 지형(잔디 상판+석재 호안) — 윗면 평평, 박스는 투명 충돌체로
            ("롯데_나무",         new Vector3(2.4f, 3.2f, 2.4f)),   // 정원수(섬·광장 곳곳)
        };

        // 타일 텍스처를 머티리얼에 입힌다(색은 흰색으로 — 텍스처 원색 유지). 파일 없으면 조용히 통과.
        private static void ApplyTexture(string texName, string matPath, Vector2 tiling)
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
            mat.SetColor("_BaseColor", Color.white);
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

        // 모델을 목표 크기 상자에 맞춰(축별 스케일) 래핑한 프리팹 생성. (NamsanModelApplyTool과 동일 규칙)
        private static GameObject BuildFitPrefab(GameObject model, string prefabPath, Vector3 targetSize,
                                                 bool minCornerPivot, bool groundPivot = false, Vector3 cellSize = default)
        {
            var root = new GameObject(Path.GetFileNameWithoutExtension(prefabPath));
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);

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

            // 축별 스케일로 목표 상자에 꽉 채움(파츠 실루엣이 칸을 채우는 게 우선)
            var s = new Vector3(targetSize.x / b.size.x, targetSize.y / b.size.y, targetSize.z / b.size.z);
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

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }
    }
}
