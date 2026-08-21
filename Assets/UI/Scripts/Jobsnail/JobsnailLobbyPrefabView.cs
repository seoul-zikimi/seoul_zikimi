using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>세션 목록 카드 1장에 필요한 표시용 데이터. 컨트롤러가 채워서 View에 넘긴다.</summary>
public struct JobsnailSessionCardData
{
    public string Name;
    public bool HasPassword;
    public int Joined;
    public int MaxPlayers;
    public string MapName;
    public string ModeLabel;
}

/// <summary>대기방 슬롯 1칸 표시용 데이터(입장 순서 고정 배치). 컨트롤러가 네트워크 로스터를 해석해 넘긴다.</summary>
public struct JobsnailLobbySlot
{
    public bool Occupied;     // 비어 있으면 회색 처리
    public string Name;       // 닉네임
    public bool IsHost;       // 방장 → 왕관 + 상태 '방장'
    public bool Ready;        // 팀원 준비 완료 여부
    public string CharacterId; // 착용 캐릭터 ID(모델 렌더)
    public string OutfitId;    // 착용 아웃핏 ID(기본 캐릭터 커스터마이징)
    public int Team;          // 0=파랑(기본), 1=빨강
}

/// <summary>대기방 화면 한 프레임 분량의 표시 상태. 네트워크 값 해석은 컨트롤러가 끝낸 뒤 넘긴다.</summary>
public struct JobsnailLobbyRoomState
{
    public JobsnailLobbySlot[] Slots;   // 슬롯 0~3 로스터(null이면 레거시 표시로 폴백)
    public string RoomName;
    public bool RoomIsFull;
    public bool ShowStartButton;
    public bool StartInteractable;
    public bool ShowReadyButton;
    public bool ReadyInteractable;
    public bool IsLocallyReady;
    public bool AllReady;
    public string StartHint;
    public string ReadyStatus;
    public int JoinedCount;
    public int ReadyCount;
    public int MaxPlayers;
    public bool ShowModeButton;
    public string ModeLabel;
    public string MapName;
    public Sprite MapThumbnail;
    public bool ShowMapArrows;
    public bool WeatherOn;         // 날씨 ON/OFF 현재 상태
    public bool ShowWeatherToggle; // 방장만 토글 가능
    public string RecordText;      // 타임어택=개인 최고기록 / 2vs2=승패 / 자유=없음
    public bool ShowTeamSelect;    // 2vs2 모드에서만 팀 선택 UI 표시
}

/// <summary>Jobsnail 로비 UI 프리팹(세션 목록 / 방 생성 / 대기방)에 붙는 View.
/// 프리팹 안의 위젯 참조를 들고 표시 갱신과 버튼 이벤트 전달만 담당한다.
/// UGS 세션·Netcode 로직은 전부 <see cref="JobsnailLobbySkinner"/>(컨트롤러)에 있다.</summary>
public sealed class JobsnailLobbyPrefabView : MonoBehaviour
{
    public enum OverlayKind
    {
        SessionList,
        CreateRoom,
        LobbyRoom
    }

    [Header("Kind")]
    [SerializeField] private OverlayKind m_Kind;

    [Header("Session List")]
    [SerializeField] private RectTransform m_PcRoot;
    [SerializeField] private ScrollRect m_SessionListScroll;
    [SerializeField] private RectTransform m_CustomSessionListRoot;
    [SerializeField] private Text m_SessionStatus;
    [SerializeField] private JobsnailSessionCardView m_SessionCardTemplate;

    [Header("Session List / Join Password")]
    [SerializeField] private GameObject m_JoinPasswordOverlay;
    [SerializeField] private InputField m_JoinPasswordInput;

    [Header("Create Room")]
    [SerializeField] private InputField m_RoomNameInput;
    [SerializeField] private InputField m_PasswordInput;
    [SerializeField] private Text m_CreateStatus;
    [SerializeField] private Text m_MapSelectLabel;
    [SerializeField] private RectTransform m_MapOptions;
    [SerializeField] private Text m_ModeSelectLabel;
    [SerializeField] private RectTransform m_ModeOptions;
    [SerializeField] private Text m_WeatherToggleLabel;
    [SerializeField] private Image m_WeatherToggleImage;
    [SerializeField] private Image m_PrivateRoomButtonImage;
    [SerializeField] private Image m_PublicRoomButtonImage;
    [SerializeField] private GameObject m_PasswordLabel;
    [SerializeField] private GameObject m_PasswordHint;

    [Header("Lobby Room")]
    [SerializeField] private Text m_LobbySubtitle;
    [SerializeField] private Text m_LobbyStatusBadgeText;
    [SerializeField] private Image m_LobbyStatusBadgeImage;
    [SerializeField] private Button m_LobbyStartButton;
    [SerializeField] private Button m_LobbyReadyButton;
    [SerializeField] private Button m_LobbyModeButton;
    [SerializeField] private Text m_LobbyStartHint;
    [SerializeField] private Text m_LobbyReadyStatus;
    [SerializeField] private GameObject[] m_LobbySlotRoots;
    [SerializeField] private Text[] m_LobbySlotNames;
    [SerializeField] private Text[] m_LobbySlotStatuses;

    [Header("Lobby Room / Map Select")]
    [SerializeField] private Image m_MapThumbnail;
    [SerializeField] private Text m_MapNameText;
    [SerializeField] private Button m_MapPrevButton;
    [SerializeField] private Button m_MapNextButton;

    private static readonly Color kAccent = new(1f, 0.78f, 0.44f, 1f);
    private static readonly Color kDisabled = new(0.78f, 0.78f, 0.78f, 1f);
    private static readonly Color kReady = new(0.45f, 0.84f, 0.38f, 1f);
    private static readonly Color kNotReady = new(1f, 0.42f, 0.42f, 1f);
    private static readonly Color kNeutral = new(0.83f, 0.83f, 0.83f, 1f);
    private static readonly Color kPrivate = new(1f, 0.55f, 0.55f, 1f);
    private static readonly Color kPublic = new(0.58f, 1f, 0.54f, 1f);

    private readonly List<JobsnailSessionCardView> m_SpawnedCards = new();
    private JobsnailLobbySkinner m_Owner;
    private int m_PrevJoinedForPop;
    private bool m_SessionListLayoutReady;
    private JobsnailLobbyCharacterStage m_CharacterStage;   // 슬롯 3D 캐릭터 렌더 스테이지(오프스크린 인프라)
    private Button m_LobbyWeatherButton;   // 프리팹의 날씨 토글 버튼(이름으로 찾음)
    private Text m_LobbyWeatherLabel;
    private Text m_LobbyRecordText;        // 프리팹의 기록 텍스트(이름으로 찾음)
    private bool m_LobbyExtrasBound;
    private GameObject m_TeamSelectRoot;   // 프리팹의 "TeamSelect" 컨테이너(2vs2에서만 표시)
    private bool m_TeamSelectBound;
    private bool m_SlotsResolved;          // 슬롯 루트/이름/상태를 이름으로 재바인딩했는지

    private static readonly Color kTeamBlue = new(0.28f, 0.5f, 1f, 1f);
    private static readonly Color kTeamRed = new(1f, 0.35f, 0.35f, 1f);

    // 세션 목록 2열 그리드 배치 값(프리팹 상태와 무관하게 런타임에서 강제).
    private static readonly Vector2 kSessionCellSize = new(388f, 120f);
    private static readonly Vector2 kSessionCellSpacing = new(18f, 14f);

    public OverlayKind Kind => m_Kind;
    public RectTransform PcRoot => m_PcRoot;
    public string RoomNameText => m_RoomNameInput != null ? m_RoomNameInput.text.Trim() : string.Empty;
    public string PasswordText => m_PasswordInput != null ? m_PasswordInput.text.Trim() : string.Empty;
    public string JoinPasswordText => m_JoinPasswordInput != null ? m_JoinPasswordInput.text : string.Empty;

    // ────────────────────────── 수명 주기 ──────────────────────────

    public void Bind(JobsnailLobbySkinner owner)
    {
        m_Owner = owner;
        JobsnailUiKit.ApplyFontPolicy(transform);

        if (m_SessionCardTemplate != null)
            m_SessionCardTemplate.gameObject.SetActive(false);
        if (m_JoinPasswordOverlay != null)
            m_JoinPasswordOverlay.SetActive(false);
        if (m_MapOptions != null)
            m_MapOptions.gameObject.SetActive(false);
        if (m_ModeOptions != null)
            m_ModeOptions.gameObject.SetActive(false);

        JuicyButton.AttachAll(gameObject);   // 프리팹에서 만든 버튼도 쫀득(중복 부착 안전)
        ResolveSlots();                       // Missing Prefab 껍데기 정리는 첫 레이캐스트 전에 끝내야 한다
    }

    public void SetVisible(bool visible)
    {
        gameObject.SetActive(visible);
        if (visible)
            transform.SetAsLastSibling();

        if (m_CharacterStage != null)
            m_CharacterStage.SetActiveRendering(visible && m_Kind == OverlayKind.LobbyRoom);
    }

    private void OnDestroy()
    {
        if (m_CharacterStage != null)
        {
            Destroy(m_CharacterStage.gameObject);
            m_CharacterStage = null;
        }
    }

    public bool IsVisible => gameObject.activeSelf;

    private JobsnailLobbySkinner Owner => m_Owner != null ? m_Owner : JobsnailLobbySkinner.ActiveInstance;

    // ────────────────────────── 세션 목록 ──────────────────────────

    public void SetSessionStatus(string message)
    {
        SetText(m_SessionStatus, message);
    }

    public void ClearSessionCards()
    {
        foreach (var card in m_SpawnedCards)
        {
            if (card != null)
                Destroy(card.gameObject);
        }
        m_SpawnedCards.Clear();
    }

    /// <summary>세션 목록 루트(CustomSessionListRoot)가 항상 "2열 그리드 + 세로 스크롤" 이 되도록 런타임에서 보장한다.
    /// 프리팹이 구버전(평면 Rect)이든 신버전(스크롤 포함)이든 동일하게 동작하도록 멱등하게 처리한다.</summary>
    private void EnsureSessionListLayout()
    {
        if (m_SessionListLayoutReady || m_CustomSessionListRoot == null)
            return;

        m_SessionListLayoutReady = true;

        var content = m_CustomSessionListRoot;

        // 1) 스크롤 구조가 없으면(구버전 프리팹) content를 Viewport 안으로 옮기고 ScrollRect로 감싼다.
        if (content.GetComponentInParent<ScrollRect>() == null)
        {
            var parent = content.parent;

            // content의 현재 사각형을 스크롤뷰가 그대로 이어받는다.
            var scrollGo = new GameObject("SessionScrollView", typeof(RectTransform), typeof(ScrollRect));
            var scrollRt = (RectTransform)scrollGo.transform;
            scrollRt.SetParent(parent, false);
            scrollRt.anchorMin = content.anchorMin;
            scrollRt.anchorMax = content.anchorMax;
            scrollRt.pivot = content.pivot;
            scrollRt.anchoredPosition = content.anchoredPosition;
            scrollRt.sizeDelta = content.sizeDelta;
            scrollRt.SetSiblingIndex(content.GetSiblingIndex());

            var viewportGo = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D), typeof(Image));
            var viewportRt = (RectTransform)viewportGo.transform;
            viewportRt.SetParent(scrollRt, false);
            viewportRt.anchorMin = Vector2.zero;
            viewportRt.anchorMax = Vector2.one;
            viewportRt.offsetMin = Vector2.zero;
            viewportRt.offsetMax = Vector2.zero;
            var viewportImage = viewportGo.GetComponent<Image>();
            viewportImage.color = new Color(1f, 1f, 1f, 0.01f);
            viewportImage.raycastTarget = true;

            // content를 Viewport 자식으로 이동 + 위쪽 고정 스트레치.
            content.SetParent(viewportRt, false);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0f, 0f);

            var scroll = scrollGo.GetComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;
            scroll.viewport = viewportRt;
            scroll.content = content;
        }

        // 2) content에 2열 GridLayoutGroup + 세로 ContentSizeFitter 보장.
        var grid = content.GetComponent<GridLayoutGroup>();
        if (grid == null)
            grid = content.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = kSessionCellSize;
        grid.spacing = kSessionCellSpacing;
        grid.padding = new RectOffset(8, 8, 10, 10);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 2;
        grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
        grid.startAxis = GridLayoutGroup.Axis.Horizontal;
        // 왼쪽 위 → 오른쪽 위 → 다음 줄 순서로 채우려면 UpperLeft(가운데 정렬 금지).
        grid.childAlignment = TextAnchor.UpperLeft;

        var fitter = content.GetComponent<ContentSizeFitter>();
        if (fitter == null)
            fitter = content.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
    }

    public void SetSessionCards(IReadOnlyList<JobsnailSessionCardData> cards, Action<int> onSelect)
    {
        ClearSessionCards();

        EnsureSessionListLayout();

        if (m_SessionCardTemplate == null || m_CustomSessionListRoot == null)
        {
            Debug.LogWarning("[JobsnailLobbyPrefabView] 세션 카드 템플릿이 프리팹에 바인딩되어 있지 않습니다. (Jobsnail/UI/Bind Runtime UI Prefabs 실행 필요)");
            return;
        }

        if (cards == null)
            return;

        // 배치(2열, 왼→오, 위→아래)와 6개 초과 시 스크롤은 프리팹의
        // GridLayoutGroup + ScrollRect가 담당한다. 여기서는 순서대로 붙이기만 한다.
        for (int i = 0; i < cards.Count; i++)
        {
            var card = Instantiate(m_SessionCardTemplate, m_CustomSessionListRoot, false);
            card.name = $"SessionCard{i}";
            card.gameObject.SetActive(true);
            card.transform.SetAsLastSibling();

            card.Apply(cards[i], i, onSelect);
            m_SpawnedCards.Add(card);
        }

        // 목록을 새로 채우면 스크롤은 맨 위(첫 방)로 되돌린다.
        if (m_SessionListScroll != null)
            m_SessionListScroll.verticalNormalizedPosition = 1f;
    }

    public void ShowJoinPasswordPrompt()
    {
        if (m_JoinPasswordInput != null)
            m_JoinPasswordInput.text = string.Empty;

        if (m_JoinPasswordOverlay == null)
        {
            Debug.LogWarning("[JobsnailLobbyPrefabView] 비밀번호 입력 오버레이가 프리팹에 없습니다. (Jobsnail/UI/Bind Runtime UI Prefabs 실행 필요)");
            return;
        }

        m_JoinPasswordOverlay.SetActive(true);
        m_JoinPasswordOverlay.transform.SetAsLastSibling();
    }

    public void HideJoinPasswordPrompt()
    {
        if (m_JoinPasswordOverlay != null)
            m_JoinPasswordOverlay.SetActive(false);
    }

    // ────────────────────────── 방 생성 ──────────────────────────

    public void InitCreateForm(string defaultRoomName, string defaultPassword, bool isPrivate)
    {
        if (m_RoomNameInput != null)
        {
            m_RoomNameInput.characterLimit = 15;   // 방 이름: 최대 15자, 모든 문자 허용
            m_RoomNameInput.text = defaultRoomName;
        }

        if (m_PasswordInput != null)
        {
            m_PasswordInput.characterLimit = 8;    // 비밀번호: 최대 8자
            m_PasswordInput.contentType = InputField.ContentType.Standard;
            // 공백만 제외하고 모든 문자 허용 → 입력 단계에서 공백 문자를 걸러낸다.
            m_PasswordInput.onValidateInput = (text, index, ch) => char.IsWhiteSpace(ch) ? '\0' : ch;
            m_PasswordInput.text = defaultPassword;
        }

        SetMapOptionsOpen(false);
        SetModeOptionsOpen(false);
        ApplyRoomType(isPrivate);
        SetCreateStatus(string.Empty);
    }

    public void SetCreateStatus(string message)
    {
        SetText(m_CreateStatus, message);
    }

    // ── 맵 / 모드 선택(‘사람 수’와 같은 버튼+드롭다운 방식, 옵션은 런타임 생성) ──

    public void SetMapLabel(string mapName)
    {
        SetText(m_MapSelectLabel, $"{(string.IsNullOrWhiteSpace(mapName) ? "맵 선택" : mapName)} ▼");
    }

    public void SetModeLabel(string modeLabel)
    {
        SetText(m_ModeSelectLabel, $"{(string.IsNullOrWhiteSpace(modeLabel) ? "모드 선택" : modeLabel)} ▼");
    }

    public void BuildMapOptions(IReadOnlyList<string> labels, Action<int> onSelect)
    {
        BuildOptionButtons(m_MapOptions, labels, onSelect);
    }

    public void BuildModeOptions(IReadOnlyList<string> labels, Action<int> onSelect)
    {
        BuildOptionButtons(m_ModeOptions, labels, onSelect);
    }

    public void SetMapOptionsOpen(bool open)
    {
        SetOptionsOpen(m_MapOptions, open);
        if (open)
            SetOptionsOpen(m_ModeOptions, false);   // 한 번에 하나만 펼침
    }

    public void ToggleMapOptions()
    {
        if (m_MapOptions != null)
            SetMapOptionsOpen(!m_MapOptions.gameObject.activeSelf);
    }

    public void SetModeOptionsOpen(bool open)
    {
        SetOptionsOpen(m_ModeOptions, open);
        if (open)
            SetOptionsOpen(m_MapOptions, false);
    }

    public void ToggleModeOptions()
    {
        if (m_ModeOptions != null)
            SetModeOptionsOpen(!m_ModeOptions.gameObject.activeSelf);
    }

    private static void SetOptionsOpen(RectTransform panel, bool open)
    {
        if (panel == null)
            return;

        panel.gameObject.SetActive(open);
        if (open)
            panel.SetAsLastSibling();
    }

    /// <summary>옵션 패널 아래에 세로로 버튼을 런타임 생성한다(맵 카탈로그 등 개수가 가변이라 코드 생성).</summary>
    private void BuildOptionButtons(RectTransform panel, IReadOnlyList<string> labels, Action<int> onSelect)
    {
        if (panel == null)
            return;

        for (int i = panel.childCount - 1; i >= 0; i--)
            Destroy(panel.GetChild(i).gameObject);

        int count = labels != null ? labels.Count : 0;
        const float rowH = 26f;
        const float pad = 4f;

        float width = panel.rect.width;
        if (width < 40f)
            width = 200f;
        float btnW = width - pad * 2f;

        panel.sizeDelta = new Vector2(panel.sizeDelta.x, count * rowH + pad * 2f);

        for (int i = 0; i < count; i++)
        {
            int index = i;
            var go = new GameObject($"Option{i}", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(panel, false);

            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(btnW, rowH - 3f);
            rt.anchoredPosition = new Vector2(0f, -pad - i * rowH);

            var img = go.GetComponent<Image>();
            img.color = Color.white;

            var button = go.GetComponent<Button>();
            button.targetGraphic = img;
            button.onClick.AddListener(() => onSelect?.Invoke(index));

            var textGo = new GameObject("Label", typeof(RectTransform));
            textGo.transform.SetParent(go.transform, false);
            var trt = (RectTransform)textGo.transform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;

            var text = textGo.AddComponent<Text>();
            text.font = JobsnailUiKit.LegacyFont;
            text.fontSize = 14;
            text.color = Color.black;
            text.alignment = TextAnchor.MiddleCenter;
            text.text = labels[index];

            JuicyButton.Attach(button);
        }
    }

    // ── 날씨 ON/OFF 토글 ──

    public void ApplyWeather(bool enabled)
    {
        SetText(m_WeatherToggleLabel, enabled ? "날씨 ON" : "날씨 OFF");
        if (m_WeatherToggleImage != null)
            m_WeatherToggleImage.color = enabled ? kPublic : kNeutral;
    }

    public void ApplyRoomType(bool isPrivate)
    {
        if (m_PrivateRoomButtonImage != null)
            m_PrivateRoomButtonImage.color = isPrivate ? kPrivate : kNeutral;

        if (m_PublicRoomButtonImage != null)
            m_PublicRoomButtonImage.color = isPrivate ? kNeutral : kPublic;

        if (m_PasswordLabel != null)
            m_PasswordLabel.SetActive(isPrivate);
        if (m_PasswordHint != null)
            m_PasswordHint.SetActive(isPrivate);
        if (m_PasswordInput != null)
            m_PasswordInput.gameObject.SetActive(isPrivate);
    }

    // ────────────────────────── 대기방 ──────────────────────────

    public void ApplyLobbyRoomState(in JobsnailLobbyRoomState state)
    {
        SetText(m_LobbySubtitle, string.IsNullOrWhiteSpace(state.RoomName) ? "이름 없는 방" : state.RoomName);
        SetText(m_LobbyStatusBadgeText, state.RoomIsFull ? "모집 완료" : "모집중");

        if (m_LobbyStatusBadgeImage != null)
            m_LobbyStatusBadgeImage.color = state.RoomIsFull ? kReady : kAccent;

        if (m_LobbyModeButton != null)
        {
            m_LobbyModeButton.gameObject.SetActive(true);        // 모드는 전원에게 표시
            m_LobbyModeButton.interactable = state.ShowModeButton; // 방장만 변경 가능
            SetButtonLabel(m_LobbyModeButton, state.ModeLabel);
        }

        EnsureLobbyExtras();
        if (m_LobbyWeatherButton != null)
        {
            m_LobbyWeatherButton.interactable = state.ShowWeatherToggle;   // 방장만 토글
            if (m_LobbyWeatherLabel != null)
                m_LobbyWeatherLabel.text = state.WeatherOn ? "날씨 ON" : "날씨 OFF";
        }
        if (m_LobbyRecordText != null)
            m_LobbyRecordText.text = state.RecordText ?? string.Empty;

        EnsureTeamSelect();
        if (m_TeamSelectRoot != null && m_TeamSelectRoot.activeSelf != state.ShowTeamSelect)
            m_TeamSelectRoot.SetActive(state.ShowTeamSelect);

        if (m_LobbyStartButton != null)
        {
            m_LobbyStartButton.gameObject.SetActive(state.ShowStartButton);
            m_LobbyStartButton.interactable = state.StartInteractable;
            SetButtonLabel(m_LobbyStartButton, "게임 시작");
            SetButtonColor(m_LobbyStartButton, state.AllReady ? kAccent : kDisabled);
        }

        if (m_LobbyReadyButton != null)
        {
            m_LobbyReadyButton.gameObject.SetActive(state.ShowReadyButton);
            m_LobbyReadyButton.interactable = state.ReadyInteractable;
            SetButtonLabel(m_LobbyReadyButton, "준비");
            SetButtonColor(m_LobbyReadyButton,
                !state.ReadyInteractable ? kDisabled : state.IsLocallyReady ? kReady : kNotReady);
        }

        SetText(m_LobbyStartHint, state.StartHint);
        SetText(m_LobbyReadyStatus, state.ReadyStatus);

        ApplyLobbySlots(state);
        ApplyMapSelect(state);
    }

    public void SetLobbyStartHint(string message)
    {
        SetText(m_LobbyStartHint, message);
    }

    /// <summary>프리팹에 직접 만들어 둔 날씨 토글/기록 요소를 이름으로 찾아 참조만 잡는다(위치는 프리팹 배치를 존중).
    /// 날씨 버튼: "LobbyWeatherButton"(Button, 자식 Text 라벨), 기록: "LobbyRecordText"(Text).</summary>
    private void EnsureLobbyExtras()
    {
        if (m_LobbyExtrasBound || m_Kind != OverlayKind.LobbyRoom)
            return;
        m_LobbyExtrasBound = true;

        var wb = FindDescendant(transform, "LobbyWeatherButton");
        if (wb != null)
        {
            m_LobbyWeatherButton = wb.GetComponent<Button>();
            m_LobbyWeatherLabel = wb.GetComponentInChildren<Text>(true);
            if (m_LobbyWeatherButton != null)
                m_LobbyWeatherButton.onClick.AddListener(() => Owner?.PrefabToggleLobbyWeather());
        }

        var rec = FindDescendant(transform, "LobbyRecordText");
        if (rec != null)
            m_LobbyRecordText = rec.GetComponent<Text>();
    }

    /// <summary>프리팹의 "TeamSelect" 컨테이너 + "BlueTeam"/"RedTeam" 버튼을 찾아 클릭을 연결한다(각자 자기 팀 선택).</summary>
    private void EnsureTeamSelect()
    {
        if (m_TeamSelectBound || m_Kind != OverlayKind.LobbyRoom)
            return;
        m_TeamSelectBound = true;

        var ts = FindDescendant(transform, "TeamSelect");
        m_TeamSelectRoot = ts != null ? ts.gameObject : null;

        var blue = FindDescendant(transform, "BlueTeam");
        var blueBtn = blue != null ? blue.GetComponent<Button>() : null;
        if (blueBtn != null)
            blueBtn.onClick.AddListener(() => Owner?.PrefabSelectTeam(0));

        var red = FindDescendant(transform, "RedTeam");
        var redBtn = red != null ? red.GetComponent<Button>() : null;
        if (redBtn != null)
            redBtn.onClick.AddListener(() => Owner?.PrefabSelectTeam(1));
    }

    /// <summary>슬롯의 "Team" 이미지 색을 팀에 맞게(0=파랑, 1=빨강) 칠하고, 점유 슬롯에서만 표시.</summary>
    private void SetSlotTeam(int index, bool occupied, int team)
    {
        var t = FindSlotChild(index, "Team");
        if (t == null)
            return;

        if (occupied)
        {
            var img = t.GetComponent<Image>();
            if (img != null)
                img.color = team == 1 ? kTeamRed : kTeamBlue;
            if (!t.gameObject.activeSelf)
                t.gameObject.SetActive(true);
        }
        else if (t.gameObject.activeSelf)
        {
            t.gameObject.SetActive(false);
        }
    }

    /// <summary>슬롯 루트/이름/상태 참조를 이름으로 다시 잡는다(프리팹에서 슬롯을 직접 편집해 serialized 배열이
    /// 끊겨도 안전). "LobbySlot{i}" / "LobbySlotName{i}" / "LobbySlotStatus{i}" 규칙을 따른다.</summary>
    private void ResolveSlots()
    {
        if (m_SlotsResolved || m_Kind != OverlayKind.LobbyRoom)
            return;
        m_SlotsResolved = true;

        m_LobbySlotRoots ??= new GameObject[4];
        m_LobbySlotNames ??= new Text[4];
        m_LobbySlotStatuses ??= new Text[4];

        // 중첩 슬롯 프리팹이 프로젝트에 없으면(Missing Prefab) 직렬화된 Text 참조가 CanvasRenderer 없는
        // 껍데기("Placeholder for referenced MonoBehaviour in Prefab instance")로 로드되고, GraphicRaycaster가
        // 매 프레임 MissingComponentException을 뿜는다 — 껍데기 Graphic은 파괴하고 참조를 비운 뒤 이름으로 다시 찾는다.
        for (int i = 0; i < m_LobbySlotNames.Length; i++)
            if (DestroyIfPlaceholderGraphic(m_LobbySlotNames[i])) m_LobbySlotNames[i] = null;
        for (int i = 0; i < m_LobbySlotStatuses.Length; i++)
            if (DestroyIfPlaceholderGraphic(m_LobbySlotStatuses[i])) m_LobbySlotStatuses[i] = null;
        for (int i = 0; i < m_LobbySlotRoots.Length; i++)
            if (m_LobbySlotRoots[i] != null && m_LobbySlotRoots[i].name.StartsWith("Placeholder for referenced")) m_LobbySlotRoots[i] = null;

        int n = Mathf.Min(4, Mathf.Min(m_LobbySlotRoots.Length, Mathf.Min(m_LobbySlotNames.Length, m_LobbySlotStatuses.Length)));
        for (int i = 0; i < n; i++)
        {
            var root = FindDescendant(transform, "LobbySlot" + i);
            if (root == null)
                continue;

            m_LobbySlotRoots[i] = root.gameObject;

            var nameT = FindDescendant(root, "LobbySlotName" + i);
            if (nameT != null)
                m_LobbySlotNames[i] = nameT.GetComponent<Text>();

            var statusT = FindDescendant(root, "LobbySlotStatus" + i);
            if (statusT != null)
                m_LobbySlotStatuses[i] = statusT.GetComponent<Text>();
        }
    }

    /// <summary>Missing Prefab 잔재로 남은 껍데기 Graphic(CanvasRenderer 없음)이면 파괴하고 true.</summary>
    private static bool DestroyIfPlaceholderGraphic(Graphic g)
    {
        if (g == null)
            return false;
        bool placeholder = g.GetComponent<CanvasRenderer>() == null || g.gameObject.name.StartsWith("Placeholder for referenced");
        if (!placeholder)
            return false;
        Debug.LogWarning($"[JobsnailLobbyPrefabView] 대기방 슬롯 참조가 Missing Prefab 껍데기({g.gameObject.name})라 제거합니다 — " +
                         "JobsnailLobbyRoomOverlay 안 슬롯 카드 프리팹이 프로젝트에 없습니다(커밋 누락). 슬롯 UI는 표시되지 않습니다.");
        Destroy(g.gameObject);
        return true;
    }

    private static Transform FindDescendant(Transform root, string name)
    {
        if (root.name == name)
            return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var found = FindDescendant(root.GetChild(i), name);
            if (found != null)
                return found;
        }
        return null;
    }

    private void ApplyLobbySlots(in JobsnailLobbyRoomState state)
    {
        ResolveSlots();   // 프리팹 편집으로 끊긴 슬롯 참조를 이름으로 복구

        if (m_LobbySlotNames == null || m_LobbySlotStatuses == null)
            return;

        int slotCount = Mathf.Min(m_LobbySlotNames.Length, m_LobbySlotStatuses.Length);
        var slots = state.Slots;

        for (int i = 0; i < slotCount; i++)
        {
            bool slotVisible = i < state.MaxPlayers;
            if (m_LobbySlotRoots != null && i < m_LobbySlotRoots.Length && m_LobbySlotRoots[i] != null)
                m_LobbySlotRoots[i].SetActive(slotVisible);

            if (!slotVisible)
            {
                SetSlotCrown(i, false);
                SetSlotCharacter(i, false, null, null);
                SetSlotTeam(i, false, 0);
                continue;
            }

            bool occupied;
            string name;
            bool isHost;
            bool ready;
            string charId;
            string outfitId;
            int team;

            if (slots != null && i < slots.Length)
            {
                occupied = slots[i].Occupied;
                name = slots[i].Name;
                isHost = slots[i].IsHost;
                ready = slots[i].Ready;
                charId = slots[i].CharacterId;
                outfitId = slots[i].OutfitId;
                team = slots[i].Team;
            }
            else
            {
                // 로스터 미제공 시 폴백(닉네임 알 수 없으므로 공백).
                occupied = i < state.JoinedCount;
                name = string.Empty;
                isHost = i == 0;
                ready = i <= state.ReadyCount;
                charId = string.Empty;
                outfitId = string.Empty;
                team = 0;
            }

            if (!occupied)
            {
                SetText(m_LobbySlotNames[i], string.Empty);   // 아직 안 들어온 자리 = 공백
                SetText(m_LobbySlotStatuses[i], string.Empty);
                SetSlotCrown(i, false);
                SetSlotCharacter(i, false, null, null);
                SetSlotTeam(i, false, 0);
                SetSlotDimmed(i, true);
                continue;
            }

            SetSlotDimmed(i, false);
            SetText(m_LobbySlotNames[i], name ?? string.Empty);            // 각자 닉네임(없으면 공백)
            SetText(m_LobbySlotStatuses[i], (!isHost && ready) ? "준비" : "대기중");  // 방장·미준비=대기중, 팀원 준비=준비
            SetSlotCrown(i, isHost);
            SetSlotCharacter(i, true, charId, outfitId);
            SetSlotTeam(i, state.ShowTeamSelect, team);   // 팀 색은 2vs2 모드에서만 표시
        }

        // 새 유저 입장 감지 → 마지막으로 채워진 슬롯 디용(구멍이 있어도 안전).
        int occupiedCount = CountOccupied(state);
        if (occupiedCount > m_PrevJoinedForPop)
        {
            int lastFilled = LastOccupiedIndex(state);
            if (lastFilled >= 0 && m_LobbySlotRoots != null && lastFilled < m_LobbySlotRoots.Length &&
                m_LobbySlotRoots[lastFilled] != null)
                GridSystem.GridJuice.Squish(m_LobbySlotRoots[lastFilled], 0.15f);
        }
        m_PrevJoinedForPop = occupiedCount;
    }

    private static int CountOccupied(in JobsnailLobbyRoomState state)
    {
        if (state.Slots == null)
            return state.JoinedCount;
        int c = 0;
        for (int i = 0; i < state.Slots.Length; i++)
            if (state.Slots[i].Occupied) c++;
        return c;
    }

    private static int LastOccupiedIndex(in JobsnailLobbyRoomState state)
    {
        if (state.Slots == null)
            return state.JoinedCount - 1;
        int last = -1;
        for (int i = 0; i < state.Slots.Length; i++)
            if (state.Slots[i].Occupied) last = i;
        return last;
    }

    /// <summary>슬롯 안에 만들어 둔 방장 왕관("LobbyCrown")을 방장 슬롯에서만 켠다. 없으면 무동작.</summary>
    private void SetSlotCrown(int index, bool show)
    {
        var crown = FindSlotChild(index, "LobbyCrown");
        if (crown != null && crown.gameObject.activeSelf != show)
            crown.gameObject.SetActive(show);
    }

    /// <summary>빈 슬롯은 흐리게(카드 색은 안 건드리고 CanvasGroup 알파만). 프리팹 스타일 존중.</summary>
    private void SetSlotDimmed(int index, bool dimmed)
    {
        if (m_LobbySlotRoots == null || index < 0 || index >= m_LobbySlotRoots.Length || m_LobbySlotRoots[index] == null)
            return;

        var go = m_LobbySlotRoots[index];
        var cg = go.GetComponent<CanvasGroup>();
        if (cg == null)
            cg = go.AddComponent<CanvasGroup>();
        cg.alpha = dimmed ? 0.45f : 1f;
    }

    private JobsnailLobbyCharacterStage EnsureCharacterStage()
    {
        if (m_CharacterStage == null)
        {
            var go = new GameObject("@JobsnailLobbyCharacterStage");
            m_CharacterStage = go.AddComponent<JobsnailLobbyCharacterStage>();
            m_CharacterStage.EnsureBuilt();
        }
        return m_CharacterStage;
    }

    /// <summary>슬롯 안에 만들어 둔 캐릭터 표시용 RawImage("LobbyCharacter")에 3D 캐릭터 열을 채운다. 없으면 무동작.</summary>
    private void SetSlotCharacter(int index, bool occupied, string charId, string outfitId)
    {
        if (m_Kind != OverlayKind.LobbyRoom)
            return;

        var found = FindSlotChild(index, "LobbyCharacter");
        if (found == null)
            return;

        var img = found.GetComponent<RawImage>();
        if (img == null)
        {
            // RenderTexture는 RawImage로만 표시된다. Image로 만들어졌으면 런타임에 교체.
            var legacy = found.GetComponent<Image>();
            if (legacy != null)
                DestroyImmediate(legacy);
            // Image를 지우면 함께 쓰던 CanvasRenderer도 사라진다 — Graphic(RawImage)엔 필수라 없으면 다시 붙인다.
            // (없으면 GraphicRaycaster가 매 프레임 MissingComponentException을 뿜는다)
            if (found.GetComponent<CanvasRenderer>() == null)
                found.gameObject.AddComponent<CanvasRenderer>();
            img = found.gameObject.AddComponent<RawImage>();
            img.raycastTarget = false;
            img.color = Color.white;
        }

        if (occupied)
        {
            var stage = EnsureCharacterStage();
            stage.SetBooth(index, true, charId, outfitId);
            img.texture = stage.Texture;
            img.uvRect = JobsnailLobbyCharacterStage.UvRectFor(index);
            if (!img.enabled)
                img.enabled = true;
        }
        else
        {
            if (m_CharacterStage != null)
                m_CharacterStage.SetBooth(index, false, null, null);
            if (img.enabled)
                img.enabled = false;
        }
    }

    private Transform FindSlotChild(int index, string name)
    {
        if (m_LobbySlotRoots == null || index < 0 || index >= m_LobbySlotRoots.Length || m_LobbySlotRoots[index] == null)
            return null;
        return FindDescendant(m_LobbySlotRoots[index].transform, name);
    }

    private void ApplyMapSelect(in JobsnailLobbyRoomState state)
    {
        SetText(m_MapNameText, state.MapName);

        if (m_MapThumbnail != null)
        {
            m_MapThumbnail.sprite = state.MapThumbnail;
            m_MapThumbnail.enabled = state.MapThumbnail != null;
        }

        if (m_MapPrevButton != null)
            m_MapPrevButton.gameObject.SetActive(state.ShowMapArrows);
        if (m_MapNextButton != null)
            m_MapNextButton.gameObject.SetActive(state.ShowMapArrows);
    }

    // ────────────────────────── 버튼 이벤트 → 컨트롤러 ──────────────────────────

    public void OnShowCreateClicked() => Owner?.PrefabShowCreateSession();
    public void OnRefreshClicked() => Owner?.PrefabRefreshSessionList();
    public void OnBackToMainClicked() => Owner?.PrefabBackToMain();
    public void OnJoinPasswordConfirmClicked() => Owner?.PrefabConfirmJoinPassword();
    public void OnJoinPasswordCancelClicked() => Owner?.PrefabCancelJoinPassword();
    public void OnCloseCreateClicked() => Owner?.PrefabCloseCreateOverlay();
    public void OnSubmitCreateClicked() => Owner?.PrefabSubmitCreateSession();
    public void OnToggleMapClicked() => Owner?.PrefabToggleMapOptions();
    public void OnToggleModeClicked() => Owner?.PrefabToggleModeOptions();
    public void OnWeatherToggleClicked() => Owner?.PrefabToggleWeather();
    public void OnPrivateRoomClicked() => Owner?.PrefabSetRoomType(true);
    public void OnPublicRoomClicked() => Owner?.PrefabSetRoomType(false);
    public void OnLobbyLeaveClicked() => Owner?.PrefabLeaveLobbyRoom();
    public void OnLobbyStartClicked() => Owner?.PrefabStartLobbyGame();
    public void OnLobbyReadyClicked() => Owner?.PrefabToggleReadyState();
    public void OnLobbyModeClicked() => Owner?.PrefabCycleGameMode();
    public void OnMapPrevClicked() => Owner?.PrefabStepMap(-1);
    public void OnMapNextClicked() => Owner?.PrefabStepMap(1);

    // ────────────────────────── 위젯 헬퍼 ──────────────────────────

    private static void SetText(Text label, string value)
    {
        if (label != null)
            label.text = value ?? string.Empty;
    }

    private static void SetButtonLabel(Button button, string label)
    {
        if (button == null)
            return;

        var text = button.GetComponentInChildren<Text>(true);
        if (text != null)
            text.text = label ?? string.Empty;
    }

    private static void SetButtonColor(Button button, Color color)
    {
        if (button == null)
            return;

        var image = button.GetComponent<Image>();
        if (image != null)
            image.color = color;
    }
}
