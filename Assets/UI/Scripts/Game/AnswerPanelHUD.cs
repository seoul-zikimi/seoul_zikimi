using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 좌하단 정답 패널 HUD. RawImage에 정답 카메라 RenderTexture 출력(인터랙티브 오빗 대상)
/// + 하단 호버 정보(커서 아래 블럭 이름·필요 공정 — AnswerHudDriver가 픽킹해서 넣어줌).
/// UIManager가 Resources/UI/HUD/AnswerPanelHUD 프리팹에서 인스턴스화. 입력 라우팅은 AnswerHudDriver.
/// </summary>
public class AnswerPanelHUD : UIHUD
{
    private const string kIdleSub = "커서를 블럭에 올리면 어떤 블럭인지 알려줘요";

    private static Font s_Font;
    private RawImage m_Surface;
    private Text m_InfoName;   // 호버한 블럭 이름
    private Text m_InfoSub;    // 필요 공정(리치텍스트) / 안내 문구

    // 커서 따라다니는 말풍선 툴팁(확대 썸네일 + 이름 + 공정) — 패널 하단 글씨가 잘 안 보인다는 피드백 반영
    private const float kTipW = 168f, kTipH = 188f;
    private GameObject m_Tip;
    private RectTransform m_TipRt;
    private RawImage m_TipThumb;
    private Text m_TipName, m_TipSub;

    public RectTransform SurfaceRect => m_Surface != null ? m_Surface.rectTransform : null;
    public void SetTexture(RenderTexture rt) { if (m_Surface != null) m_Surface.texture = rt; }

    /// <summary>호버 정보 표시. name이 비면 기본 안내 문구로 돌아간다. sub는 리치텍스트 허용.</summary>
    public void SetHoverInfo(string name, string sub)
    {
        bool has = !string.IsNullOrEmpty(name);
        if (m_InfoName != null) m_InfoName.text = has ? name : "";
        if (m_InfoSub != null)  m_InfoSub.text  = has ? sub : kIdleSub;
    }

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

    public override void Init()
    {
        if (s_Font == null) s_Font = JobsnailUiKit.LegacyFont;
        if (s_Font == null) s_Font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        const float w = 400f, img = 400f, titleH = 28f, infoH = 56f;
        float h = titleH + img + infoH;

        var panel = NewRect("Panel", transform, new Vector2(0, 0), new Vector2(0, 0),
                            new Vector2(14, 14), new Vector2(w, h));   // 좌하단
        var bg = panel.AddComponent<Image>(); bg.color = new Color(0f, 0f, 0f, 0.55f); bg.raycastTarget = false;
        panel.AddComponent<UiPopIn>();   // 등장 뽁

        MakeText(panel.transform, "정답 (TAB · 우클릭 회전 · 좌클릭 이동 · 휠 줌)",
                 new Vector2(2, 0), new Vector2(w - 4, titleH), 16, TextAnchor.MiddleLeft);

        m_Surface = MakeRawImage(panel.transform, new Vector2(0, -titleH), new Vector2(w, img));

        m_InfoName = MakeText(panel.transform, "",
                              new Vector2(8, -(titleH + img + 2)), new Vector2(w - 16, 26), 19, TextAnchor.MiddleLeft);
        m_InfoName.fontStyle = FontStyle.Bold;
        m_InfoSub = MakeText(panel.transform, kIdleSub,
                             new Vector2(8, -(titleH + img + 28)), new Vector2(w - 16, 22), 15, TextAnchor.MiddleLeft);
        m_InfoSub.color = new Color(0.85f, 0.85f, 0.85f);

        BuildTip();   // 패널보다 나중에 만들어 항상 위에 그려진다
    }

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

    private RawImage MakeRawImage(Transform parent, Vector2 pos, Vector2 size)
    {
        var go = NewRect("Surface", parent, new Vector2(0, 1), new Vector2(0, 1), pos, size);
        var ri = go.AddComponent<RawImage>();
        ri.raycastTarget = false;   // 다른 UI 클릭 안 막음(라우팅은 좌표로 판정)
        return ri;
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

    private static Text MakeText(Transform parent, string s, Vector2 pos, Vector2 size, int fontSize, TextAnchor anchor)
    {
        var go = NewRect("Text", parent, new Vector2(0, 1), new Vector2(0, 1), pos, size);
        var t = go.AddComponent<Text>();
        t.font = s_Font; t.fontSize = fontSize; t.color = Color.white; t.text = s;
        t.alignment = anchor; t.horizontalOverflow = HorizontalWrapMode.Overflow; t.raycastTarget = false;
        return t;
    }
}
