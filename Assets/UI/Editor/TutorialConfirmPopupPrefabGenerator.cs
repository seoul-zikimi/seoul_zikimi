using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// TutorialConfirmPopup 프리팹 생성기(1회 실행 → 이후 에디터에서 직접 편집).
/// Assets/Resources/UI/Popup/TutorialConfirmPopup.prefab → UIManager.ShowPopupUI&lt;TutorialConfirmPopup&gt;()로 표시.
/// 자식 이름은 TutorialConfirmPopup의 Bind enum과 1:1 — 이름 바꾸면 바인딩 깨짐 주의.
/// 여기서 만드는 건 자리만 잡은 placeholder 비주얼이다. 배경/문구/폰트/색/버튼 이미지는 에디터에서 자유롭게 재작업할 것.
/// </summary>
public static class TutorialConfirmPopupPrefabGenerator
{
    private const string kPath = "Assets/Resources/UI/Popup/TutorialConfirmPopup.prefab";

    [MenuItem("Jobsnail/UI/Generate TutorialConfirmPopup Prefab")]
    public static void Generate()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(kPath));

        var root = new GameObject("TutorialConfirmPopup", typeof(RectTransform));
        var rootRt = (RectTransform)root.transform;
        rootRt.anchorMin = Vector2.zero; rootRt.anchorMax = Vector2.one;
        rootRt.offsetMin = Vector2.zero; rootRt.offsetMax = Vector2.zero;
        root.AddComponent<TutorialConfirmPopup>();
        var rootT = root.transform;

        // 풀스크린 딤(뒤 화면 클릭 방지)
        var dim = JobsnailUiKit.Box("Dimmer", rootT, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Color(0f, 0f, 0f, 0.55f));
        dim.raycastTarget = true;

        // 중앙 패널
        var panel = JobsnailUiKit.Box("Panel", rootT, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(560, 320), new Color(1f, 0.97f, 0.86f, 0.98f));

        JobsnailUiKit.Label("Title", panel.transform, "처음이시군요!", 30, Color.black, TextAlignmentOptions.Center,
            new Vector2(0, 110), new Vector2(480, 48));

        JobsnailUiKit.Label("Body", panel.transform, "조작법을 익힌 후 플레이하는 것을 권장합니다.\n튜토리얼을 플레이하시겠습니까?",
            20, new Color(0.20f, 0.18f, 0.14f, 1f), TextAlignmentOptions.Center, new Vector2(0, 40), new Vector2(500, 80));

        BuildDontShowToggle(panel.transform, new Vector2(0, -30));

        var yes = JobsnailUiKit.Button("YesButton", panel.transform, null, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(-90, -110), new Vector2(160, 52), null, "예");
        SetColor(yes, new Color(0.56f, 0.86f, 0.48f, 1f));

        var no = JobsnailUiKit.Button("NoButton", panel.transform, null, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(90, -110), new Vector2(160, 52), null, "아니오");
        SetColor(no, new Color(0.90f, 0.90f, 0.90f, 1f));

        SavePrefab(root, kPath);
        Debug.Log($"[TutorialConfirmPopupPrefabGenerator] 생성 완료 → {kPath}");
    }

    // 체크박스(Toggle) + "이후 해당 팝업을 표시하지 않음" 라벨. Bind 대상은 토글 오브젝트 이름(DontShowAgain)뿐.
    private static void BuildDontShowToggle(Transform parent, Vector2 anchored)
    {
        var row = JobsnailUiKit.Rect("ToggleRow", parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), anchored, new Vector2(420, 32));

        var toggleGo = new GameObject("DontShowAgain", typeof(RectTransform), typeof(Toggle));
        toggleGo.transform.SetParent(row, false);
        var toggleRt = (RectTransform)toggleGo.transform;
        toggleRt.anchorMin = new Vector2(0f, 0.5f); toggleRt.anchorMax = new Vector2(0f, 0.5f);
        toggleRt.pivot = new Vector2(0f, 0.5f);
        toggleRt.anchoredPosition = new Vector2(60, 0);
        toggleRt.sizeDelta = new Vector2(24, 24);

        var bg = JobsnailUiKit.Box("Background", toggleGo.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, Color.white);
        bg.raycastTarget = true;

        var check = JobsnailUiKit.Box("Checkmark", bg.transform, new Vector2(0.15f, 0.15f), new Vector2(0.85f, 0.85f),
            Vector2.zero, Vector2.zero, new Color(0.25f, 0.42f, 0.72f, 1f));

        var toggle = toggleGo.GetComponent<Toggle>();
        toggle.targetGraphic = bg;
        toggle.graphic = check;
        toggle.isOn = false;

        JobsnailUiKit.Label("Label", row, "이후 해당 팝업을 표시하지 않음", 18, new Color(0.25f, 0.20f, 0.15f, 1f),
            TextAlignmentOptions.Left, new Vector2(150, 0), new Vector2(300, 30));
    }

    private static void SetColor(Button button, Color color)
    {
        if (button != null && button.targetGraphic != null)
            button.targetGraphic.color = color;
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
