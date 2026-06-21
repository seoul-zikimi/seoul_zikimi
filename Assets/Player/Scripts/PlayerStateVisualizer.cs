using UnityEngine;

namespace Player
{
    /// <summary>
    /// 임시 상태 표시기(달팽이·클립 없음). 실제 애니 대신 색/크기/반짝임으로 현재 상태를 눈에 보이게.
    /// 나중에 진짜 Animator 붙이면 이 컴포넌트는 빼면 됨. 내 캐릭터(owner)만.
    /// 색: idle=원래 / 걷기=연두 / 대시=붉은기 / 점프=하늘+세로늘림 / 기어=주황 / 들기=초록기 / 공정=노랑 반짝 / 배치·던지기=흰 팝.
    /// </summary>
    public class PlayerStateVisualizer : MonoBehaviour
    {
        private static readonly int s_BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int s_Color     = Shader.PropertyToID("_Color");

        private Renderer m_Rend;
        private Transform m_Visual;
        private bool m_CanScale;          // 물리 루트면 스케일 안 함
        private Vector3 m_BaseScale;
        private MaterialPropertyBlock m_Mpb;
        private PlayerMovement m_Move;
        private PlayerCarry m_Carry;
        private Rigidbody m_Rb;
        private float m_Pop;              // 배치/던지기 팝

        private void Awake()
        {
            m_Rend      = GetComponentInChildren<Renderer>();
            m_Visual    = m_Rend != null ? m_Rend.transform : transform;
            m_CanScale  = m_Visual != transform;     // 루트(물리) 스케일 회피
            m_BaseScale = m_Visual.localScale;
            m_Mpb = new MaterialPropertyBlock();
            m_Move = GetComponent<PlayerMovement>();
            m_Carry = GetComponent<PlayerCarry>();
            m_Rb = GetComponent<Rigidbody>();
        }

        private void OnEnable()
        {
            if (m_Carry == null) m_Carry = GetComponent<PlayerCarry>();
            if (m_Carry != null) { m_Carry.OnPlace += Pop; m_Carry.OnThrow += Pop; }
        }
        private void OnDisable()
        {
            if (m_Carry != null) { m_Carry.OnPlace -= Pop; m_Carry.OnThrow -= Pop; }
        }
        private void Pop() => m_Pop = 1f;

        private void Update()
        {
            if (m_Rend == null) return;
            if (m_Carry == null || !m_Carry.IsOwner) return;   // 내 캐릭터만
            if (m_Pop > 0f) m_Pop -= Time.deltaTime * 4f;

            bool grounded   = m_Move != null && m_Move.IsGrounded();
            bool climbing   = m_Move != null && m_Move.IsClimbing;
            Vector3 hv = m_Rb != null ? m_Rb.linearVelocity : Vector3.zero; hv.y = 0f;
            float speed     = hv.magnitude;
            bool holding    = m_Carry.IsHolding;
            bool processing = m_Carry.IsProcessing;
            bool active = processing || climbing || !grounded || speed > 0.2f || holding || m_Pop > 0f;

            if (!active)   // 완전 idle → 원래 외형 복원
            {
                m_Rend.SetPropertyBlock(null);
                if (m_CanScale) m_Visual.localScale = m_BaseScale;
                return;
            }

            float t = Time.time;
            Color col = new Color(0.8f, 0.8f, 0.85f);   // 기본
            float pulseSpeed = 0f, pulseAmp = 0f;
            Vector3 stretch = Vector3.one;

            if (processing)        { col = new Color(1f, 0.9f, 0.2f);   pulseSpeed = 20f; pulseAmp = 0.18f; } // 뚝딱(노랑 반짝)
            else if (climbing)     { col = new Color(1f, 0.55f, 0.2f);  pulseSpeed = 7f;  pulseAmp = 0.08f; } // 기어(주황)
            else if (!grounded)    { col = new Color(0.4f, 0.8f, 1f);   stretch = new Vector3(0.85f, 1.25f, 0.85f); } // 점프(하늘·세로늘림)
            else if (speed > 6f)   { col = new Color(1f, 0.5f, 0.4f);   pulseSpeed = 14f; pulseAmp = 0.12f; } // 대시(붉은기)
            else if (speed > 0.2f) { col = new Color(0.7f, 0.9f, 0.7f); pulseSpeed = 7f;  pulseAmp = 0.06f; } // 걷기(연두)

            if (holding)    col = Color.Lerp(col, new Color(0.3f, 0.9f, 0.4f), 0.45f);            // 들기(초록기)
            if (m_Pop > 0f) col = Color.Lerp(col, Color.white, Mathf.Max(0f, m_Pop));             // 배치/던지기 흰 팝

            m_Rend.GetPropertyBlock(m_Mpb);
            m_Mpb.SetColor(s_BaseColor, col);
            m_Mpb.SetColor(s_Color, col);
            m_Rend.SetPropertyBlock(m_Mpb);

            if (m_CanScale)
            {
                float pulse = 1f + (pulseSpeed > 0f ? Mathf.Sin(t * pulseSpeed) * pulseAmp : 0f);
                if (m_Pop > 0f) pulse *= 1f + Mathf.Max(0f, m_Pop) * 0.4f;   // 팝 펀치
                m_Visual.localScale = Vector3.Scale(m_BaseScale, stretch) * pulse;
            }
        }
    }
}
