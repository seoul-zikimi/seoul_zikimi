using UnityEngine;

namespace Player
{
    /// <summary>
    /// 비주얼 모델을 이동 방향(기어오르기 중엔 벽 방향)으로 부드럽게 회전 → '앞을 보게'.
    /// 물리 루트는 회전 고정이라 자식 모델(Animator transform)만 돌린다. 내 캐릭터(owner)만.
    /// 모델이 반대로(뒤로) 보면 m_YawOffset를 180으로.
    /// </summary>
    public class PlayerFacing : MonoBehaviour
    {
        [SerializeField] private float m_TurnSpeed = 12f;
        [SerializeField] private float m_YawOffset = 0f;   // 모델 forward가 +Z가 아니면 보정(예: 180)

        private Transform m_Visual;
        private Transform m_Arm;
        private Rigidbody m_Rb;
        private PlayerCarry m_Carry;
        private PlayerMovement m_Move;

        private void Awake()
        {
            var anim = GetComponentInChildren<Animator>();
            m_Visual = anim != null ? anim.transform : null;
            m_Arm = transform.Find("CameraArm");
            m_Rb = GetComponent<Rigidbody>();
            m_Carry = GetComponent<PlayerCarry>();
            m_Move = GetComponent<PlayerMovement>();
        }

        private void Update()
        {
            if (m_Visual == null)
            {
                var anim = GetComponentInChildren<Animator>();   // 모델이 늦게 붙어도 잡기
                if (anim != null) m_Visual = anim.transform; else return;
            }
            if (m_Carry == null || !m_Carry.IsOwner) return;

            Vector3 dir;
            if (m_Move != null && m_Move.IsClimbing && m_Arm != null)
                dir = Vector3.ProjectOnPlane(m_Arm.forward, Vector3.up);   // 기어오르기 → 벽 보기
            else
            {
                Vector3 v = m_Rb != null ? m_Rb.linearVelocity : Vector3.zero; v.y = 0f;
                if (v.sqrMagnitude < 0.04f) return;   // 거의 정지 → 마지막 방향 유지
                dir = v;
            }
            if (dir.sqrMagnitude < 1e-4f) return;

            Quaternion target = Quaternion.LookRotation(dir.normalized, Vector3.up) * Quaternion.Euler(0f, m_YawOffset, 0f);
            m_Visual.rotation = Quaternion.Slerp(m_Visual.rotation, target, 1f - Mathf.Exp(-m_TurnSpeed * Time.deltaTime));
        }
    }
}
