using System.Collections.Generic;
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
        private HashSet<int> m_Params;   // 컨트롤러에 실제 있는 파라미터(없는 거 SetX 하면 경고)

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

        private void HandlePlace() { if (Has(P_PutDown)) m_Anim.SetTrigger(P_PutDown); }
        private void HandleThrow() { if (Has(P_Throw))   m_Anim.SetTrigger(P_Throw); }

        // 컨트롤러에 있는 파라미터만 세팅(없으면 경고 → 무시). 지연 캐시.
        private bool Has(int hash)
        {
            if (m_Anim == null) return false;
            if (m_Params == null)
            {
                m_Params = new HashSet<int>();
                foreach (var p in m_Anim.parameters) m_Params.Add(p.nameHash);
            }
            return m_Params.Contains(hash);
        }

        private void Update()
        {
            if (m_Anim == null) return;                       // Animator 붙기 전엔 무동작
            if (m_Carry == null || !m_Carry.IsOwner) return;  // 우선 내 캐릭터만(원격은 후속)

            Vector3 h = m_Rb != null ? m_Rb.linearVelocity : Vector3.zero; h.y = 0f;
            if (Has(P_Speed))       m_Anim.SetFloat(P_Speed, h.magnitude);
            if (Has(P_Grounded))    m_Anim.SetBool(P_Grounded, m_Move.IsGrounded());
            if (Has(P_Climbing))    m_Anim.SetBool(P_Climbing, m_Move.IsClimbing);
            if (Has(P_Holding))     m_Anim.SetBool(P_Holding, m_Carry.IsHolding);
            if (Has(P_HoldingTool)) m_Anim.SetBool(P_HoldingTool, m_Carry.IsHoldingTool);
            if (Has(P_Processing))  m_Anim.SetBool(P_Processing, m_Carry.IsProcessing);
            if (m_Input != null && Has(P_ClimbDir)) m_Anim.SetFloat(P_ClimbDir, m_Input.MoveInput.y);
        }
    }
}
