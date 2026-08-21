using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SeoulZikimi.UI.New
{
    public sealed class RoomListPanel : MonoBehaviour, IRoomListActions
    {
        [Header("Navigation")]
        [SerializeField] private UiNewScreenRouter router;
        [SerializeField] private Button createRoomButton;
        [SerializeField] private Button refreshButton;
        [SerializeField] private Button backToMainButton;

        [Header("Filters")]
        [SerializeField] private Button allFilterButton;
        [SerializeField] private Button publicFilterButton;
        [SerializeField] private Button privateFilterButton;
        [SerializeField] private Sprite[] allFilterStates;
        [SerializeField] private Sprite[] publicFilterStates;
        [SerializeField] private Sprite[] privateFilterStates;

        [Header("Static room slots in the scene")]
        [SerializeField] private UiNewRoomCardView[] roomCards;
        [SerializeField] private float refreshCooldownSeconds = 1f;

        private RoomListFilter currentFilter;
        private float nextRefreshTime;
        private bool returningToMain;
        private readonly List<UiNewSessionRoom> rooms = new();
        private readonly List<UiNewSessionRoom> visibleRooms = new();

        public event Action RefreshRequested;
        public event Action<RoomListFilter> FilterChanged;
        public event Action<UiNewSessionRoom> RoomJoinRequested;

        private void Awake()
        {
            createRoomButton?.onClick.AddListener(() => router.Show(UiNewScreen.CreateRoom));
            refreshButton?.onClick.AddListener(RequestRefresh);
            backToMainButton?.onClick.AddListener(ReturnToMain);
            allFilterButton?.onClick.AddListener(() => SelectFilter(RoomListFilter.All));
            publicFilterButton?.onClick.AddListener(() => SelectFilter(RoomListFilter.Public));
            privateFilterButton?.onClick.AddListener(() => SelectFilter(RoomListFilter.Private));

            for (int i = 0; i < roomCards.Length; i++)
            {
                int index = i;
                roomCards[i]?.Button?.onClick.AddListener(() => RequestJoin(index));
            }

            SelectFilter(RoomListFilter.All, false);
        }

        private async void ReturnToMain()
        {
            if (returningToMain)
                return;

            returningToMain = true;
            if (backToMainButton != null)
                backToMainButton.interactable = false;

            try
            {
                // 혹시 이전 세션/Netcode가 남아 있어도 먼저 안전하게 정리한 뒤 메인으로 이동한다.
                await JobsnailSessionManager.Instance.LeaveLobbyRoomSecurelyAsync(true);
            }
            catch (Exception exception)
            {
                returningToMain = false;
                if (backToMainButton != null)
                    backToMainButton.interactable = true;
                Debug.LogError($"[UI_NEW] 메인 화면 복귀 실패: {exception.Message}");
            }
        }

        public void SetRooms(IReadOnlyList<UiNewSessionRoom> values)
        {
            rooms.Clear();
            if (values != null)
                for (int i = 0; i < values.Count; i++)
                    rooms.Add(values[i]);
            RefreshCards();
        }

        private void RequestRefresh()
        {
            if (Time.unscaledTime < nextRefreshTime)
                return;

            nextRefreshTime = Time.unscaledTime + refreshCooldownSeconds;
            RefreshRequested?.Invoke();
        }

        private void RequestJoin(int index)
        {
            if (index < 0 || index >= visibleRooms.Count)
                return;
            RoomJoinRequested?.Invoke(visibleRooms[index]);
        }

        private void SelectFilter(RoomListFilter filter, bool notify = true)
        {
            currentFilter = filter;
            SetState(allFilterButton, allFilterStates, filter == RoomListFilter.All);
            SetState(publicFilterButton, publicFilterStates, filter == RoomListFilter.Public);
            SetState(privateFilterButton, privateFilterStates, filter == RoomListFilter.Private);
            if (notify)
                FilterChanged?.Invoke(filter);
            RefreshCards();
        }

        private void RefreshCards()
        {
            visibleRooms.Clear();
            foreach (UiNewSessionRoom room in rooms)
            {
                if (currentFilter == RoomListFilter.Public && room.HasPassword
                    || currentFilter == RoomListFilter.Private && !room.HasPassword)
                    continue;
                visibleRooms.Add(room);
            }

            for (int i = 0; i < roomCards.Length; i++)
            {
                bool hasRoom = i < visibleRooms.Count;
                roomCards[i].gameObject.SetActive(hasRoom);
                if (hasRoom)
                    roomCards[i].Apply(visibleRooms[i]);
            }
        }

        private static void SetState(Button button, Sprite[] states, bool selected)
        {
            if (button == null || button.image == null || states == null || states.Length < 2)
                return;
            button.image.sprite = states[selected ? 1 : 0];
        }
    }
}
