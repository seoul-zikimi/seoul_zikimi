using UnityEngine;

namespace Player
{
    /// <summary>
    /// 착지 철푸덕(도잉도잉): 모델과 루트 사이에 스케일 전용 래퍼(~SquashWrap)를 끼워
    /// 리깅/애니메이터와 무관하게 항상 시각 스퀴시가 적용되게 한다(지난 직접 스케일 실패의 우회).
    /// 점프/낙하=길쭉, 착지=낙하속도만큼 찌부→스프링 복귀, 달리기=통통.
    /// 위치 델타 기반이라 owner/원격 동일 동작. PlayerUnit이 스폰 시 부착.
    /// </summary>
    public class PlayerSplat : MonoBehaviour
    {
        const float kStretchPerVel = 0.035f;   // 공중 수직속도당 스트레치
        const float kMaxStretch    = 0.30f;
        const float kLandImpulse   = 0.95f;    // 착지 찌부 임펄스(낙하속도 비례) — 철푸덕 강도
        const float kMaxLandSpeed  = 10f;
        const float kSpring        = 240f;     // 도잉도잉 스프링
        const float kDamp          = 9f;       // 낮을수록 더 출렁

        Transform m_Wrap;      // 스케일 전용 래퍼
        Vector3 m_PrevPos;
        bool m_WasGrounded = true;
        float m_Cur, m_Vel;    // 변형량(+=길쭉, −=찌부)과 스프링 속도
        float m_FallSpeed;     // 공중 최대 낙하속도(착지 프레임엔 델타가 0이라 미리 기억)
        float m_FindRetry;

        void Start() { TryWrap(); m_PrevPos = transform.position; }

        /// <summary>외부 임펄스(점프 발구름 등): 양수=쭉 늘어남, 음수=찌부. 스프링에 속도로 주입.</summary>
        public void AddImpulse(float amount) => m_Vel += amount;

        // 모델(Animator 루트 또는 Body)을 찾아 래퍼 밑으로 옮긴다. 모델이 늦게 생기면 재시도.
        void TryWrap()
        {
            if (m_Wrap != null) return;

            Transform model = null;
            var anim = GetComponentInChildren<Animator>();
            if (anim != null && anim.transform != transform) model = anim.transform;
            if (model == null) model = transform.Find("Body");
            if (model == null)
            {
                var r = GetComponentInChildren<Renderer>();
                if (r != null && r.transform != transform && !r.name.StartsWith("~")) model = r.transform;
            }
            if (model == null || model == transform) return;

            m_Wrap = new GameObject("~SquashWrap").transform;
            m_Wrap.SetParent(model.parent, false);
            m_Wrap.localPosition = model.localPosition;   // 래퍼가 모델 자리(피벗=발)를 물려받음
            m_Wrap.localRotation = model.localRotation;
            m_Wrap.localScale = Vector3.one;
            model.SetParent(m_Wrap, false);
            model.localPosition = Vector3.zero;
            model.localRotation = Quaternion.identity;
        }

        void LateUpdate()
        {
            if (m_Wrap == null)
            {
                m_FindRetry -= Time.deltaTime;   // 모델이 런타임에 늦게 붙는 경우 대비
                if (m_FindRetry > 0f) return;
                m_FindRetry = 0.5f;
                TryWrap();
                if (m_Wrap == null) return;
            }

            float dt = Mathf.Min(Time.deltaTime, 1f / 30f);
            if (dt <= 0f) return;

            Vector3 v = (transform.position - m_PrevPos) / dt;
            m_PrevPos = transform.position;
            bool g = Grounded();

            float target = 0f;
            if (!g)
            {
                m_FallSpeed = Mathf.Max(m_FallSpeed, -v.y);
                target = Mathf.Min(Mathf.Abs(v.y) * kStretchPerVel, kMaxStretch);   // 공중=길쭉
            }
            else
            {
                if (!m_WasGrounded && m_FallSpeed > 3f)
                    m_Vel -= Mathf.Min(m_FallSpeed, kMaxLandSpeed) * kLandImpulse;   // 철푸덕!
                m_FallSpeed = 0f;
                // 걷기/달리기 중엔 아무것도 안 함 — 도잉은 점프·낙하 착지 전용
            }
            m_WasGrounded = g;

            // 감쇠 스프링 → 도잉도잉
            m_Vel += (target - m_Cur) * kSpring * dt;
            m_Vel *= Mathf.Exp(-kDamp * dt);
            m_Cur += m_Vel * dt;

            float s = Mathf.Clamp(m_Cur, -0.5f, 0.6f);
            float y = 1f + s;
            float xz = 1f - s * 0.6f;   // 부피 보존: 찌부되면 옆으로 퍼짐
            m_Wrap.localScale = new Vector3(xz, y, xz);
        }

        bool Grounded()
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
