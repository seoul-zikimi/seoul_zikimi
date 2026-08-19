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

    private static readonly (string label, string prefix, string icon)[] kCategories =
    {
        ("전체", "", "Tab_All"), ("캐릭터", "char_", "Tab_Char"), ("스킨", "skin_", "Tab_Skin"),
        ("모자", "hat_", "Tab_Hat"), ("옷", "cloth_", "Tab_Cloth"), ("가방", "bag_", "Tab_Bag"),
        ("등껍질", "shell_", "Tab_Shell"),
    };

    // 피그마 리디자인 팔레트(보라 유리)
    private static readonly Color kPurpleDeep = new Color32(0x5A, 0x4E, 0x7D, 255);     // 제목/글자
    private static readonly Color kPurpleFill = new Color32(0x8B, 0x7B, 0xC5, 255);     // 채운 버튼
    private static readonly Color kPurpleSoft = new Color32(0xB9, 0xAE, 0xE0, 255);     // 탭 비활성
    private static readonly Color kPurpleLite = new Color32(0xED, 0xE8, 0xF8, 255);     // 밝은 면

    [MenuItem("Jobsnail/UI/Generate MyPage Prefab")]
    public static void Generate()
    {
        GenerateClosetHud();
        GenerateRecordBook();
        Debug.Log($"[MyPagePrefabGenerator] 생성 완료 → {kHudPath} + {kBookPath}");
    }

    // ── ① 옷장 HUD (피그마 새로4 리디자인) ───────────────────────────
    private static void GenerateClosetHud()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(kHudPath));
        var root = new GameObject("MyPageUI", typeof(RectTransform));
        Stretch((RectTransform)root.transform);
        var ui = root.AddComponent<MyPageUI>();
        string P = "Assets/Resources/UI_pngs/MyPage/";

        // 좌상단: ← 나가기 (보라 사각 화살표 + 라벨)
        var close = MakeSpriteButton("CloseButton", root.transform, P + "Icon_Back.png", Vector2.zero, new Vector2(64, 64));
        ((RectTransform)close.transform).SetAnchor(new Vector2(0f, 1f), new Vector2(64, -60));
        JobsnailUiKit.Label("ExitLabel", root.transform, "나가기", 24, Color.white, TextAlignmentOptions.Left, Vector2.zero, new Vector2(160, 40))
            .rectTransform.SetAnchor(new Vector2(0f, 1f), new Vector2(190, -60));

        // 우상단: 코인 필(pill) — 아이콘 + 수치
        var pillRt = JobsnailUiKit.Rect("CoinPill", root.transform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-140, -46), new Vector2(220, 52));
        var pillImg = pillRt.gameObject.AddComponent<Image>();
        pillImg.color = new Color(0.34f, 0.30f, 0.46f, 0.75f);
        Round(pillImg);
        var coinIcoRt = JobsnailUiKit.Rect("CoinIcon", pillRt, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(30, 0), new Vector2(38, 38));
        var coinIco = coinIcoRt.gameObject.AddComponent<Image>();
        coinIco.sprite = EnsureSprite(P + "Icon_Coin.png");
        coinIco.preserveAspect = true;
        var cap = JobsnailUiKit.Label("CoinCaption", pillRt, "보유 코인", 12, new Color(0.85f, 0.82f, 0.95f, 1f), TextAlignmentOptions.Right, new Vector2(15, 13), new Vector2(150, 16));
        cap.raycastTarget = false;
        JobsnailUiKit.Label("CoinText", pillRt, "0", 22, new Color(1f, 0.95f, 0.75f, 1f), TextAlignmentOptions.Right, new Vector2(-20, 0), new Vector2(150, 30))
            .rectTransform.anchoredPosition = new Vector2(15, -8);

        // 왼쪽 세로 네비: 옷장(활성) / 기록
        MakeNavButton("ClosetNav", root.transform, P, "Icon_Closet.png", "옷장", new Vector2(90, 60), true);
        var book = MakeNavButton("BookButton", root.transform, P, "Icon_Book.png", "기록", new Vector2(90, -110), false);

        // 오른쪽 옷장 패널 — 피그마 보라 유리 패널
        var panelRt = JobsnailUiKit.Rect("Panel", root.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-310, -8), new Vector2(560, 940));
        var panelImg = panelRt.gameObject.AddComponent<Image>();
        panelImg.sprite = EnsureSprite(P + "Closet_Panel2.png");
        panelImg.raycastTarget = true;   // 패널 뒤 클릭 방지
        var panel = panelRt.transform;

        // 제목 리본(가운데) + 양옆 별 + 패널 X
        var ribbon = JobsnailUiKit.Box("TitleRibbon", panel, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -40), new Vector2(240, 52), kPurpleFill);
        Round(ribbon);
        JobsnailUiKit.Label("Title", ribbon.rectTransform, "옷 장", 28, Color.white, TextAlignmentOptions.Center, Vector2.zero, new Vector2(240, 52));
        var sparkle = EnsureSprite(P + "Icon_Sparkle.png");
        foreach (var (sx, sn) in new[] { (-88f, "SparkleL"), (88f, "SparkleR") })
        {
            var spRt = JobsnailUiKit.Rect(sn, ribbon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(sx, 0), new Vector2(26, 26));
            var spImg = spRt.gameObject.AddComponent<Image>();
            spImg.sprite = sparkle;
            spImg.preserveAspect = true;
            spImg.raycastTarget = false;
        }
        var panelX = MakeSpriteButton("PanelClose", panel, P + "Icon_X.png", Vector2.zero, new Vector2(56, 56));
        ((RectTransform)panelX.transform).SetAnchor(new Vector2(1f, 1f), new Vector2(-52, -44));

        // 탭 줄 7개 — 아이콘 + 라벨(활성 = 밝은 배경, MyPageUI가 갱신)
        float tabW = 72f, tabGap = 4f;
        float tabX0 = -(kCategories.Length - 1) * (tabW + tabGap) * 0.5f - 18f;
        for (int i = 0; i < kCategories.Length; i++)
        {
            var tabBg = JobsnailUiKit.Box($"Cat{i}", panel, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(tabX0 + i * (tabW + tabGap), -132), new Vector2(tabW, 84), kPurpleSoft);
            Round(tabBg);
            var btn = tabBg.gameObject.AddComponent<Button>();
            btn.targetGraphic = tabBg;
            tabBg.raycastTarget = true;
            var icoRt = JobsnailUiKit.Rect("Icon", tabBg.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -24), new Vector2(28, 28));
            var ico = icoRt.gameObject.AddComponent<Image>();
            ico.sprite = EnsureSprite(P + kCategories[i].icon + ".png");
            ico.preserveAspect = true;
            ico.raycastTarget = false;
            var lbl = JobsnailUiKit.Label("Label", tabBg.rectTransform, kCategories[i].label, 13, Color.white, TextAlignmentOptions.Center, new Vector2(0, 19), new Vector2(tabW - 4, 22));
            lbl.rectTransform.anchorMin = lbl.rectTransform.anchorMax = new Vector2(0.5f, 0f);
            lbl.enableAutoSizing = true;
            lbl.fontSizeMin = 10; lbl.fontSizeMax = 13;
            lbl.raycastTarget = false;
            UnityEventTools.AddStringPersistentListener(btn.onClick, ui.SetFilter, kCategories[i].prefix);
        }

        // 섹션 4줄(피그마 구성): 캐릭터 / 등 껍질 / 옷 / 스킨 — 헤더 + 카드 5장(0번 = 현재 모습)
        string[] secNames = { "캐릭터", "등 껍질", "옷", "스킨" };
        var cardSprite = EnsureSprite(P + "Card_BG.png");
        for (int s = 0; s < secNames.Length; s++)
        {
            float yTop = 238 - s * 156;   // 줄 간격 156 = 헤더 28 + 카드 128 (MyPageUI 단독표시 오프셋과 동일)
            var sec = JobsnailUiKit.Rect($"Sec{s}", panel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(560, 0));
            JobsnailUiKit.Label("Header", sec, secNames[s], 19, Color.white, TextAlignmentOptions.Left, new Vector2(-150, yTop), new Vector2(260, 26));
            for (int i = 0; i < 5; i++)
            {
                var slot = JobsnailUiKit.Box($"Slot{i}", sec, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(-216 + i * 108, yTop - 90), new Vector2(100, 128), Color.white);
                slot.sprite = cardSprite;
                slot.type = Image.Type.Simple;
            }
        }
        // 빈 상태/안내문(하단 버튼 바로 위 한 줄)
        JobsnailUiKit.Label("ClosetList", panel, "", 17, kPurpleLite, TextAlignmentOptions.Center, new Vector2(0, -398), new Vector2(500, 24));

        // 패널 하단: 현재 모습 적용하기(보라 필) / 원래대로(밝은 면)
        var apply = MakeButton("ApplyButton", panel, "현재 모습 적용하기", new Vector2(-125, -436), new Vector2(230, 56), kPurpleFill, 20);
        Round(apply.GetComponent<Image>());
        var applyTxt = apply.GetComponentInChildren<TextMeshProUGUI>();
        if (applyTxt != null) applyTxt.color = Color.white;
        var revert = MakeButton("RevertButton", panel, "원래대로", new Vector2(125, -436), new Vector2(230, 56), kPurpleLite, 20);
        Round(revert.GetComponent<Image>());
        var revertTxt = revert.GetComponentInChildren<TextMeshProUGUI>();
        if (revertTxt != null) revertTxt.color = kPurpleDeep;

        // 캐릭터 좌우 화살표(거울 옆) — 보유 캐릭터 순환
        var prevRt = JobsnailUiKit.Rect("CharPrevButton", root.transform, new Vector2(0.18f, 0.42f), new Vector2(0.18f, 0.42f), Vector2.zero, new Vector2(72, 72));
        var prevImg = prevRt.gameObject.AddComponent<Image>();
        prevImg.sprite = EnsureSprite(P + "Arrow_L.png");
        prevImg.preserveAspect = true;
        prevRt.gameObject.AddComponent<Button>().targetGraphic = prevImg;
        var nextRt = JobsnailUiKit.Rect("CharNextButton", root.transform, new Vector2(0.50f, 0.42f), new Vector2(0.50f, 0.42f), Vector2.zero, new Vector2(72, 72));
        var nextImg = nextRt.gameObject.AddComponent<Image>();
        nextImg.sprite = EnsureSprite(P + "Arrow_R.png");
        nextImg.preserveAspect = true;
        nextRt.gameObject.AddComponent<Button>().targetGraphic = nextImg;

        SavePrefab(root, kHudPath);
    }

    /// <summary>왼쪽 세로 네비 버튼(보라 사각 + 흰 아이콘 + 라벨). active=밝은 보라/비활성=어두운 반투명.</summary>
    private static Button MakeNavButton(string name, Transform parent, string dir, string icon, string label, Vector2 pos, bool active)
    {
        var bg = JobsnailUiKit.Box(name, parent, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), pos, new Vector2(110, 130),
            active ? kPurpleFill : new Color(0.25f, 0.22f, 0.36f, 0.72f));
        Round(bg);
        var btn = bg.gameObject.AddComponent<Button>();
        btn.targetGraphic = bg;
        bg.raycastTarget = true;
        var icoRt = JobsnailUiKit.Rect("Icon", bg.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -46), new Vector2(60, 60));
        var ico = icoRt.gameObject.AddComponent<Image>();
        ico.sprite = EnsureSprite(dir + icon);
        ico.preserveAspect = true;
        ico.raycastTarget = false;
        var lbl = JobsnailUiKit.Label("Label", bg.rectTransform, label, 20, Color.white, TextAlignmentOptions.Center, new Vector2(0, 22), new Vector2(110, 30));
        lbl.rectTransform.anchorMin = lbl.rectTransform.anchorMax = new Vector2(0.5f, 0f);
        lbl.raycastTarget = false;
        return btn;
    }

    // ── ② 기록 책 팝업(피그마 새로5 리디자인: 목록/상세/자유모드 + 옆 탭) ──
    private static readonly Color kInk = new Color32(0x3E, 0x33, 0x2A, 255);        // 양피지 잉크
    private static readonly Color kInkSoft = new Color32(0x6E, 0x60, 0x52, 255);

    [MenuItem("Jobsnail/UI/Generate RecordBook Prefab (책만)")]
    private static void GenerateRecordBook()
    {
        // 확장형 조립: 빈 책 배경(GPT 생성) + 개별 파츠(카드/탭/리본/배너 스프라이트) + 글씨는 전부 TMP.
        Directory.CreateDirectory(Path.GetDirectoryName(kBookPath));
        var root = new GameObject("RecordBookUI", typeof(RectTransform));
        Stretch((RectTransform)root.transform);
        root.AddComponent<RecordBookUI>();
        string P = "Assets/Resources/UI_pngs/MyPage/";

        var dim = JobsnailUiKit.Box("Dim", root.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Color(0f, 0f, 0f, 0.55f));
        dim.raycastTarget = true;

        // 책(빈 양면) 배경
        var coverRt = JobsnailUiKit.Rect("Cover", root.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1180, 665));
        var coverImg = coverRt.gameObject.AddComponent<Image>();
        coverImg.sprite = EnsureSprite(P + "Book_Empty.png");
        coverImg.raycastTarget = true;
        var cover = coverRt.transform;

        // 좌상 리본 북마크 + 라벨
        var ribRt = JobsnailUiKit.Rect("Ribbon", cover, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-437f, 249f), new Vector2(106f, 140f));
        var ribImg = ribRt.gameObject.AddComponent<Image>();
        ribImg.sprite = EnsureSprite(P + "Book_Ribbon.png");
        ribImg.raycastTarget = false;

        // 우측 책 탭 2개(스프라이트는 코드가 활성/비활성 교체)
        MakeBookTab("TabRecord", cover, P, "기록", new Vector2(547f, 96f));
        MakeBookTab("TabFree", cover, P, "자유\n모드", new Vector2(547f, -35f));

        MakeSpriteButton("CloseButton", cover, P + "Icon_X.png", new Vector2(560f, 306f), new Vector2(52f, 52f));

        var cardSprite = EnsureSprite(P + "Book_Card.png", 24f);
        var pillSprite = EnsureSprite(P + "Pill_Cream.png");

        // ── 목록 뷰 ──
        var list = JobsnailUiKit.Rect("ListView", cover, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1180, 665)).transform;
        var title = JobsnailUiKit.Label("ListTitle", list, "기 록", 46, kInk, TextAlignmentOptions.Center, new Vector2(-268f, 134f), new Vector2(360f, 60f));
        title.fontStyle = FontStyles.Bold;
        JobsnailUiKit.Label("ListDesc", list, "지금까지의 플레이 기록을\n확인해 보세요.", 19, kInkSoft, TextAlignmentOptions.Center, new Vector2(-268f, 60f), new Vector2(360f, 60f));

        // 맵 카드 5칸(0..4) — 빈 칸은 코드가 '추가 예정'으로
        Vector2[] slotPos = { new Vector2(-109f, 128f), new Vector2(102f, 128f), new Vector2(313f, 128f), new Vector2(-109f, -82f), new Vector2(102f, -82f) };
        for (int i = 0; i < slotPos.Length; i++)
        {
            var slot = JobsnailUiKit.Box($"MapSlot{i}", list, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), slotPos[i], new Vector2(201f, 213f), Color.white);
            slot.sprite = cardSprite;
            slot.type = Image.Type.Sliced;
            slot.raycastTarget = true;
            slot.gameObject.AddComponent<Button>().targetGraphic = slot;

            var thumbRt = JobsnailUiKit.Rect("Thumb", slot.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 22f), new Vector2(172f, 138f));
            var thumb = thumbRt.gameObject.AddComponent<Image>();
            thumb.preserveAspect = false;
            thumb.raycastTarget = false;

            var pill = JobsnailUiKit.Rect("NamePill", slot.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -18f), new Vector2(116f, 28f));
            var pillImg = pill.gameObject.AddComponent<Image>();
            pillImg.sprite = pillSprite;
            pillImg.raycastTarget = false;
            var nameLbl = JobsnailUiKit.Label("Name", pill, "", 15, kInk, TextAlignmentOptions.Center, Vector2.zero, new Vector2(116f, 28f));
            nameLbl.fontStyle = FontStyles.Bold;
            nameLbl.enableAutoSizing = true; nameLbl.fontSizeMin = 10; nameLbl.fontSizeMax = 15;
            nameLbl.raycastTarget = false;

            var tro = JobsnailUiKit.Rect("TrophyIcon", slot.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-58f, 26f), new Vector2(24f, 24f));
            var troImg = tro.gameObject.AddComponent<Image>();
            troImg.sprite = EnsureSprite(P + "Icon_Trophy.png");
            troImg.preserveAspect = true;
            troImg.raycastTarget = false;
            var pctLbl = JobsnailUiKit.Label("Pct", slot.rectTransform, "", 16, kInk, TextAlignmentOptions.Left, new Vector2(30f, 26f), new Vector2(140f, 26f));
            pctLbl.rectTransform.anchorMin = pctLbl.rectTransform.anchorMax = new Vector2(0.5f, 0f);
            pctLbl.raycastTarget = false;

            var lockRt = JobsnailUiKit.Rect("Lock", slot.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 30f), new Vector2(46f, 46f));
            var lockImg = lockRt.gameObject.AddComponent<Image>();
            lockImg.sprite = EnsureSprite(P + "Icon_LockCircle.png");
            lockImg.preserveAspect = true;
            lockImg.raycastTarget = false;
            var soon = JobsnailUiKit.Label("Soon", slot.rectTransform, "추가 예정", 17, kInkSoft, TextAlignmentOptions.Center, new Vector2(0f, -28f), new Vector2(160f, 26f));
            soon.fontStyle = FontStyles.Bold;
            soon.raycastTarget = false;
        }

        // 하단 배너(자유 모드 기록)
        var banner = JobsnailUiKit.Box("FreeBanner", list, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(170f, -244f), new Vector2(783f, 104f), Color.white);
        banner.sprite = EnsureSprite(P + "Book_Banner.png", 20f);
        banner.type = Image.Type.Sliced;
        banner.raycastTarget = true;
        banner.gameObject.AddComponent<Button>().targetGraphic = banner;
        var camRt = JobsnailUiKit.Rect("Cam", banner.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(64f, 0f), new Vector2(52f, 52f));
        var camImg = camRt.gameObject.AddComponent<Image>();
        camImg.sprite = EnsureSprite(P + "Icon_Camera.png");
        camImg.preserveAspect = true;
        camImg.raycastTarget = false;
        var bTitle = JobsnailUiKit.Label("Title", banner.rectTransform, "자유 모드 기록", 23, kInk, TextAlignmentOptions.Left, new Vector2(115f, 14f), new Vector2(400f, 30f));
        bTitle.fontStyle = FontStyles.Bold;
        bTitle.rectTransform.anchorMin = bTitle.rectTransform.anchorMax = new Vector2(0f, 0.5f);
        bTitle.rectTransform.pivot = new Vector2(0f, 0.5f);
        bTitle.raycastTarget = false;
        var bCap = JobsnailUiKit.Label("Caption", banner.rectTransform, "완성한 건축물을 스크린샷으로 저장해 보세요!", 15, kInkSoft, TextAlignmentOptions.Left, new Vector2(115f, -14f), new Vector2(440f, 24f));
        bCap.rectTransform.anchorMin = bCap.rectTransform.anchorMax = new Vector2(0f, 0.5f);
        bCap.rectTransform.pivot = new Vector2(0f, 0.5f);
        bCap.raycastTarget = false;
        JobsnailUiKit.Label("Arrow", banner.rectTransform, ">", 28, kInkSoft, TextAlignmentOptions.Right, new Vector2(-30f, 0f), new Vector2(740f, 36f)).raycastTarget = false;

        // ── 상세 뷰 ──
        var detail = JobsnailUiKit.Rect("DetailView", cover, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1180, 665)).transform;
        var backBtn = MakeButton("BackToList", detail, "< 목록으로", new Vector2(-460f, 262f), new Vector2(140f, 44f), kPurpleFill, 17);
        Round(backBtn.GetComponent<Image>());
        var backTxt = backBtn.GetComponentInChildren<TextMeshProUGUI>();
        if (backTxt != null) backTxt.color = Color.white;

        var bigCard = JobsnailUiKit.Box("BigCard", detail, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-268f, 55f), new Vector2(432f, 380f), Color.white);
        bigCard.sprite = cardSprite;
        bigCard.type = Image.Type.Sliced;
        var bigRt = JobsnailUiKit.Rect("BookThumb", bigCard.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(396f, 344f));
        var big = bigRt.gameObject.AddComponent<Image>();
        big.preserveAspect = false;
        big.raycastTarget = false;
        var nm = JobsnailUiKit.Label("BookMapName", detail, "", 32, kInk, TextAlignmentOptions.Center, new Vector2(-268f, -185f), new Vector2(430f, 44f));
        nm.fontStyle = FontStyles.Bold;
        JobsnailUiKit.Label("MapDesc", detail, "", 16, kInkSoft, TextAlignmentOptions.Center, new Vector2(-268f, -238f), new Vector2(440f, 56f));

        var clkRt = JobsnailUiKit.Rect("ClockIcon", detail, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(60f, 255f), new Vector2(32f, 32f));
        var clk = clkRt.gameObject.AddComponent<Image>();
        clk.sprite = EnsureSprite(P + "Icon_Clock.png");
        clk.preserveAspect = true;
        var taT = JobsnailUiKit.Label("TaTitle", detail, "타임어택 모드 (최고 기록)", 22, kInk, TextAlignmentOptions.Left, new Vector2(300f, 255f), new Vector2(420f, 30f));
        taT.fontStyle = FontStyles.Bold;
        JobsnailUiKit.Label("TaSub", detail, "완성도가 우선, 그 이후 시간 순으로 정렬됩니다.", 14, kInkSoft, TextAlignmentOptions.Left, new Vector2(300f, 226f), new Vector2(420f, 22f));
        for (int p = 0; p < 4; p++)
        {
            float x = 60f + p * 120f;
            var pl = JobsnailUiKit.Label($"TaPlayers{p}", detail, $"{p + 1}인", 18, kInk, TextAlignmentOptions.Center, new Vector2(x, 172f), new Vector2(100f, 26f));
            pl.fontStyle = FontStyles.Bold;
            var trRt = JobsnailUiKit.Rect($"TaTrophy{p}", detail, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(x, 120f), new Vector2(48f, 48f));
            var tr = trRt.gameObject.AddComponent<Image>();
            tr.sprite = EnsureSprite(P + "Icon_Trophy.png");
            tr.preserveAspect = true;
            var pct = JobsnailUiKit.Label($"TaPct{p}", detail, "-", 20, kInk, TextAlignmentOptions.Center, new Vector2(x, 70f), new Vector2(104f, 28f));
            pct.fontStyle = FontStyles.Bold;
            JobsnailUiKit.Label($"TaTime{p}", detail, "", 16, kPurpleDeep, TextAlignmentOptions.Center, new Vector2(x, 40f), new Vector2(104f, 24f));
        }

        var swRt = JobsnailUiKit.Rect("SwordsIcon", detail, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(60f, -40f), new Vector2(32f, 32f));
        var sw = swRt.gameObject.AddComponent<Image>();
        sw.sprite = EnsureSprite(P + "Icon_Swords.png");
        sw.preserveAspect = true;
        var vsT = JobsnailUiKit.Label("VsTitle", detail, "2VS2 모드 (대전 기록)", 22, kInk, TextAlignmentOptions.Left, new Vector2(300f, -40f), new Vector2(420f, 30f));
        vsT.fontStyle = FontStyles.Bold;
        JobsnailUiKit.Label("VsSub", detail, "승/패 기록이 표시됩니다.", 14, kInkSoft, TextAlignmentOptions.Left, new Vector2(300f, -69f), new Vector2(420f, 22f));
        var boxRt = JobsnailUiKit.Rect("ItemBoxIcon", detail, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(120f, -160f), new Vector2(56f, 56f));
        var box = boxRt.gameObject.AddComponent<Image>();
        box.sprite = EnsureSprite(P + "Icon_ItemBox.png");
        box.preserveAspect = true;
        JobsnailUiKit.Label("VsItemLabel", detail, "아이템전", 18, kInk, TextAlignmentOptions.Center, new Vector2(120f, -110f), new Vector2(140f, 26f)).fontStyle = FontStyles.Bold;
        var vsItem = JobsnailUiKit.Label("VsItem", detail, "0승 0패", 24, kInk, TextAlignmentOptions.Left, new Vector2(310f, -160f), new Vector2(200f, 32f));
        vsItem.fontStyle = FontStyles.Bold;
        MakeSpriteButton("NextMapButton", detail, P + "Arrow_R.png", new Vector2(500f, -160f), new Vector2(56f, 56f));

        // ── 자유 모드 뷰 ──
        var free = JobsnailUiKit.Rect("FreeView", cover, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1180, 665)).transform;
        var fRib = JobsnailUiKit.Box("FreeRibbon", free, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-215f, 240f), new Vector2(320f, 56f), kPurpleFill);
        Round(fRib);
        var fcamRt = JobsnailUiKit.Rect("Cam", fRib.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(34f, 0f), new Vector2(36f, 36f));
        var fcam = fcamRt.gameObject.AddComponent<Image>();
        fcam.sprite = EnsureSprite(P + "Icon_Camera.png");
        fcam.preserveAspect = true;
        JobsnailUiKit.Label("Text", fRib.rectTransform, "자유 모드 기록", 24, Color.white, TextAlignmentOptions.Center, new Vector2(16f, 0f), new Vector2(320f, 56f));
        JobsnailUiKit.Label("FreeDesc", free, "완성한 건축물을 스크린샷으로 저장해 보세요!", 16, kInkSoft, TextAlignmentOptions.Center, new Vector2(-268f, 190f), new Vector2(440f, 26f));
        var pol = JobsnailUiKit.Box("Polaroid", free, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-268f, -45f), new Vector2(350f, 330f), Color.white);
        pol.sprite = cardSprite;
        pol.type = Image.Type.Sliced;
        pol.transform.localRotation = Quaternion.Euler(0f, 0f, 4f);
        JobsnailUiKit.Label("FreeCount", free, "보관한 스크린샷  0 / 50", 15, kInkSoft, TextAlignmentOptions.Center, new Vector2(-268f, -262f), new Vector2(320f, 24f));
        JobsnailUiKit.Label("FreeEmpty", free, "스크린샷 기능 준비 중이에요!", 22, kInkSoft, TextAlignmentOptions.Center, new Vector2(240f, 0f), new Vector2(460f, 40f));

        SavePrefab(root, kBookPath);
    }

    /// <summary>책 옆구리 탭 — 스프라이트(보라/크림)는 RecordBookUI가 활성 상태에 따라 교체.</summary>
    private static Button MakeBookTab(string name, Transform parent, string dir, string label, Vector2 pos)
    {
        var rt = JobsnailUiKit.Rect(name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, new Vector2(86f, 128f));
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = EnsureSprite(dir + "Book_TabCream.png");
        img.raycastTarget = true;
        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        var lbl = JobsnailUiKit.Label("Label", rt, label, 18, kInk, TextAlignmentOptions.Center, new Vector2(-4f, 0f), new Vector2(80f, 110f));
        lbl.fontStyle = FontStyles.Bold;
        lbl.raycastTarget = false;
        return btn;
    }

    // ── 헬퍼 ─────────────────────────────────────────────────────────
    private static Sprite EnsureSprite(string assetPath, float border = 0f)
    {
        var imp = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (imp != null)
        {
            bool dirty = false;
            if (imp.textureType != TextureImporterType.Sprite)
            {
                imp.textureType = TextureImporterType.Sprite;
                imp.alphaIsTransparency = true;
                dirty = true;
            }
            if (border > 0f && imp.spriteBorder != new Vector4(border, border, border, border))
            {
                imp.spriteBorder = new Vector4(border, border, border, border);
                dirty = true;
            }
            if (dirty) imp.SaveAndReimport();
        }
        var sp = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        if (sp == null) Debug.LogWarning($"[MyPage] 스프라이트 없음: {assetPath}");
        return sp;
    }

    /// <summary>둥근 사각(9슬라이스) 스프라이트 — 색은 Image.color로.</summary>
    private static Sprite RoundSprite() => EnsureSprite("Assets/Resources/UI_pngs/MyPage/RoundRect.png", 30f);

    private static void Round(Image img)
    {
        img.sprite = RoundSprite();
        img.type = Image.Type.Sliced;
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
