using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// EmoteWheelUI 프리팹 생성기(1회 실행 → 이후 프리팹을 에디터에서 직접 편집).
/// 프리팹에는 배경(어두운 원판 + 중앙 링 + 안내 문구)만 둔다 —
/// 대사 섹터·경계선은 EmoteWheelUI.Init()이 EmoteDefs 목록에서 런타임 생성(대사 변경 시 재생성 불필요).
/// </summary>
public static class EmoteWheelPrefabGenerator
{
    private const string kPath = "Assets/Resources/UI/HUD/EmoteWheelUI.prefab";
    private const string kDiscPath = "Assets/Resources/UI_pngs/EmoteWheel_Disc.png";
    private const string kRingPath = "Assets/Resources/UI_pngs/EmoteWheel_Ring.png";

    [MenuItem("Jobsnail/UI/Generate EmoteWheel Prefab")]
    public static void Generate()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(kPath));
        var disc = EnsureDiscSprite();
        var ring = EnsureRingSprite();

        var root = new GameObject("EmoteWheelUI", typeof(RectTransform));
        var rootRt = (RectTransform)root.transform;
        rootRt.anchorMin = Vector2.zero;
        rootRt.anchorMax = Vector2.one;
        rootRt.offsetMin = Vector2.zero;
        rootRt.offsetMax = Vector2.zero;
        root.AddComponent<EmoteWheelUI>();

        var wheel = new GameObject("Wheel", typeof(RectTransform), typeof(Image));
        wheel.transform.SetParent(root.transform, false);
        var wrt = (RectTransform)wheel.transform;
        wrt.anchorMin = wrt.anchorMax = new Vector2(0.5f, 0.5f);
        wrt.anchoredPosition = Vector2.zero;
        wrt.sizeDelta = new Vector2(680f, 680f);
        var wImg = wheel.GetComponent<Image>();
        wImg.sprite = disc;
        wImg.color = new Color(0f, 0f, 0f, 0.5f);
        wImg.raycastTarget = false;
        wheel.AddComponent<UiPopIn>();

        // 중앙 링 + 안내
        var center = new GameObject("CenterRing", typeof(RectTransform), typeof(Image));
        center.transform.SetParent(wheel.transform, false);
        var crt = (RectTransform)center.transform;
        crt.anchorMin = crt.anchorMax = new Vector2(0.5f, 0.5f);
        crt.sizeDelta = new Vector2(150f, 150f);
        var cImg = center.GetComponent<Image>();
        cImg.sprite = ring;
        cImg.color = new Color(1f, 1f, 1f, 0.92f);
        cImg.raycastTarget = false;
        MakeText("CenterLabel", wheel.transform, "감정표현\n<size=12>T 누른 채 선택</size>", 18, Vector2.zero, new Vector2(140f, 60f));

        SavePrefab(root, kPath);
        Debug.Log($"[EmoteWheelPrefabGenerator] 생성 완료 → {kPath}");
    }

    private static Text MakeText(string name, Transform parent, string content, int size, Vector2 pos, Vector2 sizeDelta)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = sizeDelta;
        var t = go.GetComponent<Text>();
        t.font = JobsnailUiKit.LegacyFont;
        t.text = content;
        t.fontSize = size;
        t.color = Color.white;
        t.alignment = TextAnchor.MiddleCenter;
        t.supportRichText = true;
        t.raycastTarget = false;
        return t;
    }

    // 어두운 원판(중앙 구멍 + 가장자리 페이드)
    private static Sprite EnsureDiscSprite()
    {
        var existing = AssetDatabase.LoadAssetAtPath<Sprite>(kDiscPath);
        if (existing != null) return existing;
        const int kSize = 512;
        var px = new Color32[kSize * kSize];
        float half = kSize * 0.5f;
        for (int y = 0; y < kSize; y++)
            for (int x = 0; x < kSize; x++)
            {
                float r = Vector2.Distance(new Vector2(x, y), new Vector2(half, half)) / half;
                float hole = Mathf.InverseLerp(0.20f, 0.25f, r);
                float edge = 1f - Mathf.InverseLerp(0.90f, 1f, r);
                px[y * kSize + x] = new Color32(255, 255, 255, (byte)(255f * Mathf.Clamp01(hole * edge)));
            }
        return SaveSprite(kDiscPath, kSize, px);
    }

    // 중앙 흰 링(테두리만)
    private static Sprite EnsureRingSprite()
    {
        var existing = AssetDatabase.LoadAssetAtPath<Sprite>(kRingPath);
        if (existing != null) return existing;
        const int kSize = 256;
        var px = new Color32[kSize * kSize];
        float half = kSize * 0.5f;
        for (int y = 0; y < kSize; y++)
            for (int x = 0; x < kSize; x++)
            {
                float r = Vector2.Distance(new Vector2(x, y), new Vector2(half, half)) / half;
                float band = Mathf.Clamp01(1f - Mathf.Abs(r - 0.93f) / 0.05f);   // r≈0.93 얇은 띠
                px[y * kSize + x] = new Color32(255, 255, 255, (byte)(255f * band));
            }
        return SaveSprite(kRingPath, kSize, px);
    }

    private static Sprite SaveSprite(string path, int size, Color32[] px)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.SetPixels32(px);
        tex.Apply();
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        imp.textureType = TextureImporterType.Sprite;
        imp.alphaIsTransparency = true;
        imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
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
