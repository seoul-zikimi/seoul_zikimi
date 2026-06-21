using UnityEngine;

namespace Player
{
    /// <summary>
    /// 게임 상태 → Animator 파라미터 구동(스켈레톤). Animator/클립은 나중에 Body에 붙임 — 없으면 무동작.
    /// 우선 내 캐릭터(owner)만. 파라미터: Speed/Grounded/Climbing/ClimbDir/Holding/HoldingTool/Processing + PutDown/Throw(trigger).
    /// 원격 캐릭터 애니(복제·RPC)는 후속.
    /// </summary>
    [RequireComponent(typeof(PlayerMovement), typeof(PlayerCarry))]
    public class PlayerAnimator : MonoBehaviour
    {
        static readonly int P_Speed       = Animator.StringToHash("Speed");
        static readonly int P_Grounded    = Animator.StringToHash("Grounded");
        static readonly int P_Climbing    = Animator.StringToHash("Climbing");
        static readonly int P_ClimbDir    = Animator.StringToHash("ClimbDir");
        static readonly int P_Holding     = Animator.StringToHash("Holding");
        static readonly int P_HoldingTool = Animator.StringToHash("HoldingTool");
        static readonly int P_Processing  = Animator.StringToHash("Processing");
        static readonly int P_PutDown     = Animator.StringToHash("PutDown");
        static readonly int P_Throw       = Animator.StringToHash("Throw");

        private Animator m_Anim;
        private PlayerMovement m_Move;
        private PlayerCarry m_Carry;
        private PlayerInputHandler m_Input;
        private Rigidbody m_Rb;

        private void Awake()
        {
            m_Anim  = GetComponentInChildren<Animator>();   // 나중에 Body에 Animator 붙이면 자동 연결(없으면 null)
            m_Move  = GetComponent<PlayerMovement>();
            m_Carry = GetComponent<PlayerCarry>();
            m_Input = GetComponent<PlayerInputHandler>();
            m_Rb    = GetComponent<Rigidbody>();
        }

        private void OnEnable()
        {
            if (m_Carry == null) m_Carry = GetComponent<PlayerCarry>();
            if (m_Carry != null) { m_Carry.OnPlace += HandlePlace; m_Carry.OnThrow += HandleThrow; }
        }
        private void OnDisable()
        {
            if (m_Carry != null) { m_Carry.OnPlace -= HandlePlace; m_Carry.OnThrow -= HandleThrow; }
        }

        private void HandlePlace() { if (m_Anim != null) m_Anim.SetTrigger(P_PutDown); }
        private void HandleThrow() { if (m_Anim != null) m_Anim.SetTrigger(P_Throw); }

        private void Update()
        {
            if (m_Anim == null) return;                       // Animator 붙기 전엔 무동작
            if (m_Carry == null || !m_Carry.IsOwner) return;  // 우선 내 캐릭터만(원격은 후속)

            Vector3 h = m_Rb != null ? m_Rb.linearVelocity : Vector3.zero; h.y = 0f;
            m_Anim.SetFloat(P_Speed, h.magnitude);
            m_Anim.SetBool(P_Grounded, m_Move.IsGrounded());
            m_Anim.SetBool(P_Climbing, m_Move.IsClimbing);
            m_Anim.SetBool(P_Holding, m_Carry.IsHolding);
            m_Anim.SetBool(P_HoldingTool, m_Carry.IsHoldingTool);
            m_Anim.SetBool(P_Processing, m_Carry.IsProcessing);
            if (m_Input != null) m_Anim.SetFloat(P_ClimbDir, m_Input.MoveInput.y);
        }
    }
}
