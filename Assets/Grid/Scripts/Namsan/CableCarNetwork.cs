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
    /// </summary>
    public class CableCarNetwork : NamsanGimmickBase
    {
        public enum CarPhase : byte { AtBase = 0, Inbound = 1, Waiting = 2, Docking = 3, Docked = 4, Closing = 5, Returning = 6 }

        public struct CarEntry : INetworkSerializable, System.IEquatable<CarEntry>
        {
            public int carId;
            public int materialId;    // -1 = 빈 차
            public byte phase;        // CarPhase
            public float phaseStart;  // 서버 시각(ServerTime) — 클라 보간 기준
            public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
            {
                s.SerializeValue(ref carId);
                s.SerializeValue(ref materialId);
                s.SerializeValue(ref phase);
                s.SerializeValue(ref phaseStart);
            }
            public bool Equals(CarEntry o) =>
                carId == o.carId && materialId == o.materialId && phase == o.phase && phaseStart == o.phaseStart;
        }

        private readonly NetworkList<CarEntry> m_Cars = new();
        private readonly NetworkVariable<int> m_QueueCount =
            new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // 서버 전용
        private readonly Queue<int> m_Queue = new();          // 대기 주문(materialId)
        private ulong[] m_DockedPickup;                       // 곤돌라별 도킹 화물 pickupId(0=없음)
        private float m_NextDispatchAt;                       // 연타 주문 시 뒷차 출발 간격 제어
        private MaterialDropField m_Drop;

        // 마커가 없을 때 폴백(하차장 기준 상대 위치 — 산 아래 방향)
        private static readonly Vector3 kFallbackBaseOffset = new Vector3(-10f, -5f, -8f);
        private const float kWireHeight = 3f;     // 마커 위 와이어 높이
        private const float kHangDepth = 1.1f;    // 와이어 아래 곤돌라 매달림 깊이
        private const float kWaitT = 0.85f;       // 대기 지점(하차장 직전)의 경로 비율
        private const float kUnloadDistance = 1.5f; // 화물이 이 이상 벗어나면 '내려진 것'으로 간주(킥 등)

        /// <summary>대기 중 주문 수(HUD 표시용).</summary>
        public int QueueCount => m_QueueCount.Value;

        protected override void Awake()
        {
            base.Awake();
            m_Drop = GetComponent<MaterialDropField>();
        }

        protected override void OnGimmickSpawn()
        {
            if (IsServer)
            {
                m_DockedPickup = new ulong[Config.CarCount];
                if (m_Cars.Count == 0)
                    for (int i = 0; i < Config.CarCount; i++)
                        m_Cars.Add(new CarEntry { carId = i, materialId = -1, phase = (byte)CarPhase.AtBase, phaseStart = Now });
            }
        }

        public override void OnNetworkDespawn()
        {
            DestroyVisuals();
            base.OnNetworkDespawn();
        }

        private float Now => NetworkManager != null ? (float)NetworkManager.ServerTime.Time : Time.time;

        // ── 서버: 주문 접수(MaterialDepot이 검증 후 넘김) ─────────────────────
        public void ServerEnqueue(int materialId)
        {
            if (!IsServer || !Active) return;
            m_Queue.Enqueue(materialId);
            m_QueueCount.Value = m_Queue.Count;
            Debug.Log($"[Namsan] 케이블카 주문 접수: 재료 id {materialId} (대기 {m_Queue.Count}건)");
        }

        /// <summary>재시작용: 대기열·곤돌라 전부 초기화(서버).</summary>
        public void ServerReset()
        {
            if (!IsServer || !Active) return;
            m_Queue.Clear();
            m_QueueCount.Value = 0;
            m_NextDispatchAt = 0f;
            for (int i = 0; i < m_Cars.Count; i++)
            {
                if (m_DockedPickup != null && i < m_DockedPickup.Length && m_DockedPickup[i] != 0)
                {
                    m_Drop.ServerRemove(m_DockedPickup[i]);
                    m_DockedPickup[i] = 0;
                }
                m_Cars[i] = new CarEntry { carId = m_Cars[i].carId, materialId = -1, phase = (byte)CarPhase.AtBase, phaseStart = Now };
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

        private bool StationBusy()
        {
            foreach (var c in m_Cars)
            {
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
                var phase = (CarPhase)c.phase;
                float elapsed = Now - c.phaseStart;

                switch (phase)
                {
                    case CarPhase.AtBase:
                        // 연타 주문이어도 뒷차는 간격을 두고 출발(줄줄이 겹쳐 보이는 것 방지)
                        if (building && m_Queue.Count > 0 && Now >= m_NextDispatchAt)
                        {
                            c.materialId = m_Queue.Dequeue();
                            m_QueueCount.Value = m_Queue.Count;
                            m_NextDispatchAt = Now + Config.CarDispatchGapSeconds;
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
                        if (!StationBusy())
                        {
                            SetPhase(ref c, CarPhase.Docking);
                            m_Cars[i] = c;
                        }
                        break;

                    case CarPhase.Docking:
                        if (elapsed >= PhaseDuration(phase))
                        {
                            // 문 열림 — 화물을 일반 바닥 픽업으로 곤돌라 안에 스폰(기존 줍기 재사용)
                            var dock = DockPos();
                            m_DockedPickup[IndexSlot(i)] = m_Drop.ServerDeliver(c.materialId, dock, dock);
                            SetPhase(ref c, CarPhase.Docked);
                            m_Cars[i] = c;
                        }
                        break;

                    case CarPhase.Docked:
                    {
                        ulong pid = m_DockedPickup[IndexSlot(i)];
                        bool taken = pid == 0 || !m_Drop.TryGetPickupPos(pid, out var ppos)
                                     || (ppos - (DockPos() + Vector3.up * 0.5f)).sqrMagnitude > kUnloadDistance * kUnloadDistance;
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

        // ── 위치 계산(서버·클라 공통 — 마커 기준) ────────────────────────────
        private Vector3 StationPos()
        {
            var t = FindSpot(NamsanSpots.CableCarStation);
            return t != null ? t.position : GridContract.Origin + new Vector3(-4f, 0f, 4f);
        }

        private Vector3 BasePos()
        {
            var t = FindSpot(NamsanSpots.CableCarOrigin);
            return t != null ? t.position : StationPos() + kFallbackBaseOffset;
        }

        private Vector3 DockPos() => StationPos();

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

        // ── 로컬 비주얼(와이어 + 곤돌라 + 남은 초) ───────────────────────────
        private GameObject m_Root;
        private LineRenderer m_Wire;
        private readonly List<GameObject> m_CarVisuals = new();
        private readonly List<GameObject> m_CargoVisuals = new();   // 이동 중 화물 미니 표시(곤돌라 자식)
        private readonly List<TextMesh> m_Timers = new();
        private readonly List<int> m_CargoShownId = new();

        private void EnsureVisuals()
        {
            if (m_Root != null) return;
            m_Root = new GameObject("~CableCars");

            var wireGo = new GameObject("~Wire");
            wireGo.transform.SetParent(m_Root.transform);
            m_Wire = wireGo.AddComponent<LineRenderer>();
            m_Wire.positionCount = 2;
            m_Wire.startWidth = m_Wire.endWidth = 0.07f;
            m_Wire.material = RuntimeMat(new Color(0.2f, 0.2f, 0.22f));

            for (int i = 0; i < m_Cars.Count; i++)
            {
                var car = GameObject.CreatePrimitive(PrimitiveType.Cube);
                car.name = $"~CableCar{i}";
                car.transform.SetParent(m_Root.transform);
                car.transform.localScale = new Vector3(1.3f, 1.0f, 1.3f);
                var col = car.GetComponent<Collider>();
                if (col != null) Destroy(col);
                Tint(car, new Color(0.85f, 0.25f, 0.2f));   // 남산 케이블카 레드

                // 매달림 팔(와이어까지 기둥)
                var arm = GameObject.CreatePrimitive(PrimitiveType.Cube);
                arm.name = "arm";
                arm.transform.SetParent(car.transform);
                arm.transform.localScale = new Vector3(0.06f, kHangDepth / 1.0f, 0.06f);
                arm.transform.localPosition = new Vector3(0f, 0.5f + kHangDepth * 0.5f, 0f);
                var acol = arm.GetComponent<Collider>();
                if (acol != null) Destroy(acol);
                Tint(arm, new Color(0.2f, 0.2f, 0.22f));

                // 남은 초 표시(도킹 중만 보임)
                var tgo = new GameObject("timer");
                tgo.transform.SetParent(car.transform);
                tgo.transform.localPosition = new Vector3(0f, 1.9f, 0f);
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
            }
        }

        private void DestroyVisuals()
        {
            if (m_Root != null) Destroy(m_Root);
            m_Root = null;
            m_Wire = null;
            m_CarVisuals.Clear();
            m_CargoVisuals.Clear();
            m_Timers.Clear();
            m_CargoShownId.Clear();
        }

        private void UpdateVisuals()
        {
            EnsureVisuals();
            if (m_CarVisuals.Count != m_Cars.Count) { DestroyVisuals(); EnsureVisuals(); }   // 카 수 변경(리셋 등) 방어

            var basePos = BasePos();
            var station = StationPos();
            m_Wire.SetPosition(0, basePos + Vector3.up * kWireHeight);
            m_Wire.SetPosition(1, station + Vector3.up * kWireHeight);

            var camT = Camera.main != null ? Camera.main.transform : null;
            for (int i = 0; i < m_Cars.Count; i++)
            {
                var c = m_Cars[i];
                var phase = (CarPhase)c.phase;
                float elapsed = Now - c.phaseStart;
                float t = PathT(phase, elapsed, Config.CarFetchSeconds, Config.CarGapSeconds, Config.CarFetchSeconds * 0.7f, c.carId);

                var pos = CarPosAt(basePos, station, t);
                if (phase == CarPhase.AtBase) pos = CarPosAt(basePos, station, 0f) + new Vector3(0f, 0f, 1.6f * c.carId);   // 차고 대기(줄지어)
                m_CarVisuals[i].transform.position = pos;

                // 이동 중 화물 미니 표시(도킹 중엔 실제 픽업이 곤돌라 안에 있으므로 숨김)
                bool showCargo = c.materialId >= 0 && phase != CarPhase.Docked;
                UpdateCargoVisual(i, showCargo ? c.materialId : -1);

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
                vis.transform.localScale = Vector3.one * (0.6f / maxAxis);
                vis.transform.localPosition = new Vector3(0f, 0.8f, 0f);
            }
            else
            {
                vis = GameObject.CreatePrimitive(PrimitiveType.Cube);
                vis.transform.SetParent(m_CarVisuals[i].transform);
                var col = vis.GetComponent<Collider>();
                if (col != null) Destroy(col);
                vis.transform.localScale = Vector3.one * 0.5f;
                vis.transform.localPosition = new Vector3(0f, 0.8f, 0f);
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
