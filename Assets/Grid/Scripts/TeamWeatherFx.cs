using SeoulZikimi.Weather;
using UnityEngine;

namespace GridSystem
{
    /// <summary>
    /// 2vs2 날씨·안개의 '내 화면' 연출(로컬 전용). 서버가 복제한 팀 상태를 보고 켜고 끈다.
    /// 공중 연출은 Weather3DVfxRig 프리팹, 바닥 연출(웅덩이·눈·잎)은 WeatherGroundFx, 안개는 카메라 포그를 사용한다.
    /// 게임플레이 효과(미끄러짐·붕괴)는 서버가 판정하고, 여기서는 보이는 것만 담당한다.
    /// </summary>
    public class TeamWeatherFx : MonoBehaviour
    {
        private static TeamWeatherFx s_Instance;
        private WeatherKind m_BaseWeather = WeatherKind.Sunny;
        private WeatherKind m_TemporaryWeather = WeatherKind.Sunny;
        private WeatherKind m_Weather = WeatherKind.Sunny;
        private bool m_Fog;
        private Weather3DVfxRig m_Rig;
        private WeatherGroundFx m_Ground;
        private bool m_MissingRigLogged;

        // 안개 복원용(원래 씬 설정)
        private bool m_SavedFog;
        private Color m_SavedFogColor;
        private float m_SavedFogDensity;
        private FogMode m_SavedFogMode;          // 씬 기본은 Linear(비주얼 정리 툴) — 복구 안 하면 Exp2로 남아 맵이 계속 뿌옇게 됨
        private float m_SavedFogStart, m_SavedFogEnd;
        private bool m_FogSaved;

        /// <summary>없으면 만들어서 반환(씬 배치 불필요).</summary>
        public static TeamWeatherFx Get()
        {
            if (s_Instance == null)
                s_Instance = new GameObject("~TeamWeatherFx").AddComponent<TeamWeatherFx>();
            return s_Instance;
        }

        /// <summary>세션 전체에 적용되는 기본 날씨를 반영한다.</summary>
        public void SetBaseWeather(WeatherKind weather)
        {
            m_BaseWeather = weather;
            ApplyEffectiveWeather();
        }

        /// <summary>경쟁 아이템의 임시 날씨를 반영한다. Sunny면 세션 기본 날씨로 돌아간다.</summary>
        public void Set(WeatherKind weather, bool fog)
        {
            m_TemporaryWeather = weather;
            ApplyEffectiveWeather();
            if (m_Fog != fog) { m_Fog = fog; ApplyFog(); }
        }

        public static void ClearBaseWeather()
        {
            if (s_Instance != null)
                s_Instance.SetBaseWeather(WeatherKind.Sunny);
        }

        private void ApplyEffectiveWeather()
        {
            WeatherKind effective = m_TemporaryWeather != WeatherKind.Sunny
                ? m_TemporaryWeather : m_BaseWeather;
            if (m_Weather == effective) return;
            m_Weather = effective;
            EnsureRig();
            if (m_Rig != null)
                m_Rig.SetWeather(effective);
            EnsureGround();
            m_Ground.SetWeather(effective);
            ApplyAmbience(effective);
        }

        // ── 날씨 앰비언스 루프(로컬) ──────────────────────────
        // '내 화면'의 유효 날씨를 그대로 따른다 — 세션 기본 날씨든 아이템으로 맞은 임시 날씨든
        // 여기 하나로 흘러들어오므로, 아이템 태풍을 맞은 순간 태풍 소리가 켜지고 풀리면 꺼진다.
        private string m_AmbienceLoop;

        private static string AmbienceFor(WeatherKind w) => w switch
        {
            WeatherKind.Rain       => "WeatherRainLoop",      // 빗소리
            WeatherKind.StrongWind => "WeatherWindLoop",      // 바람소리
            WeatherKind.Typhoon    => "WeatherTyphoonLoop",   // 비+바람(태풍 전용 클립)
            _ => null,   // Sunny·Snow·낙엽·벚꽃 — 앰비언스 없음(눈은 무음이 오히려 분위기)
        };

        private void ApplyAmbience(WeatherKind weather)
        {
            string want = AmbienceFor(weather);
            if (want == m_AmbienceLoop) return;
            if (m_AmbienceLoop != null) GridSoundBridge.StopLoop(m_AmbienceLoop);
            m_AmbienceLoop = want;
            if (want != null) GridSoundBridge.PlayLoop(want);   // 2D — 하늘에서 내리는 소리라 거리 개념 없음
        }

        private void EnsureGround()
        {
            if (m_Ground != null) return;
            // 바닥 데칼은 월드에 고정되어야 하므로 카메라를 따라가는 리그와 분리한다.
            m_Ground = new GameObject("WeatherGroundFx").AddComponent<WeatherGroundFx>();
            m_Ground.transform.SetParent(transform, false);
        }

        private void EnsureRig()
        {
            if (m_Rig != null) return;
            Weather3DVfxRig prefab = Resources.Load<Weather3DVfxRig>(
                "UI_NEW/Weather/3D/Weather3DVfxRig");
            if (prefab != null)
            {
                m_Rig = Instantiate(prefab, transform);
                m_Rig.name = "Weather3DVfxRig";
                return;
            }

            if (!m_MissingRigLogged)
            {
                m_MissingRigLogged = true;
                Debug.LogWarning("[Weather] Weather3DVfxRig 프리팹을 찾을 수 없습니다.");
            }
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
                    m_SavedFogMode = RenderSettings.fogMode;
                    m_SavedFogStart = RenderSettings.fogStartDistance;
                    m_SavedFogEnd = RenderSettings.fogEndDistance;
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
                RenderSettings.fogMode = m_SavedFogMode;
                RenderSettings.fogStartDistance = m_SavedFogStart;
                RenderSettings.fogEndDistance = m_SavedFogEnd;
                m_FogSaved = false;
            }
        }

        private void OnDestroy()
        {
            m_Fog = false; ApplyFog();
            if (m_AmbienceLoop != null) GridSoundBridge.StopLoop(m_AmbienceLoop);   // 씬 이탈 시 앰비언스 끔
            if (s_Instance == this) s_Instance = null;
        }

        // 2vs2 아이템 날씨는 '당한 팀 진영'에만 내려야 한다 — ItemNetwork가 내 존 영역을 넣어줌.
        // null이면(협동/기본 날씨) 기존대로 맵 전체.
        private Bounds? m_TemporaryArea;
        public void SetTemporaryArea(Bounds? area) => m_TemporaryArea = area;

        private GridManager m_GridForArea;
        private void Update()
        {
            // 아이템 날씨는 공중(리그)·바닥(웅덩이/눈) 모두 내 진영에만
            Bounds? tempArea = m_TemporaryWeather != WeatherKind.Sunny ? m_TemporaryArea : null;
            if (m_Ground != null) m_Ground.SetAreaOverride(tempArea);

            if (m_Rig == null) return;
            if (tempArea.HasValue)
            {
                m_Rig.CoverArea(tempArea.Value);
                return;
            }
            // 그리드가 있으면 맵 전체(그리드+여백)에 내리게, 없으면(테스트 씬) 카메라 앞
            if (m_GridForArea == null) m_GridForArea = FindFirstObjectByType<GridManager>();
            if (m_GridForArea != null)
            {
                Vector3Int size = m_GridForArea.EffectiveSize;
                Vector3 o = GridContract.Origin;
                const float margin = 10f;
                var area = new Bounds(new Vector3(o.x + size.x * 0.5f, o.y + size.y * 0.5f, o.z + size.z * 0.5f),
                                      new Vector3(size.x + margin * 2f, size.y, size.z + margin * 2f));
                m_Rig.CoverArea(area);
            }
            else m_Rig.Follow(Camera.main);
        }
    }
}
