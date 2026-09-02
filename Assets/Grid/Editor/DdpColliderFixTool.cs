using UnityEditor;
using UnityEngine;

namespace GridSystem.EditorTools
{
    /// <summary>
    /// ★ DDP 소품 콜라이더 교정 — MapBg_Ddp.prefab을 재생성 없이 그대로 두고,
    /// VARCO 소품 루트에 씌워진 '바운즈 박스 콜라이더'(곡면 밖 허공까지 막는 투명벽)를
    /// 메시 콜라이더(모양 그대로)로 바꾼다. 남산 팔각정 교정과 같은 방식, 몇 번 실행해도 동일 결과.
    /// (프리미티브 큐브들 — Deck/Plaza 등 — 은 박스 = 실제 모양이므로 건드리지 않는다.)
    /// </summary>
    public static class DdpColliderFixTool
    {
        private const string kBgPath = "Assets/Resources/MapPrefabs/MapBg_Ddp.prefab";

        [MenuItem("Tools/Map/★ DDP 소품 콜라이더 교정(곡면 투명벽 제거)")]
        public static void Fix()
        {
            var root = PrefabUtility.LoadPrefabContents(kBgPath);
            if (root == null) { Debug.LogError($"[DDP콜라이더] 배경 프리팹 없음: {kBgPath}"); return; }
            int fixedCount = 0;

            try
            {
                foreach (var bc in root.GetComponentsInChildren<BoxCollider>(true))
                {
                    // 소품 루트 판별: 자기한텐 메시가 없고(프리미티브 큐브 아님) 자식에 렌더 메시가 있는 박스
                    if (bc.GetComponent<MeshFilter>() != null) continue;
                    var filters = bc.GetComponentsInChildren<MeshFilter>(true);
                    if (filters.Length == 0) continue;

                    foreach (var mf in filters)
                        if (mf.sharedMesh != null && mf.GetComponent<MeshCollider>() == null)
                            mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
                    Object.DestroyImmediate(bc);
                    fixedCount++;
                }

                PrefabUtility.SaveAsPrefabAsset(root, kBgPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            Debug.Log($"[DDP콜라이더] 완료 ✔ 소품 {fixedCount}개: 바운즈 박스 → 메시 콜라이더");
        }
    }
}
