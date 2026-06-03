using UnityEngine;

namespace Player
{
    public class PlayerDustTrail : MonoBehaviour
    {
        [SerializeField] private GameObject m_SmokePrefab;

        private ParticleSystem  m_Ps;
        private TrailRenderer[] m_SprintTrails;
        private PlayerConfigSO  m_Config;

        public GameObject SmokePrefab { get => m_SmokePrefab; set => m_SmokePrefab = value; }

        public void Init(PlayerConfigSO config)
        {
            m_Config = config;

            if (m_SmokePrefab != null)
            {
                var go = Instantiate(m_SmokePrefab, transform);
                go.transform.localPosition = new Vector3(0, 0.05f, 0);

                m_Ps = go.GetComponent<ParticleSystem>();
                if (m_Ps != null)
                {
                    var shape = m_Ps.shape;
                    shape.enabled   = true;
                    shape.shapeType = ParticleSystemShapeType.Circle;
                    shape.radius    = 0.3f;
                    shape.rotation  = new Vector3(90f, 0f, 0f);

                    var main = m_Ps.main;
                    main.simulationSpace = ParticleSystemSimulationSpace.World;
                    main.startSpeed      = new ParticleSystem.MinMaxCurve(0.05f, 0.2f);
                    main.startLifetime   = new ParticleSystem.MinMaxCurve(0.8f, 1.4f);
                    main.startColor      = new ParticleSystem.MinMaxGradient(
                        new Color(0.6f, 0.6f, 0.6f, 0.85f),
                        new Color(0.85f, 0.85f, 0.85f, 1.0f));

                    var emission = m_Ps.emission;
                    emission.rateOverTime     = 8f;
                    emission.rateOverDistance = 0f;
                    emission.enabled          = false;
                    m_Ps.Play();
                }
            }

            SetupSprintTrail();
        }

        private void SetupSprintTrail()
        {
            var offsets = new Vector3[]
            {
                new Vector3(-0.2f, 0.08f, -0.1f),
                new Vector3( 0.2f, 0.08f, -0.1f),
            };

            m_SprintTrails = new TrailRenderer[offsets.Length * 2];

            // vertex color 지원 머티리얼 — URP → Built-in 순서로 자동 탐색
            var trailMat = CreateTrailMaterial();

            var widthCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

            var coreGrad = new Gradient();
            coreGrad.SetKeys(
                new[] {
                    new GradientColorKey(new Color(1f, 1f, 0.8f), 0f),
                    new GradientColorKey(new Color(1f, 0.9f, 0f), 1f),
                },
                new[] {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(0f, 1f),
                }
            );

            var glowGrad = new Gradient();
            glowGrad.SetKeys(
                new[] {
                    new GradientColorKey(new Color(1f, 0.7f, 0f), 0f),
                    new GradientColorKey(new Color(1f, 0.4f, 0f), 1f),
                },
                new[] {
                    new GradientAlphaKey(0.55f, 0f),
                    new GradientAlphaKey(0f,    1f),
                }
            );

            for (int i = 0; i < offsets.Length; i++)
            {
                var coreGO = new GameObject($"SprintCore_{i}");
                coreGO.transform.SetParent(transform);
                coreGO.transform.localPosition = offsets[i];
                var core = coreGO.AddComponent<TrailRenderer>();
                core.time              = 0.30f;
                core.minVertexDistance = 0.02f;
                core.widthMultiplier   = 0.12f;
                core.widthCurve        = widthCurve;
                core.colorGradient     = coreGrad;
                core.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                core.receiveShadows    = false;
                if (trailMat != null) core.material = trailMat;
                core.emitting = false;
                m_SprintTrails[i] = core;

                var glowGO = new GameObject($"SprintGlow_{i}");
                glowGO.transform.SetParent(transform);
                glowGO.transform.localPosition = offsets[i];
                var glow = glowGO.AddComponent<TrailRenderer>();
                glow.time              = 0.22f;
                glow.minVertexDistance = 0.02f;
                glow.widthMultiplier   = 0.45f;
                glow.widthCurve        = widthCurve;
                glow.colorGradient     = glowGrad;
                glow.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                glow.receiveShadows    = false;
                if (trailMat != null) glow.material = trailMat;
                glow.emitting = false;
                m_SprintTrails[offsets.Length + i] = glow;
            }
        }

        // URP / Built-in 양쪽 지원: vertex color(gradient)가 반영되는 셰이더 자동 선택
        private static Material CreateTrailMaterial()
        {
            string[] candidates =
            {
                "Universal Render Pipeline/Particles/Unlit",
                "Universal Render Pipeline/Unlit",
                "Particles/Standard Unlit",
                "Sprites/Default",
                "Unlit/Transparent",
            };
            foreach (var name in candidates)
            {
                var shader = Shader.Find(name);
                if (shader != null) return new Material(shader);
            }
            return null;
        }

        // 이동/스프린트 상태를 받아 먼지·트레일 emission 적용.
        // owner는 Rigidbody 속도로, 원격은 NetworkVariable 복제값으로 PlayerUnit이 호출.
        public void Apply(bool isMoving, bool isSprinting)
        {
            // 회색 먼지 — 스프린트 중 끔
            if (m_Ps != null)
            {
                var dustEmission = m_Ps.emission;
                dustEmission.enabled = isMoving && !isSprinting;

                var main = m_Ps.main;
                main.startSize = m_Config.DustSize;
            }

            

            if (m_SprintTrails != null)
            {
                bool emit = isMoving && isSprinting;
                int  half = m_SprintTrails.Length / 2;
                for (int i = 0; i < m_SprintTrails.Length; i++)
                {
                    var t = m_SprintTrails[i];
                    if (t == null) continue;
                    t.emitting        = emit;
                    t.widthMultiplier = i < half ? m_Config.SprintCoreWidth : m_Config.SprintGlowWidth;
                    t.time            = m_Config.SprintTrailTime;
                }
            }
        }
    }
}
