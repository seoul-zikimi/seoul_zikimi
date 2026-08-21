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
            submitButton.interactable = !string.IsNullOrWhiteSpace(roomNameInput.text)
                && (visibility == RoomVisibility.Public || !string.IsNullOrWhiteSpace(passwordInput.text));
        }

        private void Submit()
        {
            if (!submitButton.interactable)
                return;

            CreateRequested?.Invoke(new CreateRoomRequest(roomNameInput.text.Trim(), visibility, passwordInput.text, mapIndex, modeIndex, weatherEnabled));
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
