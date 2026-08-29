using UnityEditor;
using UnityEngine;

namespace GridSystem.EditorTools
{
    /// <summary>
    /// 재료 프리팹을 Pillar들과 같은 규약으로 맞춘다:
    ///   피벗 = footprint min-corner, 모델 크기 = footprint 칸에 딱 맞게(비균일 스케일).
    /// glb는 피벗·크기가 제각각이라, 이 규약이 안 맞으면 어서링(Autotiles3D)에서 칠한 것과
    /// 게임 배치·고스트가 어긋난다. footprint(기획 값)는 절대 바꾸지 않는다 — 모델을 칸에 맞춘다.
    ///
    /// [메뉴] Tools ▸ Grid ▸ 재료 프리팹 칸 맞춤(전체) — 어긋난 것만 골라 래퍼(<이름>_Fit) 생성 + MaterialDef 교체.
    /// 실행 후 'Grid Setup ▸ Create Autotiles3D Tiles From Catalog'가 자동으로 돌아 팔레트도 새 프리팹을 쓴다.
    /// 기획자는 AnswerAuthoring에서 다시 칠해 Export만 하면 게임과 1:1로 맞는다.
    /// </summary>
    public static class MaterialPrefabFitTool
    {
        const float kTol = 0.05f;   // 피벗·크기 허용 오차(유닛)

        // 의도적으로 칸보다 크게(오버필) 래핑하는 맵 — 칸맞춤이 건드리면 안 된다.
        // 경복궁 파츠는 GyeongbokgungModelApplyTool이 이음새를 가리려고 일부러 1.05~1.18배로 키운다.
        // (LotteWorldAutoSetup이 에디터 로드마다 FitAll을 자동 실행하므로, 여기서 제외하지 않으면 매번 도로 쪼그라든다)
        static bool IsExempt(MaterialDef def)
            => AssetDatabase.GetAssetPath(def).Contains("3_Gyeongbokgung");

        [MenuItem("Tools/Grid/재료 프리팹 칸 맞춤(전체)")]
        public static void FitAll()
        {
            int fixedCount = 0, okCount = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:MaterialDef"))
            {
                var def = AssetDatabase.LoadAssetAtPath<MaterialDef>(AssetDatabase.GUIDToAssetPath(guid));
                if (def == null || def.Prefab == null) continue;
                if (Fit(def)) fixedCount++; else okCount++;
            }
            AssetDatabase.SaveAssets();

            // 팔레트(Autotiles3D 타일)도 새 프리팹으로 갱신 — 어서링 화면과 게임이 같은 프리팹을 보게.
            EditorApplication.ExecuteMenuItem("Grid Setup/Create Autotiles3D Tiles From Catalog");

            Debug.Log($"[칸맞춤] 완료 — 수정 {fixedCount}개 / 이미 규약대로 {okCount}개. " +
                      "AnswerAuthoring 씬에서 'Setup Autotiles3D Authoring'을 다시 실행한 뒤 칠하세요.");
        }

        /// <summary>규약 검사만(수정 없음). 문제 없으면 null, 있으면 사람이 읽을 설명 반환.</summary>
        public static string Check(MaterialDef def)
        {
            if (def == null || def.Prefab == null || IsExempt(def)) return null;
            // DDP 절단 조각처럼 칸을 일부만 차지하는 곡면은 의도적으로 footprint를 꽉 채우지 않는다.
            // 전역 자동 맞춤 대상에 넣으면 이미 만든 *_Fit을 다시 감싸 *_Fit_Fit이 계속 생긴다.
            if (def.FreeformVisual) return null;
            var fp = def.Footprint;
            var probe = (GameObject)PrefabUtility.InstantiatePrefab(def.Prefab);
            if (probe == null) probe = Object.Instantiate(def.Prefab);
            probe.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            try
            {
                var renderers = probe.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0) return null;
                var b = renderers[0].bounds;
                foreach (var r in renderers) b.Encapsulate(r.bounds);

                bool pivotOk = b.min.magnitude <= kTol;
                bool sizeOk = Mathf.Abs(b.size.x - fp.x) <= kTol
                           && Mathf.Abs(b.size.y - fp.y) <= kTol
                           && Mathf.Abs(b.size.z - fp.z) <= kTol;
                if (pivotOk && sizeOk) return null;
                return (pivotOk ? "" : $"피벗이 min-corner가 아님(바운드 min {b.min}). ")
                     + (sizeOk ? "" : $"모델 크기 {b.size.x:F2}×{b.size.y:F2}×{b.size.z:F2} ≠ footprint {fp}. ")
                     + "어서링에서 칠한 정답과 게임 배치가 어긋납니다.";
            }
            finally { Object.DestroyImmediate(probe); }
        }

        /// <summary>단일 MaterialDef 맞춤(인스펙터 버튼용).</summary>
        public static bool FitOne(MaterialDef def)
        {
            bool changed = Fit(def);
            if (changed) AssetDatabase.SaveAssets();
            return changed;
        }

        // 프리팹이 규약(피벗 min-corner + footprint 크기)에 안 맞으면 맞춘 래퍼로 교체. 수정했으면 true.
        static bool Fit(MaterialDef def)
        {
            if (def == null || def.Prefab == null || def.FreeformVisual) return false;
            if (IsExempt(def)) return false;   // 오버필 의도 맵(경복궁 등) — 건드리지 않는다

            var fp = def.Footprint;
            var probe = (GameObject)PrefabUtility.InstantiatePrefab(def.Prefab);
            if (probe == null) probe = Object.Instantiate(def.Prefab);
            probe.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            var renderers = probe.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) { Object.DestroyImmediate(probe); return false; }

            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);

            // 바운즈가 0인 축이 있으면(빈/깨진 메시) 나눗셈이 무한대 스케일을 만든다 — 절대 건드리지 말 것.
            if (b.size.x < 1e-4f || b.size.y < 1e-4f || b.size.z < 1e-4f)
            {
                Debug.LogWarning($"[칸맞춤] {def.name}: 렌더러 바운즈가 0인 축이 있어 건너뜀(빈/깨진 메시 의심) — 크기 {b.size}");
                Object.DestroyImmediate(probe);
                return false;
            }

            bool pivotOk = b.min.magnitude <= kTol;
            bool sizeOk = Mathf.Abs(b.size.x - fp.x) <= kTol
                       && Mathf.Abs(b.size.y - fp.y) <= kTol
                       && Mathf.Abs(b.size.z - fp.z) <= kTol;
            if (pivotOk && sizeOk) { Object.DestroyImmediate(probe); return false; }   // Pillar처럼 이미 정상

            // 모델을 footprint 박스에 딱 맞게: 비균일 스케일 → 스케일 후 min-corner를 원점으로
            var scale = new Vector3(fp.x / b.size.x, fp.y / b.size.y, fp.z / b.size.z);
            probe.transform.localScale = Vector3.Scale(probe.transform.localScale, scale);

            var b2 = renderers[0].bounds;
            foreach (var r in renderers) b2.Encapsulate(r.bounds);

            var wrapper = new GameObject(def.Prefab.name + "_Fit");
            probe.transform.SetParent(wrapper.transform, true);
            probe.transform.position = -b2.min;

            string dir = System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(def.Prefab))?.Replace('\\', '/');
            if (string.IsNullOrEmpty(dir)) dir = "Assets";
            string path = $"{dir}/{def.Prefab.name}_Fit.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(wrapper, path);
            Object.DestroyImmediate(wrapper);

            var so = new SerializedObject(def);
            so.FindProperty("m_Prefab").objectReferenceValue = prefab;
            so.ApplyModifiedProperties();

            Debug.Log($"[칸맞춤] {def.name}: 모델 {b.size.x:F2}×{b.size.y:F2}×{b.size.z:F2}, 피벗오프셋 {b.min} → " +
                      $"footprint {fp} 칸에 맞춤: {path}");
            return true;
        }
    }
}
