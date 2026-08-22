using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// '완공 계획도 폰' HUD(리마스터) — 정답 3D 뷰(위)와 재료 카탈로그(아래)를 폰 한 화면에 통합.
/// 비주얼은 피그마 익스포트(PhoneBg: 상태바·제목·'현재 완성도' 뱃지·'재료 카탈로그' 구움) + 카드/주문 버튼 스프라이트.
/// 화면 블럭 클릭 = 선택(오주문 방지 2단계), 카드 클릭 = 선택, [주문!] 버튼으로 확정. TAB = 폰 꺼내기/넣기.
/// 3D 뷰 텍스처·입력 라우팅·호버/선택 픽킹은 AnswerHudDriver, 주문 목록·잔량·완성도는 GameHudDriver가 채운다.
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

    // ── 피그마 '완성본 모습' 좌표(px · 폰 좌상단 원점) ──
    private const float kPhoneX = 1029f, kPhoneY = 217f, kPhoneW = 304f, kPhoneH = 625f;   // 프레임 기준(아래로 89px 잠김 = 디자인)
    private const float kPctX = 88f, kPctY = 94f, kPctW = 27f, kPctH = 15f;               // '현재 완성도 : [  ]%' 빈칸
    private const float kViewX = 38f, kViewY = 114f, kViewS = 228f;                          // 3D 뷰(정사각)
    private const float kOrderX = 219f, kOrderY = 355f, kOrderW = 46f, kOrderH = 22f;       // [주문!]
    private const float kGridX = 35f, kGridY = 383f, kGridW = 230f;                          // 카드 그리드 원점
    private const float kCardW = 111f, kCardH = 59f, kColStep = 119f, kRowStep = 65f;
    private const int   kCols = 2;
    private const float kDisplayBottom = InGameUiSkin.FrameH - kPhoneY;                     // 폰 로컬 기준 화면 하단(536) — 그 아래는 안 보임
    // 카드 내부
    private const float kThumbX = 6f, kThumbY = 7f, kThumbW = 43f, kThumbH = 46f;
    private const float kNameX = 53f, kNameY = 8f, kNameW = 53f, kNameH = 34f;
    private const float kBadgeX = 60f, kBadgeY = 46f, kBadgeW = 50f, kBadgeH = 12f;

    private static Font s_Font;
    private GameObject m_Phone;
    private RawImage m_Surface;
    private Text m_PctText;
    private Button m_OrderBtn;
    private Image m_OrderBtnImg;
    private GameObject m_GridRoot;

    private struct Card
    {
        public Image Bg; public Outline Frame; public RawImage Thumb; public Text Badge; public GameObject BadgeBg;
        public string Name; public string Sub; public int Remaining;   // -1 = 무제한
    }
    private readonly Dictionary<int, Card> m_Cards = new();
    private int m_SelectedId = -1;
    private Action<int> m_OnOrder;

    private static readonly Color kCardIdle   = Color.white;                         // 스프라이트 원색(#BEC3CD)
    private static readonly Color kCardPicked = new Color(1f, 0.86f, 0.74f, 1f);     // 살구빛 틴트
    private static readonly Color kSoldOut    = new Color(0.72f, 0.72f, 0.72f, 1f);

    /// <summary>선택이 바뀜(-1 = 해제) — AnswerHudDriver가 3D 뷰 테두리를 동기화.</summary>
    public event Action<int> SelectionChanged;

    public RectTransform SurfaceRect => m_Surface != null ? m_Surface.rectTransform : null;
    public void SetTexture(RenderTexture rt) { if (m_Surface != null) m_Surface.texture = rt; }

    /// <summary>'현재 완성도 : N%' 숫자 갱신(GameHudDriver가 매 프레임 호출).</summary>
    public void SetCompletion(int percent)
    {
        if (m_PctText == null) return;
        string s = Mathf.Clamp(percent, 0, 100).ToString();
        if (m_PctText.text != s) m_PctText.text = s;
    }

    public override void Init()
    {
        if (s_Font == null) s_Font = JobsnailUiKit.LegacyFont;
        if (s_Font == null) s_Font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (!InGameUiSkin.Available)
            Debug.LogWarning("[AnswerPanelHUD] 리마스터 스프라이트 없음 — Assets/Resources/UI_pngs/3.inGame/Remaster 확인");

        // ── 폰 본체(우하단 · 디자인대로 아래로 살짝 잠김) ──
        var bg = InGameUiSkin.SpriteImage("Phone", transform, "PhoneBg");
        InGameUiSkin.BottomRight(bg.rectTransform, kPhoneX, kPhoneY, kPhoneW, kPhoneH);
        m_Phone = bg.gameObject;
        m_Phone.AddComponent<UiPopIn>();   // 폰 꺼낼 때 뽁

        // 완성도 숫자(뱃지 빈칸 · 우측 정렬이라 '%' 앞에 붙는다)
        m_PctText = MakeText(m_Phone.transform, "0", new Vector2(kPctX, kPctY), new Vector2(kPctW, kPctH), Px(11), TextAnchor.MiddleRight);
        m_PctText.fontStyle = FontStyle.Bold;

        // 3D 정답 뷰
        m_Surface = MakeRawImage(m_Phone.transform, new Vector2(kViewX, kViewY), new Vector2(kViewS, kViewS));

        // [주문!] 버튼(텍스트 구움) — 선택 없으면 반투명
        var btnImg = InGameUiSkin.SpriteImage("OrderBtn", m_Phone.transform, "OrderButton", raycast: true);
        Local(btnImg.rectTransform, kOrderX, kOrderY, kOrderW, kOrderH);
        m_OrderBtnImg = btnImg;
        m_OrderBtn = btnImg.gameObject.AddComponent<Button>();
        m_OrderBtn.targetGraphic = m_OrderBtnImg;
        m_OrderBtn.onClick.AddListener(() => { if (m_SelectedId >= 0) m_OnOrder?.Invoke(m_SelectedId); });
        JuicyButton.Attach(m_OrderBtn);
        UpdateOrderButton();

        BuildTip();   // 마지막에 만들어 항상 위에 그려진다
    }

    // ── 주문 그리드 (GameHudDriver가 depot 목록으로 호출) ──────────────
    public void BuildOrders(IReadOnlyList<OrderEntry> items, Action<int> onOrder)
    {
        if (m_Phone == null) return;
        m_OnOrder = onOrder;
        if (m_GridRoot != null) Destroy(m_GridRoot);
        m_Cards.Clear();
        m_SelectedId = -1;
        UpdateOrderButton();

        // 뷰포트 = 그리드 원점 ~ 실제 화면 하단(폰이 아래로 잠겨 있어 그 아래 카드는 안 보임 → 스크롤)
        float viewH = kDisplayBottom - kGridY - 4f;
        m_GridRoot = NewRect("Orders", m_Phone.transform, new Vector2(0, 1), new Vector2(0, 1),
                             new Vector2(kGridX * InGameUiSkin.S, -kGridY * InGameUiSkin.S),
                             new Vector2(kGridW * InGameUiSkin.S, viewH * InGameUiSkin.S));
        m_GridRoot.AddComponent<RectMask2D>();

        int rows = Mathf.CeilToInt(items.Count / (float)kCols);
        float contentH = rows > 0 ? (rows - 1) * kRowStep + kCardH : 0f;

        var content = NewRect("Content", m_GridRoot.transform, new Vector2(0, 1), new Vector2(1, 1),
                              Vector2.zero, new Vector2(0, contentH * InGameUiSkin.S));
        var crt = content.GetComponent<RectTransform>();
        crt.pivot = new Vector2(0.5f, 1f); crt.anchoredPosition = Vector2.zero;

        for (int i = 0; i < items.Count; i++)
        {
            int col = i % kCols, row = i / kCols;
            MakeCard(content.transform, items[i], col * kColStep, row * kRowStep);
        }

        if (contentH > viewH)
        {
            var sr = m_GridRoot.AddComponent<ScrollRect>();
            sr.content = crt;
            sr.viewport = m_GridRoot.GetComponent<RectTransform>();
            sr.horizontal = false; sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.scrollSensitivity = 20f;
        }
    }

    private void MakeCard(Transform parent, OrderEntry e, float x, float y)
    {
        var img = InGameUiSkin.SpriteImage("Card", parent, "OrderCard", raycast: true);
        Local(img.rectTransform, x, y, kCardW, kCardH);
        img.color = kCardIdle;
        var frame = img.gameObject.AddComponent<Outline>();   // 선택 테두리(주황)
        frame.effectColor = InGameUiSkin.Orange; frame.effectDistance = new Vector2(2f, 2f); frame.enabled = false;
        var btn = img.gameObject.AddComponent<Button>(); btn.targetGraphic = img;
        btn.transition = Selectable.Transition.None;
        int id = e.Id;
        btn.onClick.AddListener(() => Select(id));   // 카드 클릭 = 선택(주문은 [주문!] 버튼)
        JuicyButton.Attach(btn);

        var th = NewRect("Thumb", img.transform, new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, Vector2.zero);
        Local((RectTransform)th.transform, kThumbX, kThumbY, kThumbW, kThumbH);
        var ri = th.AddComponent<RawImage>();
        var tex = e.Prefab != null ? BlockThumbnail.Get(e.Prefab, 256) : null;
        if (tex != null) ri.texture = tex;
        else ri.color = new Color(1f, 1f, 1f, 0.15f);
        ri.raycastTarget = false;

        var nm = MakeText(img.transform, e.Name, new Vector2(kNameX, kNameY), new Vector2(kNameW, kNameH), Px(10), TextAnchor.MiddleCenter);
        nm.color = InGameUiSkin.TextGray;
        nm.horizontalOverflow = HorizontalWrapMode.Wrap; nm.verticalOverflow = VerticalWrapMode.Truncate;

        Text badge = null; GameObject badgeBg = null;
        if (e.Limit >= 0)
        {
            badgeBg = NewRect("BadgeBg", img.transform, new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, Vector2.zero);
            Local((RectTransform)badgeBg.transform, kBadgeX, kBadgeY, kBadgeW, kBadgeH);
            var bimg = badgeBg.AddComponent<Image>(); bimg.color = InGameUiSkin.Orange; bimg.raycastTarget = false;
            badge = MakeText(badgeBg.transform, $"재고: {e.Limit}개", Vector2.zero, new Vector2(kBadgeW, kBadgeH), Px(8), TextAnchor.MiddleCenter);
        }

        m_Cards[id] = new Card { Bg = img, Frame = frame, Thumb = ri, Badge = badge, BadgeBg = badgeBg,
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
            c.Bg.color = kCardPicked;
            UpdateOrderButton();
        }
        SelectionChanged?.Invoke(id);
    }

    public void ClearSelection()
    {
        if (m_SelectedId < 0) return;
        DeselectCardVisual();
        m_SelectedId = -1;
        UpdateOrderButton();
        SelectionChanged?.Invoke(-1);
    }

    private void DeselectCardVisual()
    {
        if (m_SelectedId >= 0 && m_Cards.TryGetValue(m_SelectedId, out var old))
        {
            old.Frame.enabled = false;
            old.Bg.color = old.Remaining == 0 ? kSoldOut : kCardIdle;
        }
    }

    /// <summary>수량 제한 재료의 잔량 반영. 품절이어도 선택은 되게 두고 [주문!] 버튼만 잠근다.</summary>
    public void SetRemaining(int id, int remaining)
    {
        if (remaining < 0 || !m_Cards.TryGetValue(id, out var c)) return;
        c.Remaining = remaining;
        m_Cards[id] = c;
        bool sold = remaining == 0;
        if (c.Badge != null) c.Badge.text = sold ? "품절" : $"재고: {remaining}개";
        if (c.Thumb != null && c.Thumb.texture != null)
            c.Thumb.color = sold ? new Color(0.4f, 0.4f, 0.4f, 1f) : Color.white;
        if (m_SelectedId != id) c.Bg.color = sold ? kSoldOut : kCardIdle;
        else UpdateOrderButton();
    }

    private void UpdateOrderButton()
    {
        bool can = m_SelectedId >= 0 && m_Cards.TryGetValue(m_SelectedId, out var c) && c.Remaining != 0;
        if (m_OrderBtn != null) m_OrderBtn.interactable = can;
        if (m_OrderBtnImg != null) m_OrderBtnImg.color = can ? Color.white : new Color(1f, 1f, 1f, 0.35f);
    }

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
        bg.color = new Color(1f, 1f, 1f, 0.96f);
        bg.raycastTarget = false;   // 커서 아래에 있어도 다른 UI 클릭 안 막음
        var ol = m_Tip.AddComponent<Outline>(); ol.effectColor = InGameUiSkin.CardGray; ol.effectDistance = new Vector2(2f, 2f);

        var th = NewRect("Thumb", m_Tip.transform, new Vector2(0, 1), new Vector2(0, 1),
                         new Vector2((kTipW - 128f) * 0.5f, -8f), new Vector2(128f, 128f));
        m_TipThumb = th.AddComponent<RawImage>();
        m_TipThumb.raycastTarget = false;

        m_TipName = MakeTextPx(m_Tip.transform, "", new Vector2(6, -140f), new Vector2(kTipW - 12, 24), 17, TextAnchor.MiddleCenter);
        m_TipName.fontStyle = FontStyle.Bold; m_TipName.color = InGameUiSkin.TextGray;
        m_TipSub = MakeTextPx(m_Tip.transform, "", new Vector2(6, -162f), new Vector2(kTipW - 12, 20), 13, TextAnchor.MiddleCenter);
        m_TipSub.color = InGameUiSkin.TextGray;

        m_Tip.SetActive(false);
    }

    // ── 빌더 헬퍼 ──
    private static int Px(float figmaPx) => Mathf.RoundToInt(figmaPx * InGameUiSkin.S);

    /// <summary>부모 좌상단 기준 피그마 px 배치(스케일 S).</summary>
    private static void Local(RectTransform rt, float x, float y, float w, float h)
        => InGameUiSkin.TopLeft(rt, x, y, w, h);

    private static GameObject NewRect(string name, Transform parent, Vector2 aMin, Vector2 aMax, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform)) { layer = 5 };
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = aMin; rt.anchorMax = aMax; rt.pivot = aMax;
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        return go;
    }

    private RawImage MakeRawImage(Transform parent, Vector2 figmaPos, Vector2 figmaSize)
    {
        var go = NewRect("Surface", parent, new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, Vector2.zero);
        Local((RectTransform)go.transform, figmaPos.x, figmaPos.y, figmaSize.x, figmaSize.y);
        var ri = go.AddComponent<RawImage>();
        ri.raycastTarget = false;   // 다른 UI 클릭 안 막음(라우팅은 좌표로 판정)
        return ri;
    }

    /// <summary>피그마 px 좌표/크기 텍스트.</summary>
    private static Text MakeText(Transform parent, string s, Vector2 figmaPos, Vector2 figmaSize, int fontSize, TextAnchor anchor)
    {
        var t = MakeTextPx(parent, s, Vector2.zero, Vector2.zero, fontSize, anchor);
        Local(t.rectTransform, figmaPos.x, figmaPos.y, figmaSize.x, figmaSize.y);
        return t;
    }

    /// <summary>캔버스 px 좌표/크기 텍스트(툴팁용).</summary>
    private static Text MakeTextPx(Transform parent, string s, Vector2 pos, Vector2 size, int fontSize, TextAnchor anchor)
    {
        var go = NewRect("Text", parent, new Vector2(0, 1), new Vector2(0, 1), pos, size);
        var t = go.AddComponent<Text>();
        t.font = s_Font; t.fontSize = fontSize; t.color = Color.white; t.text = s;
        t.alignment = anchor; t.horizontalOverflow = HorizontalWrapMode.Overflow; t.raycastTarget = false;
        return t;
    }
}
