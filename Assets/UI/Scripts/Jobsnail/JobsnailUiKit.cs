using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public static class JobsnailUiKit
{
    public static readonly Color Cream = new(1f, 0.96f, 0.78f, 1f);
    public static readonly Color Apricot = new(1f, 0.79f, 0.46f, 1f);
    public static readonly Color Brown = new(0.22f, 0.14f, 0.09f, 1f);
    public static readonly Color SoftGray = new(0.82f, 0.82f, 0.82f, 1f);

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
