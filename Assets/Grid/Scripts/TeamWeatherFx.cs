using SeoulZikimi.Weather;
using UnityEngine;

namespace GridSystem
{
    /// <summary>
    /// 2vs2 날씨·안개의 '내 화면' 연출(로컬 전용). 서버가 복제한 팀 상태를 보고 켜고 끈다.
    /// 파티클은 코드 생성(에셋 불필요), 안개는 카메라 포그.
    /// 게임플레이 효과(미끄러짐·붕괴)는 서버가 판정하고, 여기서는 보이는 것만 담당한다.
    /// </summary>
    public class TeamWeatherFx : MonoBehaviour
    {
        private static TeamWeatherFx s_Instance;
        private WeatherKind m_Weather = WeatherKind.Sunny;
        private bool m_Fog;
        private GameObject m_Root;
        private float m_NextDrop;

        // 안개 복원용(원래 씬 설정)
        private bool m_SavedFog;
        private Color m_SavedFogColor;
        private float m_SavedFogDensity;
        private bool m_FogSaved;

        /// <summary>없으면 만들어서 반환(씬 배치 불필요).</summary>
        public static TeamWeatherFx Get()
        {
            if (s_Instance == null)
                s_Instance = new GameObject("~TeamWeatherFx").AddComponent<TeamWeatherFx>();
            return s_Instance;
        }

        /// <summary>내 팀에 걸린 날씨(Sunny면 없음)와 안개 여부를 반영한다.</summary>
        public void Set(WeatherKind weather, bool fog)
        {
            if (m_Weather != weather)
            {
                m_Weather = weather;
                if (m_Root != null) Destroy(m_Root);
                m_Root = weather == WeatherKind.Sunny ? null : new GameObject("~WeatherParticles");
            }
            if (m_Fog != fog) { m_Fog = fog; ApplyFog(); }
        }

        private void ApplyFog()
        {
            if (m_Fog)
            {
                if (!m_FogSaved)
                {
                    m_SavedFog = RenderSettings.fog;
                    m_SavedFogColor = RenderSettings.fogColor;
                    m_SavedFogDensity = RenderSettings.fogDensity;
                    m_FogSaved = true;
                }
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.ExponentialSquared;
                RenderSettings.fogColor = new Color(0.72f, 0.74f, 0.78f);
                RenderSettings.fogDensity = 0.12f;   // 앞이 잘 안 보일 정도
            }
            else if (m_FogSaved)
            {
                RenderSettings.fog = m_SavedFog;
                RenderSettings.fogColor = m_SavedFogColor;
                RenderSettings.fogDensity = m_SavedFogDensity;
                m_FogSaved = false;
            }
        }

        private void OnDestroy()
        {
            m_Fog = false; ApplyFog();
            if (m_Root != null) Destroy(m_Root);
            if (s_Instance == this) s_Instance = null;
        }

        private void Update()
        {
            if (m_Weather == WeatherKind.Sunny || m_Root == null) return;
            var cam = Camera.main;
            if (cam == null) return;

            // 카메라 앞 상공에서 조각을 떨어뜨린다(내 화면에만 보이는 연출).
            float interval = m_Weather == WeatherKind.Typhoon ? 0.02f
                           : m_Weather == WeatherKind.Rain ? 0.03f
                           : m_Weather == WeatherKind.StrongWind ? 0.06f : 0.05f;
            if (Time.time < m_NextDrop) return;
            m_NextDrop = Time.time + interval;

            for (int i = 0; i < 3; i++) SpawnDrop(cam);
        }

        private void SpawnDrop(Camera cam)
        {
            bool snow = m_Weather == WeatherKind.Snow;
            var origin = cam.transform.position + cam.transform.forward * 6f
                       + new Vector3(Random.Range(-8f, 8f), Random.Range(5f, 9f), Random.Range(-8f, 8f));

            var col = snow ? Color.white
                    : m_Weather == WeatherKind.Typhoon ? new Color(0.6f, 0.7f, 0.9f)
                                                       : new Color(0.65f, 0.8f, 1f);
            var fx = GridJuice.MakeBit(origin, snow ? 0.08f : 0.05f, col);
            fx.transform.SetParent(m_Root.transform, true);

            // 바람 계열은 옆으로 세게 흐른다.
            float side = m_Weather == WeatherKind.StrongWind ? 6f : m_Weather == WeatherKind.Typhoon ? 9f : 0.6f;
            fx.vel = new Vector3(side, snow ? -1.5f : -12f, side * 0.35f);
            fx.gravity = snow ? -0.6f : -6f;
            fx.life = snow ? 1.6f : 0.7f;
            fx.startAlpha = 0.85f;
            if (snow) { fx.spinDeg = 120f; fx.spinAxis = Random.onUnitSphere; }
        }
    }
}
