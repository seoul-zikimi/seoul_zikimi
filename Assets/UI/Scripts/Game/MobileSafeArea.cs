using UnityEngine;

/// <summary>노치·다이내믹 아일랜드·홈 인디케이터를 피해 자식 컨트롤을 배치한다.</summary>
public sealed class MobileSafeArea : MonoBehaviour
{
    private Rect m_LastSafeArea;
    private Vector2Int m_LastScreen;

    private void OnEnable() => Apply();
    private void Update()
    {
        if (m_LastSafeArea != Screen.safeArea || m_LastScreen.x != Screen.width || m_LastScreen.y != Screen.height)
            Apply();
    }

    private void Apply()
    {
        if (Screen.width <= 0 || Screen.height <= 0) return;
        m_LastSafeArea = Screen.safeArea;
        m_LastScreen = new Vector2Int(Screen.width, Screen.height);
        var rt = transform as RectTransform;
        if (rt == null) return;

        // Device Simulator가 Game 탭과 함께 열려 있을 때 일부 Unity 버전은
        // safeArea를 시뮬레이터 원본 해상도로, Screen 크기는 Game 뷰 해상도로 보고한다.
        // 이 값을 그대로 anchor로 쓰면 우측 버튼이 화면 밖으로 밀려나므로 화면 범위로 제한한다.
        float xMin = Mathf.Clamp(m_LastSafeArea.xMin, 0f, Screen.width);
        float yMin = Mathf.Clamp(m_LastSafeArea.yMin, 0f, Screen.height);
        float xMax = Mathf.Clamp(m_LastSafeArea.xMax, xMin, Screen.width);
        float yMax = Mathf.Clamp(m_LastSafeArea.yMax, yMin, Screen.height);
        rt.anchorMin = new Vector2(xMin / Screen.width, yMin / Screen.height);
        rt.anchorMax = new Vector2(xMax / Screen.width, yMax / Screen.height);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }
}
