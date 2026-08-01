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

    // 바닥이 없어서 플레이어가 그리드 낙하 복구 높이(원점 y-12) 밑으로 떨어지는 문제 대응.
    // GridManager의 보이지 않는 바닥은 "그리드 원점" 기준으로 생기는데, 튜토리얼 배경엔 Spot_GridManager
    // 마커가 없어 원점이 배경과 무관한 곳에 남아있을 수 있다 — 그래서 독립적인 실제 바닥(콜라이더+비주얼)을
    // 배경 프리팹 자체에 직접 만들어 넣는다(그리드 좌표계에 의존하지 않아 항상 확실하게 동작).
    private const string kFloorName = "~TutorialFloor";

    [MenuItem("Jobsnail/Tutorial/Add Floor To Tutorial Background")]
    public static void AddFloor()
    {
        var contents = PrefabUtility.LoadPrefabContents(kPrefabPath);
        if (contents.transform.Find(kFloorName) != null)
        {
            Debug.Log($"[TutorialSpotMarkerFix] 이미 {kFloorName}가 있습니다 — 건너뜁니다.");
            PrefabUtility.UnloadPrefabContents(contents);
            return;
        }

        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = kFloorName;
        floor.transform.SetParent(contents.transform, false);
        // 방 벽들이 대략 로컬 -1~1 범위에 있어 보여서 넉넉하게 20x20 크기로, 벽 바닥(y=0) 바로 아래에 둔다.
        floor.transform.localPosition = new Vector3(0f, -0.1f, 0f);
        floor.transform.localScale = new Vector3(20f, 0.2f, 20f);

        PrefabUtility.SaveAsPrefabAsset(contents, kPrefabPath);
        PrefabUtility.UnloadPrefabContents(contents);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[TutorialSpotMarkerFix] {kFloorName} 바닥 추가 완료 — 위치/크기가 안 맞으면 프리팹 열어서 조정하세요.");
    }

    // 배경에 이미 완성된 모습으로 박혀있던 벽/지붕 장식을 제거한다 — 이제 이 모양은 Answer_Tutorial(정답)로
    // 옮겨졌으므로, 배경에는 빈 방만 남기고 플레이어가 직접 지어야 그 모습이 나오게 한다.
    private static readonly string[] kDecorationNames = { "LeftWall", "RightWall", "FrontWall", "BackWall", "Roof" };

    [MenuItem("Jobsnail/Tutorial/Remove Built Decoration From Tutorial Background")]
    public static void RemoveDecoration()
    {
        var contents = PrefabUtility.LoadPrefabContents(kPrefabPath);
        int removed = 0;
        foreach (var name in kDecorationNames)
        {
            var t = contents.transform.Find(name);
            if (t == null) continue;
            Object.DestroyImmediate(t.gameObject);
            removed++;
        }

        PrefabUtility.SaveAsPrefabAsset(contents, kPrefabPath);
        PrefabUtility.UnloadPrefabContents(contents);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[TutorialSpotMarkerFix] 배경 장식 {removed}개 제거 완료 — 이제 빈 방에서 시작합니다.");
    }
}
