using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace GridSystem
{
    /// <summary>
    /// DDP 기믹이 쓰는 맵 마커 이름들. 전부 "옮길 씬 오브젝트가 없는" 마커라
    /// MapLoader.ApplySpots가 건너뛰고, 각 기믹 시스템이 직접 추적한다(NamsanSpots·LotteSpots와 같은 체계).
    /// </summary>
    public static class DdpSpots
    {
        /// <summary>이간수문 물길의 경로 웨이포인트 — Spot_WaterChannel0, 1, 2… 순서대로 폴리라인(상류→하류).
        /// 2개 이상 있어야 물길이 성립한다.</summary>
        public const string WaterChannelPrefix = "Spot_WaterChannel";

        /// <summary>유구 발굴터 후보 지점 — Spot_DigSite0, 1, 2… 중 하나가 랜덤으로 드러난다.</summary>
        public const string DigSitePrefix = "Spot_DigSite";

        /// <summary>LED 장미 발판 — Spot_RosePad0, 1, 2… 순서대로. 순번이 개화 위상 오프셋이 된다.</summary>
        public const string RosePadPrefix = "Spot_RosePad";

        /// <summary>MapLoader가 씬 오브젝트 배치를 건너뛰어야 하는 마커인가(전체 이름 "Spot_..." 기준).</summary>
        public static bool IsMarkerOnly(string fullName) =>
            fullName.StartsWith(WaterChannelPrefix) ||
            fullName.StartsWith(DigSitePrefix) ||
            fullName.StartsWith(RosePadPrefix);
    }

    /// <summary>
    /// DDP 기믹 공통 골격. GameLoopManager가 런타임에 부착(서버/클라 동일 순서 — 씬 수정 불필요)하고,
    /// 맵 카드(MapDef)에 DdpGimmickConfig가 꽂혀 있을 때만 동작한다(다른 맵에서는 완전히 잠잠).
    ///
    /// NamsanGimmickBase와 같은 계약이지만, DDP 기믹은 '점 하나'가 아니라 '번호가 붙은 마커 묶음'
    /// (Spot_WaterChannel0..N 등)을 쓰기 때문에 폴리라인/목록 재탐색을 여기서 공통 처리한다.
    /// </summary>
    public abstract class DdpGimmickBase : NetworkBehaviour
    {
        protected GameLoopManager Loop { get; private set; }
        protected GridManager Grid { get; private set; }
        protected DdpGimmickConfig Config { get; private set; }

        /// <summary>이 맵에서 기믹이 켜져 있는가.</summary>
        public bool Active => Config != null;

        /// <summary>서버 기준 시각(전 클라 동일 계산의 기준). 네트워크 전이면 로컬 시간으로 폴백.</summary>
        protected float Now => NetworkManager != null ? (float)NetworkManager.ServerTime.Time : Time.time;

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
            Config = def != null ? def.DdpGimmicks : null;
            Debug.Log($"[DDP] {GetType().Name}: 맵 {Loop?.MapIndex}({def?.DisplayName ?? "?"}) → {(Active ? "기믹 켜짐" : "기믹 없음(잠자기)")}");
            if (Active) OnGimmickSpawn();
        }

        /// <summary>기믹이 켜진 맵에서 스폰 완료 시 1회(서버·클라 공통).</summary>
        protected virtual void OnGimmickSpawn() { }

        // ── 번호 마커 묶음 추적 ────────────────────────────────────────────
        // 배경은 런타임 스폰이라 늦게 생긴다 — 다 찾을 때까지 0.5초 간격 재탐색(MaterialDepot과 같은 체계).

        private float m_NextFind;

        /// <summary>"접두사 + 0,1,2…" 마커를 순번대로 모아 into에 채운다. 이미 찾았으면 아무것도 안 한다.
        /// 반환: 지금 사용 가능한 마커가 minCount개 이상인가.</summary>
        protected bool RefreshMarkers(string prefix, List<Vector3> into, int minCount)
        {
            if (into.Count >= minCount) return true;
            if (Time.time < m_NextFind) return false;
            m_NextFind = Time.time + 0.5f;

            into.Clear();
            for (int i = 0; ; i++)
            {
                var go = GameObject.Find(prefix + i);
                if (go == null) break;
                into.Add(go.transform.position);
            }
            if (into.Count < minCount) { into.Clear(); return false; }

            Debug.Log($"[DDP] 마커 연결: {prefix}0~{into.Count - 1} ({into.Count}개)");
            return true;
        }

        // ── 폴리라인 유틸(물길 경로) ───────────────────────────────────────

        /// <summary>웨이포인트 폴리라인의 총 길이.</summary>
        protected static float PathLength(List<Vector3> path)
        {
            float len = 0f;
            for (int i = 1; i < path.Count; i++) len += Vector3.Distance(path[i - 1], path[i]);
            return len;
        }

        /// <summary>월드 좌표에서 폴리라인까지의 최단 수평거리와, 그 지점의 진행 방향(하류).
        /// 경로가 2점 미만이면 false.</summary>
        protected static bool NearestOnPath(List<Vector3> path, Vector3 world,
                                            out Vector3 nearest, out Vector3 flowDir, out float horizDist)
        {
            nearest = default; flowDir = Vector3.forward; horizDist = float.MaxValue;
            if (path.Count < 2) return false;

            for (int i = 1; i < path.Count; i++)
            {
                var a = path[i - 1];
                var b = path[i];
                var ab = b - a;
                float len2 = ab.sqrMagnitude;
                float t = len2 > 1e-6f ? Mathf.Clamp01(Vector3.Dot(world - a, ab) / len2) : 0f;
                var p = a + ab * t;

                // 수평거리로만 판정 — 다리 위/아래 높이 차이는 호출부가 따로 본다.
                float d = new Vector2(world.x - p.x, world.z - p.z).magnitude;
                if (d >= horizDist) continue;

                horizDist = d;
                nearest = p;
                var dir = ab; dir.y = 0f;
                flowDir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;
            }
            return true;
        }
    }
}
