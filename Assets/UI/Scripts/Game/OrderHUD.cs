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
        public readonly int Id; public readonly string Name;
        public Entry(int id, string name) { Id = id; Name = name; }
    }

    private const float kPanelW = 240f;   // 콘텐츠 패널 너비(접힘 계산에 사용)
    private const float kBtn    = 34f;    // 토글 버튼 한 변

    private static Font s_Font;
    private GameObject m_Panel;     // 접히는 콘텐츠
    private GameObject m_Toggle;    // 항상 보이는 토글 버튼
    private Text       m_Arrow;     // < (접힘=열기) / > (펼침=닫기)
    private bool       m_Collapsed;

    public override void Init() { }   // 내용은 Build()에서(재료 데이터 필요)

    public void Build(IReadOnlyList<Entry> items, Action<int> onOrder)
    {
        if (m_Panel != null)  Destroy(m_Panel);
        if (m_Toggle != null) Destroy(m_Toggle);
        if (s_Font == null) s_Font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        const float rowH = 30f, pad = 8f, titleH = 24f;
        float h = pad * 2 + titleH + items.Count * rowH;

        // ── 콘텐츠 패널 (우상단 고정) ──
        m_Panel = NewRect("Panel", transform, new Vector2(1, 1), new Vector2(1, 1),
                          new Vector2(-10, -10), new Vector2(kPanelW, h));
        m_Panel.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.7f);

        MakeText(m_Panel.transform, "재료 주문 (배송 → 좌클릭으로 줍기)",
                 new Vector2(8, -6), new Vector2(kPanelW - 16, titleH), 14, TextAnchor.MiddleLeft);

        for (int i = 0; i < items.Count; i++)
        {
            float y = -(pad + titleH + i * rowH);
            var e = items[i];
            MakeText(m_Panel.transform, e.Name, new Vector2(8, y), new Vector2(kPanelW - 92, 24), 14, TextAnchor.MiddleLeft);
            int id = e.Id;   // 클로저 캡처 고정
            MakeButton(m_Panel.transform, "주문", new Vector2(kPanelW - 80, y), new Vector2(72, 24), () => onOrder(id));
        }

        // ── 토글 버튼 (항상 표시, 패널 왼쪽 가장자리의 손잡이) ──
        m_Toggle = NewRect("Toggle", transform, new Vector2(1, 1), new Vector2(1, 1), Vector2.zero, new Vector2(kBtn, kBtn));
        var timg = m_Toggle.AddComponent<Image>(); timg.color = new Color(0f, 0f, 0f, 0.85f);
        var tbtn = m_Toggle.AddComponent<Button>(); tbtn.targetGraphic = timg;
        tbtn.onClick.AddListener(Toggle);

        var arrow = NewRect("Arrow", m_Toggle.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var art = arrow.GetComponent<RectTransform>();
        art.pivot = new Vector2(0.5f, 0.5f); art.offsetMin = art.offsetMax = Vector2.zero;
        m_Arrow = arrow.AddComponent<Text>();
        m_Arrow.font = s_Font; m_Arrow.fontSize = 22; m_Arrow.fontStyle = FontStyle.Bold;
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
                m_Collapsed ? new Vector2(-10, -10) : new Vector2(-(10 + kPanelW), -10);
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

        var lbl = NewRect("Label", go.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var lrt = lbl.GetComponent<RectTransform>();
        lrt.pivot = new Vector2(0.5f, 0.5f); lrt.offsetMin = lrt.offsetMax = Vector2.zero;
        var lt = lbl.AddComponent<Text>();
        lt.font = s_Font; lt.fontSize = 14; lt.color = Color.white; lt.text = s; lt.alignment = TextAnchor.MiddleCenter;
    }
}
