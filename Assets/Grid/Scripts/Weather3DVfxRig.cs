using SeoulZikimi.Weather;
using UnityEngine;

namespace GridSystem
{
    /// <summary>에디터에서 제작된 3D 날씨 ParticleSystem 프리팹의 런타임 제어기.</summary>
    public sealed class Weather3DVfxRig : MonoBehaviour
    {
        [SerializeField] private ParticleSystem m_Rain;
        [SerializeField] private ParticleSystem m_Snow;
        [SerializeField] private ParticleSystem m_Wind;
        [SerializeField] private ParticleSystem m_TyphoonRain;
        [SerializeField] private ParticleSystem m_TyphoonWind;
        [SerializeField] private ParticleSystem m_AutumnLeaves;
        [SerializeField] private ParticleSystem m_CherryBlossom;

        private ParticleSystem[] m_All;

        private void Awake() => CacheSystems();

        public void Configure(
            ParticleSystem rain,
            ParticleSystem snow,
            ParticleSystem wind,
            ParticleSystem typhoonRain,
            ParticleSystem typhoonWind,
            ParticleSystem autumnLeaves,
            ParticleSystem cherryBlossom)
        {
            m_Rain = rain;
            m_Snow = snow;
            m_Wind = wind;
            m_TyphoonRain = typhoonRain;
            m_TyphoonWind = typhoonWind;
            m_AutumnLeaves = autumnLeaves;
            m_CherryBlossom = cherryBlossom;
            CacheSystems();
        }

        public void SetWeather(WeatherKind weather)
        {
            CacheSystems();
            foreach (ParticleSystem system in m_All)
                if (system != null)
                    system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            switch (weather)
            {
                case WeatherKind.Rain: Play(m_Rain); break;
                case WeatherKind.Snow: Play(m_Snow); break;
                case WeatherKind.StrongWind: Play(m_Wind); break;
                case WeatherKind.Typhoon:
                    Play(m_TyphoonRain);
                    Play(m_TyphoonWind);
                    break;
                case WeatherKind.AutumnLeaves: Play(m_AutumnLeaves); break;
                case WeatherKind.CherryBlossom: Play(m_CherryBlossom); break;
            }
        }

        public void Follow(Camera camera)
        {
            if (camera == null) return;
            transform.position = camera.transform.position
                                 + camera.transform.forward * 5f
                                 + Vector3.up * 5f;
        }

        // ── 맵 전체 강수: 이미터 박스를 그리드(+여백) 크기로 키우고 그 위에 고정. 입자 수는 면적비로 늘리되 상한(성능).
        private const float kBaseArea = 12f * 9f;     // 에셋 빌더 기본 박스(12x9)
        private const float kMaxRateMul = 6f;
        private const float kLeafRateMul = 0.3f;     // 낙엽·벚꽃은 맵 전체에 '조금만' 흩날리게(면적 확장은 유지, 밀도만 낮춤)
        // 시스템당 절대 상한 — '모바일 타깃에서만' 적용(기획 확정: 입자 크기 보상은 이질감 있어 금지,
        // 데스크톱은 종전 면적비 스케일 그대로). 종전엔 느린 입자 수명 연장에서 rate×수명×1.35로
        // 재계산돼 대형 맵 눈이 이론상 1.6만 입자까지 갔다. 에디터에선 활성 빌드 타깃 기준으로 걸린다(QA 가능).
        private const int kMaxParticlesMobile = 1000;
        private static readonly bool kCapParticles =   // const면 비모바일 타깃에서 CS0162(도달 불가) 경고
#if UNITY_IOS || UNITY_ANDROID
            true;
#else
            false;
#endif
        private readonly System.Collections.Generic.Dictionary<ParticleSystem, (float rate, int max)> m_Base = new();
        private Bounds m_CoveredArea; private bool m_Covered;

        /// <summary>맵 전체에 내리게: 이미터를 area 위에 두고 박스를 area 크기로. 한 번만 적용(면적 바뀌면 재적용).</summary>
        public void CoverArea(Bounds area, float height = 9f)
        {
            if (m_Covered && m_CoveredArea == area) return;
            m_Covered = true; m_CoveredArea = area;
            transform.position = new Vector3(area.center.x, area.min.y + height, area.center.z);   // 바닥 기준 높이(느린 눈·낙엽도 땅까지 닿게)
            CacheSystems();
            float mul = Mathf.Clamp(area.size.x * area.size.z / kBaseArea, 1f, kMaxRateMul);
            foreach (var ps in m_All)
            {
                if (ps == null) continue;
                if (!m_Base.TryGetValue(ps, out var b))
                {
                    b = (ps.emission.rateOverTime.constant, ps.main.maxParticles);
                    m_Base[ps] = b;
                }
                float rateMul = mul * ((ps == m_AutumnLeaves || ps == m_CherryBlossom) ? kLeafRateMul : 1f);
                var shape = ps.shape;
                shape.scale = new Vector3(area.size.x, shape.scale.y, area.size.z);

                float rate = b.rate * rateMul;
                int wantMax = Mathf.CeilToInt(b.max * rateMul);
                var main = ps.main;

                // 느리게 떨어지는 입자(눈·낙엽·꽃잎)는 수명이 짧아 땅에 닿기 전에 사라짐 → 낙하 시간만큼 수명 보장
                float vy = Mathf.Abs(ps.velocityOverLifetime.y.constant);
                if (vy > 0.05f)
                {
                    float need = (height + 2f) / vy;
                    if (main.startLifetime.constant < need)
                    {
                        main.startLifetime = need;
                        wantMax = Mathf.CeilToInt(rate * need * 1.35f);
                    }
                }

                // 모바일 절대 캡: 넘치는 비율만큼 방출도 함께 줄인다(정상상태 = rate×수명 — 안 줄이면 캡이 실효 없음).
                // 입자 크기 보상은 기획 확정으로 하지 않음(커진 입자가 이질적) — 밀도만 낮아진다.
                if (kCapParticles && wantMax > kMaxParticlesMobile)
                {
                    rate *= (float)kMaxParticlesMobile / wantMax;
                    wantMax = kMaxParticlesMobile;
                }

                var em = ps.emission; em.rateOverTime = rate;
                main.maxParticles = wantMax;
            }
        }

        private void CacheSystems()
        {
            m_All ??= new[]
            {
                m_Rain, m_Snow, m_Wind, m_TyphoonRain,
                m_TyphoonWind, m_AutumnLeaves, m_CherryBlossom
            };
        }

        private static void Play(ParticleSystem system)
        {
            if (system == null) return;
            // 미리 채움 — 이미터가 9m 위라 안 채우면 켠 뒤 수 초(눈은 더) 동안 하늘이 비어 보인다
            system.Simulate(Mathf.Min(system.main.startLifetime.constant, 10f), true, true);
            system.Play(true);
        }
    }
}
