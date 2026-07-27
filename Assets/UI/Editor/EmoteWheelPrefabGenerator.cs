using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// EmoteWheelUI 프리팹 생성기(1회 실행 → 이후 프리팹을 에디터에서 직접 편집).
/// 오버워치식 원형 휠: 어두운 원판 + 중앙 링 + 살 경계선 + '이모지 그림 + 흰 글씨'(박스 없음).
/// 이모지: EmojiOne 아틀라스 UV 크롭(EmoteBubble과 동일 규칙) / 붐업·붐따 = 전용 텍스처 / 하트 = 절차 생성 PNG.
/// </summary>
public static class EmoteWheelPrefabGenerator
{
    private const string kPath = "Assets/Resources/UI/HUD/EmoteWheelUI.prefab";
    private const string kDiscPath = "Assets/Resources/UI_pngs/EmoteWheel_Disc.png";
    private const string kRingPath = "Assets/Resources/UI_pngs/EmoteWheel_Ring.png";
    private const string kHeartPath = "Assets/Resources/UI_pngs/EmoteWheel_Heart.png";

    private const string kAtlasPath = "Assets/TextMesh Pro/Sprites/EmojiOne.png";
    private const string kThumbsUpPath = "Assets/Player/Textures/Emoji_ThumbsUp.png";
    private const string kThumbsDownPath = "Assets/Player/Textures/Emoji_ThumbsDown.png";

    // PlayerEmote 매핑과 동일(0=F1 … 9=F10). 아틀라스 인덱스(-1=하트PNG, -2=붐따, -3=붐업) = PlayerEmote.kEmojiForKey.
    private static readonly string[] kNames = { "하트깨짐", "반함", "멋짐", "붐업", "메롱", "힘듦", "폭소", "미소", "시무룩", "붐따" };
    private static readonly int[] kEmoji = { -1, 2, 3, -3, 11, 10, 13, 0, 15, -2 };

    [MenuItem("Jobsnail/UI/Generate EmoteWheel Prefab")]
    public static void Generate()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(kPath));
        var disc = EnsureDiscSprite();
        var ring = EnsureRingSprite();
        var heart = EnsureHeartTexture();
        var atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(kAtlasPath);
        var up = AssetDatabase.LoadAssetAtPath<Texture2D>(kThumbsUpPath);
        var down = AssetDatabase.LoadAssetAtPath<Texture2D>(kThumbsDownPath);
        if (atlas == null) Debug.LogWarning($"[EmoteWheel] 아틀라스 없음: {kAtlasPath}");

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

        // 살 경계선(섹터 사이) — 얇은 흰 선 10개
        for (int i = 0; i < 10; i++)
        {
            float deg = 90f - (i * 36f) - 18f;   // 버튼 사이 경계각
            var line = new GameObject("Spoke", typeof(RectTransform), typeof(Image));
            line.transform.SetParent(wheel.transform, false);
            var lrt = (RectTransform)line.transform;
            lrt.anchorMin = lrt.anchorMax = new Vector2(0.5f, 0.5f);
            lrt.sizeDelta = new Vector2(190f, 2f);
            float mid = (78f + 268f) * 0.5f;
            lrt.anchoredPosition = new Vector2(Mathf.Cos(deg * Mathf.Deg2Rad), Mathf.Sin(deg * Mathf.Deg2Rad)) * mid;
            lrt.localRotation = Quaternion.Euler(0f, 0f, deg);
            var li = line.GetComponent<Image>();
            li.color = new Color(1f, 1f, 1f, 0.16f);
            li.raycastTarget = false;
        }

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
        MakeText("CenterLabel", wheel.transform, "감정표현\n<size=12>T 누른 채 클릭</size>", 18, Vector2.zero, new Vector2(140f, 60f));

        // 10방향: 이모지 그림(위) + 이름(아래) — 박스 없음, 투명 히트영역만
        const float kRadius = 225f;
        for (int i = 0; i < 10; i++)
        {
            float ang = (90f - i * 36f) * Mathf.Deg2Rad;
            var pos = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * kRadius;

            var go = new GameObject($"Emote{i}", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(wheel.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(110f, 100f);
            var hit = go.GetComponent<Image>();
            hit.color = new Color(1f, 1f, 1f, 0f);   // 투명 히트영역(호버·클릭용)
            hit.raycastTarget = true;

            // 이모지 그림
            var icon = new GameObject("Icon", typeof(RectTransform), typeof(RawImage));
            icon.transform.SetParent(go.transform, false);
            var irt = (RectTransform)icon.transform;
            irt.anchorMin = irt.anchorMax = new Vector2(0.5f, 0.5f);
            irt.anchoredPosition = new Vector2(0f, 16f);
            irt.sizeDelta = new Vector2(54f, 54f);
            var ri = icon.GetComponent<RawImage>();
            ri.raycastTarget = false;
            int code = kEmoji[i];
            if (code == -1) ri.texture = heart;
            else if (code == -2) ri.texture = down;
            else if (code == -3) ri.texture = up;
            else if (atlas != null)
            {
                ri.texture = atlas;   // EmojiOne 4x4 아틀라스 UV 크롭(row 0 = 위)
                int col = code % 4, row = code / 4;
                ri.uvRect = new Rect(col / 4f, 1f - (row + 1) / 4f, 0.25f, 0.25f);
            }

            var label = MakeText("Label", go.transform, kNames[i], 16, new Vector2(0f, -28f), new Vector2(108f, 24f));
            label.color = Color.white;
        }

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

    // 하트 아이콘(F1 파티클 슬롯용) — 음함수 (x²+y²-1)³ - x²y³ ≤ 0
    private static Texture2D EnsureHeartTexture()
    {
        var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(kHeartPath);
        if (existing != null) return existing;
        const int kSize = 128;
        var px = new Color32[kSize * kSize];
        var red = new Color32(235, 75, 90, 255);
        for (int y = 0; y < kSize; y++)
            for (int x = 0; x < kSize; x++)
            {
                float fx = (x / (float)kSize - 0.5f) * 3.0f;
                float fy = (y / (float)kSize - 0.42f) * 3.0f;
                float v = fx * fx + fy * fy - 1f;
                bool inside = v * v * v - fx * fx * fy * fy * fy <= 0f;
                px[y * kSize + x] = inside ? red : new Color32(0, 0, 0, 0);
            }
        SaveSprite(kHeartPath, kSize, px);
        return AssetDatabase.LoadAssetAtPath<Texture2D>(kHeartPath);
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
