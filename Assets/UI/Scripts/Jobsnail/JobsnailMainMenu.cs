using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed class JobsnailMainMenu : MonoBehaviour
{
    private InputField m_NicknameInput;
    private GameObject m_SettingsPopup;
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

        JobsnailUiKit.Image("Main_BG", root, JobsnailUiKit.Sprite("UI_pngs/1.main/Main_BG"));

        var logo = JobsnailUiKit.Rect("Logo", root, new Vector2(0.08f, 0.76f), new Vector2(0.38f, 0.96f), Vector2.zero, Vector2.zero);
        var logoImage = logo.gameObject.AddComponent<Image>();
        logoImage.sprite = JobsnailUiKit.Sprite("UI_pngs/1.main/Logo");
        logoImage.preserveAspect = true;

        var snail = JobsnailUiKit.Rect("SnailPic", root, new Vector2(0.11f, 0.21f), new Vector2(0.43f, 0.68f), Vector2.zero, Vector2.zero);
        var snailImage = snail.gameObject.AddComponent<Image>();
        snailImage.sprite = JobsnailUiKit.Sprite("UI_pngs/1.main/SnailPic");
        snailImage.preserveAspect = true;

        var nick = JobsnailUiKit.Rect("UserNicknameTextbox", root, new Vector2(0.14f, 0.08f), new Vector2(0.34f, 0.16f), Vector2.zero, Vector2.zero);
        var nickImage = nick.gameObject.AddComponent<Image>();
        nickImage.sprite = JobsnailUiKit.Sprite("UI_pngs/1.main/UserNicknameTextbox");
        nickImage.preserveAspect = true;
        m_NicknameInput = MakeInput(nick, "UserNickname", PlayerPrefs.GetString("PlayerNickname", "UserNickname"));

        MakeMainButton(root, "GameStart_Btn", "UI_pngs/1.main/GameStart_Btn", "게임 시작",
            new Vector2(0.70f, 0.31f), new Vector2(0.88f, 0.39f), StartGame);

        MakeMainButton(root, "Settings_Btn", "UI_pngs/1.main/Settings_Btn", "설정",
            new Vector2(0.70f, 0.21f), new Vector2(0.88f, 0.29f), ToggleSettings);

        MakeMainButton(root, "QuitGame_Btn", "UI_pngs/1.main/QuitGame_Btn", "게임 종료",
            new Vector2(0.70f, 0.11f), new Vector2(0.88f, 0.19f), Quit);

        BuildSettingsPopup(root);
    }

    private void StartGame()
    {
        string nickname = m_NicknameInput != null ? m_NicknameInput.text.Trim() : "";
        if (string.IsNullOrEmpty(nickname))
            nickname = "UserNickname";

        PlayerPrefs.SetString("PlayerNickname", nickname);
        PlayerPrefs.Save();
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
        if (m_SettingsPopup != null)
            m_SettingsPopup.SetActive(!m_SettingsPopup.activeSelf);
    }

    private void BuildSettingsPopup(Transform root)
    {
        m_SettingsPopup = JobsnailUiKit.Box("SettingsPopup", root, new Vector2(0.36f, 0.30f), new Vector2(0.64f, 0.72f), Vector2.zero, Vector2.zero, new Color(1f, 0.97f, 0.86f, 0.97f)).gameObject;
        m_SettingsPopup.SetActive(false);

        MakeText(m_SettingsPopup.transform, "설정", 28, Color.black, new Vector2(0, 150), new Vector2(320, 60), TextAnchor.MiddleCenter);
        MakeVolumeSlider(m_SettingsPopup.transform, "BGM", new Vector2(0, 70), PlayerPrefs.GetFloat("BGMVolume", 0.8f), value =>
        {
            if (SoundManager.Instance != null) SoundManager.Instance.SetBGMVolume(value);
            else PlayerPrefs.SetFloat("BGMVolume", value);
        });
        MakeVolumeSlider(m_SettingsPopup.transform, "SFX", new Vector2(0, 5), PlayerPrefs.GetFloat("SFXVolume", 1f), value =>
        {
            if (SoundManager.Instance != null) SoundManager.Instance.SetSFXVolume(value);
            else PlayerPrefs.SetFloat("SFXVolume", value);
        });
        var done = JobsnailUiKit.Button("SettingsDoneButton", m_SettingsPopup.transform, null, new Vector2(0.28f, 0.30f), new Vector2(0.72f, 0.40f), Vector2.zero, Vector2.zero, ToggleSettings);
        var doneImage = done.GetComponent<Image>();
        if (doneImage != null)
            doneImage.color = new Color(0.84f, 0.84f, 0.84f, 1f);
        MakeButtonText(done.transform, "완료", 20, Color.black);

        var close = JobsnailUiKit.Button("SettingsCloseButton", m_SettingsPopup.transform, null, new Vector2(0.86f, 0.86f), new Vector2(0.96f, 0.96f), Vector2.zero, Vector2.zero, ToggleSettings);
        MakeButtonText(close.transform, "×", 26, Color.black);

        var leave = JobsnailUiKit.Button("SettingsLeaveButton", m_SettingsPopup.transform, null, new Vector2(0.25f, 0.08f), new Vector2(0.75f, 0.20f), Vector2.zero, Vector2.zero, Quit);
        var image = leave.GetComponent<Image>();
        if (image != null)
            image.color = new Color(1f, 0.70f, 0.70f, 1f);
        MakeButtonText(leave.transform, "게임 나가기", 20, Color.black);

        var ok = JobsnailUiKit.Button("SettingsOkButton", m_SettingsPopup.transform, null, new Vector2(0.38f, 0.22f), new Vector2(0.62f, 0.28f), Vector2.zero, Vector2.zero, ToggleSettings);
        var okImage = ok.GetComponent<Image>();
        if (okImage != null)
            okImage.color = new Color(1f, 0.78f, 0.44f, 1f);
        MakeButtonText(ok.transform, "확인", 18, Color.black);
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

        if (background.HasValue)
        {
            var image = go.AddComponent<Image>();
            image.color = background.Value;
        }

        var label = go.AddComponent<Text>();
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

        s_DefaultFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (s_DefaultFont == null)
            s_DefaultFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
        if (s_DefaultFont == null)
            s_DefaultFont = Font.CreateDynamicFontFromOSFont("Apple SD Gothic Neo", 16);
        if (s_DefaultFont == null)
            s_DefaultFont = Font.CreateDynamicFontFromOSFont("Arial", 16);
        if (s_DefaultFont == null)
            s_DefaultFont = Font.CreateDynamicFontFromOSFont("Helvetica", 16);
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
