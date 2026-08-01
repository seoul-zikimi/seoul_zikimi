using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 튜토리얼 전용 플레이스홀더 UI 프리팹 생성기(1회 실행 → 이후 에디터에서 직접 편집).
/// GameLoopHudPrefabGenerator.cs와 동일한 방식: 하이어라키를 코드로 조립해 프리팹으로 저장.
/// 텍스트는 전부 플레이스홀더(TMP) — 실제 이미지가 오면 각 프리팹을 에디터에서 직접 교체.
/// 한 번 손으로 수정한 프리팹은 이 메뉴를 다시 실행하면 덮어써지니 주의.
/// </summary>
public static class TutorialUiPrefabGenerator
{
    private const string kConfirmPath = "Assets/Resources/UI/Popup/ConfirmPopup.prefab";
    private const string kDialoguePath = "Assets/Resources/UI/HUD/TutorialDialogueHUD.prefab";
    private const string kTooltipPath = "Assets/Resources/UI/HUD/TutorialTooltipHUD.prefab";

    [MenuItem("Jobsnail/UI/Generate Tutorial UI Prefabs")]
    public static void Generate()
    {
        GenerateConfirmPopup();
        GenerateDialogueHud();
        GenerateTooltipHud();
        Debug.Log("[TutorialUiPrefabGenerator] 생성 완료 → ConfirmPopup / TutorialDialogueHUD / TutorialTooltipHUD");
    }

    private static void GenerateConfirmPopup()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(kConfirmPath));

        var root = new GameObject("ConfirmPopup", typeof(RectTransform));
        var rootRt = (RectTransform)root.transform;
        rootRt.anchorMin = Vector2.zero; rootRt.anchorMax = Vector2.one;
        rootRt.offsetMin = Vector2.zero; rootRt.offsetMax = Vector2.zero;
        root.AddComponent<ConfirmPopup>();
        var dimmer = root.AddComponent<Image>();
        dimmer.color = new Color(0f, 0f, 0f, 0.55f);
        dimmer.raycastTarget = true;
        var rootT = root.transform;

        var panel = JobsnailUiKit.Box("Panel", rootT, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(640, 360), new Color(1f, 0.97f, 0.86f, 0.98f));
        panel.raycastTarget = true;

        JobsnailUiKit.Label("Message", panel.transform, "", 24, JobsnailUiKit.Brown, TextAlignmentOptions.Center, new Vector2(0, 70), new Vector2(560, 190));

        BuildToggle("DontShowAgainToggle", panel.transform, new Vector2(-150, -40), new Vector2(28, 28));
        JobsnailUiKit.Label("CheckboxLabel", panel.transform, "이후 해당 팝업을 표시하지 않음", 16, JobsnailUiKit.Brown, TextAlignmentOptions.Left, new Vector2(30, -40), new Vector2(300, 28));

        JobsnailUiKit.Button("YesButton", panel.transform, null, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-110, -130), new Vector2(180, 56), null, "예");
        JobsnailUiKit.Button("NoButton", panel.transform, null, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(110, -130), new Vector2(180, 56), null, "아니오");

        SavePrefab(root, kConfirmPath);
    }

    private static void GenerateDialogueHud()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(kDialoguePath));

        var root = new GameObject("TutorialDialogueHUD", typeof(RectTransform));
        var rootRt = (RectTransform)root.transform;
        rootRt.anchorMin = new Vector2(0.28f, 0.76f);
        rootRt.anchorMax = new Vector2(0.72f, 0.94f);
        rootRt.offsetMin = Vector2.zero; rootRt.offsetMax = Vector2.zero;
        root.AddComponent<TutorialDialogueHUD>();
        var bg = root.AddComponent<Image>();
        bg.color = new Color(0.10f, 0.08f, 0.06f, 0.85f);
        bg.raycastTarget = true;
        var rootT = root.transform;

        JobsnailUiKit.Label("Line", rootT, "", 24, Color.white, TextAlignmentOptions.Center, Vector2.zero, Vector2.zero);

        var skip = JobsnailUiKit.Button("SkipButton", rootT, null, new Vector2(0.86f, 0.80f), new Vector2(0.99f, 0.98f), Vector2.zero, Vector2.zero, null, "건너뛰기");
        var skipImg = skip.GetComponent<Image>();
        if (skipImg != null) skipImg.color = new Color(1f, 1f, 1f, 0.15f);

        SavePrefab(root, kDialoguePath);
    }

    private static void GenerateTooltipHud()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(kTooltipPath));

        var root = new GameObject("TutorialTooltipHUD", typeof(RectTransform));
        var rootRt = (RectTransform)root.transform;
        rootRt.anchorMin = new Vector2(0.02f, 0.30f);
        rootRt.anchorMax = new Vector2(0.24f, 0.80f);
        rootRt.offsetMin = Vector2.zero; rootRt.offsetMax = Vector2.zero;
        root.AddComponent<TutorialTooltipHUD>();
        var bg = root.AddComponent<Image>();
        bg.color = new Color(0.10f, 0.08f, 0.06f, 0.70f);
        bg.raycastTarget = false;
        var rootT = root.transform;

        JobsnailUiKit.Label("Body", rootT, "", 20, Color.white, TextAlignmentOptions.TopLeft, Vector2.zero, Vector2.zero);

        SavePrefab(root, kTooltipPath);
    }

    // JobsnailUiKit엔 Toggle 헬퍼가 없어 여기서 최소 구성으로 직접 조립(배경+체크마크).
    private static Toggle BuildToggle(string name, Transform parent, Vector2 anchored, Vector2 size)
    {
        var rt = JobsnailUiKit.Rect(name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), anchored, size);
        var bg = rt.gameObject.AddComponent<Image>();
        bg.color = new Color(1f, 1f, 1f, 0.9f);
        bg.raycastTarget = true;

        var checkRt = JobsnailUiKit.Rect("Checkmark", rt, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(-6, -6));
        var check = checkRt.gameObject.AddComponent<Image>();
        check.color = new Color(0.90f, 0.45f, 0.10f, 1f);

        var toggle = rt.gameObject.AddComponent<Toggle>();
        toggle.targetGraphic = bg;
        toggle.graphic = check;
        toggle.isOn = false;
        return toggle;
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
