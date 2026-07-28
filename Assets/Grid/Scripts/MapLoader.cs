using UnityEngine;

namespace GridSystem
{
    /// <summary>
    /// 씬을 늘리지 않는 맵 시스템의 로컬 배경 스포너.
    /// GameLoopManager.MapIndex(서버 동기화)가 확정되면 MapCatalog에서 배경 프리팹을 꺼내 1회 스폰.
    /// 씬에는 배경을 심지 않는다(Extract 툴이 기존 Background를 프리팹으로 승격시키고 제거).
    /// </summary>
    public class MapLoader : MonoBehaviour
    {
        /// <summary>배경 스폰 직후(루트 전달). 시야가림 페이드 등 후처리는 바깥(Assembly-CSharp)이 구독 — asmdef 역참조 회피.</summary>
        public static event System.Action<GameObject> BackgroundSpawned;

        private GameLoopManager m_Loop;
        private GameObject m_Spawned;
        private int m_SpawnedIndex = -1;

        private void Update()
        {
            if (m_Loop == null) m_Loop = FindFirstObjectByType<GameLoopManager>();
            if (m_Loop == null || !m_Loop.IsSpawned) return;   // 네트워크 확정 전 대기

            int idx = m_Loop.MapIndex;
            if (idx == m_SpawnedIndex) return;

            var catalog = MapCatalog.Instance;
            var def = catalog != null ? catalog.Get(idx) : null;
            if (def == null || def.BackgroundPrefab == null)
            {
                if (m_SpawnedIndex < 0) { m_SpawnedIndex = idx; Debug.LogWarning("[MapLoader] MapCatalog/배경 프리팹 없음 — 배경 미스폰"); }
                return;
            }

            if (m_Spawned != null) Destroy(m_Spawned);   // 맵 교체(새 라운드에 다른 맵) 대응
            m_Spawned = Instantiate(def.BackgroundPrefab);
            m_Spawned.name = $"~MapBackground({def.DisplayName})";
            m_SpawnedIndex = idx;
            BackgroundSpawned?.Invoke(m_Spawned);   // 시야가림 페이드 콜라이더 등 후처리 트리거
        }
    }
}
