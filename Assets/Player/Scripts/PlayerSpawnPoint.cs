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
        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.2f, 0.9f, 0.4f, 0.9f);
            Gizmos.DrawWireSphere(transform.position, 0.5f);
            Gizmos.DrawLine(transform.position, transform.position + Vector3.up * 2f);
            Gizmos.DrawLine(transform.position, transform.position + transform.forward * 1f); // 바라보는 방향 참고용
        }
    }
}
