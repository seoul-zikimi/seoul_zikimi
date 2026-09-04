using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SeoulZikimi.UI.New
{
    public sealed class LobbyPanel : MonoBehaviour, ILobbyActions
    {
        private static readonly string[] QuickMessages =
        {
            "준비해!", "준비완료!", "맵 바꾸자!", "타임어택!", "대전 모드!", "자유 모드!"
        };

        [Header("Navigation / primary action")]
        [SerializeField] private UiNewScreenRouter router;
        [SerializeField] private Button leaveButton;
        [SerializeField] private Button readyButton;
        [SerializeField] private Button startButton;
        [SerializeField] private Sprite hostStartSprite;
        [SerializeField] private Sprite memberReadySprite;
        [SerializeField] private Sprite memberReadyCompleteSprite;

        [Header("Chat")]
        [SerializeField] private Button[] quickChatButtons;
        [SerializeField] private InputField chatInput;
        [SerializeField] private Button chatSendButton;
        [SerializeField] private Text chatLog;
        [SerializeField] private ScrollRect chatScroll;

        [Header("Lobby state")]
        [SerializeField] private Text roomTitle;
        [SerializeField] private UiNewLobbySlotView[] slots;
        [SerializeField] private Button blueTeamButton;
        [SerializeField] private Button redTeamButton;
        [SerializeField] private Button mapSelector;
        [SerializeField] private Button modeSelector;
        [SerializeField] private Button weatherToggle;
        [SerializeField] private Text mapValue;
        [SerializeField] private Text modeValue;
        [SerializeField] private Text bestRecordValue;
        [SerializeField] private Image mapThumbnail;
        [SerializeField] private Image weatherImage;
        [SerializeField] private Sprite weatherOnSprite;
        [SerializeField] private Sprite weatherOffSprite;
        [SerializeField] private GameObject mapOptionsRoot;
        [SerializeField] private GameObject modeOptionsRoot;
        [SerializeField] private Button[] mapOptionButtons;
        [SerializeField] private Button[] modeOptionButtons;
        [SerializeField] private GameObject[] settingLockOverlays;

        // 채팅 도배 방지: 연속 ChatBurstLimit개까지 바로 보내고, 그 뒤엔 ChatCooldownSeconds 대기.
        // 대기가 지나면 카운터를 0으로 되돌린다. 서버(LobbyRoomNet)에도 같은 규칙이 있다.
        private const int ChatBurstLimit = 3;
        private const float ChatCooldownSeconds = 3f;

        private readonly List<string> chatLines = new();
        private readonly string[] avatarKeys = new string[LobbyRoomNet.RoomCapacity];
        private readonly Sprite[] avatarSprites = new Sprite[LobbyRoomNet.RoomCapacity];
        private JobsnailLobbyCharacterStage avatarStage;
        private Button closeWindowButton;   // 배경 헤더의 × 자리(런타임 생성)
        private bool localIsHost;
        private int chatBurstCount;
        private float lastChatSentAt = -ChatCooldownSeconds;
        private float lastChatNoticeAt = -1f;

        public event Action LeaveRequested;
        public event Action ReadyRequested;
        public event Action StartRequested;
        public event Action<int> QuickChatRequested;
        public event Action<string> TextChatRequested;
        public event Action<int> TeamRequested;
        public event Action<int> MapRequested;
        public event Action<int> MapStepRequested;   // 맵 좌우 화살표(-1 / +1)
        public event Action<int> ModeRequested;
        public event Action WeatherRequested;

        private void Awake()
        {
            UiNewButtonVisualPolicy.Apply(transform);
            leaveButton?.onClick.AddListener(RequestLeave);
            readyButton?.gameObject.SetActive(false);
            startButton?.onClick.AddListener(() =>
            {
                if (localIsHost) StartRequested?.Invoke();
                else ReadyRequested?.Invoke();
            });
            chatSendButton?.onClick.AddListener(SendChat);
            // 모바일: 소프트 키보드의 '완료/보내기'는 Keyboard.current 이벤트를 안 만들어
            // 데스크톱용 엔터 폴링(HandleChatEnterKey)으로는 절대 안 잡힌다 — onEndEdit로 전송.
            // (뒤로가기 등으로 취소(wasCanceled)한 경우는 제외. 탭으로 포커스만 잃어도 전송되지만
            //  모바일 채팅에선 '완료=전송'이 표준 UX라 감수한다.)
            if (MobileControlsHUD.ShouldUseMobileUI)
                chatInput?.onEndEdit.AddListener(_ =>
                {
                    if (chatInput != null && !chatInput.wasCanceled)
                        SendChat();
                });
            for (int i = 0; quickChatButtons != null && i < quickChatButtons.Length; i++)
            {
                int index = i;
                quickChatButtons[i]?.onClick.AddListener(() => SendQuickChat(index));
            }
            blueTeamButton?.onClick.AddListener(() => TeamRequested?.Invoke(0));
            redTeamButton?.onClick.AddListener(() => TeamRequested?.Invoke(1));
            mapSelector?.onClick.AddListener(() => ToggleOptions(mapOptionsRoot, modeOptionsRoot));
            modeSelector?.onClick.AddListener(() => ToggleOptions(modeOptionsRoot, mapOptionsRoot));
            weatherToggle?.onClick.AddListener(() => WeatherRequested?.Invoke());
            BuildMapOptions();   // 맵 목록은 프리팹 고정이 아니라 카탈로그에서 만든다(바인딩도 여기서)
            BuildMapArrows();
            BindOptions(modeOptionButtons, index => ModeRequested?.Invoke(index), modeOptionsRoot);
            UiNewDropdownList.Setup(modeOptionsRoot,
                modeSelector != null ? (RectTransform)modeSelector.transform : null);   // 스크롤 목록 조립(맵 쪽은 BuildMapOptions가)
            // 세션 화면 배경도 같은 창 헤더를 쓴다. ×는 '나가기'와 동일하게 확인 팝업을 거쳐 방을 떠난다.
            closeWindowButton = UiNewWindowCloseButton.Attach(transform, RequestLeave);
        }

        private void OnEnable()
        {
            UiNewButtonVisualPolicy.Apply(transform);
            UiNewWindowCloseButton.KeepInvisible(closeWindowButton);   // Apply가 되돌린 ColorTint를 다시 끈다
            ClearChat();
            chatBurstCount = 0;
            lastChatSentAt = -ChatCooldownSeconds;   // 방에 새로 들어올 때 쿨타임을 물려받지 않는다
            mapOptionsRoot?.SetActive(false);
            modeOptionsRoot?.SetActive(false);
            ConfigureChatScroll();
        }

        private void Update()
        {
            HandleChatEnterKey();
            SetChatButtonsInteractable(!IsChatOnCooldown(out _));
        }

        // 엔터 전송. onEndEdit 콜백은 입력이 갱신된 '다음' 프레임에 UI 이벤트로 오기 때문에
        // 그 안에서 wasPressedThisFrame을 보면 이미 false다. 그래서 Update에서 직접 키를 본다.
        //
        // ⚠ isFocused만 보면 안 된다: InputField는 엔터를 EventSystem 업데이트에서 처리하며 즉시 포커스를
        // 놓는데, EventSystem과 이 스크립트의 Update 실행 순서는 지정돼 있지 않아 기기/빌드마다 다르다.
        // EventSystem이 먼저 돌면 엔터 프레임에 이미 isFocused=false → 전송 불발("어떤 컴은 되고 어떤 컴은 안 됨").
        // 그래서 '직전 프레임까지 포커스였다'도 함께 인정한다.
        private bool chatHadFocusLastFrame;

        private void HandleChatEnterKey()
        {
            bool focusedNow = chatInput != null && chatInput.isFocused;
            bool hadFocus = chatHadFocusLastFrame;
            chatHadFocusLastFrame = focusedNow;
            if (chatInput == null || (!focusedNow && !hadFocus)) return;
            // 프로젝트가 신형 Input System 전용(activeInputHandler=1)이라 UnityEngine.Input은 예외를 던진다.
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard == null ||
                (!keyboard.enterKey.wasPressedThisFrame && !keyboard.numpadEnterKey.wasPressedThisFrame))
                return;
            // 한글 IME: 엔터 시점엔 마지막 글자가 아직 조합 중이라 text에 없다. 여기서 바로 보내면
            // 끝 글자가 빠지고, 비운 칸에 조합 글자가 확정돼 남는다("끝 글자가 남아 엔터 두 번").
            // InputField가 이번 프레임 이벤트 처리에서 조합을 확정한 '다음' 프레임에 보낸다.
            if (!chatSendPending) StartCoroutine(SendChatAfterImeCommit());
        }

        private bool chatSendPending;

        private IEnumerator SendChatAfterImeCommit()
        {
            chatSendPending = true;
            yield return null;   // IME 조합 확정 + InputField 엔터 처리(포커스 해제) 이후
            chatSendPending = false;
            SendChat();
            if (chatInput != null && chatInput.isActiveAndEnabled)
                chatInput.ActivateInputField();   // 연속 입력이 끊기지 않게 포커스 복원
        }

        /// <summary>쿨타임 중이면 true. 쿨타임이 지났으면 연속 카운터를 초기화한다.</summary>
        private bool IsChatOnCooldown(out float remainingSeconds)
        {
            float elapsed = Time.unscaledTime - lastChatSentAt;
            if (elapsed >= ChatCooldownSeconds)
            {
                chatBurstCount = 0;
                remainingSeconds = 0f;
                return false;
            }
            remainingSeconds = ChatCooldownSeconds - elapsed;
            return chatBurstCount >= ChatBurstLimit;
        }

        /// <summary>도배 제한을 통과하면 채팅을 내보내고 true. 막히면 안내만 남기고 false.</summary>
        private bool TryEmitChat(string message)
        {
            if (IsChatOnCooldown(out float remaining))
            {
                AppendSystemNotice($"도배 방지 — {Mathf.CeilToInt(remaining)}초 뒤에 다시 보낼 수 있어요.");
                return false;
            }
            chatBurstCount++;
            lastChatSentAt = Time.unscaledTime;
            TextChatRequested?.Invoke(message);
            return true;
        }

        private void AppendSystemNotice(string message)
        {
            if (Time.unscaledTime - lastChatNoticeAt < 1f) return;   // 연타해도 안내가 도배되지 않도록
            lastChatNoticeAt = Time.unscaledTime;
            AppendNetworkChat("안내", message);
        }

        private void SetChatButtonsInteractable(bool interactable)
        {
            if (chatSendButton != null) chatSendButton.interactable = interactable;
            for (int i = 0; quickChatButtons != null && i < quickChatButtons.Length; i++)
                if (quickChatButtons[i] != null) quickChatButtons[i].interactable = interactable;
        }

        public void SetRoomName(string value)
        {
            if (roomTitle != null)
                roomTitle.text = string.IsNullOrWhiteSpace(value) ? "이름 없는 방" : TrailerMode.DisplayName(value);   // 촬영방 키워드는 평범한 제목으로
        }

        public void SetSlot(int index, bool occupied, string nickname, bool isHost, bool isLocal, bool ready,
            int team, bool versusMode, string characterId, string outfitId)
        {
            if (slots != null && index >= 0 && index < slots.Length)
                slots[index]?.Apply(occupied, nickname, isHost, isLocal, ready, team, versusMode,
                    ResolveAvatar(index, occupied, characterId, outfitId));
        }

        private Sprite ResolveAvatar(int index, bool occupied, string characterId, string outfitId)
        {
            if (index < 0 || index >= avatarKeys.Length)
                return null;

            characterId ??= string.Empty;
            outfitId ??= string.Empty;
            string key = occupied ? characterId + "|" + outfitId : null;
            if (avatarKeys[index] == key)
                return avatarSprites[index];

            ReleaseAvatar(index);
            avatarKeys[index] = key;
            if (!occupied)
            {
                avatarStage?.SetBooth(index, false, null, null);
                return null;
            }

            if (avatarStage == null)
            {
                var stageObject = new GameObject("@UI_NEW_LobbyCharacterStage");
                avatarStage = stageObject.AddComponent<JobsnailLobbyCharacterStage>();
                avatarStage.EnsureBuilt();
            }

            avatarStage.SetBooth(index, true, characterId, outfitId);
            avatarSprites[index] = avatarStage.CaptureBoothSprite(index);
            avatarStage.SetActiveRendering(false);
            return avatarSprites[index];
        }

        private void ReleaseAvatar(int index)
        {
            Sprite sprite = avatarSprites[index];
            avatarSprites[index] = null;
            if (sprite == null)
                return;
            Texture2D texture = sprite.texture;
            Destroy(sprite);
            if (texture != null)
                Destroy(texture);
        }

        private void OnDestroy()
        {
            for (int i = 0; i < avatarSprites.Length; i++)
                ReleaseAvatar(i);
            if (avatarStage != null)
                Destroy(avatarStage.gameObject);
        }

        public void SetTeam(int team, bool versusMode, bool interactable)
        {
            bool enabled = versusMode && interactable;
            SetTeamButton(blueTeamButton, enabled && team == 0, new Color(0.72f, 0.84f, 1f), enabled);
            SetTeamButton(redTeamButton, enabled && team == 1, new Color(1f, 0.76f, 0.78f), enabled);
        }

        public void SetBestRecord(string value)
        {
            if (bestRecordValue != null)
                bestRecordValue.text = string.IsNullOrWhiteSpace(value) ? "없음" : value;
        }

        private Button mapPrevArrow, mapNextArrow;

        // 맵 썸네일 좌우 화살표(피그마 '맵 화살표' 18x56) — 드롭다운 없이 이전/다음 맵으로. 방장만 보인다.
        private void BuildMapArrows()
        {
            if (mapThumbnail == null) return;
            mapPrevArrow = MakeArrow("MapArrow_L", "UI_NEW/02_세션 화면/맵 화살표 왼쪽", new Vector2(0f, 0.5f), new Vector2(-24f, 0f), -1);
            mapNextArrow = MakeArrow("MapArrow_R", "UI_NEW/02_세션 화면/맵 화살표 오른쪽", new Vector2(1f, 0.5f), new Vector2(24f, 0f), +1);
        }

        private Button MakeArrow(string name, string spritePath, Vector2 anchor, Vector2 offset, int step)
        {
            var sprite = Resources.Load<Sprite>(spritePath);
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(mapThumbnail.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = offset;
            rt.sizeDelta = sprite != null ? new Vector2(sprite.rect.width, sprite.rect.height) : new Vector2(18f, 56f);
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.preserveAspect = true;
            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => { mapOptionsRoot?.SetActive(false); MapStepRequested?.Invoke(step); });
            JuicyButton.Attach(btn);
            return btn;
        }

        public void SetSettings(string mapName, string modeName, Sprite thumbnail, bool weatherOn, bool editable)
        {
            if (mapPrevArrow != null) mapPrevArrow.gameObject.SetActive(editable);
            if (mapNextArrow != null) mapNextArrow.gameObject.SetActive(editable);
            if (mapValue != null) mapValue.text = mapName ?? string.Empty;
            if (modeValue != null) modeValue.text = modeName ?? string.Empty;
            if (mapThumbnail != null)
            {
                mapThumbnail.sprite = thumbnail;
                mapThumbnail.preserveAspect = true;
                mapThumbnail.color = thumbnail != null
                    ? Color.white
                    : new Color(0.55f, 0.72f, 0.93f);
            }
            if (weatherImage != null)
                weatherImage.sprite = weatherOn ? weatherOnSprite : weatherOffSprite;
            if (mapSelector != null) mapSelector.interactable = editable;
            if (modeSelector != null) modeSelector.interactable = editable;
            if (weatherToggle != null) weatherToggle.interactable = editable;
            if (settingLockOverlays != null)
                foreach (GameObject overlay in settingLockOverlays)
                    overlay?.SetActive(!editable);
            if (!editable)
            {
                mapOptionsRoot?.SetActive(false);
                modeOptionsRoot?.SetActive(false);
            }
        }

        public void SetPrimaryAction(bool isHost, bool locallyReady, bool allReady, bool connected)
        {
            localIsHost = isHost;
            if (startButton == null) return;
            startButton.gameObject.SetActive(true);
            startButton.interactable = connected && (!isHost || allReady);
            if (startButton.image != null)
            {
                startButton.image.sprite = isHost ? hostStartSprite
                    : locallyReady ? memberReadyCompleteSprite : memberReadySprite;
                // 상태 색을 덧씌우지 않는다. 준비된 원본 버튼 스프라이트를 그대로 표시한다.
                startButton.image.color = Color.white;
            }
        }

        public void AppendNetworkChat(string nickname, string message)
        {
            bool wasAtBottom = chatScroll == null || chatScroll.verticalNormalizedPosition <= 0.05f;
            chatLines.Add($"{nickname} : {message}");
            if (chatLines.Count > 50) chatLines.RemoveAt(0);
            if (chatLog != null) chatLog.text = string.Join("\n", chatLines);
            if (wasAtBottom)
                StartCoroutine(ScrollToBottom());
        }

        public void ClearChat()
        {
            chatLines.Clear();
            if (chatLog != null) chatLog.text = string.Empty;
        }

        private void RequestLeave()
        {
            LeaveRequested?.Invoke();
            // 경고 팝업 문구("나갈 경우, 방이 삭제됩니다")는 방장에게만 해당한다.
            // 팀원은 방이 유지되므로 경고 없이 바로 퇴장한다.
            bool isHost = JobsnailSessionManager.Instance.ActiveSession?.IsHost ?? localIsHost;
            if (isHost)
            {
                router.Show(UiNewScreen.HostLeaveWarning);
                return;
            }
            transform.root.GetComponentInChildren<HostLeaveWarningPanel>(true)?.Confirm();
        }

        private void SendQuickChat(int index)
        {
            if (index < 0 || index >= QuickMessages.Length) return;
            if (!TryEmitChat(QuickMessages[index])) return;   // 빠른채팅도 같은 도배 제한을 받는다
            QuickChatRequested?.Invoke(index);
        }

        private void SendChat()
        {
            if (chatInput == null) return;
            string message = chatInput.text.Trim();
            if (message.Length == 0) return;
            if (!TryEmitChat(message.Length > 50 ? message.Substring(0, 50) : message)) return;
            chatInput.text = string.Empty;
        }

        // 옵션 버튼 순번 → 카탈로그 인덱스. 공터가 빠지므로 둘은 같지 않다.
        private readonly List<int> mapCatalogIndices = new();

        /// <summary>맵 선택지를 카탈로그로 다시 만든다(개수·라벨·매핑 전부). Awake에서 1회.
        /// MapRequested는 카탈로그 인덱스를 그대로 내보낸다 — LobbyRoomNetController.HostSelectMap이 그걸 기대한다.</summary>
        private void BuildMapOptions()
        {
            UiNewMapOptions.CollectSelectable(mapCatalogIndices);
            if (mapCatalogIndices.Count == 0) return;

            var buttons = UiNewMapOptions.FitPool(mapOptionButtons, mapCatalogIndices.Count);
            for (int i = 0; i < buttons.Length; i++)
            {
                int catalogIndex = mapCatalogIndices[i];
                UiNewMapOptions.SetLabel(buttons[i], UiNewMapOptions.LabelOf(catalogIndex));
                buttons[i].onClick.RemoveAllListeners();
                buttons[i].onClick.AddListener(() =>
                {
                    MapRequested?.Invoke(catalogIndex);
                    mapOptionsRoot?.SetActive(false);
                });
            }
            mapOptionButtons = buttons;
            UiNewDropdownList.Setup(mapOptionsRoot,
                mapSelector != null ? (RectTransform)mapSelector.transform : null);   // 스크롤 목록 조립(최대 4행 + 바깥 클릭 닫힘)
        }

        private static void BindOptions(Button[] options, Action<int> callback, GameObject root)
        {
            if (options == null) return;
            for (int i = 0; i < options.Length; i++)
            {
                int index = i;
                options[i]?.onClick.AddListener(() => { callback(index); root?.SetActive(false); });
            }
        }

        private static void ToggleOptions(GameObject target, GameObject other)
        {
            if (target == null) return;
            other?.SetActive(false);
            target.SetActive(!target.activeSelf);
        }

        private static void SetTeamButton(Button button, bool selected, Color selectedColor, bool interactable)
        {
            if (button == null) return;
            button.interactable = interactable;
            if (button.image != null)
                button.image.color = selected ? selectedColor : new Color(0.80f, 0.80f, 0.80f);
        }

        private IEnumerator ScrollToBottom()
        {
            yield return null;
            Canvas.ForceUpdateCanvases();
            if (chatScroll != null) chatScroll.verticalNormalizedPosition = 0f;
        }

        private void ConfigureChatScroll()
        {
            if (chatScroll == null) return;
            chatScroll.horizontal = false;
            chatScroll.vertical = true;
            chatScroll.movementType = ScrollRect.MovementType.Clamped;
            chatScroll.scrollSensitivity = 30f;
            if (chatScroll.viewport != null && chatScroll.viewport.TryGetComponent(out Image viewportImage))
                viewportImage.raycastTarget = true;
        }
    }
}
