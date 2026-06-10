using UnityEngine;

namespace GridSystem
{
    /// <summary>
    /// 바닥 재료의 '노답중력' 비주얼 — 서버 권위 목표 위치(target)를 향해 굴러간다(순수 로컬).
    /// 스폰 시 fromPos에서 떨어지고, 플레이어가 차면(서버가 target 갱신) 그 방향으로 데굴데굴 굴러감.
    /// 위치 권위는 MaterialDropField(NetworkList)에 있고, 이 컴포넌트는 보이는 모션만 담당.
    /// </summary>
    public class PickupBody : MonoBehaviour
    {
        private Vector3 m_Target;
        private float m_VVel;                       // 수직 속도(낙하)
        private const float kHorizSpeed = 7f;       // 굴러가는 속도
        private const float kGravity = 22f;         // 과장된 중력

        public void Init(Vector3 from, Vector3 target)
        {
            transform.position = from;
            transform.rotation = Quaternion.Euler(Random.Range(0f, 360f), Random.Range(0f, 360f), Random.Range(0f, 360f));
            m_Target = target;
            m_VVel = 0f;
        }

        public void SetTarget(Vector3 target) => m_Target = target;   // 킥 → 새 목표로 굴러감

        private void Update()
        {
            var pos = transform.position;

            // 수평: 목표(킥/낙하지점)로 굴러 이동
            var toH = new Vector3(m_Target.x - pos.x, 0f, m_Target.z - pos.z);
            float hd = toH.magnitude;
            Vector3 move = Vector3.zero;
            if (hd > 0.001f)
            {
                float step = Mathf.Min(hd, kHorizSpeed * Time.deltaTime);
                move = toH / hd * step;
                pos += move;
            }

            // 수직: 중력으로 목표 높이까지 낙하
            if (pos.y > m_Target.y + 0.001f)
            {
                m_VVel -= kGravity * Time.deltaTime;
                pos.y += m_VVel * Time.deltaTime;
                if (pos.y < m_Target.y) { pos.y = m_Target.y; m_VVel = 0f; }
            }
            else { pos.y = m_Target.y; m_VVel = 0f; }

            transform.position = pos;

            // 구르기: 수평 이동 방향에 수직인 축으로 회전
            if (move.sqrMagnitude > 1e-6f)
            {
                var axis = Vector3.Cross(Vector3.up, move.normalized);
                transform.Rotate(axis, move.magnitude * 220f, Space.World);
            }
        }
    }
}
