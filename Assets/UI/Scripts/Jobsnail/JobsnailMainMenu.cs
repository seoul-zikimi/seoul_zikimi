using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed class JobsnailMainMenu : MonoBehaviour
{
    private InputField m_NicknameInput;
    private GameObject m_SettingsPopup;
    private JobsnailLobbyCharacterStage m_CharacterStage;
    private Sprite m_CharacterSprite;
    private static Font s_DefaultFont;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != SceneNames.BootstrapScene)
            return;

        // 최초 실행 = 인트로 컷씬(상경 연출 + 초기 캐릭터 선택) 먼저, 완료 후 메인 메뉴
        if (!SaveService.IntroSeen)
        {
            IntroCutscene.Show(Show);
            return;
        }

        Show();
    }

    public static void Show()
    {
        EnsureEventSystem();
        var canvas = JobsnailUiKit.EnsureOverlayCanvas("@JobsnailMainMenu", 500);
        if (canvas.GetComponent<JobsnailMainMenu>() == null)
            canvas.gameObject.AddComponent<JobsnailMainMenu>();
    }

    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null)
            return;

        var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        DontDestroyOnLoad(es);
    }

    private void Awake()
    {
        if (SoundManager.Instance != null)
            SoundManager.Instance.SetPhase(global::GamePhase.Lobby);
        Build();
    }

    private void Build()
    {
        var root = transform;

        for (int i = root.childCount - 1; i >= 0; i--)
            Destroy(root.GetChild(i).gameObject);

        JobsnailUiKit.CoverFill(JobsnailUiKit.Image("Main_BG", root, JobsnailUiKit.Sprite("UI_pngs/1.main/Main_BG")));   // 화면 꽉 채움(레터박스 X)

        var logo = JobsnailUiKit.Rect("Logo", root, new Vector2(0.05f, 0.70f), new Vector2(0.45f, 0.985f), Vector2.zero, Vector2.zero);   // 새 로고(건축레인저 : 서울) — 더 큼직하게
        var logoImage = logo.gameObject.AddComponent<Image>();
        logoImage.sprite = JobsnailUiKit.Sprite("UI_pngs/1.main/Logo");
        logoImage.preserveAspect = true;

        var snail = JobsnailUiKit.Rect("SnailPic", root, new Vector2(0.11f, 0.21f), new Vector2(0.43f, 0.68f), Vector2.zero, Vector2.zero);
        snail.offsetMin = new Vector2(-56f, -80f);   // 플레이 모드에서 잡은 확대 배치 그대로
        snail.offsetMax = new Vector2(56f, 80f);
        snail.localScale = Vector3.one * 2.2779f;
        var snailImage = snail.gameObject.AddComponent<Image>();
        snailImage.sprite = JobsnailUiKit.Sprite("UI_pngs/1.main/SnailPic");
        snailImage.preserveAspect = true;
        ApplySelectedCharacterPreview(snailImage);

        var nick = JobsnailUiKit.Rect("UserNicknameTextbox", root, new Vector2(0.14f, 0.08f), new Vector2(0.34f, 0.16f), Vector2.zero, Vector2.zero);
        var nickImage = nick.gameObject.AddComponent<Image>();
        nickImage.sprite = JobsnailUiKit.Sprite("UI_pngs/1.main/UserNicknameTextbox");
        nickImage.preserveAspect = true;
        m_NicknameInput = MakeInput(nick, "닉네임을 입력하세요", SaveService.Nickname);

        MakeMainButton(root, "GameStart_Btn", "UI_pngs/1.main/GameStart_Btn", "게임 시작",
            new Vector2(0.70f, 0.41f), new Vector2(0.88f, 0.49f), StartGame);

        MakeMainButton(root, "MyPage_Btn", "UI_pngs/1.main/MyPage_Btn", "마이페이지",
            new Vector2(0.70f, 0.31f), new Vector2(0.88f, 0.39f), OpenMyPage);

        MakeMainButton(root, "Settings_Btn", "UI_pngs/1.main/Settings_Btn", "설정",
            new Vector2(0.70f, 0.21f), new Vector2(0.88f, 0.29f), ToggleSettings);

        MakeMainButton(root, "QuitGame_Btn", "UI_pngs/1.main/QuitGame_Btn", "게임 종료",
            new Vector2(0.70f, 0.11f), new Vector2(0.88f, 0.19f), Quit);

        BuildSettingsPopup(root);
        JobsnailUiKit.ApplyFontPolicy(root);
        JuicyButton.AttachAll(gameObject);   // 메뉴·팝업 전 버튼 호버·프레스 쫀득
    }

    private void ApplySelectedCharacterPreview(Image target)
    {
        var stageObject = new GameObject("@JobsnailMainCharacterStage");
        m_CharacterStage = stageObject.AddComponent<JobsnailLobbyCharacterStage>();
        m_CharacterStage.EnsureBuilt();
        m_CharacterStage.SetBooth(0, true, SaveService.EquippedCharacter, SaveService.EquippedOutfit);
        m_CharacterSprite = m_CharacterStage.CaptureBoothSprite(0);
        m_CharacterStage.SetActiveRendering(false);
        if (m_CharacterSprite != null)
        {
            target.sprite = m_CharacterSprite;
            target.color = Color.white;
            target.preserveAspect = true;
        }
    }

    private void OnDestroy()
    {
        if (m_CharacterSprite != null)
        {
            Texture2D texture = m_CharacterSprite.texture;
            Destroy(m_CharacterSprite);
            if (texture != null)
                Destroy(texture);
        }
        if (m_CharacterStage != null)
            Destroy(m_CharacterStage.gameObject);
    }

    private void Update()
    {
        // ESC = 설정 팝업 닫기
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null && kb.escapeKey.wasPressedThisFrame && m_SettingsPopup != null && m_SettingsPopup.activeSelf)
            ToggleSettings();
    }

    private void OpenMyPage()
    {
        SceneManager.LoadScene(SceneNames.MyPage);   // 마이페이지 = 전용 씬(옷장 3D + HUD)
    }

    private void StartGame()
    {
        string nickname = m_NicknameInput != null ? m_NicknameInput.text.Trim() : "";
        if (string.IsNullOrEmpty(nickname))
            nickname = "달팽이";

        if (SaveService.Nickname != nickname)
            SaveService.Nickname = nickname;   // 변경 시 자동저장(Easy Save, PlayerPrefs 동시 기록)
        SceneManager.LoadScene(SceneNames.Lobby);
    }

    private static Button MakeMainButton(Transform root, string name, string spritePath, string label, Vector2 anchorMin, Vector2 anchorMax, UnityEngine.Events.UnityAction onClick)
    {
        Sprite sprite = JobsnailUiKit.Sprite(spritePath);
        var button = JobsnailUiKit.Button(name, root, sprite, anchorMin, anchorMax, Vector2.zero, Vector2.zero, onClick);
        var image = button.GetComponent<Image>();
        if (image != null)
        {
            image.color = sprite != null ? Color.white : new Color(1f, 0.76f, 0.42f, 1f);
            image.raycastTarget = true;
            image.preserveAspect = sprite != null;
        }

        if (sprite == null)
            MakeButtonText(button.transform, label, 18, Color.black);

        return button;
    }

    private void ToggleSettings()
    {
        if (m_SettingsPopup == null) return;
        bool show = !m_SettingsPopup.activeSelf;
        m_SettingsPopup.SetActive(show);
        if (!show)   // 닫을 때 볼륨 설정 저장 — SoundManager에 위임(없으면 폴백)
        {
            if (SoundManager.Instance != null) SoundManager.Instance.SaveVolumes();
            else PlayerPrefs.Save();
        }
    }

    private void BuildSettingsPopup(Transform root)
    {
        // 설정 영역만 새 톤으로 구성한다. 메인 메뉴의 사용자가 배치한 다른 UI는 건드리지 않는다.
        var overlay = JobsnailUiKit.Button("SettingsPopup", root, null, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, ToggleSettings);
        var overlayImage = overlay.GetComponent<Image>();
        if (overlayImage != null) overlayImage.color = new Color(0.10f, 0.07f, 0.05f, 0.55f);
        overlay.gameObject.AddComponent<NoJuicyButtonMotion>();
        var overlayMotion = overlay.GetComponent<JuicyButton>();
        if (overlayMotion != null)
        {
            overlayMotion.enabled = false;
            overlay.transform.localScale = Vector3.one;
        }
        m_SettingsPopup = overlay.gameObject;
        m_SettingsPopup.SetActive(false);

        var shadow = JobsnailUiKit.Box("PanelShadow", m_SettingsPopup.transform,
            new Vector2(0.315f, 0.16f), new Vector2(0.685f, 0.82f), new Vector2(10, -12), Vector2.zero,
            new Color(0.16f, 0.09f, 0.05f, 0.42f));
        StyleRounded(shadow, shadow.color);

        var panelImage = JobsnailUiKit.Box("Panel", m_SettingsPopup.transform,
            new Vector2(0.315f, 0.17f), new Vector2(0.685f, 0.83f), Vector2.zero, Vector2.zero,
            new Color(1f, 0.965f, 0.88f, 1f));
        StyleRounded(panelImage, panelImage.color);
        var panel = panelImage.transform;

        var header = JobsnailUiKit.Box("Header", panel, new Vector2(0.035f, 0.83f), new Vector2(0.965f, 0.965f),
            Vector2.zero, Vector2.zero, new Color(1f, 0.57f, 0.16f, 1f));
        StyleRounded(header, header.color);
        MakeText(header.transform, "환경 설정", 28, Color.white, Vector2.zero, Vector2.zero, TextAnchor.MiddleCenter);

        MakeText(panel, "소리와 조작 환경을 편하게 맞춰보세요", 17, new Color(0.40f, 0.31f, 0.25f, 1f),
            new Vector2(0, 208), new Vector2(520, 40), TextAnchor.MiddleCenter);

        var audioCardImage = JobsnailUiKit.Box("AudioCard", panel, new Vector2(0.06f, 0.53f), new Vector2(0.94f, 0.77f),
            Vector2.zero, Vector2.zero, new Color(1f, 0.91f, 0.75f, 0.72f));
        StyleRounded(audioCardImage, audioCardImage.color);
        var audioCard = audioCardImage.transform;
        MakeText(audioCard, "사운드", 18, new Color(0.31f, 0.22f, 0.17f, 1f),
            new Vector2(-225, 57), new Vector2(100, 32), TextAnchor.MiddleLeft);

        MakeVolumeSlider(audioCard, "BGM", new Vector2(0, 18), PlayerPrefs.GetFloat("BGMVolume", 0.8f), value =>
        {
            if (SoundManager.Instance != null) SoundManager.Instance.SetBGMVolume(value);
            else PlayerPrefs.SetFloat("BGMVolume", value);
        });
        MakeVolumeSlider(audioCard, "SFX", new Vector2(0, -32), PlayerPrefs.GetFloat("SFXVolume", 1f), value =>
        {
            if (SoundManager.Instance != null) SoundManager.Instance.SetSFXVolume(value);
            else PlayerPrefs.SetFloat("SFXVolume", value);
        });

        var keySettings = JobsnailUiKit.Button("KeySettingsButton", panel, null,
            new Vector2(0.06f, 0.385f), new Vector2(0.94f, 0.49f), Vector2.zero, Vector2.zero,
            OpenKeySettings);
        StyleFlatButton(keySettings, new Color(1f, 0.78f, 0.42f, 1f));
        MakeButtonText(keySettings.transform, "키 설정", 20, new Color(0.24f, 0.16f, 0.11f, 1f));

        var tutorialBtn = JobsnailUiKit.Button("TutorialReplayButton", panel, null,
            new Vector2(0.06f, 0.255f), new Vector2(0.94f, 0.36f), Vector2.zero, Vector2.zero,
            () => TutorialFlowController.ReplayTutorial());
        StyleFlatButton(tutorialBtn, new Color(0.82f, 0.88f, 0.86f, 1f));
        MakeButtonText(tutorialBtn.transform, "튜토리얼 다시 보기", 18, new Color(0.24f, 0.22f, 0.19f, 1f));

        var close = JobsnailUiKit.Button("SettingsCloseButton", panel, null,
            new Vector2(0.89f, 0.86f), new Vector2(0.95f, 0.935f), Vector2.zero, Vector2.zero, ToggleSettings);
        StyleFlatButton(close, new Color(1f, 0.91f, 0.76f, 0.96f));
        MakeButtonText(close.transform, "×", 24, new Color(0.35f, 0.22f, 0.14f, 1f));

        var leave = JobsnailUiKit.Button("SettingsLeaveButton", panel, null,
            new Vector2(0.06f, 0.075f), new Vector2(0.47f, 0.19f), Vector2.zero, Vector2.zero, Quit);
        StyleFlatButton(leave, new Color(0.92f, 0.76f, 0.70f, 1f));
        MakeButtonText(leave.transform, "게임 나가기", 18, new Color(0.38f, 0.18f, 0.14f, 1f));

        var done = JobsnailUiKit.Button("SettingsDoneButton", panel, null,
            new Vector2(0.53f, 0.075f), new Vector2(0.94f, 0.19f), Vector2.zero, Vector2.zero, ToggleSettings);
        StyleFlatButton(done, new Color(1f, 0.57f, 0.16f, 1f));
        MakeButtonText(done.transform, "완료", 19, Color.white);
    }

    private static void StyleRounded(Image image, Color color)
    {
        if (image == null) return;
        image.sprite = JobsnailUiKit.Sprite("UI_pngs/MyPage/RoundRect");
        image.type = Image.Type.Sliced;
        image.color = color;
    }

    private static void StyleFlatButton(Button button, Color color)
    {
        if (button == null) return;
        var image = button.GetComponent<Image>();
        StyleRounded(image, color);
        button.transition = Selectable.Transition.ColorTint;
        var colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1f, 0.94f, 0.82f, 1f);
        colors.pressedColor = new Color(0.88f, 0.82f, 0.72f, 1f);
        colors.selectedColor = colors.highlightedColor;
        colors.colorMultiplier = 1f;
        button.colors = colors;
    }

    private void OpenKeySettings()
    {
        // 두 팝업이 서로의 입력/정렬을 가리지 않도록 기존 설정 창을 먼저 닫는다.
        if (m_SettingsPopup != null)
            m_SettingsPopup.SetActive(false);

        KeyBindingPopup.Open();
    }

    private static void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private static InputField MakeInput(Transform parent, string placeholder, string value)
    {
        var go = new GameObject("NicknameInput", typeof(RectTransform), typeof(Image), typeof(InputField));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(0.08f, 0.12f);
        rt.anchorMax = new Vector2(0.92f, 0.88f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var bg = go.GetComponent<Image>();
        bg.color = new Color(1f, 1f, 1f, 0.02f);

        var text = MakeText(go.transform, value, 22, Color.white, Vector2.zero, Vector2.zero, TextAnchor.MiddleCenter);
        var ph = MakeText(go.transform, placeholder, 22, new Color(1f, 1f, 1f, 0.55f), Vector2.zero, Vector2.zero, TextAnchor.MiddleCenter);

        var input = go.GetComponent<InputField>();
        input.textComponent = text;
        input.placeholder = ph;
        input.text = value;
        input.characterLimit = 12;
        return input;
    }

    private static Text MakeText(Transform parent, string text, int size, Color color, Vector2 anchored, Vector2 sizeDelta, TextAnchor anchor, Color? background = null)
    {
        var go = new GameObject("Text", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = sizeDelta == Vector2.zero ? Vector2.zero : new Vector2(0.5f, 0.5f);
        rt.anchorMax = sizeDelta == Vector2.zero ? Vector2.one : new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchored;
        rt.sizeDelta = sizeDelta;

        GameObject textGo = go;
        if (background.HasValue)
        {
            var image = go.AddComponent<Image>();
            image.color = background.Value;
            image.raycastTarget = false;

            textGo = new GameObject("Label", typeof(RectTransform));
            textGo.transform.SetParent(go.transform, false);
            var textRt = (RectTransform)textGo.transform;
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;
        }

        var label = textGo.AddComponent<Text>();
        label.text = text;
        var font = GetDefaultFont();
        if (font != null)
            label.font = font;
        label.fontSize = size;
        label.color = color;
        label.alignment = anchor;
        return label;
    }

    private static void MakeButtonText(Transform button, string text, int size, Color color)
    {
        foreach (var old in button.GetComponentsInChildren<Text>(true))
            Destroy(old.gameObject);

        MakeText(button, text, size, color, Vector2.zero, Vector2.zero, TextAnchor.MiddleCenter);
    }

    private static Font GetDefaultFont()
    {
        if (s_DefaultFont != null)
            return s_DefaultFont;

        s_DefaultFont = JobsnailUiKit.LegacyFont;
        if (s_DefaultFont == null)
            s_DefaultFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (s_DefaultFont == null)
            s_DefaultFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
        if (s_DefaultFont == null)
            s_DefaultFont = Font.CreateDynamicFontFromOSFont("Apple SD Gothic Neo", 16);
        return s_DefaultFont;
    }

    private static void MakeVolumeSlider(Transform parent, string label, Vector2 anchored, float value, UnityEngine.Events.UnityAction<float> onChanged)
    {
        var row = new GameObject(label + "VolumeRow", typeof(RectTransform));
        row.transform.SetParent(parent, false);
        var rowRt = (RectTransform)row.transform;
        rowRt.anchorMin = new Vector2(0.5f, 0.5f);
        rowRt.anchorMax = new Vector2(0.5f, 0.5f);
        rowRt.anchoredPosition = anchored;
        rowRt.sizeDelta = new Vector2(300, 44);

        MakeText(row.transform, label, 16, Color.black, new Vector2(-115, 0), new Vector2(60, 36), TextAnchor.MiddleLeft);

        var sliderGo = new GameObject("Slider", typeof(RectTransform), typeof(Slider));
        sliderGo.transform.SetParent(row.transform, false);
        var rt = (RectTransform)sliderGo.transform;
        rt.anchorMin = new Vector2(0.35f, 0.25f);
        rt.anchorMax = new Vector2(0.95f, 0.75f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var background = JobsnailUiKit.Box("Background", sliderGo.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Color(0.82f, 0.82f, 0.82f, 1f));
        background.raycastTarget = true;

        var fillArea = new GameObject("Fill Area", typeof(RectTransform));
        fillArea.transform.SetParent(sliderGo.transform, false);
        var fillAreaRt = (RectTransform)fillArea.transform;
        fillAreaRt.anchorMin = Vector2.zero;
        fillAreaRt.anchorMax = Vector2.one;
        fillAreaRt.offsetMin = new Vector2(4, 4);
        fillAreaRt.offsetMax = new Vector2(-4, -4);

        var fill = JobsnailUiKit.Box("Fill", fillArea.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Color(1f, 0.72f, 0.36f, 1f));
        fill.raycastTarget = true;

        var handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
        handleArea.transform.SetParent(sliderGo.transform, false);
        var handleAreaRt = (RectTransform)handleArea.transform;
        handleAreaRt.anchorMin = Vector2.zero;
        handleAreaRt.anchorMax = Vector2.one;
        handleAreaRt.offsetMin = new Vector2(8, 0);
        handleAreaRt.offsetMax = new Vector2(-8, 0);

        var handle = JobsnailUiKit.Box("Handle", handleArea.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(20, 28), new Color(0.32f, 0.22f, 0.15f, 1f));
        handle.raycastTarget = true;

        var slider = sliderGo.GetComponent<Slider>();
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.value = Mathf.Clamp01(value);
        slider.fillRect = fill.rectTransform;
        slider.handleRect = handle.rectTransform;
        slider.targetGraphic = handle;
        slider.onValueChanged.AddListener(onChanged);
    }
}
