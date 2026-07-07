using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// GameLoopHUD 프리팹 생성기(1회 실행 → 이후 에디터에서 직접 편집).
/// Assets/Resources/UI/HUD/GameLoopHUD.prefab → UIManager.ShowHUDUI&lt;GameLoopHUD&gt;()로 표시.
/// 자식 이름은 GameLoopHUD의 Bind enum과 1:1 — 이름 바꾸면 바인딩 깨짐 주의.
/// 버튼/슬라이더 콜백은 프리팹에 저장되지 않음 → GameLoopHUD.Init()이 배선.
/// </summary>
public static class GameLoopHudPrefabGenerator
{
    private const string kPath = "Assets/Resources/UI/HUD/GameLoopHUD.prefab";

    [MenuItem("Jobsnail/UI/Generate GameLoopHud Prefab")]
    public static void Generate()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(kPath));

        var root = new GameObject("GameLoopHUD", typeof(RectTransform));
        var rootRt = (RectTransform)root.transform;
        rootRt.anchorMin = Vector2.zero; rootRt.anchorMax = Vector2.one;
        rootRt.offsetMin = Vector2.zero; rootRt.offsetMax = Vector2.zero;
        root.AddComponent<GameLoopHUD>();
        var rootT = root.transform;

        // ── 상단 타이머 ──
        var top = JobsnailUiKit.Box("TopBar", rootT, new Vector2(0.42f, 0.92f), new Vector2(0.58f, 0.99f), Vector2.zero, Vector2.zero, new Color(0.84f, 0.82f, 0.70f, 0.92f));
        JobsnailUiKit.Label("Timer", top.transform, "0:00", 34, Color.black, TextAlignmentOptions.Center, Vector2.zero, Vector2.zero);

        // ── 종료 요청 클러스터(인원 아이콘 + 버튼) ──
        var cbar = JobsnailUiKit.Box("EndRequestCluster", rootT, new Vector2(0.70f, 0.925f), new Vector2(0.945f, 0.985f), Vector2.zero, Vector2.zero, new Color(1f, 1f, 1f, 0f));
        var snail = JobsnailUiKit.Sprite("UI_pngs/3.inGame/Person_Icon") ?? JobsnailUiKit.Sprite("UI_pngs/3.inGame/Snail_Icon");
        for (int i = 0; i < 4; i++)
        {
            var icon = JobsnailUiKit.Box($"P{i}", cbar.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(16 + i * 26, 0), new Vector2(22, 22), Color.white);
            icon.sprite = snail;
            icon.preserveAspect = true;
        }
        var end = JobsnailUiKit.Button("EndRequestButton", cbar.transform, null, new Vector2(0.46f, 0.12f), new Vector2(1f, 0.88f), Vector2.zero, Vector2.zero, null, "종료 요청");
        SetColor(end, new Color(1f, 0.78f, 0.44f, 1f));

        // ── 설정(톱니) ──
        var gearSprite = JobsnailUiKit.Sprite("UI_pngs/settingsicon");
        var gear = JobsnailUiKit.Button("SettingsIconButton", rootT, gearSprite, new Vector2(0.955f, 0.925f), new Vector2(0.99f, 0.985f), Vector2.zero, Vector2.zero, null, gearSprite == null ? "설정" : null);
        if (gear.targetGraphic is Image gearImg)
        {
            gearImg.raycastTarget = true;
            gearImg.color = Color.white;
            gearImg.preserveAspect = true;
        }

        BuildSettingsPopup(rootT);
        BuildResultPanel(rootT);

        // ── "공사 시작!" 배너(숨김 상태로 저장 · UiPopIn이 등장 팝 담당) ──
        var banner = JobsnailUiKit.Label("StartBanner", rootT, "공사 시작!", 64, new Color(1f, 0.72f, 0.20f, 1f), TextAlignmentOptions.Center, Vector2.zero, new Vector2(760, 130));
        banner.gameObject.AddComponent<UiPopIn>();
        banner.gameObject.SetActive(false);

        // ── 정산서 ↔ 크레인샷 토글 버튼(하단 중앙, 숨김) — 정산 중에만 표시 ──
        var crane = JobsnailUiKit.Button("CraneToggleButton", rootT, null,
            new Vector2(0.5f, 0.06f), new Vector2(0.5f, 0.06f), Vector2.zero, new Vector2(230, 52), null, "건축물 둘러보기");
        SetColor(crane, new Color(0.60f, 0.82f, 0.95f, 1f));
        crane.gameObject.SetActive(false);

        // ── 이벤트 토스트(좌측 위, 숨김) — 완성도 돌파 알림 ──
        var toast = JobsnailUiKit.Label("EventToast", rootT, "", 26, new Color(0.25f, 0.16f, 0.08f, 1f), TextAlignmentOptions.Center, Vector2.zero, new Vector2(340, 52));
        toast.rectTransform.anchorMin = toast.rectTransform.anchorMax = new Vector2(0.13f, 0.72f);
        toast.gameObject.AddComponent<UiPopIn>();
        toast.gameObject.SetActive(false);

        SavePrefab(root, kPath);
        Debug.Log($"[GameLoopHudPrefabGenerator] 생성 완료 → {kPath}");
    }

    private static void BuildSettingsPopup(Transform root)
    {
        var popup = JobsnailUiKit.Box("InGameSettingsPopup", root, new Vector2(0.34f, 0.24f), new Vector2(0.66f, 0.76f), Vector2.zero, Vector2.zero, new Color(1f, 0.97f, 0.86f, 0.98f)).gameObject;

        JobsnailUiKit.Label("Title", popup.transform, "설정", 30, Color.black, TextAlignmentOptions.Center, new Vector2(0, 210), new Vector2(360, 56));

        MakeVolumeSlider(popup.transform, "BGM", "BGMSlider", new Vector2(0, 120));
        MakeVolumeSlider(popup.transform, "SFX", "SFXSlider", new Vector2(0, 60));
        MakeVolumeSlider(popup.transform, "감도", "SensSlider", new Vector2(0, 0));

        var exit = JobsnailUiKit.Button("ExitGameButton", popup.transform, null, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -95), new Vector2(320, 52), null, "게임 나가기");
        SetColor(exit, new Color(1f, 0.62f, 0.62f, 1f));   // 분홍(주의)

        var close = JobsnailUiKit.Button("SettingsCloseButton", popup.transform, null, new Vector2(0.88f, 0.88f), new Vector2(0.98f, 0.98f), Vector2.zero, Vector2.zero, null, "×");
        SetColor(close, new Color(1f, 0.97f, 0.86f, 0f));

        popup.SetActive(false);
    }

    private static void BuildResultPanel(Transform root)
    {
        var panel = JobsnailUiKit.Box("ResultPanel", root, new Vector2(0.30f, 0.10f), new Vector2(0.70f, 0.90f), Vector2.zero, Vector2.zero, new Color(1f, 1f, 1f, 0.98f)).gameObject;

        // ── 상단: 제목 / 명단 / JOBSNAIL 로고 ──
        var title = JobsnailUiKit.Label("Title", panel.transform, "정산서", 44, Color.black, TextAlignmentOptions.Center, new Vector2(0, 378), new Vector2(360, 64));
        title.fontStyle = FontStyles.Bold;
        JobsnailUiKit.Label("Players", panel.transform, "", 15, new Color(0.30f, 0.22f, 0.15f, 1f), TextAlignmentOptions.Left, new Vector2(-235, 320), new Vector2(290, 26));

        var brandSprite = JobsnailUiKit.Sprite("UI_pngs/3.inGame/JobSnailLogo");
        if (brandSprite != null)
        {
            var logo = JobsnailUiKit.Box("Logo", panel.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(245, 320), new Vector2(180, 50), Color.white);
            logo.sprite = brandSprite;
            logo.preserveAspect = true;
        }
        else
        {
            var brand = JobsnailUiKit.Label("Logo", panel.transform, "JOBSNAIL", 26, new Color(0.95f, 0.42f, 0.12f, 1f), TextAlignmentOptions.Right, new Vector2(215, 320), new Vector2(200, 40));
            brand.fontStyle = FontStyles.Bold | FontStyles.Italic;
            if (TMP_Settings.defaultFontAsset != null) brand.font = TMP_Settings.defaultFontAsset;
        }

        var snailSprite = JobsnailUiKit.Sprite("UI_pngs/3.inGame/Snail_Icon");
        if (snailSprite != null)
        {
            var snailIcon = JobsnailUiKit.Box("LogoSnail", panel.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(356, 320), new Vector2(32, 32), Color.white);
            snailIcon.sprite = snailSprite;
            snailIcon.preserveAspect = true;
        }

        // ── 완성 건축물 이미지(런타임에 AnswerPreview RT 연결) + 프레임 ──
        JobsnailUiKit.Box("ImageFrame", panel.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 115), new Vector2(340, 270), new Color(0.85f, 0.85f, 0.85f, 1f));
        var riRt = JobsnailUiKit.Rect("ResultImage", panel.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 115), new Vector2(324, 254));
        var ri = riRt.gameObject.AddComponent<RawImage>();
        ri.raycastTarget = false;

        // ── 구조물명 / 소요시간 / 완성도 / 등급 ──
        JobsnailUiKit.Label("Structure", panel.transform, "", 24, Color.black, TextAlignmentOptions.Center, new Vector2(0, -50), new Vector2(440, 38));
        JobsnailUiKit.Label("Time", panel.transform, "소요시간     0 : 00", 22, new Color(0.20f, 0.18f, 0.14f, 1f), TextAlignmentOptions.Center, new Vector2(0, -108), new Vector2(440, 36));
        JobsnailUiKit.Label("Score", panel.transform, "건축 0 % 완료", 30, Color.black, TextAlignmentOptions.Center, new Vector2(0, -172), new Vector2(440, 52));

        // 완성도 별점(1~3개): 등급 글씨(EXCELLENT/TRY AGAIN) '뒤에' 깔리는 배경(원래 기획).
        // grade/stamp보다 먼저 생성 → 렌더 순서상 뒤. 채움/개수는 GameLoopHUD가 완성도에 따라 설정.
        var starSprite = JobsnailUiKit.Sprite("UI_pngs/3.inGame/star");
        var starRow = JobsnailUiKit.Rect("StarRow", panel.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(168, -172), new Vector2(180, 60));
        for (int i = 0; i < 3; i++)
        {
            var s = JobsnailUiKit.Box($"GradeStar{i}", starRow, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-46 + i * 46, 0), new Vector2(44, 44), starSprite != null ? Color.white : new Color(1f, 1f, 1f, 0f));
            s.sprite = starSprite;
            s.preserveAspect = true;
        }

        var grade = JobsnailUiKit.Label("Grade", panel.transform, "", 34, new Color(0.85f, 0.15f, 0.12f, 1f), TextAlignmentOptions.Center, new Vector2(175, -172), new Vector2(240, 70));
        grade.fontStyle = FontStyles.Bold;
        grade.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -12f);

        var stampSprite = JobsnailUiKit.Sprite("UI_pngs/3.inGame/exellent");
        var stamp = JobsnailUiKit.Box("GradeStamp", panel.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(150, -172), new Vector2(240, 88), stampSprite != null ? Color.white : new Color(1f, 1f, 1f, 0f));
        stamp.sprite = stampSprite;
        stamp.preserveAspect = true;
        stamp.gameObject.SetActive(false);

        JobsnailUiKit.Box("Divider", panel.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -252), new Vector2(640, 3), new Color(0.80f, 0.80f, 0.80f, 1f));

        var room = JobsnailUiKit.Button("RoomButton", panel.transform, null, new Vector2(0.14f, 0.05f), new Vector2(0.49f, 0.13f), Vector2.zero, Vector2.zero, null, "방으로 돌아가기");
        SetColor(room, new Color(0.97f, 0.85f, 0.58f, 1f));
        var leave = JobsnailUiKit.Button("LeaveButton", panel.transform, null, new Vector2(0.51f, 0.05f), new Vector2(0.86f, 0.13f), Vector2.zero, Vector2.zero, null, "나가기");
        SetColor(leave, new Color(0.97f, 0.85f, 0.58f, 1f));

        panel.SetActive(false);
    }

    // 볼륨/감도 슬라이더 비주얼(값·콜백은 런타임 Init이 배선). sliderName은 Bind enum과 일치해야 함.
    private static void MakeVolumeSlider(Transform parent, string label, string sliderName, Vector2 anchored)
    {
        var row = new GameObject(label + "VolumeRow", typeof(RectTransform));
        row.transform.SetParent(parent, false);
        var rowRt = (RectTransform)row.transform;
        rowRt.anchorMin = new Vector2(0.5f, 0.5f);
        rowRt.anchorMax = new Vector2(0.5f, 0.5f);
        rowRt.anchoredPosition = anchored;
        rowRt.sizeDelta = new Vector2(340, 44);

        JobsnailUiKit.Label(label + "Label", row.transform, label, 18, Color.black, TextAlignmentOptions.Left, new Vector2(-126, 0), new Vector2(70, 36));

        var sliderGo = new GameObject(sliderName, typeof(RectTransform), typeof(Slider));
        sliderGo.transform.SetParent(row.transform, false);
        var rt = (RectTransform)sliderGo.transform;
        rt.anchorMin = new Vector2(0.35f, 0.25f);
        rt.anchorMax = new Vector2(0.95f, 0.75f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var background = JobsnailUiKit.Box("Background", sliderGo.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Color(0.82f, 0.82f, 0.82f, 1f));
        background.raycastTarget = true;

        var fillArea = new GameObject("Fill Area", typeof(RectTransform));
        fillArea.transform.SetParent(sliderGo.transform, false);
        var fillAreaRt = (RectTransform)fillArea.transform;
        fillAreaRt.anchorMin = Vector2.zero;
        fillAreaRt.anchorMax = Vector2.one;
        fillAreaRt.offsetMin = new Vector2(4, 4);
        fillAreaRt.offsetMax = new Vector2(-4, -4);

        var fill = JobsnailUiKit.Box("Fill", fillArea.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Color(1f, 0.72f, 0.36f, 1f));
        fill.raycastTarget = true;

        var handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
        handleArea.transform.SetParent(sliderGo.transform, false);
        var handleAreaRt = (RectTransform)handleArea.transform;
        handleAreaRt.anchorMin = Vector2.zero;
        handleAreaRt.anchorMax = Vector2.one;
        handleAreaRt.offsetMin = new Vector2(8, 0);
        handleAreaRt.offsetMax = new Vector2(-8, 0);

        var handle = JobsnailUiKit.Box("Handle", handleArea.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(20, 30), new Color(0.32f, 0.22f, 0.15f, 1f));
        handle.raycastTarget = true;

        var slider = sliderGo.GetComponent<Slider>();
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.fillRect = fill.rectTransform;
        slider.handleRect = handle.rectTransform;
        slider.targetGraphic = handle;
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
