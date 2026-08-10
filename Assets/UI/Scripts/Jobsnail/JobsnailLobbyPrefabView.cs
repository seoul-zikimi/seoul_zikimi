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
    [SerializeField] private Text m_MaxPlayersLabel;
    [SerializeField] private GameObject m_MaxPlayersOptions;
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

    // 세션 카드 그리드(2열) 배치 상수 — 프리팹 좌표계 기준.
    private const int kMaxSessionCards = 6;
    private const float kCardColumnX = 210f;
    private const float kCardTopY = 135f;
    private const float kCardRowStep = 135f;

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
        if (m_MaxPlayersOptions != null)
            m_MaxPlayersOptions.SetActive(false);

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

        int count = Mathf.Min(cards.Count, kMaxSessionCards);
        for (int i = 0; i < count; i++)
        {
            var card = Instantiate(m_SessionCardTemplate, m_CustomSessionListRoot, false);
            card.name = $"SessionCard{i}";
            card.gameObject.SetActive(true);

            var rt = (RectTransform)card.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(
                i % 2 == 0 ? -kCardColumnX : kCardColumnX,
                kCardTopY - (i / 2) * kCardRowStep);

            card.Apply(cards[i], i, onSelect);
            m_SpawnedCards.Add(card);
        }
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

    public void InitCreateForm(string defaultRoomName, string defaultPassword, int defaultMaxPlayers, bool isPrivate)
    {
        if (m_RoomNameInput != null)
        {
            m_RoomNameInput.characterLimit = 15;
            m_RoomNameInput.text = defaultRoomName;
        }

        if (m_PasswordInput != null)
        {
            m_PasswordInput.characterLimit = 32;
            m_PasswordInput.contentType = InputField.ContentType.Standard;
            m_PasswordInput.text = defaultPassword;
        }

        SetMaxPlayersLabel(defaultMaxPlayers);
        SetMaxPlayersOptionsOpen(false);
        ApplyRoomType(isPrivate);
        SetCreateStatus(string.Empty);
    }

    public void SetCreateStatus(string message)
    {
        SetText(m_CreateStatus, message);
    }

    public void SetMaxPlayersLabel(int value)
    {
        SetText(m_MaxPlayersLabel, $"{value}명 ▼");
    }

    public void SetMaxPlayersOptionsOpen(bool open)
    {
        if (m_MaxPlayersOptions == null)
            return;

        m_MaxPlayersOptions.SetActive(open);
        if (open)
            m_MaxPlayersOptions.transform.SetAsLastSibling();
    }

    public void ToggleMaxPlayersOptions()
    {
        if (m_MaxPlayersOptions != null)
            SetMaxPlayersOptionsOpen(!m_MaxPlayersOptions.activeSelf);
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
    public void OnToggleMaxPlayersClicked() => Owner?.PrefabToggleMaxPlayersOptions();
    public void OnSelectMaxPlayers1Clicked() => Owner?.PrefabSelectMaxPlayers(1);
    public void OnSelectMaxPlayers2Clicked() => Owner?.PrefabSelectMaxPlayers(2);
    public void OnSelectMaxPlayers3Clicked() => Owner?.PrefabSelectMaxPlayers(3);
    public void OnSelectMaxPlayers4Clicked() => Owner?.PrefabSelectMaxPlayers(4);
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
