using UnityEngine;

namespace GridSystem
{
    /// <summary>
    /// 경복궁 전용: 1층 벽 링(y0~2)이 전부 지어지면 마루 받침 까치발(StubRoot)을 내민다.
    /// 배경 프리팹에 붙어 모든 피어에서 로컬로 동작 — 복제된 그리드 상태(GridNetwork.IsCellFree)만 읽으므로
    /// 네트워크 동기화가 따로 필요 없다(전 피어가 같은 상태를 보고 같은 프레임쯤 활성화).
    /// 까치발 콜라이더가 ExternalSupportBelow 지지 판정을 통과시켜 마루(y4) 배치가 가능해진다.
    /// → 시공 순서 강제: 1층 벽 완공 → 까치발 등장 → 마루 깔기.
    /// [08/28] 마루가 전부 프리셋으로 깔린 채 시작하면서 맵 툴이 StubRoot를 처음부터 활성으로 굽는다
    /// — 그 경우 이 컴포넌트는 첫 Update에서 스스로 꺼진다(마루 프리셋을 되돌릴 때를 대비해 남겨둠).
    /// </summary>
    public class GyeongbokgungCorbels : MonoBehaviour
    {
        [Tooltip("까치발 부모(초기 비활성). 1층 벽 링 완공 시 활성화된다.")]
        public GameObject StubRoot;

        private GridNetwork m_Grid;
        private float m_NextCheck;

        private void Update()
        {
            if (StubRoot == null || StubRoot.activeSelf) { enabled = false; return; }
            if (Time.time < m_NextCheck) return;
            m_NextCheck = Time.time + 1f;

            if (m_Grid == null) m_Grid = FindObjectOfType<GridNetwork>();
            if (m_Grid == null || !m_Grid.IsSpawned) return;

            if (WallRingComplete()) StubRoot.SetActive(true);
        }

        // 1층 벽 링: 앞뒤 x5..24 (z4, z15) + 좌우 z5..14 (x5, x24), y0..2 — GyeongbokgungMapTool.kPalace와 정합.
        private bool WallRingComplete()
        {
            for (int y = 0; y < 3; y++)
            {
                for (int x = 5; x <= 24; x++)
                    if (m_Grid.IsCellFree(new Vector3Int(x, y, 4)) || m_Grid.IsCellFree(new Vector3Int(x, y, 15)))
                        return false;
                for (int z = 5; z <= 14; z++)
                    if (m_Grid.IsCellFree(new Vector3Int(5, y, z)) || m_Grid.IsCellFree(new Vector3Int(24, y, z)))
                        return false;
            }
            return true;
        }
    }
}
