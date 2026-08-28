using GridSystem;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 기기 성능 적응 튜너(모바일 전용, 씬 배치 불필요 — 부트스트랩 자동 생성).
///
/// 시작 시: 기기 메모리로 성능 티어(Low/Mid/High)를 정해 해상도 스케일·그림자 거리·
/// 목표 프레임을 한 번 맞춘다(3GB급 구형 폰과 최신 폰이 같은 설정을 쓰면 한쪽은 발열·크래시,
/// 한쪽은 낭비).
///
/// 플레이 중: 5초 창으로 평균 프레임 시간을 재서 예산을 넘게 밀리면 해상도 스케일을 한 단계
/// 내리고(최저 0.5), 여유가 계속되면 티어 기본값까지만 되올린다. 최저 스케일로도 밀리면
/// 그림자를 끄고, 그래도 안 되면 목표 프레임을 30으로 낮춘다 — 프레임 유지가 화질보다 먼저다.
///
/// 메모리 경고(Application.lowMemory): 맵 지연 로드 캐시 해제 + 미사용 에셋 언로드 +
/// 밉맵 한 단계 강하 — iOS EXC_RESOURCE 킬을 맞기 전에 우리가 먼저 내려놓는다.
///
/// 에디터에선 전체 no-op — 런타임에 URP 에셋(renderScale 등)을 만지면 에셋 파일이 더러워지고,
/// 에디터 성능은 튜닝 대상이 아니다. 데스크톱 빌드는 vSync만 켜고 화질은 그대로(PC는 좋게).
/// </summary>
public sealed class DevicePerformanceTuner : MonoBehaviour
{
    private enum Tier { Low, Mid, High }

    private const float kWindowSeconds = 5f;    // 판정 창
    private const float kScaleStep = 0.1f;      // 해상도 스케일 조정 폭
    private const float kScaleMin = 0.5f;
    private const int kGoodWindowsToRaise = 3;  // 연속 여유 창 수 — 이만큼 여유가 이어져야 화질 복구

    private static DevicePerformanceTuner s_Instance;

    private UniversalRenderPipelineAsset m_Rp;
    private Tier m_Tier;
    private float m_BaseScale;          // 티어 기본 스케일(이 위로는 안 올린다)
    private float m_BaseShadowDistance;
    private int m_TargetFps;

    private float m_WindowTime;
    private int m_WindowFrames;
    private int m_GoodStreak;
    private float m_Cooldown;           // 조정 직후 다음 창은 건너뜀(측정 오염 방지)

#if !UNITY_EDITOR
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (s_Instance != null) return;
        var go = new GameObject("~DevicePerformanceTuner");
        DontDestroyOnLoad(go);
        s_Instance = go.AddComponent<DevicePerformanceTuner>();
    }
#endif

    private void Awake()
    {
        if (!Application.isMobilePlatform)
        {
            // 데스크톱: 무제한 프레임만 막는다(발열·소음). 화질 설정(PC 품질)은 손대지 않음.
            QualitySettings.vSyncCount = 1;
            Application.targetFrameRate = -1;
            enabled = false;
            return;
        }

        m_Rp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        m_Tier = DetectTier();
        ApplyTierBaseline();

        Application.lowMemory += OnLowMemory;
        Debug.Log($"[DevicePerf] tier={m_Tier} mem={SystemInfo.systemMemorySize}MB gpu={SystemInfo.graphicsDeviceName} " +
                  $"scale={m_BaseScale} shadow={m_BaseShadowDistance} fps={m_TargetFps}");
    }

    private void OnDestroy()
    {
        if (s_Instance == this) Application.lowMemory -= OnLowMemory;
    }

    private static Tier DetectTier()
    {
        int mem = SystemInfo.systemMemorySize;   // MB. 보수적으로: 모르면 낮게
        if (mem <= 0) return Tier.Low;
        if (mem <= 3200) return Tier.Low;        // ~3GB: iPhone 8~X세대, 저가 안드로이드
        if (mem <= 5500) return Tier.Mid;        // 4~5GB: 중급기
        return Tier.High;                        // 6GB+: 최신 플래그십
    }

    private void ApplyTierBaseline()
    {
        switch (m_Tier)
        {
            case Tier.Low:
                m_TargetFps = 30;
                m_BaseScale = 0.7f;
                m_BaseShadowDistance = 25f;
                QualitySettings.globalTextureMipmapLimit = 1;   // 밉 텍스처 메모리 절반
                QualitySettings.lodBias = 0.75f;
                break;
            case Tier.Mid:
                m_TargetFps = 60;
                m_BaseScale = 0.8f;                              // Mobile_RPAsset 기본과 동일
                m_BaseShadowDistance = 40f;
                break;
            default:
                m_TargetFps = 60;
                m_BaseScale = 0.9f;
                m_BaseShadowDistance = 50f;
                break;
        }

        QualitySettings.vSyncCount = 0;          // 모바일은 targetFrameRate가 기준
        Application.targetFrameRate = m_TargetFps;
        if (m_Rp != null)
        {
            m_Rp.renderScale = m_BaseScale;
            m_Rp.shadowDistance = m_BaseShadowDistance;
        }
    }

    private void Update()
    {
        m_WindowTime += Time.unscaledDeltaTime;
        m_WindowFrames++;
        if (m_WindowTime < kWindowSeconds) return;

        float avg = m_WindowTime / m_WindowFrames;
        m_WindowTime = 0f; m_WindowFrames = 0;

        if (m_Cooldown > 0f) { m_Cooldown -= 1f; return; }   // 조정 직후 창은 판정 제외

        float budget = 1f / m_TargetFps;
        if (avg > budget * 1.2f) { m_GoodStreak = 0; StepDown(avg); }
        else if (avg < budget * 0.75f && ++m_GoodStreak >= kGoodWindowsToRaise) { m_GoodStreak = 0; StepUp(); }
    }

    /// <summary>프레임 예산 초과 — 화질을 한 단계 내린다: 스케일 → 그림자 → 30fps 순.</summary>
    private void StepDown(float avg)
    {
        m_Cooldown = 1f;
        if (m_Rp == null) return;

        if (m_Rp.renderScale > kScaleMin + 0.01f)
        {
            m_Rp.renderScale = Mathf.Max(kScaleMin, m_Rp.renderScale - kScaleStep);
            Debug.Log($"[DevicePerf] 프레임 {avg * 1000f:F1}ms > 예산 — renderScale {m_Rp.renderScale:F2}로 하향");
        }
        else if (m_Rp.shadowDistance > 0f)
        {
            m_Rp.shadowDistance = 0f;
            Debug.Log("[DevicePerf] 최저 스케일에서도 초과 — 그림자 끔");
        }
        else if (m_TargetFps > 30)
        {
            m_TargetFps = 30;
            Application.targetFrameRate = 30;
            Debug.Log("[DevicePerf] 그림자 없이도 초과 — 목표 30fps로 전환");
        }
    }

    /// <summary>여유가 이어짐 — 내렸던 것을 역순으로 티어 기본값까지만 되올린다.</summary>
    private void StepUp()
    {
        m_Cooldown = 1f;
        if (m_Rp == null) return;

        if (m_Rp.shadowDistance <= 0f && m_BaseShadowDistance > 0f)
        {
            m_Rp.shadowDistance = m_BaseShadowDistance;
            Debug.Log("[DevicePerf] 여유 확인 — 그림자 복구");
        }
        else if (m_Rp.renderScale < m_BaseScale - 0.01f)
        {
            m_Rp.renderScale = Mathf.Min(m_BaseScale, m_Rp.renderScale + kScaleStep * 0.5f);
            Debug.Log($"[DevicePerf] 여유 확인 — renderScale {m_Rp.renderScale:F2}로 복구");
        }
    }

    /// <summary>OS 메모리 경고 — 킬 당하기 전에 우리가 먼저 내려놓는다.</summary>
    private void OnLowMemory()
    {
        QualitySettings.globalTextureMipmapLimit =
            Mathf.Min(QualitySettings.globalTextureMipmapLimit + 1, 2);
        MapCatalog.Instance?.ReleaseHeavyCaches();
        Resources.UnloadUnusedAssets();
        System.GC.Collect();
        Debug.LogWarning($"[DevicePerf] 메모리 경고 — 캐시 해제 + 밉맵 한도 {QualitySettings.globalTextureMipmapLimit}");
    }
}
