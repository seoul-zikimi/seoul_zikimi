using System.IO;
using UnityEditor;
using UnityEngine;

namespace GridSystem.EditorTools
{
    /// <summary>
    /// ★ 남산 VARCO 모델 적용 — Models 폴더의 GLB를 파츠 규격(footprint)에 맞춰 래핑하고
    /// 파츠 def의 Prefab을 교체한다. 색큐브 → 진짜 모델 전환이 원클릭.
    ///
    /// 사용법: VARCO에서 뽑은 GLB를 Assets/Prefabs/Map/2_NamsanTower/Models/&lt;파츠이름&gt;.glb 로 넣고 실행.
    /// · 파츠 9종: &lt;이름&gt;_Fit.prefab 생성(footprint 크기로 스케일, 피벗 min-corner) → def.Prefab 교체
    /// · 남산_케이블카.glb: Resources/Namsan/CableCarGondola.prefab (곤돌라 비주얼 — CableCarNetwork가 로드)
    /// · 남산_팔각정.glb / 남산_로비건물.glb: _Fit.prefab 생성 → 맵 생성 툴이 그레이박스 대신 배치
    ///   (적용 후 Tools ▸ Map ▸ ★ 남산타워 맵 생성을 다시 실행하면 배경에 반영됨)
    /// 없는 GLB는 건너뛴다(부분 적용 가능). 몇 번을 다시 실행해도 같은 결과.
    /// </summary>
    public static class NamsanModelApplyTool
    {
        private const string kDir      = "Assets/Prefabs/Map/2_NamsanTower";
        private const string kModelDir = kDir + "/Models";
        private const string kGondolaPrefabPath = "Assets/Resources/Namsan/CableCarGondola.prefab";

        // 파츠 이름 → footprint (NamsanTowerMapTool.kParts와 동일해야 한다)
        // visXZ = 보이는 굵기 배율(발자국·판정은 그대로, 실루엣만 얇게) — 실물 비례 보정용.
        //   받침(0.8)은 전망대보다 지름이 작아야 하고, 상부·최상부 안테나(0.5)는 하부 안테나의 절반 굵기.
        private static readonly (string name, Vector3Int fp, float visXZ)[] kParts =
        {
            ("남산_기반",            new Vector3Int(5, 1, 5), 1f),
            ("남산_하부기둥",        new Vector3Int(1, 2, 1), 1f),
            // 1.6배: 링(철망) 포함 전체 폭이 칸 밖으로 나가는 대신 중심 원기둥이 위아래 하부기둥과 같은 굵기가 된다.
            // 링이 덜/더 튀어나오면 이 값만 조절(중심기둥 굵기 = 1.6 ÷ 모델의 링:기둥 비율).
            ("남산_철제받침기둥",    new Vector3Int(1, 2, 1), 1.6f),
            ("남산_철제전망대",      new Vector3Int(3, 2, 3), 1f),
            ("남산_전망대받침",      new Vector3Int(3, 1, 3), 0.8f),
            ("남산_하부안테나_빨강", new Vector3Int(1, 2, 1), 1f),
            ("남산_하부안테나_하양", new Vector3Int(1, 2, 1), 1f),
            ("남산_상부안테나",      new Vector3Int(1, 2, 1), 0.5f),
            ("남산_최상부안테나",    new Vector3Int(1, 3, 1), 0.5f),
        };

        [MenuItem("Tools/Map/★ 남산 VARCO 모델 적용")]
        public static void Apply()
        {
            int applied = 0, skipped = 0;

            // ① 파츠 9종 → _Fit 래핑 + def.Prefab 교체
            foreach (var (name, fp, visXZ) in kParts)
            {
                var model = LoadModel(name);
                if (model == null) { skipped++; continue; }

                // 높이는 칸을 꽉 채운다(0.97로 줄이면 쌓았을 때 블록 사이에 가로 틈이 보인다). 옆면만 살짝 여백.
                var target = new Vector3(fp.x * 0.97f * visXZ, fp.y, fp.z * 0.97f * visXZ);
                var fit = BuildFitPrefab(model, $"{kDir}/{name}_Fit.prefab",
                    target, minCornerPivot: true, cellSize: new Vector3(fp.x, fp.y, fp.z));
                if (fit == null) { skipped++; continue; }

                var def = AssetDatabase.LoadAssetAtPath<MaterialDef>($"{kDir}/{name}_Def.asset");
                if (def == null) { Debug.LogWarning($"[남산모델] def가 없음: {name}_Def.asset — 먼저 '★ 남산타워 맵 생성'을 실행하세요."); skipped++; continue; }
                var so = new SerializedObject(def);
                so.FindProperty("m_Prefab").objectReferenceValue = fit;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(def);
                applied++;
                Debug.Log($"[남산모델] 적용: {name} → {name}_Fit.prefab (footprint {fp.x}×{fp.y}×{fp.z})");
            }

            // ② 케이블카 곤돌라(런타임 로드용 — 중심 피벗, 곤돌라 몸통 크기)
            {
                var model = LoadModel("남산_케이블카");
                if (model != null)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(kGondolaPrefabPath));
                    var fit = BuildFitPrefab(model, kGondolaPrefabPath, new Vector3(2.0f, 3.4f, 2.0f), minCornerPivot: false);
                    if (fit != null) { applied++; Debug.Log("[남산모델] 곤돌라 적용 — 다음 플레이부터 케이블카가 이 모델로 보입니다."); }
                }
                else skipped++;
            }

            // ③ 배경 소품(팔각정·로비건물) — _Fit만 만들어두면 맵 생성 툴이 그레이박스 대신 쓴다
            foreach (var (name, size, _) in BackgroundProps())
            {
                var model = LoadModel(name);
                if (model == null) { skipped++; continue; }
                if (BuildFitPrefab(model, $"{kDir}/{name}_Fit.prefab", size, minCornerPivot: false, groundPivot: true) != null)
                    applied++;
            }

            // ④ 지형 텍스처(Models 폴더에 png가 있으면) — 잔디→평지, 나무데크→데크, 도시→원경 평원
            ApplyTexture("텍스처_잔디", "Assets/Map/Materials/Mat_NamsanGround.mat", new Vector2(6f, 5f));
            ApplyTexture("텍스처_나무데크", "Assets/Map/Materials/Mat_NamsanDeck.mat", new Vector2(5f, 4f));
            ApplyTexture("텍스처_도시", "Assets/Map/Materials/Mat_NamsanCity.mat", new Vector2(6f, 6f));

            AssetDatabase.SaveAssets();
            Debug.Log($"[남산모델] 완료 ✔ 적용 {applied}건 / 건너뜀 {skipped}건 (GLB 없음 등)\n" +
                      $"팔각정·로비건물·산 등을 적용했다면 Tools ▸ Map ▸ ★ 남산타워 맵 생성 을 다시 실행해 배경에 반영하세요.");
        }

        /// <summary>맵 생성 툴이 참조하는 배경 소품 정의: (이름, 목표 크기, 배치 여유).</summary>
        public static (string name, Vector3 size, float _)[] BackgroundProps() => new[]
        {
            ("남산_팔각정",   new Vector3(6f, 4f, 6f), 0f),
            ("남산_로비건물", new Vector3(5f, 1.9f, 4f), 0f),
            ("남산_자물쇠벽", new Vector3(6f, 2f, 0.8f), 0f),
            ("남산_하트동상", new Vector3(1.6f, 2.2f, 0.8f), 0f),
            ("남산_산",       new Vector3(46f, 10f, 40f), 0f),   // 윗면 평평한 산 — 평지 비주얼 대체(충돌은 투명 박스)
            ("남산_나무",     new Vector3(2.2f, 3.2f, 2.2f), 0f),  // 맵 곳곳에 심는 소나무
            ("남산_산맥",     new Vector3(60f, 8f, 12f), 0f),      // 맵 밖 원경 능선
            ("남산_철탑",     new Vector3(2f, 7f, 2f), 0f),        // 케이블카 지지 철탑
            ("남산_계단",     new Vector3(3.4f, 2.6f, 4.4f), 0f),  // 나무 계단(충돌은 투명 박스 유지)
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
            Debug.Log($"[남산모델] 텍스처 적용: {texName} → {Path.GetFileName(matPath)}");
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

        // 모델을 목표 크기 상자에 맞춰(축별 스케일) 래핑한 프리팹 생성.
        // minCornerPivot: 블록 규약(피벗=min-corner, [0..fp] 점유). false면 중심 피벗.
        // groundPivot: 중심 피벗이되 바닥만 y=0 (배경 소품용).
        private static GameObject BuildFitPrefab(GameObject model, string prefabPath, Vector3 targetSize,
                                                 bool minCornerPivot, bool groundPivot = false, Vector3 cellSize = default)
        {
            var root = new GameObject(Path.GetFileNameWithoutExtension(prefabPath));
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);

            var rends = inst.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0)
            {
                Debug.LogWarning($"[남산모델] 렌더러가 없음: {model.name} — 건너뜀");
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
