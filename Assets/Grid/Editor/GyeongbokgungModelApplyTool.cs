using System.Collections.Generic;
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
    /// (모서리기와·쿼터 슬라이스는 08/28 폐기 — 정답이 직선 기와만 쓴다.)
    /// </summary>
    public static class GyeongbokgungModelApplyTool
    {
        private const string kDir      = "Assets/Prefabs/Map/3_Gyeongbokgung";
        private const string kModelDir = kDir + "/Models";

        // 블록 간 빈틈 대책: VARCO 메시는 실루엣이 울퉁불퉁해서(처마 곡선·공포 요철) 바운딩박스를 꽉 못 채운다.
        // 여백 대신 칸보다 '살짝 넘치게' 스케일해 이웃 블록과 겹치며 이음새를 가린다(파츠별 xzOver/yOver).
        // 넘친 만큼은 칸 중심 기준 양쪽으로 균등하게 삐져나온다 — 벽 위로 넘친 부분은 기와 밑에 숨는다.
        // ⚠ 이 오버필 때문에 '재료 프리팹 칸 맞춤(전체)'을 돌리면 규약 위반으로 보고 도로 1.0으로 쪼그려버린다 —
        //   경복궁 파츠엔 칸맞춤(전체)을 돌리지 말고 이 툴만 재실행할 것.

        // def 이름 → (원본 모델, footprint, Y회전, 옆 오버필, 세로 오버필) — footprint는 GyeongbokgungMapTool.kParts와 동일해야 한다.
        // 세로 틈이 눈에 띄는 문제(08/26 스크린샷): 벽 계열은 yOver를 크게 줘서 위 기와 밑으로 밀어넣는다.
        private static readonly (string defName, string modelName, Vector3Int fp, float yRot, float xzOver, float yOver)[] kParts =
        {
            ("경복궁_벽모듈",           "경복궁_벽모듈",     new Vector3Int(4, 3, 1), 0f,  1.10f, 1.14f),
            ("경복궁_벽모듈_측면",      "경복궁_벽모듈",     new Vector3Int(1, 3, 5), 90f, 1.10f, 1.14f),
            ("경복궁_문모듈",           "경복궁_문모듈",     new Vector3Int(4, 3, 1), 0f,  1.18f, 1.14f),
            // (모서리기와는 08/28 폐기 — 코너 모델이 계속 안 예뻐서 정답을 직선 기와만으로 재설계)
            // 세로 변형(구 장세로·단세로)은 08/28 폐기 — 세로 자리는 같은 기와를 회전 배치(rot1)로 채운다
            ("경복궁_직선기와_장",      "경복궁_직선기와",   new Vector3Int(8, 3, 3), 0f,  1.10f, 1.12f),
            ("경복궁_직선기와_단",      "경복궁_직선기와",   new Vector3Int(6, 3, 3), 0f,  1.10f, 1.12f),
            ("경복궁_2층벽모듈",        "경복궁_2층벽모듈",  new Vector3Int(4, 2, 1), 0f,  1.10f, 1.16f),
            ("경복궁_2층벽모듈_측면",   "경복궁_2층벽모듈",  new Vector3Int(1, 2, 6), 90f, 1.10f, 1.16f),
            // 지붕: 삼각 왕관 한 덩어리(레퍼런스 사진 기반 재생성 모델). 반쪽 잇기 폐기 — 이음새·참조 문제 원천 제거.
            ("경복궁_지붕",             "경복궁_지붕",       new Vector3Int(16, 3, 8), 0f, 1.04f, 1.12f),
            ("경복궁_마루",             "경복궁_마루",       new Vector3Int(8, 1, 4), 0f,  1.08f, 1.06f),
            // 사방신 석상 4종(기믹 전용 재료 — GuardianNetwork가 낙하시킴). 오버필 없음.
            ("경복궁_석상_청룡",        "경복궁_석상_청룡",  new Vector3Int(2, 2, 2), 0f,  1.00f, 1.00f),
            ("경복궁_석상_백호",        "경복궁_석상_백호",  new Vector3Int(2, 2, 2), 0f,  1.00f, 1.00f),
            ("경복궁_석상_주작",        "경복궁_석상_주작",  new Vector3Int(2, 2, 2), 0f,  1.00f, 1.00f),
            ("경복궁_석상_현무",        "경복궁_석상_현무",  new Vector3Int(2, 2, 2), 0f,  1.00f, 1.00f),
        };

        [MenuItem("Tools/Map/★ 경복궁 VARCO 모델 적용")]
        public static void Apply()
        {
            int applied = 0, skipped = 0;
            foreach (var (defName, modelName, fp, yRot, xzOver, yOver) in kParts)
            {
                var model = LoadModel(modelName);
                if (model == null) { skipped++; continue; }

                var target = new Vector3(fp.x * xzOver, fp.y * yOver, fp.z * xzOver);
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

            // ── 배경 소품(드므·받침대·돌울타리·돌계단) — _Fit(바닥 피벗)만 만들어두면 맵 생성 툴이 그레이박스 대신 쓴다 ──
            foreach (var (name, size) in kProps)
            {
                var model = LoadModel(name);
                if (model == null) { skipped++; continue; }
                if (BuildFitPrefab(model, $"{kDir}/{name}_Fit.prefab", size, 0f, size, groundPivot: true) != null) applied++;
            }

            // 박석 마당 텍스처(Models/경복궁_텍스처_박석.png가 있으면) — 남산 ApplyTexture 관행
            ApplyCourtTexture();

            AssetDatabase.SaveAssets();
            Debug.Log($"[경복궁모델] 완료 ✔ 적용 {applied}건 / 건너뜀 {skipped}건 (GLB 없음 등)\n" +
                      $"바로 플레이하면 새 모델로 보입니다. 배경 소품(드므·울타리 등)을 적용했다면 'Tools ▸ Map ▸ ★ 경복궁 맵 생성'을 재실행해 배경에 반영하세요.");
        }

        // 배경 소품: (모델 이름, 목표 크기). _Fit은 바닥 피벗(중심 XZ, 바닥 y0).
        private static readonly (string name, Vector3 size)[] kProps =
        {
            ("경복궁_드므",     new Vector3(1.3f, 1.5f, 1.3f)),
            ("경복궁_받침대",   new Vector3(2.4f, 0.9f, 2.4f)),
            ("경복궁_돌울타리", new Vector3(2.1f, 1.1f, 0.55f)),
            ("경복궁_돌계단",   new Vector3(4f, 1.2f, 2.5f)),
            ("경복궁_회랑",     new Vector3(8f, 4.5f, 3.5f)),   // 8칸 세그먼트 — 맵 툴 Cloister가 줄지어 세운다
            ("경복궁_근정문",   new Vector3(12f, 7f, 5f)),      // 남쪽 정문 — 회랑 링 남쪽 중앙
        };

        // 박석 타일 텍스처를 마당 머티리얼에 입힌다(색은 흰색으로 — 텍스처 원색 유지). 파일 없으면 조용히 통과.
        private static void ApplyCourtTexture()
        {
            Texture2D tex = null;
            foreach (var ext in new[] { "png", "jpg" })
            {
                tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{kModelDir}/경복궁_텍스처_박석.{ext}");
                if (tex != null) break;
            }
            var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Map/Materials/Mat_GbkCourt.mat");
            if (tex == null || mat == null) return;
            mat.SetTexture("_BaseMap", tex);
            mat.SetTextureScale("_BaseMap", new Vector2(9f, 7f));
            mat.SetColor("_BaseColor", Color.white);
            EditorUtility.SetDirty(mat);
            Debug.Log("[경복궁모델] 박석 텍스처 적용 → Mat_GbkCourt");
        }

        private static GameObject LoadModel(string name)
        {
            foreach (var ext in new[] { "glb", "fbx", "obj", "prefab" })
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>($"{kModelDir}/{name}.{ext}");
                if (go != null) return go;
            }
            return null;
        }

        // 모델을 Y축 회전 후 목표 크기 상자에 맞춰(축별 스케일) 래핑한 프리팹 생성.
        // 기본 피벗 = min-corner(블록 규약). groundPivot=true면 중심 XZ + 바닥 y0(배경 소품용).
        private static GameObject BuildFitPrefab(GameObject model, string prefabPath, Vector3 targetSize,
                                                 float yRot, Vector3 cellSize, bool groundPivot = false)
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
            if (groundPivot)
            {
                var c = b.center;
                inst.transform.localPosition -= new Vector3(c.x, b.min.y, c.z);
            }
            else
            {
                // min-corner가 (여백/2) 지점에 오도록 — 블록이 [0..fp] 칸 중앙에 앉는다
                var margin = (cellSize - targetSize) * 0.5f;
                inst.transform.localPosition -= b.min - margin;
            }

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }
    }
}
