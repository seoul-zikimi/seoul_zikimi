using System.Collections;
using GridSystem;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
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
    private enum GOs { TopBar, EndRequestCluster, InGameSettingsPopup, ResultPanel, StartBanner }
    private enum Texts { Timer, Players, Structure, Time, Score, Grade }
    private enum Imgs { P0, P1, P2, P3, GradeStar, GradeStamp }
    private enum Raws { ResultImage }
    private enum Btns { EndRequestButton, SettingsIconButton, SettingsCloseButton, ExitGameButton, RoomButton, LeaveButton }
    private enum Slds { BGMSlider, SFXSlider, SensSlider }

    private GameLoopManager m_Loop;
    private AnswerPreview m_AnswerPreview;
    private TextMeshProUGUI m_TimerText, m_ResultScoreText, m_ResultNamesText, m_ResultStructText, m_ResultTimeText, m_ResultGradeText;
    private Image m_ResultGradeImage, m_ResultStar;
    private Image[] m_PeopleIcons;
    private RawImage m_ResultImage;
    private GameObject m_TopBar, m_ConsentBar, m_ResultPanel, m_SettingsPopup, m_StartBanner;
    private Button m_SettingsButton, m_EndRequestButton;
    private bool m_ResultDismissed, m_ResultWasShown, m_ResultIntroPlaying, m_UrgentBgmStarted;
    private GridSystem.GamePhase m_PrevPhase = (GridSystem.GamePhase)(-1);
    private Coroutine m_BannerCo, m_StarBobCo;

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
        m_ResultStar = Get<Image>((int)Imgs.GradeStar);
        m_ResultGradeImage = Get<Image>((int)Imgs.GradeStamp);
        m_ResultImage = Get<RawImage>((int)Raws.ResultImage);

        m_EndRequestButton = Get<Button>((int)Btns.EndRequestButton);
        m_SettingsButton = Get<Button>((int)Btns.SettingsIconButton);

        // 프리팹엔 onClick이 저장 안 되므로 여기서 전부 배선(클릭음 포함).
        Wire(Btns.EndRequestButton, OnEndRequest);
        Wire(Btns.SettingsIconButton, ToggleSettingsPopup);
        Wire(Btns.SettingsCloseButton, ToggleSettingsPopup);
        Wire(Btns.ExitGameButton, async () => await JobsnailSessionManager.Instance.LeaveLobbyRoomSecurelyAsync());
        Wire(Btns.RoomButton, () => { if (m_Loop != null) m_Loop.RequestReturnToRoom(); });
        Wire(Btns.LeaveButton, async () => await JobsnailSessionManager.Instance.LeaveLobbyRoomSecurelyAsync());

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

        m_Loop = null;
        m_UrgentBgmStarted = false;
        m_PrevPhase = (GridSystem.GamePhase)(-1);

        if (SoundManager.Instance != null)
            SoundManager.Instance.SetPhase(global::GamePhase.Building);
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
        SetVisible(ready);
        if (!ready)
            return;

        var phase = m_Loop.Phase;   // 빌딩 페이즈 진입 순간 "공사 시작!" 배너 슬램
        if (phase != m_PrevPhase)
        {
            if (phase == GridSystem.GamePhase.Building) ShowStartBanner();
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
        if (m_TimerText != null)
        {
            m_TimerText.text = m_Loop.IsBuilding ? $"{secs / 60}:{secs % 60:00}" : "종료";

            // 막판 30초: 타이머 빨갛게 + 두근두근 펄스
            if (m_Loop.IsBuilding && m_Loop.TimeLeft <= 30f)
            {
                float beat = Mathf.Abs(Mathf.Sin(Time.unscaledTime * 5f));
                m_TimerText.rectTransform.localScale = Vector3.one * (1f + 0.14f * beat);
                m_TimerText.color = Color.Lerp(new Color(0.80f, 0.10f, 0.10f, 1f), Color.black, beat * 0.5f);
            }
            else
            {
                m_TimerText.rectTransform.localScale = Vector3.one;
                m_TimerText.color = Color.black;
            }
        }

        UpdateResultPanel();

        if (m_EndRequestButton != null)
        {
            string verb = m_Loop.IsBuilding ? "종료 요청" : "재시작";
            var lbl = m_EndRequestButton.GetComponentInChildren<TextMeshProUGUI>();
            if (lbl != null) lbl.text = (m_Loop.HasLocalConsent ? "동의 취소" : verb) + " (Enter)";
            SetButtonColor(m_EndRequestButton, m_Loop.HasLocalConsent ? new Color(0.56f, 0.86f, 0.48f, 1f) : new Color(1f, 0.78f, 0.44f, 1f));
        }
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

    private void SetVisible(bool visible)
    {
        if (m_TopBar != null) m_TopBar.SetActive(visible);
        if (m_ConsentBar != null) m_ConsentBar.SetActive(visible);
        if (m_SettingsButton != null) m_SettingsButton.gameObject.SetActive(visible);
        if (!visible)
        {
            if (m_ResultPanel != null) m_ResultPanel.SetActive(false);
            if (m_SettingsPopup != null) m_SettingsPopup.SetActive(false);
        }
    }

    // ── "공사 시작!" 배너: 빌딩 페이즈 진입 때 팝인 → 1.2초 뒤 축소 퇴장 ──
    private void ShowStartBanner()
    {
        if (m_StartBanner == null) return;
        if (m_BannerCo != null) StopCoroutine(m_BannerCo);
        m_BannerCo = StartCoroutine(StartBannerCo());
    }

    private IEnumerator StartBannerCo()
    {
        m_StartBanner.SetActive(false);   // UiPopIn 재발동용 토글
        m_StartBanner.SetActive(true);
        m_StartBanner.transform.SetAsLastSibling();
        yield return new WaitForSecondsRealtime(1.2f);

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
        bool show = !m_Loop.IsBuilding && !m_ResultDismissed;
        m_ResultPanel.SetActive(show);
        if (!show)
        {
            m_ResultWasShown = false;   // 다시 숨김 → 다음 표시 때 인트로 연출 재생
            return;
        }

        var score = m_Loop.Score;
        int pct = Mathf.RoundToInt(score.Percent);

        bool firstShow = !m_ResultWasShown;
        m_ResultWasShown = true;
        if (firstShow) { m_ResultIntroPlaying = true; StartCoroutine(ResultIntro(pct)); }
        if (!m_ResultIntroPlaying) m_ResultScoreText.text = $"건축 {pct} % 완료";   // 인트로 중엔 코루틴이 숫자 담당

        if (m_ResultStructText != null)
        {
            string nm = m_Loop.AnswerName;
            m_ResultStructText.text = string.IsNullOrEmpty(nm) ? "" : nm;
        }

        if (m_ResultTimeText != null)
        {
            int e = Mathf.Max(0, Mathf.RoundToInt(m_Loop.Elapsed));
            m_ResultTimeText.text = $"소요시간     {e / 60} : {e % 60:00}";
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

        if (m_ResultImage != null)
        {
            if (m_AnswerPreview == null) m_AnswerPreview = FindFirstObjectByType<AnswerPreview>();
            if (m_AnswerPreview != null && m_AnswerPreview.RT != null && m_ResultImage.texture != m_AnswerPreview.RT)
                m_ResultImage.texture = m_AnswerPreview.RT;
        }
    }

    // 결과창 등장 연출: 완성도 숫자 롤업 + 별 팝 + 등급 슬램. 시간정지와 무관하게 unscaled로.
    private IEnumerator ResultIntro(int pct)
    {
        var star = m_ResultStar != null ? m_ResultStar.rectTransform : null;
        var grade = m_ResultGradeText != null ? m_ResultGradeText.rectTransform : null;
        var stamp = m_ResultGradeImage != null ? m_ResultGradeImage.rectTransform : null;
        if (star != null) star.localScale = Vector3.zero;
        if (grade != null) grade.localScale = Vector3.zero;
        if (stamp != null) stamp.localScale = Vector3.zero;
        if (m_ResultScoreText != null) m_ResultScoreText.text = "건축 0 % 완료";

        float t = 0f; const float dur = 0.55f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / dur);
            int cur = Mathf.RoundToInt(Mathf.Lerp(0f, pct, 1f - (1f - k) * (1f - k)));   // ease-out
            if (m_ResultScoreText != null) m_ResultScoreText.text = $"건축 {cur} % 완료";
            if (star != null) star.localScale = Vector3.one * EaseOutBack(k);            // 별 팝
            yield return null;
        }
        if (m_ResultScoreText != null) m_ResultScoreText.text = $"건축 {pct} % 완료";
        if (star != null) star.localScale = Vector3.one;

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

        if (pct >= 90) StartCoroutine(FireworksCo());   // EXCELLENT — 폭죽 축포!

        if (star != null)   // 별 둥실둥실(정산창 떠 있는 동안)
        {
            if (m_StarBobCo != null) StopCoroutine(m_StarBobCo);
            m_StarBobCo = StartCoroutine(StarBobCo(star));
        }

        m_ResultIntroPlaying = false;
    }

    private IEnumerator StarBobCo(RectTransform star)
    {
        Vector2 basePos = star.anchoredPosition;
        float t = 0f;
        while (star != null && star.gameObject.activeInHierarchy)
        {
            t += Time.unscaledDeltaTime;
            star.anchoredPosition = basePos + Vector2.up * (Mathf.Sin(t * 2.4f) * 5f);
            star.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 1.8f) * 5f);
            yield return null;
        }
        if (star != null) { star.anchoredPosition = basePos; star.localRotation = Quaternion.identity; }
        m_StarBobCo = null;
    }

    // 축하 폭죽: 카메라 앞 공중에 3발 연발(Resources/Fx/ResultFirework = CFXR4 랜덤색 사본).
    private IEnumerator FireworksCo()
    {
        var prefab = Resources.Load<GameObject>("Fx/ResultFirework");
        var cam = Camera.main;
        if (prefab == null || cam == null) yield break;

        for (int i = 0; i < 3; i++)
        {
            Vector3 pos = cam.transform.position + cam.transform.forward * 9f
                        + cam.transform.right * Random.Range(-3.5f, 3.5f)
                        + cam.transform.up * Random.Range(0.5f, 2.5f);
            var go = Instantiate(prefab, pos, Quaternion.identity);
            Destroy(go, 6f);
            yield return new WaitForSecondsRealtime(0.45f);
        }
    }

    // 오버슛 이징(0→1.1→1) — 팝/슬램용.
    private static float EaseOutBack(float k)
    {
        const float c1 = 1.70158f, c3 = c1 + 1f;
        float p = k - 1f;
        return 1f + c3 * p * p * p + c1 * p * p;
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
