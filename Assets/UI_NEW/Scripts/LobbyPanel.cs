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

        private readonly List<string> chatLines = new();
        private readonly string[] avatarKeys = new string[LobbyRoomNet.RoomCapacity];
        private readonly Sprite[] avatarSprites = new Sprite[LobbyRoomNet.RoomCapacity];
        private JobsnailLobbyCharacterStage avatarStage;
        private bool localIsHost;

        public event Action LeaveRequested;
        public event Action ReadyRequested;
        public event Action StartRequested;
        public event Action<int> QuickChatRequested;
        public event Action<string> TextChatRequested;
        public event Action<int> TeamRequested;
        public event Action<int> MapRequested;
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
            chatInput?.onEndEdit.AddListener(_ =>
            {
                // 프로젝트가 신형 Input System 전용(activeInputHandler=1)이라 UnityEngine.Input은 예외를 던진다.
                var keyboard = UnityEngine.InputSystem.Keyboard.current;
                if (keyboard == null ||
                    (!keyboard.enterKey.wasPressedThisFrame && !keyboard.numpadEnterKey.wasPressedThisFrame))
                    return;
                SendChat();
                chatInput.ActivateInputField();   // 엔터 전송 후 포커스 유지 — 연속 입력
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
            BindOptions(mapOptionButtons, index => MapRequested?.Invoke(index), mapOptionsRoot);
            BindOptions(modeOptionButtons, index => ModeRequested?.Invoke(index), modeOptionsRoot);
        }

        private void OnEnable()
        {
            UiNewButtonVisualPolicy.Apply(transform);
            ClearChat();
            mapOptionsRoot?.SetActive(false);
            modeOptionsRoot?.SetActive(false);
            ConfigureChatScroll();
        }

        public void SetRoomName(string value)
        {
            if (roomTitle != null)
                roomTitle.text = string.IsNullOrWhiteSpace(value) ? "이름 없는 방" : value;
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

        public void SetSettings(string mapName, string modeName, Sprite thumbnail, bool weatherOn, bool editable)
        {
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
            router.Show(UiNewScreen.HostLeaveWarning);
        }

        private void SendQuickChat(int index)
        {
            if (index < 0 || index >= QuickMessages.Length) return;
            QuickChatRequested?.Invoke(index);
            TextChatRequested?.Invoke(QuickMessages[index]);
        }

        private void SendChat()
        {
            if (chatInput == null) return;
            string message = chatInput.text.Trim();
            if (message.Length == 0) return;
            TextChatRequested?.Invoke(message.Length > 50 ? message.Substring(0, 50) : message);
            chatInput.text = string.Empty;
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
