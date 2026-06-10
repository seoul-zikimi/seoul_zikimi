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
    /// </summary>
    [CreateAssetMenu(fileName = "MapAnswerData", menuName = "Grid/MapAnswerData")]
    public class MapAnswerData : ScriptableObject, ISerializationCallbackReceiver
    {
        [SerializeField] private Vector3Int m_GridSize = new Vector3Int(8, 4, 8);
        [SerializeField] private AnswerCell[] m_Cells = new AnswerCell[0];
        [SerializeField] private Vector3 m_StartPilePosition;
        [SerializeField] private float m_TimeLimitSeconds = 180f;
        [SerializeField] private Sprite m_AnswerImage;

        public Vector3Int GridSize => m_GridSize;
        public Vector3 StartPilePosition => m_StartPilePosition;
        public float TimeLimitSeconds => m_TimeLimitSeconds;
        public Sprite AnswerImage => m_AnswerImage;
        public IReadOnlyList<AnswerCell> Cells => m_Cells;

        [System.NonSerialized] private Dictionary<Vector3Int, AnswerCell> m_Lookup;

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

        // ── ISerializationCallbackReceiver: 역직렬화 후 캐시 무효화 → 다음 접근 시 지연 재구성 ──
        public void OnBeforeSerialize() { }
        public void OnAfterDeserialize() => m_Lookup = null;
    }
}
