using System.Collections.Generic;
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
        [Tooltip("정답 맵 목록 — 게임 시작/재시작 때 이 중에서 랜덤으로 하나 선택(서버 권위).")]
        [SerializeField] private List<MapAnswerData> m_Answers = new();
        [SerializeField] private bool m_DrawGizmos = true;

        private int m_ActiveIndex;

        /// <summary>현재 선택된 정답이 바뀌었을 때(랜덤 선택/재시작). 비주얼 갱신용.</summary>
        public event System.Action OnAnswerChanged;

        public RuntimeGrid Grid { get; private set; }
        public Vector3Int GridSize => m_GridSize;
        public MaterialCatalog Catalog => m_Catalog;

        /// <summary>고를 수 있는 정답 개수.</summary>
        public int AnswerCount => m_Answers != null ? m_Answers.Count : 0;

        /// <summary>현재 선택된 정답(없으면 null).</summary>
        public MapAnswerData Answer =>
            (m_Answers != null && m_Answers.Count > 0)
                ? m_Answers[Mathf.Clamp(m_ActiveIndex, 0, m_Answers.Count - 1)]
                : null;

        /// <summary>정답 선택(서버가 뽑은 인덱스를 전 클라가 적용). 바뀌면 OnAnswerChanged 발생.</summary>
        public void SelectAnswer(int index)
        {
            int n = (m_Answers != null) ? m_Answers.Count : 0;
            if (n <= 0) { m_ActiveIndex = 0; return; }   // 리스트 없음 → 레거시 단일만
            index = ((index % n) + n) % n;
            if (index == m_ActiveIndex) return;
            m_ActiveIndex = index;
            OnAnswerChanged?.Invoke();
        }

        private void Awake()
        {
            GridContract.Origin = transform.position;   // 그리드 기준점 = 이 오브젝트 위치(맵 옮기면 정답·그리드도 따라감)
            EnsureGrid();
            CreateGround();   // 물리 점프로 Y가 풀려도 플레이어가 추락하지 않게 그리드 바닥 콜라이더 생성
        }

        // 그리드 XZ를 덮는, 보이지 않는 바닥 콜라이더(윗면 = 그리드 바닥 y). 렌더러 없음.
        private void CreateGround()
        {
            float u = GridContract.Unit;
            Vector3 baseW = GridCoordinates.CellToWorld(Vector3Int.zero);   // 그리드 min-corner(바닥)
            float sx = m_GridSize.x * u, sz = m_GridSize.z * u;
            const float thick = 1f, margin = 4f;

            var go = new GameObject("~Ground");
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(baseW.x + sx * 0.5f, baseW.y - thick * 0.5f, baseW.z + sz * 0.5f);
            go.AddComponent<BoxCollider>().size = new Vector3(sx + margin, thick, sz + margin);
        }

        public void EnsureGrid()
        {
            Grid ??= new RuntimeGrid(m_GridSize);
        }

        private void OnDrawGizmos()
        {
            GridContract.Origin = transform.position;   // 에디터에서도 옮긴 위치 반영(기즈모 off여도 동기화)
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
