using UnityEditor;
using UnityEngine;

/// <summary>
/// mk-secondary 병합으로 도입된 "Spot_" 마커 시스템(배경 프리팹에 Spot_HammerStation 등을 두면
/// Resources/SystemObjects/의 프리팹이 그 자리에 자동 스폰됨)에 맞춰, 튜토리얼 배경에도
/// Spot_HammerStation 마커를 추가한다. 병합 중 GameScene.unity에 임시로만 있던 HammerStation
/// 오브젝트를 정리하면서 튜토리얼의 망치 작업대가 없어졌기 때문에 필요.
/// 위치는 대략치 — 에디터에서 Spot_HammerStation을 드래그해 방 안 원하는 자리로 옮기면 됨.
/// </summary>
public static class TutorialSpotMarkerFix
{
    private const string kPrefabPath = "Assets/Map/Prefabs/MapBg_Tutorial.prefab";
    private const string kMarkerName = "Spot_HammerStation";

    [MenuItem("Jobsnail/Tutorial/Add Spot_HammerStation Marker To Tutorial Background")]
    public static void AddMarker()
    {
        var contents = PrefabUtility.LoadPrefabContents(kPrefabPath);
        if (contents.transform.Find(kMarkerName) != null)
        {
            Debug.Log($"[TutorialSpotMarkerFix] 이미 {kMarkerName}가 있습니다 — 건너뜁니다.");
            PrefabUtility.UnloadPrefabContents(contents);
            return;
        }

        var marker = new GameObject(kMarkerName);
        marker.transform.SetParent(contents.transform, false);
        marker.transform.localPosition = new Vector3(0.2f, 0f, -0.25f);   // 대략 방 안쪽 — 에디터에서 위치 조정 권장

        PrefabUtility.SaveAsPrefabAsset(contents, kPrefabPath);
        PrefabUtility.UnloadPrefabContents(contents);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[TutorialSpotMarkerFix] {kMarkerName} 추가 완료 — 위치가 이상하면 프리팹 열어서 드래그로 조정하세요.");
    }
}
