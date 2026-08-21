using System.Collections.Generic;
using UnityEngine;

namespace GridSystem
{
    /// <summary>
    /// 게임에 존재하는 맵 목록(순서 = 맵 인덱스, 네트워크로 인덱스만 동기화).
    /// 반드시 Resources/MapCatalog.asset 하나만 존재해야 함 — Extract 툴이 자동 생성/등록.
    /// </summary>
    [CreateAssetMenu(menuName = "Jobsnail/Map Catalog", fileName = "MapCatalog")]
    public class MapCatalog : ScriptableObject
    {
        [SerializeField] private List<MapDef> m_Maps = new();

        public IReadOnlyList<MapDef> Maps => m_Maps;
        public int Count => m_Maps.Count;

        public MapDef Get(int index) =>
            (index >= 0 && index < m_Maps.Count) ? m_Maps[index] : (m_Maps.Count > 0 ? m_Maps[0] : null);

        /// <summary>2vs2 전용 공터(경기장) 맵. 없으면 null — 호출부는 기존 동작(선택 맵 배경 대칭 복제)으로 폴백.</summary>
        public MapDef FindVersusArena()
        {
            for (int i = 0; i < m_Maps.Count; i++)
                if (m_Maps[i] != null && m_Maps[i].IsVersusArena) return m_Maps[i];
            return null;
        }

        private static MapCatalog s_Instance;
        public static MapCatalog Instance
        {
            get
            {
                if (s_Instance == null) s_Instance = Resources.Load<MapCatalog>("MapCatalog");
                return s_Instance;
            }
        }

#if UNITY_EDITOR
        public void EditorAdd(MapDef def)
        {
            if (def != null && !m_Maps.Contains(def)) m_Maps.Add(def);
        }
#endif
    }
}
