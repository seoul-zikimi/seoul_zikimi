using GridSystem;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>매치 시작 연출 드라이버 — 전원 로딩 대기 화면(LoadingHUD)과 시작 카운트다운(CountdownHUD).
///
/// 비주얼은 Resources/UI/HUD/LoadingHUD·CountdownHUD 프리팹(기획자 제작)을 그대로 쓰고,
/// 이 컴포넌트는 이름으로 요소를 찾아 값만 채운다:
/// · LoadingHUD/Snail_Icon — 로딩바(거북이): 입장 인원 비율에 따라 풀숲 → 집 앞으로 이동(부드럽게 보간)
/// · LoadingHUD/Information — 맵/모드별 랜덤 팁(LoadingTips, 기획 엑셀 소스). 팁이 없는 조합이면 Tip_Box째 숨김
/// · CountdownHUD/Information — 카운트다운 스프라이트(Countdown_3/2/1/Start). 이미지가 없으면 텍스트 폴백
///
/// 진행/게이트 판정은 전부 GameLoopManager(서버 권위: m_CountdownStart·m_LoadedCount)를 읽기만 한다.
/// 입력 잠금은 GameLoopManager가 GameplayInputBlocker.MatchGateBlocked로 처리.</summary>
public sealed class MatchStartHUD : MonoBehaviour
{
    // 로딩 배경(Loading_Bg) 기준 거북이 이동 구간: 왼쪽 풀숲 앞 → 오른쪽 집 문 앞.
    private const float kSnailStartX = -550f;
    private const float kSnailEndX = 330f;
    private const float kStartBannerSeconds = 0.8f;   // "START!" 표시 후 사라지기까지
    private const float kIntroHoldSeconds = 0.5f;     // 로딩창 등장 직후 달팽이가 시작 위치에서 대기하는 시간

    private GameLoopManager m_Loop;
    private GameObject m_Loading;
    private RectTransform m_SnailIcon;
    private static GameObject s_EarlyLoading;   // 로비에서 미리 띄운 로딩 화면(씬 전환 생존)
    private GameObject m_Countdown;
    private TMP_Text m_CountdownText;                  // 구버전 프리팹(텍스트) 폴백
    private UnityEngine.UI.Image m_CountdownImage;
    private static string s_TipText;   // 이번 매치에 보여줄 팁(early → 게임씬 인계 시에도 동일 문구 유지)
    private float m_ShownProgress;     // 로딩바 표시값(목표로 서서히 보간)
    private float m_LoadingVisibleSeconds;   // 게임씬에서 로딩 화면이 실제 렌더된 시간(최소 노출 보장용)
    private int m_LastShownNumber = -1;
    private bool m_Done;
    private static bool s_LoadPriorityLowered;
    private static UnityEngine.ThreadPriority s_PrevLoadPriority;

    // ── 부트스트랩: GameScene 진입 시 자동 생성 ──
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != SceneNames.GameScene)
            return;
        new GameObject("@MatchStartHUD").AddComponent<MatchStartHUD>();
    }

    /// <summary>로비에서 게임 시작 직후(씬 전환 전) 로딩 화면을 미리 띄운다.
    /// DontDestroyOnLoad로 씬을 건너 살아남고, 게임씬의 MatchStartHUD가 이어받는다.
    /// 중복 호출 안전(이미 떠 있으면 무시).</summary>
    public static void ShowLoadingEarly()
    {
        if (s_EarlyLoading != null)
            return;
        var prefab = Resources.Load<GameObject>("UI/HUD/LoadingHUD");
        if (prefab == null)
            return;
        s_EarlyLoading = Instantiate(prefab);
        s_EarlyLoading.name = "@LoadingHUD(early)";
        DontDestroyOnLoad(s_EarlyLoading);
        ElevateCanvas(s_EarlyLoading, 5000);   // 게임 HUD들보다 항상 위
        s_EarlyLoading.AddComponent<EarlyLoadingDriver>();   // 씬 전환 중에도 거북이 연출(시작 위치 고정 + 슬금슬금)
        s_TipText = null;                      // 새 매치 — 팁 새로 뽑기
        ApplyTip(s_EarlyLoading);
        LowerLoadPriority();
        Debug.Log("[MatchStartHUD] 로딩 화면 선표시(씬 전환 전).");
    }

    /// <summary>로딩 프리팹의 Information에 맵/모드별 랜덤 팁을 채운다. 팁이 없으면 Tip_Box째 숨김.
    /// 문구는 매치당 1회만 뽑아(s_TipText) early → 게임씬 인계 시에도 바뀌지 않는다.</summary>
    private static void ApplyTip(GameObject loadingRoot)
    {
        var info = FindDeep(loadingRoot.transform, "Information");
        var text = info != null ? info.GetComponent<TMP_Text>() : null;
        if (text == null)
            return;

        // null = 아직 못 정함(랜덤 맵 서버 미확정 등) — 캐시하지 않고 다음 ApplyTip에서 재시도.
        // "" = 확정됐지만 팁 없는 조합 — 재시도 종료, 박스 숨김 유지.
        if (s_TipText == null)
            s_TipText = ResolveTip();

        bool show = !string.IsNullOrEmpty(s_TipText);
        if (show)
            text.text = s_TipText;

        // early 단계(미확정)에 숨겼다가 게임씬에서 확정되면 다시 켜야 하므로 양방향 토글.
        var box = FindDeep(loadingRoot.transform, "Tip_Box");
        if (box != null) box.gameObject.SetActive(show);
        info.gameObject.SetActive(show);
    }

    /// <summary>현재 맵/모드를 알아내 팁을 뽑는다. 로비(씬 전환 전)에선 LobbyRoomNet 복제값,
    /// 게임씬에선 GameLoopManager 복제값, 둘 다 없으면(튜토리얼 등) 호스트 정적값 폴백.</summary>
    private static string ResolveTip()
    {
        int mapIndex;
        SeoulZikimi.Gameplay.GameModeKind mode;

        var loop = FindFirstObjectByType<GameLoopManager>();
        var lobby = loop == null ? FindFirstObjectByType<LobbyRoomNet>() : null;
        if (loop != null && loop.IsSpawned)
        {
            mapIndex = loop.MapIndex;
            mode = loop.Mode;
        }
        else if (lobby != null && lobby.IsSpawned)
        {
            mapIndex = lobby.SelectedMap;
            // 로비 모드 4종(0 타임어택/1 아이템전/2 팀 타임어택/3 자유) → GameModeKind
            mode = lobby.SelectedLobbyMode switch
            {
                1 or 2 => SeoulZikimi.Gameplay.GameModeKind.TeamVersus,
                3 => SeoulZikimi.Gameplay.GameModeKind.FreeBuild,
                _ => SeoulZikimi.Gameplay.GameModeKind.TimeAttack,
            };
        }
        else
        {
            mapIndex = GameLoopManager.HostSelectedMap;
            mode = (SeoulZikimi.Gameplay.GameModeKind)Mathf.Clamp(GameLoopManager.HostSelectedMode, 0, 2);
        }

        var def = (mapIndex >= 0 && MapCatalog.Instance != null) ? MapCatalog.Instance.Get(mapIndex) : null;
        if (def == null)
            return null;   // 맵 미확정('랜덤' 등) — GameLoopManager 스폰 후(TickLoading) 실제 맵으로 재시도
        if (def.IsTutorial)
            return "서울에 오신 것을 환영합니다!";
        return LoadingTips.Pick(def.name, mode) ?? "";   // 확정됐는데 팁이 없으면 ""로 캐시(박스 숨김 확정)
    }

    /// <summary>씬 전환 구간(early 로딩창) 전용 달팽이 드라이버 — 서버 진행도를 모르는 구간이라
    /// 시작 위치에서 잠깐 대기 후 40%까지만 천천히 기어가는 연출. GameScene의 MatchStartHUD가
    /// Progress/VisibleSeconds를 이어받아 끊김 없이 계속 간다.</summary>
    private sealed class EarlyLoadingDriver : MonoBehaviour
    {
        public float Progress;
        public float VisibleSeconds;
        private RectTransform m_Snail;

        private void Awake()
        {
            m_Snail = FindDeep(transform, "Snail_Icon") as RectTransform;
            Apply();   // 첫 렌더 전에 시작 위치로(프리팹 기본 위치가 중간이라 그대로 두면 어색)
        }

        private void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            VisibleSeconds += dt;
            if (VisibleSeconds < kIntroHoldSeconds)
                return;
            Progress = Mathf.MoveTowards(Progress, 0.4f, 0.08f * dt);
            Apply();
        }

        private void Apply()
        {
            if (m_Snail == null)
                return;
            var p = m_Snail.anchoredPosition;
            p.x = Mathf.Lerp(kSnailStartX, kSnailEndX, Mathf.Clamp01(Progress));
            m_Snail.anchoredPosition = p;
        }
    }

    // 씬/에셋 백그라운드 로딩이 메인 스레드를 덜 잡아먹게 낮춘다(로딩은 느려지지만 화면이 덜 끊김).
    // 로딩 화면이 떠 있는 동안만 유지하고 매치 시작 시 원복.
    private static void LowerLoadPriority()
    {
        if (s_LoadPriorityLowered)
            return;
        s_LoadPriorityLowered = true;
        s_PrevLoadPriority = Application.backgroundLoadingPriority;
        Application.backgroundLoadingPriority = UnityEngine.ThreadPriority.Low;
    }

    private static void RestoreLoadPriority()
    {
        if (!s_LoadPriorityLowered)
            return;
        s_LoadPriorityLowered = false;
        Application.backgroundLoadingPriority = s_PrevLoadPriority;
    }

    // 프리팹 안의 Canvas를 최상단 정렬로 올린다(다른 HUD가 로딩/카운트다운을 가리지 않게).
    private static void ElevateCanvas(GameObject root, int order)
    {
        foreach (var canvas in root.GetComponentsInChildren<Canvas>(true))
        {
            canvas.overrideSorting = true;
            canvas.sortingOrder = order;
        }
    }

    private void Start()
    {
        // 로비에서 미리 띄운 로딩 화면이 있으면 이어받고, 없으면 지금 띄운다
        // (씬에 들어온 즉시 — 다른 사람 기다리는 동안 검은 화면 방지).
        if (s_EarlyLoading != null)
        {
            m_Loading = s_EarlyLoading;
            s_EarlyLoading = null;
            var early = m_Loading.GetComponent<EarlyLoadingDriver>();
            if (early != null)
            {
                m_ShownProgress = early.Progress;              // 전환 중 진행도 이어받기(순간이동 방지)
                m_LoadingVisibleSeconds = early.VisibleSeconds; // 등장 대기도 이미 소화한 만큼 인정
                Destroy(early);
            }
        }
        else
        {
            s_TipText = null;   // early 없이 직행한 매치(튜토리얼·재시작 등) — 지난 매치 문구가 남지 않게 새로 뽑는다
            var prefab = Resources.Load<GameObject>("UI/HUD/LoadingHUD");
            if (prefab != null)
                m_Loading = Instantiate(prefab);
        }

        if (m_Loading != null)
        {
            ElevateCanvas(m_Loading, 5000);
            LowerLoadPriority();
            ApplyTip(m_Loading);   // early 없이 바로 뜬 경우 대비(이미 채워져 있으면 같은 문구 재적용)
            var snail = FindDeep(m_Loading.transform, "Snail_Icon");
            m_SnailIcon = snail as RectTransform;
            SetSnailX(m_ShownProgress);
        }
    }

    private void Update()
    {
        if (m_Done)
            return;

        if (m_Loop == null)
        {
            m_Loop = FindFirstObjectByType<GameLoopManager>();
            if (m_Loop == null || !m_Loop.IsSpawned)
                return;
        }

        if (!m_Loop.CountdownArmed)
        {
            TickLoading();
            return;
        }

        // 카운트다운이 잡혀도 실제 3-2-1 시작 시각 전(예약 여유 + 서버 최소 노출 구간)에는
        // 로딩 화면을 유지한다 — 이 구간에 거북이가 집까지 도착하는 연출이 나온다.
        // 늦게 로딩 끝난 클라(씬 로드 중 프레임이 안 그려짐)도 잠깐은 보게 하되,
        // 카운트다운을 깎아먹지 않는 범위(남은 시간 1초 초과)에서만 붙잡는다.
        if (m_Loading != null
            && (m_Loop.CountdownRemaining > 3f
                || (m_LoadingVisibleSeconds < 0.5f && m_Loop.CountdownRemaining > 1f)))
        {
            TickLoading();
            return;
        }

        if (m_Loading != null)
        {
            Destroy(m_Loading);
            m_Loading = null;
            RestoreLoadPriority();
        }

        TickCountdown();
        AnimateCountdownCut(Mathf.Min(Time.unscaledDeltaTime, 0.1f));
    }

    private void TickLoading()
    {
        // '랜덤 맵'은 early 시점엔 실제 맵을 몰라 팁을 못 뽑는다 — 서버가 맵을 확정한 뒤(IsSpawned) 재시도.
        if (s_TipText == null && m_Loading != null)
            ApplyTip(m_Loading);

        // 렌더된 시간만 누적(씬 로드 스톨로 delta가 수 초씩 튀는 프레임은 잘라서 계산 —
        // 멈춘 화면을 '보여준 시간'으로 치지 않기 위함).
        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
        m_LoadingVisibleSeconds += dt;

        // 등장 직후엔 시작 위치에서 잠깐 멈춰 있다가 출발(뜨자마자 중간으로 튀는 어색함 방지).
        if (m_LoadingVisibleSeconds < kIntroHoldSeconds)
        {
            SetSnailX(m_ShownProgress);
            return;
        }

        int expected = Mathf.Max(1, m_Loop.PlayerCount);
        float target = m_Loop.CountdownArmed ? 1f : Mathf.Clamp01((float)m_Loop.LoadedPlayerCount / expected);

        // 인원 단위 진행도는 계단식이라 멈춰 보인다 — 실제 진행도보다 조금 앞(cap)까지
        // 천천히 기어가게 해서 항상 움직이는 느낌을 준다. 단 진짜 완료 전엔 95%를 안 넘는다.
        float cap = target >= 1f ? 1f : Mathf.Min(target + 0.35f, 0.95f);
        // 따라잡기도 미끄러지듯 완만하게(0.35/s), 마무리(전원 완료)만 빠르게 쓸어담는다.
        float speed = m_ShownProgress < target ? (m_Loop.CountdownArmed ? 1.2f : 0.35f) : 0.06f;
        m_ShownProgress = Mathf.MoveTowards(m_ShownProgress, cap, speed * dt);
        SetSnailX(m_ShownProgress);
    }

    private void TickCountdown()
    {
        float remain = m_Loop.CountdownRemaining;

        if (m_Countdown == null)
        {
            var prefab = Resources.Load<GameObject>("UI/HUD/CountdownHUD");
            if (prefab == null) { m_Done = true; Destroy(gameObject); return; }
            m_Countdown = Instantiate(prefab);
            ElevateCanvas(m_Countdown, 5001);
            var info = FindDeep(m_Countdown.transform, "Information");
            m_CountdownImage = info != null ? info.GetComponent<UnityEngine.UI.Image>() : null;
            m_CountdownText = info != null ? info.GetComponent<TMP_Text>() : null;
        }

        if (remain > 0f)
        {
            int number = Mathf.CeilToInt(remain);   // 3 → 2 → 1
            if (number != m_LastShownNumber)
            {
                m_LastShownNumber = number;
                ShowCountdown(number.ToString());
            }
            return;
        }

        // START! 표시 후 정리(게임은 이미 시작 — 입력 잠금은 GameLoopManager가 해제).
        if (m_LastShownNumber != 0)
        {
            m_LastShownNumber = 0;
            ShowCountdown("Start");
        }

        if (remain <= -kStartBannerSeconds)
        {
            m_Done = true;
            Destroy(m_Countdown);
            Destroy(gameObject);
        }
    }

    // 숫자(3/2/1) 원본(633x580)이 화면에서 과하게 커서 줄여 표시. START는 원본 크기 그대로.
    private const float kCountdownNumberScale = 0.6f;
    // 컷 등장 팝 연출: 크게 나타나서 탄성 있게 제자리(오버슈트) + 짧은 페이드인.
    private const float kCutPopSeconds = 0.35f;
    private const float kStartFadeOutSeconds = 0.3f;   // START가 사라지기 직전 페이드아웃 구간

    private float m_CutAnimTime = -1f;   // 현재 컷 표시 후 경과(연출용). <0이면 연출 없음
    private bool m_CutIsStart;

    /// <summary>카운트다운 한 컷 표시. key = "3"/"2"/"1"/"Start" — Remaster 스프라이트 우선, 없으면 텍스트.</summary>
    private void ShowCountdown(string key)
    {
        if (m_CountdownImage != null)
        {
            var sprite = Resources.Load<Sprite>("UI_pngs/3.inGame/Remaster/Countdown_" + key);
            if (sprite != null)
            {
                m_CountdownImage.sprite = sprite;
                m_CountdownImage.SetNativeSize();   // 숫자와 START(940x345) 크기가 달라 컷마다 맞춘다
                if (key != "Start")
                    m_CountdownImage.rectTransform.sizeDelta *= kCountdownNumberScale;
                m_CountdownImage.enabled = true;
                m_CutIsStart = key == "Start";
                m_CutAnimTime = 0f;
                AnimateCountdownCut(0f);   // 첫 프레임부터 팝 시작 상태로(원본 크기로 한 프레임 번쩍이는 것 방지)
                return;
            }
        }
        if (m_CountdownText != null)
            m_CountdownText.text = key == "Start" ? "START!" : key;
    }

    /// <summary>컷 팝 연출 한 프레임 진행 — 스케일 1.6→1(오버슈트로 살짝 눌렸다 복귀), 기울기 −8°→0°,
    /// 빠른 페이드인. START는 표시 종료 직전 확대+페이드아웃까지.</summary>
    private void AnimateCountdownCut(float dt)
    {
        if (m_CountdownImage == null || m_CutAnimTime < 0f)
            return;
        m_CutAnimTime += dt;

        var rt = m_CountdownImage.rectTransform;
        float t = Mathf.Clamp01(m_CutAnimTime / kCutPopSeconds);
        float e = EaseOutBack(t);

        float startScale = m_CutIsStart ? 0.4f : 1.6f;   // START는 작게서 튀어나오고, 숫자는 쾅 내려앉는 느낌
        float scale = Mathf.LerpUnclamped(startScale, 1f, e);
        float tilt = Mathf.LerpUnclamped(m_CutIsStart ? 0f : -8f, 0f, e);
        float alpha = Mathf.Clamp01(t / 0.4f);   // 처음 40% 구간에 빠르게 페이드인

        // START 마무리: 사라지기 직전 살짝 커지며 페이드아웃(kStartBannerSeconds 뒤 파괴됨)
        if (m_CutIsStart && m_CutAnimTime > kStartBannerSeconds - kStartFadeOutSeconds)
        {
            float f = Mathf.Clamp01((m_CutAnimTime - (kStartBannerSeconds - kStartFadeOutSeconds)) / kStartFadeOutSeconds);
            scale = Mathf.Lerp(1f, 1.15f, f);
            alpha = 1f - f;
        }

        rt.localScale = new Vector3(scale, scale, 1f);
        rt.localEulerAngles = new Vector3(0f, 0f, tilt);
        var c = m_CountdownImage.color;
        c.a = alpha;
        m_CountdownImage.color = c;
    }

    // 오버슈트 이징(back-out): 목표를 지나쳤다가 탄력 있게 돌아온다.
    private static float EaseOutBack(float t)
    {
        const float c1 = 2.3f;         // 오버슈트 강도
        const float c3 = c1 + 1f;
        float u = t - 1f;
        return 1f + c3 * u * u * u + c1 * u * u;
    }

    private void SetSnailX(float progress01)
    {
        if (m_SnailIcon == null)
            return;
        var p = m_SnailIcon.anchoredPosition;
        p.x = Mathf.Lerp(kSnailStartX, kSnailEndX, Mathf.Clamp01(progress01));
        m_SnailIcon.anchoredPosition = p;
    }

    private void OnDestroy()
    {
        if (m_Loading != null) Destroy(m_Loading);
        if (m_Countdown != null) Destroy(m_Countdown);
        s_TipText = null;   // 다음 매치에선 새 팁
        RestoreLoadPriority();
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
}
