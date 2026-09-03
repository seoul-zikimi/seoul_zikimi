using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// '완공 계획도 폰' HUD(리마스터) — 정답 3D 뷰(위)와 재료 카탈로그(아래)를 폰 한 화면에 통합.
/// 비주얼은 피그마 익스포트(PhoneBg: 상태바·제목·'현재 완성도' 뱃지·'재료 카탈로그' 구움) + 카드/주문 버튼 스프라이트.
/// 화면 블럭 클릭 = 선택(오주문 방지 2단계), 카드 클릭 = 선택, [주문!] 버튼으로 확정. TAB = 폰 꺼내기/넣기.
/// 3D 뷰 텍스처·입력 라우팅·호버/선택 픽킹은 AnswerHudDriver, 주문 목록·잔량·완성도는 GameHudDriver가 채운다.
/// UIManager가 Resources/UI/HUD/AnswerPanelHUD 프리팹(빈 껍데기)에서 인스턴스화.
/// </summary>
public class AnswerPanelHUD : UIHUD
{
    public struct OrderEntry
    {
        public int Id; public string Name; public GameObject Prefab;
        public int Limit;     // MaxSpawnCount (-1 = 무제한 → 배지 없음)
        public string Sub;    // 필요 공정 리치텍스트(드라이버가 계산 — 이 클래스는 GridSystem을 모른다)
    }

    private const string kIdleName = "블럭을 골라 주문하세요";
    private const string kIdleSub = "화면 블럭 클릭 = 선택 · 우클릭 회전 · 휠 줌";

    // ── 피그마 '완성본 모습' 좌표(px · 폰 좌상단 원점) ──
    private const float kPhoneX = 1029f, kPhoneY = 217f, kPhoneW = 304f, kPhoneH = 625f;   // 프레임 기준(아래로 89px 잠김 = 디자인)
    private const float kPctX = 88f, kPctY = 94f, kPctW = 27f, kPctH = 15f;               // '현재 완성도 : [  ]%' 빈칸
    private const float kViewX = 38f, kViewY = 114f, kViewS = 228f;                          // 3D 뷰(정사각)
    private const float kOrderX = 219f, kOrderY = 355f, kOrderW = 46f, kOrderH = 22f;       // [주문!]
    private const float kGridX = 35f, kGridY = 383f, kGridW = 230f;                          // 카드 그리드 원점
    private const float kCardW = 111f, kCardH = 59f, kColStep = 119f, kRowStep = 65f;
    private const int   kCols = 2;
    private const float kDisplayBottom = InGameUiSkin.FrameH - kPhoneY;                     // 폰 로컬 기준 화면 하단(536) — 그 아래는 안 보임
    // 카드 내부
    private const float kThumbX = 6f, kThumbY = 7f, kThumbW = 43f, kThumbH = 46f;
    private const float kNameX = 53f, kNameY = 8f, kNameW = 53f, kNameH = 34f;
    private const float kBadgeX = 60f, kBadgeY = 46f, kBadgeW = 50f, kBadgeH = 12f;
    // 정답 뷰 우상단 확대 버튼(25x25) · '도움말 ?' 호버 영역(배경에 구워진 글자/아이콘 위)
    private const float kExpandW = 25f, kExpandH = 25f;
    private const float kHelpX = 214f, kHelpY = 69f, kHelpW = 50f, kHelpH = 24f;

    private static Font s_Font;
    private GameObject m_Phone;
    private RawImage m_Surface;
    private Text m_SelName, m_SelSub;
    private Text m_CompletionText;
    private Text m_PctText;
    private Button m_OrderBtn;
    private Image m_OrderBtnImg;
    private GameObject m_GridRoot;

    private struct Card
    {
        public Image Bg; public Outline Frame; public RawImage Thumb; public Text Badge; public GameObject BadgeBg;
        public string Name; public string Sub; public int Remaining;   // -1 = 무제한
    }
    private readonly Dictionary<int, Card> m_Cards = new();
    private int m_SelectedId = -1;
    private const float kDoubleClickSec = 0.3f;   // 이 안에 같은 재료를 다시 클릭 = 즉시 주문
    private int m_LastClickId = -1;
    private float m_LastClickTime;
    private Action<int> m_OnOrder;
    private bool m_MobileLayout;        // 가로(랜드스케이프) 레이아웃 사용 중 — 모바일 기기 또는 확대 보기
    private bool m_IsMobileDevice;      // 모바일 포팅 여부(항상 가로)
    private bool m_ExpandedView;        // PC에서 확대 버튼으로 가로 전체화면 보기 중
    private RenderTexture m_Texture;    // 재구성 때 되살릴 정답 뷰 RT
    private int m_LastPct;
    private int m_ShownPct = -1;   // 실제 텍스트에 반영된 값 — 같으면 SetCompletion이 조기 리턴
    private IReadOnlyList<OrderEntry> m_CachedItems;
    private readonly Dictionary<int, int> m_CachedRemaining = new();
    private GameObject m_HelpTip;
    private GameObject m_CollapseTab;   // 폰 접기/펴기 손잡이(폰이 숨어도 남는다)
    private GameObject m_LandscapeClose; // 가로 화면 하단 '폰 내리기 / 작게 보기' 버튼
    private Text m_CollapseLabel;
    private bool m_Collapsed;

    /// <summary>폰이 펼쳐졌는지(모바일 월드 입력 잠금 등에서 구독).</summary>
    public static event Action<bool> PhoneVisibilityChanged;
    public bool PhoneOpen => !m_Collapsed;

    /// <summary>커서가 폰 UI 부품(확대 버튼·도움말) 위 — AnswerHudDriver가 정답 뷰 클릭/호버를 양보한다.</summary>
    public bool ChromeHovered { get; private set; }

    private static readonly Color kCardIdle   = Color.white;                         // 스프라이트 원색(#BEC3CD)
    private static readonly Color kCardPicked = new Color(1f, 0.86f, 0.74f, 1f);     // 살구빛 틴트
    private static readonly Color kSoldOut    = new Color(0.72f, 0.72f, 0.72f, 1f);
    private static readonly Color kSelGreen   = new Color(0.30f, 0.85f, 0.40f);

    /// <summary>선택이 바뀜(-1 = 해제) — AnswerHudDriver가 3D 뷰 테두리를 동기화.</summary>
    public event Action<int> SelectionChanged;

    public RectTransform SurfaceRect => m_Surface != null ? m_Surface.rectTransform : null;
    public void SetTexture(RenderTexture rt) { m_Texture = rt; if (m_Surface != null) m_Surface.texture = rt; }

    // UIManager가 HUD를 캐시하므로, 만들어질 때와 지금의 모바일 여부가 달라졌으면(에디터 프리뷰 토글 등)
    // 데스크톱 폰 UI가 모바일 화면에 그대로 재사용된다 — 표시될 때마다 검사해 전부 다시 짓는다.
    private void OnEnable()
    {
        if (m_Phone == null || m_IsMobileDevice == MobileControlsHUD.ShouldUseMobileUI) return;
        Rebuild();   // 자식을 전부 지우고 백지에서 다시 지은 뒤 RT·주문·선택·완성도를 복원
    }

    /// <summary>'현재 완성도 : N%' 숫자 갱신(GameHudDriver가 매 프레임 호출).</summary>
    public void SetCompletion(int percent)
    {
        int clamped = Mathf.Clamp(percent, 0, 100);
        m_LastPct = clamped;
        if (clamped == m_ShownPct) return;   // 매 프레임 호출됨 — 값이 그대로면 문자열 조립부터 스킵
        m_ShownPct = clamped;
        string s = clamped.ToString();
        if (m_PctText != null && m_PctText.text != s) m_PctText.text = s;
        if (m_CompletionText != null)
        {
            string mobile = $"현재 완성도 : {clamped}%";
            if (m_CompletionText.text != mobile) m_CompletionText.text = mobile;
        }
    }

    public override void Init()
    {
        if (s_Font == null) s_Font = JobsnailUiKit.LegacyFont;
        if (s_Font == null) s_Font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (!InGameUiSkin.Available)
            Debug.LogWarning("[AnswerPanelHUD] 리마스터 스프라이트 없음 — Assets/Resources/UI_pngs/3.inGame/Remaster 확인");

        // 어떤 경로로 두 번 불려도(레이아웃 재구축·유령 중복 인스턴스) 요소가 누적되지 않게 항상 백지에서 짓는다.
        // 스케일 안 맞은 옛 3D 뷰/카드가 새 폰 위에 겹쳐 보이던 문제의 방어선.
        for (int i = transform.childCount - 1; i >= 0; i--) Destroy(transform.GetChild(i).gameObject);
        m_Phone = null; m_GridRoot = null; m_Surface = null; m_Tip = null;
        m_HelpTip = null; m_CollapseTab = null; m_CollapseLabel = null; m_LandscapeClose = null;
        m_BlockBanner = null; m_BlockText = null;
        m_SelName = m_SelSub = m_CompletionText = null;
        m_PctText = null; m_OrderBtn = null; m_OrderBtnImg = null;
        m_ShownPct = -1;   // 텍스트를 새로 지었으니 다음 SetCompletion이 반드시 다시 채우게
        m_Cards.Clear(); m_SelectedId = -1; m_LastClickId = -1; ChromeHovered = false;
        foreach (var other in FindObjectsByType<AnswerPanelHUD>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (other != this) Destroy(other.gameObject);   // 루트/캐시 꼬임으로 남은 중복 HUD 정리

        m_IsMobileDevice = MobileControlsHUD.ShouldUseMobileUI;
        m_MobileLayout = m_IsMobileDevice || m_ExpandedView;   // 확대 보기 = 모바일과 같은 가로 폰 화면
        if (m_MobileLayout)
        {
            InitMobileLayout();
            return;
        }

        // ── 폰 본체(우하단 · 디자인대로 아래로 살짝 잠김) ──
        var bg = InGameUiSkin.SpriteImage("Phone", transform, "PhoneBg");
        InGameUiSkin.BottomRight(bg.rectTransform, kPhoneX, kPhoneY, kPhoneW, kPhoneH);
        m_Phone = bg.gameObject;
        m_Phone.AddComponent<UiPopIn>();   // 폰 꺼낼 때 뽁

        // 완성도 숫자(뱃지 빈칸 · 우측 정렬이라 '%' 앞에 붙는다)
        m_PctText = MakeText(m_Phone.transform, "0", new Vector2(kPctX, kPctY), new Vector2(kPctW, kPctH), Px(11), TextAnchor.MiddleRight);
        m_PctText.fontStyle = FontStyle.Bold;

        // 3D 정답 뷰
        m_Surface = MakeRawImage(m_Phone.transform, new Vector2(kViewX, kViewY), new Vector2(kViewS, kViewS));

        // [주문!] 버튼(텍스트 구움) — 선택 없으면 반투명
        var btnImg = InGameUiSkin.SpriteImage("OrderBtn", m_Phone.transform, "OrderButton", raycast: true);
        Local(btnImg.rectTransform, kOrderX, kOrderY, kOrderW, kOrderH);
        m_OrderBtnImg = btnImg;
        m_OrderBtn = btnImg.gameObject.AddComponent<Button>();
        m_OrderBtn.targetGraphic = m_OrderBtnImg;
        m_OrderBtn.onClick.AddListener(() => { if (m_SelectedId >= 0) m_OnOrder?.Invoke(m_SelectedId); });
        JuicyButton.Attach(m_OrderBtn);
        UpdateOrderButton();

        // 정답 뷰 우상단 확대 버튼 — 누르면 가로 폰 전체화면
        var expand = InGameUiSkin.SpriteImage("ExpandBtn", m_Phone.transform, "AnswerExpandButton", raycast: true);
        Local(expand.rectTransform, kViewX + kViewS - kExpandW, kViewY, kExpandW, kExpandH);
        var expandBtn = expand.gameObject.AddComponent<Button>();
        expandBtn.targetGraphic = expand;
        expandBtn.transition = Selectable.Transition.None;
        expandBtn.onClick.AddListener(ToggleExpanded);
        JuicyButton.Attach(expandBtn);
        HoverRelay.Attach(expand.gameObject, on => ChromeHovered = on);

        // '도움말 ?'(배경에 구워짐) 위 투명 호버 영역 — 정답 뷰 조작법 말풍선
        var help = NewRect("HelpHotspot", m_Phone.transform, new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, Vector2.zero);
        Local((RectTransform)help.transform, kHelpX, kHelpY, kHelpW, kHelpH);
        var helpImg = help.AddComponent<Image>();
        helpImg.color = new Color(1f, 1f, 1f, 0f);
        helpImg.raycastTarget = true;
        BuildHelpTip();
        HoverRelay.Attach(help, on =>
        {
            ChromeHovered = on;
            ShowHelpTip(on);
        });

        BuildCollapseTab();
        BuildBlockBanner();
        BuildTip();   // 마지막에 만들어 항상 위에 그려진다
    }

    // ── 폰 접기/펴기 손잡이 — 조작법 툴팁처럼 화살표 클릭으로 여닫는다(TAB은 고스트 전용) ──
    private void BuildCollapseTab()
    {
        m_CollapseTab = NewRect("PhoneTab", transform, new Vector2(1f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(64f, 64f));
        var img = m_CollapseTab.AddComponent<Image>();
        img.sprite = JobsnailUiKit.Sprite("UI_pngs/MyPage/RoundRect");
        img.type = Image.Type.Sliced;
        img.color = new Color(1f, 0.97f, 0.92f, 0.97f);
        var btn = m_CollapseTab.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(ToggleCollapsed);
        JuicyButton.Attach(btn);
        m_CollapseLabel = MakeTextPx(m_CollapseTab.transform, "", Vector2.zero, new Vector2(64f, 64f), 34, TextAnchor.MiddleCenter);
        m_CollapseLabel.color = InGameUiSkin.TextGray;
        m_CollapseLabel.fontStyle = FontStyle.Bold;

        // 데스크톱: 판 시작 = 펼침(고스트도 기본 켜짐). 모바일: 전체화면 주문서가 시작부터 덮으면 조작을 못 해
        // 접힌 채로 시작 — 폰 버튼을 눌러야 열린다(기획 2026-09-04).
        m_Collapsed = m_IsMobileDevice;
        ApplyCollapsed();
    }

    /// <summary>폰 접기/펴기(손잡이 클릭 · 모바일 폰 버튼).</summary>
    public void ToggleCollapsed()
    {
        m_Collapsed = !m_Collapsed;
        ApplyCollapsed();
        if (SoundManager.Instance != null) SoundManager.Instance.PlaySFX(SFXType.UIClick);
    }

    private void ApplyCollapsed()
    {
        // 폰이 접히면 미니씬 RT가 화면에서 사라지므로 정답 카메라도 쉬게 한다(AnswerPreview가 읽음)
        GridSystem.AnswerPreview.PanelOpen = !m_Collapsed;
        if (m_Phone != null) m_Phone.SetActive(!m_Collapsed);
        if (m_CollapseTab != null)
        {
            // 펼침 = 폰 위쪽 손잡이 / 접힘 = 화면 우하단 구석
            var rt = (RectTransform)m_CollapseTab.transform;
            rt.anchoredPosition = m_Collapsed ? new Vector2(-7f, 24f) : new Vector2(-7f, 779f);   // 화면 오른쪽 끝(폰과 같은 라인)
            m_CollapseTab.SetActive(!m_MobileLayout);   // 가로 화면은 자체 '작게 보기/폰 내리기' 버튼 사용
        }
        if (m_CollapseLabel != null) m_CollapseLabel.text = m_Collapsed ? "▲" : "▼";
        if (m_LandscapeClose != null) m_LandscapeClose.SetActive(!m_Collapsed);
        if (m_HelpTip != null && m_Collapsed) m_HelpTip.SetActive(false);
        PhoneVisibilityChanged?.Invoke(!m_Collapsed);   // 구독자(모바일 월드 입력 잠금)에 현재 상태 통지
    }

    // 가로 화면용 도움말 말풍선(같은 내용, 큰 글씨 · 터치 기기는 손가락 문구)
    private void BuildLandscapeHelpTip()
    {
        var tip = NewRect("HelpTip", m_Phone.transform, new Vector2(0, 1), new Vector2(0, 1),
            new Vector2(64f, -104f), new Vector2(660f, 240f));   // 왼쪽 칸 안에(구분선 888 안 넘게)
        var bg = tip.AddComponent<Image>();
        bg.sprite = JobsnailUiKit.Sprite("UI_pngs/MyPage/RoundRect");
        bg.type = Image.Type.Sliced;
        bg.color = new Color(1f, 1f, 1f, 0.99f);
        bg.raycastTarget = false;
        var ol = tip.AddComponent<Outline>();
        ol.effectColor = InGameUiSkin.CardGray;
        ol.effectDistance = new Vector2(3f, 3f);

        var title = MakeTextPx(tip.transform, "완공 계획도 보는 법", new Vector2(24f, -18f), new Vector2(612f, 38f), 30, TextAnchor.MiddleLeft);
        title.color = InGameUiSkin.Orange;
        title.fontStyle = FontStyle.Bold;

        var body = MakeTextPx(tip.transform, m_IsMobileDevice
                ? "· 한 손가락 드래그 : 카메라 회전\n· 두 손가락 : 확대 / 축소\n· 블럭을 누르면 그 재료가 바로 선택돼요"
                : "· 좌클릭 드래그 : 위치 이동\n· 우클릭 드래그 : 카메라 회전\n· 마우스 휠 : 확대 / 축소\n· 블럭을 클릭하면 그 재료가 바로 선택돼요",
            new Vector2(24f, -62f), new Vector2(612f, 166f), 25, TextAnchor.UpperLeft);
        body.color = InGameUiSkin.TextGray;
        body.horizontalOverflow = HorizontalWrapMode.Wrap;
        body.verticalOverflow = VerticalWrapMode.Overflow;

        m_HelpTip = tip;
        tip.SetActive(false);
    }

    // ── 도움말 말풍선(정답 뷰 조작법) — '?' 아이콘 호버 시 표시 ─────────
    private void BuildHelpTip()
    {
        var tip = NewRect("HelpTip", m_Phone.transform, new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, Vector2.zero);
        Local((RectTransform)tip.transform, 16f, 116f, 264f, 140f);   // 완성도 배지 아래, 정답 뷰 위에 덮어서
        var bg = tip.AddComponent<Image>();
        bg.color = new Color(1f, 1f, 1f, 0.98f);
        bg.raycastTarget = false;
        var ol = tip.AddComponent<Outline>();
        ol.effectColor = InGameUiSkin.CardGray;
        ol.effectDistance = new Vector2(2f, 2f);

        var title = MakeText(tip.transform, "완공 계획도 보는 법", new Vector2(10f, 6f), new Vector2(244f, 20f), Px(13), TextAnchor.MiddleLeft);
        title.color = InGameUiSkin.Orange;
        title.fontStyle = FontStyle.Bold;

        var body = MakeText(tip.transform,
            "· 좌클릭 드래그 : 위치 이동\n· 우클릭 드래그 : 카메라 회전\n· 마우스 휠 : 확대 / 축소\n· 블럭에 커서를 올리면 이름이 뜨고,\n  클릭하면 그 재료가 바로 선택돼요",
            new Vector2(10f, 30f), new Vector2(244f, 104f), Px(11), TextAnchor.UpperLeft);
        body.color = InGameUiSkin.TextGray;
        body.horizontalOverflow = HorizontalWrapMode.Wrap;
        body.verticalOverflow = VerticalWrapMode.Overflow;

        m_HelpTip = tip;
        tip.SetActive(false);
    }

    // 말풍선은 나중에 만들어진 정답 뷰·카드에 가리므로 켤 때마다 맨 앞으로 올린다.
    private void ShowHelpTip(bool on)
    {
        if (m_HelpTip == null) return;
        if (on) m_HelpTip.transform.SetAsLastSibling();
        m_HelpTip.SetActive(on);
    }

    /// <summary>확대 버튼 / 가로 화면의 닫기 — 세로 폰 ↔ 가로 전체화면 전환(내용은 그대로 복원).</summary>
    public void ToggleExpanded()
    {
        m_ExpandedView = !m_ExpandedView;
        Rebuild();
    }

    // 레이아웃 전환: 자식 전부 버리고 다시 만든 뒤 정답 RT·주문 목록·선택·잔량·완성도를 복원.
    private void Rebuild()
    {
        int selected = m_SelectedId;
        var items = m_CachedItems;
        var onOrder = m_OnOrder;

        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var child = transform.GetChild(i).gameObject;
            child.transform.SetParent(null, false);
            Destroy(child);
        }
        m_Phone = null; m_Surface = null; m_PctText = null; m_CompletionText = null;
        m_SelName = null; m_SelSub = null; m_OrderBtn = null; m_OrderBtnImg = null;
        m_GridRoot = null; m_Tip = null; m_HelpTip = null; m_CollapseTab = null; m_CollapseLabel = null; m_LandscapeClose = null;
        m_BlockBanner = null; m_BlockText = null;
        m_Cards.Clear();
        m_SelectedId = -1;
        ChromeHovered = false;

        Init();
        if (m_Texture != null) SetTexture(m_Texture);
        if (items != null) BuildOrders(items, onOrder);
        if (m_CachedRemaining.Count > 0)
        {
            var snapshot = new List<KeyValuePair<int, int>>(m_CachedRemaining);   // SetRemaining이 캐시를 다시 쓰므로 사본으로 순회
            foreach (var kv in snapshot) SetRemaining(kv.Key, kv.Value);
        }
        if (selected >= 0) Select(selected);
        SetCompletion(m_LastPct);
        ApplyBlockVisual();   // 재구축 중에도 주문 해킹이 걸려 있을 수 있다
        UpdateOrderButton();
    }

    /// <summary>
    /// 모바일 가로 화면(기획서 스타일): 다크 베젤 폰 + 흰 화면.
    /// 왼쪽 완공 계획도(+주황 완성도 배지) / 오른쪽 재료 카탈로그 + [주문!] / 폰 아래 [폰 내리기].
    /// </summary>
    private void InitMobileLayout()
    {
        m_Phone = NewRect("MobilePhone", transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(1800f, 940f));
        // 4:3 태블릿 등 좁은 화면 대응: AspectRatioFitter는 rect 크기를 바꿔 고정 px 자식들이 삐져나가므로
        // 1800x940 저작 크기를 유지한 채 LateUpdate에서 localScale로 화면 안에 맞춘다.
        m_Phone.AddComponent<NoJuicyButtonMotion>();
        var bezel = m_Phone.AddComponent<Image>();
        bezel.sprite = JobsnailUiKit.Sprite("UI_pngs/MyPage/RoundRect");
        bezel.type = Image.Type.Sliced;
        bezel.color = new Color(0.09f, 0.09f, 0.11f, 0.99f);
        bezel.raycastTarget = true;

        var screen = NewRect("Screen", m_Phone.transform, new Vector2(0, 1), new Vector2(0, 1),
            new Vector2(20f, -20f), new Vector2(1760f, 900f));
        var screenImg = screen.AddComponent<Image>();
        screenImg.sprite = JobsnailUiKit.Sprite("UI_pngs/MyPage/RoundRect");
        screenImg.type = Image.Type.Sliced;
        screenImg.color = new Color(0.995f, 0.995f, 0.99f, 1f);
        screenImg.raycastTarget = false;

        var divider = NewRect("Divider", m_Phone.transform, new Vector2(0, 1), new Vector2(0, 1),
            new Vector2(888f, -60f), new Vector2(3f, 820f));
        var dividerImg = divider.AddComponent<Image>();
        dividerImg.color = new Color(0.88f, 0.88f, 0.87f, 1f);
        dividerImg.raycastTarget = false;

        var ink = new Color(0.16f, 0.16f, 0.15f, 1f);

        var planTitle = MakeTextPx(m_Phone.transform, "완공 계획도",
            new Vector2(64f, -48f), new Vector2(360f, 56f), 34, TextAnchor.MiddleLeft);
        planTitle.fontStyle = FontStyle.Bold;
        planTitle.color = ink;

        // 도움말 아이콘(가로 화면엔 배경 그림이 없으니 아이콘 에셋으로 직접) — 호버/터치하면 조작법 말풍선
        var helpGo = NewRect("HelpIcon", m_Phone.transform, new Vector2(0, 1), new Vector2(0, 1),
            new Vector2(306f, -52f), new Vector2(44f, 44f));
        var helpIcon = helpGo.AddComponent<Image>();
        helpIcon.sprite = InGameUiSkin.Load("HelpIcon");
        helpIcon.preserveAspect = true;
        helpIcon.raycastTarget = true;
        helpGo.AddComponent<NoJuicyButtonMotion>();
        BuildLandscapeHelpTip();
        HoverRelay.Attach(helpGo, on => ShowHelpTip(on));
        var helpBtn = helpGo.AddComponent<Button>();   // 터치 기기: 탭으로도 열고 닫기
        helpBtn.targetGraphic = helpIcon;
        helpBtn.transition = Selectable.Transition.None;
        helpBtn.onClick.AddListener(() => ShowHelpTip(m_HelpTip == null || !m_HelpTip.activeSelf));

        var badge = NewRect("CompletionBadge", m_Phone.transform, new Vector2(0, 1), new Vector2(0, 1),
            new Vector2(560f, -54f), new Vector2(296f, 48f));
        var badgeImg = badge.AddComponent<Image>();
        badgeImg.sprite = JobsnailUiKit.Sprite("UI_pngs/MyPage/RoundRect");
        badgeImg.type = Image.Type.Sliced;
        badgeImg.color = new Color(1f, 0.44f, 0.08f, 1f);
        badgeImg.raycastTarget = false;
        m_CompletionText = MakeTextPx(badge.transform, "현재 완성도 :  - %",
            Vector2.zero, new Vector2(296f, 48f), 23, TextAnchor.MiddleCenter);
        m_CompletionText.fontStyle = FontStyle.Bold;

        // 왼쪽 칸(64~856)에 들어가는 정사각 뷰 — RT가 512x512라 가로로 늘리면 모델이 뚱뚱해진다
        m_Surface = MakeRawImageRaw(m_Phone.transform, new Vector2(135f, -120f), new Vector2(650f, 650f));
        m_Surface.color = Color.white;

        m_SelName = MakeTextPx(m_Phone.transform, kIdleName,
            new Vector2(64f, -786f), new Vector2(620f, 36f), 24, TextAnchor.MiddleLeft);
        m_SelName.fontStyle = FontStyle.Bold;
        m_SelName.color = ink;
        m_SelSub = MakeTextPx(m_Phone.transform, "오른쪽 재료를 골라 주문하세요",
            new Vector2(64f, -824f), new Vector2(720f, 30f), 19, TextAnchor.MiddleLeft);
        m_SelSub.color = new Color(0.45f, 0.45f, 0.44f, 1f);

        var catalogTitle = MakeTextPx(m_Phone.transform, "재료 카탈로그",
            new Vector2(920f, -48f), new Vector2(420f, 56f), 34, TextAnchor.MiddleLeft);
        catalogTitle.fontStyle = FontStyle.Bold;
        catalogTitle.color = ink;

        var btnGo = NewRect("OrderBtn", m_Phone.transform, new Vector2(0, 1), new Vector2(0, 1),
            new Vector2(916f, -796f), new Vector2(836f, 76f));
        m_OrderBtnImg = btnGo.AddComponent<Image>();
        m_OrderBtnImg.sprite = JobsnailUiKit.Sprite("UI_pngs/MyPage/RoundRect");
        m_OrderBtnImg.type = Image.Type.Sliced;
        m_OrderBtn = btnGo.AddComponent<Button>();
        m_OrderBtn.targetGraphic = m_OrderBtnImg;
        m_OrderBtn.onClick.AddListener(() => { if (m_SelectedId >= 0) m_OnOrder?.Invoke(m_SelectedId); });
        var orderLabel = MakeTextPx(btnGo.transform, "주문!", Vector2.zero, new Vector2(836f, 76f), 30, TextAnchor.MiddleCenter);
        orderLabel.fontStyle = FontStyle.Bold;
        UpdateOrderButton();
        BuildBlockBanner();

        GameObject closeGo;
        if (m_ExpandedView)
        {
            // PC 확대 보기: 기존 하단 [작게 보기 ▾] 버튼 유지
            closeGo = NewRect("ClosePhone", transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 10f), new Vector2(320f, 62f));
            closeGo.AddComponent<NoJuicyButtonMotion>();   // GameHudDriver의 JuicyButton 스윕에서 제외(모바일 무모션 정책)
            var closeImg = closeGo.AddComponent<Image>();
            closeImg.sprite = JobsnailUiKit.Sprite("UI_pngs/MyPage/RoundRect");
            closeImg.type = Image.Type.Sliced;
            closeImg.color = new Color(0.94f, 0.94f, 0.93f, 0.97f);
            var close = closeGo.AddComponent<Button>();
            close.targetGraphic = closeImg;
            close.onClick.AddListener(ToggleExpanded);   // PC 확대 보기 → 작은 폰으로
            var closeLabel = MakeTextPx(closeGo.transform, "작게 보기 ▾", Vector2.zero, new Vector2(320f, 62f), 24, TextAnchor.MiddleCenter);
            closeLabel.color = ink;
            closeLabel.fontStyle = FontStyle.Bold;
        }
        else
        {
            // 모바일: 하단 [폰 내리기] 버튼 대신 ① 폰 밖 아무데나 터치 = 내리기(투명 전면 오버레이, 폰 뒤에 깔림)
            // ② 폰 우상단 X 버튼. (하단 버튼이 아이폰 홈 제스처 영역과 겹치고, 밖-터치가 더 직관적이라는 피드백)
            closeGo = NewRect("PhoneDismissOverlay", transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            closeGo.AddComponent<NoJuicyButtonMotion>();
            var overlayImg = closeGo.AddComponent<Image>();
            overlayImg.color = new Color(0f, 0f, 0f, 0f);   // 완전 투명 — 터치만 받는다
            var overlayBtn = closeGo.AddComponent<Button>();
            overlayBtn.targetGraphic = overlayImg;
            overlayBtn.transition = Selectable.Transition.None;
            overlayBtn.onClick.AddListener(ToggleCollapsed);
            closeGo.transform.SetSiblingIndex(m_Phone.transform.GetSiblingIndex());   // 폰보다 뒤(아래) — 폰 위 터치는 폰이 먹는다

            // X 버튼 — 폰(베젤)의 자식이라 폰 표시/숨김에 자동 동행
            var xGo = NewRect("CloseX", m_Phone.transform, Vector2.one, Vector2.one,
                new Vector2(-22f, -22f), new Vector2(64f, 64f));
            xGo.AddComponent<NoJuicyButtonMotion>();
            var xImg = xGo.AddComponent<Image>();
            xImg.sprite = JobsnailUiKit.Sprite("UI_pngs/MyPage/RoundRect");
            xImg.type = Image.Type.Sliced;
            xImg.color = new Color(0.94f, 0.94f, 0.93f, 0.97f);
            var xBtn = xGo.AddComponent<Button>();
            xBtn.targetGraphic = xImg;
            xBtn.onClick.AddListener(ToggleCollapsed);
            // ✕ 글리프(U+2715)는 SUITE 폰트에 없어 실기기에서 회색 원만 보였다 —
            // 폰트 의존 없이 바 2개를 ±45° 회전해 X를 그린다.
            for (int i = 0; i < 2; i++)
            {
                var bar = NewRect("XBar", xGo.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    Vector2.zero, new Vector2(32f, 5f));
                var barImg = bar.AddComponent<Image>();
                barImg.color = ink;
                barImg.raycastTarget = false;
                bar.transform.localRotation = Quaternion.Euler(0f, 0f, i == 0 ? 45f : -45f);
            }
        }
        m_Collapsed = m_IsMobileDevice;   // 모바일은 접힌 채 시작(Init과 동일 규칙)
        if (m_Phone != null) m_Phone.SetActive(!m_Collapsed);
        PhoneVisibilityChanged?.Invoke(!m_Collapsed);
        closeGo.SetActive(!m_Collapsed);
        m_LandscapeClose = closeGo;   // 기존 표시/숨김 동기화 그대로 재사용(모바일에선 오버레이가 그 역할)
    }

    // 폰(1800x940 고정 저작 크기)을 화면 크기에 맞춰 축소. 16:9에선 1배(여백 유지), 세로 태블릿 등에선 알아서 줄어든다.
    // 크기 캐시 가드 없이 매 프레임 맞춘다 — 재구축으로 폰이 새로 만들어져도(스케일 1) 다음 프레임에 바로 교정된다.
    private void LateUpdate()
    {
        if (!m_MobileLayout || m_Phone == null) return;
        var avail = ((RectTransform)transform).rect.size;
        // 노치·펀치홀 폰: 세이프영역 비율만큼 가용 크기를 줄여 레이아웃이 노치에 안 가리게(태블릿은 보통 그대로).
        var sa = Screen.safeArea;
        if (Screen.width > 0 && Screen.height > 0)
            avail = new Vector2(avail.x * sa.width / Screen.width, avail.y * sa.height / Screen.height);
        float s = Mathf.Min(1f, (avail.x - 40f) / 1800f, (avail.y - 40f) / 940f);
        if (s > 0f) m_Phone.transform.localScale = new Vector3(s, s, 1f);
    }

    // ── 주문 그리드 (GameHudDriver가 depot 목록으로 호출) ──────────────
    public void BuildOrders(IReadOnlyList<OrderEntry> items, Action<int> onOrder)
    {
        if (m_Phone == null) return;
        m_OnOrder = onOrder;
        m_CachedItems = items;
        if (m_GridRoot != null) Destroy(m_GridRoot);
        m_Cards.Clear();
        m_SelectedId = -1;
        UpdateOrderButton();

        if (m_MobileLayout)
        {
            BuildMobileOrders(items);
            return;
        }

        // 뷰포트 = 그리드 원점 ~ 실제 화면 하단(폰이 아래로 잠겨 있어 그 아래 카드는 안 보임 → 스크롤)
        float viewH = kDisplayBottom - kGridY - 4f;
        m_GridRoot = NewRect("Orders", m_Phone.transform, new Vector2(0, 1), new Vector2(0, 1),
                             new Vector2(kGridX * InGameUiSkin.S, -kGridY * InGameUiSkin.S),
                             new Vector2(kGridW * InGameUiSkin.S, viewH * InGameUiSkin.S));
        m_GridRoot.AddComponent<RectMask2D>();

        int rows = Mathf.CeilToInt(items.Count / (float)kCols);
        float contentH = rows > 0 ? (rows - 1) * kRowStep + kCardH : 0f;

        var content = NewRect("Content", m_GridRoot.transform, new Vector2(0, 1), new Vector2(1, 1),
                              Vector2.zero, new Vector2(0, contentH * InGameUiSkin.S));
        var crt = content.GetComponent<RectTransform>();
        crt.pivot = new Vector2(0.5f, 1f); crt.anchoredPosition = Vector2.zero;

        for (int i = 0; i < items.Count; i++)
        {
            int col = i % kCols, row = i / kCols;
            MakeCard(content.transform, items[i], col * kColStep, row * kRowStep);
        }

        if (contentH > viewH)
        {
            var sr = m_GridRoot.AddComponent<ScrollRect>();
            sr.content = crt;
            sr.viewport = m_GridRoot.GetComponent<RectTransform>();
            sr.horizontal = false; sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.scrollSensitivity = 20f;
        }
    }

    // 기획서: 2열 가로형 카드(썸네일 좌 + 이름 우 + '재고: N개' 배지)
    private void BuildMobileOrders(IReadOnlyList<OrderEntry> items)
    {
        const int cols = 2;
        const float gap = 16f;
        const float gridW = 836f;
        const float gridH = 660f;
        float cardW = (gridW - gap * (cols + 1)) / cols;
        const float cardH = 150f;

        m_GridRoot = NewRect("Orders", m_Phone.transform, new Vector2(0, 1), new Vector2(0, 1),
            new Vector2(916f, -120f), new Vector2(gridW, gridH));
        var gridImage = m_GridRoot.AddComponent<Image>();
        gridImage.color = new Color(0f, 0f, 0f, 0.01f);   // 스크롤 히트 영역(거의 투명)
        m_GridRoot.AddComponent<RectMask2D>();

        int rows = Mathf.CeilToInt(items.Count / (float)cols);
        float contentH = gap + rows * (cardH + gap);
        var content = NewRect("Content", m_GridRoot.transform, new Vector2(0, 1), new Vector2(1, 1),
            Vector2.zero, new Vector2(0f, contentH));
        var contentRt = content.GetComponent<RectTransform>();
        contentRt.pivot = new Vector2(0.5f, 1f);
        contentRt.anchoredPosition = Vector2.zero;

        for (int i = 0; i < items.Count; i++)
        {
            int col = i % cols;
            int row = i / cols;
            MakeMobileCard(content.transform, items[i],
                new Vector2(gap + col * (cardW + gap), -(gap + row * (cardH + gap))),
                cardW, cardH);
        }

        if (contentH > gridH)
        {
            var scroll = m_GridRoot.AddComponent<ScrollRect>();
            scroll.content = contentRt;
            scroll.viewport = m_GridRoot.GetComponent<RectTransform>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 34f;
        }
    }

    // 모바일 가로형 카드: [썸네일 | 이름] + 우하단 '재고: N개' 배지(수량 제한 재료만)
    private void MakeMobileCard(Transform parent, OrderEntry e, Vector2 pos, float w, float h)
    {
        var card = NewRect("Card", parent, new Vector2(0, 1), new Vector2(0, 1), pos, new Vector2(w, h));
        var img = card.AddComponent<Image>();
        img.sprite = JobsnailUiKit.Sprite("UI_pngs/MyPage/RoundRect");
        img.type = Image.Type.Sliced;
        img.color = IdleCardColor;
        var frame = card.AddComponent<Outline>();   // 선택 테두리(게임 집기 초록과 동일 색)
        frame.effectColor = kSelGreen; frame.effectDistance = new Vector2(3f, 3f); frame.enabled = false;
        var btn = card.AddComponent<Button>(); btn.targetGraphic = img;
        int id = e.Id;
        btn.onClick.AddListener(() => SelectOrOrder(id));   // 탭 = 선택 · 더블탭 = 즉시 주문

        var th = NewRect("Thumb", card.transform, new Vector2(0, 1), new Vector2(0, 1),
            new Vector2(14f, -14f), new Vector2(122f, 122f));
        var ri = th.AddComponent<RawImage>();
        var tex = e.Prefab != null ? BlockThumbnail.Get(e.Prefab, 256) : null;
        if (tex != null) ri.texture = tex;
        else ri.color = new Color(0f, 0f, 0f, 0.06f);
        ri.raycastTarget = false;

        var nm = MakeTextPx(card.transform, e.Name, new Vector2(150f, -30f), new Vector2(w - 164f, 70f), 22, TextAnchor.MiddleLeft);
        nm.fontStyle = FontStyle.Bold;
        nm.color = new Color(0.16f, 0.16f, 0.15f, 1f);
        nm.horizontalOverflow = HorizontalWrapMode.Wrap;
        nm.verticalOverflow = VerticalWrapMode.Truncate;

        Text badge = null;
        if (e.Limit >= 0)
        {
            var bg = NewRect("BadgeBg", card.transform, new Vector2(1, 0), new Vector2(1, 0),
                new Vector2(-10f, 10f), new Vector2(152f, 38f));
            var bimg = bg.AddComponent<Image>();
            bimg.sprite = JobsnailUiKit.Sprite("UI_pngs/MyPage/RoundRect");
            bimg.type = Image.Type.Sliced;
            bimg.color = new Color(0.30f, 0.30f, 0.29f, 0.95f);
            bimg.raycastTarget = false;
            badge = MakeTextPx(bg.transform, $"재고: {e.Limit}개", Vector2.zero, new Vector2(152f, 38f), 19, TextAnchor.MiddleCenter);
            badge.fontStyle = FontStyle.Bold;
        }

        m_Cards[id] = new Card { Bg = img, Frame = frame, Thumb = ri, Badge = badge,
                                Name = e.Name, Sub = e.Sub, Remaining = e.Limit };
    }

    private void MakeCard(Transform parent, OrderEntry e, float x, float y)
    {
        var img = InGameUiSkin.SpriteImage("Card", parent, "OrderCard", raycast: true);
        Local(img.rectTransform, x, y, kCardW, kCardH);
        img.color = kCardIdle;
        var frame = img.gameObject.AddComponent<Outline>();   // 선택 테두리(주황)
        frame.effectColor = InGameUiSkin.Orange; frame.effectDistance = new Vector2(2f, 2f); frame.enabled = false;
        var btn = img.gameObject.AddComponent<Button>(); btn.targetGraphic = img;
        btn.transition = Selectable.Transition.None;
        int id = e.Id;
        btn.onClick.AddListener(() => SelectOrOrder(id));   // 카드 클릭 = 선택 · 더블클릭 = 즉시 주문
        JuicyButton.Attach(btn);

        var th = NewRect("Thumb", img.transform, new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, Vector2.zero);
        Local((RectTransform)th.transform, kThumbX, kThumbY, kThumbW, kThumbH);
        var ri = th.AddComponent<RawImage>();
        var tex = e.Prefab != null ? BlockThumbnail.Get(e.Prefab, 256) : null;
        if (tex != null) ri.texture = tex;
        else ri.color = new Color(1f, 1f, 1f, 0.15f);
        ri.raycastTarget = false;

        var nm = MakeText(img.transform, e.Name, new Vector2(kNameX, kNameY), new Vector2(kNameW, kNameH), Px(10), TextAnchor.MiddleCenter);
        nm.color = InGameUiSkin.TextGray;
        nm.horizontalOverflow = HorizontalWrapMode.Wrap; nm.verticalOverflow = VerticalWrapMode.Truncate;

        Text badge = null; GameObject badgeBg = null;
        if (e.Limit >= 0)
        {
            badgeBg = NewRect("BadgeBg", img.transform, new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, Vector2.zero);
            Local((RectTransform)badgeBg.transform, kBadgeX, kBadgeY, kBadgeW, kBadgeH);
            var bimg = badgeBg.AddComponent<Image>(); bimg.color = InGameUiSkin.Orange; bimg.raycastTarget = false;
            badge = MakeText(badgeBg.transform, $"재고: {e.Limit}개", Vector2.zero, new Vector2(kBadgeW, kBadgeH), Px(8), TextAnchor.MiddleCenter);
        }

        m_Cards[id] = new Card { Bg = img, Frame = frame, Thumb = ri, Badge = badge, BadgeBg = badgeBg,
                                Name = e.Name, Sub = e.Sub, Remaining = e.Limit };
    }

    // ── 선택 (카드 클릭 / 3D 뷰 클릭 → 드라이버 경유) ─────────────────
    /// <summary>
    /// 클릭 = 선택, 같은 재료를 kDoubleClickSec 안에 다시 클릭 = [주문!]을 거치지 않고 즉시 주문.
    /// 카드(PC·모바일)와 정답 3D 뷰 클릭이 모두 이 경로를 타므로 어디서 더블클릭하든 동작이 같다.
    /// </summary>
    public void SelectOrOrder(int id)
    {
        if (!m_Cards.TryGetValue(id, out var c)) return;
        float now = Time.unscaledTime;
        bool again = id == m_LastClickId && now - m_LastClickTime <= kDoubleClickSec;
        m_LastClickId = again ? -1 : id;   // 3연타가 곧바로 두 번째 주문이 되지 않게 초기화
        m_LastClickTime = now;
        Select(id);
        if (again && c.Remaining != 0 && m_BlockSecs == 0) m_OnOrder?.Invoke(id);   // 품절·주문 차단이면 선택만
    }

    public void Select(int id)
    {
        if (!m_Cards.TryGetValue(id, out var c)) return;
        if (m_SelectedId != id)
        {
            DeselectCardVisual();
            m_SelectedId = id;
            c.Frame.enabled = true;
            c.Bg.color = PickedCardColor;
            if (m_MobileLayout) SetSelBar(c.Name + RemainSuffix(c), c.Sub);
            UpdateOrderButton();
        }
        SelectionChanged?.Invoke(id);
    }

    public void ClearSelection()
    {
        m_LastClickId = -1;   // 해제 뒤 첫 클릭이 더블클릭으로 잡히지 않게
        if (m_SelectedId < 0) return;
        DeselectCardVisual();
        m_SelectedId = -1;
        if (m_MobileLayout) SetSelBar(null, null);
        UpdateOrderButton();
        SelectionChanged?.Invoke(-1);
    }

    private void DeselectCardVisual()
    {
        if (m_SelectedId >= 0 && m_Cards.TryGetValue(m_SelectedId, out var old))
        {
            old.Frame.enabled = false;
            old.Bg.color = old.Remaining == 0 ? kSoldOut : IdleCardColor;
        }
    }

    /// <summary>수량 제한 재료의 잔량 반영. 품절이어도 선택은 되게 두고 [주문!] 버튼만 잠근다.</summary>
    public void SetRemaining(int id, int remaining)
    {
        if (remaining < 0) return;
        m_CachedRemaining[id] = remaining;
        if (!m_Cards.TryGetValue(id, out var c)) return;
        c.Remaining = remaining;
        m_Cards[id] = c;
        bool sold = remaining == 0;
        if (c.Badge != null)
        {
            c.Badge.text = sold ? "품절" : $"재고: {remaining}개";
            c.Badge.color = sold ? new Color(1f, 0.55f, 0.45f) : Color.white;
        }
        if (c.Thumb != null && c.Thumb.texture != null)
            c.Thumb.color = sold ? new Color(0.4f, 0.4f, 0.4f, 1f) : Color.white;
        if (m_SelectedId != id) c.Bg.color = sold ? kSoldOut : IdleCardColor;
        if (m_SelectedId == id)
        {
            if (m_MobileLayout) SetSelBar(c.Name + RemainSuffix(c), c.Sub);
            UpdateOrderButton();
        }
    }

    private static string RemainSuffix(Card c) =>
        c.Remaining < 0 ? "" : c.Remaining == 0 ? "  <color=#FF8C73>품절</color>" : $"  ({c.Remaining}개 남음)";

    private void SetSelBar(string name, string sub)
    {
        bool has = !string.IsNullOrEmpty(name);
        if (m_SelName != null) m_SelName.text = has ? name : kIdleName;
        // 모바일엔 우클릭·휠 안내 대신 터치 문구
        if (m_SelSub != null)  m_SelSub.text  = has ? sub : m_MobileLayout ? "오른쪽 재료를 골라 주문하세요" : kIdleSub;
    }

    private void UpdateOrderButton()
    {
        bool can = m_BlockSecs == 0 && m_SelectedId >= 0 && m_Cards.TryGetValue(m_SelectedId, out var c) && c.Remaining != 0;
        if (m_OrderBtn != null) m_OrderBtn.interactable = can;
        if (m_OrderBtnImg != null)
            m_OrderBtnImg.color = m_MobileLayout
                ? can ? new Color(1f, 0.44f, 0.08f, 1f) : new Color(0.64f, 0.62f, 0.58f, 0.78f)
                : can ? Color.white : new Color(1f, 1f, 1f, 0.35f);
    }

    private Color IdleCardColor => m_MobileLayout
        ? new Color(0.92f, 0.92f, 0.91f, 1f)
        : kCardIdle;

    private Color PickedCardColor => m_MobileLayout
        ? new Color(0.85f, 0.93f, 0.84f, 1f)
        : kCardPicked;

    // ── 주문 차단(상대의 '주문 해킹') 안내 배너 ──────────────────────
    // 서버가 차단된 주문을 조용히 버리면 "왜인지 모르겠는데 주문이 안 됨"이 된다(QA).
    // 카드 그리드 맨 위에 이유 + 남은 초를 띄우고 [주문!]도 함께 잠근다.
    private GameObject m_BlockBanner;
    private Text m_BlockText;
    private Image m_BlockIcon;
    private int m_BlockSecs;   // 0 = 주문 가능
    private static readonly Color kBlockPurple = new Color(0.62f, 0.20f, 0.80f, 0.96f);

    /// <summary>주문 차단 배너 아이콘(주문해킹) — 드라이버가 주입(이 클래스는 GridSystem을 모른다).</summary>
    public Sprite OrderBlockIcon;

    /// <summary>주문 차단 남은 초(0 = 정상). GameHudDriver가 매 프레임 넘긴다.</summary>
    public void SetOrderBlocked(float remainingSeconds)
    {
        int secs = remainingSeconds > 0f ? Mathf.CeilToInt(remainingSeconds) : 0;
        if (secs == m_BlockSecs) return;   // 매 프레임 호출 — 초가 넘어갈 때만 갱신
        m_BlockSecs = secs;
        ApplyBlockVisual();
        UpdateOrderButton();
    }

    private void BuildBlockBanner()
    {
        if (m_Phone == null) return;
        GameObject go;
        if (m_MobileLayout)
        {
            var size = new Vector2(836f, 64f);
            go = NewRect("OrderBlockBanner", m_Phone.transform, new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(916f, -120f), size);
            m_BlockText = MakeTextPx(go.transform, "", Vector2.zero, size, 26, TextAnchor.MiddleCenter);
        }
        else
        {
            go = NewRect("OrderBlockBanner", m_Phone.transform, new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, Vector2.zero);
            Local((RectTransform)go.transform, kGridX, kGridY, kGridW, 22f);
            m_BlockText = MakeText(go.transform, "", Vector2.zero, new Vector2(kGridW, 22f), Px(10), TextAnchor.MiddleCenter);
        }
        var bg = go.AddComponent<Image>();
        bg.sprite = JobsnailUiKit.Sprite("UI_pngs/MyPage/RoundRect");
        bg.type = Image.Type.Sliced;
        bg.color = kBlockPurple;
        bg.raycastTarget = false;   // 배너 아래 카드는 계속 고를 수 있게(주문만 잠긴다)
        m_BlockText.fontStyle = FontStyle.Bold;
        m_BlockText.transform.SetAsLastSibling();

        // 왼쪽 끝 주문해킹 아이콘 — 문구를 읽기 전에 '무슨 아이템인지' 보이게
        float iconSize = m_MobileLayout ? 48f : 18f;
        var iconGo = new GameObject("Icon", typeof(Image));
        var iconRt = (RectTransform)iconGo.transform;
        iconRt.SetParent(go.transform, false);
        iconRt.anchorMin = iconRt.anchorMax = new Vector2(0f, 0.5f);
        iconRt.anchoredPosition = new Vector2(iconSize * 0.65f + 4f, 0f);
        iconRt.sizeDelta = new Vector2(iconSize, iconSize);
        m_BlockIcon = iconGo.GetComponent<Image>();
        m_BlockIcon.preserveAspect = true;
        m_BlockIcon.raycastTarget = false;

        m_BlockBanner = go;
        ApplyBlockVisual();
    }

    private void ApplyBlockVisual()
    {
        if (m_BlockBanner == null) return;
        bool on = m_BlockSecs > 0;
        if (on)
        {
            if (m_BlockText != null) m_BlockText.text = $"주문 해킹! {m_BlockSecs}초 뒤 주문 가능";
            m_BlockBanner.transform.SetAsLastSibling();   // 나중에 지어진 카드 그리드보다 위로
        }
        if (m_BlockIcon != null)
        {
            if (m_BlockIcon.sprite == null && OrderBlockIcon != null) m_BlockIcon.sprite = OrderBlockIcon;
            m_BlockIcon.enabled = m_BlockIcon.sprite != null;
        }
        if (m_BlockBanner.activeSelf != on) m_BlockBanner.SetActive(on);
    }

    // ── 커서 옆 말풍선 툴팁(호버 전용 — 선택과 별개) ─────────────────
    private const float kTipW = 168f, kTipH = 188f;
    private GameObject m_Tip;
    private RectTransform m_TipRt;
    private RawImage m_TipThumb;
    private Text m_TipName, m_TipSub;

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

    private void BuildTip()
    {
        m_Tip = new GameObject("Tooltip", typeof(RectTransform)) { layer = 5 };
        m_TipRt = m_Tip.GetComponent<RectTransform>();
        m_TipRt.SetParent(transform, false);
        m_TipRt.anchorMin = m_TipRt.anchorMax = new Vector2(0.5f, 0.5f);
        m_TipRt.pivot = Vector2.zero;   // 좌하단 피벗 — 커서 우상단으로 펼쳐짐
        m_TipRt.sizeDelta = new Vector2(kTipW, kTipH);
        var bg = m_Tip.AddComponent<Image>();
        bg.color = new Color(1f, 1f, 1f, 0.96f);
        bg.raycastTarget = false;   // 커서 아래에 있어도 다른 UI 클릭 안 막음
        var ol = m_Tip.AddComponent<Outline>(); ol.effectColor = InGameUiSkin.CardGray; ol.effectDistance = new Vector2(2f, 2f);

        var th = NewRect("Thumb", m_Tip.transform, new Vector2(0, 1), new Vector2(0, 1),
                         new Vector2((kTipW - 128f) * 0.5f, -8f), new Vector2(128f, 128f));
        m_TipThumb = th.AddComponent<RawImage>();
        m_TipThumb.raycastTarget = false;

        m_TipName = MakeTextPx(m_Tip.transform, "", new Vector2(6, -140f), new Vector2(kTipW - 12, 24), 17, TextAnchor.MiddleCenter);
        m_TipName.fontStyle = FontStyle.Bold; m_TipName.color = InGameUiSkin.TextGray;
        m_TipSub = MakeTextPx(m_Tip.transform, "", new Vector2(6, -162f), new Vector2(kTipW - 12, 20), 13, TextAnchor.MiddleCenter);
        m_TipSub.color = InGameUiSkin.TextGray;

        m_Tip.SetActive(false);
    }

    // ── 빌더 헬퍼 ──
    private static int Px(float figmaPx) => Mathf.RoundToInt(figmaPx * InGameUiSkin.S);

    /// <summary>부모 좌상단 기준 피그마 px 배치(스케일 S).</summary>
    private static void Local(RectTransform rt, float x, float y, float w, float h)
        => InGameUiSkin.TopLeft(rt, x, y, w, h);

    private static GameObject NewRect(string name, Transform parent, Vector2 aMin, Vector2 aMax, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform)) { layer = 5 };
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = aMin; rt.anchorMax = aMax; rt.pivot = aMax;
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        return go;
    }

    /// <summary>가로(1800x940) 레이아웃용 — 저작 px 그대로. 피그마 배율을 타면 폰 밖으로 나간다.</summary>
    private static RawImage MakeRawImageRaw(Transform parent, Vector2 pos, Vector2 size)
    {
        var go = NewRect("Surface", parent, new Vector2(0, 1), new Vector2(0, 1), pos, size);
        var ri = go.AddComponent<RawImage>();
        ri.raycastTarget = false;
        return ri;
    }

    private RawImage MakeRawImage(Transform parent, Vector2 figmaPos, Vector2 figmaSize)
    {
        var go = NewRect("Surface", parent, new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, Vector2.zero);
        Local((RectTransform)go.transform, figmaPos.x, figmaPos.y, figmaSize.x, figmaSize.y);
        var ri = go.AddComponent<RawImage>();
        ri.raycastTarget = false;   // 다른 UI 클릭 안 막음(라우팅은 좌표로 판정)
        return ri;
    }

    /// <summary>피그마 px 좌표/크기 텍스트.</summary>
    private static Text MakeText(Transform parent, string s, Vector2 figmaPos, Vector2 figmaSize, int fontSize, TextAnchor anchor)
    {
        var t = MakeTextPx(parent, s, Vector2.zero, Vector2.zero, fontSize, anchor);
        Local(t.rectTransform, figmaPos.x, figmaPos.y, figmaSize.x, figmaSize.y);
        return t;
    }

    /// <summary>캔버스 px 좌표/크기 텍스트(툴팁용).</summary>
    private static Text MakeTextPx(Transform parent, string s, Vector2 pos, Vector2 size, int fontSize, TextAnchor anchor)
    {
        var go = NewRect("Text", parent, new Vector2(0, 1), new Vector2(0, 1), pos, size);
        var t = go.AddComponent<Text>();
        t.font = s_Font; t.fontSize = fontSize; t.color = Color.white; t.text = s;
        t.alignment = anchor; t.horizontalOverflow = HorizontalWrapMode.Overflow; t.raycastTarget = false;
        return t;
    }

    /// <summary>포인터 진입/이탈을 콜백으로 넘기는 초소형 릴레이(EventTrigger 대체).</summary>
    private sealed class HoverRelay : MonoBehaviour,
        UnityEngine.EventSystems.IPointerEnterHandler, UnityEngine.EventSystems.IPointerExitHandler
    {
        private Action<bool> m_OnHover;

        public static void Attach(GameObject go, Action<bool> onHover)
        {
            var relay = go.GetComponent<HoverRelay>() ?? go.AddComponent<HoverRelay>();
            relay.m_OnHover = onHover;
        }

        public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData _) => m_OnHover?.Invoke(true);
        public void OnPointerExit(UnityEngine.EventSystems.PointerEventData _) => m_OnHover?.Invoke(false);
        private void OnDisable() => m_OnHover?.Invoke(false);
    }
}
