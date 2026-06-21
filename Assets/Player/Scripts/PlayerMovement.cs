using UnityEngine;

namespace Player
{
    [UnityEngine.RequireComponent(typeof(UnityEngine.Rigidbody))]
    public class PlayerMovement : MonoBehaviour
    {
        const float kJumpHeight = 1.1f;   // 점프 정점 높이(칸). 1칸 블록 위에 올라탈 수 있게 살짝 여유.
        const float kWallReach  = 0.7f;   // 벽 감지 거리(캡슐 반경+α)
        const float kClimbRayH  = 0.4f;   // 벽 감지 레이 높이(발 근처) — 발이 벽 위로 오르면 꼭대기로 판정

        private Rigidbody m_Rb;
        private PlayerConfigSO m_Config;
        private bool  m_IsClimbing;
        private float m_ClimbCooldown;    // 벽점프 직후 즉시 재부착 방지
        public bool IsClimbing => m_IsClimbing;

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

        // ── 벽 기어오르기 ───────────────────────────────────────────
        // 전방(수평 카메라 forward)에 벽이 있나 + 그 방향 반환.
        private bool WallInFront(Transform cameraArm, out Vector3 inDir)
        {
            inDir = Vector3.ProjectOnPlane(cameraArm.forward, Vector3.up).normalized;
            var origin = transform.position + Vector3.up * kClimbRayH;
            foreach (var h in Physics.RaycastAll(origin, inDir, kWallReach, ~0, QueryTriggerInteraction.Ignore))
                if (h.collider.transform != transform && !h.collider.transform.IsChildOf(transform))
                    return true;
            return false;
        }

        // 일반 이동 전 호출: 벽 보고 W면 기어오르기 진입. (벽점프 직후 쿨다운 동안은 안 붙음)
        public bool TryStartClimb(Vector2 input, Transform cameraArm)
        {
            if (m_IsClimbing) return true;
            if (m_ClimbCooldown > 0f) { m_ClimbCooldown -= Time.fixedDeltaTime; return false; }
            if (input.y > 0.1f && WallInFront(cameraArm, out _)) m_IsClimbing = true;
            return m_IsClimbing;
        }

        // 기어오르기 이동 + 탈출 (중력 off 상태, FixedUpdate).
        public void Climb(Vector2 input, Transform cameraArm)
        {
            if (!WallInFront(cameraArm, out Vector3 inDir))   // 발이 벽 위로(꼭대기) 또는 벽 벗어남 → 렛지로 넘기고 해제
            {
                m_Rb.linearVelocity = inDir * m_Config.MoveSpeed + Vector3.up * m_Config.ClimbSpeed;
                m_IsClimbing = false;
                return;
            }
            if (input.y < 0f && IsGrounded()) { m_IsClimbing = false; return; }   // 내려와 접지 → 해제

            Vector3 right = Vector3.ProjectOnPlane(cameraArm.right, Vector3.up).normalized;
            float vy      = input.y * m_Config.ClimbSpeed;            // W=↑ / S=↓
            Vector3 along = right * (input.x * m_Config.ClimbSpeed);  // A/D 좌우
            Vector3 into  = inDir * 0.5f;                            // 벽에 약하게 밀착(마찰로 못 오르는 것 방지)
            m_Rb.linearVelocity = new Vector3(along.x + into.x, vy, along.z + into.z);
        }

        // 벽에서 점프 탈출: 벽 반대로 + 위로.
        public void ClimbJumpOff(Transform cameraArm)
        {
            Vector3 inDir = Vector3.ProjectOnPlane(cameraArm.forward, Vector3.up).normalized;
            float jumpV = Mathf.Sqrt(2f * Physics.gravity.magnitude * kJumpHeight);
            m_Rb.linearVelocity = -inDir * m_Config.MoveSpeed + Vector3.up * jumpV;
            m_IsClimbing = false;
            m_ClimbCooldown = 0.35f;
        }
    }
}
