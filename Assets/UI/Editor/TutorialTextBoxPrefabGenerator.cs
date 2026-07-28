using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// TutorialTextBoxHUD 프리팹 생성기(1회 실행 → 이후 에디터에서 직접 편집).
/// Assets/Resources/UI/HUD/TutorialTextBoxHUD.prefab → UIManager.ShowHUDUI&lt;TutorialTextBoxHUD&gt;()로 표시.
/// 자식 이름은 TutorialTextBoxHUD의 Bind enum과 1:1 — 이름 바꾸면 바인딩 깨짐 주의.
/// 여기서 만드는 건 자리만 잡은 placeholder 비주얼이다. 위치/크기/배경/폰트/색은 에디터에서 자유롭게 재작업할 것.
/// </summary>
public static class TutorialTextBoxPrefabGenerator
{
    private const string kPath = "Assets/Resources/UI/HUD/TutorialTextBoxHUD.prefab";

    [MenuItem("Jobsnail/UI/Generate TutorialTextBoxHud Prefab")]
    public static void Generate()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(kPath));

        var root = new GameObject("TutorialTextBoxHUD", typeof(RectTransform));
        var rootRt = (RectTransform)root.transform;
        rootRt.anchorMin = Vector2.zero; rootRt.anchorMax = Vector2.one;
        rootRt.offsetMin = Vector2.zero; rootRt.offsetMax = Vector2.zero;
        root.AddComponent<TutorialTextBoxHUD>();
        var rootT = root.transform;

        // 화면 상단 중앙 대사 박스(placeholder 위치/크기 — 자유 재배치 가능)
        var box = JobsnailUiKit.Box("Box", rootT, new Vector2(0.24f, 0.80f), new Vector2(0.76f, 0.92f),
            Vector2.zero, Vector2.zero, new Color(0.10f, 0.10f, 0.12f, 0.82f));
        box.raycastTarget = true;   // 클릭으로 다음 줄 넘기기 위해 레이캐스트 대상이어야 함

        var line = JobsnailUiKit.Label("Line", box.transform, "대사가 여기에 표시됩니다.", 26, Color.white,
            TextAlignmentOptions.Center, Vector2.zero, Vector2.zero);
        var lineRt = (RectTransform)line.transform;
        lineRt.anchorMin = new Vector2(0.04f, 0.1f);
        lineRt.anchorMax = new Vector2(0.96f, 0.9f);
        lineRt.offsetMin = lineRt.offsetMax = Vector2.zero;
        line.textWrappingMode = TextWrappingModes.Normal;

        box.gameObject.SetActive(false);

        SavePrefab(root, kPath);
        Debug.Log($"[TutorialTextBoxPrefabGenerator] 생성 완료 → {kPath}");
    }

    private static void SavePrefab(GameObject root, string path)
    {
        if (File.Exists(path)) AssetDatabase.DeleteAsset(path);
        PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }
}
