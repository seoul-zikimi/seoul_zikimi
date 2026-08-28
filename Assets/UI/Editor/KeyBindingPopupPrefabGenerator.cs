using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>키 설정 팝업을 실제 편집 가능한 프리팹으로 생성하고 인게임 설정 버튼을 기존 HUD 프리팹에 추가한다.</summary>
public static class KeyBindingPopupPrefabGenerator
{
    private const string kPopupPath = "Assets/Resources/UI/Popup/KeyBindingPopup.prefab";
    private const string kGameHudPath = "Assets/Resources/UI/HUD/GameLoopHUD.prefab";
    private const int kRowCapacity = 32;

    [MenuItem("Jobsnail/UI/Generate Key Binding Popup")]
    public static void Generate()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(kPopupPath));
        GeneratePopup();
        PatchGameHud();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[KeyBindingPopup] 생성 완료: {kPopupPath}");
    }

    private static void GeneratePopup()
    {
        var root = Rect("KeyBindingPopup", null, Vector2.zero, Vector2.zero);
        Stretch(root);
        root.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.62f);
        var canvas = root.gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 650; // 메인 메뉴 Canvas(500)보다 위
        // Canvas 추가 시 Unity가 루트 RectTransform 값을 초기화하므로 마지막에 다시 복구한다.
        Stretch(root);
        root.localScale = Vector3.one;
        root.pivot = new Vector2(0.5f, 0.5f);
        root.gameObject.AddComponent<GraphicRaycaster>();
        root.gameObject.AddComponent<KeyBindingPopup>();
        root.gameObject.AddComponent<NoJuicyButtonMotion>();

        var shadow = Image("PanelShadow", root, new Vector2(1060, 920), new Color(0.15f, 0.09f, 0.05f, 0.45f));
        shadow.anchoredPosition = new Vector2(12, -14);
        ApplyRounded(shadow);

        var panel = Image("Panel", root, new Vector2(1060, 920), new Color(1f, 0.965f, 0.88f, 1f));
        ApplyRounded(panel);
        var header = Image("Header", panel, new Vector2(990, 100), new Color(1f, 0.57f, 0.16f, 1f));
        header.anchoredPosition = new Vector2(0, 390);
        ApplyRounded(header);
        var title = Label("Title", header, "키 설정", 32, Vector2.zero, new Vector2(500, 60), FontStyles.Bold);
        title.color = Color.white;
        Label("Guide", panel, "바꾸고 싶은 키를 누른 뒤 새 키를 입력하세요 · 변경 내용은 자동 저장됩니다", 18,
            new Vector2(0, 320), new Vector2(900, 36));

        var scrollRoot = Image("ScrollView", panel, new Vector2(930, 620), new Color(1f, 0.91f, 0.75f, 0.58f));
        scrollRoot.anchoredPosition = new Vector2(-8, -8);
        ApplyRounded(scrollRoot);
        var scrollRect = scrollRoot.gameObject.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.scrollSensitivity = 36f;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;

        var viewport = Image("Viewport", scrollRoot, Vector2.zero, new Color(1f, 1f, 1f, 0.01f));
        Stretch(viewport, new Vector2(12, 12), new Vector2(-30, -12));
        viewport.gameObject.AddComponent<RectMask2D>();
        scrollRect.viewport = viewport;

        float contentHeight = kRowCapacity * 58f + 8f;
        var content = Rect("Content", viewport, Vector2.zero, new Vector2(0, contentHeight));
        content.anchorMin = new Vector2(0, 1);
        content.anchorMax = new Vector2(1, 1);
        content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        scrollRect.content = content;

        for (int i = 0; i < kRowCapacity; i++)
            BuildRow(content, i);

        var scrollbarRoot = Image("Scrollbar", scrollRoot, new Vector2(16, 584), new Color(0.66f, 0.55f, 0.43f, 0.32f));
        scrollbarRoot.anchorMin = scrollbarRoot.anchorMax = new Vector2(1f, 0.5f);
        scrollbarRoot.anchoredPosition = new Vector2(-10, 0);
        var slidingArea = Rect("Sliding Area", scrollbarRoot, Vector2.zero, Vector2.zero);
        Stretch(slidingArea, new Vector2(2, 2), new Vector2(-2, -2));
        var handle = Image("Handle", slidingArea, Vector2.zero, new Color(1f, 0.58f, 0.18f, 1f));
        Stretch(handle);
        var scrollbar = scrollbarRoot.gameObject.AddComponent<Scrollbar>();
        scrollbar.handleRect = handle;
        scrollbar.targetGraphic = handle.GetComponent<Image>();
        scrollbar.direction = Scrollbar.Direction.BottomToTop;
        scrollRect.verticalScrollbar = scrollbar;
        scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

        Label("StatusText", panel, "키 항목을 선택하세요.", 17, new Vector2(0, -342), new Vector2(760, 32));
        var resetAll = Button("ResetAllButton", panel, "전체 기본값", new Vector2(-160, -402), new Vector2(270, 56), new Color(0.82f, 0.86f, 0.84f, 1f));
        var close = Button("CloseButton", panel, "완료", new Vector2(160, -402), new Vector2(270, 56), new Color(1f, 0.57f, 0.16f, 1f));
        resetAll.navigation = close.navigation = new Navigation { mode = Navigation.Mode.Automatic };

        JobsnailUiKit.ApplyFontPolicy(root);
        PrefabUtility.SaveAsPrefabAsset(root.gameObject, kPopupPath);
        Object.DestroyImmediate(root.gameObject);
    }

    private static void BuildRow(RectTransform content, int index)
    {
        var row = Image($"BindingRow_{index:00}", content, new Vector2(0, 54),
            index % 2 == 0 ? new Color(1f, 0.99f, 0.96f, 0.96f) : new Color(1f, 0.94f, 0.84f, 0.72f));
        ApplyRounded(row);
        row.anchorMin = new Vector2(0, 1);
        row.anchorMax = new Vector2(1, 1);
        row.pivot = new Vector2(0.5f, 1f);
        row.offsetMin = new Vector2(6, -(index * 58f + 54f));
        row.offsetMax = new Vector2(-6, -index * 58f);
        row.gameObject.AddComponent<KeyBindingRow>();

        var action = Label("ActionLabel", row, "동작", 19, Vector2.zero, Vector2.zero);
        action.rectTransform.anchorMin = new Vector2(0.03f, 0);
        action.rectTransform.anchorMax = new Vector2(0.43f, 1);
        action.rectTransform.offsetMin = action.rectTransform.offsetMax = Vector2.zero;
        action.alignment = TextAlignmentOptions.MidlineLeft;

        var rebind = Button("RebindButton", row, "키", Vector2.zero, Vector2.zero, new Color(1f, 0.77f, 0.42f, 1f));
        var rebindRt = (RectTransform)rebind.transform;
        rebindRt.anchorMin = new Vector2(0.45f, 0.13f);
        rebindRt.anchorMax = new Vector2(0.82f, 0.87f);
        rebindRt.offsetMin = rebindRt.offsetMax = Vector2.zero;
        rebind.GetComponentInChildren<TextMeshProUGUI>().name = "BindingLabel";

        var reset = Button("ResetButton", row, "초기화", Vector2.zero, Vector2.zero, new Color(0.84f, 0.84f, 0.80f, 1f));
        var resetRt = (RectTransform)reset.transform;
        resetRt.anchorMin = new Vector2(0.84f, 0.13f);
        resetRt.anchorMax = new Vector2(0.98f, 0.87f);
        resetRt.offsetMin = resetRt.offsetMax = Vector2.zero;
    }

    private static void PatchGameHud()
    {
        if (!File.Exists(kGameHudPath)) return;
        var root = PrefabUtility.LoadPrefabContents(kGameHudPath);
        try
        {
            var settings = Find(root.transform, "InGameSettingsPopup");
            if (settings == null) return;
            if (settings.GetComponent<NoJuicyButtonMotion>() == null)
                settings.gameObject.AddComponent<NoJuicyButtonMotion>();
            if (settings is RectTransform settingsRect)
            {
                if (settingsRect.TryGetComponent(out Image settingsImage))
                {
                    settingsImage.color = new Color(1f, 0.965f, 0.88f, 0.99f);
                    ApplyRounded(settingsRect);
                }
            }
            var exit = Find(settings, "ExitGameButton") as RectTransform;
            if (exit != null) exit.anchoredPosition = new Vector2(0, -148);
            if (Find(settings, "KeySettingsButton") == null)
                Button("KeySettingsButton", settings, "키 설정", new Vector2(0, -78), new Vector2(320, 52), new Color(0.74f, 0.84f, 0.96f, 1f));
            foreach (var button in settings.GetComponentsInChildren<Button>(true))
            {
                if (button.transform is RectTransform buttonRect)
                    ApplyRounded(buttonRect);
                if (button.name == "KeySettingsButton" && button.targetGraphic != null)
                    button.targetGraphic.color = new Color(1f, 0.77f, 0.42f, 1f);
                else if (button.name == "ExitGameButton" && button.targetGraphic != null)
                    button.targetGraphic.color = new Color(0.92f, 0.76f, 0.70f, 1f);
            }
            PrefabUtility.SaveAsPrefabAsset(root, kGameHudPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static Transform Find(Transform root, string name)
    {
        foreach (var child in root.GetComponentsInChildren<Transform>(true))
            if (child.name == name) return child;
        return null;
    }

    private static RectTransform Rect(string name, Transform parent, Vector2 anchored, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        if (parent != null) go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchored;
        rt.sizeDelta = size;
        return rt;
    }

    private static RectTransform Image(string name, Transform parent, Vector2 size, Color color)
    {
        var rt = Rect(name, parent, Vector2.zero, size);
        var image = rt.gameObject.AddComponent<Image>();
        image.color = color;
        return rt;
    }

    private static TextMeshProUGUI Label(string name, Transform parent, string text, float size, Vector2 anchored,
        Vector2 boxSize, FontStyles style = FontStyles.Normal)
    {
        var rt = Rect(name, parent, anchored, boxSize);
        var label = rt.gameObject.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.font = JobsnailUiKit.TmpFont;
        label.fontSize = size;
        label.fontStyle = style;
        label.color = new Color(0.20f, 0.17f, 0.14f, 1f);
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;
        return label;
    }

    private static Button Button(string name, Transform parent, string text, Vector2 anchored, Vector2 size, Color color)
    {
        var rt = Image(name, parent, size, color);
        ApplyRounded(rt);
        rt.anchoredPosition = anchored;
        var button = rt.gameObject.AddComponent<Button>();
        button.targetGraphic = rt.GetComponent<Image>();
        var colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1f, 0.93f, 0.78f, 1f);
        colors.pressedColor = new Color(0.88f, 0.82f, 0.72f, 1f);
        colors.selectedColor = colors.highlightedColor;
        colors.disabledColor = new Color(0.75f, 0.75f, 0.75f, 0.55f);
        colors.colorMultiplier = 1f;
        button.colors = colors;
        var label = Label("Label", rt, text, 18, Vector2.zero, Vector2.zero);
        Stretch(label.rectTransform);
        return button;
    }

    private static void ApplyRounded(RectTransform rect)
    {
        if (rect == null || !rect.TryGetComponent(out Image image)) return;
        image.sprite = Resources.Load<Sprite>("UI_pngs/MyPage/RoundRect");
        if (image.sprite != null)
            image.type = UnityEngine.UI.Image.Type.Sliced;
    }

    private static void Stretch(RectTransform rt, Vector2? minOffset = null, Vector2? maxOffset = null)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = minOffset ?? Vector2.zero;
        rt.offsetMax = maxOffset ?? Vector2.zero;
    }
}
