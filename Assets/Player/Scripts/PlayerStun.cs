using UnityEngine;

namespace Player
{
    /// <summary>
    /// 돌풍 추락 스턴(남산 기믹) — '바람에 밀린 채 공중에 뜬' 플레이어가 착지하면 잠깐 행동 불능.
    /// PlayerBounce.IsBouncing과 같은 방식으로 PlayerUnit.FixedUpdate가 이동을 차단한다(오너 로컬).
    /// 스턴이 걸리면 들고 있던 재료/도구는 발밑에 떨어져 노답중력으로 굴러간다(분실 없음).
    /// </summary>
    public class PlayerStun : MonoBehaviour
    {
        private float m_Timer;
        private bool m_WindAirborne;   // 공중에서 바람에 밀린 적 있음(착지 시 스턴 조건)
        private PlayerCarry m_Carry;
        private const float kMinFallSpeed = 4f;   // 이만큼은 떨어져야 스턴(착지 FX 임계값과 동일 — 계단 오르내림 제외)

        public bool IsStunned => m_Timer > 0f;

        /// <summary>PlayerUnit(오너)이 FixedUpdate마다 호출 — 타이머와 '공중에서 밀림' 플래그를 굴린다.</summary>
        public void Tick(bool grounded, bool pushedByWind)
        {
            if (m_Timer > 0f)
            {
                m_Timer -= Time.fixedDeltaTime;
                return;
            }

            if (!grounded) { if (pushedByWind) m_WindAirborne = true; }
            else m_WindAirborne = false;   // 살포시 착지(스턴 없음) — 플래그만 정리
        }

        /// <summary>센 착지 통지 — PlayerMovement의 착지 감지(FX와 같은 지점)가 호출.
        /// FixedUpdate에서 낙하속도를 읽으면 Update의 리셋과 레이스가 나서, 감지 지점에서 직접 받는다.</summary>
        public void NotifyHardLanding(float fallSpeed)
        {
            if (m_Timer > 0f || !m_WindAirborne) return;
            m_WindAirborne = false;
            if (fallSpeed > kMinFallSpeed)
                Stun(GridSystem.GustNetwork.StunSecondsOrDefault);
        }

        /// <summary>즉시 스턴(초). 든 것을 떨어뜨리고 어질어질 연출.</summary>
        public void Stun(float seconds)
        {
            if (seconds <= 0f) return;
            m_Timer = seconds;

            if (m_Carry == null) m_Carry = GetComponent<PlayerCarry>();
            if (m_Carry != null) m_Carry.ForceDrop();   // 들고 있던 재료 → 발밑 픽업(굴러감)

            GridSystem.GridJuice.WorldToast(transform.position + Vector3.up * 2.2f, "어질어질…", new Color(1f, 0.9f, 0.3f));
            GridSystem.GridJuice.FovPunch(Camera.main, -3f);
            var splat = GetComponent<PlayerSplat>();
            if (splat != null) splat.AddImpulse(2.2f);   // 철푸덕
        }
    }
}
