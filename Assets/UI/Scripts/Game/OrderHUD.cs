using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 우상단 재료 주문 HUD (접기/펴기). UIManager가 Resources/UI/HUD/OrderHUD 프리팹에서 인스턴스화.
/// 코너의 <,> 토글 버튼은 항상 표시, 콘텐츠 패널만 접힌다(우측으로 슬라이드 느낌).
/// Build()로 재료 목록 + 주문 콜백을 받아 행(이름 + 주문 버튼)을 코드로 구성.
/// </summary>
public class OrderHUD : UIHUD
{
    public readonly struct Entry
    {
        public readonly int Id; public readonly string Name; public readonly GameObject Prefab;
        public Entry(int id, string name, GameObject prefab) { Id = id; Name = name; Prefab = prefab; }
    }

    private const float kPanelW = 360f;   // 콘텐츠 패널 너비(접힘 계산에 사용)
    private const float kBtn    = 48f;    // 토글 버튼 한 변

    private static Font s_Font;
    private GameObject m_Panel;     // 접히는 콘텐츠
    private GameObject m_Toggle;    // 항상 보이는 토글 버튼
    private Text       m_Arrow;     // < (접힘=열기) / > (펼침=닫기)
    private bool       m_Collapsed;
    private float      m_PanelH;   // 패널 높이 (Apply에서 토글 Y 위치 계산용)

    public override void Init() { }   // 내용은 Build()에서(재료 데이터 필요)

    public void Build(IReadOnlyList<Entry> items, Action<int> onOrder)
    {
        if (m_Panel != null)  Destroy(m_Panel);
        if (m_Toggle != null) Destroy(m_Toggle);
        if (s_Font == null) s_Font = JobsnailUiKit.LegacyFont;
        if (s_Font == null) s_Font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        // ── 2열 카드 그리드: 큰 썸네일이 곧 주문 버튼, 이름은 밑에 작게. 길어지면 스크롤 ──
        const float pad = 12f, titleH = 32f, cardGap = 8f, kMaxPanelH = 480f;
        float cardW = (kPanelW - pad * 3) / 2f;          // 2열
        float thumbSize = cardW - 16f;                    // 카드 폭에 꽉 차는 썸네일
        float cardH = thumbSize + 26f;                    // 썸네일 + 이름 줄
        int rows = Mathf.CeilToInt(items.Count / 2f);
        float contentH = pad * 2 + titleH + rows * (cardH + cardGap);
        float h = Mathf.Min(contentH, kMaxPanelH);        // 재료가 많으면 높이 고정 + 스크롤

        m_PanelH = h;
        // ── 콘텐츠 패널 (우하단 고정, Y 오프셋으로 잘림 방지) ──
        m_Panel = NewRect("Panel", transform, new Vector2(1, 0f), new Vector2(1, 0f),
                          new Vector2(-10, 80), new Vector2(kPanelW, h));   // 우하단 기준, 80px 위부터 시작
        m_Panel.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.7f);

        // 뷰포트(마스크) + 콘텐츠 — 카드가 넘치면 휠로 스크롤
        var viewport = NewRect("Viewport", m_Panel.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var vrt = viewport.GetComponent<RectTransform>();
        vrt.pivot = new Vector2(0.5f, 0.5f); vrt.offsetMin = vrt.offsetMax = Vector2.zero;
        viewport.AddComponent<RectMask2D>();

        var content = NewRect("Content", viewport.transform, new Vector2(0, 1), new Vector2(1, 1), Vector2.zero, new Vector2(0, contentH));
        var crt = content.GetComponent<RectTransform>();
        crt.pivot = new Vector2(0.5f, 1f); crt.anchoredPosition = Vector2.zero;

        MakeText(content.transform, "재료 주문 (카드 클릭 = 주문)",
                 new Vector2(8, -6), new Vector2(kPanelW - 16, titleH), 17, TextAnchor.MiddleLeft);

        for (int i = 0; i < items.Count; i++)
        {
            int col = i % 2, row = i / 2;
            float x = pad + col * (cardW + pad);
            float y = -(pad + titleH + row * (cardH + cardGap));
            int id = items[i].Id;   // 클로저 캡처 고정
            MakeCard(content.transform, items[i], new Vector2(x, y), cardW, cardH, thumbSize, () => onOrder(id));
        }

        if (contentH > h)
        {
            var sr = m_Panel.AddComponent<ScrollRect>();
            sr.content = crt;
            sr.viewport = vrt;
            sr.horizontal = false;
            sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.scrollSensitivity = 24f;
        }

        // ── 토글 버튼 (항상 표시, 패널 왼쪽 가장자리의 손잡이) ──
        m_Toggle = NewRect("Toggle", transform, new Vector2(1, 0f), new Vector2(1, 0f), Vector2.zero, new Vector2(kBtn, kBtn));
        var timg = m_Toggle.AddComponent<Image>(); timg.color = new Color(0f, 0f, 0f, 0.85f);
        var tbtn = m_Toggle.AddComponent<Button>(); tbtn.targetGraphic = timg;
        tbtn.onClick.AddListener(Toggle);
        JuicyButton.Attach(tbtn);
        m_Panel.AddComponent<UiPopIn>();   // 펼칠 때마다 뽁 등장

        var arrow = NewRect("Arrow", m_Toggle.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var art = arrow.GetComponent<RectTransform>();
        art.pivot = new Vector2(0.5f, 0.5f); art.offsetMin = art.offsetMax = Vector2.zero;
        m_Arrow = arrow.AddComponent<Text>();
        m_Arrow.font = s_Font; m_Arrow.fontSize = 28; m_Arrow.fontStyle = FontStyle.Bold;
        m_Arrow.color = Color.white; m_Arrow.alignment = TextAnchor.MiddleCenter;

        m_Collapsed = false;   // 기본 펼침(주문 목록 보이게). 접힘으로 시작하려면 true.
        Apply();
    }

    private void Toggle() { m_Collapsed = !m_Collapsed; Apply(); }

    // 접힘 상태를 패널 표시·버튼 위치·화살표에 반영.
    private void Apply()
    {
        if (m_Panel != null) m_Panel.SetActive(!m_Collapsed);
        if (m_Toggle != null)   // 펼침이면 패널 왼쪽으로, 접힘이면 코너로
            m_Toggle.GetComponent<RectTransform>().anchoredPosition =
                m_Collapsed ? new Vector2(-10, 80) : new Vector2(-(10 + kPanelW), 80);
        if (m_Arrow != null) m_Arrow.text = m_Collapsed ? "<" : ">";
    }

    // ── uGUI 빌더 헬퍼 ───────────────────────────────────────────────
    private static GameObject NewRect(string name, Transform parent, Vector2 aMin, Vector2 aMax,
                                      Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform)) { layer = 5 /* UI */ };
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = aMin; rt.anchorMax = aMax; rt.pivot = aMax;
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        return go;
    }

    // 주문 카드: 카드 전체가 버튼, 위엔 큰 썸네일·아래엔 작은 이름 (한눈에 무슨 블록인지 보이게)
    private static void MakeCard(Transform parent, Entry e, Vector2 pos, float w, float h, float thumbSize, Action onClick)
    {
        var card = NewRect("Card", parent, new Vector2(0, 1), new Vector2(0, 1), pos, new Vector2(w, h));
        var img = card.AddComponent<Image>(); img.color = new Color(1f, 1f, 1f, 0.10f);
        var btn = card.AddComponent<Button>(); btn.targetGraphic = img;
        btn.onClick.AddListener(() => onClick());
        JuicyButton.Attach(btn);

        var thumb = NewRect("Thumb", card.transform, new Vector2(0, 1), new Vector2(0, 1),
                            new Vector2((w - thumbSize) * 0.5f, -4f), new Vector2(thumbSize, thumbSize));
        var ri = thumb.AddComponent<RawImage>();
        var tex = e.Prefab != null ? BlockThumbnail.Get(e.Prefab, 256) : null;   // 큰 카드니까 고해상 렌더
        if (tex != null) ri.texture = tex;
        else ri.color = new Color(1f, 1f, 1f, 0.15f);
        ri.raycastTarget = false;   // 클릭은 카드가 받는다

        MakeText(card.transform, e.Name, new Vector2(4, -(4f + thumbSize)), new Vector2(w - 8, 18), 13, TextAnchor.MiddleCenter);
    }

    private static void MakeText(Transform parent, string s, Vector2 pos, Vector2 size, int fontSize, TextAnchor anchor)
    {
        var go = NewRect("Text", parent, new Vector2(0, 1), new Vector2(0, 1), pos, size);
        var t = go.AddComponent<Text>();
        t.font = s_Font; t.fontSize = fontSize; t.color = Color.white; t.text = s;
        t.alignment = anchor; t.horizontalOverflow = HorizontalWrapMode.Overflow;
    }

    private static void MakeButton(Transform parent, string s, Vector2 pos, Vector2 size, Action onClick)
    {
        var go = NewRect("Button", parent, new Vector2(0, 1), new Vector2(0, 1), pos, size);
        var img = go.AddComponent<Image>(); img.color = new Color(0.25f, 0.42f, 0.72f, 1f);
        var btn = go.AddComponent<Button>(); btn.targetGraphic = img;
        btn.onClick.AddListener(() => onClick());
        JuicyButton.Attach(btn);

        var lbl = NewRect("Label", go.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var lrt = lbl.GetComponent<RectTransform>();
        lrt.pivot = new Vector2(0.5f, 0.5f); lrt.offsetMin = lrt.offsetMax = Vector2.zero;
        var lt = lbl.AddComponent<Text>();
        lt.font = s_Font; lt.fontSize = 17; lt.color = Color.white; lt.text = s; lt.alignment = TextAnchor.MiddleCenter;
    }
}
