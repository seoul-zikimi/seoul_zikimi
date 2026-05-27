using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UIManager HUD 사용 예시. Init()에서 UI를 코드로 생성.
/// Resources/UI/HUD/DemoHUD 프리팹에서 인스턴스화됨.
/// </summary>
public class DemoHUD : UIHUD
{
    public override void Init()
    {
        // 풀스크린 파란 반투명 오버레이 (HUD 활성 표시용)
        var rt = GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;

        gameObject.AddComponent<Image>().color = new Color(0.1f, 0.5f, 1f, 0.18f);
    }
}
