using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// '시공도면 폰' HUD — 정답 3D 뷰(위)와 재료 주문(아래)을 폰 한 화면에 통합.
/// 화면 블럭 클릭 = 선택(오주문 방지 2단계), 카드/[주문] 버튼으로 확정. TAB = 폰 꺼내기/넣기.
/// 3D 뷰 텍스처·입력 라우팅·호버/선택 픽킹은 AnswerHudDriver, 주문 목록·잔량은 GameHudDriver가 채운다.
/// UIManager가 Resources/UI/HUD/AnswerPanelHUD 프리팹(빈 껍데기)에서 인스턴스화.
/// </summary>
public class AnswerPanelHUD : UIHUD
{
    public struct OrderEntry
    {
        public int Id; public string Name; public GameObject Prefab;
        public int Limit;     // MaxSpawnCount (-1 = 무제한 → 배지 없음)
        public string Sub;    // 필요 공정 리치텍스트(드라이버가 계산 — 이 클래스는 GridSystem을 모른다)
    }

    private const string kIdleName = "블럭을 골라 주문하세요";
    private const string kIdleSub  = "화면 블럭 클릭 = 선택 · 우클릭 회전 · 휠 줌";

    // 폰 레이아웃: 베젤 안에 [타이틀 / 3D 화면 / 선택 바 / 주문 그리드]
    private const float kW = 360f, kBezel = 8f, kTitleH = 26f, kScreenH = 344f, kSelH = 52f, kGridH = 190f;
    private const float kGap = 6f;
    private const int   kCols = 4;
    private const string kSidePref = "Jobsnail_PhoneSide";   // 1 = 오른쪽(기본), 0 = 왼쪽

    private static Font s_Font;
    private GameObject m_Phone;
    private RawImage m_Surface;
    private Text m_SelName, m_SelSub;
    private Text m_CompletionText;
    private Button m_OrderBtn;
    private Image m_OrderBtnImg;
    private GameObject m_GridRoot;

    private struct Card
    {
        public Image Bg; public Outline Frame; public RawImage Thumb; public Text Badge;
        public string Name; public string Sub; public int Remaining;   // -1 = 무제한
    }
    private readonly Dictionary<int, Card> m_Cards = new();
    private int m_SelectedId = -1;
    private Action<int> m_OnOrder;
    private bool m_MobileLayout;

    private static readonly Color kCardIdle   = new Color(1f, 1f, 1f, 0.10f);
    private static readonly Color kCardPicked = new Color(0.30f, 0.85f, 0.40f, 0.22f);
    private static readonly Color kSelGreen   = new Color(0.30f, 0.85f, 0.40f);

    /// <summary>선택이 바뀜(-1 = 해제) — AnswerHudDriver가 3D 뷰 테두리를 동기화.</summary>
    public event Action<int> SelectionChanged;

    public RectTransform SurfaceRect => m_Surface != null ? m_Surface.rectTransform : null;
    public void SetTexture(RenderTexture rt) { if (m_Surface != null) m_Surface.texture = rt; }

    private bool m_Right;

    // 폰을 화면 좌/우 하단에 붙인다. 툴팁·픽킹은 좌표 기반이라 어느 쪽이든 그대로 동작.
    private void ApplySide(bool right)
    {
        m_Right = right;
        PlayerPrefs.SetInt(kSidePref, right ? 1 : 0);
        var rt = m_Phone.GetComponent<RectTransform>();
        var a = right ? new Vector2(1f, 0f) : new Vector2(0f, 0f);
        rt.anchorMin = rt.anchorMax = a; rt.pivot = a;
        rt.anchoredPosition = right ? new Vector2(-14f, 14f) : new Vector2(14f, 14f);
    }

    public override void Init()
    {
        if (s_Font == null) s_Font = JobsnailUiKit.LegacyFont;
        if (s_Font == null) s_Font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        m_MobileLayout = MobileControlsHUD.ShouldUseMobileUI;
        if (m_MobileLayout)
        {
            InitMobileLayout();
            return;
        }

        float h = kBezel * 2 + kTitleH + kScreenH + kSelH + kGridH;
        m_Phone = NewRect("Phone", transform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(14, 14), new Vector2(kW, h));
        var bezel = m_Phone.AddComponent<Image>(); bezel.color = new Color(0.06f, 0.06f, 0.09f, 0.95f); bezel.raycastTarget = false;
        m_Phone.AddComponent<UiPopIn>();   // 폰 꺼낼 때 뽁
        ApplySide(PlayerPrefs.GetInt(kSidePref, 1) == 1);   // 기본 오른쪽, 마지막 선택 기억

        float y = -kBezel;
        var title = MakeText(m_Phone.transform, "시공도면 폰  (TAB 꺼내기/넣기)",
                             new Vector2(kBezel + 4, y), new Vector2(kW - kBezel * 2 - 44, kTitleH), 14, TextAnchor.MiddleLeft);
        title.color = new Color(0.75f, 0.78f, 0.85f);

        // 좌/우 전환 버튼 — 화면 어느 쪽에 둘지 즉석 토글(PlayerPrefs 저장)
        var sideGo = NewRect("SideBtn", m_Phone.transform, new Vector2(1, 1), new Vector2(1, 1),
                             new Vector2(-kBezel, -kBezel - 2), new Vector2(32, 22));
        var sideImg = sideGo.AddComponent<Image>(); sideImg.color = new Color(1f, 1f, 1f, 0.12f);
        var sideBtn = sideGo.AddComponent<Button>(); sideBtn.targetGraphic = sideImg;
        sideBtn.onClick.AddListener(() => ApplySide(!m_Right));
        JuicyButton.Attach(sideBtn);
        var sideLbl = MakeText(sideGo.transform, "<>", Vector2.zero, new Vector2(32, 22), 13, TextAnchor.MiddleCenter);
        sideLbl.fontStyle = FontStyle.Bold;
        y -= kTitleH;

        m_Surface = MakeRawImage(m_Phone.transform, new Vector2(kBezel, y), new Vector2(kW - kBezel * 2, kScreenH));
        y -= kScreenH;

        // ── 선택 정보 바 + [주문] 버튼 ──
        m_SelName = MakeText(m_Phone.transform, kIdleName,
                             new Vector2(kBezel + 4, y - 4), new Vector2(kW - kBezel * 2 - 96, 24), 16, TextAnchor.MiddleLeft);
        m_SelName.fontStyle = FontStyle.Bold;
        m_SelSub = MakeText(m_Phone.transform, kIdleSub,
                            new Vector2(kBezel + 4, y - 28), new Vector2(kW - kBezel * 2 - 96, 20), 12, TextAnchor.MiddleLeft);
        m_SelSub.color = new Color(0.8f, 0.8f, 0.8f);

        var btnGo = NewRect("OrderBtn", m_Phone.transform, new Vector2(0, 1), new Vector2(0, 1),
                            new Vector2(kW - kBezel - 84, y - 8), new Vector2(84, 38));
        m_OrderBtnImg = btnGo.AddComponent<Image>();
        m_OrderBtn = btnGo.AddComponent<Button>(); m_OrderBtn.targetGraphic = m_OrderBtnImg;
        m_OrderBtn.onClick.AddListener(() => { if (m_SelectedId >= 0) m_OnOrder?.Invoke(m_SelectedId); });
        JuicyButton.Attach(m_OrderBtn);
        var bl = NewRect("Label", btnGo.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var blrt = bl.GetComponent<RectTransform>();
        blrt.pivot = new Vector2(0.5f, 0.5f); blrt.offsetMin = blrt.offsetMax = Vector2.zero;
        var blt = bl.AddComponent<Text>();
        blt.font = s_Font; blt.fontSize = 17; blt.fontStyle = FontStyle.Bold;
        blt.color = Color.white; blt.text = "주문"; blt.alignment = TextAnchor.MiddleCenter;
        UpdateOrderButton();

        BuildTip();   // 마지막에 만들어 항상 위에 그려진다
    }

    /// <summary>
    /// 모바일 가로 화면(기획서 스타일): 다크 베젤 폰 + 흰 화면.
    /// 왼쪽 완공 계획도(+주황 완성도 배지) / 오른쪽 재료 카탈로그 + [주문!] / 폰 아래 [폰 내리기].
    /// </summary>
    private void InitMobileLayout()
    {
        m_Phone = NewRect("MobilePhone", transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(1800f, 940f));
        // 4:3 태블릿 등 좁은 화면 대응: AspectRatioFitter는 rect 크기를 바꿔 고정 px 자식들이 삐져나가므로
        // 1800x940 저작 크기를 유지한 채 LateUpdate에서 localScale로 화면 안에 맞춘다.
        m_Phone.AddComponent<NoJuicyButtonMotion>();
        var bezel = m_Phone.AddComponent<Image>();
        bezel.sprite = JobsnailUiKit.Sprite("UI_pngs/MyPage/RoundRect");
        bezel.type = Image.Type.Sliced;
        bezel.color = new Color(0.09f, 0.09f, 0.11f, 0.99f);
        bezel.raycastTarget = true;

        var screen = NewRect("Screen", m_Phone.transform, new Vector2(0, 1), new Vector2(0, 1),
            new Vector2(20f, -20f), new Vector2(1760f, 900f));
        var screenImg = screen.AddComponent<Image>();
        screenImg.sprite = JobsnailUiKit.Sprite("UI_pngs/MyPage/RoundRect");
        screenImg.type = Image.Type.Sliced;
        screenImg.color = new Color(0.995f, 0.995f, 0.99f, 1f);
        screenImg.raycastTarget = false;

        var divider = NewRect("Divider", m_Phone.transform, new Vector2(0, 1), new Vector2(0, 1),
            new Vector2(888f, -60f), new Vector2(3f, 820f));
        var dividerImg = divider.AddComponent<Image>();
        dividerImg.color = new Color(0.88f, 0.88f, 0.87f, 1f);
        dividerImg.raycastTarget = false;

        var ink = new Color(0.16f, 0.16f, 0.15f, 1f);

        var planTitle = MakeText(m_Phone.transform, "완공 계획도",
            new Vector2(64f, -48f), new Vector2(360f, 56f), 34, TextAnchor.MiddleLeft);
        planTitle.fontStyle = FontStyle.Bold;
        planTitle.color = ink;

        var badge = NewRect("CompletionBadge", m_Phone.transform, new Vector2(0, 1), new Vector2(0, 1),
            new Vector2(560f, -54f), new Vector2(296f, 48f));
        var badgeImg = badge.AddComponent<Image>();
        badgeImg.sprite = JobsnailUiKit.Sprite("UI_pngs/MyPage/RoundRect");
        badgeImg.type = Image.Type.Sliced;
        badgeImg.color = new Color(1f, 0.44f, 0.08f, 1f);
        badgeImg.raycastTarget = false;
        m_CompletionText = MakeText(badge.transform, "현재 완성도 :  - %",
            Vector2.zero, new Vector2(296f, 48f), 23, TextAnchor.MiddleCenter);
        m_CompletionText.fontStyle = FontStyle.Bold;

        m_Surface = MakeRawImage(m_Phone.transform, new Vector2(64f, -120f), new Vector2(792f, 650f));
        m_Surface.color = Color.white;

        m_SelName = MakeText(m_Phone.transform, kIdleName,
            new Vector2(64f, -786f), new Vector2(620f, 36f), 24, TextAnchor.MiddleLeft);
        m_SelName.fontStyle = FontStyle.Bold;
        m_SelName.color = ink;
        m_SelSub = MakeText(m_Phone.transform, "오른쪽 재료를 골라 주문하세요",
            new Vector2(64f, -824f), new Vector2(720f, 30f), 19, TextAnchor.MiddleLeft);
        m_SelSub.color = new Color(0.45f, 0.45f, 0.44f, 1f);

        var catalogTitle = MakeText(m_Phone.transform, "재료 카탈로그",
            new Vector2(920f, -48f), new Vector2(420f, 56f), 34, TextAnchor.MiddleLeft);
        catalogTitle.fontStyle = FontStyle.Bold;
        catalogTitle.color = ink;

        var btnGo = NewRect("OrderBtn", m_Phone.transform, new Vector2(0, 1), new Vector2(0, 1),
            new Vector2(916f, -796f), new Vector2(836f, 76f));
        m_OrderBtnImg = btnGo.AddComponent<Image>();
        m_OrderBtnImg.sprite = JobsnailUiKit.Sprite("UI_pngs/MyPage/RoundRect");
        m_OrderBtnImg.type = Image.Type.Sliced;
        m_OrderBtn = btnGo.AddComponent<Button>();
        m_OrderBtn.targetGraphic = m_OrderBtnImg;
        m_OrderBtn.onClick.AddListener(() => { if (m_SelectedId >= 0) m_OnOrder?.Invoke(m_SelectedId); });
        var orderLabel = MakeText(btnGo.transform, "주문!", Vector2.zero, new Vector2(836f, 76f), 30, TextAnchor.MiddleCenter);
        orderLabel.fontStyle = FontStyle.Bold;
        UpdateOrderButton();

        // 폰 내리기 — 화면 하단 중앙(폰 프레임과 살짝 겹침, 기획서의 '이거 누르면 폰 내려짐')
        var closeGo = NewRect("ClosePhone", transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, 10f), new Vector2(320f, 62f));
        closeGo.AddComponent<NoJuicyButtonMotion>();   // GameHudDriver의 JuicyButton 스윕에서 제외(모바일 무모션 정책)
        var closeImg = closeGo.AddComponent<Image>();
        closeImg.sprite = JobsnailUiKit.Sprite("UI_pngs/MyPage/RoundRect");
        closeImg.type = Image.Type.Sliced;
        closeImg.color = new Color(0.94f, 0.94f, 0.93f, 0.97f);
        var close = closeGo.AddComponent<Button>();
        close.targetGraphic = closeImg;
        close.onClick.AddListener(Player.MobileGameplayInput.ToggleOrder);
        var closeLabel = MakeText(closeGo.transform, "폰 내리기 ▾", Vector2.zero, new Vector2(320f, 62f), 24, TextAnchor.MiddleCenter);
        closeLabel.color = ink;
        closeLabel.fontStyle = FontStyle.Bold;
    }

    /// <summary>현재 완성도 % — AnswerHudDriver가 주기적으로 밀어 넣는다(모바일 배지 전용).</summary>
    public void SetCompletion(int percent)
    {
        if (m_CompletionText != null)
            m_CompletionText.text = $"현재 완성도 : {percent}%";
    }

    // 폰(1800x940 고정 저작 크기)을 화면 크기에 맞춰 축소. 16:9에선 1배(여백 유지), 4:3 태블릿에선 알아서 줄어든다.
    private Vector2 m_LastFitSize;

    private void LateUpdate()
    {
        if (!m_MobileLayout || m_Phone == null) return;
        var avail = ((RectTransform)transform).rect.size;
        if (avail == m_LastFitSize) return;
        m_LastFitSize = avail;
        float s = Mathf.Min(1f, (avail.x - 40f) / 1800f, (avail.y - 40f) / 940f);
        if (s > 0f) m_Phone.transform.localScale = new Vector3(s, s, 1f);
    }

    // ── 주문 그리드 (GameHudDriver가 depot 목록으로 호출) ──────────────
    public void BuildOrders(IReadOnlyList<OrderEntry> items, Action<int> onOrder)
    {
        if (m_Phone == null) return;
        m_OnOrder = onOrder;
        if (m_GridRoot != null) Destroy(m_GridRoot);
        m_Cards.Clear();
        m_SelectedId = -1;
        SetSelBar(null, null);
        UpdateOrderButton();

        if (m_MobileLayout)
        {
            BuildMobileOrders(items);
            return;
        }

        float innerW = kW - kBezel * 2;
        float top = -(kBezel + kTitleH + kScreenH + kSelH);
        m_GridRoot = NewRect("Orders", m_Phone.transform, new Vector2(0, 1), new Vector2(0, 1),
                             new Vector2(kBezel, top), new Vector2(innerW, kGridH));
        m_GridRoot.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.04f);
        m_GridRoot.AddComponent<RectMask2D>();

        float cardW = (innerW - kGap * (kCols + 1)) / kCols;
        float thumb = cardW - 8f;
        float cardH = thumb + 16f;
        int rows = Mathf.CeilToInt(items.Count / (float)kCols);
        float contentH = kGap + rows * (cardH + kGap);

        var content = NewRect("Content", m_GridRoot.transform, new Vector2(0, 1), new Vector2(1, 1),
                              Vector2.zero, new Vector2(0, contentH));
        var crt = content.GetComponent<RectTransform>();
        crt.pivot = new Vector2(0.5f, 1f); crt.anchoredPosition = Vector2.zero;

        for (int i = 0; i < items.Count; i++)
        {
            int col = i % kCols, row = i / kCols;
            MakeCard(content.transform, items[i],
                     new Vector2(kGap + col * (cardW + kGap), -(kGap + row * (cardH + kGap))), cardW, cardH, thumb);
        }

        if (contentH > kGridH)
        {
            var sr = m_GridRoot.AddComponent<ScrollRect>();
            sr.content = crt;
            sr.viewport = m_GridRoot.GetComponent<RectTransform>();
            sr.horizontal = false; sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.scrollSensitivity = 20f;
        }
    }

    // 기획서: 2열 가로형 카드(썸네일 좌 + 이름 우 + '재고: N개' 배지)
    private void BuildMobileOrders(IReadOnlyList<OrderEntry> items)
    {
        const int cols = 2;
        const float gap = 16f;
        const float gridW = 836f;
        const float gridH = 660f;
        float cardW = (gridW - gap * (cols + 1)) / cols;
        const float cardH = 150f;

        m_GridRoot = NewRect("Orders", m_Phone.transform, new Vector2(0, 1), new Vector2(0, 1),
            new Vector2(916f, -120f), new Vector2(gridW, gridH));
        var gridImage = m_GridRoot.AddComponent<Image>();
        gridImage.color = new Color(0f, 0f, 0f, 0.01f);   // 스크롤 히트 영역(거의 투명)
        m_GridRoot.AddComponent<RectMask2D>();

        int rows = Mathf.CeilToInt(items.Count / (float)cols);
        float contentH = gap + rows * (cardH + gap);
        var content = NewRect("Content", m_GridRoot.transform, new Vector2(0, 1), new Vector2(1, 1),
            Vector2.zero, new Vector2(0f, contentH));
        var contentRt = content.GetComponent<RectTransform>();
        contentRt.pivot = new Vector2(0.5f, 1f);
        contentRt.anchoredPosition = Vector2.zero;

        for (int i = 0; i < items.Count; i++)
        {
            int col = i % cols;
            int row = i / cols;
            MakeMobileCard(content.transform, items[i],
                new Vector2(gap + col * (cardW + gap), -(gap + row * (cardH + gap))),
                cardW, cardH);
        }

        if (contentH > gridH)
        {
            var scroll = m_GridRoot.AddComponent<ScrollRect>();
            scroll.content = contentRt;
            scroll.viewport = m_GridRoot.GetComponent<RectTransform>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 34f;
        }
    }

    // 모바일 가로형 카드: [썸네일 | 이름] + 우하단 '재고: N개' 배지(수량 제한 재료만)
    private void MakeMobileCard(Transform parent, OrderEntry e, Vector2 pos, float w, float h)
    {
        var card = NewRect("Card", parent, new Vector2(0, 1), new Vector2(0, 1), pos, new Vector2(w, h));
        var img = card.AddComponent<Image>();
        img.sprite = JobsnailUiKit.Sprite("UI_pngs/MyPage/RoundRect");
        img.type = Image.Type.Sliced;
        img.color = IdleCardColor;
        var frame = card.AddComponent<Outline>();   // 선택 테두리(게임 집기 초록과 동일 색)
        frame.effectColor = kSelGreen; frame.effectDistance = new Vector2(3f, 3f); frame.enabled = false;
        var btn = card.AddComponent<Button>(); btn.targetGraphic = img;
        int id = e.Id;
        btn.onClick.AddListener(() => Select(id));

        var th = NewRect("Thumb", card.transform, new Vector2(0, 1), new Vector2(0, 1),
            new Vector2(14f, -14f), new Vector2(122f, 122f));
        var ri = th.AddComponent<RawImage>();
        var tex = e.Prefab != null ? BlockThumbnail.Get(e.Prefab, 256) : null;
        if (tex != null) ri.texture = tex;
        else ri.color = new Color(0f, 0f, 0f, 0.06f);
        ri.raycastTarget = false;

        var nm = MakeText(card.transform, e.Name, new Vector2(150f, -30f), new Vector2(w - 164f, 70f), 22, TextAnchor.MiddleLeft);
        nm.fontStyle = FontStyle.Bold;
        nm.color = new Color(0.16f, 0.16f, 0.15f, 1f);
        nm.horizontalOverflow = HorizontalWrapMode.Wrap;
        nm.verticalOverflow = VerticalWrapMode.Truncate;

        Text badge = null;
        if (e.Limit >= 0)
        {
            var bg = NewRect("BadgeBg", card.transform, new Vector2(1, 0), new Vector2(1, 0),
                new Vector2(-10f, 10f), new Vector2(152f, 38f));
            var bimg = bg.AddComponent<Image>();
            bimg.sprite = JobsnailUiKit.Sprite("UI_pngs/MyPage/RoundRect");
            bimg.type = Image.Type.Sliced;
            bimg.color = new Color(0.30f, 0.30f, 0.29f, 0.95f);
            bimg.raycastTarget = false;
            badge = MakeText(bg.transform, $"재고: {e.Limit}개", Vector2.zero, new Vector2(152f, 38f), 19, TextAnchor.MiddleCenter);
            badge.fontStyle = FontStyle.Bold;
        }

        m_Cards[id] = new Card { Bg = img, Frame = frame, Thumb = ri, Badge = badge,
                                Name = e.Name, Sub = e.Sub, Remaining = e.Limit };
    }

    private void MakeCard(Transform parent, OrderEntry e, Vector2 pos, float w, float h, float thumbSize)
    {
        var card = NewRect("Card", parent, new Vector2(0, 1), new Vector2(0, 1), pos, new Vector2(w, h));
        var img = card.AddComponent<Image>();
        img.color = IdleCardColor;
        var frame = card.AddComponent<Outline>();   // 선택 테두리(게임 집기 초록과 동일 색)
        frame.effectColor = kSelGreen; frame.effectDistance = new Vector2(2f, 2f); frame.enabled = false;
        var btn = card.AddComponent<Button>(); btn.targetGraphic = img;
        int id = e.Id;
        btn.onClick.AddListener(() => Select(id));   // 카드 클릭 = 선택(주문은 [주문] 버튼)
        JuicyButton.Attach(btn);

        var th = NewRect("Thumb", card.transform, new Vector2(0, 1), new Vector2(0, 1),
                         new Vector2((w - thumbSize) * 0.5f, -2f), new Vector2(thumbSize, thumbSize));
        var ri = th.AddComponent<RawImage>();
        var tex = e.Prefab != null ? BlockThumbnail.Get(e.Prefab, 256) : null;
        if (tex != null) ri.texture = tex;
        else ri.color = new Color(1f, 1f, 1f, 0.15f);
        ri.raycastTarget = false;

        var nm = MakeText(card.transform, e.Name, new Vector2(2, -(2f + thumbSize)), new Vector2(w - 4, 14), 11, TextAnchor.MiddleCenter);
        nm.horizontalOverflow = HorizontalWrapMode.Wrap; nm.verticalOverflow = VerticalWrapMode.Truncate;

        Text badge = null;
        if (e.Limit >= 0)
        {
            var bg = NewRect("BadgeBg", card.transform, new Vector2(1, 1), new Vector2(1, 1),
                             new Vector2(-2, -2), new Vector2(34, 16));
            var bimg = bg.AddComponent<Image>();
            bimg.color = new Color(0f, 0f, 0f, 0.65f);
            bimg.raycastTarget = false;
            badge = MakeText(bg.transform, $"×{e.Limit}", Vector2.zero, new Vector2(34, 16), 11, TextAnchor.MiddleCenter);
            badge.fontStyle = FontStyle.Bold;
        }

        m_Cards[id] = new Card { Bg = img, Frame = frame, Thumb = ri, Badge = badge,
                                Name = e.Name, Sub = e.Sub, Remaining = e.Limit };
    }

    // ── 선택 (카드 클릭 / 3D 뷰 클릭 → 드라이버 경유) ─────────────────
    public void Select(int id)
    {
        if (!m_Cards.TryGetValue(id, out var c)) return;
        if (m_SelectedId != id)
        {
            DeselectCardVisual();
            m_SelectedId = id;
            c.Frame.enabled = true;
            c.Bg.color = PickedCardColor;
            SetSelBar(c.Name + RemainSuffix(c), c.Sub);
            UpdateOrderButton();
        }
        SelectionChanged?.Invoke(id);
    }

    public void ClearSelection()
    {
        if (m_SelectedId < 0) return;
        DeselectCardVisual();
        m_SelectedId = -1;
        SetSelBar(null, null);
        UpdateOrderButton();
        SelectionChanged?.Invoke(-1);
    }

    private void DeselectCardVisual()
    {
        if (m_SelectedId >= 0 && m_Cards.TryGetValue(m_SelectedId, out var old))
        {
            old.Frame.enabled = false;
            old.Bg.color = IdleCardColor;
        }
    }

    /// <summary>수량 제한 재료의 잔량 반영. 품절이어도 선택은 되게 두고 [주문] 버튼만 잠근다.</summary>
    public void SetRemaining(int id, int remaining)
    {
        if (remaining < 0 || !m_Cards.TryGetValue(id, out var c)) return;
        c.Remaining = remaining;
        m_Cards[id] = c;
        bool sold = remaining == 0;
        if (c.Badge != null)
        {
            c.Badge.text = sold ? "품절" : m_MobileLayout ? $"재고: {remaining}개" : $"×{remaining}";
            c.Badge.color = sold ? new Color(1f, 0.55f, 0.45f) : Color.white;
        }
        if (c.Thumb != null && c.Thumb.texture != null)
            c.Thumb.color = sold ? new Color(0.4f, 0.4f, 0.4f, 1f) : Color.white;
        if (m_SelectedId == id)
        {
            SetSelBar(c.Name + RemainSuffix(c), c.Sub);
            UpdateOrderButton();
        }
    }

    private static string RemainSuffix(Card c) =>
        c.Remaining < 0 ? "" : c.Remaining == 0 ? "  <color=#FF8C73>품절</color>" : $"  ({c.Remaining}개 남음)";

    private void SetSelBar(string name, string sub)
    {
        bool has = !string.IsNullOrEmpty(name);
        if (m_SelName != null) m_SelName.text = has ? name : kIdleName;
        // 모바일엔 우클릭·휠 안내 대신 터치 문구
        if (m_SelSub != null)  m_SelSub.text  = has ? sub : m_MobileLayout ? "오른쪽 재료를 골라 주문하세요" : kIdleSub;
    }

    private void UpdateOrderButton()
    {
        bool can = m_SelectedId >= 0 && m_Cards.TryGetValue(m_SelectedId, out var c) && c.Remaining != 0;
        if (m_OrderBtn != null) m_OrderBtn.interactable = can;
        if (m_OrderBtnImg != null)
            m_OrderBtnImg.color = m_MobileLayout
                ? can ? new Color(1f, 0.44f, 0.08f, 1f) : new Color(0.64f, 0.62f, 0.58f, 0.78f)
                : can ? new Color(0.22f, 0.55f, 0.30f) : new Color(0.28f, 0.28f, 0.30f, 0.9f);
    }

    private Color IdleCardColor => m_MobileLayout
        ? new Color(0.92f, 0.92f, 0.91f, 1f)
        : kCardIdle;

    private Color PickedCardColor => m_MobileLayout
        ? new Color(0.85f, 0.93f, 0.84f, 1f)
        : kCardPicked;

    // ── 커서 옆 말풍선 툴팁(호버 전용 — 선택과 별개) ─────────────────
    private const float kTipW = 168f, kTipH = 188f;
    private GameObject m_Tip;
    private RectTransform m_TipRt;
    private RawImage m_TipThumb;
    private Text m_TipName, m_TipSub;

    /// <summary>커서 옆 말풍선 툴팁. thumb = 주문 카드와 같은 BlockThumbnail 렌더(없으면 텍스트만).</summary>
    public void ShowTip(Vector2 screenPos, string name, string sub, Texture thumb)
    {
        if (m_Tip == null) return;
        m_TipName.text = name;
        m_TipSub.text  = sub;
        m_TipThumb.texture = thumb;
        m_TipThumb.enabled = thumb != null;   // 썸네일 없는 재료는 흰 사각형 대신 숨김

        var parent = (RectTransform)transform;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screenPos, null, out var local);
        var pos = local + new Vector2(18f, 14f);   // 커서 우상단에 말풍선
        var pr = parent.rect;                       // 화면 밖으로 안 나가게 클램프
        pos.x = Mathf.Min(pos.x, pr.xMax - kTipW);
        pos.y = Mathf.Min(pos.y, pr.yMax - kTipH);
        m_TipRt.anchoredPosition = pos;
        if (!m_Tip.activeSelf) m_Tip.SetActive(true);
    }

    public void HideTip() { if (m_Tip != null && m_Tip.activeSelf) m_Tip.SetActive(false); }

    private void BuildTip()
    {
        m_Tip = new GameObject("Tooltip", typeof(RectTransform)) { layer = 5 };
        m_TipRt = m_Tip.GetComponent<RectTransform>();
        m_TipRt.SetParent(transform, false);
        m_TipRt.anchorMin = m_TipRt.anchorMax = new Vector2(0.5f, 0.5f);
        m_TipRt.pivot = Vector2.zero;   // 좌하단 피벗 — 커서 우상단으로 펼쳐짐
        m_TipRt.sizeDelta = new Vector2(kTipW, kTipH);
        var bg = m_Tip.AddComponent<Image>();
        bg.color = new Color(0.05f, 0.05f, 0.08f, 0.92f);
        bg.raycastTarget = false;   // 커서 아래에 있어도 다른 UI 클릭 안 막음

        var th = NewRect("Thumb", m_Tip.transform, new Vector2(0, 1), new Vector2(0, 1),
                         new Vector2((kTipW - 128f) * 0.5f, -8f), new Vector2(128f, 128f));
        m_TipThumb = th.AddComponent<RawImage>();
        m_TipThumb.raycastTarget = false;

        m_TipName = MakeText(m_Tip.transform, "", new Vector2(6, -140f), new Vector2(kTipW - 12, 24), 17, TextAnchor.MiddleCenter);
        m_TipName.fontStyle = FontStyle.Bold;
        m_TipSub = MakeText(m_Tip.transform, "", new Vector2(6, -162f), new Vector2(kTipW - 12, 20), 13, TextAnchor.MiddleCenter);

        m_Tip.SetActive(false);
    }

    // ── 빌더 헬퍼(OrderHUD와 동일 스타일) ──
    private static GameObject NewRect(string name, Transform parent, Vector2 aMin, Vector2 aMax, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform)) { layer = 5 };
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = aMin; rt.anchorMax = aMax; rt.pivot = aMax;
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        return go;
    }

    private RawImage MakeRawImage(Transform parent, Vector2 pos, Vector2 size)
    {
        var go = NewRect("Surface", parent, new Vector2(0, 1), new Vector2(0, 1), pos, size);
        var ri = go.AddComponent<RawImage>();
        ri.raycastTarget = false;   // 다른 UI 클릭 안 막음(라우팅은 좌표로 판정)
        return ri;
    }

    private static Text MakeText(Transform parent, string s, Vector2 pos, Vector2 size, int fontSize, TextAnchor anchor)
    {
        var go = NewRect("Text", parent, new Vector2(0, 1), new Vector2(0, 1), pos, size);
        var t = go.AddComponent<Text>();
        t.font = s_Font; t.fontSize = fontSize; t.color = Color.white; t.text = s;
        t.alignment = anchor; t.horizontalOverflow = HorizontalWrapMode.Overflow; t.raycastTarget = false;
        return t;
    }
}
