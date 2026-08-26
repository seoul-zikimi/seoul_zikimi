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
                var em = ps.emission; em.rateOverTime = b.rate * rateMul;
                var main = ps.main; main.maxParticles = Mathf.CeilToInt(b.max * rateMul);
                // 느리게 떨어지는 입자(눈·낙엽·꽃잎)는 수명이 짧아 땅에 닿기 전에 사라짐 → 낙하 시간만큼 수명 보장
                float vy = Mathf.Abs(ps.velocityOverLifetime.y.constant);
                if (vy > 0.05f)
                {
                    float need = (height + 2f) / vy;
                    if (main.startLifetime.constant < need) { main.startLifetime = need; main.maxParticles = Mathf.CeilToInt(b.rate * rateMul * need * 1.35f); }
                }
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
            if (system != null)
                system.Play(true);
        }
    }
}
