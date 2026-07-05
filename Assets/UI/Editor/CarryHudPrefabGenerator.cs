using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// CarryHudUI 프리팹 생성기(1회 실행 → 이후 프리팹을 에디터에서 직접 편집).
/// Assets/Resources/UI/HUD/CarryHudUI.prefab → UIManager.ShowHUDUI&lt;CarryHudUI&gt;()로 표시.
/// 구 PlayerCarry.OnGUI 레이아웃(좌상단 패널 960x150 · 로딩바 96x12 · 힌트 280x20)을 그대로 재현.
/// </summary>
public static class CarryHudPrefabGenerator
{
    private const string kPath = "Assets/Resources/UI/HUD/CarryHudUI.prefab";

    [MenuItem("Jobsnail/UI/Generate CarryHud Prefab")]
    public static void Generate()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(kPath));

        var root = new GameObject("CarryHudUI", typeof(RectTransform));
        Stretch((RectTransform)root.transform);
        root.AddComponent<CarryHudUI>();

        // ── 좌상단 조작법/상태 패널 ──
        var hint = Panel("HintPanel", root.transform, new Color(0f, 0f, 0f, 0.6f));
        var hintRt = (RectTransform)hint.transform;
        hintRt.anchorMin = hintRt.anchorMax = new Vector2(0f, 1f);
        hintRt.pivot = new Vector2(0f, 1f);
        hintRt.anchoredPosition = new Vector2(10f, -10f);
        hintRt.sizeDelta = new Vector2(960f, 150f);
        var hintText = MakeText("HintText", hint.transform, 18, TextAnchor.UpperLeft);
        hintText.rectTransform.anchorMin = Vector2.zero;
        hintText.rectTransform.anchorMax = Vector2.one;
        hintText.rectTransform.offsetMin = new Vector2(8f, 6f);
        hintText.rectTransform.offsetMax = new Vector2(-8f, -6f);

        // ── E 공정 / Z 되돌리기 로딩바(위치는 런타임에 스크린 좌표로 지정) ──
        MakeBar("ProcessBar", root.transform, new Color(0.35f, 0.60f, 1.00f));
        MakeBar("RevertBar", root.transform, new Color(0.90f, 0.45f, 0.30f));

        // ── 공정 안내(도구 들고 조준 시 · 대상 블록 위) ──
        var ph = Panel("ProcessHintPanel", root.transform, new Color(0f, 0f, 0f, 0.72f));
        var phRt = (RectTransform)ph.transform;
        phRt.anchorMin = phRt.anchorMax = new Vector2(0.5f, 0.5f);
        phRt.pivot = new Vector2(0.5f, 0f);                     // 기준점(블록 위) 위로 뜸
        phRt.sizeDelta = new Vector2(280f, 20f);
        var phText = MakeText("ProcessHintText", ph.transform, 13, TextAnchor.MiddleCenter);
        Stretch(phText.rectTransform);
        ph.SetActive(false);

        SavePrefab(root, kPath);
        Debug.Log($"[CarryHudPrefabGenerator] 생성 완료 → {kPath}");
    }

    // 로딩바: BG(검정) + Fill(색 · pivot 왼쪽 → localScale.x로 채움) + 위 라벨. 기본 비활성.
    private static void MakeBar(string name, Transform parent, Color fillColor)
    {
        var bar = new GameObject(name, typeof(RectTransform));
        bar.transform.SetParent(parent, false);
        var rt = (RectTransform)bar.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0f);                       // 기준점 위로 뜸(구 OnGUI와 동일)
        rt.sizeDelta = new Vector2(100f, 16f);

        var bg = Panel($"{name}Bg", bar.transform, new Color(0f, 0f, 0f, 0.7f));
        Stretch((RectTransform)bg.transform);

        var fill = Panel($"{name}Fill", bar.transform, fillColor);
        var frt = (RectTransform)fill.transform;
        frt.anchorMin = frt.anchorMax = new Vector2(0f, 0.5f);
        frt.pivot = new Vector2(0f, 0.5f);
        frt.anchoredPosition = new Vector2(2f, 0f);
        frt.sizeDelta = new Vector2(96f, 12f);

        var label = MakeText($"{name}Label", bar.transform, 13, TextAnchor.MiddleCenter);
        var lrt = label.rectTransform;
        lrt.anchorMin = lrt.anchorMax = new Vector2(0.5f, 1f);
        lrt.pivot = new Vector2(0.5f, 0f);
        lrt.anchoredPosition = new Vector2(0f, 4f);
        lrt.sizeDelta = new Vector2(156f, 18f);

        bar.SetActive(false);
    }

    private static GameObject Panel(string name, Transform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return go;
    }

    private static Text MakeText(string name, Transform parent, int size, TextAnchor anchor)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<Text>();
        t.font = JobsnailUiKit.LegacyFont;
        t.fontSize = size;
        t.color = Color.white;
        t.alignment = anchor;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        return t;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
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
