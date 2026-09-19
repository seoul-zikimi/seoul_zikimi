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
        labelRt.sizeDelta = new Vector2(1400f, 120f);   // 문구가 길어져 넓힘
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
        hint.text = L.T("잠시만 기다려 주세요. 20초 안에 다시 연결되지 않으면 방 목록으로 돌아갑니다",
                        "Please wait. If it doesn't reconnect within 20 seconds, you'll return to the room list");
        hint.raycastTarget = false;

        // '대기하지 않고 나가기' — 방장이 강제 종료된 경우 등, 재접속을 기다리는 동안 다른 UI가 전부 막혀(이 오버레이가 입력을 차단) 나갈 방법이 없었다.
        var btnGo = new GameObject("LeaveButton", typeof(RectTransform), typeof(Image), typeof(Button));
        var btnRt = (RectTransform)btnGo.transform;
        btnRt.SetParent(s_Root.transform, false);
        btnRt.anchorMin = btnRt.anchorMax = new Vector2(0.5f, 0.5f);
        btnRt.anchoredPosition = new Vector2(0f, -170f);
        btnRt.sizeDelta = new Vector2(420f, 72f);
        var btnImg = btnGo.GetComponent<Image>();
        btnImg.sprite = JobsnailUiKit.Sprite("UI_pngs/MyPage/RoundRect");
        btnImg.type = Image.Type.Sliced;
        btnImg.color = new Color(1f, 0.97f, 0.92f, 0.97f);
        var btn = btnGo.GetComponent<Button>();
        btn.targetGraphic = btnImg;
        btn.onClick.AddListener(() =>
            _ = JobsnailSessionManager.Instance.EndSessionBecauseHostLeftAsync(
                "재접속 대기 중 유저가 나가기 선택", L.T("방에서 나왔어요.", "You left the room.")));

        var btnLabelGo = new GameObject("Label", typeof(RectTransform));
        var btnLabelRt = (RectTransform)btnLabelGo.transform;
        btnLabelRt.SetParent(btnRt, false);
        btnLabelRt.anchorMin = Vector2.zero; btnLabelRt.anchorMax = Vector2.one; btnLabelRt.sizeDelta = Vector2.zero;
        var btnLabel = btnLabelGo.AddComponent<Text>();
        btnLabel.font = JobsnailUiKit.LegacyFont;
        btnLabel.fontSize = 28;
        btnLabel.fontStyle = FontStyle.Bold;
        btnLabel.color = new Color(0.24f, 0.16f, 0.11f);
        btnLabel.alignment = TextAnchor.MiddleCenter;
        btnLabel.text = L.T("대기하지 않고 나가기", "Leave without waiting");
        btnLabel.raycastTarget = false;

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
            Label.text = L.T("방장과 연결이 끊겼어요, 재접속 중", "Lost connection to the host, reconnecting") + new string('.', m_Dots);
        }
    }
}
