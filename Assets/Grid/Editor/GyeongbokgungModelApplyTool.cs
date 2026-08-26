using System.IO;
using UnityEditor;
using UnityEngine;

namespace GridSystem.EditorTools
{
    /// <summary>
    /// ★ 경복궁 VARCO 모델 적용 — Models 폴더의 GLB를 파츠 규격(footprint)에 맞춰 래핑하고
    /// 파츠 def의 Prefab을 교체한다. 색큐브 → 진짜 모델 전환이 원클릭. (남산 적용 툴과 같은 방식)
    ///
    /// 고유 모델은 7종뿐이고, 측면/세로/단 변형은 같은 GLB를 Y축 회전·축별 스케일로 재사용한다:
    ///   벽모듈_측면 ← 벽모듈(90°), 직선기와_단 ← 직선기와(짧게), _장세로/_단세로 ← 직선기와(90°),
    ///   2층벽모듈_측면 ← 2층벽모듈(90°).
    /// 없는 GLB는 건너뛴다(부분 적용 가능). 몇 번을 다시 실행해도 같은 결과.
    /// 모서리기와는 정답의 rotationStep(GyeongbokgungMapTool.kCornerRots)으로 4모서리가 바깥을 보게 배치된다.
    /// 방향이 일괄로 어긋나 보이면 그쪽 kCornerRotOffset을 조절할 것.
    /// </summary>
    public static class GyeongbokgungModelApplyTool
    {
        private const string kDir      = "Assets/Prefabs/Map/3_Gyeongbokgung";
        private const string kModelDir = kDir + "/Models";

        // def 이름 → (원본 모델 이름, footprint, Y회전) — footprint는 GyeongbokgungMapTool.kParts와 동일해야 한다.
        private static readonly (string defName, string modelName, Vector3Int fp, float yRot)[] kParts =
        {
            ("경복궁_벽모듈",           "경복궁_벽모듈",     new Vector3Int(4, 3, 1), 0f),
            ("경복궁_벽모듈_측면",      "경복궁_벽모듈",     new Vector3Int(1, 3, 5), 90f),
            ("경복궁_문모듈",           "경복궁_문모듈",     new Vector3Int(4, 3, 1), 0f),
            ("경복궁_모서리기와",       "경복궁_모서리기와", new Vector3Int(3, 3, 3), 0f),
            ("경복궁_직선기와_장",      "경복궁_직선기와",   new Vector3Int(8, 3, 3), 0f),
            ("경복궁_직선기와_단",      "경복궁_직선기와",   new Vector3Int(6, 3, 3), 0f),
            ("경복궁_직선기와_장세로",  "경복궁_직선기와",   new Vector3Int(3, 3, 8), 90f),
            ("경복궁_직선기와_단세로",  "경복궁_직선기와",   new Vector3Int(3, 3, 6), 90f),
            ("경복궁_2층벽모듈",        "경복궁_2층벽모듈",  new Vector3Int(4, 2, 1), 0f),
            ("경복궁_2층벽모듈_측면",   "경복궁_2층벽모듈",  new Vector3Int(1, 2, 6), 90f),
            ("경복궁_지붕",             "경복궁_지붕",       new Vector3Int(8, 3, 8), 0f),
            ("경복궁_마루",             "경복궁_마루",       new Vector3Int(6, 1, 5), 0f),
        };

        [MenuItem("Tools/Map/★ 경복궁 VARCO 모델 적용")]
        public static void Apply()
        {
            int applied = 0, skipped = 0;
            foreach (var (defName, modelName, fp, yRot) in kParts)
            {
                var model = LoadModel(modelName);
                if (model == null) { skipped++; continue; }

                // 높이는 칸을 꽉 채운다(줄이면 쌓았을 때 가로 틈이 보인다). 옆면만 살짝 여백 — 남산과 동일.
                var target = new Vector3(fp.x * 0.97f, fp.y, fp.z * 0.97f);
                var fit = BuildFitPrefab(model, $"{kDir}/{defName}_Fit.prefab",
                    target, yRot, cellSize: new Vector3(fp.x, fp.y, fp.z));
                if (fit == null) { skipped++; continue; }

                var def = AssetDatabase.LoadAssetAtPath<MaterialDef>($"{kDir}/{defName}_Def.asset");
                if (def == null)
                {
                    Debug.LogWarning($"[경복궁모델] def가 없음: {defName}_Def.asset — 먼저 'Tools ▸ Map ▸ ★ 경복궁 맵 생성'을 실행하세요.");
                    skipped++; continue;
                }
                var so = new SerializedObject(def);
                so.FindProperty("m_Prefab").objectReferenceValue = fit;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(def);
                applied++;
                Debug.Log($"[경복궁모델] 적용: {defName} ← {modelName}.glb (footprint {fp.x}×{fp.y}×{fp.z}, 회전 {yRot}°)");
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[경복궁모델] 완료 ✔ 적용 {applied}건 / 건너뜀 {skipped}건 (GLB 없음 등)\n" +
                      $"바로 플레이하면 새 모델로 보입니다. 배경까지 다시 만들려면 'Tools ▸ Map ▸ ★ 경복궁 맵 생성'을 재실행하세요.");
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

        // 모델을 Y축 회전 후 목표 크기 상자에 맞춰(축별 스케일) 래핑한 프리팹 생성. 피벗 = min-corner(블록 규약).
        private static GameObject BuildFitPrefab(GameObject model, string prefabPath, Vector3 targetSize,
                                                 float yRot, Vector3 cellSize)
        {
            var root = new GameObject(Path.GetFileNameWithoutExtension(prefabPath));
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
            inst.transform.localRotation = Quaternion.Euler(0f, yRot, 0f);

            var rends = inst.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0)
            {
                Debug.LogWarning($"[경복궁모델] 렌더러가 없음: {model.name} — 건너뜀");
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

            // 회전 반영된 월드 바운즈 기준 축별 스케일 — 파츠 실루엣이 칸을 채우는 게 우선
            var s = new Vector3(targetSize.x / b.size.x, targetSize.y / b.size.y, targetSize.z / b.size.z);
            // 회전된 로컬축에 맞춰 스케일 성분을 돌려 적용(90° 단위라 축 교환으로 충분)
            var local = Quaternion.Inverse(inst.transform.localRotation) * s;
            local = new Vector3(Mathf.Abs(local.x), Mathf.Abs(local.y), Mathf.Abs(local.z));
            inst.transform.localScale = Vector3.Scale(inst.transform.localScale, local);

            b = rends[0].bounds;
            foreach (var r in rends) b.Encapsulate(r.bounds);
            // min-corner가 (여백/2) 지점에 오도록 — 블록이 [0..fp] 칸 중앙에 앉는다
            var margin = (cellSize - targetSize) * 0.5f;
            inst.transform.localPosition -= b.min - margin;

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }
    }
}
