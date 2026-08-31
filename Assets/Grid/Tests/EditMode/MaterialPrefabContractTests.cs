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

        /// <summary>'일부러' 칸보다 크게 래핑한 재료는 칸맞춤 검사 면제
        /// (MaterialPrefabFitTool.IsExempt와 같은 규칙. 툴에 면제가 생겼는데 테스트가 몰라 CI에서 오탐).
        /// · 경복궁 파츠: 이음새를 가리려고 1.05~1.18배 오버필(폴더 단위).
        /// · MaterialDef.IntentionalOverfill: 재료 단위 면제(롯데 중앙첨탑의 밑동 연장 등).</summary>
        internal static bool IsOverfillExempt(MaterialDef def)
            => def.IntentionalOverfill || AssetDatabase.GetAssetPath(def).Contains("3_Gyeongbokgung");

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
                if (def.FreeformVisual) continue;   // 큰 모델을 자른 조각 — 피벗은 '칸의 min-corner'라 메시 바운즈와 다르다
                if (IsOverfillExempt(def)) continue;   // 경복궁 의도적 오버필
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
                if (def.FreeformVisual) continue;   // 곡면 조각은 칸을 꽉 채우지 않는다 — 늘리면 조각끼리 곡면이 어긋난다
                if (IsOverfillExempt(def)) continue;   // 경복궁 의도적 오버필
                if (!TryBounds(def.Prefab, out var b)) continue;
                var fp = def.Footprint;
                Assert.IsTrue(Mathf.Abs(b.size.x - fp.x) <= kTol &&
                              Mathf.Abs(b.size.y - fp.y) <= kTol &&
                              Mathf.Abs(b.size.z - fp.z) <= kTol,
                    $"[{def.name}] 모델 크기 {b.size.x:F2}×{b.size.y:F2}×{b.size.z:F2} ≠ footprint {fp} — " +
                    "어서링 화면과 실제 점유 칸이 다르게 보입니다. [자동으로 칸에 맞추기]로 고치세요.");
            }
        }

        /// <summary>재료 프리팹이 '실제로 뭔가를 그린다'는 최소 규약.
        ///
        /// <para>이 검사가 없어서 DDP 조각 12종이 통째로 투명해진 사고가 있었다: 래퍼 프리팹(_Fit)이
        /// 중첩 프리팹의 MeshFilter.m_Mesh를 None으로 오버라이드해, 렌더러는 살아 있는데 그릴 메시가 없었다.
        /// 그러면 주문창 썸네일이 빈칸이 되고(BlockThumbnail이 투명 텍스처를 뽑는다), 배달된 재료와
        /// 배치된 블록이 눈에 안 보이며, 정답 고스트·미니 프리뷰도 동시에 죽는다.</para>
        ///
        /// <para>위의 피벗·크기 검사는 이걸 못 잡는다 — 렌더러가 있어 TryBounds가 true를 돌려주고,
        /// 크기 0짜리 바운즈는 모든 부등식을 공허하게 통과하기 때문이다.</para></summary>
        [Test]
        public void 재료_프리팹이_실제로_그려진다()
        {
            foreach (var def in AllDefs())
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(def.Prefab);
                if (go == null) go = Object.Instantiate(def.Prefab);
                try
                {
                    foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
                        Assert.IsNotNull(mf.sharedMesh,
                            $"[{def.name}] 프리팹 '{def.Prefab.name}'의 '{mf.name}' MeshFilter에 메시가 없음(None) — " +
                            "주문창 썸네일이 빈칸이 되고 배달·배치된 블록이 투명해집니다. " +
                            "래퍼 프리팹이 중첩 원본의 m_Mesh를 오버라이드했는지 확인하세요(Revert override).");
                }
                finally { Object.DestroyImmediate(go); }

                if (!TryBounds(def.Prefab, out var b)) continue;
                Assert.Greater(b.size.x * b.size.y * b.size.z, 1e-6f,
                    $"[{def.name}] 프리팹 '{def.Prefab.name}'의 렌더 바운드 부피가 0 — 화면에 아무것도 안 그려집니다.");
            }
        }

        /// <summary>자유 형상이라도 '칸 밖으로 삐져나오지는 않는다'는 최소 규약은 지켜야 한다.
        /// 삐져나오면 옆 조각과 겹쳐 보이고, 어서링에서 칠한 칸과 눈에 보이는 덩어리가 어긋난다.</summary>
        [Test]
        public void 자유형상_조각이_칸_밖으로_나가지_않는다()
        {
            const float kSlack = 0.15f;   // 곡면 접합부가 살짝 겹치는 정도는 허용(이음매가 벌어지는 것보다 낫다)
            foreach (var def in AllDefs())
            {
                if (!def.FreeformVisual) continue;
                if (!TryBounds(def.Prefab, out var b)) continue;
                var fp = def.Footprint;
                Assert.IsTrue(b.min.x >= -kSlack && b.min.y >= -kSlack && b.min.z >= -kSlack &&
                              b.max.x <= fp.x + kSlack && b.max.y <= fp.y + kSlack && b.max.z <= fp.z + kSlack,
                    $"[{def.name}] 자유 형상 조각이 칸을 벗어남: 바운즈 {b.min}~{b.max}, footprint {fp} — " +
                    "자른 조각의 피벗이 칸의 min-corner에 안 맞았거나 footprint가 작습니다.");
            }
        }
    }
}
