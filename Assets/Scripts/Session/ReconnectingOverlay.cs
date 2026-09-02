using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 네트워크 재접속 시도 동안 화면 전체를 덮는 안내 오버레이.
/// 입력을 막아(뒤 UI 오조작 방지) "재접속 중..." 만 보여준다 — JobsnailSessionManager가 켜고 끈다.
/// </summary>
public static class ReconnectingOverlay
{
    private static GameObject s_Root;

    public static void Show()
    {
        if (s_Root != null)
            return;

        s_Root = new GameObject("@ReconnectingOverlay", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Object.DontDestroyOnLoad(s_Root);
        var canvas = s_Root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 900;   // 모든 게임 UI(팝업 30·인트로 600) 위

        var scaler = s_Root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        var dimGo = new GameObject("Dim", typeof(RectTransform), typeof(Image));
        var dimRt = (RectTransform)dimGo.transform;
        dimRt.SetParent(s_Root.transform, false);
        dimRt.anchorMin = Vector2.zero; dimRt.anchorMax = Vector2.one; dimRt.sizeDelta = Vector2.zero;
        dimGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);   // raycastTarget 기본 true — 뒤 UI 입력 차단

        var labelGo = new GameObject("Label", typeof(RectTransform));
        var labelRt = (RectTransform)labelGo.transform;
        labelRt.SetParent(s_Root.transform, false);
        labelRt.anchorMin = labelRt.anchorMax = new Vector2(0.5f, 0.5f);
        labelRt.sizeDelta = new Vector2(800f, 120f);
        var label = labelGo.AddComponent<Text>();
        label.font = JobsnailUiKit.LegacyFont;
        label.fontSize = 40;
        label.fontStyle = FontStyle.Bold;
        label.color = Color.white;
        label.alignment = TextAnchor.MiddleCenter;
        label.raycastTarget = false;

        var hintGo = new GameObject("Hint", typeof(RectTransform));
        var hintRt = (RectTransform)hintGo.transform;
        hintRt.SetParent(s_Root.transform, false);
        hintRt.anchorMin = hintRt.anchorMax = new Vector2(0.5f, 0.5f);
        hintRt.anchoredPosition = new Vector2(0f, -70f);
        hintRt.sizeDelta = new Vector2(800f, 44f);
        var hint = hintGo.AddComponent<Text>();
        hint.font = JobsnailUiKit.LegacyFont;
        hint.fontSize = 22;
        hint.color = new Color(1f, 1f, 1f, 0.75f);
        hint.alignment = TextAnchor.MiddleCenter;
        hint.text = "연결이 불안정해요. 잠시만 기다려 주세요";
        hint.raycastTarget = false;

        s_Root.AddComponent<DotsAnimator>().Label = label;
    }

    public static void Hide()
    {
        if (s_Root == null)
            return;
        Object.Destroy(s_Root);
        s_Root = null;
    }

    /// <summary>"재접속 중" 뒤에 점을 굴려 멈춘 화면이 아님을 보여준다.</summary>
    private sealed class DotsAnimator : MonoBehaviour
    {
        public Text Label;
        private float m_Next;
        private int m_Dots;

        private void Update()
        {
            if (Label == null || Time.unscaledTime < m_Next)
                return;
            m_Next = Time.unscaledTime + 0.4f;
            m_Dots = (m_Dots + 1) % 4;
            Label.text = "재접속 중" + new string('.', m_Dots);
        }
    }
}
