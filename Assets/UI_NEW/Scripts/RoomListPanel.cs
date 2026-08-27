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

        private Button closeWindowButton;   // 배경 헤더의 × 자리(런타임 생성)
        private RoomListFilter currentFilter;
        private float nextRefreshTime;
        private bool returningToMain;
        private readonly List<UiNewSessionRoom> rooms = new();
        private readonly List<UiNewSessionRoom> visibleRooms = new();

        public event Action RefreshRequested;
        public event Action<RoomListFilter> FilterChanged;
        public event Action<UiNewSessionRoom> RoomJoinRequested;

        private Text emptyLabel;   // 조건에 맞는 방 0개 안내(카드 영역 가운데 · 런타임 생성)

        private void Awake()
        {
            createRoomButton?.onClick.AddListener(() => router.Show(UiNewScreen.CreateRoom));
            BuildEmptyLabel();
            refreshButton?.onClick.AddListener(RequestRefresh);
            backToMainButton?.onClick.AddListener(ReturnToMain);
            // 배경에 그려진 창 헤더의 ×도 '메인으로'와 같은 동작(메인 화면 복귀)으로 묶는다.
            closeWindowButton = UiNewWindowCloseButton.Attach(transform, ReturnToMain);
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
            SetReturnButtonsInteractable(false);

            try
            {
                // 혹시 이전 세션/Netcode가 남아 있어도 먼저 안전하게 정리한 뒤 메인으로 이동한다.
                await JobsnailSessionManager.Instance.LeaveLobbyRoomSecurelyAsync(true);
            }
            catch (Exception exception)
            {
                returningToMain = false;
                SetReturnButtonsInteractable(true);
                Debug.LogError($"[UI_NEW] 메인 화면 복귀 실패: {exception.Message}");
            }
        }

        // '메인으로' 버튼과 헤더 × 는 같은 동작이라 중복 클릭 가드도 함께 건다.
        private void SetReturnButtonsInteractable(bool value)
        {
            if (backToMainButton != null)
                backToMainButton.interactable = value;
            if (closeWindowButton != null)
                closeWindowButton.interactable = value;
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
            if (emptyLabel != null) emptyLabel.gameObject.SetActive(visibleRooms.Count == 0);
        }

        // 카드 슬롯들의 부모 한가운데에 안내 문구. 폰트/색은 카드의 텍스트에서 가져와 톤을 맞춘다.
        private void BuildEmptyLabel()
        {
            if (roomCards == null || roomCards.Length == 0 || roomCards[0] == null) return;
            Transform parent = roomCards[0].transform.parent;
            if (parent == null) return;

            var go = new GameObject("EmptyLabel", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.transform.SetAsLastSibling();
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);   // 화면 가운데 고정 크기(부모 레이아웃/사이즈 영향 없음)
            rt.anchoredPosition = new Vector2(0f, -40f);
            rt.sizeDelta = new Vector2(900f, 80f);

            emptyLabel = go.AddComponent<Text>();
            var sample = roomCards[0].GetComponentInChildren<Text>(true);
            emptyLabel.font = sample != null && sample.font != null ? sample.font : JobsnailUiKit.LegacyFont;
            emptyLabel.fontSize = 26;
            emptyLabel.color = new Color(0.55f, 0.47f, 0.40f, 0.85f);   // 크림 배경 위 연갈색(카드 글자색은 흰색일 수 있어 고정)
            emptyLabel.alignment = TextAnchor.MiddleCenter;
            emptyLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
            emptyLabel.verticalOverflow = VerticalWrapMode.Overflow;
            emptyLabel.text = "조건에 맞는 방이 없어요";
            emptyLabel.raycastTarget = false;
            emptyLabel.gameObject.SetActive(false);
        }

        private static void SetState(Button button, Sprite[] states, bool selected)
        {
            if (button == null || button.image == null || states == null || states.Length < 2)
                return;
            button.image.sprite = states[selected ? 1 : 0];
        }
    }
}
