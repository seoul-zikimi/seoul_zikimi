using UnityEngine;
using UnityEngine.UI;

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
    [SerializeField] private Text m_LobbyStartHint;
    [SerializeField] private Text m_LobbyReadyStatus;
    [SerializeField] private GameObject[] m_LobbySlotRoots;
    [SerializeField] private Text[] m_LobbySlotNames;
    [SerializeField] private Text[] m_LobbySlotStatuses;

    private JobsnailLobbySkinner m_Owner;

    public OverlayKind Kind => m_Kind;
    public RectTransform PcRoot => m_PcRoot;
    public RectTransform CustomSessionListRoot => m_CustomSessionListRoot;
    public Text SessionStatus => m_SessionStatus;
    public InputField RoomNameInput => m_RoomNameInput;
    public InputField PasswordInput => m_PasswordInput;
    public Text CreateStatus => m_CreateStatus;
    public Text MaxPlayersLabel => m_MaxPlayersLabel;
    public GameObject MaxPlayersOptions => m_MaxPlayersOptions;
    public Image PrivateRoomButtonImage => m_PrivateRoomButtonImage;
    public Image PublicRoomButtonImage => m_PublicRoomButtonImage;
    public GameObject PasswordLabel => m_PasswordLabel;
    public GameObject PasswordHint => m_PasswordHint;
    public Text LobbySubtitle => m_LobbySubtitle;
    public Text LobbyStatusBadgeText => m_LobbyStatusBadgeText;
    public Image LobbyStatusBadgeImage => m_LobbyStatusBadgeImage;
    public Button LobbyStartButton => m_LobbyStartButton;
    public Button LobbyReadyButton => m_LobbyReadyButton;
    public Text LobbyStartHint => m_LobbyStartHint;
    public Text LobbyReadyStatus => m_LobbyReadyStatus;
    public GameObject[] LobbySlotRoots => m_LobbySlotRoots;
    public Text[] LobbySlotNames => m_LobbySlotNames;
    public Text[] LobbySlotStatuses => m_LobbySlotStatuses;

    public void Bind(JobsnailLobbySkinner owner)
    {
        m_Owner = owner;
    }

    private JobsnailLobbySkinner Owner => m_Owner != null ? m_Owner : JobsnailLobbySkinner.ActiveInstance;

    public void OnShowCreateClicked() => Owner?.PrefabShowCreateSession();
    public void OnRefreshClicked() => Owner?.PrefabRefreshSessionList();
    public void OnBackToMainClicked() => Owner?.PrefabBackToMain();
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
}
