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

/// <summary>대기방 화면 한 프레임 분량의 표시 상태. 네트워크 값 해석은 컨트롤러가 끝낸 뒤 넘긴다.</summary>
public struct JobsnailLobbyRoomState
{
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

    public OverlayKind Kind => m_Kind;
    public RectTransform PcRoot => m_PcRoot;
    public string RoomNameText => m_RoomNameInput != null ? m_RoomNameInput.text.Trim() : string.Empty;
    public string PasswordText => m_PasswordInput != null ? m_PasswordInput.text.Trim() : string.Empty;
    public string JoinPasswordText => m_JoinPasswordInput != null ? m_JoinPasswordInput.text : string.Empty;

    // ────────────────────────── 수명 주기 ──────────────────────────

    public void Bind(JobsnailLobbySkinner owner)
    {
        m_Owner = owner;

        if (m_SessionCardTemplate != null)
            m_SessionCardTemplate.gameObject.SetActive(false);
        if (m_JoinPasswordOverlay != null)
            m_JoinPasswordOverlay.SetActive(false);
        if (m_MapOptions != null)
            m_MapOptions.gameObject.SetActive(false);
        if (m_ModeOptions != null)
            m_ModeOptions.gameObject.SetActive(false);

        JuicyButton.AttachAll(gameObject);   // 프리팹에서 만든 버튼도 쫀득(중복 부착 안전)
    }

    public void SetVisible(bool visible)
    {
        gameObject.SetActive(visible);
        if (visible)
            transform.SetAsLastSibling();
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

    public void SetSessionCards(IReadOnlyList<JobsnailSessionCardData> cards, Action<int> onSelect)
    {
        ClearSessionCards();

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
            m_LobbyModeButton.gameObject.SetActive(state.ShowModeButton);
            SetButtonLabel(m_LobbyModeButton, state.ModeLabel);
        }

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

    private void ApplyLobbySlots(in JobsnailLobbyRoomState state)
    {
        if (m_LobbySlotNames == null || m_LobbySlotStatuses == null)
            return;

        int slotCount = Mathf.Min(m_LobbySlotNames.Length, m_LobbySlotStatuses.Length);
        for (int i = 0; i < slotCount; i++)
        {
            if (m_LobbySlotRoots != null && i < m_LobbySlotRoots.Length && m_LobbySlotRoots[i] != null)
                m_LobbySlotRoots[i].SetActive(i < state.MaxPlayers);

            if (i >= state.MaxPlayers)
                continue;

            if (i == 0)
            {
                SetText(m_LobbySlotNames[i], "방장");
                SetText(m_LobbySlotStatuses[i], state.AllReady ? "시작 가능" : "방장 / 준비 완료");
                continue;
            }

            if (i < state.JoinedCount)
            {
                SetText(m_LobbySlotNames[i], $"팀원 {i}");
                SetText(m_LobbySlotStatuses[i], i <= state.ReadyCount ? "준비 완료" : "대기중...");
            }
            else
            {
                SetText(m_LobbySlotNames[i], "빈 자리");
                SetText(m_LobbySlotStatuses[i], string.Empty);
            }
        }

        // 새 팀원 입장 → 그 슬롯 디용
        if (state.JoinedCount > m_PrevJoinedForPop && m_LobbySlotRoots != null &&
            state.JoinedCount - 1 < m_LobbySlotRoots.Length && m_LobbySlotRoots[state.JoinedCount - 1] != null)
            GridSystem.GridJuice.Squish(m_LobbySlotRoots[state.JoinedCount - 1], 0.15f);
        m_PrevJoinedForPop = state.JoinedCount;
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
