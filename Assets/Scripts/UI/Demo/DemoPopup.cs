using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UIManager Popup 사용 예시. Init()에서 UI를 코드로 생성.
/// Resources/UI/Popup/DemoPopup 프리팹에서 인스턴스화됨.
/// [C] 키로 닫기.
/// </summary>
public class DemoPopup : UIPopup
{
    public override void Init()
    {
        // 풀스크린 어두운 오버레이
        var rt = GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.65f);

        // 중앙 패널
        var panel = new GameObject("Panel", typeof(RectTransform));
        panel.transform.SetParent(transform, false);
        var panelRT = panel.GetComponent<RectTransform>();
        panelRT.anchorMin = new Vector2(0.3f, 0.35f);
        panelRT.anchorMax = new Vector2(0.7f, 0.65f);
        panelRT.offsetMin = panelRT.offsetMax = Vector2.zero;
        panel.AddComponent<Image>().color = new Color(0.12f, 0.12f, 0.18f, 1f);
    }
}
