using UnityEditor;
using UnityEngine;

namespace GridSystem.EditorTools
{
    /// <summary>
    /// ★ 전 맵 소품 콜라이더 교정 — Resources/MapPrefabs의 모든 MapBg 프리팹에서
    /// VARCO 소품 루트에 씌워진 '바운즈 박스 콜라이더'(나무·곡면 모양 밖 허공까지 막는 투명벽)를
    /// 메시 콜라이더(모양 그대로)로 바꾼다. DDP 교정과 같은 방식의 일괄 처리판 — 재생성 불필요.
    /// 프리미티브 큐브(Deck/Plaza 등 — 박스 = 실제 모양, 자기 GO에 MeshFilter 있음)는 안 건드린다.
    /// 여러 번 실행해도 결과 동일.
    /// </summary>
    public static class MapPropColliderFixTool
    {
        [MenuItem("Tools/Map/★ 전 맵 소품 콜라이더 교정(투명벽 제거)")]
        public static void FixAll()
        {
            int totalProps = 0, totalMaps = 0;
            foreach (var guid in AssetDatabase.FindAssets("MapBg_ t:Prefab", new[] { "Assets/Resources/MapPrefabs" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                int fixedCount = FixPrefab(path);
                if (fixedCount < 0) continue;
                totalMaps++;
                totalProps += fixedCount;
                if (fixedCount > 0) Debug.Log($"[맵콜라이더] {System.IO.Path.GetFileNameWithoutExtension(path)}: 소품 {fixedCount}개 교정");
            }
            Debug.Log($"[맵콜라이더] 완료 ✔ 맵 {totalMaps}개 검사, 바운즈 박스 → 메시 콜라이더 {totalProps}건");
        }

        private static int FixPrefab(string path)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            if (root == null) return -1;
            int fixedCount = 0;
            try
            {
                foreach (var bc in root.GetComponentsInChildren<BoxCollider>(true))
                {
                    // 소품 루트 판별: 자기 GO엔 메시가 없고(프리미티브 큐브 아님) 자식에 렌더 메시가 있는 박스
                    if (bc.GetComponent<MeshFilter>() != null) continue;
                    var filters = bc.GetComponentsInChildren<MeshFilter>(true);
                    if (filters.Length == 0) continue;

                    foreach (var mf in filters)
                        if (mf.sharedMesh != null && mf.GetComponent<MeshCollider>() == null)
                            mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
                    Object.DestroyImmediate(bc, true);
                    fixedCount++;
                }
                if (fixedCount > 0) PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            return fixedCount;
        }
    }
}
