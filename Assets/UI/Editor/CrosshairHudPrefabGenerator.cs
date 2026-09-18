using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// CrosshairHUD 프리팹 생성기(1회 실행 → 이후 프리팹을 에디터에서 직접 편집).
/// Assets/Resources/UI/HUD/CrosshairHUD.prefab → UIManager.ShowHUDUI&lt;CrosshairHUD&gt;()로 표시.
/// </summary>
public static class CrosshairHudPrefabGenerator
{
    public const string kPath = "Assets/Resources/UI/HUD/CrosshairHUD.prefab";

    // 1920x1080 기준 px. 모양 바꾸면 메뉴로 재생성(또는 프리팹을 에디터에서 직접 편집)
    private const float kThickness = 2f;    // 팔 굵기 = 중앙 점 크기
    private const float kArmLength = 9f;    // 팔 길이
    private const float kGap       = 5f;    // 중앙에서 팔 시작까지 빈 거리

    [MenuItem("Jobsnail/UI/Generate Crosshair Prefab")]
    public static void Generate()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(kPath));

        var root = new GameObject("CrosshairHUD", typeof(RectTransform));
        var rootRt = (RectTransform)root.transform;
        rootRt.anchorMin = Vector2.zero;
        rootRt.anchorMax = Vector2.one;
        rootRt.offsetMin = Vector2.zero;
        rootRt.offsetMax = Vector2.zero;
        root.AddComponent<CrosshairHUD>();

        // 십자 조준선: 가운데를 비운 팔 4개 + 중앙 점. 어두운 테두리를 먼저 깔고 흰색을 얹는다 — 밝은 바닥·어두운 건물 어디서든 보이게
        var outline = new Color(0f, 0f, 0f, 0.55f);
        for (int pass = 0; pass < 2; pass++)
        {
            float pad = pass == 0 ? 1f : 0f;   // 0 = 테두리(사방 1px 크게), 1 = 흰색
            var color = pass == 0 ? outline : Color.white;
            string tag = pass == 0 ? "Outline" : "Arm";
            float off = kGap + kArmLength * 0.5f;
            Bar(tag + "Up",    root.transform, new Vector2(0f,  off), new Vector2(kThickness + pad * 2f, kArmLength + pad * 2f), color);
            Bar(tag + "Down",  root.transform, new Vector2(0f, -off), new Vector2(kThickness + pad * 2f, kArmLength + pad * 2f), color);
            Bar(tag + "Left",  root.transform, new Vector2(-off, 0f), new Vector2(kArmLength + pad * 2f, kThickness + pad * 2f), color);
            Bar(tag + "Right", root.transform, new Vector2( off, 0f), new Vector2(kArmLength + pad * 2f, kThickness + pad * 2f), color);
            Bar(tag + "Dot",   root.transform, Vector2.zero,          Vector2.one * (kThickness + pad * 2f),                    color);
        }

        if (File.Exists(kPath)) AssetDatabase.DeleteAsset(kPath);
        PrefabUtility.SaveAsPrefabAsset(root, kPath);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[CrosshairHudPrefabGenerator] 생성 완료 → {kPath}");
    }

    private static void Bar(string name, Transform parent, Vector2 pos, Vector2 size, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        var img = go.GetComponent<Image>();
        img.color = color;
        img.raycastTarget = false;   // 클릭을 먹지 않게
    }
}
