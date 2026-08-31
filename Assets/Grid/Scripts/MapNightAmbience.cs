using UnityEngine;
using UnityEngine.Rendering;

namespace GridSystem
{
    /// <summary>
    /// 맵 전용 '밤' 환경 오버라이드 — 배경 프리팹 루트에 붙여 두면
    /// 스폰(OnEnable) 때 씬의 하늘·안개·앰비언트·태양을 밤 값으로 바꾸고,
    /// 맵 교체·씬 전환(OnDisable) 때 원래 값으로 되돌린다.
    ///
    /// 씬(GameScene)은 전 맵이 공유하므로 씬 세팅은 '맑은 오후'(MapVisualPolishTool) 그대로 두고,
    /// 밤 컨셉 맵만 이걸로 갈아입는다. 2vs2는 공터(경기장) 배경을 쓰므로 밤이 새지 않는다.
    ///
    /// 태양 라이트는 새로 만들지 않고 씬의 Directional을 달빛으로 재사용한다 —
    /// FastSky가 이 라이트 방향으로 낮/저녁/밤(별)을 판정하므로, 고도를 낮춘 MoonEuler면
    /// 하늘도 따라서 밤이 된다(밤하늘 수치는 NightSky 머티리얼 에셋에서 조절).
    ///
    /// 날씨 아이템(TeamWeatherFx)의 안개 저장/복원과는 겹쳐도 안전 —
    /// 밤 적용이 먼저(맵 로드)라 날씨는 '밤 안개'를 저장했다가 '밤 안개'로 되돌린다.
    ///
    /// 맵 교체 시 Unity의 Destroy가 프레임 끝으로 미뤄져 '새 맵 OnEnable → 옛 맵 OnDisable'
    /// 순서가 될 수 있다 — 그래서 원본 저장/복원은 static 1벌로 관리하고,
    /// 마지막으로 적용한 인스턴스만 복원한다(옛 맵의 뒤늦은 OnDisable이 밤값을 덮지 않게).
    /// </summary>
    public class MapNightAmbience : MonoBehaviour
    {
        [Header("하늘")]
        [Tooltip("밤하늘 스카이박스(비우면 하늘은 안 바꾼다). 별·구름 수치는 이 머티리얼 에셋에서 조절.")]
        public Material NightSky;

        [Header("안개 (Linear) — 안개색 = 밤 지평선 색")]
        public Color FogColor = new Color(0.09f, 0.12f, 0.21f);
        public float FogStart = 70f;
        public float FogEnd   = 280f;

        [Header("앰비언트 (Trilight) — 너무 어두우면 플레이가 안 보인다. 카툰 밤은 '파랗게', 검게 말고")]
        public Color AmbientSky     = new Color(0.40f, 0.46f, 0.64f);   // 08/31 톤 업 — "빛이 어색·칙칙" 피드백
        public Color AmbientEquator = new Color(0.30f, 0.33f, 0.46f);
        public Color AmbientGround  = new Color(0.17f, 0.17f, 0.24f);

        [Header("달빛 (씬 Directional 재사용)")]
        public Color MoonColor     = new Color(0.66f, 0.74f, 0.94f);
        public float MoonIntensity = 0.55f;   // 그림자가 새까맣게 죽지 않게 살짝 올림
        [Tooltip("달 방향(오일러). 고도(x)를 낮게 두면 FastSky가 저녁→밤(별)으로 넘어간다.")]
        public Vector3 MoonEuler = new Vector3(14f, -35f, 0f);

        // ── 원복용 스냅샷(static 1벌 — 클래스 주석의 파괴 순서 문제 참고) ──
        private static MapNightAmbience s_Applied;   // 마지막으로 적용한 인스턴스
        private static bool s_Saved;
        private static Material s_Skybox;
        private static bool s_FogOn; private static FogMode s_FogMode;
        private static Color s_FogColor; private static float s_FogStart, s_FogEnd, s_FogDensity;
        private static AmbientMode s_AmbMode; private static float s_AmbIntensity;
        private static Color s_AmbSky, s_AmbEq, s_AmbGround;
        private static Light s_Sun;
        private static Color s_SunColor; private static float s_SunIntensity; private static Quaternion s_SunRot;

        private void OnEnable()
        {
            if (!s_Saved)
            {
                s_Skybox = RenderSettings.skybox;
                s_FogOn = RenderSettings.fog; s_FogMode = RenderSettings.fogMode;
                s_FogColor = RenderSettings.fogColor;
                s_FogStart = RenderSettings.fogStartDistance; s_FogEnd = RenderSettings.fogEndDistance;
                s_FogDensity = RenderSettings.fogDensity;
                s_AmbMode = RenderSettings.ambientMode; s_AmbIntensity = RenderSettings.ambientIntensity;
                s_AmbSky = RenderSettings.ambientSkyColor; s_AmbEq = RenderSettings.ambientEquatorColor;
                s_AmbGround = RenderSettings.ambientGroundColor;
                s_Sun = FindSun();
                if (s_Sun != null) { s_SunColor = s_Sun.color; s_SunIntensity = s_Sun.intensity; s_SunRot = s_Sun.transform.rotation; }
                s_Saved = true;
            }
            s_Applied = this;

            if (NightSky != null) RenderSettings.skybox = NightSky;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = FogColor;
            RenderSettings.fogStartDistance = FogStart;
            RenderSettings.fogEndDistance = FogEnd;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = AmbientSky;
            RenderSettings.ambientEquatorColor = AmbientEquator;
            RenderSettings.ambientGroundColor = AmbientGround;
            RenderSettings.ambientIntensity = 1f;

            var sun = s_Sun != null ? s_Sun : FindSun();
            if (sun != null)
            {
                sun.color = MoonColor;
                sun.intensity = MoonIntensity;
                sun.transform.rotation = Quaternion.Euler(MoonEuler);
            }
        }

        private void OnDisable()
        {
            // 내가 마지막 적용자일 때만 복원 — 맵 교체로 새 맵이 이미 적용했다면 조용히 물러난다.
            if (s_Applied != this) return;
            s_Applied = null;
            if (!s_Saved) return;
            s_Saved = false;

            RenderSettings.skybox = s_Skybox;
            RenderSettings.fog = s_FogOn;
            RenderSettings.fogMode = s_FogMode;
            RenderSettings.fogColor = s_FogColor;
            RenderSettings.fogStartDistance = s_FogStart;
            RenderSettings.fogEndDistance = s_FogEnd;
            RenderSettings.fogDensity = s_FogDensity;
            RenderSettings.ambientMode = s_AmbMode;
            RenderSettings.ambientSkyColor = s_AmbSky;
            RenderSettings.ambientEquatorColor = s_AmbEq;
            RenderSettings.ambientGroundColor = s_AmbGround;
            RenderSettings.ambientIntensity = s_AmbIntensity;
            if (s_Sun != null)
            {
                s_Sun.color = s_SunColor;
                s_Sun.intensity = s_SunIntensity;
                s_Sun.transform.rotation = s_SunRot;
            }
        }

        private static Light FindSun()
        {
            if (RenderSettings.sun != null) return RenderSettings.sun;
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional) return l;
            return null;
        }
    }
}
