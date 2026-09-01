using UnityEngine;

namespace Player
{
    /// <summary>
    /// 플레이어 스폰 위치 마커. 씬에 빈 GameObject 하나 두고 PlayerSpawnPoint를 붙인 뒤
    /// 원하는 위치로 옮기면, 그 자리에 플레이어가 스폰된다.
    /// (PlayerUnit.OnNetworkSpawn이 씬에서 이 마커를 찾아 사용. 없으면 그리드 중앙으로 fallback.)
    /// 위치는 인스펙터의 Transform Position 또는 씬 뷰에서 직접 끌어서 조정.
    /// </summary>
    public sealed class PlayerSpawnPoint : MonoBehaviour
    {
        [Tooltip("멀티 스폰 시 플레이어끼리 벌어질 최소 간격(m). 이 마커를 중심으로 인원수만큼 링 배치된다.")]
        [SerializeField] private float m_SpawnSpacing = 1.8f;

        /// <summary>플레이어 간 최소 간격(m). 0 이하면 분산 없이 마커 위치에 그대로 스폰.</summary>
        public float SpawnSpacing => m_SpawnSpacing;

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.2f, 0.9f, 0.4f, 0.9f);
            Gizmos.DrawWireSphere(transform.position, 0.5f);
            Gizmos.DrawLine(transform.position, transform.position + Vector3.up * 2f);
            Gizmos.DrawLine(transform.position, transform.position + transform.forward * 1f); // 바라보는 방향 참고용

            // 4인 기준 분산 링(실제 반지름은 인원수에 따라 변함) — 스폰 자리가 지형 밖으로 나가지 않는지 눈으로 확인용
            if (m_SpawnSpacing <= 0f) return;
            Gizmos.color = new Color(0.2f, 0.9f, 0.4f, 0.35f);
            float radius = m_SpawnSpacing / (2f * Mathf.Sin(Mathf.PI / 4f));
            Vector3 prev = transform.position + new Vector3(radius, 0f, 0f);
            for (int i = 1; i <= 24; i++)
            {
                float a = Mathf.PI * 2f * i / 24f;
                Vector3 cur = transform.position + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius;
                Gizmos.DrawLine(prev, cur);
                prev = cur;
            }
        }
    }
}
