using System.Collections;
using GridSystem;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 인게임 루프 HUD: 타이머 · 종료동의 · 설정 팝업 · 정산서 · 시작 배너.
/// 비주얼은 Resources/UI/HUD/GameLoopHUD 프리팹(UIBase 규칙 — 코드는 바인딩+상태 갱신만).
/// 프리팹 생성/수정: Jobsnail ▸ UI ▸ Generate GameLoopHud Prefab (이후 에디터에서 자유 편집).
/// GameScene 로드 시 아래 Bootstrap이 UIManager.ShowHUDUI로 표시.
/// </summary>
public sealed class GameLoopHUD : UIHUD
{
    private enum GOs { TopBar, EndRequestCluster, InGameSettingsPopup, ResultPanel, StartBanner, StarRow }
    private enum Texts { Timer, Players, Structure, Time, Score, Grade, EventToast, CoinReward }
    private enum Imgs { P0, P1, P2, P3, GradeStar0, GradeStar1, GradeStar2, GradeStamp }
    private enum Raws { ResultImage }
    private enum Btns { EndRequestButton, SettingsIconButton, SettingsCloseButton, ExitGameButton, RoomButton, LeaveButton, CraneToggleButton }
    private enum Slds { BGMSlider, SFXSlider, SensSlider }

    private GameLoopManager m_Loop;
    private AnswerPreview m_AnswerPreview;
    private TextMeshProUGUI m_TimerText, m_ResultScoreText, m_ResultNamesText, m_ResultStructText, m_ResultTimeText, m_ResultGradeText, m_CoinRewardText;
    private Image m_ResultGradeImage;
    private Image[] m_ResultStars;
    private GameObject m_StarRow;
    private static readonly Color kStarGold = Color.white;                       // 채운 별(스프라이트 원색=금색)
    private static readonly Color kStarDim = new Color(0.55f, 0.55f, 0.55f, 0.18f); // 빈 별(아주 옅게 — 글씨 안 가리게)
    private Image[] m_PeopleIcons;
    private RawImage m_ResultImage;
    private GameObject m_TopBar, m_ConsentBar, m_ResultPanel, m_SettingsPopup, m_StartBanner;
    private Button m_SettingsButton, m_EndRequestButton;
    private bool m_ResultDismissed, m_ResultWasShown, m_ResultIntroPlaying, m_UrgentBgmStarted;
    private GridSystem.GamePhase m_PrevPhase = (GridSystem.GamePhase)(-1);
    private Coroutine m_BannerCo, m_StarBobCo;
    private GridNetwork m_Net;
    private int m_LastTimerSecs = -1;  // 초 변화 감지(타이머 톡)
    private float m_TimerTick;         // 초 넘김 팝 감쇠값
    private bool m_CraneViewing;      // true = 정산서 숨기고 크레인샷 보는 중
    private Button m_CraneToggleBtn;  // 정산서↔크레인샷 토글(프리팹 바인딩, 정산 중에만 표시)
    private Sprite m_EndBaked, m_EndBlank;   // 종료 요청 버튼 스프라이트(텍스트 구움 / 빈 흰색)

    // ── 부트스트랩: GameScene 진입 시 프리팹 HUD 표시 ──
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != SceneNames.GameScene)
            return;

        if (EventSystem.current == null)
        {
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            DontDestroyOnLoad(es);
        }
        if (UIManager.Instance == null)
            new GameObject("UIManager").AddComponent<UIManager>();

        if (Resources.Load<GameObject>("UI/HUD/GameLoopHUD") == null)
        {
            Debug.LogWarning("[GameLoopHUD] 프리팹 없음 — 메뉴 Jobsnail ▸ UI ▸ Generate GameLoopHud Prefab 실행하세요.");
            return;
        }
        UIManager.Instance.ShowHUDUI<GameLoopHUD>();
        if (Resources.Load<GameObject>("UI/HUD/ControlsTooltipHUD") != null)   // 좌상단 조작법 툴팁(접기/펴기)
            UIManager.Instance.ShowHUDUI<ControlsTooltipHUD>();
    }

    public override void Init()
    {
        Bind<GameObject>(typeof(GOs));
        Bind<TextMeshProUGUI>(typeof(Texts));
        Bind<Image>(typeof(Imgs));
        Bind<RawImage>(typeof(Raws));
        Bind<Button>(typeof(Btns));
        Bind<Slider>(typeof(Slds));

        m_TopBar = Get<GameObject>((int)GOs.TopBar);
        m_ConsentBar = Get<GameObject>((int)GOs.EndRequestCluster);
        m_SettingsPopup = Get<GameObject>((int)GOs.InGameSettingsPopup);
        m_ResultPanel = Get<GameObject>((int)GOs.ResultPanel);
        m_StartBanner = Get<GameObject>((int)GOs.StartBanner);

        m_TimerText = Get<TextMeshProUGUI>((int)Texts.Timer);
        m_ResultNamesText = Get<TextMeshProUGUI>((int)Texts.Players);
        m_ResultStructText = Get<TextMeshProUGUI>((int)Texts.Structure);
        m_ResultTimeText = Get<TextMeshProUGUI>((int)Texts.Time);
        m_ResultScoreText = Get<TextMeshProUGUI>((int)Texts.Score);
        m_ResultGradeText = Get<TextMeshProUGUI>((int)Texts.Grade);

        m_PeopleIcons = new[] { Get<Image>((int)Imgs.P0), Get<Image>((int)Imgs.P1), Get<Image>((int)Imgs.P2), Get<Image>((int)Imgs.P3) };
        m_ResultStars = new[] { Get<Image>((int)Imgs.GradeStar0), Get<Image>((int)Imgs.GradeStar1), Get<Image>((int)Imgs.GradeStar2) };
        m_StarRow = Get<GameObject>((int)GOs.StarRow);
        m_ResultGradeImage = Get<Image>((int)Imgs.GradeStamp);
        m_ResultImage = Get<RawImage>((int)Raws.ResultImage);

        m_EndRequestButton = Get<Button>((int)Btns.EndRequestButton);
        m_SettingsButton = Get<Button>((int)Btns.SettingsIconButton);
        m_CraneToggleBtn = Get<Button>((int)Btns.CraneToggleButton);
        m_ToastText = Get<TextMeshProUGUI>((int)Texts.EventToast);
        m_CoinRewardText = Get<TextMeshProUGUI>((int)Texts.CoinReward);
        if (m_CoinRewardText != null) m_CoinRewardText.text = "";
        m_Toast = m_ToastText != null ? m_ToastText.gameObject : null;
        if (m_Toast != null) m_Toast.SetActive(false);
        if (m_CraneToggleBtn != null) m_CraneToggleBtn.gameObject.SetActive(false);

        // 프리팹엔 onClick이 저장 안 되므로 여기서 전부 배선(클릭음 포함).
        Wire(Btns.EndRequestButton, OnEndRequest);
        Wire(Btns.SettingsIconButton, ToggleSettingsPopup);
        Wire(Btns.SettingsCloseButton, ToggleSettingsPopup);
        Wire(Btns.ExitGameButton, async () => await JobsnailSessionManager.Instance.LeaveLobbyRoomSecurelyAsync());
        Wire(Btns.RoomButton, () => { if (m_Loop != null) m_Loop.RequestReturnToRoom(); });
        Wire(Btns.LeaveButton, async () => await JobsnailSessionManager.Instance.LeaveLobbyRoomSecurelyAsync());
        Wire(Btns.CraneToggleButton, () => m_CraneViewing = !m_CraneViewing);

        WireSlider(Slds.BGMSlider, PlayerPrefs.GetFloat("BGMVolume", 0.8f), v =>
        {
            if (SoundManager.Instance != null) SoundManager.Instance.SetBGMVolume(v);
            else PlayerPrefs.SetFloat("BGMVolume", v);
        });
        WireSlider(Slds.SFXSlider, PlayerPrefs.GetFloat("SFXVolume", 1f), v =>
        {
            if (SoundManager.Instance != null) SoundManager.Instance.SetSFXVolume(v);
            else PlayerPrefs.SetFloat("SFXVolume", v);
        });
        WireSlider(Slds.SensSlider, PlayerPrefs.GetFloat("MouseSensitivity", 0.5f),
            v => Player.PlayerCameraController.SetSensitivity01(v));

        if (m_SettingsPopup != null) m_SettingsPopup.SetActive(false);
        if (m_ResultPanel != null) m_ResultPanel.SetActive(false);
        if (m_StartBanner != null) m_StartBanner.SetActive(false);

        // 텍스트 애니메이터: 큰 순간 텍스트만 예쁘게(글자별 물결·흔들)
        AddJuicyText(m_ResultGradeText, 5f, 4.5f, 0.45f, 8f);                 // 등급(EXCELLENT! 등)
        AddJuicyText(m_StartBanner != null ? m_StartBanner.GetComponent<TextMeshProUGUI>() : null, 6f, 5f, 0.4f, 6f); // 배너(완성!!/공사 시작!)
        AddJuicyText(m_ToastText, 3.5f, 4f, 0.5f, 5f);                        // 돌파 토스트

        m_Loop = null;
        m_UrgentBgmStarted = false;
        m_PrevPhase = (GridSystem.GamePhase)(-1);

        if (SoundManager.Instance != null)
            SoundManager.Instance.SetPhase(global::GamePhase.Building);
    }

    // 텍스트 애니메이터 부착(중복 방지) — 큰 순간 TMP만 글자별 물결·흔들
    private static void AddJuicyText(TextMeshProUGUI txt, float amp, float freq, float phase, float rot)
    {
        if (txt == null) return;
        var jt = txt.GetComponent<JuicyText>();
        if (jt == null) jt = txt.gameObject.AddComponent<JuicyText>();
        jt.Configure(amp, freq, phase, rot);
    }

    private void Wire(Btns which, UnityEngine.Events.UnityAction action)
    {
        var btn = Get<Button>((int)which);
        if (btn == null) return;
        btn.onClick.RemoveAllListeners();
        btn.onClick.AddListener(() => { if (SoundManager.Instance != null) SoundManager.Instance.PlaySFX(SFXType.UIClick); });
        btn.onClick.AddListener(action);
    }

    private void WireSlider(Slds which, float initial, UnityEngine.Events.UnityAction<float> onChanged)
    {
        var s = Get<Slider>((int)which);
        if (s == null) return;
        s.onValueChanged.RemoveAllListeners();
        s.value = Mathf.Clamp01(initial);
        s.onValueChanged.AddListener(onChanged);
    }

    private void Update()
    {
        if (m_Loop == null)
            m_Loop = FindFirstObjectByType<GameLoopManager>();

        bool ready = m_Loop != null && m_Loop.IsSpawned;
        // 시간제한이 없는 모드(자유 모드/튜토리얼)는 TimeLeft가 사실상 무한대라 숫자가 의미 없다 — 타이머 박스(베이지 배경 포함) 자체를 숨긴다.
        bool timeLimited = ready && m_Loop.ModeDef.TimeLimitPolicy != SeoulZikimi.Gameplay.TimeLimitPolicy.Unlimited;
        SetVisible(ready, timeLimited);
        if (!ready)
            return;

        var phase = m_Loop.Phase;   // 빌딩 페이즈 진입 순간 "공사 시작!" 배너 슬램
        if (phase != m_PrevPhase)
        {
            if (phase == GridSystem.GamePhase.Building)
            {
                ShowStartBanner();
                m_LastMilestone = 0;
                m_PlayersAtStart = Mathf.Clamp(m_Loop.NameCount, 1, 4);   // 기록용 인원수 = 시작 시점 팀원 수(중도 이탈 무관)
            }
            m_PrevPhase = phase;
        }

        if (m_Loop.IsBuilding)
        {
            if (m_Loop.TimeLeft <= 0f)
            {
                m_Loop.RequestFinishByTimeout();
                return;
            }

            if (m_Loop.TimeLeft > 60f)
                m_UrgentBgmStarted = false;

            if (!m_UrgentBgmStarted && m_Loop.TimeLeft <= 60f)
            {
                m_UrgentBgmStarted = true;
                if (SoundManager.Instance != null)
                    SoundManager.Instance.SetPhase(global::GamePhase.BuildingUrgent);
            }
        }

        int secs = Mathf.CeilToInt(m_Loop.TimeLeft);
        if (m_TimerText != null && timeLimited)
        {
            string timer = m_Loop.IsBuilding ? $"{secs / 60} : {secs % 60:00}" : "종료";
            // 2vs2 건축 중: 타이머 밑에 양 팀 완성도 실시간 표시
            if (m_Loop.IsVersus && m_Loop.IsBuilding)
            {
                if (m_Net == null) m_Net = FindFirstObjectByType<GridNetwork>();
                if (m_Net != null)
                {
                    int my = Mathf.Max(0, m_Loop.LocalTeam);
                    int a = Mathf.RoundToInt(m_Net.ScoreFor(my).Percent);
                    int b = Mathf.RoundToInt(m_Net.ScoreFor(1 - my).Percent);
                    timer += $"\n<size=60%>우리 {a}% : 상대 {b}%</size>";
                }
                // 소지 아이템 안내(F로 사용)
                var items = m_Loop.GetComponent<GridSystem.ItemNetwork>();
                string held = items != null ? items.LocalHeldName() : "";
                if (!string.IsNullOrEmpty(held))
                    timer += $"\n<size=55%>[{held}] E로 사용</size>";
                string status = GridSystem.ItemNetwork.LocalStatusLine();
                if (!string.IsNullOrEmpty(status))
                    timer += $"\n<size=55%>{status}</size>";
            }
            m_TimerText.text = timer;

            // 막판 30초: 타이머 빨갛게 + 두근두근 펄스 + 화면 가장자리 빨간 비네트
            if (m_Loop.IsBuilding && m_Loop.TimeLeft <= 30f)
            {
                float beat = Mathf.Abs(Mathf.Sin(Time.unscaledTime * 5f));
                m_TimerText.rectTransform.localScale = Vector3.one * (1f + 0.14f * beat);
                m_TimerText.color = Color.Lerp(new Color(1f, 0.20f, 0.16f, 1f), Color.white, beat * 0.35f);
                EnsureVignette();
                if (m_Vignette != null) m_Vignette.intensity.Override(0.16f + 0.14f * beat);
            }
            else
            {
                if (m_Loop.IsBuilding && secs != m_LastTimerSecs) m_TimerTick = 1f;   // 초 넘어갈 때 톡
                m_TimerTick = Mathf.Max(0f, m_TimerTick - Time.unscaledDeltaTime * 6f);
                m_TimerText.rectTransform.localScale = Vector3.one * (1f + 0.12f * m_TimerTick);
                m_TimerText.color = m_Loop.IsBuilding && m_Loop.TimeLeft <= 60f ? new Color(1f, 0.28f, 0.22f, 1f) : Color.white;   // 1분 미만 빨강(기획서 3.2)
                if (m_Vignette != null) m_Vignette.intensity.Override(0f);
            }
            m_LastTimerSecs = secs;
        }

        SetCrane(!m_Loop.IsBuilding);       // 정산 중 = 건축물 한 바퀴 크레인샷
        UpdateMilestoneToast();             // 완성도 25/50/75/90% 돌파 토스트

        // 정확히 100%(만점) 완공일 때만 폭죽 멈춤없이. 반올림(99.6→100) 오발화 방지.
        if (IsComplete()) StartResultFireworks();
        else StopResultFireworks();

        UpdateResultPanel();

        UpdateEndRequestButton();
        if (m_PeopleIcons != null)
            for (int i = 0; i < m_PeopleIcons.Length; i++)
            {
                if (m_PeopleIcons[i] == null) continue;
                bool connected = i < m_Loop.PlayerCount;
                m_PeopleIcons[i].gameObject.SetActive(connected);
                if (connected)   // 동의=검정(채움) / 미동의=흐린(빈칸)
                    m_PeopleIcons[i].color = i < m_Loop.ConsentCount ? Color.black : new Color(0f, 0f, 0f, 0.28f);
            }
    }

    // 종료 요청 버튼 상태: 기본(건축 중·미동의) = '조기 종료 요청 (ENTER)' 구워진 스프라이트,
    // 그 외(동의 취소 / 재시작) = 텍스트 지운 흰 버튼 스프라이트에 틴트 + TMP 라벨.
    private void UpdateEndRequestButton()
    {
        if (m_EndRequestButton == null) return;
        if (m_EndBaked == null) m_EndBaked = InGameUiSkin.Load("EndRequestButton");
        if (m_EndBlank == null) m_EndBlank = InGameUiSkin.Load("EndRequestButton_Blank");
        var img = m_EndRequestButton.targetGraphic as Image;
        var lblT = m_EndRequestButton.transform.Find("Label");
        var lbl = lblT != null ? lblT.GetComponent<TextMeshProUGUI>() : m_EndRequestButton.GetComponentInChildren<TextMeshProUGUI>(true);
        bool consent = m_Loop.HasLocalConsent;
        bool baked = m_EndBaked != null && m_EndBlank != null && img != null && m_Loop.IsBuilding && !consent;
        if (baked)
        {
            img.sprite = m_EndBaked; img.color = Color.white;
            if (lbl != null && lbl.gameObject.activeSelf) lbl.gameObject.SetActive(false);
            return;
        }
        string text = (consent ? "동의 취소" : m_Loop.IsBuilding ? "종료 요청" : "재시작") + "\n<size=70%>(ENTER)</size>";
        if (img != null && m_EndBlank != null)
        {
            img.sprite = m_EndBlank;
            img.color = consent ? InGameUiSkin.Consent : InGameUiSkin.Orange;
        }
        else SetButtonColor(m_EndRequestButton, consent ? InGameUiSkin.Consent : new Color(1f, 0.78f, 0.44f, 1f));
        if (lbl != null)
        {
            if (!lbl.gameObject.activeSelf) lbl.gameObject.SetActive(true);
            if (lbl.text != text) lbl.text = text;
        }
    }

    private void SetVisible(bool visible, bool timeLimited)
    {
        if (m_TopBar != null) m_TopBar.SetActive(visible && timeLimited);   // TopBar = 타이머 베이지 박스 자체(자식이 Timer 텍스트뿐)
        if (m_ConsentBar != null) m_ConsentBar.SetActive(visible);
        if (m_SettingsButton != null) m_SettingsButton.gameObject.SetActive(visible);
        if (!visible)
        {
            if (m_ResultPanel != null) m_ResultPanel.SetActive(false);
            if (m_SettingsPopup != null) m_SettingsPopup.SetActive(false);
            if (m_CraneToggleBtn != null) m_CraneToggleBtn.gameObject.SetActive(false);
            if (m_Toast != null) m_Toast.SetActive(false);
        }
    }

    // ── 중앙 배너(공사 시작 / 완성 등): 팝인 → 잠깐 → 축소 퇴장 ──
    private void ShowStartBanner() => ShowBanner("공사 시작!", new Color(1f, 0.72f, 0.20f, 1f));

    private void ShowBanner(string text, Color color)
    {
        if (m_StartBanner == null) return;
        var lbl = m_StartBanner.GetComponent<TextMeshProUGUI>();
        if (lbl != null) { lbl.text = text; lbl.color = color; }
        if (m_BannerCo != null) StopCoroutine(m_BannerCo);
        m_BannerCo = StartCoroutine(StartBannerCo());
    }

    private IEnumerator StartBannerCo()
    {
        m_StartBanner.SetActive(false);   // UiPopIn 재발동용 토글
        m_StartBanner.SetActive(true);
        m_StartBanner.transform.SetAsLastSibling();
        yield return new WaitForSecondsRealtime(1.4f);

        var t = m_StartBanner.transform;   // 스륵 축소 퇴장
        for (float e = 0f; e < 0.15f && m_StartBanner != null; e += Time.unscaledDeltaTime)
        {
            t.localScale = Vector3.one * Mathf.Lerp(1f, 0f, e / 0.15f);
            yield return null;
        }
        if (m_StartBanner != null) { m_StartBanner.SetActive(false); t.localScale = Vector3.one; }
        m_BannerCo = null;
    }

    private void UpdateResultPanel()
    {
        if (m_ResultPanel == null || m_Loop == null)
            return;

        if (m_Loop.IsBuilding) m_ResultDismissed = false;   // 새 라운드 → 다음 종료 때 결과창 다시 표시
        bool resultPhase = !m_Loop.IsBuilding && !m_ResultDismissed;
        UpdateCraneToggle(resultPhase);                     // 정산 중에만 "크레인샷 보기" 버튼 표시
        bool show = resultPhase && !m_CraneViewing;         // 크레인샷 보는 중엔 정산서 숨김
        m_ResultPanel.SetActive(show);
        if (!resultPhase)
        {
            if (m_ResultWasShown && m_Net != null) m_Net.EndResultPreview();   // 썸네일 라이브 카메라 끄기
            m_ResultWasShown = false;   // 다시 숨김 → 다음 표시 때 인트로 연출 재생
            m_CraneViewing = false;
        }
        if (!show)
            return;

        // 2vs2: 점수는 '내 팀' 기준으로 표시
        if (m_Net == null) m_Net = FindFirstObjectByType<GridNetwork>();
        bool versus = m_Loop.IsVersus;
        int myTeam = Mathf.Max(0, m_Loop.LocalTeam);
        var score = (versus && m_Net != null) ? m_Net.ScoreFor(myTeam) : m_Loop.Score;
        int pct = Mathf.RoundToInt(score.Percent);

        bool firstShow = !m_ResultWasShown;
        m_ResultWasShown = true;
        if (firstShow)
        {
            transform.SetAsLastSibling();   // 정산서를 주문·힌트 등 다른 HUD보다 앞으로
            m_ResultIntroPlaying = true;
            StartCoroutine(ResultIntro(pct));

            // ── 저장(Easy Save): 맵별×인원수별 최고기록 갱신 + 타임어택 코인 지급 ──
            string map = !string.IsNullOrEmpty(m_Loop.AnswerName) ? m_Loop.AnswerName
                       : UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            int players = m_PlayersAtStart > 0 ? m_PlayersAtStart : Mathf.Clamp(m_Loop.NameCount, 1, 4);
            bool newBest = SaveService.ReportRecord(map, players, pct, m_Loop.Elapsed);
            int coins = SaveService.TimeAttackReward(pct, pct > 0 ? StarCount(pct) : 0);
            if (coins > 0) SaveService.AddCoins(coins);
            if (m_CoinRewardText != null)
                m_CoinRewardText.text = (newBest ? "신기록!  " : "") + $"+{coins}코인  (보유 {SaveService.Coins}코인)";

            // 2vs2: 내 팀 기준 승/패를 맵별 전적에 기록(무승부 제외). 키는 로비 전적 표시와 동일하게 맵 DisplayName.
            if (versus && m_Loop.WinnerTeam >= 0)
            {
                var mapDef = GridSystem.MapCatalog.Instance != null ? GridSystem.MapCatalog.Instance.Get(m_Loop.MapIndex) : null;
                SaveService.ReportVersus(mapDef != null ? mapDef.DisplayName : map, m_Loop.WinnerTeam == myTeam);
            }

            // 정산서 이미지 = 내가 실제로 지은 구조물(미니씬 렌더). 실패 시 정답 미리보기로 폴백.
            if (m_ResultImage != null)
            {
                if (m_Net == null) m_Net = FindFirstObjectByType<GridNetwork>();
                var rt = m_Net != null ? m_Net.BuildResultPreview() : null;
                if (rt == null && m_AnswerPreview == null) m_AnswerPreview = FindFirstObjectByType<AnswerPreview>();
                if (rt == null && m_AnswerPreview != null) rt = m_AnswerPreview.RT;
                if (rt != null) m_ResultImage.texture = rt;
            }
        }
        if (!m_ResultIntroPlaying)
        {
            if (versus && m_Net != null)
            {
                // 승/패/무 + 양 팀 완성도 (WinnerTeam: -1=무승부, 0/1=승리 팀)
                int enemyPct = Mathf.RoundToInt(m_Net.ScoreFor(1 - myTeam).Percent);
                int w = m_Loop.WinnerTeam;
                string verdict = w == -1 ? "무승부 (DRAW)" : (w == myTeam ? "승리!" : "패배...");   // 폰트가 한글/ASCII만 지원 — 이모지 금지
                m_ResultScoreText.text = $"{verdict}\n우리 팀 {pct}%  :  상대 팀 {enemyPct}%";
            }
            else m_ResultScoreText.text = $"건축 {pct} % 완료";   // 인트로 중엔 코루틴이 숫자 담당
        }

        if (m_ResultStructText != null)
        {
            string nm = m_Loop.AnswerName;
            m_ResultStructText.text = string.IsNullOrEmpty(nm) ? "" : nm;
        }

        if (m_ResultTimeText != null)
        {
            int e = Mathf.Max(0, Mathf.RoundToInt(m_Loop.Elapsed));
            m_ResultTimeText.text = $"소요시간     {e / 60} : {e % 60:00}";

            // DDP 유구 발굴 보너스 — 그 맵에서 유물을 캤을 때만 한 줄 덧붙인다.
            // (전용 텍스트 슬롯을 만들면 결과 패널 프리팹을 건드려야 해서 여기 붙였다)
            var dig = GridSystem.ExcavationNetwork.Instance;
            int artifacts = dig != null ? dig.ArtifactsFound : 0;
            if (artifacts > 0)
                m_ResultTimeText.text += $"\n발굴한 유물   {artifacts} 개   + {score.bonus} 점";
        }

        if (m_ResultNamesText != null)
        {
            string names = "";
            for (int i = 0; i < m_Loop.NameCount; i++)
                names += (i > 0 ? ", " : "") + m_Loop.GetName(i);
            m_ResultNamesText.text = names;
        }

        if (m_ResultGradeText != null)
        {
            bool useStamp = pct >= 90 && m_ResultGradeImage != null && m_ResultGradeImage.sprite != null;
            if (m_ResultGradeImage != null) m_ResultGradeImage.gameObject.SetActive(useStamp);
            m_ResultGradeText.gameObject.SetActive(!useStamp);

            if (pct >= 90) { m_ResultGradeText.text = "EXCELLENT!"; m_ResultGradeText.color = new Color(0.85f, 0.15f, 0.12f, 1f); }
            else if (pct >= 70) { m_ResultGradeText.text = "GREAT!"; m_ResultGradeText.color = new Color(0.90f, 0.45f, 0.10f, 1f); }
            else if (pct >= 50) { m_ResultGradeText.text = "GOOD!"; m_ResultGradeText.color = new Color(0.30f, 0.55f, 0.85f, 1f); }
            else { m_ResultGradeText.text = "TRY AGAIN"; m_ResultGradeText.color = new Color(0.45f, 0.40f, 0.35f, 1f); }
        }

        // 완성도별 별점 채움(개수 = StarCount). 인트로가 팝 애니메이션 담당.
        if (m_ResultStars != null)
        {
            int stars = StarCount(pct);
            for (int i = 0; i < m_ResultStars.Length; i++)
                if (m_ResultStars[i] != null) m_ResultStars[i].color = i < stars ? kStarGold : kStarDim;
        }
    }

    private static int StarCount(int pct) => pct >= 90 ? 3 : pct >= 60 ? 2 : 1;   // 1~3개

    // 결과창 등장 연출: 완성도 숫자 롤업 + 별 팝 + 등급 슬램. 시간정지와 무관하게 unscaled로.
    private IEnumerator ResultIntro(int pct)
    {
        var grade = m_ResultGradeText != null ? m_ResultGradeText.rectTransform : null;
        var stamp = m_ResultGradeImage != null ? m_ResultGradeImage.rectTransform : null;
        if (grade != null) grade.localScale = Vector3.zero;
        if (stamp != null) stamp.localScale = Vector3.zero;
        if (m_ResultStars != null)                            // 별 전부 숨김(아래서 하나씩 팝)
            foreach (var s in m_ResultStars) if (s != null) s.rectTransform.localScale = Vector3.zero;
        if (m_ResultScoreText != null) m_ResultScoreText.text = "건축 0 % 완료";

        float t = 0f; const float dur = 0.55f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / dur);
            int cur = Mathf.RoundToInt(Mathf.Lerp(0f, pct, 1f - (1f - k) * (1f - k)));   // ease-out
            if (m_ResultScoreText != null) m_ResultScoreText.text = $"건축 {cur} % 완료";
            yield return null;
        }
        if (m_ResultScoreText != null) m_ResultScoreText.text = $"건축 {pct} % 완료";

        if (SoundManager.Instance != null) SoundManager.Instance.PlaySFX(SFXType.UIClick);
        float t2 = 0f; const float dur2 = 0.28f;
        while (t2 < dur2)   // 등급/스탬프 쾅
        {
            t2 += Time.unscaledDeltaTime;
            float s = EaseOutBack(Mathf.Clamp01(t2 / dur2));
            if (grade != null) grade.localScale = Vector3.one * s;
            if (stamp != null) stamp.localScale = Vector3.one * s;
            yield return null;
        }
        if (grade != null) grade.localScale = Vector3.one;
        if (stamp != null) stamp.localScale = Vector3.one;

        // 별점 하나씩 톡톡 등장 — 채운 별·빈(회색) 별 모두 같은 팝(색만 다름). 소리는 채운 별만.
        int starCount = StarCount(pct);
        if (m_ResultStars != null)
            for (int i = 0; i < m_ResultStars.Length; i++)
            {
                var srt = m_ResultStars[i] != null ? m_ResultStars[i].rectTransform : null;
                if (srt == null) continue;
                if (i < starCount && SoundManager.Instance != null) SoundManager.Instance.PlaySFX(SFXType.UIClick);
                for (float tp = 0f; tp < 0.22f; tp += Time.unscaledDeltaTime)
                {
                    srt.localScale = Vector3.one * EaseOutBack(Mathf.Clamp01(tp / 0.22f));
                    yield return null;
                }
                srt.localScale = Vector3.one;
            }

        if (m_StarRow != null)   // 별 줄 둥실둥실(정산창 떠 있는 동안)
        {
            if (m_StarBobCo != null) StopCoroutine(m_StarBobCo);
            m_StarBobCo = StartCoroutine(StarBobCo((RectTransform)m_StarRow.transform));
        }

        m_ResultIntroPlaying = false;
    }

    private IEnumerator StarBobCo(RectTransform row)
    {
        Vector2 basePos = row.anchoredPosition;
        float t = 0f;
        while (row != null && row.gameObject.activeInHierarchy)
        {
            t += Time.unscaledDeltaTime;
            row.anchoredPosition = basePos + Vector2.up * (Mathf.Sin(t * 2.4f) * 4f);
            row.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 1.8f) * 3f);
            yield return null;
        }
        if (row != null) { row.anchoredPosition = basePos; row.localRotation = Quaternion.identity; }
        m_StarBobCo = null;
    }

    // ── 축하 폭죽(Resources/Fx/ResultFirework = CFXR4 랜덤색 사본) ──
    private Coroutine m_FireworksCo;

    private void StartResultFireworks()   // 결과창 동안 멈춤없이 팡팡
    {
        if (m_FireworksCo == null) m_FireworksCo = StartCoroutine(FireworksLoop());
    }
    private void StopResultFireworks()
    {
        if (m_FireworksCo != null) { StopCoroutine(m_FireworksCo); m_FireworksCo = null; }
    }

    private IEnumerator FireworksLoop()
    {
        var prefab = Resources.Load<GameObject>("Fx/ResultFirework");
        while (IsComplete())   // 만점(100%) 유지되는 동안 멈춤없이
        {
            SpawnFireworkBurst(prefab, Camera.main);
            yield return new WaitForSecondsRealtime(Random.Range(0.3f, 0.55f));
        }
        m_FireworksCo = null;
    }

    // 폭죽 색 팔레트(발마다 랜덤 — 프리팹은 고정색이라 파티클 startColor로 틴트)
    private static readonly Color[] kFireworkColors =
    {
        new Color(1f, 0.30f, 0.30f), new Color(1f, 0.62f, 0.15f), new Color(1f, 0.90f, 0.25f),
        new Color(0.35f, 1f, 0.45f), new Color(0.25f, 0.85f, 1f), new Color(0.45f, 0.55f, 1f),
        new Color(0.80f, 0.40f, 1f), new Color(1f, 0.45f, 0.80f), Color.white,
    };

    private void SpawnFireworkBurst(GameObject prefab, Camera cam)
    {
        if (prefab == null || cam == null) return;
        int burst = Random.value < 0.35f ? 2 : 1;   // 가끔 2발 동시
        for (int b = 0; b < burst; b++)
        {
            Vector3 pos = cam.transform.position + cam.transform.forward * Random.Range(6f, 9f)
                        + cam.transform.right * Random.Range(-4.5f, 4.5f)
                        + cam.transform.up * Random.Range(-0.8f, 1.5f);   // 눈높이 근처(제자리 폭발이라 여기서 팡)
            var go = Instantiate(prefab, pos, Quaternion.identity);
            var col = kFireworkColors[Random.Range(0, kFireworkColors.Length)];   // 이 발의 색
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>())
            {
                var main = ps.main;
                main.startColor = new ParticleSystem.MinMaxGradient(col);
            }
            Destroy(go, 6f);
        }
    }

    // 정확히 만점(모든 칸 배치+공정 완료). Percent 반올림(99.6→100) 오발화 방지용.
    private bool IsComplete()
    {
        if (m_Loop == null) return false;
        var s = m_Loop.Score;
        return s.maxScore > 0 && s.score >= s.maxScore;
    }

    // 오버슛 이징(0→1.1→1) — 팝/슬램용.
    private static float EaseOutBack(float k)
    {
        const float c1 = 1.70158f, c3 = c1 + 1f;
        float p = k - 1f;
        return 1f + c3 * p * p * p + c1 * p * p;
    }

    // ── 막판 비네트(화면 가장자리 빨간 두근두근) ──
    private Volume m_UrgentVolume;
    private UnityEngine.Rendering.Universal.Vignette m_Vignette;

    private void EnsureVignette()
    {
        if (m_UrgentVolume != null) return;
        var go = new GameObject("~UrgentVignette");
        go.transform.SetParent(transform, false);
        m_UrgentVolume = go.AddComponent<Volume>();
        m_UrgentVolume.isGlobal = true;
        m_UrgentVolume.priority = 100f;
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        m_Vignette = profile.Add<UnityEngine.Rendering.Universal.Vignette>(true);
        m_Vignette.color.Override(new Color(0.55f, 0.04f, 0.04f));
        m_Vignette.smoothness.Override(0.6f);
        m_Vignette.intensity.Override(0f);
        m_UrgentVolume.profile = profile;
    }

    // ── 정산 크레인샷: 결과창 동안 완성 건축물을 천천히 한 바퀴 ──
    private GameObject m_CraneGo;

    private void SetCrane(bool on)
    {
        if (on == (m_CraneGo != null)) return;
        if (!on) { Destroy(m_CraneGo); m_CraneGo = null; return; }

        var gm = FindFirstObjectByType<GridManager>();
        if (gm == null) return;
        float u = GridContract.Unit;
        Vector3 center = GridContract.Origin + new Vector3(gm.GridSize.x, gm.GridSize.y * 0.8f, gm.GridSize.z) * (0.5f * u);

        m_CraneGo = new GameObject("~ResultCraneCam");
        var vcam = m_CraneGo.AddComponent<Unity.Cinemachine.CinemachineCamera>();
        vcam.Priority = 50;   // 플레이어 vcam보다 높음 → 브레인이 자동 블렌드
        m_CraneGo.AddComponent<CraneOrbit>().Init(center, gm.GridSize.x * u * 0.95f, gm.GridSize.y * u * 0.55f);
    }

    // ── 정산서 ↔ 크레인샷 토글 버튼(프리팹 바인딩, 정산 중에만 표시) ──
    private void UpdateCraneToggle(bool resultPhase)
    {
        if (m_CraneToggleBtn == null) return;
        m_CraneToggleBtn.gameObject.SetActive(resultPhase);
        if (!resultPhase) return;
        var lbl = m_CraneToggleBtn.GetComponentInChildren<TextMeshProUGUI>();
        if (lbl != null) lbl.text = m_CraneViewing ? "정산서 보기" : "건축물 둘러보기";
    }

    // ── 이벤트 토스트(좌측 슬쩍): 완성도 돌파 알림 + 100% 완성 축하 ──
    private int m_LastMilestone;
    private int m_PlayersAtStart;   // 게임 시작 시점 팀원 수(최고기록 인원수 키)
    private bool m_CelebratedComplete;
    private GameObject m_Toast;
    private TextMeshProUGUI m_ToastText;
    private Coroutine m_ToastCo;

    private void UpdateMilestoneToast()
    {
        if (m_Loop == null || !m_Loop.IsBuilding) { m_CelebratedComplete = false; return; }
        int pct = Mathf.RoundToInt(m_Loop.Score.Percent);

        if (IsComplete() && !m_CelebratedComplete)   // 정확히 만점(100%) 도달 = 클라이맥스!
        {
            m_CelebratedComplete = true;
            CelebrateComplete();
            return;
        }

        int milestone = pct >= 90 ? 90 : pct >= 75 ? 75 : pct >= 50 ? 50 : pct >= 25 ? 25 : 0;
        if (milestone > m_LastMilestone)
        {
            m_LastMilestone = milestone;
            ShowToast($"완성도 {milestone}% 돌파!");
        }
    }

    // 100% 완성 축하: "완성!!" 배너 + 폭죽 + 다리 전체 물결 + 화면 펀치
    private void CelebrateComplete()
    {
        ShowBanner("완성!!", new Color(1f, 0.55f, 0.15f, 1f));
        // 폭죽은 Update의 완성도 90%+ 감지가 멈춤없이 처리(여기선 배너·물결만)

        if (m_Net == null) m_Net = FindFirstObjectByType<GridNetwork>();
        var gm = FindFirstObjectByType<GridManager>();
        if (m_Net != null && gm != null)
        {
            float u = GridContract.Unit;
            Vector3 center = GridContract.Origin + (Vector3)gm.GridSize * (0.5f * u);
            m_Net.RippleAround(center, gm.GridSize.x * u, 0.14f);   // 다리 전체 젤리 물결
        }
        GridSystem.GridJuice.FovPunch(Camera.main, 4f);
        if (SoundManager.Instance != null) SoundManager.Instance.PlaySFX(SFXType.UIClick);
    }

    private void ShowToast(string msg, float seconds = 2f)
    {
        if (m_Toast == null || m_ToastText == null) return;   // 프리팹 바인딩(EventToast)
        m_Toast.transform.SetAsLastSibling();   // 정산 패널 위에도 보이게 맨 앞으로
        m_ToastText.text = msg;
        if (m_ToastCo != null) StopCoroutine(m_ToastCo);
        m_ToastCo = StartCoroutine(ToastCo(seconds));
    }

    private IEnumerator ToastCo(float seconds)
    {
        m_Toast.SetActive(false);   // UiPopIn 재발동
        m_Toast.SetActive(true);
        yield return new WaitForSecondsRealtime(seconds);
        m_Toast.SetActive(false);
        m_ToastCo = null;
    }

    private void ToggleSettingsPopup()
    {
        if (m_SettingsPopup == null)
            return;

        bool show = !m_SettingsPopup.activeSelf;
        m_SettingsPopup.SetActive(show);
        if (!show && SoundManager.Instance != null)
            SoundManager.Instance.SaveVolumes();
    }

    private void OnEndRequest()
    {
        if (m_Loop == null) m_Loop = FindFirstObjectByType<GameLoopManager>();
        if (m_Loop != null) m_Loop.RequestToggleConsent();
    }

    private static void SetButtonColor(Button button, Color color)
    {
        if (button != null && button.targetGraphic != null)
            button.targetGraphic.color = color;
    }
}

/// <summary>정산 크레인샷: 중심을 바라보며 천천히 원 궤도 회전. GameLoopHUD.SetCrane이 vcam에 부착.</summary>
public class CraneOrbit : MonoBehaviour
{
    const float kYawSpeed = 14f;   // 초당 회전 각도

    Vector3 m_Center;
    float m_Radius, m_Height, m_Yaw;

    public void Init(Vector3 center, float radius, float height)
    {
        m_Center = center;
        m_Radius = Mathf.Max(4f, radius);
        m_Height = Mathf.Max(2f, height);
        m_Yaw = 0f;
        Apply();
    }

    void LateUpdate()
    {
        m_Yaw += kYawSpeed * Time.unscaledDeltaTime;   // 결과창 = 시간 흐름 무관
        Apply();
    }

    void Apply()
    {
        float rad = m_Yaw * Mathf.Deg2Rad;
        transform.position = m_Center + new Vector3(Mathf.Cos(rad) * m_Radius, m_Height, Mathf.Sin(rad) * m_Radius);
        transform.LookAt(m_Center);
    }
}
