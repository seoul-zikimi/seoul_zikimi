using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// GameLoopHUD 프리팹 생성기(1회 실행 → 이후 에디터에서 직접 편집).
/// Assets/Resources/UI/HUD/GameLoopHUD.prefab → UIManager.ShowHUDUI&lt;GameLoopHUD&gt;()로 표시.
/// 자식 이름은 GameLoopHUD의 Bind enum과 1:1 — 이름 바꾸면 바인딩 깨짐 주의.
/// 버튼/슬라이더 콜백은 프리팹에 저장되지 않음 → GameLoopHUD.Init()이 배선.
/// </summary>
public static class GameLoopHudPrefabGenerator
{
    private const string kPath = "Assets/Resources/UI/HUD/GameLoopHUD.prefab";

    /// <summary>리마스터 레이아웃이 적용된 프리팹인지(자동 재생성 판단용 마커 노드).</summary>
    public const string kRemasterMarker = "RemasterMarker_v16";   // 레이아웃 바뀌면 버전 올리기 → 자동 재생성

    [MenuItem("Jobsnail/UI/Generate GameLoopHud Prefab")]
    public static void Generate()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(kPath));

        var root = new GameObject("GameLoopHUD", typeof(RectTransform));
        var rootRt = (RectTransform)root.transform;
        rootRt.anchorMin = Vector2.zero; rootRt.anchorMax = Vector2.one;
        rootRt.offsetMin = Vector2.zero; rootRt.offsetMax = Vector2.zero;
        root.AddComponent<GameLoopHUD>();
        root.AddComponent<MobileSafeArea>();   // 노치·펀치홀 회피 — HUD 전체(타이머·버튼·버프바)를 세이프에어리어 안으로
        var rootT = root.transform;

        // ── 상단: 타이머 아이콘 + 남은 시간(리마스터 · 피그마 "2 : 15" 642,19 120x60 · 아이콘 38x44 왼쪽) ──
        // TopBar 는 투명 컨테이너(무제한 모드에선 통째로 숨김). Timer 텍스트는 위 정렬 + 아래로 넘침 허용(2vs2 점수줄).
        var topRt = JobsnailUiKit.Rect("TopBar", rootT, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
        InGameUiSkin.TopCenter(topRt, 556, 16, 226, 60);
        var top = topRt.gameObject;
        // (유리 알약 배경은 빼기로 — 2026-08-23. 글자 halo 만으로 강조)
        var halo = InGameUiSkin.SpriteImage("TimerIconHalo", top.transform, "TimerIcon_Halo");   // 숫자와 같은 흰 빛 번짐(아이콘 뒤)
        InGameUiSkin.TopLeft(halo.rectTransform, -6 - 12, 9 - 12, 38 + 24, 44 + 24);
        var shadow = InGameUiSkin.SpriteImage("TimerIconShadow", top.transform, "TimerIcon_Shadow");   // 얇은 어두운 테두리(대비용 · halo 위, 아이콘 아래)
        InGameUiSkin.TopLeft(shadow.rectTransform, -6 - 12, 9 - 12, 38 + 24, 44 + 24);
        var icon = InGameUiSkin.SpriteImage("TimerIcon", top.transform, "TimerIcon");
        InGameUiSkin.TopLeft(icon.rectTransform, -6, 9, 38, 44);   // 숫자 기준 좌측 하단으로
        var timer = JobsnailUiKit.Label("Timer", top.transform, "0 : 00", Mathf.RoundToInt(44 * InGameUiSkin.S), Color.white, TextAlignmentOptions.TopLeft, Vector2.zero, Vector2.zero);
        InGameUiSkin.TopLeft(timer.rectTransform, 52, 6, 220, 60);   // 아이콘 바로 오른쪽에 왼쪽 정렬   // 시계 아이콘(8~52) 높이에 맞춰 살짝 아래
        timer.rectTransform.sizeDelta = new Vector2(timer.rectTransform.sizeDelta.x, 160f);   // 2vs2 점수/아이템 줄이 아래로 이어짐
        timer.fontStyle = FontStyles.Bold;
        timer.textWrappingMode = TextWrappingModes.NoWrap;
        timer.overflowMode = TextOverflowModes.Overflow;

        // ── 종료 요청 클러스터(인원 아이콘 + 버튼) — 버튼 피그마 (1071,26) 108x34, 아이콘은 그 왼쪽 ──
        var cbarRt = JobsnailUiKit.Rect("EndRequestCluster", rootT, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
        InGameUiSkin.TopRight(cbarRt, 1071 - 120, 26, 108 + 120, 34);
        var cbar = cbarRt.gameObject;
        var snail = JobsnailUiKit.Sprite("UI_pngs/3.inGame/Person_Icon") ?? JobsnailUiKit.Sprite("UI_pngs/3.inGame/Snail_Icon");
        for (int i = 0; i < 4; i++)
        {
            var pIcon = JobsnailUiKit.Box($"P{i}", cbar.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(18 + i * 38, 0), new Vector2(30, 30), Color.white);
            pIcon.sprite = snail;
            pIcon.preserveAspect = true;
        }
        var endSprite = InGameUiSkin.Load("EndRequestButton");
        var end = JobsnailUiKit.Button("EndRequestButton", cbar.transform, endSprite, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, null, endSprite == null ? "종료 요청" : null);
        InGameUiSkin.TopRight((RectTransform)end.transform, 120, 0, 108, 34, frameW: 228);
        if (end.targetGraphic is Image endImg) endImg.preserveAspect = false;
        // 상태 라벨(동의 취소 / 재시작) — 기본 상태는 스프라이트에 구워진 텍스트를 쓰므로 숨김. GameLoopHUD 가 빈 버튼 스프라이트로 바꿔 켠다.
        var endLabel = JobsnailUiKit.Label("Label", end.transform, "", Mathf.RoundToInt(13 * InGameUiSkin.S), Color.white, TextAlignmentOptions.Center, Vector2.zero, Vector2.zero);
        endLabel.fontStyle = FontStyles.Bold;
        endLabel.raycastTarget = false;
        endLabel.gameObject.SetActive(false);

        // ── 2vs2 버프/디버프 아이콘 바 — 종료 요청 버튼 아래(우상단). 칸은 GameLoopHUD가 런타임에 채움 ──
        var buffRt = JobsnailUiKit.Rect("BuffBar", rootT, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
        InGameUiSkin.TopRight(buffRt, 1071 - 200, 68, 108 + 200, 44);
        var buffLayout = buffRt.gameObject.AddComponent<HorizontalLayoutGroup>();
        buffLayout.childAlignment = TextAnchor.MiddleRight;
        buffLayout.spacing = 6f;
        buffLayout.childControlWidth = false; buffLayout.childControlHeight = false;
        buffLayout.childForceExpandWidth = false; buffLayout.childForceExpandHeight = false;

        // ── 설정(톱니) — 피그마 (1270,21) 49x49 ──
        var gearSprite = InGameUiSkin.Load("SettingsButton") ?? JobsnailUiKit.Sprite("UI_pngs/settingsicon");
        var gear = JobsnailUiKit.Button("SettingsIconButton", rootT, gearSprite, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, null, gearSprite == null ? "설정" : null);
        InGameUiSkin.TopRight((RectTransform)gear.transform, 1270, 21, 49, 49);
        if (gear.targetGraphic is Image gearImg)
        {
            gearImg.raycastTarget = true;
            gearImg.color = Color.white;
            gearImg.preserveAspect = true;
        }

        BuildSettingsPopup(rootT);
        BuildResultPanel(rootT);

        // ── "공사 시작!" 배너(숨김 상태로 저장 · UiPopIn이 등장 팝 담당) ──
        var banner = JobsnailUiKit.Label("StartBanner", rootT, "공사 시작!", 64, new Color(1f, 0.72f, 0.20f, 1f), TextAlignmentOptions.Center, Vector2.zero, new Vector2(760, 130));
        banner.gameObject.AddComponent<UiPopIn>();
        banner.gameObject.SetActive(false);

        // ── 정산서 ↔ 크레인샷 토글 버튼(하단 중앙, 숨김) — 정산 중에만 표시 ──
        var crane = JobsnailUiKit.Button("CraneToggleButton", rootT, null,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 32f), new Vector2(230, 52), null, "건축물 둘러보기");   // 화면 맨 아래(버튼 스트립 밑)
        SetColor(crane, new Color(0.60f, 0.82f, 0.95f, 1f));
        crane.gameObject.SetActive(false);

        // ── 이벤트 토스트(좌측 위, 숨김) — 완성도 돌파 알림 ──
        var toast = JobsnailUiKit.Label("EventToast", rootT, "", 26, new Color(0.25f, 0.16f, 0.08f, 1f), TextAlignmentOptions.Center, Vector2.zero, new Vector2(340, 52));
        toast.rectTransform.anchorMin = toast.rectTransform.anchorMax = new Vector2(0.13f, 0.72f);
        toast.gameObject.AddComponent<UiPopIn>();
        toast.gameObject.SetActive(false);

        new GameObject(kRemasterMarker, typeof(RectTransform)).transform.SetParent(rootT, false);   // 리마스터 버전 마커(빈 노드)

        SavePrefab(root, kPath);
        Debug.Log($"[GameLoopHudPrefabGenerator] 생성 완료 → {kPath}");
    }

    private static void BuildSettingsPopup(Transform root)
    {
        var popup = JobsnailUiKit.Box("InGameSettingsPopup", root, new Vector2(0.34f, 0.24f), new Vector2(0.66f, 0.76f), Vector2.zero, Vector2.zero, new Color(1f, 0.97f, 0.86f, 0.98f)).gameObject;
        popup.AddComponent<NoJuicyButtonMotion>();
        var popupImage = popup.GetComponent<Image>();
        if (popupImage != null)
        {
            popupImage.sprite = JobsnailUiKit.Sprite("UI_pngs/MyPage/RoundRect");
            popupImage.type = Image.Type.Sliced;
            popupImage.color = new Color(1f, 0.965f, 0.88f, 0.99f);
        }

        JobsnailUiKit.Label("Title", popup.transform, "설정", 30, Color.black, TextAlignmentOptions.Center, new Vector2(0, 210), new Vector2(360, 56));

        MakeVolumeSlider(popup.transform, "BGM", "BGMSlider", new Vector2(0, 120));
        MakeVolumeSlider(popup.transform, "SFX", "SFXSlider", new Vector2(0, 60));
        MakeVolumeSlider(popup.transform, "감도", "SensSlider", new Vector2(0, 0));

        var keySettings = JobsnailUiKit.Button("KeySettingsButton", popup.transform, null,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -78), new Vector2(320, 52), null, "키 설정");
        SetColor(keySettings, new Color(1f, 0.77f, 0.42f, 1f));

        var exit = JobsnailUiKit.Button("ExitGameButton", popup.transform, null, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -148), new Vector2(320, 52), null, "게임 나가기");
        SetColor(exit, new Color(0.92f, 0.76f, 0.70f, 1f));

        var close = JobsnailUiKit.Button("SettingsCloseButton", popup.transform, null, new Vector2(0.88f, 0.88f), new Vector2(0.98f, 0.98f), Vector2.zero, Vector2.zero, null, "×");
        SetColor(close, new Color(1f, 0.97f, 0.86f, 0f));

        popup.SetActive(false);
    }

    // ── 정산서(리마스터): 영수증 배경(라벨·칸 구움 · 1056x1150) + 버튼 스트립(1048x148) + 도장 3종 ──
    // 좌표는 배경 PNG 픽셀(좌상단 원점) 그대로 적고 kR 배율로 캔버스에 올린다. 자식 이름은 GameLoopHUD Bind enum과 1:1.
    private const float kR = 0.72f;   // 배경 1150px → 828 캔버스px(버튼 스트립 포함 1080 안에 들어가게)
    private static Vector2 Rv(float x, float y) => new Vector2(x * kR, -y * kR);
    private static Vector2 Rs(float w, float h) => new Vector2(w * kR, h * kR);
    private static int Rf(float px) => Mathf.RoundToInt(px * kR);

    /// <summary>배경 좌상단 기준 (x,y,w,h) 배경px → 앵커/피벗 좌상단 RectTransform.</summary>
    private static RectTransform RPlace(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = Rv(x, y);
        rt.sizeDelta = Rs(w, h);
        return rt;
    }

    private static TextMeshProUGUI RLabel(string name, Transform parent, float x, float y, float w, float h, float fontPx, Color color, TextAlignmentOptions align, bool bold = false)
    {
        var t = JobsnailUiKit.Label(name, parent, "", Rf(fontPx), color, align, Vector2.zero, Vector2.one);
        RPlace(t.rectTransform, x, y, w, h);
        if (bold) t.fontStyle = FontStyles.Bold;
        t.raycastTarget = false;
        return t;
    }

    private static void BuildResultPanel(Transform root)
    {
        var ink = new Color(0.27f, 0.22f, 0.18f, 1f);       // 영수증 글자색(진갈색)
        var inkSoft = new Color(0.42f, 0.37f, 0.32f, 1f);

        // 패널 = 배경 이미지 자체. 위로 조금 올려 아래 버튼 스트립 자리를 확보.
        var bgSprite = InGameUiSkin.Load("Result_Bg");
        var panelRt = JobsnailUiKit.Rect("ResultPanel", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 57f), Rs(1056, 1150));
        var panelImg = panelRt.gameObject.AddComponent<Image>();
        panelImg.sprite = bgSprite;
        panelImg.color = bgSprite != null ? Color.white : new Color(1f, 1f, 1f, 0.98f);
        panelImg.raycastTarget = true;   // 뒤 클릭 차단
        var panel = panelRt.gameObject;
        var pT = panel.transform;

        // 참여자 / 정산번호 / 발행일자
        RLabel("Players", pT, 85, 278, 560, 52, 24, ink, TextAlignmentOptions.BottomLeft);
        RLabel("ReceiptNo", pT, 893, 256, 112, 34, 24, inkSoft, TextAlignmentOptions.MidlineLeft);
        RLabel("IssueDate", pT, 808, 298, 196, 36, 24, inkSoft, TextAlignmentOptions.MidlineRight);

        // 완성 건축물 이미지(런타임에 RT 연결) — 배경에 테두리가 있어 프레임 불필요
        var riRt = JobsnailUiKit.Rect("ResultImage", pT, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
        RPlace(riRt, 84, 382, 512, 444);
        var ri = riRt.gameObject.AddComponent<RawImage>();
        ri.raycastTarget = false;

        // 프로젝트명 / 소요시간 / 건축 완료율(숫자만 · '%' 는 배경) / 업무결과(2vs2 승패 문구)
        RLabel("Structure", pT, 715, 432, 290, 56, 30, ink, TextAlignmentOptions.MidlineLeft, bold: true);
        RLabel("Time", pT, 715, 556, 290, 56, 30, ink, TextAlignmentOptions.MidlineLeft, bold: true);
        RLabel("Score", pT, 760, 614, 160, 50, 40, ink, TextAlignmentOptions.MidlineRight, bold: true);
        var grade = RLabel("Grade", pT, 715, 772, 290, 56, 26, ink, TextAlignmentOptions.MidlineLeft, bold: true);
        grade.textWrappingMode = TextWrappingModes.Normal;

        // 도장(EXCELLENT / GOOD JOB / TRY AGAIN — 런타임에 완성도로 선택) — 업무결과 칸 위에 비스듬히
        var stampSprite = InGameUiSkin.Load("Stamp_Excellent") ?? JobsnailUiKit.Sprite("UI_pngs/3.inGame/exellent");
        var stampRt = JobsnailUiKit.Rect("GradeStamp", pT, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
        RPlace(stampRt, 760, 690, 238, 146);
        stampRt.pivot = new Vector2(0.5f, 0.5f);
        stampRt.anchoredPosition = Rv(760 + 119, 690 + 73);
        stampRt.localRotation = Quaternion.Euler(0f, 0f, -8f);
        var stamp = stampRt.gameObject.AddComponent<Image>();
        stamp.sprite = stampSprite;
        stamp.preserveAspect = true;
        stamp.raycastTarget = false;
        stamp.color = stampSprite != null ? Color.white : new Color(1f, 1f, 1f, 0f);
        stamp.gameObject.SetActive(false);

        // 최종정산금액(코인 보상)
        RLabel("CoinReward", pT, 60, 958, 936, 56, 30, InGameUiSkin.Orange, TextAlignmentOptions.Center, bold: true);

        // 별점 1~3 (채움/개수는 런타임) — 금액 아래 가운데
        var starSprite = JobsnailUiKit.Sprite("UI_pngs/3.inGame/star");
        var starRow = JobsnailUiKit.Rect("StarRow", pT, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
        RPlace(starRow, 528 - 120, 1030, 240, 70);
        starRow.pivot = new Vector2(0.5f, 0.5f);
        starRow.anchoredPosition = Rv(528, 1065);
        for (int i = 0; i < 3; i++)
        {
            var st = JobsnailUiKit.Box($"GradeStar{i}", starRow, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2((-1 + i) * Rf(78), 0), Rs(64, 64), starSprite != null ? Color.white : new Color(1f, 1f, 1f, 0f));
            st.sprite = starSprite;
            st.preserveAspect = true;
        }

        // ── 버튼 스트립(방으로 돌아가기 | 나가기 — 한 장에 구움) · 패널 아래 ──
        var stripSprite = InGameUiSkin.Load("Result_Buttons");
        var stripRt = JobsnailUiKit.Rect("ResultButtons", pT, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, -Rf(148) * 0.5f - 8f), Rs(1048, 148));
        var strip = stripRt.gameObject.AddComponent<Image>();
        strip.sprite = stripSprite;
        strip.raycastTarget = false;
        strip.color = stripSprite != null ? Color.white : new Color(1f, 0.97f, 0.86f, 1f);
        const float kDivider = 512f;   // 두 버튼 경계(스트립 px)
        var room = JobsnailUiKit.Button("RoomButton", stripRt, null, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, null, stripSprite == null ? "방으로 돌아가기" : null);
        RPlace((RectTransform)room.transform, 30, 18, kDivider - 40, 112);
        var leave = JobsnailUiKit.Button("LeaveButton", stripRt, null, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, null, stripSprite == null ? "나가기" : null);
        RPlace((RectTransform)leave.transform, kDivider + 10, 18, 1048 - kDivider - 40, 112);
        foreach (var btn in new[] { room, leave })
            if (stripSprite != null && btn.targetGraphic is Image bi) bi.color = new Color(1f, 1f, 1f, 0f);   // 투명 히트 영역(비주얼은 스트립)

        panel.SetActive(false);
    }

    // 볼륨/감도 슬라이더 비주얼(값·콜백은 런타임 Init이 배선). sliderName은 Bind enum과 일치해야 함.
    private static void MakeVolumeSlider(Transform parent, string label, string sliderName, Vector2 anchored)
    {
        var row = new GameObject(label + "VolumeRow", typeof(RectTransform));
        row.transform.SetParent(parent, false);
        var rowRt = (RectTransform)row.transform;
        rowRt.anchorMin = new Vector2(0.5f, 0.5f);
        rowRt.anchorMax = new Vector2(0.5f, 0.5f);
        rowRt.anchoredPosition = anchored;
        rowRt.sizeDelta = new Vector2(340, 44);

        JobsnailUiKit.Label(label + "Label", row.transform, label, 18, Color.black, TextAlignmentOptions.Left, new Vector2(-126, 0), new Vector2(70, 36));

        var sliderGo = new GameObject(sliderName, typeof(RectTransform), typeof(Slider));
        sliderGo.transform.SetParent(row.transform, false);
        var rt = (RectTransform)sliderGo.transform;
        rt.anchorMin = new Vector2(0.35f, 0.25f);
        rt.anchorMax = new Vector2(0.95f, 0.75f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var background = JobsnailUiKit.Box("Background", sliderGo.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Color(0.82f, 0.82f, 0.82f, 1f));
        background.raycastTarget = true;

        var fillArea = new GameObject("Fill Area", typeof(RectTransform));
        fillArea.transform.SetParent(sliderGo.transform, false);
        var fillAreaRt = (RectTransform)fillArea.transform;
        fillAreaRt.anchorMin = Vector2.zero;
        fillAreaRt.anchorMax = Vector2.one;
        fillAreaRt.offsetMin = new Vector2(4, 4);
        fillAreaRt.offsetMax = new Vector2(-4, -4);

        var fill = JobsnailUiKit.Box("Fill", fillArea.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Color(1f, 0.72f, 0.36f, 1f));
        fill.raycastTarget = true;

        var handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
        handleArea.transform.SetParent(sliderGo.transform, false);
        var handleAreaRt = (RectTransform)handleArea.transform;
        handleAreaRt.anchorMin = Vector2.zero;
        handleAreaRt.anchorMax = Vector2.one;
        handleAreaRt.offsetMin = new Vector2(8, 0);
        handleAreaRt.offsetMax = new Vector2(-8, 0);

        var handle = JobsnailUiKit.Box("Handle", handleArea.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(20, 30), new Color(0.32f, 0.22f, 0.15f, 1f));
        handle.raycastTarget = true;

        var slider = sliderGo.GetComponent<Slider>();
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.fillRect = fill.rectTransform;
        slider.handleRect = handle.rectTransform;
        slider.targetGraphic = handle;
    }

    private static void SetColor(Button button, Color color)
    {
        if (button != null && button.targetGraphic != null)
            button.targetGraphic.color = color;
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
