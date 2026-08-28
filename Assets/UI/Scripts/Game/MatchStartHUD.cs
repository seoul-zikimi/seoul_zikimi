using GridSystem;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>매치 시작 연출 드라이버 — 전원 로딩 대기 화면(LoadingHUD)과 시작 카운트다운(CountdownHUD).
///
/// 비주얼은 Resources/UI/HUD/LoadingHUD·CountdownHUD 프리팹(기획자 제작)을 그대로 쓰고,
/// 이 컴포넌트는 이름으로 요소를 찾아 값만 채운다:
/// · LoadingHUD/Snail_Icon — 로딩바: 입장 인원 비율에 따라 x −190 → 150 으로 이동(부드럽게 보간)
/// · LoadingHUD/Information — 추후 엑셀 파싱 정보글 자리(지금은 프리팹 내용 유지)
/// · CountdownHUD/Information — 3, 2, 1, START!
///
/// 진행/게이트 판정은 전부 GameLoopManager(서버 권위: m_CountdownStart·m_LoadedCount)를 읽기만 한다.
/// 입력 잠금은 GameLoopManager가 GameplayInputBlocker.MatchGateBlocked로 처리.</summary>
public sealed class MatchStartHUD : MonoBehaviour
{
    private const float kSnailStartX = -190f;
    private const float kSnailEndX = 150f;
    private const float kStartBannerSeconds = 0.8f;   // "START!" 표시 후 사라지기까지
    private const float kIntroHoldSeconds = 0.5f;     // 로딩창 등장 직후 달팽이가 시작 위치에서 대기하는 시간

    private GameLoopManager m_Loop;
    private GameObject m_Loading;
    private RectTransform m_SnailIcon;
    private static GameObject s_EarlyLoading;   // 로비에서 미리 띄운 로딩 화면(씬 전환 생존)
    private GameObject m_Countdown;
    private TMP_Text m_CountdownText;
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
        s_EarlyLoading.AddComponent<EarlyLoadingDriver>();   // 씬 전환 중에도 달팽이 연출(시작 위치 고정 + 슬금슬금)
        LowerLoadPriority();
        Debug.Log("[MatchStartHUD] 로딩 화면 선표시(씬 전환 전).");
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
            var prefab = Resources.Load<GameObject>("UI/HUD/LoadingHUD");
            if (prefab != null)
                m_Loading = Instantiate(prefab);
        }

        if (m_Loading != null)
        {
            ElevateCanvas(m_Loading, 5000);
            LowerLoadPriority();
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

        // 늦게 로딩 끝난 클라(씬 로드 중 프레임이 안 그려짐)도 로딩 화면을 잠깐은 보게 한다.
        // 단 카운트다운을 깎아먹지 않는 범위(남은 시간 1초 초과)에서만 붙잡는다.
        if (m_Loading != null && m_LoadingVisibleSeconds < 0.5f && m_Loop.CountdownRemaining > 1f)
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
    }

    private void TickLoading()
    {
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
            m_CountdownText = info != null ? info.GetComponent<TMP_Text>() : null;
        }

        if (remain > 0f)
        {
            int number = Mathf.CeilToInt(remain);   // 3 → 2 → 1
            if (number != m_LastShownNumber && m_CountdownText != null)
            {
                m_LastShownNumber = number;
                m_CountdownText.text = number.ToString();
            }
            return;
        }

        // START! 표시 후 정리(게임은 이미 시작 — 입력 잠금은 GameLoopManager가 해제).
        if (m_CountdownText != null && m_LastShownNumber != 0)
        {
            m_LastShownNumber = 0;
            m_CountdownText.text = "START!";
        }

        if (remain <= -kStartBannerSeconds)
        {
            m_Done = true;
            Destroy(m_Countdown);
            Destroy(gameObject);
        }
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
