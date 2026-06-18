using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 우하단 정답 패널 HUD. RawImage에 정답 카메라 RenderTexture 출력(인터랙티브 오빗 대상) + 색 범례.
/// UIManager가 Resources/UI/HUD/AnswerPanelHUD 프리팹에서 인스턴스화. 입력 라우팅은 AnswerHudDriver.
/// </summary>
public class AnswerPanelHUD : UIHUD
{
    private static Font s_Font;
    private RawImage m_Surface;

    public RectTransform SurfaceRect => m_Surface != null ? m_Surface.rectTransform : null;
    public void SetTexture(RenderTexture rt) { if (m_Surface != null) m_Surface.texture = rt; }

    public override void Init()
    {
        if (s_Font == null) s_Font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        const float w = 240f, img = 240f, titleH = 22f, legendH = 24f;
        float h = titleH + img + legendH;

        var panel = NewRect("Panel", transform, new Vector2(1, 0), new Vector2(1, 0),
                            new Vector2(-14, 14), new Vector2(w, h));
        var bg = panel.AddComponent<Image>(); bg.color = new Color(0f, 0f, 0f, 0.55f); bg.raycastTarget = false;

        MakeText(panel.transform, "정답 (TAB · 우클릭 회전 · 스크롤 줌)",
                 new Vector2(2, 0), new Vector2(w - 4, titleH), 13, TextAnchor.MiddleLeft);

        m_Surface = MakeRawImage(panel.transform, new Vector2(0, -titleH), new Vector2(w, img));

        var legend = NewRect("Legend", panel.transform, new Vector2(0, 1), new Vector2(0, 1),
                             new Vector2(4, -(titleH + img)), new Vector2(w - 4, legendH));
        Swatch(legend.transform, 0,   new Color(0.72f, 0.72f, 0.72f), "배치");
        Swatch(legend.transform, 78,  new Color(0.35f, 0.60f, 1.00f), "고정");
        Swatch(legend.transform, 156, new Color(0.30f, 0.85f, 0.40f), "페인트");
    }

    private RawImage MakeRawImage(Transform parent, Vector2 pos, Vector2 size)
    {
        var go = NewRect("Surface", parent, new Vector2(0, 1), new Vector2(0, 1), pos, size);
        var ri = go.AddComponent<RawImage>();
        ri.raycastTarget = false;   // 다른 UI 클릭 안 막음(라우팅은 좌표로 판정)
        return ri;
    }

    private void Swatch(Transform parent, float x, Color c, string label)
    {
        var sw = NewRect("Swatch", parent, new Vector2(0, 1), new Vector2(0, 1), new Vector2(x, -4), new Vector2(14, 14));
        var img = sw.AddComponent<Image>(); img.color = c; img.raycastTarget = false;
        MakeText(parent, label, new Vector2(x + 17, 0), new Vector2(58, 20), 12, TextAnchor.MiddleLeft);
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

    private static void MakeText(Transform parent, string s, Vector2 pos, Vector2 size, int fontSize, TextAnchor anchor)
    {
        var go = NewRect("Text", parent, new Vector2(0, 1), new Vector2(0, 1), pos, size);
        var t = go.AddComponent<Text>();
        t.font = s_Font; t.fontSize = fontSize; t.color = Color.white; t.text = s;
        t.alignment = anchor; t.horizontalOverflow = HorizontalWrapMode.Overflow; t.raycastTarget = false;
    }
}
