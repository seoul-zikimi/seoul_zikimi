using UnityEditor;
using UnityEngine;

/// <summary>
/// mk-secondary 병합으로 도입된 "Spot_" 마커 시스템(배경 프리팹에 Spot_이름 마커를 두면
/// Resources/SystemObjects/의 프리팹이 그 자리에 자동 스폰되거나, 씬의 GridManager/MaterialDepot이
/// 그 위치를 따라감)에 맞춰 튜토리얼 배경에 필요한 마커들을 배치한다.
///
/// 중요: GridManager는 옮기지 않는다(Spot_GridManager 마커 제거). GameScene의 GridManager는
/// 원래 (-11, 0, -7)에 고정 배치되어 있고(광통교용으로 이미 검증된 위치), 이걸 런타임에 옮기면
/// "플레이어 스폰 타이밍 vs 그리드 원점 이동 타이밍" 레이스가 생겨 사거리 판정이 꼬일 수 있다
/// (배치하려는데 역방향으로 튕기는 버그의 유력한 원인). 대신 튜토리얼 콘텐츠(바닥·마커)를
/// GridManager의 기존 위치 기준으로 옮겨서 맞춘다 — 그리드 원점 자체는 항상 예측 가능하게 유지.
/// 전부 create-or-update 방식이라 여러 번 실행해도 안전하다(이미 있으면 위치만 최신 값으로 갱신).
/// </summary>
public static class TutorialSpotMarkerFix
{
    private const string kPrefabPath = "Assets/Map/Prefabs/MapBg_Tutorial.prefab";

    // GameScene에 고정된 GridManager의 실제 위치(Assets/Scenes/GameScene.unity에서 직접 확인한 값).
    // 이 좌표를 기준으로 나머지 마커를 배치해야 그리드 원점(GridContract.Origin)과 어긋나지 않는다.
    private static readonly Vector3 kGridOrigin = new(-11f, 0f, -7f);

    private const string kGridManagerMarker = "Spot_GridManager";   // 더는 쓰지 않음 — 있으면 제거

    private const string kPlayerSpawnMarker = "Spot_PlayerSpawnPoint";
    private static readonly Vector3 kPlayerSpawnPos = kGridOrigin + new Vector3(3f, 0.1f, 1f);   // 방에서 몇 걸음 거리

    private const string kHammerMarker = "Spot_HammerStation";
    private static readonly Vector3 kHammerPos = kGridOrigin + new Vector3(2.5f, 0f, 2.5f);   // y=바닥(마커 Y=접지점 — MapLoader가 반높이 올린다)

    private const string kDeliveryMarker = "Spot_DeliveryZone";
    private static readonly Vector3 kDeliveryPos = kGridOrigin + new Vector3(-1.5f, 0f, 1f);   // 방 반대쪽, 역시 가까운 거리

    [MenuItem("Jobsnail/Tutorial/Setup All Spot Markers (스폰+망치+배송, 그리드 고정 기준)")]
    public static void SetupAllMarkers()
    {
        var contents = PrefabUtility.LoadPrefabContents(kPrefabPath);

        // Spot_GridManager는 더 이상 쓰지 않는다 — 있으면 제거(이전 실행분 정리).
        var oldGridMarker = contents.transform.Find(kGridManagerMarker);
        if (oldGridMarker != null) Object.DestroyImmediate(oldGridMarker.gameObject);

        SetMarkerPosition(contents, kPlayerSpawnMarker, kPlayerSpawnPos);
        SetMarkerPosition(contents, kHammerMarker, kHammerPos);
        SetMarkerPosition(contents, kDeliveryMarker, kDeliveryPos);

        PrefabUtility.SaveAsPrefabAsset(contents, kPrefabPath);
        PrefabUtility.UnloadPrefabContents(contents);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[TutorialSpotMarkerFix] Spot 마커 설정 완료(그리드 원점은 안 옮김) — 플레이어스폰/망치/배송을 GridManager 고정 위치 기준으로 모았습니다.");
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

    // 바닥이 없어서 플레이어가 그리드 낙하 복구 높이 밑으로 떨어지는 문제 대응.
    // GridManager 자체 그리드 크기(16×8×16 기본값)에 맞춰 이미 보이지 않는 바닥이 자동 생기지만,
    // 확실하게 동작하는 독립적인 실제 바닥(콜라이더+비주얼)도 GridManager 고정 위치 기준으로 추가해둔다.
    private const string kFloorName = "~TutorialFloor";

    [MenuItem("Jobsnail/Tutorial/Add Floor To Tutorial Background")]
    public static void AddFloor()
    {
        var contents = PrefabUtility.LoadPrefabContents(kPrefabPath);
        var existing = contents.transform.Find(kFloorName);
        var floorGo = existing != null ? existing.gameObject : GameObject.CreatePrimitive(PrimitiveType.Cube);
        floorGo.name = kFloorName;
        if (existing == null) floorGo.transform.SetParent(contents.transform, false);
        // GridManager 고정 위치(kGridOrigin) 중심으로 20x20 — 방·스폰·작업대·배송 지점을 전부 덮는다.
        floorGo.transform.localPosition = kGridOrigin + new Vector3(0f, -0.1f, 0f);
        floorGo.transform.localScale = new Vector3(20f, 0.2f, 20f);

        PrefabUtility.SaveAsPrefabAsset(contents, kPrefabPath);
        PrefabUtility.UnloadPrefabContents(contents);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[TutorialSpotMarkerFix] {kFloorName} 바닥 추가/갱신 완료 (GridManager 고정 위치 기준).");
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
