using UnityEditor;
using UnityEngine;

namespace GridSystem.EditorTools
{
    /// <summary>
    /// ★ 남산 소품 콜라이더 교정 — 기획자가 꾸민 MapBg_NamsanTower.prefab을 '재생성 없이' 그대로 두고
    /// 콜라이더만 고친다:
    /// · 남산_계단: 통짜 박스(투명 벽 느낌) → 경사 램프 콜라이더(달려서 자연스럽게 오름) + 레거시 Stair1~4 박스 제거
    /// · 남산_팔각정: 통짜 박스 → 메시 콜라이더(모양 그대로)
    /// 몇 번을 다시 실행해도 같은 결과. 램프 방향이 반대면 계단 모델을 Y 180° 돌리고 재실행.
    /// </summary>
    public static class NamsanColliderFixTool
    {
        private const string kBgPath = "Assets/Resources/MapPrefabs/MapBg_NamsanTower.prefab";

        [MenuItem("Tools/Map/★ 남산 소품 콜라이더 교정")]
        public static void Fix()
        {
            var root = PrefabUtility.LoadPrefabContents(kBgPath);
            if (root == null) { Debug.LogError($"[남산콜라이더] 배경 프리팹이 없음: {kBgPath}"); return; }
            int fixedCount = 0;

            try
            {
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (t == null) continue;

                    // 레거시 계단 박스(투명 충돌체) 제거 — 램프가 대체
                    if (t.name.StartsWith("Stair") && t.name.Length <= 7 && t.GetComponent<BoxCollider>() != null)
                    {
                        Object.DestroyImmediate(t.gameObject);
                        fixedCount++;
                        continue;
                    }

                    if (t.name.Contains("남산_계단"))
                    {
                        StripBoxColliders(t);
                        BuildRamp(t);
                        fixedCount++;
                    }
                    else if (t.name.Contains("남산_팔각정"))
                    {
                        StripBoxColliders(t);
                        foreach (var mf in t.GetComponentsInChildren<MeshFilter>())
                            if (mf.sharedMesh != null && mf.GetComponent<MeshCollider>() == null)
                                mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
                        fixedCount++;
                    }
                }

                PrefabUtility.SaveAsPrefabAsset(root, kBgPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[남산콜라이더] 완료 ✔ {fixedCount}건 교정 — 계단은 경사 램프, 팔각정은 메시 콜라이더. " +
                      "램프 오르는 방향이 반대면 계단 모델을 Y 180° 돌리고 다시 실행하세요.");
        }

        private static void StripBoxColliders(Transform t)
        {
            foreach (var bc in t.GetComponentsInChildren<BoxCollider>(true))
                Object.DestroyImmediate(bc);
            var old = t.Find("~Ramp");
            if (old != null) Object.DestroyImmediate(old.gameObject);
        }

        // 계단 바운즈에 맞는 경사 램프 박스: 낮은 끝 → 높은 끝을 하나의 빗면으로.
        private static void BuildRamp(Transform stair)
        {
            var rends = stair.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) return;
            var b = rends[0].bounds;
            foreach (var r in rends) b.Encapsulate(r.bounds);

            var f = stair.forward; f.y = 0f;
            f = f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.forward;
            float len = Mathf.Abs(b.size.x * f.x) + Mathf.Abs(b.size.z * f.z);          // 진행 방향 발자국 길이
            float wid = Mathf.Abs(b.size.x * f.z) + Mathf.Abs(b.size.z * f.x);          // 좌우 폭
            float h = b.size.y;
            if (len < 0.1f || h < 0.1f) return;

            float angle = Mathf.Atan2(h, len) * Mathf.Rad2Deg;
            var ramp = new GameObject("~Ramp");
            ramp.transform.SetParent(stair, false);
            ramp.transform.position = b.center;
            ramp.transform.rotation = Quaternion.LookRotation(f) * Quaternion.Euler(-angle, 0f, 0f);
            var bc = ramp.AddComponent<BoxCollider>();
            bc.size = new Vector3(wid * 0.95f, 0.25f, Mathf.Sqrt(len * len + h * h));
        }
    }
}
