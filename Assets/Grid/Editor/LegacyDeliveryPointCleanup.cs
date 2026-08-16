using UnityEditor;
using UnityEngine;

namespace GridSystem.EditorTools
{
    /// <summary>일회성 정리: 배경 프리팹의 레거시 "DeliveryPoint" 오브젝트 제거(표준은 Spot_DeliveryZone).</summary>
    public static class LegacyDeliveryPointCleanup
    {
        [MenuItem("Tools/Map/레거시 DeliveryPoint 정리")]
        public static void Run()
        {
            int removed = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Map" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var root = PrefabUtility.LoadPrefabContents(path);
                bool dirty = false;
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (t == null || t.name != "DeliveryPoint") continue;
                    Debug.Log($"[정리] {path}: DeliveryPoint 삭제(위치 {t.position})");
                    Object.DestroyImmediate(t.gameObject);
                    dirty = true; removed++;
                    break;   // 목록이 무효해지므로 한 번에 하나씩(같은 프리팹은 아래에서 재검사 안 해도 충분)
                }
                if (dirty) PrefabUtility.SaveAsPrefabAsset(root, path);
                PrefabUtility.UnloadPrefabContents(root);
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[정리] 레거시 DeliveryPoint {removed}개 삭제 완료");
        }
    }
}
