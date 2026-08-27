using UnityEditor;
using UnityEngine;

/// <summary>
/// 인게임 UI 리마스터 자동 적용: 스크립트 리로드 후 GameLoopHUD / CarryHudUI 프리팹이 구 레이아웃이면(마커 노드 없음)
/// 생성기를 1회 돌려 재생성한다. (두 프리팹은 항상 생성기로만 만들어져 왔음 — 커밋 이력상 생성기와 함께 갱신)
/// 수동: Jobsnail ▸ UI ▸ Remaster InGame HUD (regenerate all)
/// </summary>
[InitializeOnLoad]
public static class InGameUiRemasterAutoApply
{
    private const string kGameLoop = "Assets/Resources/UI/HUD/GameLoopHUD.prefab";
    private const string kCarry    = "Assets/Resources/UI/HUD/CarryHudUI.prefab";
    private const string kSprite   = "Assets/Resources/UI_pngs/3.inGame/Remaster/PhoneBg.png";

    static InGameUiRemasterAutoApply()
    {
        EditorApplication.delayCall += AutoApply;
        // 플레이 중에 컴파일되면 위 delayCall 은 건너뛰므로, 에디트 모드로 돌아올 때 한 번 더 시도
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += AutoApply;
        };
    }

    private static void AutoApply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            return;
        if (AssetDatabase.LoadAssetAtPath<Sprite>(kSprite) == null)
            return;   // 리마스터 스프라이트 아직 임포트 전 — 다음 리로드에 재시도

        bool any = false;
        if (!HasChild(kGameLoop, GameLoopHudPrefabGenerator.kRemasterMarker)) { GameLoopHudPrefabGenerator.Generate(); any = true; }
        if (!HasChild(kCarry, CarryHudPrefabGenerator.kRemasterMarker))       { CarryHudPrefabGenerator.Generate();    any = true; }
        if (any) Debug.Log("[InGameUiRemaster] 리마스터 레이아웃으로 HUD 프리팹 자동 재생성 완료");
    }

    [MenuItem("Jobsnail/UI/Remaster InGame HUD (regenerate all)")]
    public static void RegenerateAll()
    {
        GameLoopHudPrefabGenerator.Generate();
        CarryHudPrefabGenerator.Generate();
    }

    private static bool HasChild(string prefabPath, string childName)
    {
        var go = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (go == null) return false;
        foreach (var t in go.GetComponentsInChildren<Transform>(true))
            if (t.name == childName) return true;
        return false;
    }
}
