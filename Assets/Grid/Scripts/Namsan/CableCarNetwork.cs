using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace GridSystem
{
    /// <summary>
    /// 남산 케이블카 운송 — 이 맵에서는 하늘 배송 대신 곤돌라가 재료를 실어 나른다(기획서 §1).
    /// · 주문(MaterialDepot 검증 통과) → 대기열 → 빈 곤돌라가 산 아래에서 싣고 올라옴(CarFetchSeconds)
    /// · 하차장에는 한 번에 한 대만 문을 연다. 앞차가 떠나면 다음 차가 CarGapSeconds 뒤 도착.
    /// · 문이 열리면 화물이 '일반 바닥 픽업'으로 곤돌라 안에 스폰 — 기존 좌클릭 줍기를 그대로 쓴다.
    /// · CarTimeoutSeconds 안에 안 가져가면 곤돌라가 재료를 싣고 떠나며 삭제(재주문 가능, 비용 없음).
    /// 상태는 전이 시점에만 복제(NetworkList) — 곤돌라 위치는 각 클라가 서버 시각 기준으로 보간(대역폭 0).
    ///
    /// 2vs2: 레인(팀)별로 독립된 대기열·곤돌라·하차장을 둔다. 팀A는 마커 그대로, 팀B는 분할벽 점대칭 지점
    /// (기존 배송/미러 규칙과 동일)에 하차장·와이어가 대칭으로 생긴다. 팀 주문은 자기 레인으로만 배차된다.
    /// </summary>
    public class CableCarNetwork : NamsanGimmickBase
    {
        public enum CarPhase : byte { AtBase = 0, Inbound = 1, Waiting = 2, Docking = 3, Docked = 4, Closing = 5, Returning = 6 }

        public struct CarEntry : INetworkSerializable, System.IEquatable<CarEntry>
        {
            public int carId;
            public int materialId;    // -1 = 빈 차
            public byte phase;        // CarPhase
            public byte team;         // 0=팀A, 1=팀B (협동은 항상 0)
            public float phaseStart;  // 서버 시각(ServerTime) — 클라 보간 기준
            public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
            {
                s.SerializeValue(ref carId);
                s.SerializeValue(ref materialId);
                s.SerializeValue(ref phase);
                s.SerializeValue(ref team);
                s.SerializeValue(ref phaseStart);
            }
            public bool Equals(CarEntry o) =>
                carId == o.carId && materialId == o.materialId && phase == o.phase && team == o.team && phaseStart == o.phaseStart;
        }

        private const int kMaxLanes = 2;

        private readonly NetworkList<CarEntry> m_Cars = new();
        private readonly NetworkVariable<int> m_QueueCount =
            new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // 서버 전용 — 레인(팀)별
        private readonly Queue<int>[] m_Queue = { new(), new() };   // 레인별 대기 주문(materialId)
        private readonly float[] m_NextDispatchAt = new float[kMaxLanes];   // 레인별 뒷차 출발 간격 제어
        private ulong[] m_DockedPickup;                             // 곤돌라별(carId) 도킹 화물 pickupId(0=없음)
        private IPickupField m_Drop;   // 배송·회수 계약만(ServerDeliver/Remove/TryGetPickupPos) — GridInterfaces.cs 채택 규약

        private int m_Lanes = 1;   // 협동 1, 2vs2 2

        // 마커가 없을 때 폴백(하차장 기준 상대 위치 — 산 아래 방향)
        private static readonly Vector3 kFallbackBaseOffset = new Vector3(-10f, -5f, -8f);
        private const float kWireHeight = 4.5f;   // 양 끝(마커) 와이어 높이 — 중간은 철탑 꼭대기를 자동 경유
        private const float kHangDepth = 1.9f;    // 와이어 아래 곤돌라 '중심'까지 깊이(몸통 3.4 기준)
        private const float kWaitT = 0.85f;       // 대기 지점(하차장 직전)의 경로 비율
        private const float kUnloadDistance = 1.8f; // 화물이 이 이상 벗어나면 '내려진 것'으로 간주(킥 등)
        private const float kCargoLift = 0.1f;    // 화물 안착 높이(하차장 기준) — 곤돌라 아래 짧은 줄에 매달림

        /// <summary>대기 중 주문 수(HUD 표시용, 전 레인 합).</summary>
        public int QueueCount => m_QueueCount.Value;

        protected override void Awake()
        {
            base.Awake();
            m_Drop = GetComponent<MaterialDropField>();
        }

        protected override void OnGimmickSpawn()
        {
            m_Lanes = (Loop != null && Loop.IsVersus) ? 2 : 1;

            if (IsServer)
            {
                m_DockedPickup = new ulong[Config.CarCount * m_Lanes];
                if (m_Cars.Count == 0)
                {
                    for (int lane = 0; lane < m_Lanes; lane++)
                        for (int k = 0; k < Config.CarCount; k++)
                            m_Cars.Add(new CarEntry
                            {
                                carId = lane * Config.CarCount + k,
                                materialId = -1,
                                phase = (byte)CarPhase.AtBase,
                                team = (byte)lane,
                                phaseStart = Now
                            });
                }
            }
        }

        public override void OnNetworkDespawn()
        {
            DestroyVisuals();
            base.OnNetworkDespawn();
        }

        private float Now => NetworkManager != null ? (float)NetworkManager.ServerTime.Time : Time.time;

        // 클라이언트/서버 공통: 곤돌라 team 태그에서 실제 레인 수를 추론(늦참자·비주얼용).
        private int LaneCount()
        {
            int max = 0;
            foreach (var c in m_Cars)
                if (c.team > max) max = c.team;
            return Mathf.Max(m_Lanes, max + 1);
        }

        private int ClampTeam(int team) => (team >= 0 && team < m_Lanes) ? team : 0;

        // ── 서버: 주문 접수(MaterialDepot이 검증 후 넘김) ─────────────────────
        public void ServerEnqueue(int materialId, int team = 0)
        {
            if (!IsServer || !Active) return;
            int lane = ClampTeam(team);
            m_Queue[lane].Enqueue(materialId);
            m_QueueCount.Value = TotalQueued();
            Debug.Log($"[Namsan] 케이블카 주문 접수: 재료 id {materialId} (팀 {lane}, 대기 {m_Queue[lane].Count}건)");
        }

        private int TotalQueued()
        {
            int n = 0;
            for (int i = 0; i < m_Lanes; i++) n += m_Queue[i].Count;
            return n;
        }

        /// <summary>재시작용: 대기열·곤돌라 전부 초기화(서버).</summary>
        public void ServerReset()
        {
            if (!IsServer || !Active) return;
            for (int i = 0; i < kMaxLanes; i++)
            {
                m_Queue[i].Clear();
                m_NextDispatchAt[i] = 0f;
            }
            m_QueueCount.Value = 0;
            for (int i = 0; i < m_Cars.Count; i++)
            {
                int slot = IndexSlot(i);
                if (m_DockedPickup != null && slot < m_DockedPickup.Length && m_DockedPickup[slot] != 0)
                {
                    m_Drop.ServerRemove(m_DockedPickup[slot]);
                    m_DockedPickup[slot] = 0;
                }
                m_Cars[i] = new CarEntry
                {
                    carId = m_Cars[i].carId,
                    materialId = -1,
                    phase = (byte)CarPhase.AtBase,
                    team = m_Cars[i].team,
                    phaseStart = Now
                };
            }
        }

        // ── 상태머신(서버) + 비주얼(전 클라) ──────────────────────────────────
        private void Update()
        {
            if (!Active || !IsSpawned) return;
            if (IsServer) ServerTick();
            UpdateVisuals();
        }

        private float PhaseDuration(CarPhase p) => p switch
        {
            CarPhase.Inbound => Config.CarFetchSeconds,
            CarPhase.Docking => Config.CarGapSeconds,
            CarPhase.Docked => Config.CarTimeoutSeconds,
            CarPhase.Closing => Config.CarCloseSeconds,
            CarPhase.Returning => Config.CarFetchSeconds * 0.7f,
            _ => 0f,
        };

        // 같은 레인(팀)에 이미 도킹 중인 곤돌라가 있으면 그 하차장은 사용 중.
        private bool StationBusy(int team)
        {
            foreach (var c in m_Cars)
            {
                if (c.team != team) continue;
                var p = (CarPhase)c.phase;
                if (p == CarPhase.Docking || p == CarPhase.Docked || p == CarPhase.Closing) return true;
            }
            return false;
        }

        private void ServerTick()
        {
            // 게임이 건축 중이 아니면 새 배차만 멈춘다(이미 뜬 차는 마저 돈다).
            bool building = Loop != null && Loop.IsBuilding;

            for (int i = 0; i < m_Cars.Count; i++)
            {
                var c = m_Cars[i];
                int team = ClampTeam(c.team);
                var phase = (CarPhase)c.phase;
                float elapsed = Now - c.phaseStart;

                switch (phase)
                {
                    case CarPhase.AtBase:
                        // 연타 주문이어도 뒷차는 간격을 두고 출발(줄줄이 겹쳐 보이는 것 방지)
                        if (building && m_Queue[team].Count > 0 && Now >= m_NextDispatchAt[team])
                        {
                            c.materialId = m_Queue[team].Dequeue();
                            m_QueueCount.Value = TotalQueued();
                            m_NextDispatchAt[team] = Now + Config.CarDispatchGapSeconds;
                            SetPhase(ref c, CarPhase.Inbound);
                            m_Cars[i] = c;
                        }
                        break;

                    case CarPhase.Inbound:
                        if (elapsed >= PhaseDuration(phase))
                        {
                            SetPhase(ref c, CarPhase.Waiting);
                            m_Cars[i] = c;
                        }
                        break;

                    case CarPhase.Waiting:
                        if (!StationBusy(team))
                        {
                            SetPhase(ref c, CarPhase.Docking);
                            m_Cars[i] = c;
                        }
                        break;

                    case CarPhase.Docking:
                        if (elapsed >= PhaseDuration(phase))
                        {
                            // 문 열림 — 화물을 곤돌라 '안'(kCargoLift 높이)에 일반 픽업으로 스폰(기존 줍기 재사용).
                            // ServerDeliver가 착지 높이를 존중하므로 공중의 곤돌라 내부에 그대로 앉는다.
                            var inside = DockPos(team) + Vector3.up * kCargoLift;
                            m_DockedPickup[IndexSlot(i)] = m_Drop.ServerDeliver(c.materialId, inside, inside);
                            SetPhase(ref c, CarPhase.Docked);
                            m_Cars[i] = c;
                        }
                        break;

                    case CarPhase.Docked:
                    {
                        ulong pid = m_DockedPickup[IndexSlot(i)];
                        var cargoRest = DockPos(team) + Vector3.up * (kCargoLift + 0.5f);   // ServerDeliver의 실제 안착 지점
                        bool taken = pid == 0 || !m_Drop.TryGetPickupPos(pid, out var ppos)
                                     || (ppos - cargoRest).sqrMagnitude > kUnloadDistance * kUnloadDistance;
                        if (taken)
                        {
                            // 주워갔거나(엔트리 소멸) 발로 차서 밖으로 굴러감 — 하역 완료로 본다.
                            m_DockedPickup[IndexSlot(i)] = 0;
                            c.materialId = -1;
                            SetPhase(ref c, CarPhase.Closing);
                            m_Cars[i] = c;
                        }
                        else if (elapsed >= PhaseDuration(phase))
                        {
                            // 미수령 — 재료를 싣고 떠난다(삭제 처리, 재주문 가능). materialId는 연출용으로 유지.
                            m_Drop.ServerRemove(pid);
                            m_DockedPickup[IndexSlot(i)] = 0;
                            SetPhase(ref c, CarPhase.Closing);
                            m_Cars[i] = c;
                        }
                        break;
                    }

                    case CarPhase.Closing:
                        if (elapsed >= PhaseDuration(phase))
                        {
                            SetPhase(ref c, CarPhase.Returning);
                            m_Cars[i] = c;
                        }
                        break;

                    case CarPhase.Returning:
                        if (elapsed >= PhaseDuration(phase))
                        {
                            c.materialId = -1;   // 미수령 회수분도 여기서 완전히 사라진다
                            SetPhase(ref c, CarPhase.AtBase);
                            m_Cars[i] = c;
                        }
                        break;
                }
            }
        }

        private void SetPhase(ref CarEntry c, CarPhase p)
        {
            c.phase = (byte)p;
            c.phaseStart = Now;
        }

        // NetworkList 인덱스가 carId 순서와 같다는 보장은 초기화 방식상 성립하지만, 안전하게 carId로 슬롯을 잡는다.
        private int IndexSlot(int listIndex) => Mathf.Clamp(m_Cars[listIndex].carId, 0, m_DockedPickup.Length - 1);

        // ── 위치 계산(서버·클라 공통 — 팀A는 마커, 팀B는 분할벽 점대칭) ────────
        private Vector3 MirrorPoint(Vector3 p)
        {
            if (Grid == null) return p;
            var pivot = VersusBackground.MirrorPivot(Grid.ZoneSize, Grid.EffectiveSize);
            return new Vector3(2f * pivot.x - p.x, p.y, 2f * pivot.z - p.z);
        }

        private Vector3 StationPosA()
        {
            var t = FindSpot(NamsanSpots.CableCarStation);
            return t != null ? t.position : GridContract.Origin + new Vector3(-4f, 0f, 4f);
        }

        private Vector3 BasePosA()
        {
            var t = FindSpot(NamsanSpots.CableCarOrigin);
            return t != null ? t.position : StationPosA() + kFallbackBaseOffset;
        }

        private Vector3 StationPos(int team) => team == 1 ? MirrorPoint(StationPosA()) : StationPosA();
        private Vector3 BasePos(int team) => team == 1 ? MirrorPoint(BasePosA()) : BasePosA();
        private Vector3 DockPos(int team) => StationPos(team);

        // ── 와이어 폴리라인(레인별): 산 아래 → (남산_철탑 꼭대기 경유) → 하차장 ──
        // 팀A는 실제 경로를 만들고, 팀B는 그 경로를 분할벽 점대칭으로 미러링해 대칭을 보장한다.
        private readonly List<Vector3>[] m_WirePts = { new(), new() };
        private readonly List<float>[] m_WireCum = { new(), new() };
        private readonly float[] m_WireLen = new float[kMaxLanes];
        private float m_NextPathBuild;

        private void BuildWirePath()
        {
            BuildTeamAWire(m_WirePts[0]);
            FinishWirePath(0);

            if (LaneCount() >= 2)
            {
                m_WirePts[1].Clear();
                foreach (var p in m_WirePts[0])
                    m_WirePts[1].Add(MirrorPoint(p));
                FinishWirePath(1);
            }
        }

        // 씬 순회 캐시: 마커·철탑 Transform은 한 번 찾아두고 2초마다 '위치만' 재샘플한다.
        // 배경 스폰/파괴로 씬 계층 수(hierarchyCount 합)가 변할 때만 풀 재스캔(MirrorReflection과 같은 패턴).
        // 캐시가 비어 있으면(배경이 아직 안 떴으면) 종전처럼 2초마다 재시도한다.
        private readonly List<(int n, Transform tr)> m_WireMarkers = new();        // Spot_CableWireN(번호순 정렬)
        private readonly List<(Transform root, Renderer[] rends)> m_PylonCache = new();   // 남산_철탑 루트 + 렌더러
        private readonly List<GameObject> m_SceneRoots = new();                    // hierarchyCount 합산용 스크래치
        private readonly List<(float t, Vector3 p)> m_PylonTops = new();           // 경유점 스크래치
        private int m_ScannedHierarchy = -1;

        private int SceneHierarchyCount()
        {
            gameObject.scene.GetRootGameObjects(m_SceneRoots);   // 비할당 오버로드
            int n = 0;
            for (int i = 0; i < m_SceneRoots.Count; i++) n += m_SceneRoots[i].transform.hierarchyCount;
            return n;
        }

        private bool WireCacheValid()
        {
            for (int i = 0; i < m_WireMarkers.Count; i++)
                if (m_WireMarkers[i].tr == null) return false;
            for (int i = 0; i < m_PylonCache.Count; i++)
            {
                if (m_PylonCache[i].root == null) return false;
                var rends = m_PylonCache[i].rends;
                for (int r = 0; r < rends.Length; r++)
                    if (rends[r] == null) return false;
            }
            return true;
        }

        private void RescanWireTransforms()
        {
            m_WireMarkers.Clear();
            m_PylonCache.Clear();
            foreach (var tr in FindObjectsByType<Transform>(FindObjectsSortMode.None))
            {
                if (tr.name.StartsWith("Spot_CableWire"))
                {
                    int.TryParse(tr.name.Substring("Spot_CableWire".Length), out int n);
                    m_WireMarkers.Add((n, tr));
                }
                else if (tr.name.Contains("남산_철탑"))
                {
                    if (tr.parent != null && tr.parent.name.Contains("남산_철탑")) continue;   // 루트만
                    var rends = tr.GetComponentsInChildren<Renderer>();
                    if (rends.Length == 0) continue;
                    m_PylonCache.Add((tr, rends));
                }
            }
            m_WireMarkers.Sort((x, y) => x.n.CompareTo(y.n));
        }

        private void BuildTeamAWire(List<Vector3> pts)
        {
            pts.Clear();

            int hc = SceneHierarchyCount();
            bool empty = m_WireMarkers.Count == 0 && m_PylonCache.Count == 0;
            if (hc != m_ScannedHierarchy || empty || !WireCacheValid())
            {
                m_ScannedHierarchy = hc;
                RescanWireTransforms();
            }

            // 수동 경로 우선: 배경에 Spot_CableWire1, 2, 3… 마커가 있으면 그 위치(높이 포함)를
            // 번호 순서대로 '그대로' 통과한다 — 기획자가 선을 완전히 직접 그리는 방식.
            if (m_WireMarkers.Count >= 2)
            {
                foreach (var m in m_WireMarkers) pts.Add(m.tr.position);
                return;
            }

            // 자동 경로: 산 아래 → (철탑 꼭대기 경유) → 하차장
            var a = BasePos(0) + Vector3.up * kWireHeight;
            var b = StationPos(0) + Vector3.up * kWireHeight;
            pts.Add(a);

            var dir = b - a;
            float dl2 = Mathf.Max(1e-4f, dir.sqrMagnitude);
            m_PylonTops.Clear();
            foreach (var (_, rends) in m_PylonCache)
            {
                var bd = rends[0].bounds;
                foreach (var r in rends) bd.Encapsulate(r.bounds);
                var top = new Vector3(bd.center.x, bd.max.y - 0.15f, bd.center.z);   // 크로스암 살짝 아래
                float t = Vector3.Dot(top - a, dir) / dl2;
                if (t > 0.02f && t < 0.98f) m_PylonTops.Add((t, top));
            }
            m_PylonTops.Sort((x, y) => x.t.CompareTo(y.t));
            foreach (var p in m_PylonTops) pts.Add(p.p);
            pts.Add(b);
        }

        private void FinishWirePath(int team)
        {
            var cum = m_WireCum[team];
            var pts = m_WirePts[team];
            cum.Clear();
            cum.Add(0f);
            float len = 0f;
            for (int i = 1; i < pts.Count; i++)
            {
                len += Vector3.Distance(pts[i - 1], pts[i]);
                cum.Add(len);
            }
            m_WireLen[team] = len;
        }

        /// <summary>레인(team) 폴리라인 위 비율 t(0~1, 호 길이 기준)의 와이어 위치.</summary>
        private Vector3 WirePosAt(int team, float t)
        {
            var pts = m_WirePts[team];
            if (pts.Count < 2)
                return Vector3.Lerp(BasePos(team), StationPos(team), Mathf.Clamp01(t)) + Vector3.up * kWireHeight;
            var cum = m_WireCum[team];
            float d = Mathf.Clamp01(t) * m_WireLen[team];
            for (int i = 1; i < pts.Count; i++)
                if (d <= cum[i] || i == pts.Count - 1)
                {
                    float seg = cum[i] - cum[i - 1];
                    float u = seg > 1e-4f ? (d - cum[i - 1]) / seg : 0f;
                    return Vector3.Lerp(pts[i - 1], pts[i], u);
                }
            return pts[pts.Count - 1];
        }

        /// <summary>경로 비율 t(0=산 아래, 1=하차장)의 곤돌라 중심 위치. 순수 계산 — 테스트 대상.</summary>
        public static Vector3 CarPosAt(Vector3 basePos, Vector3 stationPos, float t)
        {
            var wireFrom = basePos + Vector3.up * kWireHeight;
            var wireTo = stationPos + Vector3.up * kWireHeight;
            return Vector3.Lerp(wireFrom, wireTo, Mathf.Clamp01(t)) + Vector3.down * kHangDepth;
        }

        /// <summary>페이즈·경과시간 → 경로 비율 t. 순수 계산 — 테스트 대상.</summary>
        public static float PathT(CarPhase phase, float elapsed, float fetchDur, float gapDur, float returnDur, int carId)
        {
            switch (phase)
            {
                case CarPhase.Inbound: return Mathf.Lerp(0f, kWaitT, fetchDur <= 0f ? 1f : Mathf.Clamp01(elapsed / fetchDur));
                case CarPhase.Waiting: return kWaitT - carId * 0.06f;   // 줄줄이 대기(겹침 방지 살짝 간격)
                case CarPhase.Docking: return Mathf.Lerp(kWaitT, 1f, gapDur <= 0f ? 1f : Mathf.Clamp01(elapsed / gapDur));
                case CarPhase.Docked:
                case CarPhase.Closing: return 1f;
                case CarPhase.Returning: return Mathf.Lerp(1f, 0f, returnDur <= 0f ? 1f : Mathf.Clamp01(elapsed / returnDur));
                default: return 0f;
            }
        }

        // ── 로컬 비주얼(레인별 와이어 + 곤돌라 + 남은 초) ─────────────────────
        private GameObject m_Root;
        private readonly List<LineRenderer> m_Wires = new();        // 레인별 와이어
        private readonly List<GameObject> m_CarVisuals = new();
        private readonly List<GameObject> m_CargoVisuals = new();   // 이동 중 화물 미니 표시(곤돌라 자식)
        private readonly List<TextMesh> m_Timers = new();
        private readonly List<int> m_CargoShownId = new();
        private readonly List<GameObject> m_Ropes = new();          // 곤돌라 아래 화물 매달림 줄
        private readonly List<PickupBody> m_ShrunkCargo = new();    // 곤돌라보다 큰 재료의 비주얼 압축(1회 적용 추적)

        private void EnsureVisuals()
        {
            if (m_Root != null) return;
            m_Root = new GameObject("~CableCars");

            int lanes = LaneCount();
            for (int lane = 0; lane < lanes; lane++)
            {
                var wireGo = new GameObject($"~Wire{lane}");
                wireGo.transform.SetParent(m_Root.transform);
                var wire = wireGo.AddComponent<LineRenderer>();
                wire.positionCount = 2;
                wire.startWidth = wire.endWidth = 0.07f;
                wire.material = RuntimeMat(new Color(0.2f, 0.2f, 0.22f));
                m_Wires.Add(wire);
            }

            // VARCO 곤돌라 모델(에디터 툴이 생성) — 없으면 빨간 큐브 폴백.
            // 루트는 항상 스케일 1 빈 오브젝트: 자식(팔·타이머·말풍선)이 비주얼 스케일에 안 찌그러진다.
            var gondolaModel = Resources.Load<GameObject>("Namsan/CableCarGondola");

            for (int i = 0; i < m_Cars.Count; i++)
            {
                var car = new GameObject($"~CableCar{i}");
                car.transform.SetParent(m_Root.transform);
                if (gondolaModel != null)
                {
                    var vis = Instantiate(gondolaModel, car.transform);
                    vis.transform.localPosition = Vector3.zero;
                    foreach (var c in car.GetComponentsInChildren<Collider>()) Destroy(c);
                }
                else
                {
                    var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    cube.name = "body";
                    cube.transform.SetParent(car.transform, false);
                    cube.transform.localScale = new Vector3(2.0f, 3.4f, 2.0f);   // 세로로 길쭉한 곤돌라
                    var col = cube.GetComponent<Collider>();
                    if (col != null) Destroy(col);
                    Tint(cube, new Color(0.85f, 0.25f, 0.2f));   // 남산 케이블카 레드
                }

                // 매달림 팔(와이어까지 기둥)
                var arm = GameObject.CreatePrimitive(PrimitiveType.Cube);
                arm.name = "arm";
                arm.transform.SetParent(car.transform);
                arm.transform.localScale = new Vector3(0.06f, 0.4f, 0.06f);   // 몸통 지붕(+1.7) ↔ 와이어(+1.9) 연결
                arm.transform.localPosition = new Vector3(0f, 1.8f, 0f);
                var acol = arm.GetComponent<Collider>();
                if (acol != null) Destroy(acol);
                Tint(arm, new Color(0.2f, 0.2f, 0.22f));

                // 화물 매달림 줄(곤돌라 바닥 → 화물) — 화물이 있을 때만 켠다
                var rope = GameObject.CreatePrimitive(PrimitiveType.Cube);
                rope.name = "rope";
                rope.transform.SetParent(car.transform, false);
                rope.transform.localScale = new Vector3(0.05f, 0.6f, 0.05f);
                rope.transform.localPosition = new Vector3(0f, -1.95f, 0f);   // 몸통 바닥(-1.7) → 화물
                Destroy(rope.GetComponent<Collider>());
                Tint(rope, new Color(0.2f, 0.2f, 0.22f));
                rope.SetActive(false);

                // 남은 초 표시(도킹 중만 보임)
                var tgo = new GameObject("timer");
                tgo.transform.SetParent(car.transform);
                tgo.transform.localPosition = new Vector3(0f, 2.4f, 0f);
                var tm = tgo.AddComponent<TextMesh>();
                tm.fontSize = 48;
                tm.characterSize = 0.045f;
                tm.anchor = TextAnchor.MiddleCenter;
                tm.alignment = TextAlignment.Center;
                tm.color = Color.white;
                var font = BuiltinFont();
                if (font != null)
                {
                    tm.font = font;
                    var mr = tgo.GetComponent<MeshRenderer>();
                    if (mr != null) mr.material = font.material;
                }

                m_CarVisuals.Add(car);
                m_Timers.Add(tm);
                m_CargoVisuals.Add(null);
                m_CargoShownId.Add(-1);
                m_Ropes.Add(rope);
                m_ShrunkCargo.Add(null);
            }
        }

        private void DestroyVisuals()
        {
            if (m_Root != null) Destroy(m_Root);
            m_Root = null;
            m_Wires.Clear();
            m_CarVisuals.Clear();
            m_CargoVisuals.Clear();
            m_Timers.Clear();
            m_CargoShownId.Clear();
            m_Ropes.Clear();
            m_ShrunkCargo.Clear();
        }

        private void UpdateVisuals()
        {
            EnsureVisuals();
            // 카 수 or 레인 수 변경(리셋 등) 방어
            if (m_CarVisuals.Count != m_Cars.Count || m_Wires.Count != LaneCount()) { DestroyVisuals(); EnsureVisuals(); }

            // 와이어 폴리라인 갱신(철탑 이동 반영, 2초 간격) + 레인별 선 렌더러 반영
            if (Time.time >= m_NextPathBuild)
            {
                m_NextPathBuild = Time.time + 2f;
                BuildWirePath();
                for (int lane = 0; lane < m_Wires.Count; lane++)
                {
                    var pts = m_WirePts[lane];
                    m_Wires[lane].positionCount = pts.Count;
                    for (int w = 0; w < pts.Count; w++) m_Wires[lane].SetPosition(w, pts[w]);
                }
            }

            var camT = Camera.main != null ? Camera.main.transform : null;
            for (int i = 0; i < m_Cars.Count; i++)
            {
                var c = m_Cars[i];
                int team = ClampTeam(c.team);
                var phase = (CarPhase)c.phase;
                float elapsed = Now - c.phaseStart;
                float t = PathT(phase, elapsed, Config.CarFetchSeconds, Config.CarGapSeconds, Config.CarFetchSeconds * 0.7f, c.carId);

                // 대기(AtBase) 곤돌라는 아예 숨긴다 — 주문하면 산 아래에서 올라오며 등장(3대 뭉침 방지)
                bool carVisible = phase != CarPhase.AtBase;
                if (m_CarVisuals[i].activeSelf != carVisible) m_CarVisuals[i].SetActive(carVisible);
                if (!carVisible) { UpdateCargoVisual(i, -1); continue; }

                // 곤돌라 = 자기 레인 와이어 폴리라인에 매달림(철탑 경유 경로 그대로 따라감)
                var pos = WirePosAt(team, t) + Vector3.down * kHangDepth;
                m_CarVisuals[i].transform.position = pos;

                // 진행 방향으로 몸통 회전(현재 구간의 수평 방향)
                var horiz = WirePosAt(team, Mathf.Min(1f, t + 0.02f)) - WirePosAt(team, Mathf.Max(0f, t - 0.02f));
                horiz.y = 0f;
                if (horiz.sqrMagnitude > 1e-4f)
                    m_CarVisuals[i].transform.rotation = Quaternion.LookRotation(horiz);

                // 매달림 줄: 화물이 있는 동안(이동 중 미니 or 도킹 중 실제 픽업) 보인다
                bool hasCargo = c.materialId >= 0 && phase != CarPhase.AtBase && phase != CarPhase.Returning
                                || (phase == CarPhase.Docked);
                if (m_Ropes[i].activeSelf != hasCargo) m_Ropes[i].SetActive(hasCargo);

                // 곤돌라보다 큰 재료(전망대·기반 등)는 안에 있는 동안 비주얼만 압축(판정·데이터 불변)
                if (phase == CarPhase.Docked)
                {
                    if (m_ShrunkCargo[i] == null && c.materialId >= 0)
                    {
                        var cdef = Grid != null && Grid.Catalog != null ? Grid.Catalog.GetById(c.materialId) : null;
                        float maxAxis = cdef != null ? Mathf.Max(cdef.Footprint.x, Mathf.Max(cdef.Footprint.y, cdef.Footprint.z)) : 1f;
                        if (maxAxis > 1.4f)
                        {
                            var rest = StationPos(team) + Vector3.up * (kCargoLift + 0.5f);
                            foreach (var pb in FindObjectsByType<PickupBody>(FindObjectsSortMode.None))
                                if (pb.MaterialId == c.materialId && (pb.transform.position - rest).sqrMagnitude < 1.0f)
                                {
                                    pb.SetVisualScale(1.4f / maxAxis);
                                    m_ShrunkCargo[i] = pb;
                                    break;
                                }
                        }
                    }
                }
                else m_ShrunkCargo[i] = null;

                // 이동 중 화물은 곤돌라 아래 매달려 간다(항상 보임) — 도킹하면 실제 픽업이 그 자리에 대신 생김
                bool showCargo = c.materialId >= 0 && phase != CarPhase.Docked;
                UpdateCargoVisual(i, showCargo ? c.materialId : -1);
                if (m_CargoVisuals[i] != null)
                    m_CargoVisuals[i].transform.localPosition =
                        new Vector3(Mathf.Sin(Time.time * 2f + i) * 0.08f, -2.0f, 0f);   // 살짝 흔들리는 매달림(몸통 아래)

                // 남은 초: 도킹(열림) 중 = 수령 타이머, 그 외 숨김
                if (phase == CarPhase.Docked)
                {
                    float remain = Mathf.Max(0f, Config.CarTimeoutSeconds - elapsed);
                    m_Timers[i].text = Mathf.CeilToInt(remain).ToString();
                    m_Timers[i].color = remain <= 5f ? new Color(1f, 0.35f, 0.3f) : Color.white;
                    if (camT != null) m_Timers[i].transform.rotation = Quaternion.LookRotation(m_Timers[i].transform.position - camT.position);
                }
                else m_Timers[i].text = "";
            }
        }

        private void UpdateCargoVisual(int i, int materialId)
        {
            if (m_CargoShownId[i] == materialId) return;
            m_CargoShownId[i] = materialId;
            if (m_CargoVisuals[i] != null) { Destroy(m_CargoVisuals[i]); m_CargoVisuals[i] = null; }
            if (materialId < 0) return;

            var def = Grid != null && Grid.Catalog != null ? Grid.Catalog.GetById(materialId) : null;
            GameObject vis;
            if (def != null && def.Prefab != null)
            {
                vis = Instantiate(def.Prefab, m_CarVisuals[i].transform);
                foreach (var col in vis.GetComponentsInChildren<Collider>()) Destroy(col);
                float maxAxis = Mathf.Max(1, Mathf.Max(def.Footprint.x, Mathf.Max(def.Footprint.y, def.Footprint.z)));
                vis.transform.localScale = Vector3.one * (0.9f / maxAxis);
                vis.transform.localPosition = new Vector3(0f, -2.0f, 0f);
            }
            else
            {
                vis = GameObject.CreatePrimitive(PrimitiveType.Cube);
                vis.transform.SetParent(m_CarVisuals[i].transform);
                var col = vis.GetComponent<Collider>();
                if (col != null) Destroy(col);
                vis.transform.localScale = Vector3.one * 0.7f;
                vis.transform.localPosition = new Vector3(0f, -2.0f, 0f);
                Tint(vis, new Color(0.72f, 0.72f, 0.72f));
            }
            m_CargoVisuals[i] = vis;
        }

        // 런타임 TextMesh용 내장 폰트(버전에 따라 이름이 다름)
        private static Font BuiltinFont()
        {
            try { return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); }
            catch { }
            try { return Resources.GetBuiltinResource<Font>("Arial.ttf"); }
            catch { }
            return null;
        }

        // 런타임 프리미티브용 URP Lit(빌드에서 기본 머티리얼 깨짐 방지 — MaterialDepot과 동일 관례)
        private static Material s_Mat;
        private static Material RuntimeMat(Color c)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            var m = sh != null ? new Material(sh) : new Material(Shader.Find("Standard"));
            m.hideFlags = HideFlags.HideAndDontSave;
            m.color = c;
            return m;
        }

        private static void Tint(GameObject go, Color c)
        {
            var r = go.GetComponent<Renderer>();
            if (r == null) return;
            if (s_Mat == null) s_Mat = RuntimeMat(Color.white);
            r.sharedMaterial = s_Mat;
            var mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            mpb.SetColor(Shader.PropertyToID("_BaseColor"), c);
            mpb.SetColor(Shader.PropertyToID("_Color"), c);
            r.SetPropertyBlock(mpb);
        }
    }
}
