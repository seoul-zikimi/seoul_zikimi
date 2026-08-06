using UnityEditor;
using UnityEngine;

namespace GridSystem.EditorTools
{
    /// <summary>일회성: 튜토리얼 배경에 빠진 Spot_PaintStation 마커를 추가(망치대 옆 2m — 위치는 기획이 조정).</summary>
    public static class TutorialMarkerFix
    {
        [MenuItem("Tools/Map/튜토리얼 Spot_PaintStation 보강")]
        public static void Run()
        {
            const string path = "Assets/Map/Prefabs/MapBg_Tutorial.prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name == "Spot_PaintStation") { Debug.Log("[튜토리얼] Spot_PaintStation 이미 있음"); return; }

                Transform hammer = null;
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name == "Spot_HammerStation") { hammer = t; break; }

                var spot = new GameObject("Spot_PaintStation");
                spot.transform.SetParent(hammer != null ? hammer.parent : root.transform, false);
                spot.transform.position = hammer != null ? hammer.position + new Vector3(2f, 0f, 0f) : Vector3.zero;
                PrefabUtility.SaveAsPrefabAsset(root, path);
                Debug.Log($"[튜토리얼] Spot_PaintStation 추가 → {spot.transform.position}");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
    }
}
