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
    private enum Texts { Timer, Players, Structure, Time, Score, Grade, EventToast, CoinReward, ReceiptNo, IssueDate }
    private enum Imgs { P0, P1, P2, P3, GradeStar0, GradeStar1, GradeStar2, GradeStamp }
    private enum Raws { ResultImage }
    private enum Btns { EndRequestButton, SettingsIconButton, SettingsCloseButton, KeySettingsButton, ExitGameButton, RoomButton, LeaveButton, CraneToggleButton }
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
    private Button m_SettingsButton, m_EndRequestButton, m_RoomButton;
    private bool m_ResultDismissed, m_ResultWasShown, m_ResultIntroPlaying, m_UrgentBgmStarted;
    // 정산 내용 게이트 — 지난 프레임에 그린 값들(같으면 재조립 스킵)
    private int m_RpPct = int.MinValue, m_RpEnemyPct, m_RpElapsed, m_RpArtifacts, m_RpBonus, m_RpNames, m_RpWinner;
    private bool m_RpIntro;
    private GridSystem.GamePhase m_PrevPhase = (GridSystem.GamePhase)(-1);
    private Coroutine m_BannerCo, m_StarBobCo;
    private GridNetwork m_Net;
    private int m_LastTimerSecs = -1;  // 초 변화 감지(타이머 톡)
    private GridSystem.ItemNetwork m_ItemNet;   // 타이머 안내줄용 — 매 프레임 GetComponent 방지
    // 타이머 문자열은 표시 내용이 바뀐 프레임에만 조립한다(매 프레임 보간·연결 GC 방지)
    private bool m_ShownTimerBuilding;
    private int m_ShownTimerSecs = int.MinValue;
    private int m_ShownTimerPctMine = -2, m_ShownTimerPctOther = -2;
    private string m_ShownTimerHeld = "";
    private float m_TimerTick;         // 초 넘김 팝 감쇠값
    private bool m_CraneViewing;      // true = 정산서 숨기고 크레인샷 보는 중
    private Button m_CraneToggleBtn;  // 정산서↔크레인샷 토글(프리팹 바인딩, 정산 중에만 표시)
    private Sprite m_EndBaked, m_EndBlank;   // 종료 요청 버튼 스프라이트(텍스트 구움 / 빈 흰색)
    private string m_VersusLine;             // 2vs2 정산 승패 한 줄(업무결과 칸)

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
        // 좌상단 조작법 툴팁(접기/펴기) — 키보드/마우스 안내라 모바일에선 띄우지 않는다(눈 버튼 자리와도 겹침).
        if (!MobileControlsHUD.ShouldUseMobileUI && Resources.Load<GameObject>("UI/HUD/ControlsTooltipHUD") != null)
            UIManager.Instance.ShowHUDUI<ControlsTooltipHUD>();
        else
            UIManager.Instance.HideHUDUI<ControlsTooltipHUD>();   // 프리뷰 토글 등으로 이전 세션에 떠 있던 것 정리
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
        m_RoomButton = Get<Button>((int)Btns.RoomButton);
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
        Wire(Btns.KeySettingsButton, () => KeyBindingPopup.Open());
        if (MobileControlsHUD.ShouldUseMobileUI)   // 모바일은 키보드가 없어 키 설정이 무의미 — 같은 자리를 '버튼 배치'로 쓴다
        {
            var keyBtn = Get<Button>((int)Btns.KeySettingsButton);
            if (keyBtn != null)
            {
                var label = keyBtn.GetComponentInChildren<TextMeshProUGUI>(true);
                if (label != null) label.text = "버튼 배치";
                Wire(Btns.KeySettingsButton, () =>
                {
                    ToggleSettingsPopup();               // 팝업을 닫고 바로 드래그 편집 시작
                    MobileControlsHUD.BeginLayoutEdit();
                });
            }
        }
        Wire(Btns.ExitGameButton, async () => await JobsnailSessionManager.Instance.LeaveLobbyRoomSecurelyAsync());
        Wire(Btns.RoomButton, OnReturnToRoom);
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

        EmphasizeTimer();

        // 텍스트 애니메이터: 큰 순간 텍스트만 예쁘게(글자별 물결·흔들)
        AddJuicyText(m_ResultGradeText, 5f, 4.5f, 0.45f, 8f);                 // 등급(EXCELLENT! 등)
        AddJuicyText(m_StartBanner != null ? m_StartBanner.GetComponent<TextMeshProUGUI>() : null, 6f, 5f, 0.4f, 6f); // 배너(완성!!/공사 시작!)
        AddJuicyText(m_ToastText, 3.5f, 4f, 0.5f, 5f);                        // 돌파 토스트

        m_Loop = null;
        m_UrgentBgmStarted = false;
        m_PrevPhase = (GridSystem.GamePhase)(-1);

        // 화마 첫 발화·사방신 도착 시네마틱 구독(경복궁) — Init 재호출 대비 중복 방지 후 등록, OnDestroy에서 해제
        FireNetwork.FirstFireCinematic -= PlayFireCinematic;
        FireNetwork.FirstFireCinematic += PlayFireCinematic;
        GuardianNetwork.StatueArrived -= PlayStatueCinematic;
        GuardianNetwork.StatueArrived += PlayStatueCinematic;
        FireNetwork.Ignited -= PlayFirePulse;
        FireNetwork.Ignited += PlayFirePulse;

        if (SoundManager.Instance != null)
            SoundManager.Instance.SetPhase(global::GamePhase.Building);
    }

    private void OnDestroy()
    {
        FireNetwork.FirstFireCinematic -= PlayFireCinematic;
        GuardianNetwork.StatueArrived -= PlayStatueCinematic;
        FireNetwork.Ignited -= PlayFirePulse;
    }

    // 타이머 강조: 글자 주변에 부드러운 흰 빛 번짐(Underlay 를 halo 로) — 유리 알약 배경(프리팹) 위에서 은은하게 빛나게
    private void EmphasizeTimer()
    {
        if (m_TimerText == null) return;
        var mat = m_TimerText.fontMaterial;   // 공유 머티리얼 복제(이 HUD 전용)
        mat.EnableKeyword(ShaderUtilities.Keyword_Underlay);
        mat.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(1f, 1f, 1f, 0.55f));
        mat.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0f);
        mat.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, 0f);
        mat.SetFloat(ShaderUtilities.ID_UnderlayDilate, 0.5f);
        mat.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0.8f);
        // 밝은 배경(눈·하늘)에서 흰 halo 가 묻히지 않게 얇은 어두운 테두리 한 겹(대비용). 빼려면 이 두 줄 → DisableKeyword.
        mat.EnableKeyword(ShaderUtilities.Keyword_Outline);
        mat.SetColor(ShaderUtilities.ID_OutlineColor, new Color(0.15f, 0.12f, 0.10f, 0.55f));
        mat.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.09f);
        mat.SetFloat(ShaderUtilities.ID_FaceDilate, 0.05f);
        m_TimerText.UpdateMeshPadding();
    }

    // 텍스트 애니메이터 부착(중복 방지) — 큰 순간 TMP만 글자별 물결·흔들
    private static void AddJuicyText(TextMeshProUGUI txt, float amp, float freq, float phase, float rot)
    {
        if (txt == null) return;
        var jt = txt.GetComponent<JuicyText>();
        if (jt == null) jt = txt.gameObject.AddComponent<JuicyText>();
        jt.Configure(amp, freq, phase, rot);
    }

    // 방으로 돌아가기 = 각자 눌러야 이동한다. 내 클릭만 서버에 등록되고, 남은 사람은 끌려가지 않는다.
    private void OnReturnToRoom()
    {
        if (m_Loop == null) return;

        m_Loop.RequestReturnToRoom();

        // 호스트는 RPC가 즉시 처리돼 표가 이미 반영돼 있고, 클라이언트는 다음 복제까지 한 박자 늦다.
        int need = Mathf.Max(1, m_Loop.PlayerCount);
        int done = m_Loop.RoomReturnVoteCount + (m_Loop.HasLocalRoomReturnVote ? 0 : 1);
        done = Mathf.Clamp(done, 1, need);
        if (done < need)
            ShowToast($"다른 사람도 눌러야 방으로 돌아가요 ({done}/{need})", 2.5f);
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

            // 씬 가드: 로비로 복귀한 뒤 이 갱신이 한 프레임 늦게 돌면 로비 BGM을 긴박 BGM으로 덮어쓴다.
            if (!m_UrgentBgmStarted && m_Loop.TimeLeft <= 60f
                && SceneManager.GetActiveScene().name == SceneNames.GameScene)
            {
                m_UrgentBgmStarted = true;
                if (SoundManager.Instance != null)
                    SoundManager.Instance.SetPhase(global::GamePhase.BuildingUrgent);
            }
        }

        int secs = Mathf.CeilToInt(m_Loop.TimeLeft);
        if (m_TimerText != null && timeLimited)
        {
            bool building = m_Loop.IsBuilding;
            int pctMine = -1, pctOther = -1;   // -1 = 이번 프레임 표시 없음(협동/정산)
            string held = "";
            if (m_Loop.IsVersus && building)
            {
                if (m_Net == null) m_Net = FindFirstObjectByType<GridNetwork>();
                if (m_Net != null)
                {
                    int my = Mathf.Max(0, m_Loop.LocalTeam);
                    pctMine = Mathf.RoundToInt(m_Net.ScoreFor(my).Percent);
                    pctOther = Mathf.RoundToInt(m_Net.ScoreFor(1 - my).Percent);
                }
                if (m_ItemNet == null) m_ItemNet = m_Loop.GetComponent<GridSystem.ItemNetwork>();
                held = m_ItemNet != null ? m_ItemNet.LocalHeldName() : "";
            }

            if (building != m_ShownTimerBuilding || secs != m_ShownTimerSecs
                || pctMine != m_ShownTimerPctMine || pctOther != m_ShownTimerPctOther
                || held != m_ShownTimerHeld)
            {
                m_ShownTimerBuilding = building; m_ShownTimerSecs = secs;
                m_ShownTimerPctMine = pctMine; m_ShownTimerPctOther = pctOther; m_ShownTimerHeld = held;

                string timer = building ? $"{secs / 60} : {secs % 60:00}" : "종료";
                // 2vs2 건축 중: 완성도·소지 아이템 안내는 타이머 아래 '별도 중앙 정렬 텍스트'로 —
                // 타이머는 왼쪽 정렬(시계 아이콘 짝)이라 같은 텍스트에 넣으면 줄이 왼쪽으로 쏠리고 겹친다.
                string sub = pctMine >= 0 ? $"우리 {pctMine}% : 상대 {pctOther}%" : "";
                if (!string.IsNullOrEmpty(held))
                    sub += held == "대포"   // 기획서: 대포는 조준+꾹 발사 안내
                        ? "\n<size=75%>[대포] 상대 건물 조준 후 E 꾹 눌렀다 떼면 발사!</size>"
                        : $"\n<size=75%>[{held}] E로 사용</size>";
                VsPctText().text = sub;
                // 걸린 효과(날씨·버프·디버프)는 점수줄 아래 버프 아이콘 바가 담당 — UpdateBuffBar()
                m_TimerText.text = timer;
            }

            // 막판 30초: 타이머 빨갛게 + 두근두근 펄스 + 화면 가장자리 빨간 비네트
            if (m_Loop.IsBuilding && m_Loop.TimeLeft <= 30f)
            {
                float beat = Mathf.Abs(Mathf.Sin(Time.unscaledTime * 5f));
                PulseTimerLine(1f + 0.14f * beat);
                m_TimerText.color = Color.Lerp(new Color(1f, 0.20f, 0.16f, 1f), Color.white, beat * 0.35f);
                EnsureVignette();
                if (m_Vignette != null) m_Vignette.intensity.Override(Mathf.Max(0.16f + 0.14f * beat, m_FireVignette));
            }
            else
            {
                if (m_Loop.IsBuilding && secs != m_LastTimerSecs) m_TimerTick = 1f;   // 초 넘어갈 때 톡
                m_TimerTick = Mathf.Max(0f, m_TimerTick - Time.unscaledDeltaTime * 6f);
                PulseTimerLine(1f + 0.12f * m_TimerTick);
                m_TimerText.color = m_Loop.IsBuilding && m_Loop.TimeLeft <= 60f ? new Color(1f, 0.28f, 0.22f, 1f) : Color.white;   // 1분 미만 빨강(기획서 3.2)
                if (m_Vignette != null) m_Vignette.intensity.Override(m_FireVignette);   // 화마 시네마틱 몫은 유지
            }
            m_LastTimerSecs = secs;
        }

        UpdateBuffBar();                    // 우상단 버프/디버프 아이콘(라디얼 카운트다운)
        SetCrane(!m_Loop.IsBuilding);       // 정산 중 = 건축물 한 바퀴 크레인샷
        UpdateMilestoneToast();             // 완성도 25/50/75/90% 돌파 토스트

        // 정확히 100%(만점) 완공일 때만 폭죽(최대 kFireworksSeconds). 반올림(99.6→100) 오발화 방지.
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
    private Image m_EndImg;                    // 매 프레임 Find/GetComponent 방지 캐시
    private TextMeshProUGUI m_EndLbl;
    private Button m_EndCachedFor;             // 어떤 버튼 인스턴스 기준 캐시인지(재구축 대응)
    private int m_EndShownKey = -1;            // (동의, 건축중, 스프라이트 로드됨) 상태 키

    private void UpdateEndRequestButton()
    {
        if (m_EndRequestButton == null) return;
        if (m_EndBaked == null) m_EndBaked = InGameUiSkin.Load("EndRequestButton");
        if (m_EndBlank == null) m_EndBlank = InGameUiSkin.Load("EndRequestButton_Blank");
        if (m_EndCachedFor != m_EndRequestButton)
        {
            m_EndCachedFor = m_EndRequestButton;
            m_EndImg = m_EndRequestButton.targetGraphic as Image;
            var lblT = m_EndRequestButton.transform.Find("Label");
            m_EndLbl = lblT != null ? lblT.GetComponent<TextMeshProUGUI>() : m_EndRequestButton.GetComponentInChildren<TextMeshProUGUI>(true);
            m_EndShownKey = -1;
        }
        bool consent = m_Loop.HasLocalConsent;
        bool spritesOk = m_EndBaked != null && m_EndBlank != null;
        int key = (consent ? 1 : 0) | (m_Loop.IsBuilding ? 2 : 0) | (spritesOk ? 4 : 0);
        if (spritesOk && key == m_EndShownKey) return;   // 상태 그대로 — 문자열 조립·재대입 스킵(로드 전엔 기존처럼 재시도)
        m_EndShownKey = key;

        var img = m_EndImg;
        var lbl = m_EndLbl;
        bool baked = spritesOk && img != null && m_Loop.IsBuilding && !consent;
        if (baked)
        {
            img.sprite = m_EndBaked; img.color = Color.white;
            if (lbl != null && lbl.gameObject.activeSelf) lbl.gameObject.SetActive(false);
            return;
        }
        string text = (consent ? "동의 취소" : m_Loop.IsBuilding ? "종료 요청" : "재시작")
            + (MobileControlsHUD.ShouldUseMobileUI ? "" : "\n<size=70%>(ENTER)</size>");
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
        if (lbl != null)
        {
            lbl.textWrappingMode = TextWrappingModes.NoWrap;   // 긴 문장(석상 도착 등)이 2줄로 밀리지 않게
            lbl.overflowMode = TextOverflowModes.Overflow;
            lbl.text = text; lbl.color = color;
        }
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
        UpdateResultTapZone(show);                          // 모바일: 정산서 밖 터치 = 둘러보기
        m_ResultPanel.SetActive(show);
        if (!resultPhase)
        {
            if (m_ResultWasShown && m_Net != null) m_Net.EndResultPreview();   // 썸네일 라이브 카메라 끄기
            m_ResultWasShown = false;   // 다시 숨김 → 다음 표시 때 인트로 연출 재생
            m_CraneViewing = false;
            m_RpPct = int.MinValue;     // 다음 표시 때 내용 강제 재조립
        }
        if (!show)
            return;

        // 이미 누른 사람은 다시 못 누르게 — 나머지가 누를 때까지 대기 중이라는 표시.
        if (m_RoomButton != null)
            m_RoomButton.interactable = !m_Loop.HasLocalRoomReturnVote;

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

            // 영수증 메타(정산번호 'JKM-' 뒤 / 발행 일자) — 리마스터 배경 칸
            var now = System.DateTime.Now;
            var receiptNo = Get<TextMeshProUGUI>((int)Texts.ReceiptNo);
            if (receiptNo != null) receiptNo.text = UnityEngine.Random.Range(100000, 1000000).ToString();   // 6자리 난수(칸 좁아 한 줄)
            var issueDate = Get<TextMeshProUGUI>((int)Texts.IssueDate);
            if (issueDate != null) issueDate.text = now.ToString("yyyy.MM.dd");

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

        // ── 내용 게이트: 표시값이 지난 프레임과 같으면(정산 화면 대부분의 시간) 아래의
        // 문자열 조립·스프라이트 Load·텍스트 재대입을 전부 건너뛴다. 점수 복제가 한 틱 늦게
        // 도착하는 경우도 값 변화로 잡혀 자동 갱신된다.
        {
            var dig0 = GridSystem.ExcavationNetwork.Instance;
            int artifacts0 = dig0 != null ? dig0.ArtifactsFound : 0;
            int enemyPct0 = versus && m_Net != null ? Mathf.RoundToInt(m_Net.ScoreFor(1 - myTeam).Percent) : -1;
            int elapsed0 = Mathf.Max(0, Mathf.RoundToInt(m_Loop.Elapsed));
            if (!firstShow && pct == m_RpPct && enemyPct0 == m_RpEnemyPct && elapsed0 == m_RpElapsed
                && artifacts0 == m_RpArtifacts && score.bonus == m_RpBonus && m_ResultIntroPlaying == m_RpIntro
                && m_Loop.NameCount == m_RpNames && m_Loop.WinnerTeam == m_RpWinner)
                return;
            m_RpPct = pct; m_RpEnemyPct = enemyPct0; m_RpElapsed = elapsed0; m_RpArtifacts = artifacts0;
            m_RpBonus = score.bonus; m_RpIntro = m_ResultIntroPlaying; m_RpNames = m_Loop.NameCount; m_RpWinner = m_Loop.WinnerTeam;
        }

        if (!m_ResultIntroPlaying)
        {
            if (versus && m_Net != null)
            {
                // 승/패/무 + 양 팀 완성도 (WinnerTeam: -1=무승부, 0/1=승리 팀)
                int enemyPct = Mathf.RoundToInt(m_Net.ScoreFor(1 - myTeam).Percent);
                int w = m_Loop.WinnerTeam;
                string verdict = w == -1 ? "무승부 (DRAW)" : (w == myTeam ? "승리!" : "패배...");   // 폰트가 한글/ASCII만 지원 — 이모지 금지
                // 승패 문구를 큼직하게, 완성도 비교는 작게 — 도장과 겹치지 않도록 도장은 꺼서 사용(아래 useStamp 처리)
                m_VersusLine = $"<size=40>{verdict}</size>\n<size=22>우리 {pct}% : 상대 {enemyPct}%</size>";
            }
            else m_VersusLine = null;
            m_ResultScoreText.text = pct.ToString();   // '건축 완료율 [  ]%' — 숫자만(라벨·%는 배경). 인트로 중엔 코루틴이 숫자 담당
        }

        if (m_ResultStructText != null)
        {
            string nm = m_Loop.AnswerName;
            m_ResultStructText.text = string.IsNullOrEmpty(nm) ? "" : nm;
        }

        if (m_ResultTimeText != null)
        {
            int e = Mathf.Max(0, Mathf.RoundToInt(m_Loop.Elapsed));
            m_ResultTimeText.text = $"{e / 60} : {e % 60:00}";   // 라벨은 영수증 배경이 담당 — 숫자만

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
            // 도장: 완성도별 3종(EXCELLENT ≥90 / GOOD JOB ≥50 / TRY AGAIN). 스프라이트 없으면 글자 폴백.
            var stampSprite = InGameUiSkin.Load(pct >= 90 ? "Stamp_Excellent" : pct >= 50 ? "Stamp_GoodJob" : "Stamp_TryAgain");
            // 2vs2 승패 문구는 도장(완성도 기준 EXCELLENT/GOOD JOB/TRY AGAIN)과 의미가 다르고
            // 같은 칸에 겹쳐 그려지므로, 승패 문구가 있으면 도장은 끈다.
            bool useStamp = string.IsNullOrEmpty(m_VersusLine) && stampSprite != null && m_ResultGradeImage != null;
            if (m_ResultGradeImage != null)
            {
                if (useStamp) m_ResultGradeImage.sprite = stampSprite;
                m_ResultGradeImage.gameObject.SetActive(useStamp);
            }

            // '업무결과' 칸: 2vs2 는 승패 한 줄, 협동은 도장이 말해주니 비움(도장 없을 때만 등급 글자)
            if (!string.IsNullOrEmpty(m_VersusLine)) { m_ResultGradeText.text = m_VersusLine; m_ResultGradeText.color = new Color(0.27f, 0.22f, 0.18f, 1f); }
            else if (useStamp) m_ResultGradeText.text = "";
            else if (pct >= 90) { m_ResultGradeText.text = "EXCELLENT!"; m_ResultGradeText.color = new Color(0.85f, 0.15f, 0.12f, 1f); }
            else if (pct >= 50) { m_ResultGradeText.text = "GOOD JOB!"; m_ResultGradeText.color = new Color(0.90f, 0.45f, 0.10f, 1f); }
            else { m_ResultGradeText.text = "TRY AGAIN"; m_ResultGradeText.color = new Color(0.45f, 0.40f, 0.35f, 1f); }
            m_ResultGradeText.gameObject.SetActive(true);
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
        if (m_ResultScoreText != null) m_ResultScoreText.text = "0";

        float t = 0f; const float dur = 0.55f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / dur);
            int cur = Mathf.RoundToInt(Mathf.Lerp(0f, pct, 1f - (1f - k) * (1f - k)));   // ease-out
            if (m_ResultScoreText != null) m_ResultScoreText.text = cur.ToString();
            yield return null;
        }
        if (m_ResultScoreText != null) m_ResultScoreText.text = pct.ToString();

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
    private bool m_FireworksDone;                    // 이번 100% 유지 구간에서 이미 다 쏘았음(재점화 방지)
    private const float kFireworksSeconds = 10f;     // 100% 축하 폭죽 지속 시간

    private void StartResultFireworks()   // 100% 달성 순간부터 kFireworksSeconds 동안 팡팡
    {
        if (!m_FireworksDone && m_FireworksCo == null) m_FireworksCo = StartCoroutine(FireworksLoop());
    }
    private void StopResultFireworks()
    {
        if (m_FireworksCo != null) { StopCoroutine(m_FireworksCo); m_FireworksCo = null; }
        m_FireworksDone = false;   // 100%가 깨졌다가 다시 완성되면 새로 축하
    }

    private IEnumerator FireworksLoop()
    {
        var prefab = Resources.Load<GameObject>("Fx/ResultFirework");
        float elapsed = 0f;
        while (IsComplete() && elapsed < kFireworksSeconds)   // 만점(100%) 유지 중 최대 kFireworksSeconds
        {
            SpawnFireworkBurst(prefab, Camera.main);
            float wait = Random.Range(0.3f, 0.55f);
            yield return new WaitForSecondsRealtime(wait);
            elapsed += wait;
        }
        m_FireworksDone = true;   // 시간 소진 — 100%가 유지되는 동안은 다시 안 쏨
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
    // ── 우상단 버프/디버프 아이콘 바 — 걸린 효과마다 아이콘 + 어두워지는 라디얼(경과분) + 남은 초 ──
    private RectTransform m_BuffBar;
    private readonly System.Collections.Generic.Dictionary<SeoulZikimi.Gameplay.CompetitiveItemKind,
        (GameObject go, Image overlay, TextMeshProUGUI secs, Image icon)> m_BuffCells = new();
    private static readonly System.Collections.Generic.List<GridSystem.ItemNetwork.LocalStatus> s_Statuses = new();
    private static readonly System.Collections.Generic.List<SeoulZikimi.Gameplay.CompetitiveItemKind> s_GoneKinds = new();

    // '우리 % : 상대 %' 중앙 정렬 줄 — 타이머(왼쪽 정렬)와 분리해 런타임 생성. TopBar가 화면 중앙 기준.
    private TextMeshProUGUI m_VsPctText;
    private TextMeshProUGUI VsPctText()
    {
        if (m_VsPctText != null) return m_VsPctText;
        var go = new GameObject("VersusPct", typeof(TextMeshProUGUI));
        var rt = (RectTransform)go.transform;
        rt.SetParent(m_TopBar.transform, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -88f);   // 타이머 숫자줄(폰트 63 + halo) 아래 여유 있게
        rt.sizeDelta = new Vector2(700f, 90f);          // 2줄(완성도 + 아이템 안내)까지 수용
        m_VsPctText = go.GetComponent<TextMeshProUGUI>();
        m_VsPctText.font = m_TimerText.font;
        m_VsPctText.fontSharedMaterial = m_TimerText.fontSharedMaterial;   // 타이머와 같은 halo 스타일
        m_VsPctText.fontStyle = m_TimerText.fontStyle;
        m_VsPctText.fontSize = m_TimerText.fontSize * 0.6f;
        m_VsPctText.alignment = TextAlignmentOptions.Top;
        m_VsPctText.textWrappingMode = TextWrappingModes.NoWrap;
        m_VsPctText.overflowMode = TextOverflowModes.Overflow;
        m_VsPctText.color = Color.white;
        m_VsPctText.raycastTarget = false;
        return m_VsPctText;
    }

    private void UpdateBuffBar()
    {
        if (m_BuffBar == null)
        {
            var t = transform.Find("BuffBar");
            if (t == null) return;   // 구버전 프리팹(재생성 전) — 표시 생략
            m_BuffBar = (RectTransform)t;
            // 우상단 → 상단 중앙('우리 % : 상대 %' 바로 아래)로 재배치 — 프리팹 재생성 없이 코드로.
            // 화면 끝은 시선 밖이라 걸린 걸 모른다(QA) — 점수줄과 세로로 나란히 두어 한 시선에 들어오게.
            m_BuffBar.anchorMin = m_BuffBar.anchorMax = new Vector2(0.5f, 1f);
            m_BuffBar.pivot = new Vector2(0.5f, 1f);
            m_BuffBar.anchoredPosition = new Vector2(0f, -196f);   // 점수줄(-88, 2줄 최대 ~95px)과 안 겹치게
            m_BuffBar.sizeDelta = new Vector2(700f, 62f);
            var lay = m_BuffBar.GetComponent<HorizontalLayoutGroup>();
            if (lay != null) lay.childAlignment = TextAnchor.MiddleCenter;
        }

        GridSystem.ItemNetwork.GetLocalStatuses(s_Statuses);

        s_GoneKinds.Clear();
        foreach (var kv in m_BuffCells)
        {
            bool alive = false;
            foreach (var st in s_Statuses) if (st.Kind == kv.Key) { alive = true; break; }
            if (!alive) { if (kv.Value.go != null) Destroy(kv.Value.go); s_GoneKinds.Add(kv.Key); }
        }
        foreach (var k in s_GoneKinds) { m_BuffCells.Remove(k); m_BuffSecsShown.Remove(k); }

        foreach (var st in s_Statuses)
        {
            if (!m_BuffCells.TryGetValue(st.Kind, out var cell))
                m_BuffCells[st.Kind] = cell = MakeBuffCell(st.Kind);
            cell.overlay.fillAmount = 1f - Mathf.Clamp01(st.Remaining / st.Total);   // 경과분이 어둡게 차오름 = 남은 밝은 부분이 줄어듦
            int secsLeft = Mathf.CeilToInt(st.Remaining);   // 초가 실제로 넘어갈 때만 문자열 생성(매 프레임 ToString GC 방지)
            if (!m_BuffSecsShown.TryGetValue(st.Kind, out int shown) || shown != secsLeft)
            {
                m_BuffSecsShown[st.Kind] = secsLeft;
                cell.secs.text = secsLeft.ToString();
                cell.secs.color = secsLeft <= 3 ? new Color(1f, 0.35f, 0.3f) : Color.white;
            }
            // 만료 직전(3초): 아이콘 깜빡여 '곧 풀린다' 신호
            var ic = cell.icon.color;
            ic.a = st.Remaining < 3f ? 0.45f + 0.55f * Mathf.Abs(Mathf.Sin(Time.time * 7f)) : 1f;
            cell.icon.color = ic;
        }
    }

    private readonly System.Collections.Generic.Dictionary<SeoulZikimi.Gameplay.CompetitiveItemKind, int> m_BuffSecsShown = new();

    private (GameObject, Image, TextMeshProUGUI, Image) MakeBuffCell(SeoulZikimi.Gameplay.CompetitiveItemKind kind)
    {
        var go = new GameObject(kind.ToString(), typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(m_BuffBar, false);
        rt.sizeDelta = new Vector2(62f, 62f);   // 걸린 효과는 한눈에 보여야 함(QA) — 바 높이보다 커도 마스크 없어 그대로 그려짐
        go.AddComponent<GridSystem.ItemPopIn>();   // 걸리는 순간 뿅 팝인(월드 아이템 상자와 같은 이징)

        Image Img(string name, Color color)
        {
            var child = new GameObject(name, typeof(Image));
            var crt = (RectTransform)child.transform;
            crt.SetParent(rt, false);
            crt.anchorMin = Vector2.zero; crt.anchorMax = Vector2.one;
            crt.offsetMin = Vector2.zero; crt.offsetMax = Vector2.zero;
            var img = child.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        // 배경·테두리 색으로 버프(초록)/디버프(빨강) 즉시 구분
        bool isBuff = GridSystem.ItemNetwork.IsBuff(kind);
        var edge = isBuff ? new Color(0.2f, 0.85f, 0.35f, 0.95f) : new Color(1f, 0.28f, 0.22f, 0.95f);
        var bg = Img("Bg", isBuff ? new Color(0.03f, 0.22f, 0.08f, 0.6f) : new Color(0.25f, 0.03f, 0.03f, 0.6f));
        var outline = bg.gameObject.AddComponent<Outline>();
        outline.effectColor = edge;
        outline.effectDistance = new Vector2(2f, -2f);

        var icon = Img("Icon", Color.white);
        icon.sprite = GridSystem.HeldItemBubble.LoadIcon(kind);
        icon.preserveAspect = true;
        if (icon.sprite == null) { icon.color = GridSystem.ItemNetwork.KindColor(kind); }   // 아이콘 없으면 종류색 칸
        ((RectTransform)icon.transform).offsetMin = new Vector2(4f, 4f);   // 테두리 안쪽으로 살짝 여백
        ((RectTransform)icon.transform).offsetMax = new Vector2(-4f, -4f);

        var overlay = Img("Overlay", new Color(0f, 0f, 0f, 0.55f));
        overlay.type = Image.Type.Filled;
        overlay.fillMethod = Image.FillMethod.Radial360;
        overlay.fillOrigin = (int)Image.Origin360.Top;
        overlay.fillClockwise = true;
        overlay.fillAmount = 0f;

        var secsGo = new GameObject("Secs", typeof(TextMeshProUGUI));
        var srt = (RectTransform)secsGo.transform;
        srt.SetParent(rt, false);
        srt.anchorMin = Vector2.zero; srt.anchorMax = Vector2.one;
        srt.offsetMin = Vector2.zero; srt.offsetMax = Vector2.zero;
        var secs = secsGo.GetComponent<TextMeshProUGUI>();
        secs.font = JobsnailUiKit.TmpFont;
        secs.fontSize = 24f;   // 남은 초가 아이콘만큼 중요 — 멀리서도 읽히게 크게
        secs.fontStyle = FontStyles.Bold;
        secs.alignment = TextAlignmentOptions.BottomRight;
        secs.color = Color.white;
        secs.raycastTarget = false;
        // 밝은 아이콘 위에서도 읽히게 TMP 아웃라인(UI.Shadow는 TMP에 안 먹음 — 재질 인스턴스 몇 개는 감수)
        secs.outlineColor = new Color32(0, 0, 0, 220);
        secs.outlineWidth = 0.22f;

        return (go, overlay, secs, icon);
    }

    // 시계 줄(첫 줄)만 펌핑 — 아래 '우리:상대'·아이템 줄은 고정.
    // rect 통짜 스케일이면 전 줄이 같이 흔들려서, 첫 줄 글자 버텍스만 라인 중심 기준으로 키운다(레이아웃 불변).
    private void PulseTimerLine(float scale)
    {
        m_TimerText.rectTransform.localScale = Vector3.one;
        if (Mathf.Approximately(scale, 1f)) return;
        m_TimerText.ForceMeshUpdate();
        var ti = m_TimerText.textInfo;
        if (ti.lineCount == 0) return;
        var line = ti.lineInfo[0];
        Vector3 center = (line.lineExtents.min + line.lineExtents.max) * 0.5f;
        for (int i = line.firstCharacterIndex; i <= line.lastCharacterIndex && i < ti.characterCount; i++)
        {
            var ch = ti.characterInfo[i];
            if (!ch.isVisible) continue;
            var verts = ti.meshInfo[ch.materialReferenceIndex].vertices;
            for (int v = 0; v < 4; v++)
                verts[ch.vertexIndex + v] = center + (verts[ch.vertexIndex + v] - center) * scale;
        }
        m_TimerText.UpdateVertexData(TMP_VertexDataUpdateFlags.Vertices);
    }

    private Volume m_UrgentVolume;
    private UnityEngine.Rendering.Universal.Vignette m_Vignette;
    private float m_FireVignette;      // 화마 첫 등장 시네마틱의 비네트 몫(막판 30초 펄스와 Max 합성)
    private Coroutine m_FireCineCo;

    // ── 화마 첫 등장 연출(경복궁): 빨간 비네트가 확 조여들며 경고 배너 + 사방신 안내. FireNetwork가 호출 ──
    public void PlayFireCinematic()
    {
        ShowBanner("화마가 나타났다!\n<size=55%>불이 붙을지도 모른다…</size>", new Color(1f, 0.30f, 0.12f, 1f));
        if (m_FireCineCo != null) StopCoroutine(m_FireCineCo);
        m_FireCineCo = StartCoroutine(FireCinematicCo());
    }

    private IEnumerator FireCinematicCo()
    {
        EnsureVignette();
        for (float e = 0f; e < 0.5f; e += Time.unscaledDeltaTime)   // 확 조여들기
        { m_FireVignette = Mathf.Lerp(0f, 0.42f, e / 0.5f); yield return null; }

        bool toastShown = false;
        for (float hold = 0f; hold < 2.2f; hold += Time.unscaledDeltaTime)   // 두근두근 유지
        {
            m_FireVignette = 0.34f + 0.08f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 6f));
            if (!toastShown && hold >= 1.6f)   // 배너 퇴장 직후 이어지는 안내 멘트
            {
                toastShown = true;
                ShowToast("사방신 석상을 배치하면 그들의 힘이 화마를 억누를지도…", 5f);
            }
            yield return null;
        }

        for (float e = 0f; e < 1.5f; e += Time.unscaledDeltaTime)   // 천천히 풀리기
        { m_FireVignette = Mathf.Lerp(0.34f, 0f, e / 1.5f); yield return null; }
        m_FireVignette = 0f;
        m_FireCineCo = null;
    }

    // ── 매 발화 짧은 빨간 펄스: 어디선가 불이 붙었다는 화면 신호. FireNetwork.Ignited 이벤트가 호출 ──
    private Coroutine m_FirePulseCo;

    private void PlayFirePulse()
    {
        if (m_FireCineCo != null) return;   // 첫 발화 대형 시네마틱 중엔 생략(겹침 방지)
        if (m_FirePulseCo != null) StopCoroutine(m_FirePulseCo);
        m_FirePulseCo = StartCoroutine(FirePulseCo());
    }

    private IEnumerator FirePulseCo()
    {
        EnsureVignette();
        if (m_Vignette != null) m_Vignette.color.Override(new Color(0.55f, 0.04f, 0.04f));   // 화마는 항상 빨강(석상 연출이 색을 바꿨어도)
        for (float e = 0f; e < 0.2f; e += Time.unscaledDeltaTime)   // 번쩍
        { m_FireVignette = Mathf.Lerp(0f, 0.28f, e / 0.2f); yield return null; }
        for (float e = 0f; e < 0.9f; e += Time.unscaledDeltaTime)   // 스르륵
        { m_FireVignette = Mathf.Lerp(0.28f, 0f, e / 0.9f); yield return null; }
        m_FireVignette = 0f;
        m_FirePulseCo = null;
    }

    // ── 사방신 석상 도착 연출: 방위색 비네트 펄스 + 도착 배너(화마 등장과 같은 문법). GuardianNetwork 이벤트가 호출 ──
    private Coroutine m_StatueCineCo;

    private void PlayStatueCinematic(string text, Color color)
    {
        ShowBanner(text, Color.Lerp(color, Color.white, 0.35f));   // 현무처럼 어두운 방위색은 텍스트용으로 밝힘
        if (m_StatueCineCo != null) StopCoroutine(m_StatueCineCo);
        m_StatueCineCo = StartCoroutine(StatueCinematicCo(color));
    }

    private IEnumerator StatueCinematicCo(Color color)
    {
        EnsureVignette();
        if (m_Vignette != null) m_Vignette.color.Override(color);
        for (float e = 0f; e < 0.35f; e += Time.unscaledDeltaTime)   // 번쩍
        { m_FireVignette = Mathf.Lerp(0f, 0.35f, e / 0.35f); yield return null; }
        for (float e = 0f; e < 1.4f; e += Time.unscaledDeltaTime)    // 스르륵
        { m_FireVignette = Mathf.Lerp(0.35f, 0f, e / 1.4f); yield return null; }
        m_FireVignette = 0f;
        if (m_Vignette != null) m_Vignette.color.Override(new Color(0.55f, 0.04f, 0.04f));   // 기본(위급 빨강) 복원
        m_StatueCineCo = null;
    }

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
    // 모바일: 정산서가 떠 있는 동안엔 버튼 대신 '정산서 밖 터치'가 둘러보기 진입을 담당(아래 UpdateResultTapZone)
    // → [건축물 둘러보기] 버튼은 숨기고, 둘러보기 중의 [정산서 보기]만 남긴다(+아이폰 홈 제스처 회피로 y 72).
    private void UpdateCraneToggle(bool resultPhase)
    {
        if (m_CraneToggleBtn == null) return;
        bool mobile = MobileControlsHUD.ShouldUseMobileUI;
        m_CraneToggleBtn.gameObject.SetActive(resultPhase && (!mobile || m_CraneViewing));
        if (!resultPhase) return;
        var lbl = m_CraneToggleBtn.GetComponentInChildren<TextMeshProUGUI>();
        if (lbl != null) lbl.text = m_CraneViewing ? "정산서 보기" : "건축물 둘러보기";
        if (mobile)
        {
            var rt = (RectTransform)m_CraneToggleBtn.transform;
            if (rt.anchoredPosition.y != 72f) rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, 72f);
        }
    }

    // ── 모바일 전용: 정산서 밖 화면 터치 = 건축물 둘러보기 진입(투명 전면 오버레이, 정산서 뒤에 깔림) ──
    private GameObject m_ResultTapZone;

    private void UpdateResultTapZone(bool receiptShown)
    {
        bool want = receiptShown && MobileControlsHUD.ShouldUseMobileUI;
        if (!want)
        {
            if (m_ResultTapZone != null && m_ResultTapZone.activeSelf) m_ResultTapZone.SetActive(false);
            return;
        }
        if (m_ResultTapZone == null)
        {
            var go = new GameObject("~ResultTapToCrane", typeof(RectTransform)) { layer = 5 };
            var rt = (RectTransform)go.transform;
            rt.SetParent(m_ResultPanel.transform.parent, false);
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.sizeDelta = Vector2.zero;
            var img = go.AddComponent<UnityEngine.UI.Image>();
            img.color = new Color(0f, 0f, 0f, 0f);   // 완전 투명 — 터치만 받는다
            go.AddComponent<NoJuicyButtonMotion>();
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() => m_CraneViewing = true);
            m_ResultTapZone = go;
        }
        // 정산서 바로 뒤(아래)에 유지 — 정산서 종이 위 터치는 정산서가 먹고, 바깥만 이 오버레이에 닿는다
        m_ResultTapZone.transform.SetSiblingIndex(m_ResultPanel.transform.GetSiblingIndex());
        if (!m_ResultTapZone.activeSelf) m_ResultTapZone.SetActive(true);
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
