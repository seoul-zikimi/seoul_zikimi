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
    private GameLoopManager m_Loop;      // 완성도 배지(모바일)용
    private GridNetwork     m_Net;
    private float           m_NextCompletion;
    private bool           m_Visible;
    private bool           m_Dragging;   // 패널 위에서 우클릭 시작 → 버튼 뗄 때까지 회전 캡처
    private Vector2        m_PressPos;   // 좌클릭 시작 위치 — 클릭(선택)과 드래그(팬) 구분용
    private bool           m_PressOnPanel;

    private void OnEnable()
    {
        AnswerPreview.Ready             += OnReady;
        AnswerPreview.VisibilityChanged += OnVisibility;
    }
    private void OnDisable()
    {
        AnswerPreview.Ready             -= OnReady;
        AnswerPreview.VisibilityChanged -= OnVisibility;
        if (m_Hud != null) m_Hud.SelectionChanged -= OnHudSelection;
        if (m_Preview != null) m_Preview.SelectionAutoCleared -= OnSelectionAutoCleared;
        AnswerPanelFocus.Active = false;
    }

    private void OnReady(AnswerPreview p)
    {
        m_Preview = p;
        // 모바일 흰색 폰 화면에 맞춰 미니 프리뷰 배경을 밝은 회색으로(데스크톱은 기본 어두운 색 유지)
        if (MobileControlsHUD.ShouldUseMobileUI)
            p.SetBackground(new Color(0.90f, 0.90f, 0.89f, 1f));
        if (UIManager.Instance == null) return;
        m_Hud = UIManager.Instance.ShowHUDUI<AnswerPanelHUD>();
        m_Hud.SetTexture(p.RT);                                            // RT 재생성 대응(매 Build)
        m_Hud.SelectionChanged -= OnHudSelection;                          // 재구독(중복 방지)
        m_Hud.SelectionChanged += OnHudSelection;
        m_Preview.SelectionAutoCleared -= OnSelectionAutoCleared;          // 다 지으면 프리뷰가 선택을 풀음 → HUD 카드도 해제
        m_Preview.SelectionAutoCleared += OnSelectionAutoCleared;
        m_Visible = p.IsVisible;
        if (!m_Visible) UIManager.Instance.HideHUDUI<AnswerPanelHUD>();    // 초기 가시성 동기화
    }

    // HUD 카드 선택 ↔ 3D 뷰 테두리 동기화 (id -1 = 해제)
    private void OnSelectionAutoCleared()
    {
        if (m_Hud != null) m_Hud.ClearSelection();
    }

    private void OnHudSelection(int id)
    {
        if (m_Preview == null) return;
        if (id < 0) m_Preview.ClearSelection();
        else        m_Preview.SelectMaterialById(id);
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
        var gameplayInput = Player.PlayerInputHandler.Local;
        if (gameplayInput != null && gameplayInput.ConsumeToggleOrder())
        {
            // PC: TAB = 인월드 정답 고스트만. 모바일: 폰 버튼 = 폰 접기/펴기(고스트는 눈 버튼 담당).
            if (MobileControlsHUD.ShouldUseMobileUI) { if (m_Hud != null) m_Hud.ToggleCollapsed(); }
            else if (m_Preview != null) m_Preview.ToggleVisibility();
        }

        // 모바일에서는 AnswerPanelHUD가 좌측 완공 계획도/우측 재료 카탈로그의
        // 전체화면 레이아웃을 사용한다. 표시 중 월드 조작 잠금은 아래 포커스와
        // MobileControlsHUD의 VisibilityChanged 구독이 함께 담당한다.

        UpdateCompletionBadge();   // 마우스 없는 기기에서도 돌도록 아래 early-return 앞에서

        if (m_Hud == null || m_Preview == null || Mouse.current == null) { AnswerPanelFocus.Active = false; return; }

        var rect = m_Hud.SurfaceRect;
        bool over = m_Visible && m_Hud.PhoneOpen && rect != null && !m_Hud.ChromeHovered &&   // 접힘·확대 버튼·도움말 위에선 정답 뷰 입력 양보
            RectTransformUtility.RectangleContainsScreenPoint(rect, Mouse.current.position.ReadValue(), null);

        // 좌클릭·우클릭 어느 쪽이든 패널 위에서 드래그 시작 → 회전(좌클릭이 더 직관적이라는 피드백 반영).
        // 패널 위에서 시작한 좌클릭은 AnswerPanelFocus 덕에 게임 줍기로 안 새어나간다(PlayerCarry가 양보).
        var rmb = Mouse.current.rightButton;
        var lmb = Mouse.current.leftButton;
        var cursor = Mouse.current.position.ReadValue();
        bool anyPressed = rmb.isPressed || lmb.isPressed;
        if ((rmb.wasPressedThisFrame || lmb.wasPressedThisFrame) && over) m_Dragging = true;
        if (!anyPressed || !m_Visible) m_Dragging = false;   // 버튼 떼거나 패널 숨기면 해제

        // 클릭 = 선택: 누른 자리에서 거의 안 움직이고 뗐으면 팬이 아니라 클릭으로 본다(6px 임계).
        if (lmb.wasPressedThisFrame && over) { m_PressOnPanel = true; m_PressPos = cursor; }
        if (lmb.wasReleasedThisFrame)
        {
            if (m_PressOnPanel && over && (cursor - m_PressPos).sqrMagnitude < 36f)
                ClickSelect(rect, cursor);
            m_PressOnPanel = false;
        }

        bool focus = over || m_Dragging;   // 캡처 중이면 패널 밖이어도 정답 카메라가 입력 소유
        AnswerPanelFocus.Active = focus;   // 플레이어 카메라·게임 클릭이 read해서 양보
        if (focus)
        {
            // 우클릭 드래그 = 회전 · 좌클릭 드래그 = 상하좌우 이동(팬) · 휠 = 줌
            Vector2 delta = m_Dragging ? Mouse.current.delta.ReadValue() : Vector2.zero;
            float   zoom  = over ? Mouse.current.scroll.ReadValue().y : 0f;   // 줌은 패널 위에서만
            if (rmb.isPressed && delta != Vector2.zero) m_Preview.DriveOrbit(delta, 0f);
            else if (lmb.isPressed && delta != Vector2.zero) m_Preview.DrivePan(delta);
            if (zoom != 0f) m_Preview.DriveOrbit(Vector2.zero, zoom);
        }

        UpdateHover(rect, over && !anyPressed);   // 드래그 중엔 호버 끔(회전하다 라벨이 튀지 않게)
    }

    // 폰 화면의 '현재 완성도 : N%' 배지 갱신(0.25초 스로틀). 팀전이면 우리 팀 점수.
    private void UpdateCompletionBadge()
    {
        if (m_Hud == null || !m_Visible || Time.unscaledTime < m_NextCompletion) return;
        m_NextCompletion = Time.unscaledTime + 0.25f;
        if (m_Loop == null) m_Loop = FindFirstObjectByType<GameLoopManager>();
        if (m_Loop == null) return;
        if (m_Loop.IsVersus && m_Net == null) m_Net = FindFirstObjectByType<GridNetwork>();
        var score = m_Loop.IsVersus && m_Net != null ? m_Net.ScoreFor(m_Loop.LocalTeam) : m_Loop.Score;
        m_Hud.SetCompletion(Mathf.RoundToInt(score.Percent));
    }

    // 화면 클릭 → 미니씬 픽킹 → 같은 재료 전체 테두리 + HUD 카드 선택. 빈 곳 클릭 = 해제.
    private void ClickSelect(RectTransform rect, Vector2 screenPos)
    {
        var uv = ToViewportUV(rect, screenPos);
        if (m_Preview.TrySelectAt(uv, out var def)) m_Hud.Select(def.Id);
        else { m_Preview.ClearSelection(); m_Hud.ClearSelection(); }
    }

    // ── 호버 픽킹: 커서 아래 블럭 하이라이트 + 말풍선 툴팁 ──
    private void UpdateHover(RectTransform rect, bool active)
    {
        if (!active)
        {
            m_Preview.ClearHover();
            m_Hud.HideTip();
            return;
        }

        var screenPos = Mouse.current.position.ReadValue();
        var uv = ToViewportUV(rect, screenPos);
        if (m_Preview.TryHover(uv, out var def))
        {
            string name = def != null ? def.name : "블럭";
            // 말풍선: 주문 카드와 같은 썸네일 렌더 재사용 → "정답의 이 블럭 = 저 카드" 매칭이 눈에 보인다
            m_Hud.ShowTip(screenPos, name, ProcLine(def),
                          def != null && def.Prefab != null ? BlockThumbnail.Get(def.Prefab, 256) : null);
        }
        else m_Hud.HideTip();
    }

    // 패널 스크린 좌표 → RawImage 로컬 → 뷰포트 UV(0~1). 오버레이 캔버스라 카메라 null.
    private static Vector2 ToViewportUV(RectTransform rect, Vector2 screenPos)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, screenPos, null, out var local);
        var r = rect.rect;
        return new Vector2((local.x - r.xMin) / r.width, (local.y - r.yMin) / r.height);
    }

    // 필요 공정을 공정 고유색(망치=파랑, 페인트=초록)의 리치텍스트로 — 없으면 "놓기만 하면 완성".
    // GameHudDriver도 주문 카드용으로 같은 문자열을 쓴다.
    public static string ProcLine(MaterialDef def)
    {
        if (def == null) return "";
        string s = "";
        foreach (var p in def.RequiredProcesses)
            s += (s.Length > 0 ? "  " : "")
               + (p == ProcessType.Fixed
                    ? "<color=#5C9AFF>망치로 고정 필요</color>"
                    : "<color=#4DD966>페인트칠 필요</color>");
        return s.Length > 0 ? s : "놓기만 하면 완성";
    }
}
