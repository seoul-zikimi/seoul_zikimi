using UnityEditor;
using UnityEngine;

/// <summary>
/// mk-secondary 병합으로 도입된 "Spot_" 마커 시스템(배경 프리팹에 Spot_이름 마커를 두면
/// Resources/SystemObjects/의 프리팹이 그 자리에 자동 스폰되거나, 씬의 GridManager/MaterialDepot이
/// 그 위치를 따라감)에 맞춰 튜토리얼 배경에 필요한 마커들을 전부 가깝게 모아 배치한다.
/// 전부 create-or-update 방식이라 여러 번 실행해도 안전하다(이미 있으면 위치만 최신 값으로 갱신).
/// </summary>
public static class TutorialSpotMarkerFix
{
    private const string kPrefabPath = "Assets/Map/Prefabs/MapBg_Tutorial.prefab";

    // 그리드 원점(Spot_GridManager)을 배경 로컬 (0,0,0) — 즉 벽 4개가 모이는 방 바로 그 자리 — 로 고정한다.
    // 이후 모든 위치는 전부 이 원점 기준 몇 칸 이내로 가깝게 모아서, 플레이어가 스폰되자마자
    // 방·망치 작업대·배송 지점을 전부 눈에 보이는 거리에서 바로 찾을 수 있게 한다.
    private const string kGridManagerMarker = "Spot_GridManager";
    private static readonly Vector3 kGridManagerPos = Vector3.zero;

    private const string kPlayerSpawnMarker = "Spot_PlayerSpawnPoint";
    private static readonly Vector3 kPlayerSpawnPos = new(3f, 0.1f, 1f);   // 방에서 몇 걸음 거리

    private const string kHammerMarker = "Spot_HammerStation";
    private static readonly Vector3 kHammerPos = new(2.5f, 0.5f, 2.5f);   // 바닥에 파묻히지 않게 y=0.5(큐브가 바닥에 얹힌 높이)

    private const string kDeliveryMarker = "Spot_DeliveryZone";
    private static readonly Vector3 kDeliveryPos = new(-1.5f, 0f, 1f);   // 방 반대쪽, 역시 가까운 거리

    [MenuItem("Jobsnail/Tutorial/Setup All Spot Markers (그리드원점+스폰+망치+배송)")]
    public static void SetupAllMarkers()
    {
        var contents = PrefabUtility.LoadPrefabContents(kPrefabPath);

        SetMarkerPosition(contents, kGridManagerMarker, kGridManagerPos);
        SetMarkerPosition(contents, kPlayerSpawnMarker, kPlayerSpawnPos);
        SetMarkerPosition(contents, kHammerMarker, kHammerPos);
        SetMarkerPosition(contents, kDeliveryMarker, kDeliveryPos);

        PrefabUtility.SaveAsPrefabAsset(contents, kPrefabPath);
        PrefabUtility.UnloadPrefabContents(contents);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[TutorialSpotMarkerFix] Spot 마커 4개(그리드원점/플레이어스폰/망치/배송) 위치 설정 완료 — 전부 방에서 가까운 거리로 모았습니다.");
    }

    private static void SetMarkerPosition(GameObject root, string name, Vector3 localPos)
    {
        var t = root.transform.Find(name);
        if (t == null)
        {
            var marker = new GameObject(name);
            marker.transform.SetParent(root.transform, false);
            t = marker.transform;
        }
        t.localPosition = localPos;
    }

    // 바닥이 없어서 플레이어가 그리드 낙하 복구 높이(원점 y-12) 밑으로 떨어지는 문제 대응.
    // 그리드 좌표계(Spot_GridManager)와 별개로, 확실히 동작하는 독립적인 실제 바닥(콜라이더+비주얼)을
    // 배경 프리팹 자체에 직접 만들어 넣는다.
    private const string kFloorName = "~TutorialFloor";

    [MenuItem("Jobsnail/Tutorial/Add Floor To Tutorial Background")]
    public static void AddFloor()
    {
        var contents = PrefabUtility.LoadPrefabContents(kPrefabPath);
        var existing = contents.transform.Find(kFloorName);
        var floorGo = existing != null ? existing.gameObject : GameObject.CreatePrimitive(PrimitiveType.Cube);
        floorGo.name = kFloorName;
        if (existing == null) floorGo.transform.SetParent(contents.transform, false);
        // 방·스폰·작업대가 전부 원점 기준 몇 칸 이내라 20x20이면 넉넉히 다 덮는다. 벽 바닥(y=0) 바로 아래.
        floorGo.transform.localPosition = new Vector3(0f, -0.1f, 0f);
        floorGo.transform.localScale = new Vector3(20f, 0.2f, 20f);

        PrefabUtility.SaveAsPrefabAsset(contents, kPrefabPath);
        PrefabUtility.UnloadPrefabContents(contents);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[TutorialSpotMarkerFix] {kFloorName} 바닥 추가/갱신 완료.");
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
