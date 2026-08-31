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

        /// <summary>맵 선택에서 "랜덤"을 뜻하는 센티널 인덱스(실제 맵이 아니다).
        /// 로비/방 생성에서는 이 값이 그대로 돌아다니고, 실제 맵은 게임 시작 시 서버가
        /// PickRandomPlayable()로 한 번 뽑아 확정한다(GameLoopManager.ResolvedHostMap).</summary>
        public const int RandomMapIndex = -1;

        public IReadOnlyList<MapDef> Maps => m_Maps;
        public int Count => m_Maps.Count;

        public MapDef Get(int index) =>
            (index >= 0 && index < m_Maps.Count) ? m_Maps[index] : (m_Maps.Count > 0 ? m_Maps[0] : null);

        /// <summary>플레이어가 직접 고를 수 있는 맵인지 — 2vs2 공터(대전 모드가 자동으로 씀)와
        /// 튜토리얼(설정창의 "튜토리얼 다시보기" 전용)은 목록에서 뺀다.</summary>
        public bool IsSelectable(int index)
        {
            var def = (index >= 0 && index < m_Maps.Count) ? m_Maps[index] : null;
            return def != null && !def.IsVersusArena && !def.IsTutorial;
        }

        /// <summary>'랜덤' 선택 시 실제로 플레이할 맵을 뽑는다(공터·튜토리얼 제외). 후보가 없으면 0.</summary>
        public int PickRandomPlayable()
        {
            var candidates = new List<int>();
            for (int i = 0; i < m_Maps.Count; i++)
                if (IsSelectable(i)) candidates.Add(i);
            return candidates.Count > 0 ? candidates[Random.Range(0, candidates.Count)] : 0;
        }

        /// <summary>2vs2 전용 공터(경기장) 맵. 없으면 null — 호출부는 기존 동작(선택 맵 배경 대칭 복제)으로 폴백.</summary>
        public MapDef FindVersusArena()
        {
            for (int i = 0; i < m_Maps.Count; i++)
                if (m_Maps[i] != null && m_Maps[i].IsVersusArena) return m_Maps[i];
            return null;
        }

        /// <summary>대전 모드 팀 구역(한 팀 몫, x는 아직 2배 전) 건축 영역 크기.
        /// 경기장(공터) 맵과 선택한 맵의 정답이 요구하는 크기 중 축별로 더 큰 값을 합성한다 —
        /// 경기장 크기만 쓰면 선택한 맵의 정답이 더 높거나 넓을 때 그 초과분이 범위 밖 판정으로 배치 불가가 된다.</summary>
        public Vector3Int VersusZoneGridSize(int selectedMapIndex)
        {
            var arena = FindVersusArena();
            var selected = Get(selectedMapIndex);
            Vector3Int size = (arena != null && arena.HasGridSize) ? arena.GridSize : default;
            if (selected != null && selected.HasGridSize)
            {
                size = size == default ? selected.GridSize : new Vector3Int(
                    Mathf.Max(size.x, selected.GridSize.x),
                    Mathf.Max(size.y, selected.GridSize.y),
                    Mathf.Max(size.z, selected.GridSize.z));
            }
            return size;
        }

        /// <summary>게임 → 로비 복귀 시 맵 모델 지연 로드 캐시 해제(모바일 메모리) — Resources.UnloadUnusedAssets와 함께 쓸 것.</summary>
        public void ReleaseHeavyCaches()
        {
            foreach (var m in m_Maps)
                if (m != null) m.ReleaseHeavyCache();
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
