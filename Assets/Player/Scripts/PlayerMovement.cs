using UnityEngine;

namespace Player
{
    [UnityEngine.RequireComponent(typeof(UnityEngine.Rigidbody))]
    public class PlayerMovement : MonoBehaviour
    {
        const float kJumpHeight = 1.1f;   // 점프 정점 높이(칸). 1칸 블록 위에 올라탈 수 있게 살짝 여유.

        private Rigidbody m_Rb;
        private PlayerConfigSO m_Config;

        public void Init(PlayerConfigSO config)
        {
            m_Config = config; m_Rb = GetComponent<Rigidbody>();
        }

        // 카메라 forward 기준 이동 (FixedUpdate에서 호출)
        public void Move(Vector2 input, Transform cameraArm, bool isSprinting = false)
        {
            Vector3 forward = Vector3.ProjectOnPlane(cameraArm.forward, Vector3.up).normalized;
            Vector3 right   = Vector3.ProjectOnPlane(cameraArm.right,   Vector3.up).normalized;
            Vector3 dir     = forward * input.y + right * input.x;
            if (dir.sqrMagnitude > 1f) dir.Normalize();
            float speed = isSprinting ? m_Config.SprintSpeed : m_Config.MoveSpeed;
            Vector3 v = dir * speed;
            m_Rb.linearVelocity = new Vector3(v.x, m_Rb.linearVelocity.y, v.z);   // Y 보존(중력·점프가 담당)
        }

        // 접지 상태에서만 위로 임펄스. WASD를 같이 누르면 수평속도가 살아 있어 '방향 점프'가 됨.
        public void Jump()
        {
            if (!IsGrounded()) return;
            float jumpV = Mathf.Sqrt(2f * Physics.gravity.magnitude * kJumpHeight);
            m_Rb.linearVelocity = new Vector3(m_Rb.linearVelocity.x, jumpV, m_Rb.linearVelocity.z);
            if (SoundManager.Instance != null)
                SoundManager.Instance.PlaySFX(SFXType.Jump);
        }

        // 발밑 짧은 레이로 접지 판정(자기/자식 콜라이더는 제외).
        private bool IsGrounded()
        {
            var hits = Physics.RaycastAll(transform.position + Vector3.up * 0.1f, Vector3.down,
                                          0.3f, ~0, QueryTriggerInteraction.Ignore);
            foreach (var h in hits)
                if (h.collider.transform != transform && !h.collider.transform.IsChildOf(transform))
                    return true;
            return false;
        }
    }
}
