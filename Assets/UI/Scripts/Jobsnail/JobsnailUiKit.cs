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

    public static Font LegacyFont
    {
        get
        {
            if (s_LegacyFont != null)
                return s_LegacyFont;

#if UNITY_EDITOR
            s_LegacyFont = AssetDatabase.LoadAssetAtPath<Font>("Assets/Font/서울한강 장체M.ttf");
            if (s_LegacyFont != null)
                return s_LegacyFont;
#endif

            s_LegacyFont = Font.CreateDynamicFontFromOSFont("서울한강 장체 M", 16);
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

#if UNITY_EDITOR
            s_TmpFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Font/서울한강 장체M SDF.asset");
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
