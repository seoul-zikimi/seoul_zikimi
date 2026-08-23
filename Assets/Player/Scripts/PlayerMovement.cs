using UnityEngine;

namespace Player
{
    [UnityEngine.RequireComponent(typeof(UnityEngine.Rigidbody))]
    public class PlayerMovement : MonoBehaviour
    {
        const float kJumpHeight = 1.1f;   // 점프 정점 높이(칸). 1칸 블록 위에 올라탈 수 있게 살짝 여유.
        const float kWallReach  = 0.7f;   // 벽 감지 거리(캡슐 반경+α)
        const float kClimbRayH  = 0.4f;   // 벽 감지 레이 높이(발 근처) — 발이 벽 위로 오르면 꼭대기로 판정

        private const int kCastMask = ~(1 << 2);   // Ignore Raycast 제외 — 앞에 든 화물(PlayerCarry)이 벽/바닥으로 안 잡히게
        private Rigidbody m_Rb;
        private PlayerCarry m_Carry;
        private PlayerConfigSO m_Config;
        private bool  m_IsClimbing;
        private float m_ClimbCooldown;    // 벽점프 직후 즉시 재부착 방지
        public bool IsClimbing => m_IsClimbing;

        /// <summary>외력 채널(남산 돌풍 등) — Move/Climb가 매 틱 입력 속도에 합산한다.
        /// Move가 linearVelocity를 통째로 덮어쓰므로 AddForce로는 지속 밀림이 불가능해 만든 통로.</summary>
        public Vector3 ExternalPush { get; set; }

        /// <summary>공중 최대 낙하 속도(착지 판정용 — 돌풍 추락 스턴이 읽는다).</summary>
        public float FallSpeed => m_FallSpeed;

        public void Init(PlayerConfigSO config)
        {
            m_Config = config; m_Rb = GetComponent<Rigidbody>();
            m_Splat = GetComponent<PlayerSplat>();   // 점프 발구름 스트레치용(스폰 시 부착됨)
        }

        private PlayerSplat m_Splat;
        private void JumpStretch()   // 이륙 순간 쭉 늘어남(착지 철푸덕과 짝) — 점프 아치 완성
        {
            if (m_Splat == null) m_Splat = GetComponent<PlayerSplat>();
            if (m_Splat != null) m_Splat.AddImpulse(2.5f);   // 착지 철푸덕(~2.0)과 짝 맞춤
        }

        // ── 착지 쫀득: 낙하 후 접지 순간 흙 팡 + '밟힌 것'이 디용(비계/블록) ──
        private bool m_WasGrounded = true;
        private float m_FallSpeed;   // 공중에서의 최대 낙하 속도

        private void Update()
        {
            if (m_Rb == null) return;
            bool g = IsGrounded();
            if (!g) m_FallSpeed = Mathf.Max(m_FallSpeed, -m_Rb.linearVelocity.y);
            else
            {
                if (!m_WasGrounded && m_FallSpeed > 4f)   // 어느 정도 떨어졌을 때만(계단 오르내림 제외)
                {
                    GridSystem.GridJuice.GroundHit(transform.position, 0.55f);
                    SquishLandedOn();
                    if (!m_Rb.isKinematic)   // 원격(kinematic)은 남의 착지 — 내 카메라는 내 착지에만 반응
                    {
                        GridSystem.GridJuice.FovPunch(Camera.main, -Mathf.Min(1.5f + m_FallSpeed * 0.45f, 6f));
                        var stun = GetComponent<PlayerStun>();   // 남산 돌풍: 밀린 채 추락했으면 착지 스턴
                        if (stun != null) stun.NotifyHardLanding(m_FallSpeed);
                    }
                }
                m_FallSpeed = 0f;
            }
            m_WasGrounded = g;
        }

        // 밟힌 대상 디용: 비계면 그 비주얼, 그리드 블록(~Solid)이면 그 셀의 블록 비주얼.
        private void SquishLandedOn()
        {
            foreach (var h in Physics.RaycastAll(transform.position + Vector3.up * 0.1f, Vector3.down,
                                                 0.5f, kCastMask, QueryTriggerInteraction.Ignore))
            {
                var t = h.collider.transform;
                if (t == transform || t.IsChildOf(transform)) continue;
                var go = h.collider.gameObject;
                if (go.name.StartsWith("~Scaffold"))
                    GridSystem.GridJuice.Squish(go, 0.10f);
                else if (go.name == "~Solid")
                {
                    var net = FindFirstObjectByType<GridSystem.GridNetwork>();
                    if (net != null)
                        GridSystem.GridJuice.Squish(
                            net.VisualAt(GridSystem.GridCoordinates.WorldToCell(h.point + Vector3.down * 0.05f)), 0.08f);
                }
                break;   // 첫 유효 대상만
            }
        }

        // 카메라 forward 기준 이동 (FixedUpdate에서 호출)
        public void Move(Vector2 input, Transform cameraArm, bool isSprinting = false)
        {
            Vector3 forward = Vector3.ProjectOnPlane(cameraArm.forward, Vector3.up).normalized;
            Vector3 right   = Vector3.ProjectOnPlane(cameraArm.right,   Vector3.up).normalized;
            Vector3 dir     = forward * input.y + right * input.x;
            if (dir.sqrMagnitude > 1f) dir.Normalize();
            if (m_Carry == null) m_Carry = GetComponent<PlayerCarry>();
            if (m_Carry != null && m_Carry.TryGetGroupMove(dir, out var group)) { dir = group; isSprinting = false; }   // 같이 들기: 전원 입력 평균(같은 방향=풀속도, 반대=상쇄)
            float speed = isSprinting ? m_Config.SprintSpeed : m_Config.MoveSpeed;
            speed *= GridSystem.ItemNetwork.LocalMoveMultiplier();   // 2vs2 아이템: 속도 버프/디버프(협동=1)
            if (m_Carry == null) m_Carry = GetComponent<PlayerCarry>();
            if (m_Carry != null) speed *= m_Carry.MoveMultiplier;     // 무거운 재료 혼자 들면 0.7배(동료가 붙으면 1)
            Vector3 v = dir * speed;
            if (m_Carry != null && v.sqrMagnitude > 1e-6f)   // 앞에 든 화물이 벽/블록에 박히면 그쪽 이동 취소(축별로 → 벽 따라 미끄러짐)
            {
                float dt = Time.fixedDeltaTime * 1.2f;   // 한 틱 앞 + 약간 여유
                if (m_Carry.CargoBlocked(v * dt))
                {
                    Vector3 vx = new Vector3(v.x, 0f, 0f), vz = new Vector3(0f, 0f, v.z);
                    bool okX = Mathf.Abs(v.x) > 1e-4f && !m_Carry.CargoBlocked(vx * dt);
                    bool okZ = Mathf.Abs(v.z) > 1e-4f && !m_Carry.CargoBlocked(vz * dt);
                    v = (okX ? vx : Vector3.zero) + (okZ ? vz : Vector3.zero);
                }
            }
            m_Rb.linearVelocity = new Vector3(v.x + ExternalPush.x, m_Rb.linearVelocity.y, v.z + ExternalPush.z);   // Y 보존(중력·점프가 담당)
            if (m_Carry != null) m_Carry.ApplyTether(m_Rb, input.magnitude);   // 같이 들기: 자기 면 슬롯으로 스프링(반대로 당기면 서로 잡힘)
        }

        // 접지 상태에서만 위로 임펄스. WASD를 같이 누르면 수평속도가 살아 있어 '방향 점프'가 됨.
        public void Jump()
        {
            if (!IsGrounded()) return;
            float jumpV = Mathf.Sqrt(2f * Physics.gravity.magnitude * kJumpHeight);
            m_Rb.linearVelocity = new Vector3(m_Rb.linearVelocity.x, jumpV, m_Rb.linearVelocity.z);
            JumpStretch();   // 발구름 쭉
            if (SoundManager.Instance != null)
                SoundManager.Instance.PlaySFX(SFXType.Jump);
        }

        // 발밑 짧은 레이로 접지 판정(자기/자식 콜라이더는 제외).
        public bool IsGrounded()
        {
            var hits = Physics.RaycastAll(transform.position + Vector3.up * 0.1f, Vector3.down,
                                          0.3f, kCastMask, QueryTriggerInteraction.Ignore);
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
            foreach (var h in Physics.RaycastAll(origin, inDir, kWallReach, kCastMask, QueryTriggerInteraction.Ignore))
            {
                var t = h.collider.transform;
                if (t == transform || t.IsChildOf(transform)) continue;   // 자기/자식 제외
                if (h.collider.CompareTag("Player")) continue;            // 다른 플레이어는 벽 아님(기어오르기 X → 바운스)
                if (h.collider.CompareTag("Boundary")) continue;          // 투명 경계벽은 기어오르기 X(탈출 방지)
                return true;
            }
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
            // 돌풍은 매달린 상태에도 작용 — 벽에서 밀려나면 다음 틱 WallInFront가 실패해 자연스럽게 떨어진다.
            m_Rb.linearVelocity = new Vector3(along.x + into.x + ExternalPush.x, vy, along.z + into.z + ExternalPush.z);
        }

        // 벽에서 점프 탈출: 벽 반대로 + 위로.
        public void ClimbJumpOff(Transform cameraArm)
        {
            Vector3 inDir = Vector3.ProjectOnPlane(cameraArm.forward, Vector3.up).normalized;
            float jumpV = Mathf.Sqrt(2f * Physics.gravity.magnitude * kJumpHeight);
            m_Rb.linearVelocity = -inDir * m_Config.MoveSpeed + Vector3.up * jumpV;
            JumpStretch();   // 벽차기도 발구름 쭉
            m_IsClimbing = false;
            m_ClimbCooldown = 0.35f;
        }
    }
}
