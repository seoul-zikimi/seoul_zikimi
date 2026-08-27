using System.Collections.Generic;
using UnityEngine;

namespace GridSystem
{
    /// <summary>정답 셀 한 칸. 멀티칸 오브젝트도 익스포터가 칸 단위로 펼쳐 저장한다.</summary>
    [System.Serializable]
    public struct AnswerCell
    {
        public Vector3Int cell;
        public int materialId;
        public byte rotationStep;  // 0~3
    }

    /// <summary>
    /// (A) 오서링 그리드에서 익스포트된 정답. 종료 시 (B)RuntimeGrid와 셀 단위로 비교해 채점.
    /// 직렬화는 AnswerCell[] 배열, 런타임 조회는 셀→AnswerCell Dictionary로 (지연) 재구성.
    /// 요구 공정은 저장하지 않음 → MaterialDef.RequiredProcesses에서 파생.
    /// m_PresetCells: 게임 시작 시 기본 제공(씬에 미리 배치)되는 블럭 좌표 목록.
    ///   채점에서 제외되며(maxScore에 포함 안 됨), 플레이어가 지어야 할 부분만 100%로 계산.
    /// </summary>
    [CreateAssetMenu(fileName = "MapAnswerData", menuName = "Grid/MapAnswerData")]
    public class MapAnswerData : ScriptableObject, ISerializationCallbackReceiver
    {
        [SerializeField] private Vector3Int m_GridSize = new Vector3Int(8, 4, 8);
        [SerializeField] private AnswerCell[] m_Cells = new AnswerCell[0];
        [SerializeField] private Vector3Int[] m_PresetCells = new Vector3Int[0];   // 기본 제공 블럭 셀 좌표 — 채점 제외
        // preset 셀을 라운드 시작 시 '진짜 그리드 블록'(공정 완료 상태)으로 스폰할지.
        // 광통교처럼 배경 프리팹이 기제공 부분의 비주얼을 담당하는 맵은 false(기본값) 유지 — 켜면 이중 스폰된다.
        [SerializeField] private bool m_SpawnPresetBlocks = false;
        [SerializeField] private Vector3 m_StartPilePosition;
        [SerializeField] private float m_TimeLimitSeconds = 180f;
        [SerializeField] private Sprite m_AnswerImage;
        [SerializeField] private string m_DisplayName;   // 정산서에 표시할 구조물 이름(비우면 에셋 파일명)

        public Vector3Int GridSize => m_GridSize;
        public Vector3 StartPilePosition => m_StartPilePosition;
        public float TimeLimitSeconds => m_TimeLimitSeconds;
        public Sprite AnswerImage => m_AnswerImage;
        public string DisplayName => string.IsNullOrEmpty(m_DisplayName) ? name : m_DisplayName;
        public IReadOnlyList<AnswerCell> Cells => m_Cells;
        public bool SpawnPresetBlocks => m_SpawnPresetBlocks;

        [System.NonSerialized] private Dictionary<Vector3Int, AnswerCell> m_Lookup;
        [System.NonSerialized] private HashSet<Vector3Int> m_PresetSet;

        /// <summary>해당 셀이 기본 제공 블럭(채점 제외 대상)인지 확인.</summary>
        public bool IsPreset(Vector3Int cell)
        {
            if (m_PresetSet == null) RebuildPresetSet();
            return m_PresetSet.Contains(cell);
        }

        public bool TryGet(Vector3Int cell, out AnswerCell answer)
        {
            if (m_Lookup == null) RebuildLookup();
            return m_Lookup.TryGetValue(cell, out answer);
        }

        public IReadOnlyDictionary<Vector3Int, AnswerCell> Lookup
        {
            get
            {
                if (m_Lookup == null) RebuildLookup();
                return m_Lookup;
            }
        }

        private void RebuildLookup()
        {
            m_Lookup = new Dictionary<Vector3Int, AnswerCell>(m_Cells.Length);
            foreach (var c in m_Cells)
                m_Lookup[c.cell] = c;
        }

        private void RebuildPresetSet()
        {
            m_PresetSet = new HashSet<Vector3Int>(m_PresetCells ?? System.Array.Empty<Vector3Int>());
        }

        // ── ISerializationCallbackReceiver: 역직렬화 후 캐시 무효화 → 다음 접근 시 지연 재구성 ──
        public void OnBeforeSerialize() { }
        public void OnAfterDeserialize() { m_Lookup = null; m_PresetSet = null; }
    }
}
