using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GridSystem.Tests
{
    /// <summary>
    /// 회전(0~3)마다 프리팹 비주얼이 점유 칸 AABB에 정확히 안착하는지 검사.
    /// 정답 고스트·배치 프리뷰·실블록이 전부 PlaceRotatedPrefab을 쓰므로,
    /// 여기서 어긋나면 "고스트와 실블록이 밀려 보이는" 문제의 수치 증거가 된다.
    /// </summary>
    public class MaterialPrefabRotationVisualTests
    {
        const float kTol = 0.08f;

        [Test]
        public void 회전_0_90_180_270_모두_비주얼이_점유칸에_안착한다()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:MaterialDef"))
            {
                var def = AssetDatabase.LoadAssetAtPath<MaterialDef>(AssetDatabase.GUIDToAssetPath(guid));
                if (def == null || def.Prefab == null) continue;
                var fp = def.Footprint;

                for (int rot = 0; rot < 4; rot++)
                {
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(def.Prefab);
                    if (go == null) go = Object.Instantiate(def.Prefab);
                    try
                    {
                        GridFootprint.PlaceRotatedPrefab(go, Vector3.zero, fp, rot, 1f);

                        var rs = go.GetComponentsInChildren<Renderer>();
                        if (rs.Length == 0) continue;
                        var b = rs[0].bounds;
                        foreach (var r in rs) b.Encapsulate(r.bounds);

                        bool swap = (rot % 2) == 1;
                        var expSize = new Vector3(swap ? fp.z : fp.x, fp.y, swap ? fp.x : fp.z);

                        Assert.IsTrue(b.min.magnitude <= kTol,
                            $"[{def.name}] rot{rot}: 비주얼 min-corner가 (0,0,0)이 아님 — bounds.min {b.min}. " +
                            "이 회전으로 놓으면 고스트/실블록이 그만큼 밀려 보입니다.");
                        Assert.IsTrue((b.size - expSize).magnitude <= kTol * 3f,
                            $"[{def.name}] rot{rot}: 비주얼 크기 {b.size} ≠ 기대 {expSize}.");
                    }
                    finally { Object.DestroyImmediate(go); }
                }
            }
        }
    }
}
