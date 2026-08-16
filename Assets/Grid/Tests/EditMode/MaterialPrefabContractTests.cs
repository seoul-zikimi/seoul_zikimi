using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GridSystem.Tests
{
    /// <summary>
    /// 재료 프리팹 규약(피벗=min-corner, 크기=footprint 칸) 상시 감시.
    /// 이 규약이 어긋나면 어서링에서 칠한 정답과 게임 배치/고스트가 어긋난다 — Play 전에 이름으로 잡는다.
    /// 고치는 법: 그 MaterialDef 인스펙터의 [자동으로 칸에 맞추기] 버튼, 또는
    /// Tools ▸ Grid ▸ 재료 프리팹 칸 맞춤(전체).
    /// </summary>
    public class MaterialPrefabContractTests
    {
        const float kTol = 0.05f;

        static System.Collections.Generic.IEnumerable<MaterialDef> AllDefs()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:MaterialDef"))
            {
                var def = AssetDatabase.LoadAssetAtPath<MaterialDef>(AssetDatabase.GUIDToAssetPath(guid));
                if (def != null && def.Prefab != null) yield return def;
            }
        }

        static bool TryBounds(GameObject prefab, out Bounds b)
        {
            b = default;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            if (go == null) go = Object.Instantiate(prefab);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            try
            {
                var rs = go.GetComponentsInChildren<Renderer>();
                if (rs.Length == 0) return false;
                b = rs[0].bounds;
                foreach (var r in rs) b.Encapsulate(r.bounds);
                return true;
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void 재료_프리팹_피벗이_min_corner다()
        {
            foreach (var def in AllDefs())
            {
                if (!TryBounds(def.Prefab, out var b)) continue;
                Assert.LessOrEqual(b.min.magnitude, kTol,
                    $"[{def.name}] 프리팹 '{def.Prefab.name}' 피벗이 min-corner가 아님(바운드 min {b.min}) — " +
                    "어서링과 게임 배치가 어긋납니다. MaterialDef 인스펙터의 [자동으로 칸에 맞추기]로 고치세요.");
            }
        }

        [Test]
        public void 재료_모델_크기가_footprint_칸과_같다()
        {
            foreach (var def in AllDefs())
            {
                if (!TryBounds(def.Prefab, out var b)) continue;
                var fp = def.Footprint;
                Assert.IsTrue(Mathf.Abs(b.size.x - fp.x) <= kTol &&
                              Mathf.Abs(b.size.y - fp.y) <= kTol &&
                              Mathf.Abs(b.size.z - fp.z) <= kTol,
                    $"[{def.name}] 모델 크기 {b.size.x:F2}×{b.size.y:F2}×{b.size.z:F2} ≠ footprint {fp} — " +
                    "어서링 화면과 실제 점유 칸이 다르게 보입니다. [자동으로 칸에 맞추기]로 고치세요.");
            }
        }
    }
}
