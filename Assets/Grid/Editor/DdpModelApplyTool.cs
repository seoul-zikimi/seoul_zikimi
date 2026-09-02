using System.IO;
using UnityEditor;
using UnityEngine;

namespace GridSystem.EditorTools
{
    /// <summary>
    /// ★ DDP VARCO 모델 적용 — Models 폴더의 GLB를 파츠 규격(footprint)에 맞춰 래핑하고
    /// 파츠 def의 Prefab을 교체한다. 색큐브 → 진짜 모델 전환이 원클릭(남산·롯데 툴과 동일 체계).
    ///
    /// 사용법: VARCO에서 뽑은 GLB를 Assets/Prefabs/Map/4_Ddp/Models/&lt;이름&gt;.glb 로 넣고 실행.
    /// · 파츠 8종: &lt;이름&gt;_Fit.prefab 생성(footprint 크기로 스케일, 피벗 min-corner) → def.Prefab 교체
    /// · 배경 소품(이간수문·원경·장미 한 송이·가로등·나무): _Fit.prefab 생성
    ///   (적용 후 Tools ▸ Map ▸ ★ DDP 맵 생성을 다시 실행하면 배경에 반영됨)
    /// (유구 발굴터 기믹 제거(08/31)로 DigStake·Artifact0~2 런타임 프리팹은 더 이상 만들지 않는다)
    /// 없는 GLB는 건너뛴다(부분 적용 가능). 몇 번을 다시 실행해도 같은 결과.
    /// </summary>
    public static class DdpModelApplyTool
    {
        private const string kDir      = "Assets/Prefabs/Map/4_Ddp";
        private const string kModelDir = kDir + "/Models";
        private const string kResDir   = "Assets/Resources/Ddp";

        // 파츠 이름 → footprint (DdpMapTool.kParts와 동일해야 한다)
        // ⚠ 규약(MaterialPrefabContractTests): 비주얼 크기 = footprint와 정확히(오차 0.05) —
        //   비율 보정·여백은 테스트를 깨뜨려서 쓰지 않는다.
        // ⚠ footprint는 원본 메시의 실측 비율을 참고해 정했다(왜곡 최소화). 자세한 근거는 DdpMapTool.kParts 주석.
        private static readonly (string name, Vector3Int fp)[] kParts =
        {
            ("DDP_기단",       new Vector3Int(5, 1, 4)),
            ("DDP_곡면본체",   new Vector3Int(4, 2, 3)),
            ("DDP_곡면익부",   new Vector3Int(6, 1, 3)),
            ("DDP_은색패널",   new Vector3Int(2, 2, 1)),
            ("DDP_나선램프",   new Vector3Int(1, 2, 1)),
            ("DDP_유리커튼월", new Vector3Int(1, 2, 1)),
            ("DDP_캐노피",     new Vector3Int(3, 1, 3)),
            ("DDP_LED장미",    new Vector3Int(1, 1, 1)),
        };

        [MenuItem("Tools/Map/★ DDP VARCO 모델 적용")]
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
                if (def == null) { Debug.LogWarning($"[DDP모델] def가 없음: {name}_Def.asset — 먼저 '★ DDP 맵 생성'을 실행하세요."); skipped++; continue; }
                var so = new SerializedObject(def);
                so.FindProperty("m_Prefab").objectReferenceValue = fit;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(def);
                applied++;
                Debug.Log($"[DDP모델] 적용: {name} → {name}_Fit.prefab (footprint {fp.x}×{fp.y}×{fp.z})");
            }

            // ② 배경 소품 — _Fit만 만들어두면 맵 생성 툴이 그레이박스 대신 쓴다
            foreach (var (name, size, uniform) in BackgroundProps())
            {
                var model = LoadModel(name);
                if (model == null) { skipped++; continue; }
                if (BuildFitPrefab(model, $"{kDir}/{name}_Fit.prefab", size,
                                   minCornerPivot: false, groundPivot: true, uniform: uniform) != null)
                    applied++;
            }

            // (③ 런타임 Resources 프리팹(DigStake·Artifact0~2)은 발굴 기믹 제거(08/31)로 더 이상 만들지 않는다)

            // ④ 지형 텍스처(Models 폴더에 png가 있으면)
            ApplyTexture("텍스처_잔디지붕", "Assets/Map/Materials/Mat_DdpDeck.mat", new Vector2(6f, 5f));
            ApplyTexture("텍스처_광장바닥", "Assets/Map/Materials/Mat_DdpPlaza.mat", new Vector2(8f, 5f));
            ApplyTexture("텍스처_은색패널", "Assets/Map/Materials/Mat_DdpSilver.mat", new Vector2(4f, 2f));

            AssetDatabase.SaveAssets();
            Debug.Log($"[DDP모델] 완료 ✔ 적용 {applied}건 / 건너뜀 {skipped}건 (GLB 없음 등)\n" +
                      $"배경 소품을 적용했다면 Tools ▸ Map ▸ ★ DDP 맵 생성 을 다시 실행해 배경에 반영하세요.");
        }

        /// <summary>맵 생성 툴이 참조하는 배경 소품 정의: (이름, 목표 크기, 비율유지).
        /// uniform=true면 가장 긴 축만 목표에 맞추고 나머지는 비율 그대로 — 작은 소품이 뭉개지지 않게.</summary>
        public static (string name, Vector3 size, bool uniform)[] BackgroundProps() => new[]
        {
            // 원본 메시 실측 비율 1 : 0.50 : 0.24 — 목표도 같은 비율로 줘야 석축이 판자로 늘어나지 않는다.
            // (예전 값 1.6×3.4×7.0은 세로 4.3배·깊이 18배로 뭉개져 '이끼 낀 블럭 더미'로 보였다)
            ("DDP_이간수문",   new Vector3(7.0f, 3.5f, 1.7f), true),
            ("DDP_원경",       new Vector3(30f, 9f, 12f),     false),   // 원경은 실루엣만 보이면 되니 상자 채우기
            ("DDP_장미한송이", new Vector3(0.30f, 0.85f, 0.30f), true), // 광장 화단에 한 송이씩 심는다
            ("DDP_가로등",     new Vector3(0.9f, 3.2f, 0.9f),  true),  // 야경 가로등(있으면 그레이박스 기둥 대체)
            ("DDP_나무",       new Vector3(3.0f, 3.6f, 3.0f),  true),  // 광장 나무(있으면 원기둥+구 그레이박스 대체)
            ("DDP_미디어폴",   new Vector3(0.5f, 4.8f, 0.5f),  true),  // 광장 가장자리 LED 기둥(발광 슬리브는 툴이 얹음)
            ("DDP_벤치",       new Vector3(2.2f, 0.9f, 0.9f),  true),  // 광장 곡면 벤치(장식)
        };

        // ── Resources/Ddp 런타임 프리팹 ────────────────────────────────────
        // RosePad(장미 발판)·DigStake(발굴 말뚝)·Artifact0~2(유물)는 더 이상 만들지 않는다 —
        // 장미 발판 기믹과 유구 발굴터 기믹(08/31)을 뺐다.
        // 지면의 LED 장미정원(DDP_장미한송이를 한 송이씩 심는 것)은 그대로 남아 있다.

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
            Debug.Log($"[DDP모델] 텍스처 적용: {texName} → {Path.GetFileName(matPath)}");
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

        // 모델을 목표 크기에 맞춰 래핑한 프리팹 생성. (Namsan·Lotte 툴과 동일 규칙)
        private static GameObject BuildFitPrefab(GameObject model, string prefabPath, Vector3 targetSize,
                                                 bool minCornerPivot, bool groundPivot = false,
                                                 Vector3 cellSize = default, bool uniform = false)
        {
            var root = SpawnFitted(model, null, Path.GetFileNameWithoutExtension(prefabPath),
                                   targetSize, minCornerPivot, groundPivot, cellSize, uniform);
            if (root == null) return null;
            Directory.CreateDirectory(Path.GetDirectoryName(prefabPath));
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        /// <summary>모델을 목표 상자에 맞춰 스케일·정렬한 래퍼 GameObject를 만든다(프리팹 저장은 하지 않음).
        /// 파츠는 칸을 꽉 채워야 하므로 축별 스케일(uniform=false)을, 작은 소품은 비율 유지(uniform=true)를 쓴다.</summary>
        private static GameObject SpawnFitted(GameObject model, Transform parent, string name, Vector3 targetSize,
                                              bool minCornerPivot = false, bool groundPivot = false,
                                              Vector3 cellSize = default, bool uniform = false)
        {
            var root = new GameObject(name);
            if (parent != null) root.transform.SetParent(parent, false);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);

            var rends = inst.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0)
            {
                Debug.LogWarning($"[DDP모델] 렌더러가 없음: {model.name} — 건너뜀");
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

            Vector3 s;
            if (uniform)
            {
                // 비율 유지 — 목표 상자 안에 들어가는 최대 배율 하나만 쓴다(작은 소품이 늘어나지 않게)
                float k = Mathf.Min(targetSize.x / b.size.x, targetSize.y / b.size.y, targetSize.z / b.size.z);
                s = new Vector3(k, k, k);
            }
            else
            {
                // 축별 스케일로 목표 상자에 꽉 채움(파츠 실루엣이 칸을 채우는 게 우선)
                s = new Vector3(targetSize.x / b.size.x, targetSize.y / b.size.y, targetSize.z / b.size.z);
            }
            inst.transform.localScale = Vector3.Scale(inst.transform.localScale, s);

            // 스케일 반영된 바운즈로 피벗 정렬
            b = rends[0].bounds;
            foreach (var r in rends) b.Encapsulate(r.bounds);
            var rootPos = root.transform.position;
            if (minCornerPivot)
            {
                // min-corner가 (여백/2) 지점에 오도록 — 블록이 [0..fp] 칸 중앙에 앉는다(얇은 파츠도 칸 중앙 정렬)
                var cell = cellSize == default ? targetSize : cellSize;
                var margin = (cell - targetSize) * 0.5f;
                inst.transform.localPosition -= b.min - rootPos - margin;
            }
            else if (groundPivot)
            {
                var c = b.center;
                inst.transform.localPosition -= new Vector3(c.x, b.min.y, c.z) - rootPos;
            }
            else
            {
                inst.transform.localPosition -= b.center - rootPos;
            }
            return root;
        }
    }
}
