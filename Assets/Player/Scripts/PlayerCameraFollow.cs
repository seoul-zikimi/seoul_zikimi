using UnityEngine;

namespace Player
{
    /// <summary>
    /// CameraArm에 붙임. LateUpdate에서 world position을 SmoothDamp로 덮어써서
    /// 플레이어를 부드럽게 따라가게 함 (Smooth Follow / Lazy Camera).
    /// </summary>
    public class PlayerCameraFollow : MonoBehaviour
    {
        [SerializeField] float m_SmoothTime = 0.15f;  // 작을수록 빠름 (0.05~0.3 권장)

        Transform m_Target;
        Vector3   m_CurrentPos;
        Vector3   m_Velocity;

        public void Init(Transform target)
        {
            m_Target     = target;
            m_CurrentPos = target.position;  // 스폰 첫 프레임은 순간이동 (텔레포트 방지)
        }

        void LateUpdate()
        {
            if (m_Target == null) return;

            m_CurrentPos = Vector3.SmoothDamp(
                m_CurrentPos,
                m_Target.position,
                ref m_Velocity,
                m_SmoothTime,
                Mathf.Infinity,
                Time.smoothDeltaTime  // 프레임 스파이크 완화
            );
            transform.position = m_CurrentPos;  // world position 덮어쓰기
        }
    }
}
