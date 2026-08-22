using System;
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
        private int mapIndex;
        private int modeIndex;
        private bool weatherEnabled = true;
        private bool passwordVisible;

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
            BindOptions(mapOptionButtons, SelectMap, mapOptionsRoot);
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
            mapIndex = 0;
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

        private void SelectMap(int index)
        {
            mapIndex = Mathf.Clamp(index, 0, Mathf.Max(0, MapCount - 1));
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
            // 방 이름은 비워도 됨(기본 이름 사용) — 비밀방만 비밀번호 필수
            submitButton.interactable = visibility == RoomVisibility.Public || !string.IsNullOrWhiteSpace(passwordInput.text);
        }

        private void Submit()
        {
            if (!submitButton.interactable)
                return;

            string roomName = string.IsNullOrWhiteSpace(roomNameInput.text) ? defaultRoomName : roomNameInput.text.Trim();
            CreateRequested?.Invoke(new CreateRoomRequest(roomName, visibility, passwordInput.text, mapIndex, modeIndex, weatherEnabled));
        }

        public void CompleteCreation() => router.Show(UiNewScreen.Lobby);

        private static int MapCount => GridSystem.MapCatalog.Instance != null
            ? Mathf.Max(1, GridSystem.MapCatalog.Instance.Count) : MapFallbacks.Length;

        private static string GetMapLabel(int index)
        {
            if (GridSystem.MapCatalog.Instance != null)
            {
                GridSystem.MapDef definition = GridSystem.MapCatalog.Instance.Get(index);
                if (definition != null) return definition.DisplayName;
            }
            return MapFallbacks[Mathf.Clamp(index, 0, MapFallbacks.Length - 1)];
        }
    }
}
