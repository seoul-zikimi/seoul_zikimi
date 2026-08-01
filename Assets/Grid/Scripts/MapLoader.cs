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

        /// <summary>배경/마커 적용 전이면 true — 플레이어 배치 등은 이게 풀릴 때까지 대기(맵별 위치 레이스 방지).</summary>
        public static bool Pending { get; private set; }

        private GameLoopManager m_Loop;
        private GameObject m_Spawned;
        private GameObject m_MirrorClone;   // 2vs2 배경 대칭 복제본(맵 교체 시 같이 정리)
        private int m_SpawnedIndex = -1;

        private void Awake() => Pending = true;
        private void OnDestroy() => Pending = false;   // 씬 전환 시 대기 해제

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
                if (m_SpawnedIndex < 0) { m_SpawnedIndex = idx; Pending = false; Debug.LogWarning("[MapLoader] MapCatalog/배경 프리팹 없음 — 배경 미스폰"); }
                return;
            }

            if (m_Spawned != null) Destroy(m_Spawned);   // 맵 교체(새 라운드에 다른 맵) 대응
            if (m_MirrorClone != null) Destroy(m_MirrorClone);
            ClearSpawnedSystemObjects();                // 이전 맵이 만든 작업대 등 정리
            m_Spawned = Instantiate(def.BackgroundPrefab);
            m_Spawned.name = $"~MapBackground({def.DisplayName})";
            m_SpawnedIndex = idx;
            ApplySpots(m_Spawned);                  // 맵 마커(Spot_*)대로 시스템 오브젝트 이동 — 후처리·플레이어 배치보다 먼저
            if (m_Loop.IsVersus) SetupVersusBackground(m_Spawned);   // 2vs2: 대칭 처리(전용 맵이면 통과)
            Pending = false;
            BackgroundSpawned?.Invoke(m_Spawned);   // 시야가림 페이드 콜라이더 등 후처리 트리거
        }

        // 2vs2 배경 대칭 처리 —
        // · 배경 프리팹 안에 "VersusSymmetric" 빈 오브젝트가 있으면: 이미 대칭으로 제작된 전용 맵 → 아무것도 안 함(권장).
        // · 없으면: 임시로 배경을 분할벽 중심 기준 180° 회전 복제(비주얼 전용). 가운데가 구조물로 막힐 수 있어
        //   2vs2 전용 맵(가운데 비운 대칭 배경)을 만드는 것을 권장.
        private void SetupVersusBackground(GameObject bg)
        {
            if (VersusBackground.IsAuthoredSymmetric(bg)) return;   // 전용 대칭 맵 — 복제 불필요

            var gm = FindFirstObjectByType<GridManager>();
            if (gm == null) return;
            m_MirrorClone = VersusBackground.CreateMirror(bg, VersusBackground.MirrorPivot(gm.ZoneSize, gm.EffectiveSize));
            Debug.Log("[MapLoader] 2vs2 배경 임시 대칭 복제 — 전용 대칭 맵을 만들면 VersusSymmetric 마커로 끌 수 있음");
        }

        // 배경 프리팹 안의 "Spot_<오브젝트이름>" 마커 위치·회전대로 씬의 시스템 오브젝트를 이동.
        // 예: Spot_GridManager, Spot_PaintStation, Spot_HammerStation, Spot_PlayerSpawnPoint.
        // 배송 지점(Spot_DeliveryZone)은 옮길 씬 오브젝트가 없어 MaterialDepot이 직접 추적한다.
        // 마커가 없으면 기존 씬 위치 유지(하위 호환). 이름으로 찾으므로 asmdef 타입 참조 불필요.
        // 마커 대상이 씬에 없으면 Resources/SystemObjects/<이름> 프리팹을 스폰한다.
        // 덕분에 작업대 같은 건 씬에 미리 둘 필요 없이 "맵에 마커를 두면 생긴다".
        // 다음 맵으로 갈아탈 때 같이 지우려고 목록으로 들고 있는다.
        private static readonly System.Collections.Generic.List<GameObject> s_SpawnedSystemObjects = new();

        private static GameObject SpawnSystemObject(string targetName)
        {
            var prefab = Resources.Load<GameObject>($"SystemObjects/{targetName}");
            if (prefab == null) return null;
            var go = Instantiate(prefab);
            go.name = targetName;   // (Clone) 제거 — 다음 맵에서 GameObject.Find로 다시 찾을 수 있게
            s_SpawnedSystemObjects.Add(go);
            Debug.Log($"[MapLoader] 시스템 오브젝트 스폰: {targetName}");
            return go;
        }

        private static void ClearSpawnedSystemObjects()
        {
            foreach (var go in s_SpawnedSystemObjects) if (go != null) Destroy(go);
            s_SpawnedSystemObjects.Clear();
        }

        private static void ApplySpots(GameObject bg)
        {
            foreach (var t in bg.GetComponentsInChildren<Transform>(true))
            {
                if (!t.name.StartsWith("Spot_")) continue;
                string targetName = t.name.Substring("Spot_".Length);

                // 배송 지점은 옮길 씬 오브젝트가 없다 — MaterialDepot이 이 마커를 직접 추적한다(라이브 반영).
                if (targetName == "DeliveryZone") continue;

                var target = GameObject.Find(targetName);
                if (target == null) target = SpawnSystemObject(targetName);   // 씬에 없으면 마커 자리에 새로 만든다
                if (target == null) { Debug.LogWarning($"[MapLoader] Spot 대상도 프리팹도 없음: {targetName} (Resources/SystemObjects/{targetName})"); continue; }

                // 그리드는 회전 불가(셀=월드축 정렬) — GridManager는 위치만 적용하고 기준점 갱신
                var gm = target.GetComponent<GridManager>();
                if (gm != null)
                {
                    target.transform.position = t.position;
                    GridContract.Origin = gm.transform.position;
                }
                else
                {
                    target.transform.SetPositionAndRotation(t.position, t.rotation);
                }

                Debug.Log($"[MapLoader] 맵 마커 적용: {targetName} → {t.position}");
            }
        }
    }
}
