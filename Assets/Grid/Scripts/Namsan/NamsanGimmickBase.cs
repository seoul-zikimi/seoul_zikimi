using Unity.Netcode;
using UnityEngine;

namespace GridSystem
{
    /// <summary>
    /// 남산 기믹이 쓰는 맵 마커 이름들. 전부 "옮길 씬 오브젝트가 없는" 마커라
    /// MapLoader.ApplySpots가 건너뛰고, 각 기믹 시스템이 직접 추적한다(Spot_DeliveryZone과 같은 체계).
    /// </summary>
    public static class NamsanSpots
    {
        public const string CableCarStation = "Spot_CableCarStation"; // 케이블카 하차장(재료 받는 곳)
        public const string CableCarOrigin  = "Spot_CableCarOrigin";  // 산 아래 출발점(와이어 반대쪽 끝)
        public const string ElevatorLower   = "Spot_ElevatorLower";   // 데크 밑 건물의 엘리베이터 문
        public const string ElevatorUpper   = "Spot_ElevatorUpper";   // 철제 전망대층의 엘리베이터 문

        /// <summary>MapLoader가 씬 오브젝트 배치를 건너뛰어야 하는 마커인가(전체 이름 "Spot_..." 기준).</summary>
        public static bool IsMarkerOnly(string fullName) =>
            fullName == CableCarStation || fullName == CableCarOrigin ||
            fullName == ElevatorLower || fullName == ElevatorUpper ||
            fullName.StartsWith("Spot_CableWire");   // 케이블카 선 수동 경유점(1, 2, 3…)
    }

    /// <summary>
    /// 남산 기믹 공통 골격. GameLoopManager가 런타임에 부착(서버/클라 동일 순서 — 씬 수정 불필요)하고,
    /// 맵 카드(MapDef)에 NamsanGimmickConfig가 꽂혀 있을 때만 동작한다(다른 맵에서는 완전히 잠잠).
    /// </summary>
    public abstract class NamsanGimmickBase : NetworkBehaviour
    {
        protected GameLoopManager Loop { get; private set; }
        protected GridManager Grid { get; private set; }
        protected NamsanGimmickConfig Config { get; private set; }

        /// <summary>이 맵에서 기믹이 켜져 있는가.</summary>
        public bool Active => Config != null;

        protected virtual void Awake()
        {
            Loop = GetComponent<GameLoopManager>();
            Grid = GetComponent<GridManager>();
        }

        public override void OnNetworkSpawn()
        {
            // GameLoopManager.OnNetworkSpawn(부착 순서상 먼저 실행)이 MapIndex를 확정한 뒤라 바로 조회 가능.
            var cat = MapCatalog.Instance;
            var def = (cat != null && Loop != null) ? cat.Get(Loop.MapIndex) : null;
            Config = def != null ? def.NamsanGimmicks : null;
            Debug.Log($"[Namsan] {GetType().Name}: 맵 {Loop?.MapIndex}({def?.DisplayName ?? "?"}) → {(Active ? "기믹 켜짐" : "기믹 없음(잠자기)")}");
            if (Active) OnGimmickSpawn();
        }

        /// <summary>기믹이 켜진 맵에서 스폰 완료 시 1회(서버·클라 공통).</summary>
        protected virtual void OnGimmickSpawn() { }

        // ── 마커 추적(배경이 런타임 스폰이라 늦게 생김 — MaterialDepot과 같은 0.5초 재탐색) ──
        private readonly System.Collections.Generic.Dictionary<string, Transform> m_Spots = new();
        private float m_NextSpotFind;

        /// <summary>배경 프리팹의 마커 Transform. 아직 못 찾았으면 null(0.5초 간격 재탐색).</summary>
        protected Transform FindSpot(string spotName)
        {
            if (m_Spots.TryGetValue(spotName, out var t) && t != null) return t;
            if (Time.time < m_NextSpotFind) return null;
            m_NextSpotFind = Time.time + 0.5f;
            var go = GameObject.Find(spotName);
            if (go == null) return null;
            m_Spots[spotName] = go.transform;
            Debug.Log($"[Namsan] 마커 연결: {spotName} = {go.transform.position}");
            return go.transform;
        }
    }
}
