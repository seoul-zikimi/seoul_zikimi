using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 좌상단 '조작법' 툴팁 HUD(리마스터). 펼침 = 조작법 패널 이미지(텍스트 구움) + 우측 탭(접기 버튼),
/// 접힘 = '조작법 보기' 버튼 이미지. 접힘 상태는 PlayerPrefs 로 기억.
/// 비주얼은 전부 피그마 익스포트 스프라이트(InGameUiSkin) — 코드는 조립·토글만.
/// UIManager가 Resources/UI/HUD/ControlsTooltipHUD 프리팹(빈 껍데기)에서 인스턴스화. GameLoopHUD 부트스트랩이 GameScene 진입 시 표시.
/// </summary>
public class ControlsTooltipHUD : UIHUD
{
    private const string kPref = "Jobsnail_ControlsTipCollapsed";

    // 피그마 '완성본 모습' 좌표(px): 펼침 패널 (18,26) 435x166 · 우측 탭 = 패널 로컬 (378,0) 57x34 · 접힘 버튼 (18,26) 94x34
    private GameObject m_Open, m_Closed;
    private bool m_Collapsed;

    public bool IsCollapsed => m_Collapsed;

    public override void Init()
    {
        // 펼침 패널
        var open = InGameUiSkin.SpriteImage("OpenPanel", transform, "Tooltip_Open");
        InGameUiSkin.TopLeft(open.rectTransform, 18, 26, 435, 166);
        open.gameObject.AddComponent<UiPopIn>();
        m_Open = open.gameObject;

        // 우측 탭 위 투명 버튼 → 접기. 화살표(별도 에셋 · 피그마 Group 8: 438,28 13x30 → 패널 로컬 420,2)는
        // 버튼의 자식으로 넣어 호버/클릭 때 같이 통통 튀게(JuicyButton).
        var tab = new GameObject("CollapseTab", typeof(RectTransform), typeof(Image), typeof(Button)) { layer = 5 };
        tab.transform.SetParent(open.transform, false);
        InGameUiSkin.TopLeft((RectTransform)tab.transform, 378, 0, 57, 34);
        var tabImg = tab.GetComponent<Image>();
        tabImg.color = new Color(1f, 1f, 1f, 0f);   // 투명 히트 영역
        tabImg.raycastTarget = true;
        var tabBtn = tab.GetComponent<Button>();
        tabBtn.targetGraphic = tabImg;
        tabBtn.transition = Selectable.Transition.None;
        tabBtn.onClick.AddListener(() => SetCollapsed(true));
        var arrow = InGameUiSkin.SpriteImage("CollapseArrow", tab.transform, "Tooltip_CollapseArrow");
        InGameUiSkin.TopLeft(arrow.rectTransform, 420 - 378, 2, 13, 30);
        JuicyButton.Attach(tabBtn);

        // 접힘 버튼('조작법 보기' + 화살표 구움)
        var closed = InGameUiSkin.SpriteImage("ClosedButton", transform, "Tooltip_Closed", raycast: true);
        InGameUiSkin.TopLeft(closed.rectTransform, 18, 26, 94, 34);
        var closedBtn = closed.gameObject.AddComponent<Button>();
        closedBtn.targetGraphic = closed;
        closedBtn.onClick.AddListener(() => SetCollapsed(false));
        JuicyButton.Attach(closedBtn);
        closed.gameObject.AddComponent<UiPopIn>();
        m_Closed = closed.gameObject;

        SetCollapsed(PlayerPrefs.GetInt(kPref, 0) == 1, playSfx: false);
    }

    public void SetCollapsed(bool collapsed, bool playSfx = true)
    {
        m_Collapsed = collapsed;
        PlayerPrefs.SetInt(kPref, collapsed ? 1 : 0);
        if (m_Open != null) m_Open.SetActive(!collapsed);
        if (m_Closed != null) m_Closed.SetActive(collapsed);
        if (playSfx && SoundManager.Instance != null) SoundManager.Instance.PlaySFX(SFXType.UIClick);
    }

    public void Toggle() => SetCollapsed(!m_Collapsed);
}
