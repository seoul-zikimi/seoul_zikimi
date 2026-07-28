using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 마이페이지 프리팹 생성기 — Jobsnail ▸ UI ▸ Generate MyPage Prefab.
/// ① 옷장 HUD(Resources/UI/HUD/MyPageUI): 레퍼런스 구조 — 오른쪽 반투명 패널(아이템 그리드 4×5)
///    + 패널 오른쪽 세로 카테고리 아이콘 열 + 우상단 X. 왼쪽은 씬의 3D 캐릭터.
/// ② 기록 책 팝업(Resources/UI/Popup/RecordBookUI): 펼친 책 양면 — 왼쪽(맵 이름·썸네일), 오른쪽(기록).
/// 생성 후 에디터에서 자유 편집(코드는 텍스트/상태만 갱신).
/// </summary>
public static class MyPagePrefabGenerator
{
    private const string kHudPath = "Assets/Resources/UI/HUD/MyPageUI.prefab";
    private const string kBookPath = "Assets/Resources/UI/Popup/RecordBookUI.prefab";

    private static readonly (string label, string prefix)[] kCategories =
    {
        ("전체", ""), ("캐릭터", "char_"), ("스킨", "skin_"), ("모자", "hat_"),
        ("옷", "cloth_"), ("가방", "bag_"), ("등껍질", "shell_"),
    };

    [MenuItem("Jobsnail/UI/Generate MyPage Prefab")]
    public static void Generate()
    {
        GenerateClosetHud();
        GenerateRecordBook();
        Debug.Log($"[MyPagePrefabGenerator] 생성 완료 → {kHudPath} + {kBookPath}");
    }

    // ── ① 옷장 HUD ──────────────────────────────────────────────────
    private static void GenerateClosetHud()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(kHudPath));
        var root = new GameObject("MyPageUI", typeof(RectTransform));
        Stretch((RectTransform)root.transform);
        var ui = root.AddComponent<MyPageUI>();

        // 좌상단: 코인
        JobsnailUiKit.Label("CoinText", root.transform, "보유 코인  0", 24, new Color(1f, 0.92f, 0.6f, 1f), TextAlignmentOptions.Left, new Vector2(0, 0), new Vector2(300, 40))
            .rectTransform.SetAnchor(new Vector2(0f, 1f), new Vector2(170, -50));

        // 우상단: X
        var close = MakeButton("CloseButton", root.transform, "X", Vector2.zero, new Vector2(54, 54), new Color(1f, 0.85f, 0.6f, 0.95f), 24);
        ((RectTransform)close.transform).SetAnchor(new Vector2(1f, 1f), new Vector2(-50, -50));

        // 오른쪽 옷장 패널 — GPT 생성 프레임 이미지(보라 반투명 유리+옷걸이 장식)
        var panelRt = JobsnailUiKit.Rect("Panel", root.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-400, 0), new Vector2(490, 1000));
        var panelImg = panelRt.gameObject.AddComponent<Image>();
        panelImg.sprite = EnsureSprite("Assets/Resources/UI_pngs/MyPage/Closet_Panel.png");
        panelImg.raycastTarget = true;   // 패널 뒤 클릭 방지
        var panel = panelRt.transform;
        JobsnailUiKit.Label("Title", panel, "옷 장", 30, new Color(0.42f, 0.36f, 0.55f, 1f), TextAlignmentOptions.Center, new Vector2(0, 400), new Vector2(220, 46));

        // 아이템 그리드 4×5 (빈 슬롯 — 아이템 생기면 채워짐)
        for (int i = 0; i < 20; i++)
        {
            int cx = i % 4, cy = i / 4;
            JobsnailUiKit.Box($"Slot{i}", panel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-150 + cx * 100, 290 - cy * 148), new Vector2(90, 90), new Color(1f, 1f, 1f, 0.85f));
        }
        // 빈 상태 안내(아이템 생기면 코드가 숨김/갱신)
        JobsnailUiKit.Label("ClosetList", panel, "", 20, new Color(0.95f, 0.95f, 1f, 1f), TextAlignmentOptions.Center, new Vector2(0, 0), new Vector2(480, 200));

        // 패널 오른쪽 세로 카테고리 열(레퍼런스의 원형 아이콘 자리 — 지금은 텍스트 버튼)
        for (int i = 0; i < kCategories.Length; i++)
        {
            var btn = MakeButton($"Cat{i}", root.transform, kCategories[i].label, Vector2.zero, new Vector2(92, 64), new Color(0.36f, 0.34f, 0.5f, 0.9f), 16);
            ((RectTransform)btn.transform).SetAnchor(new Vector2(1f, 0.5f), new Vector2(-62, 310 - i * 92));
            var t = btn.GetComponentInChildren<TextMeshProUGUI>();
            if (t != null) t.color = Color.white;
            UnityEventTools.AddStringPersistentListener(btn.onClick, ui.SetFilter, kCategories[i].prefix);
        }

        // 하단: 기록(책) / 적용 / 되돌리기
        var book = MakeButton("BookButton", root.transform, "기록", Vector2.zero, new Vector2(140, 56), new Color(0.98f, 0.90f, 0.66f, 0.95f), 20);
        ((RectTransform)book.transform).SetAnchor(new Vector2(0f, 0f), new Vector2(120, 60));
        var apply = MakeButton("ApplyButton", root.transform, "현재 모습 적용하기", Vector2.zero, new Vector2(250, 56), new Color(1f, 0.78f, 0.44f, 1f), 19);
        ((RectTransform)apply.transform).SetAnchor(new Vector2(0.5f, 0f), new Vector2(60, 60));
        var revert = MakeButton("RevertButton", root.transform, "되돌리기", Vector2.zero, new Vector2(160, 56), Color.white, 19);
        ((RectTransform)revert.transform).SetAnchor(new Vector2(0.5f, 0f), new Vector2(280, 60));

        SavePrefab(root, kHudPath);
    }

    // ── ② 기록 책 팝업(펼친 책 양면) ─────────────────────────────────
    [MenuItem("Jobsnail/UI/Generate RecordBook Prefab (책만)")]
    private static void GenerateRecordBook()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(kBookPath));
        var root = new GameObject("RecordBookUI", typeof(RectTransform));
        Stretch((RectTransform)root.transform);
        root.AddComponent<RecordBookUI>();

        var dim = JobsnailUiKit.Box("Dim", root.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Color(0f, 0f, 0f, 0.5f));
        dim.raycastTarget = true;

        // 책 배경 = GPT 생성 펼친 책 이미지(가죽 표지+양피지 양면)
        var coverRt = JobsnailUiKit.Rect("Cover", root.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1180, 680));
        var coverImg = coverRt.gameObject.AddComponent<Image>();
        coverImg.sprite = EnsureSprite("Assets/Resources/UI_pngs/MyPage/RecordBook_BG.png");
        coverImg.raycastTarget = true;
        var cover = coverRt.transform;

        // 왼쪽 페이지: 맵 이름 + 썸네일
        JobsnailUiKit.Label("BookMapName", cover, "맵 이름", 28, new Color(0.30f, 0.18f, 0.08f, 1f), TextAlignmentOptions.Center, new Vector2(-255, 230), new Vector2(420, 44));
        var thumbRt = JobsnailUiKit.Rect("BookThumb", cover, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-255, 20), new Vector2(380, 300));
        var thumb = thumbRt.gameObject.AddComponent<Image>();
        thumb.color = new Color(0.9f, 0.87f, 0.78f, 1f);   // 썸네일 없으면 빈 종이 느낌
        thumb.preserveAspect = true;

        // 오른쪽 페이지: 기록
        JobsnailUiKit.Label("BookRecords", cover, "", 21, new Color(0.20f, 0.15f, 0.10f, 1f), TextAlignmentOptions.TopLeft, new Vector2(255, 60), new Vector2(380, 300));
        JobsnailUiKit.Label("BookVersus", cover, "", 21, new Color(0.20f, 0.15f, 0.10f, 1f), TextAlignmentOptions.TopLeft, new Vector2(255, -170), new Vector2(380, 120));

        // 넘김 + 페이지 + 닫기 — 나무 아트 버튼(화살표는 좌우반전 재활용)
        MakeSpriteButton("PrevMapButton", cover, "Assets/Resources/UI_pngs/MyPage/Book_Arrow.png", new Vector2(-560, 0), new Vector2(76, 80));
        MakeSpriteButton("NextMapButton", cover, "Assets/Resources/UI_pngs/MyPage/Book_Arrow.png", new Vector2(560, 0), new Vector2(76, 80), flipX: true);
        JobsnailUiKit.Label("PageLabel", cover, "1 / 1", 18, new Color(0.55f, 0.42f, 0.28f, 1f), TextAlignmentOptions.Center, new Vector2(0, -310), new Vector2(160, 28));
        MakeSpriteButton("CloseButton", cover, "Assets/Resources/UI_pngs/MyPage/Book_Close.png", new Vector2(555, 300), new Vector2(58, 58));

        SavePrefab(root, kBookPath);
    }

    // ── 헬퍼 ─────────────────────────────────────────────────────────
    private static Sprite EnsureSprite(string assetPath)
    {
        var imp = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (imp != null && imp.textureType != TextureImporterType.Sprite)
        {
            imp.textureType = TextureImporterType.Sprite;
            imp.alphaIsTransparency = true;
            imp.SaveAndReimport();
        }
        var sp = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        if (sp == null) Debug.LogWarning($"[MyPage] 스프라이트 없음: {assetPath}");
        return sp;
    }

    private static void SetAnchor(this RectTransform rt, Vector2 anchor, Vector2 pos)
    {
        rt.anchorMin = rt.anchorMax = anchor;
        rt.anchoredPosition = pos;
    }

    /// <summary>아트 스프라이트 버튼(텍스트 없음 또는 오버레이). flipX=true면 좌우반전(오른쪽 화살표용).</summary>
    private static Button MakeSpriteButton(string name, Transform parent, string spritePath, Vector2 pos, Vector2 size, string label = null, int fontSize = 20, bool flipX = false)
    {
        var btn = JobsnailUiKit.Button(name, parent, null, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, size, null, label ?? "");
        var img = btn.GetComponent<Image>();
        if (img != null)
        {
            img.sprite = EnsureSprite(spritePath);
            img.color = Color.white;
            img.preserveAspect = true;
        }
        if (flipX) btn.transform.localScale = new Vector3(-1f, 1f, 1f);
        var txt = btn.GetComponentInChildren<TextMeshProUGUI>();
        if (txt != null)
        {
            if (string.IsNullOrEmpty(label)) Object.DestroyImmediate(txt.gameObject);
            else
            {
                txt.fontSize = fontSize;
                txt.color = new Color(0.35f, 0.30f, 0.50f, 1f);
                if (flipX) txt.rectTransform.localScale = new Vector3(-1f, 1f, 1f);   // 텍스트는 다시 원방향
            }
        }
        return btn;
    }

    private static Button MakeButton(string name, Transform parent, string label, Vector2 pos, Vector2 size, Color color, int fontSize)
    {
        var btn = JobsnailUiKit.Button(name, parent, null, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, size, null, label);
        var img = btn.GetComponent<Image>();
        if (img != null) img.color = color;
        var txt = btn.GetComponentInChildren<TextMeshProUGUI>();
        if (txt != null) { txt.fontSize = fontSize; txt.color = Color.black; }
        return btn;
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
