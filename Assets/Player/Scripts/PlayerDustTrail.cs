using UnityEngine;

namespace Player
{
    public class PlayerDustTrail : MonoBehaviour
    {
        [SerializeField] private GameObject m_SmokePrefab;

        private ParticleSystem m_Ps;
        private PlayerConfigSO m_Config;
        private Rigidbody m_Rb;

        public GameObject SmokePrefab { get => m_SmokePrefab; set => m_SmokePrefab = value; }

        public void Init(PlayerConfigSO config, Rigidbody rb)
        {
            m_Config = config;
            m_Rb     = rb;

            if (m_SmokePrefab == null) return;

            var go = Instantiate(m_SmokePrefab, transform);
            go.transform.localPosition = new Vector3(0, 0.05f, 0);

            m_Ps = go.GetComponent<ParticleSystem>();
            if (m_Ps != null)
            {
                // Shape — 바닥 수평 원형 방출 (CFXR cone 위 방향 덮어쓰기)
                var shape = m_Ps.shape;
                shape.enabled   = true;
                shape.shapeType = ParticleSystemShapeType.Circle;
                shape.radius    = 0.3f;
                shape.rotation  = new Vector3(90f, 0f, 0f); // 수평 배치

                var main = m_Ps.main;
                main.simulationSpace = ParticleSystemSimulationSpace.World; // 경로에 남음
                main.startSpeed      = new ParticleSystem.MinMaxCurve(0.05f, 0.2f); // 거의 안 움직임
                main.startLifetime   = new ParticleSystem.MinMaxCurve(0.8f, 1.4f);  // 오래 잔류
                main.startColor      = new ParticleSystem.MinMaxGradient(
                    new Color(0.6f, 0.6f, 0.6f, 0.85f),
                    new Color(0.85f, 0.85f, 0.85f, 1.0f));

                var emission = m_Ps.emission;
                emission.rateOverTime     = 8f; // 초당 8개 덩어리 puff
                emission.rateOverDistance = 0f;
                emission.enabled          = false; // Update에서 속도 기반 on/off
                m_Ps.Play();
            }
        }

        private void Update()
        {
            if (m_Ps == null || m_Rb == null) return;

            bool isMoving = m_Rb.linearVelocity.magnitude > 0.2f;
            var emission = m_Ps.emission;
            emission.enabled = isMoving;

            // SO 값 실시간 반영
            var main = m_Ps.main;
            main.startSize = m_Config.DustSize;
        }
    }
}
