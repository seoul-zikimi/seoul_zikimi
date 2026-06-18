using UnityEngine;
using UnityEngine.InputSystem;
using GridSystem;

/// <summary>
/// 정답 패널 HUD 구동 + 입력 라우팅. AnswerPreview(GridSystem) 이벤트를 받아 UIManager HUD에 연결.
/// 커서가 패널(RawImage) 위면 우드래그=회전·스크롤=줌을 정답 카메라로, 아니면 플레이어 카메라로.
/// EventSystem/UIManager 부트는 GameHudDriver가 멱등 보장 → 여기선 구독/라우팅만.
/// </summary>
public class AnswerHudDriver : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        var go = new GameObject("~AnswerHudDriver");
        DontDestroyOnLoad(go);
        go.AddComponent<AnswerHudDriver>();
    }

    private AnswerPreview  m_Preview;
    private AnswerPanelHUD m_Hud;
    private bool           m_Visible;
    private bool           m_Dragging;   // 패널 위에서 우클릭 시작 → 버튼 뗄 때까지 회전 캡처

    private void OnEnable()
    {
        AnswerPreview.Ready             += OnReady;
        AnswerPreview.VisibilityChanged += OnVisibility;
    }
    private void OnDisable()
    {
        AnswerPreview.Ready             -= OnReady;
        AnswerPreview.VisibilityChanged -= OnVisibility;
        AnswerPanelFocus.Active = false;
    }

    private void OnReady(AnswerPreview p)
    {
        m_Preview = p;
        if (UIManager.Instance == null) return;
        m_Hud = UIManager.Instance.ShowHUDUI<AnswerPanelHUD>();
        m_Hud.SetTexture(p.RT);                                            // RT 재생성 대응(매 Build)
        m_Visible = p.IsVisible;
        if (!m_Visible) UIManager.Instance.HideHUDUI<AnswerPanelHUD>();    // 초기 가시성 동기화
    }

    private void OnVisibility(bool visible)
    {
        m_Visible = visible;
        if (UIManager.Instance == null) return;
        if (visible) m_Hud = UIManager.Instance.ShowHUDUI<AnswerPanelHUD>();
        else         UIManager.Instance.HideHUDUI<AnswerPanelHUD>();
    }

    private void Update()
    {
        if (m_Hud == null || m_Preview == null || Mouse.current == null) { AnswerPanelFocus.Active = false; return; }

        var rect = m_Hud.SurfaceRect;
        bool over = m_Visible && rect != null &&
            RectTransformUtility.RectangleContainsScreenPoint(rect, Mouse.current.position.ReadValue(), null);

        var rmb = Mouse.current.rightButton;
        if (rmb.wasPressedThisFrame && over) m_Dragging = true;   // 패널 위에서 우클릭 시작 → 캡처
        if (!rmb.isPressed || !m_Visible)    m_Dragging = false;  // 버튼 떼거나 패널 숨기면 해제

        bool focus = over || m_Dragging;   // 캡처 중이면 패널 밖이어도 정답 카메라가 입력 소유
        AnswerPanelFocus.Active = focus;   // 플레이어 카메라·게임 클릭이 read해서 양보
        if (focus)
        {
            Vector2 rot  = rmb.isPressed ? Mouse.current.delta.ReadValue() : Vector2.zero;
            float   zoom = over ? Mouse.current.scroll.ReadValue().y : 0f;   // 줌은 패널 위에서만
            if (rot != Vector2.zero || zoom != 0f) m_Preview.DriveOrbit(rot, zoom);
        }
    }
}
