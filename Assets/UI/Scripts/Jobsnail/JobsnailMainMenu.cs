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

        // UI 리마스터: 비주얼은 전부 에디터 프리팹(00_MainScreen)에 있고, 여기서는 인스턴스화 + 로직 바인딩만 한다.
        var remaster = Resources.Load<GameObject>("UI_NEW/Prefabs/00_MainScreen");
        if (remaster != null)
        {
            BuildFromPrefab(root, remaster);
            return;
        }

        BuildLegacy(root);
    }

    private void BuildFromPrefab(Transform root, GameObject prefab)
    {
        // 새 배경 아트는 1920x1080 안전영역 밖 블리드(2432x1608)를 갖고 있다.
        // Expand여야 안전영역이 절대 잘리지 않고, 화면비에 따라 블리드가 자연스럽게 드러난다.
        var scaler = GetComponent<CanvasScaler>();
        if (scaler != null)
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

        var screen = Instantiate(prefab, root).transform;
        screen.name = prefab.name;

        BindButton(screen, "Button_GameStart", StartGame);
        BindButton(screen, "Button_MyPage", OpenMyPage);
        BindButton(screen, "Button_Settings", ToggleSettings);
        BindButton(screen, "Button_GameExit", Quit);

        var nickPanel = FindDeep(screen, "NicknamePanel");
        m_NicknameInput = nickPanel != null ? nickPanel.GetComponent<InputField>() : null;
        if (m_NicknameInput != null)
            m_NicknameInput.text = SaveService.Nickname;
        SyncNicknameLegacyKey();
        HookNicknameInput();

        var characterImage = FindDeep(screen, "CharacterImage");
        if (characterImage != null && characterImage.TryGetComponent(out Image target))
            ApplySelectedCharacterPreview(target);

        BuildSettingsPopup(root);
        JobsnailUiKit.ApplyFontPolicy(root);
        JuicyButton.AttachAll(gameObject);
    }

    private static void BindButton(Transform screen, string name, UnityEngine.Events.UnityAction onClick)
    {
        var found = FindDeep(screen, name);
        if (found == null || !found.TryGetComponent(out Button button))
        {
            Debug.LogError($"[JobsnailMainMenu] 프리팹에서 버튼을 못 찾음: {name}");
            return;
        }
        button.onClick.AddListener(() =>
        {
            if (SoundManager.Instance != null)
                SoundManager.Instance.PlaySFX(SFXType.UIClick);
        });
        button.onClick.AddListener(onClick);
    }

    private static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name)
            return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var found = FindDeep(root.GetChild(i), name);
            if (found != null)
                return found;
        }
        return null;
    }

    // 리마스터 프리팹이 없을 때만 쓰는 구버전 코드 생성 UI(폴백).
    private void BuildLegacy(Transform root)
    {
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
        SyncNicknameLegacyKey();
        HookNicknameInput();

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
        // 시연용 버튼(캐시 삭제·처음부터 시작)이 메뉴를 재구성(Build)할 수 있어, 이전 스테이지/스프라이트를 먼저 정리한다.
        if (m_CharacterStage != null) Destroy(m_CharacterStage.gameObject);
        if (m_CharacterSprite != null)
        {
            Texture2D old = m_CharacterSprite.texture;
            Destroy(m_CharacterSprite);
            if (old != null) Destroy(old);
            m_CharacterSprite = null;
        }

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

    // 편집 없이 바로 게임에 들어가는 경우까지 커버 — 메뉴 진입 시점에 정본(ES3)을
    // 인게임 판독처(PlayerPrefs "PlayerNickname")로 밀어 두 저장소를 항상 일치시킨다.
    private static void SyncNicknameLegacyKey()
    {
        string nick = SaveService.Nickname;
        if (!string.IsNullOrEmpty(nick) && PlayerPrefs.GetString("PlayerNickname", "") != nick)
        {
            PlayerPrefs.SetString("PlayerNickname", nick);
            PlayerPrefs.Save();
        }
    }

    // 닉네임은 "게임 시작"뿐 아니라 입력 확정(엔터·포커스 아웃)과 메뉴 종료 시점에도 저장한다.
    // 마이페이지·설정 등 다른 경로로 빠져나가도 세션·인게임이 최신 닉네임을 읽게 하기 위함.
    private void HookNicknameInput()
    {
        if (m_NicknameInput == null)
            return;
        m_NicknameInput.onEndEdit.AddListener(_ => CommitNickname());
    }

    private void CommitNickname()
    {
        if (m_NicknameInput == null)
            return;

        string nickname = m_NicknameInput.text.Trim();
        if (string.IsNullOrEmpty(nickname))
            return;   // 비워둔 상태는 저장하지 않고 기존 값 유지(기본값은 게임 시작 때 적용)

        // 항상 저장한다(같은 값이어도). 인게임 이름표(GameLoopManager 등 GridSystem 쪽)는
        // 어셈블리 방향 때문에 SaveService(ES3)가 아니라 PlayerPrefs를 읽는데,
        // "ES3와 같으면 스킵" 가드가 있으면 과거에 ES3만 갱신된 세이브에서 PlayerPrefs가
        // 영영 옛 이름으로 남는다 — "메인에서 바꿔도 인게임 미적용" QA의 원인.
        SaveService.Nickname = nickname;   // Easy Save + PlayerPrefs 동시 기록
    }

    private void OnDisable()
    {
        CommitNickname();
    }

    private void OpenMyPage()
    {
        CommitNickname();
        SceneManager.LoadScene(SceneNames.MyPage);   // 마이페이지 = 전용 씬(옷장 3D + HUD)
    }

    private void StartGame()
    {
        CommitNickname();
        if (string.IsNullOrEmpty(SaveService.Nickname))
            SaveService.Nickname = "달팽이";
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
        // 패널 안쪽 클릭(슬라이더 조작 등)이 오버레이 버튼까지 올라가 팝업이 닫히는 것을 막는다.
        panelImage.gameObject.AddComponent<JobsnailClickBlocker>();
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

        BuildDemoTools();
    }

    // ── 시연용 도구: 설정 패널 아래 분리된 스트립(캐시 삭제 / 코인 지급 / 인트로부터 시작) ──
    private Text m_DemoCoinLabel;

    private void BuildDemoTools()
    {
        var stripImage = JobsnailUiKit.Box("DemoTools", m_SettingsPopup.transform,
            new Vector2(0.315f, 0.082f), new Vector2(0.685f, 0.152f), Vector2.zero, Vector2.zero,
            new Color(1f, 0.965f, 0.88f, 0.96f));
        StyleRounded(stripImage, stripImage.color);
        var strip = stripImage.transform;

        var wipe = JobsnailUiKit.Button("DemoResetButton", strip, null,
            new Vector2(0.025f, 0.16f), new Vector2(0.32f, 0.84f), Vector2.zero, Vector2.zero, DemoResetSave);
        StyleFlatButton(wipe, new Color(0.92f, 0.76f, 0.70f, 1f));
        MakeButtonText(wipe.transform, "캐시 삭제", 16, new Color(0.38f, 0.18f, 0.14f, 1f));

        var coins = JobsnailUiKit.Button("DemoCoinsButton", strip, null,
            new Vector2(0.3525f, 0.16f), new Vector2(0.6475f, 0.84f), Vector2.zero, Vector2.zero, DemoGrantCoins);
        StyleFlatButton(coins, new Color(1f, 0.85f, 0.5f, 1f));
        MakeButtonText(coins.transform, "코인 +10000", 16, new Color(0.35f, 0.24f, 0.10f, 1f));

        var intro = JobsnailUiKit.Button("DemoIntroButton", strip, null,
            new Vector2(0.68f, 0.16f), new Vector2(0.975f, 0.84f), Vector2.zero, Vector2.zero, DemoPlayIntro);
        StyleFlatButton(intro, new Color(0.75f, 0.84f, 0.93f, 1f));
        MakeButtonText(intro.transform, "처음부터 시작", 16, new Color(0.16f, 0.26f, 0.38f, 1f));

        // 보유 코인 확인용 캡션 — 코인 지급이 실제로 됐는지 시연 중 바로 보이게
        m_DemoCoinLabel = MakeText(m_SettingsPopup.transform, "", 15, new Color(1f, 0.95f, 0.85f, 0.95f),
            new Vector2(0f, -474f), new Vector2(400f, 26f), TextAnchor.MiddleCenter);   // 스트립(중앙 기준 y -451..-376) 바로 아래
        m_DemoCoinLabel.raycastTarget = false;
        RefreshDemoCoinLabel();
    }

    private void RefreshDemoCoinLabel()
    {
        if (m_DemoCoinLabel != null)
            m_DemoCoinLabel.text = $"보유 코인 {SaveService.Coins:N0}";
    }

    private void DemoGrantCoins()
    {
        SaveService.AddCoins(10000);
        RefreshDemoCoinLabel();
    }

    private void DemoResetSave()
    {
        SaveService.ResetAll();
        Build();   // 닉네임·캐릭터 미리보기까지 첫 실행 상태로 재구성(팝업도 함께 새로 만들어짐)
    }

    private void DemoPlayIntro()
    {
        ToggleSettings();   // 팝업 닫기(볼륨 저장 포함)
        // 첫 실행과 동일한 흐름: 상경 스토리 → 초기 캐릭터 선택 → 완료 시 메뉴 재구성(선택 캐릭터 반영)
        IntroCutscene.Show(Build);
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
