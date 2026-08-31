using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace GridSystem
{
    /// <summary>
    /// 롯데월드 기믹이 쓰는 맵 마커 이름들. 전부 "옮길 씬 오브젝트가 없는" 마커라
    /// MapLoader.ApplySpots가 건너뛰고, 기믹 시스템이 직접 추적한다(NamsanSpots와 같은 체계).
    /// </summary>
    public static class LotteSpots
    {
        /// <summary>퍼레이드 경로 웨이포인트 접두사 — Spot_ParadePoint0, 1, 2… 순서대로 폴리라인.</summary>
        public const string ParadePointPrefix = "Spot_ParadePoint";

        /// <summary>모노레일 기둥 마커 접두사 — 맵 생성 툴이 이 위치에 기둥·빔을 짓고, 재생성 때 사용자 조정을 유지한다.</summary>
        public const string MonoPillarPrefix = "Spot_MonoPillar";

        /// <summary>MapLoader가 씬 오브젝트 배치를 건너뛰어야 하는 마커인가(전체 이름 "Spot_..." 기준).</summary>
        public static bool IsMarkerOnly(string fullName) =>
            fullName.StartsWith(ParadePointPrefix) || fullName.StartsWith(MonoPillarPrefix);
    }

    /// <summary>
    /// 롯데월드 퍼레이드 — 예고 후 퍼레이드 카 행렬이 경로(Spot_ParadePoint0~N)를 따라 지나간다.
    /// 경로는 배송존·작업대(광장)와 건축장(섬) 사이를 가로지르므로 자재 운반 타이밍을 강제한다.
    /// 치이면 경로 밖으로 밀려나고(HitPushSpeed) 벗어난 순간 스턴 + 든 재료 드롭.
    /// 서버는 페이즈 전이만 복제, 카 위치는 페이즈 시작 시각 기반 결정론(전 클라 동일 계산) —
    /// 밀림 적용은 각 오너 로컬(PlayerUnit이 CurrentPushAt 폴링, GustNetwork와 동일 구조).
    /// GameLoopManager가 런타임 부착, 맵 카드에 LotteGimmickConfig가 없으면 스스로 잠잔다.
    /// </summary>
    public class ParadeNetwork : NetworkBehaviour
    {
        public enum ParadePhase : byte { Idle = 0, Warning = 1, Running = 2 }

        public struct ParadeState : INetworkSerializable, System.IEquatable<ParadeState>
        {
            public byte phase;
            public float phaseStart;     // 서버 시각
            public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
            {
                s.SerializeValue(ref phase);
                s.SerializeValue(ref phaseStart);
            }
            public bool Equals(ParadeState o) => phase == o.phase && phaseStart == o.phaseStart;
        }

        private readonly NetworkVariable<ParadeState> m_State =
            new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private GameLoopManager m_Loop;
        private LotteGimmickConfig m_Config;
        private float m_NextParadeAt;   // 서버 전용: 다음 퍼레이드 예고 시각

        /// <summary>이 맵에서 퍼레이드가 켜져 있으면 인스턴스(플레이어 쪽 접근용). 아니면 null.</summary>
        public static ParadeNetwork Instance { get; private set; }

        public bool Active => m_Config != null;
        public ParadePhase Phase => (ParadePhase)m_State.Value.phase;

        private float Now => NetworkManager != null ? (float)NetworkManager.ServerTime.Time : Time.time;

        private void Awake() => m_Loop = GetComponent<GameLoopManager>();

        public override void OnNetworkSpawn()
        {
            // GameLoopManager.OnNetworkSpawn(부착 순서상 먼저 실행)이 MapIndex를 확정한 뒤라 바로 조회 가능.
            var cat = MapCatalog.Instance;
            var def = (cat != null && m_Loop != null) ? cat.Get(m_Loop.MapIndex) : null;
            m_Config = def != null ? def.LotteGimmicks : null;
            Debug.Log($"[Lotte] ParadeNetwork: 맵 {m_Loop?.MapIndex}({def?.DisplayName ?? "?"}) → {(Active ? "퍼레이드 켜짐" : "기믹 없음(잠자기)")}");
            if (!Active) return;

            Instance = this;
            m_State.OnValueChanged += OnStateChanged;
            if (IsServer) ScheduleNext();
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this) Instance = null;
            m_State.OnValueChanged -= OnStateChanged;
            DestroyVisuals();
        }

        /// <summary>재시작용(서버): 퍼레이드 중이면 끊고 새 주기로.</summary>
        public void ServerReset()
        {
            if (!IsServer || !Active) return;
            m_State.Value = new ParadeState { phase = (byte)ParadePhase.Idle, phaseStart = Now };
            ScheduleNext();
        }

        private void ScheduleNext() =>
            m_NextParadeAt = Now + Random.Range(m_Config.ParadeMinInterval, m_Config.ParadeMaxInterval);

        private void Update()
        {
            if (!Active || !IsSpawned) return;
            RefreshPath();
            if (IsServer) ServerTick();
            UpdateVisuals();
        }

        private void ServerTick()
        {
            var s = m_State.Value;
            float elapsed = Now - s.phaseStart;

            switch ((ParadePhase)s.phase)
            {
                case ParadePhase.Idle:
                    if (m_Loop != null && m_Loop.IsBuilding && Now >= m_NextParadeAt && m_PathLength > 0f)
                    {
                        s.phase = (byte)ParadePhase.Warning;
                        s.phaseStart = Now;
                        m_State.Value = s;
                    }
                    break;

                case ParadePhase.Warning:
                    if (elapsed >= m_Config.ParadeWarnSeconds)
                    {
                        s.phase = (byte)ParadePhase.Running;
                        s.phaseStart = Now;
                        m_State.Value = s;
                    }
                    break;

                case ParadePhase.Running:
                    if (elapsed >= RunDuration || (m_Loop != null && !m_Loop.IsBuilding))
                    {
                        s.phase = (byte)ParadePhase.Idle;
                        s.phaseStart = Now;
                        m_State.Value = s;
                        ScheduleNext();
                    }
                    break;
            }
        }

        // 행렬 전체가 경로를 다 지나가는 데 걸리는 시간(마지막 카 출발 지연 + 주파 시간)
        private float RunDuration =>
            m_PathLength / Mathf.Max(0.5f, m_Config.CarSpeed) + m_Config.CarGapSeconds * Mathf.Max(0, m_Config.CarCount - 1) + 0.5f;

        // ── 경로(웨이포인트 폴리라인) — 배경이 런타임 스폰이라 늦게 생김: 0.5초 간격 재탐색 ──
        private readonly List<Vector3> m_Path = new();
        private float m_PathLength;
        private float m_NextPathFind;

        private void RefreshPath()
        {
            if (m_Path.Count >= 2 || Time.time < m_NextPathFind) return;
            m_NextPathFind = Time.time + 0.5f;

            m_Path.Clear();
            for (int i = 0; ; i++)
            {
                var go = GameObject.Find(LotteSpots.ParadePointPrefix + i);
                if (go == null) break;
                m_Path.Add(go.transform.position);
            }
            if (m_Path.Count < 2) { m_Path.Clear(); return; }

            m_PathLength = 0f;
            for (int i = 1; i < m_Path.Count; i++) m_PathLength += Vector3.Distance(m_Path[i - 1], m_Path[i]);
            Debug.Log($"[Lotte] 퍼레이드 경로 연결: 웨이포인트 {m_Path.Count}개, 길이 {m_PathLength:0.0}m");
        }

        /// <summary>경로 시작에서 distance만큼 간 지점과 진행 방향. 경로 밖(<0, >len)이면 false.</summary>
        private bool SamplePath(float distance, out Vector3 pos, out Vector3 forward)
        {
            pos = default; forward = Vector3.forward;
            if (m_Path.Count < 2 || distance < 0f || distance > m_PathLength) return false;
            for (int i = 1; i < m_Path.Count; i++)
            {
                float seg = Vector3.Distance(m_Path[i - 1], m_Path[i]);
                if (distance <= seg || i == m_Path.Count - 1)
                {
                    float t = seg > 1e-4f ? Mathf.Clamp01(distance / seg) : 0f;
                    pos = Vector3.Lerp(m_Path[i - 1], m_Path[i], t);
                    forward = (m_Path[i] - m_Path[i - 1]).normalized;
                    return true;
                }
                distance -= seg;
            }
            return false;
        }

        /// <summary>index번 카의 현재 위치·방향(경로 밖이면 false). 페이즈 시작 시각 기반 — 전 클라 동일.</summary>
        private bool CarAt(int index, out Vector3 pos, out Vector3 forward)
        {
            pos = default; forward = Vector3.forward;
            if (Phase != ParadePhase.Running) return false;
            float t = Now - m_State.Value.phaseStart - index * m_Config.CarGapSeconds;
            return t >= 0f && SamplePath(t * m_Config.CarSpeed, out pos, out forward);
        }

        // ── 플레이어 밀림 + 스턴(오너 로컬에서 폴링 — GustNetwork.CurrentPushAt과 동일 계약) ──
        private bool m_LocalWasHit;    // 로컬 플레이어가 카에 접촉 중(벗어나는 순간 스턴)
        private float m_PendingStun;   // 접촉 종료 시 걸어야 할 스턴(초) — PlayerUnit이 Consume해서 적용
        private float m_ImmuneUntil;   // 치인 직후 무적 만료 시각 — 행렬에 연쇄로 갈려 못 빠져나오는 문제 방지
        private const float kHitImmuneSeconds = 2f;

        /// <summary>지금 이 위치의 플레이어가 받아야 할 수평 밀림 속도(m/s). 퍼레이드 밖이면 zero.</summary>
        public static Vector3 CurrentPushAt(Transform player)
        {
            var p = Instance;
            return p != null ? p.LocalPush(player) : Vector3.zero;
        }

        /// <summary>치인 뒤 걸어야 할 스턴(초)을 1회 소비. 없으면 0.
        /// (GridSystem 어셈블리는 Player를 모름 — 스턴 적용은 PlayerUnit이 폴링해서 담당.)</summary>
        public static float ConsumePendingStun()
        {
            var p = Instance;
            if (p == null || p.m_PendingStun <= 0f) return 0f;
            float s = p.m_PendingStun;
            p.m_PendingStun = 0f;
            return s;
        }

        private Vector3 LocalPush(Transform player)
        {
            if (!Active || Phase != ParadePhase.Running)
            {
                m_LocalWasHit = false;
                return Vector3.zero;
            }
            if (Time.time < m_ImmuneUntil)   // 무적 중 — 다음 카가 연달아 밀지 않는다
            {
                m_LocalWasHit = false;
                return Vector3.zero;
            }

            for (int i = 0; i < m_Config.CarCount; i++)
            {
                if (!CarAt(i, out var carPos, out var fwd)) continue;
                var to = player.position - carPos;
                if (Mathf.Abs(to.y) > 2f) continue;
                to.y = 0f;
                if (to.sqrMagnitude > m_Config.CarHitRadius * m_Config.CarHitRadius) continue;

                // 진행 방향의 좌/우 중 플레이어가 있는 쪽으로 쓸어냄(+ 약간 앞쪽 — 차에 떠밀리는 그림)
                var side = Vector3.Cross(Vector3.up, fwd);            // fwd의 오른쪽
                if (Vector3.Dot(side, to) < 0f) side = -side;         // 플레이어가 있는 쪽
                m_LocalWasHit = true;
                return (side + fwd * 0.35f).normalized * m_Config.HitPushSpeed;
            }

            // 접촉이 끝난 순간 스턴 예약(밀려나 경로 밖으로 벗어난 뒤 넘어짐) — PlayerUnit이 소비해 적용
            if (m_LocalWasHit)
            {
                m_LocalWasHit = false;
                m_PendingStun = m_Config.HitStunSeconds;
                m_ImmuneUntil = Time.time + kHitImmuneSeconds;   // 한 번 치였으면 2초 무적 — "영원히 치임" 방지
            }
            return Vector3.zero;
        }

        // ── 연출(전 클라 로컬): 예고 토스트 + 경로 표시 + 퍼레이드 카 ─────────────
        private readonly List<GameObject> m_Cars = new();
        private readonly List<GameObject> m_RouteMarks = new();

        // 카 색(그레이박스 폴백) — 알록달록 퍼레이드 느낌
        private static readonly Color[] kCarColors =
        {
            new Color(0.98f, 0.45f, 0.65f),   // 핑크
            new Color(0.35f, 0.70f, 0.98f),   // 하늘
            new Color(0.99f, 0.85f, 0.30f),   // 노랑
            new Color(0.55f, 0.90f, 0.50f),   // 연두
        };

        private void OnStateChanged(ParadeState _, ParadeState next)
        {
            if ((ParadePhase)next.phase != ParadePhase.Warning) return;

            // 내 화면 위 예고(오너 로컬 위치)
            var nm = NetworkManager.Singleton;
            var po = nm != null && nm.LocalClient != null ? nm.LocalClient.PlayerObject : null;
            if (po != null)
                GridJuice.WorldToast(po.transform.position + Vector3.up * 2.6f, "🎉 퍼레이드 지나갑니다!", new Color(0.98f, 0.45f, 0.65f));
            // TODO(사운드팀): 퍼레이드 예고 팡파레/행진곡 SFX — GridSoundBridge에 전용 이름 추가 후 여기서 호출
        }

        private void UpdateVisuals()
        {
            bool show = Phase != ParadePhase.Idle && m_Path.Count >= 2;
            UpdateRouteMarks(show);
            UpdateCars();
        }

        // 경로 표시: 웨이포인트 사이 얇은 점선(예고=주황, 진행=핑크 펄스) — 어디로 지나가는지 미리 보인다
        private void UpdateRouteMarks(bool show)
        {
            if (!show)
            {
                foreach (var m in m_RouteMarks) if (m != null) m.SetActive(false);
                return;
            }

            if (m_RouteMarks.Count == 0)
            {
                const float kDash = 1.2f, kGap = 1.0f;
                float d = 0f;
                while (d < m_PathLength)
                {
                    if (SamplePath(d, out var pos, out var fwd))
                    {
                        var dash = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        dash.name = "~ParadeRouteDash";
                        var col = dash.GetComponent<Collider>();
                        if (col != null) Destroy(col);
                        dash.transform.position = pos + Vector3.up * 0.05f;
                        dash.transform.rotation = Quaternion.LookRotation(fwd);
                        dash.transform.localScale = new Vector3(0.25f, 0.06f, kDash);
                        m_RouteMarks.Add(dash);
                    }
                    d += kDash + kGap;
                }
            }

            bool running = Phase == ParadePhase.Running;
            var c = running ? new Color(0.98f, 0.45f, 0.65f) : new Color(1f, 0.65f, 0.2f);
            float pulse = 0.55f + Mathf.Abs(Mathf.Sin(Time.time * (running ? 8f : 4f))) * 0.45f;
            foreach (var m in m_RouteMarks)
            {
                if (m == null) continue;
                if (!m.activeSelf) m.SetActive(true);
                Tint(m, c * pulse);
            }
        }

        private void UpdateCars()
        {
            if (m_Config == null) return;
            while (m_Cars.Count < m_Config.CarCount) m_Cars.Add(BuildCar(m_Cars.Count));

            for (int i = 0; i < m_Cars.Count; i++)
            {
                if (m_Cars[i] == null) continue;
                bool visible = CarAt(i, out var pos, out var fwd);
                if (m_Cars[i].activeSelf != visible) m_Cars[i].SetActive(visible);
                if (!visible) continue;
                m_Cars[i].transform.SetPositionAndRotation(pos, Quaternion.LookRotation(fwd));
                // 통통 튀는 축제 바운스(비주얼만)
                float bob = Mathf.Abs(Mathf.Sin((Time.time + i * 0.4f) * 6f)) * 0.12f;
                m_Cars[i].transform.position = pos + Vector3.up * bob;
            }
        }

        // 퍼레이드 카 1대 — Resources/LotteWorld/ParadeCar(+ParadeCar2, 3…) 프리팹(VARCO 모델 자리)이 있으면
        // 변형을 순서대로 돌려 쓰고, 없으면 알록달록 그레이박스(몸체+2층+깃대). 콜라이더 없음 — 충돌은 CurrentPushAt이 거리로 판정.
        private static GameObject[] s_CarPrefabs;

        private GameObject BuildCar(int index)
        {
            if (s_CarPrefabs == null)
            {
                var found = new List<GameObject>();
                var first = Resources.Load<GameObject>("LotteWorld/ParadeCar");
                if (first != null) found.Add(first);
                for (int v = 2; ; v++)
                {
                    var p = Resources.Load<GameObject>($"LotteWorld/ParadeCar{v}");
                    if (p == null) break;
                    found.Add(p);
                }
                s_CarPrefabs = found.ToArray();
            }
            if (s_CarPrefabs.Length > 0)
            {
                var inst = Instantiate(s_CarPrefabs[index % s_CarPrefabs.Length]);
                inst.name = $"~ParadeCar{index}";
                foreach (var c in inst.GetComponentsInChildren<Collider>()) Destroy(c);
                inst.SetActive(false);
                return inst;
            }

            var root = new GameObject($"~ParadeCar{index}");
            var color = kCarColors[index % kCarColors.Length];

            void Box(string n, Vector3 lp, Vector3 ls, Color c)
            {
                var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
                b.name = n;
                var col = b.GetComponent<Collider>();
                if (col != null) Destroy(col);
                b.transform.SetParent(root.transform, false);
                b.transform.localPosition = lp;
                b.transform.localScale = ls;
                Tint(b, c);
            }

            Box("Body", new Vector3(0f, 0.55f, 0f), new Vector3(1.9f, 1.1f, 2.9f), color);
            Box("Top", new Vector3(0f, 1.45f, -0.4f), new Vector3(1.3f, 0.7f, 1.5f), Color.Lerp(color, Color.white, 0.45f));
            Box("Pole", new Vector3(0f, 2.4f, 0.9f), new Vector3(0.1f, 1.6f, 0.1f), new Color(0.9f, 0.9f, 0.92f));
            Box("Flag", new Vector3(0.35f, 3.0f, 0.9f), new Vector3(0.7f, 0.45f, 0.05f), new Color(0.99f, 0.85f, 0.30f));
            root.SetActive(false);
            return root;
        }

        private void DestroyVisuals()
        {
            foreach (var c in m_Cars) if (c != null) Destroy(c);
            m_Cars.Clear();
            foreach (var m in m_RouteMarks) if (m != null) Destroy(m);
            m_RouteMarks.Clear();
        }

        private static Material s_Mat;
        private static void Tint(GameObject go, Color c)
        {
            var r = go.GetComponent<Renderer>();
            if (r == null) return;
            if (s_Mat == null)
            {
                var sh = Shader.Find("Universal Render Pipeline/Lit");
                s_Mat = sh != null ? new Material(sh) { hideFlags = HideFlags.HideAndDontSave } : null;
            }
            if (s_Mat != null) r.sharedMaterial = s_Mat;
            var mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            mpb.SetColor(Shader.PropertyToID("_BaseColor"), c);
            mpb.SetColor(Shader.PropertyToID("_Color"), c);
            r.SetPropertyBlock(mpb);
        }
    }
}
