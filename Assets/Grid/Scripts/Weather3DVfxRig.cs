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
