using UnityEngine;

namespace GridSystem
{
    /// <summary>
    /// (B) 런타임 그리드의 씬 호스트(싱글플레이). RuntimeGrid(순수 로직)를 보유하고
    /// 입력/비주얼을 붙인다. 멀티는 나중에 GridNetwork(NetworkBehaviour)가 같은 역할.
    /// </summary>
    public class GridManager : MonoBehaviour
    {
        [SerializeField] private Vector3Int m_GridSize = new Vector3Int(8, 4, 8);
        [SerializeField] private MaterialCatalog m_Catalog;
        [SerializeField] private MapAnswerData m_Answer;
        [SerializeField] private bool m_DrawGizmos = true;

        public RuntimeGrid Grid { get; private set; }
        public Vector3Int GridSize => m_GridSize;
        public MaterialCatalog Catalog => m_Catalog;
        public MapAnswerData Answer => m_Answer;

        private void Awake() => EnsureGrid();

        public void EnsureGrid()
        {
            Grid ??= new RuntimeGrid(m_GridSize);
        }

        private void OnDrawGizmos()
        {
            if (!m_DrawGizmos) return;

            float u = GridContract.Unit;
            Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.25f);
            for (int x = 0; x < m_GridSize.x; x++)
            for (int y = 0; y < m_GridSize.y; y++)
            for (int z = 0; z < m_GridSize.z; z++)
            {
                // CellToWorld는 셀의 min-corner → 중심은 +0.5u. 셀 = 1유닛 와이어 큐브.
                Vector3 center = GridCoordinates.CellToWorld(new Vector3Int(x, y, z)) + Vector3.one * 0.5f * u;
                Gizmos.DrawWireCube(center, Vector3.one * u);
            }
        }
    }
}
