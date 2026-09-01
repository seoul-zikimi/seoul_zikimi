using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SeoulZikimi.UI.New
{
    public sealed class RoomCreationPanel : MonoBehaviour, IRoomCreationActions
    {
        [SerializeField] private UiNewScreenRouter router;
        [SerializeField] private InputField roomNameInput;
        [SerializeField] private InputField passwordInput;
        [SerializeField] private Button publicButton;
        [SerializeField] private Button privateButton;
        [SerializeField] private Button passwordVisibilityButton;
        [SerializeField] private Button mapButton;
        [SerializeField] private Button modeButton;
        [SerializeField] private Button weatherButton;
        [SerializeField] private Button submitButton;
        [SerializeField] private Image publicImage;
        [SerializeField] private Image privateImage;
        [SerializeField] private Image passwordVisibilityImage;
        [SerializeField] private Image weatherImage;
        [SerializeField] private Text mapValueLabel;
        [SerializeField] private Text modeValueLabel;
        [SerializeField] private Sprite publicSelected;
        [SerializeField] private Sprite publicUnselected;
        [SerializeField] private Sprite privateSelected;
        [SerializeField] private Sprite privateUnselected;
        [SerializeField] private Sprite passwordShown;
        [SerializeField] private Sprite passwordHidden;
        [SerializeField] private Sprite weatherOn;
        [SerializeField] private Sprite weatherOff;
        [SerializeField] private GameObject mapOptionsRoot;
        [SerializeField] private GameObject modeOptionsRoot;
        [SerializeField] private Button[] mapOptionButtons;
        [SerializeField] private Button[] modeOptionButtons;

        // 방 이름 비워두고 만들면 쓰는 기본 이름(플레이스홀더에도 표시) — 열 때마다 랜덤
        private static readonly string[] DefaultNameAdjectives = { "튼튼한", "성실한", "느긋한", "야무진", "든든한", "부지런한", "씩씩한", "꼼꼼한" };
        private static readonly string[] DefaultNameNouns = { "소라게", "달팽이", "거북이", "개미", "비버", "두더지", "딱따구리", "일개미" };
        private string defaultRoomName = "";

        private static readonly string[] MapFallbacks = { "(001) 광통교", "(002) 남산타워", "(003) 서울광장" };
        private static readonly string[] Modes = { "타임어택 모드", "대전 모드(아이템전)", "대전 모드", "자유 건축 모드" };

        private RoomVisibility visibility = RoomVisibility.Public;

        // 옵션 버튼 순번 → 카탈로그 인덱스. 공터가 빠지므로 둘은 같지 않다.
        private readonly List<int> mapCatalogIndices = new();

        private int mapIndex;   // 카탈로그 인덱스(CreateRoomRequest로 그대로 나간다)
        private int modeIndex;
        private bool weatherEnabled = true;
        private bool passwordVisible;
        private UiLoadingSpinner creatingSpinner;   // 방 생성 요청 중 표시(응답 오면 제거)

        public event Action<CreateRoomRequest> CreateRequested;

        private void Awake()
        {
            UiNewButtonVisualPolicy.Apply(transform);
            roomNameInput.characterLimit = 15;
            roomNameInput.onValueChanged.AddListener(_ => RefreshValidation());
            passwordInput.onValueChanged.AddListener(_ => RefreshValidation());
            publicButton.onClick.AddListener(() => SetVisibility(RoomVisibility.Public));
            privateButton.onClick.AddListener(() => SetVisibility(RoomVisibility.Private));
            passwordVisibilityButton.onClick.AddListener(TogglePasswordVisibility);
            mapButton.onClick.AddListener(() => ToggleOptions(mapOptionsRoot, modeOptionsRoot));
            modeButton.onClick.AddListener(() => ToggleOptions(modeOptionsRoot, mapOptionsRoot));
            BuildMapOptions();   // 맵 목록은 프리팹 고정이 아니라 카탈로그에서 만든다
            BindOptions(modeOptionButtons, SelectMode, modeOptionsRoot);
            weatherButton.onClick.AddListener(ToggleWeather);
            submitButton.onClick.AddListener(Submit);
            ResetForm();
        }

        private void OnEnable()
        {
            UiNewButtonVisualPolicy.Apply(transform);
            if (roomNameInput != null)
                ResetForm();
        }

        public void Close()
        {
            ResetForm();
            router.Show(UiNewScreen.RoomList);
        }

        private void ResetForm()
        {
            roomNameInput.text = string.Empty;
            defaultRoomName = $"{DefaultNameAdjectives[UnityEngine.Random.Range(0, DefaultNameAdjectives.Length)]} {DefaultNameNouns[UnityEngine.Random.Range(0, DefaultNameNouns.Length)]} 구합니다";
            if (roomNameInput.placeholder is Text placeholder) placeholder.text = defaultRoomName;
            passwordInput.text = string.Empty;
            mapIndex = DefaultMapIndex();   // 첫 실제 맵(목록 맨 앞의 '랜덤'은 기본값으로 쓰지 않는다)
            modeIndex = 0;
            weatherEnabled = true;
            passwordVisible = false;
            mapOptionsRoot?.SetActive(false);
            modeOptionsRoot?.SetActive(false);
            SetVisibility(RoomVisibility.Public);
            ApplySelectionVisuals();
        }

        private void SetVisibility(RoomVisibility value)
        {
            visibility = value;
            passwordInput.interactable = value == RoomVisibility.Private;
            publicImage.sprite = value == RoomVisibility.Public ? publicSelected : publicUnselected;
            privateImage.sprite = value == RoomVisibility.Private ? privateSelected : privateUnselected;
            RefreshValidation();
        }

        private void TogglePasswordVisibility()
        {
            passwordVisible = !passwordVisible;
            passwordInput.contentType = passwordVisible ? InputField.ContentType.Standard : InputField.ContentType.Password;
            passwordInput.ForceLabelUpdate();
            passwordVisibilityImage.sprite = passwordVisible ? passwordShown : passwordHidden;
        }

        /// <summary>맵 선택지를 카탈로그로 다시 만든다(개수·라벨·매핑 전부). Awake에서 1회.</summary>
        private void BuildMapOptions()
        {
            UiNewMapOptions.CollectSelectable(mapCatalogIndices);
            if (mapCatalogIndices.Count == 0)   // 카탈로그를 못 읽은 비상시에만 폴백 라벨
                for (int i = 0; i < MapFallbacks.Length; i++) mapCatalogIndices.Add(i);

            var buttons = UiNewMapOptions.FitPool(mapOptionButtons, mapCatalogIndices.Count);
            for (int i = 0; i < buttons.Length; i++)
            {
                int slot = i;
                UiNewMapOptions.SetLabel(buttons[i], GetMapLabel(mapCatalogIndices[i]));
                buttons[i].onClick.RemoveAllListeners();
                buttons[i].onClick.AddListener(() => { SelectMap(slot); mapOptionsRoot?.SetActive(false); });
            }
            mapOptionButtons = buttons;
        }

        // 인자는 '옵션 버튼 순번'이고, 실제로 들고 다니는 mapIndex는 카탈로그 인덱스다.
        private void SelectMap(int slot)
        {
            if (slot < 0 || slot >= mapCatalogIndices.Count) return;
            mapIndex = mapCatalogIndices[slot];
            ApplySelectionVisuals();
        }

        private void SelectMode(int index)
        {
            modeIndex = Mathf.Clamp(index, 0, Modes.Length - 1);
            ApplySelectionVisuals();
        }

        private static void ToggleOptions(GameObject target, GameObject other)
        {
            if (target == null) return;
            other?.SetActive(false);
            target.SetActive(!target.activeSelf);
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

        private void ToggleWeather()
        {
            weatherEnabled = !weatherEnabled;
            ApplySelectionVisuals();
        }

        private void ApplySelectionVisuals()
        {
            mapValueLabel.text = GetMapLabel(mapIndex);
            modeValueLabel.text = Modes[modeIndex];
            weatherImage.sprite = weatherEnabled ? weatherOn : weatherOff;
            passwordVisibilityImage.sprite = passwordVisible ? passwordShown : passwordHidden;
            RefreshValidation();
        }

        private void RefreshValidation()
        {
            // 방 이름은 비워도 됨(기본 이름 사용) — 비밀방만 비밀번호 필수. 생성 요청 중엔 잠금 유지.
            submitButton.interactable = creatingSpinner == null
                && (visibility == RoomVisibility.Public || !string.IsNullOrWhiteSpace(passwordInput.text));
        }

        private void Submit()
        {
            if (!submitButton.interactable || creatingSpinner != null)
                return;

            // 응답이 올 때까지 버튼 가운데 스피너 표시 + 버튼 잠금(중복 생성 방지)
            creatingSpinner = UiLoadingSpinner.AttachBeside((RectTransform)submitButton.transform, Vector2.zero);
            RefreshValidation();

            string roomName = string.IsNullOrWhiteSpace(roomNameInput.text) ? defaultRoomName : roomNameInput.text.Trim();
            CreateRequested?.Invoke(new CreateRoomRequest(roomName, visibility, passwordInput.text, mapIndex, modeIndex, weatherEnabled));
        }

        public void CompleteCreation()
        {
            ClearCreationPending();
            router.Show(UiNewScreen.Lobby);
        }

        /// <summary>방 생성 실패 시 컨트롤러가 호출 — 스피너 제거 + 버튼 재활성.</summary>
        public void FailCreation() => ClearCreationPending();

        private void ClearCreationPending()
        {
            if (creatingSpinner != null)
            {
                creatingSpinner.Detach();
                creatingSpinner = null;
            }
            RefreshValidation();
        }

        private void OnDisable() => ClearCreationPending();

        /// <summary>방을 새로 만들 때의 기본 맵 — 목록 맨 앞은 '랜덤'이므로 그 다음(첫 실제 맵)을 고른다.</summary>
        private int DefaultMapIndex()
        {
            for (int i = 0; i < mapCatalogIndices.Count; i++)
                if (mapCatalogIndices[i] != GridSystem.MapCatalog.RandomMapIndex) return mapCatalogIndices[i];
            return 0;
        }

        private static string GetMapLabel(int index)
        {
            if (index == GridSystem.MapCatalog.RandomMapIndex) return UiNewMapOptions.RandomLabel;
            if (GridSystem.MapCatalog.Instance != null)
            {
                GridSystem.MapDef definition = GridSystem.MapCatalog.Instance.Get(index);
                if (definition != null) return definition.DisplayName;
            }
            return MapFallbacks[Mathf.Clamp(index, 0, MapFallbacks.Length - 1)];
        }
    }
}
