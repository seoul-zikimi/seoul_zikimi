using UnityEngine;

namespace GridSystem
{
    /// <summary>
    /// 2vs2 안개 아이템의 '상대 진영' 연출(로컬 전용). 안개에 걸린 팀 구역을 CFXR 연기 구름
    /// 파티클(Resources/Fx/ZoneFogCloud = cfxr smoke cloud x4 ab blurred 사본)로 덮어,
    /// 시전자·아군도 안개가 적용됐음을 볼 수 있게 한다.
    /// 당한 팀 본인 화면은 TeamWeatherFx의 카메라 포그가 담당(여긴 남의 시점 전용).
    /// </summary>
    public class ZoneFogFx : MonoBehaviour
    {
        private static readonly ZoneFogFx[] s_ByTeam = new ZoneFogFx[2];
        private static Material s_CloudMat;
        private static bool s_MatTried;

        private ParticleSystem m_Ps;

        /// <summary>team 구역 위에 안개 파티클 생성(이미 있으면 무시).</summary>
        public static void Show(int team, Bounds zone)
        {
            if (team < 0 || team > 1 || s_ByTeam[team] != null) return;
            var fx = new GameObject($"~ZoneFog_{team}").AddComponent<ZoneFogFx>();
            s_ByTeam[team] = fx;
            fx.Build(zone);
        }

        /// <summary>방출 중단 → 남은 구름이 자연 소멸한 뒤 제거.</summary>
        public static void Hide(int team)
        {
            if (team < 0 || team > 1 || s_ByTeam[team] == null) return;
            var fx = s_ByTeam[team];
            s_ByTeam[team] = null;   // 슬롯 즉시 비움(페이드 아웃 중 재시전 가능)
            if (fx.m_Ps != null) fx.m_Ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            Destroy(fx.gameObject, 9f);
        }

        private void Build(Bounds zone)
        {
            if (!s_MatTried)
            {
                s_MatTried = true;
                s_CloudMat = Resources.Load<Material>("Fx/ZoneFogCloud");
            }

            transform.position = new Vector3(zone.center.x, zone.min.y + 1.8f, zone.center.z);
            m_Ps = gameObject.AddComponent<ParticleSystem>();
            m_Ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);   // 설정 전 정지

            var main = m_Ps.main;
            main.loop = true;
            main.prewarm = true;   // 시전 즉시 가득 찬 안개(피드백은 즉각성이 생명)
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = 8f;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(4.5f, 8f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new Color(0.78f, 0.81f, 0.86f, 0.55f);
            main.maxParticles = 300;

            var emission = m_Ps.emission;
            emission.rateOverTime = zone.size.x * zone.size.z / 12f;   // 존 넓이에 비례(13×13 ≈ 14/s)

            var shape = m_Ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(zone.size.x, 3.2f, zone.size.z);

            // 유기적 표류 — 직선 이동 대신 노이즈로 스멀스멀
            var noise = m_Ps.noise;
            noise.enabled = true;
            noise.strength = 0.18f;
            noise.frequency = 0.08f;

            // 개별 구름 소프트 등장/소멸(전체 페이드 역할도 겸함)
            var col = m_Ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f),
                        new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;

            var rot = m_Ps.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-8f * Mathf.Deg2Rad, 8f * Mathf.Deg2Rad);

            // cfxr 텍스처는 2×2 시트 — 수명 동안 4프레임을 천천히 돌아 형태가 뭉게뭉게 변함
            var tsa = m_Ps.textureSheetAnimation;
            tsa.enabled = true;
            tsa.numTilesX = 2;
            tsa.numTilesY = 2;
            tsa.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0f, 1f, 1f));

            var rend = GetComponent<ParticleSystemRenderer>();
            if (s_CloudMat != null) rend.material = s_CloudMat;
            rend.maxParticleSize = 1.5f;   // 기본 0.5는 대형 구름을 화면에서 잘라먹는다
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            m_Ps.Play();
        }

        private void OnDestroy()
        {
            for (int t = 0; t < 2; t++) if (s_ByTeam[t] == this) s_ByTeam[t] = null;
        }
    }
}
