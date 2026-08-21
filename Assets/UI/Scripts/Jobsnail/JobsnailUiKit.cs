using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

public static class JobsnailUiKit
{
    public static readonly Color Cream = new(1f, 0.96f, 0.78f, 1f);
    public static readonly Color Apricot = new(1f, 0.79f, 0.46f, 1f);
    public static readonly Color Brown = new(0.22f, 0.14f, 0.09f, 1f);
    public static readonly Color SoftGray = new(0.82f, 0.82f, 0.82f, 1f);

    private static Font s_LegacyFont;
    private static TMP_FontAsset s_TmpFont;
    private static JobsnailFontSet s_FontSet;

    private static JobsnailFontSet FontSet
        => s_FontSet != null ? s_FontSet : s_FontSet = Resources.Load<JobsnailFontSet>("UI/Jobsnail/JobsnailFontSet");

    public static Font LegacyFont
    {
        get
        {
            if (s_LegacyFont != null)
                return s_LegacyFont;

            s_LegacyFont = FontSet != null ? FontSet.LegacyFont : null;
            if (s_LegacyFont != null)
                return s_LegacyFont;

#if UNITY_EDITOR
            s_LegacyFont = AssetDatabase.LoadAssetAtPath<Font>("Assets/Font/SUITE/SUITE-Medium.ttf");
            if (s_LegacyFont != null)
                return s_LegacyFont;
#endif

            s_LegacyFont = Font.CreateDynamicFontFromOSFont("SUITE Medium", 16);
            if (s_LegacyFont == null)
                s_LegacyFont = Font.CreateDynamicFontFromOSFont("SeoulHangang", 16);
            if (s_LegacyFont == null)
                s_LegacyFont = Font.CreateDynamicFontFromOSFont("SeoulHangangC", 16);
            if (s_LegacyFont == null)
                s_LegacyFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (s_LegacyFont == null)
                s_LegacyFont = Font.CreateDynamicFontFromOSFont("Apple SD Gothic Neo", 16);
            return s_LegacyFont;
        }
    }

    public static TMP_FontAsset TmpFont
    {
        get
        {
            if (s_TmpFont != null)
                return s_TmpFont;

            s_TmpFont = FontSet != null ? FontSet.TmpFont : null;
            if (s_TmpFont != null)
                return s_TmpFont;

#if UNITY_EDITOR
            s_TmpFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Font/SUITE/SUITE-Medium SDF.asset");
            if (s_TmpFont != null)
                return s_TmpFont;
#endif

            s_TmpFont = Resources.Load<TMP_FontAsset>("Fonts/서울한강 장체M SDF");
            if (s_TmpFont == null)
                s_TmpFont = TMP_Settings.defaultFontAsset;
            return s_TmpFont;
        }
    }

    public static Sprite Sprite(string resourcesPath) => Resources.Load<Sprite>(resourcesPath);

    /// <summary>기존에 프리팹에 저장된 폰트까지 SUITE Medium으로 통일한다.</summary>
    public static void ApplyFontPolicy(Transform root)
    {
        if (root == null)
            return;

        Font legacy = LegacyFont;
        if (legacy != null)
            foreach (Text text in root.GetComponentsInChildren<Text>(true))
                text.font = legacy;

        TMP_FontAsset tmp = TmpFont;
        if (tmp != null)
            foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
                text.font = tmp;
    }

    public static RectTransform Rect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchored, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchored;
        rt.sizeDelta = size;
        return rt;
    }

    public static Image Image(string name, Transform parent, Sprite sprite, Color? color = null)
    {
        var rt = Rect(name, parent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var image = rt.gameObject.AddComponent<Image>();
        image.sprite = sprite;
        image.color = color ?? Color.white;
        image.preserveAspect = sprite != null;
        image.raycastTarget = false;
        return image;
    }

    /// <summary>배경 이미지를 화면에 꽉 채운다(비율 유지 + 넘치는 부분 크롭). 레터박스 여백 제거용.</summary>
    public static void CoverFill(Image image)
    {
        if (image == null || image.sprite == null) return;
        image.preserveAspect = false;                       // 종횡비는 Fitter가 담당
        var rt = image.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        var fitter = image.GetComponent<AspectRatioFitter>();
        if (fitter == null) fitter = image.gameObject.AddComponent<AspectRatioFitter>();
        fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;   // 부모를 덮도록 확대(크롭)
        var s = image.sprite.rect;
        fitter.aspectRatio = s.height > 0f ? s.width / s.height : 1.777f;
    }

    public static Image Box(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchored, Vector2 size, Color color)
    {
        var rt = Rect(name, parent, anchorMin, anchorMax, anchored, size);
        var image = rt.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    public static Button Button(string name, Transform parent, Sprite sprite, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchored, Vector2 size, UnityAction onClick, string fallbackText = null)
    {
        var rt = Rect(name, parent, anchorMin, anchorMax, anchored, size);
        var image = rt.gameObject.AddComponent<Image>();
        image.sprite = sprite;
        image.color = Color.white;
        image.preserveAspect = sprite != null;

        var button = rt.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(PlayUIClick);
        if (onClick != null)
            button.onClick.AddListener(onClick);
        JuicyButton.Attach(button);   // 모든 킷 버튼 = 쫀득(호버·눌림·복귀)

        if (!string.IsNullOrEmpty(fallbackText) && sprite == null)
        {
            image.color = new Color(1f, 0.78f, 0.44f, 1f);
            Label("Label", rt, fallbackText, 22, Brown, TextAlignmentOptions.Center, Vector2.zero, Vector2.zero);
        }

        return button;
    }

    public static TextMeshProUGUI Label(string name, Transform parent, string text, int size, Color color, TextAlignmentOptions align, Vector2 anchored, Vector2 boxSize)
    {
        RectTransform rt;
        if (boxSize == Vector2.zero)
            rt = Rect(name, parent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        else
            rt = Rect(name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), anchored, boxSize);

        var label = rt.gameObject.AddComponent<TextMeshProUGUI>();
        label.text = text;
        if (TmpFont != null)
            label.font = TmpFont;
        label.fontSize = size;
        label.color = color;
        label.alignment = align;
        label.textWrappingMode = TextWrappingModes.Normal;
        return label;
    }

    public static Canvas EnsureOverlayCanvas(string name, int sortingOrder)
    {
        var existing = GameObject.Find(name);
        if (existing != null && existing.TryGetComponent(out Canvas existingCanvas))
            return existingCanvas;

        var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;

        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        return canvas;
    }

    private static void PlayUIClick()
    {
        if (SoundManager.Instance != null)
            SoundManager.Instance.PlaySFX(SFXType.UIClick);
    }
}
