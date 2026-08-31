using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace GridSystem
{
    /// <summary>
    /// DDP 기믹 ① 이간수문(二間水門) 물길 —
    /// 예고 후 수문이 열리고 수로(Spot_WaterChannel0~N)를 따라 물이 흐른다.
    ///
    /// 실제 DDP 부지는 2008년 동대문운동장을 철거하다 한양도성 성곽과 이간수문이 발굴돼 설계가 바뀐 곳이다.
    /// 그 수문이 열린다는 설정으로, 수로는 어울림광장(배송·작업대)과 잔디지붕 데크(건축장) 사이를 지난다.
    ///
    /// · 휩쓸리면 하류로 쓸려가고(CurrentSpeed), 벗어나는 순간 스턴 + 든 재료 드롭.
    /// · 대신 재료를 수로에 두면 하류(건축장 쪽)로 빠르게 흘러간다 — 위험을 감수하면 운반이 빨라진다.
    ///
    /// 서버는 페이즈 전이만 복제하고, 물 위치·연출은 페이즈 시작 시각 기반 결정론(전 클라 동일 계산).
    /// 밀림 적용은 각 오너 로컬(PlayerUnit이 CurrentPushAt 폴링 — GustNetwork·ParadeNetwork와 동일 계약).
    /// GameLoopManager가 런타임 부착, 맵 카드에 DdpGimmickConfig가 없으면 스스로 잠잔다.
    /// </summary>
    public class WaterGateNetwork : DdpGimmickBase
    {
        public enum FloodPhase : byte { Idle = 0, Warning = 1, Flowing = 2 }

        public struct FloodState : INetworkSerializable, System.IEquatable<FloodState>
        {
            public byte phase;
            public float phaseStart;     // 서버 시각
            public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
            {
                s.SerializeValue(ref phase);
                s.SerializeValue(ref phaseStart);
            }
            public bool Equals(FloodState o) => phase == o.phase && phaseStart == o.phaseStart;
        }

        private readonly NetworkVariable<FloodState> m_State =
            new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private float m_NextFloodAt;    // 서버 전용: 다음 방류 예고 시각

        /// <summary>이 맵에서 물길이 켜져 있으면 인스턴스(플레이어·발굴터 쪽 접근용). 아니면 null.</summary>
        public static WaterGateNetwork Instance { get; private set; }

        public FloodPhase Phase => (FloodPhase)m_State.Value.phase;

        /// <summary>지금 물이 흐르는 중인가(발굴터가 이 값을 보고 잠긴다). 기믹이 없는 맵에선 항상 false.</summary>
        public static bool IsFlooding => Instance != null && Instance.Phase == FloodPhase.Flowing;

        /// <summary>물이 '방금 빠졌다'는 통지 — 발굴터가 새 유구를 드러내는 트리거로 쓴다(서버에서만 발생).</summary>
        public static event System.Action FloodEnded;

        protected override void OnGimmickSpawn()
        {
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

        /// <summary>재시작용(서버): 흐르는 중이면 끊고 새 주기로.</summary>
        public void ServerReset()
        {
            if (!IsServer || !Active) return;
            m_State.Value = new FloodState { phase = (byte)FloodPhase.Idle, phaseStart = Now };
            ScheduleNext();
        }

        private void ScheduleNext() =>
            m_NextFloodAt = Now + Random.Range(Config.FloodMinInterval, Config.FloodMaxInterval);

        private void Update()
        {
            if (!Active || !IsSpawned) return;
            RefreshMarkers(DdpSpots.WaterChannelPrefix, m_Path, 2);
            if (IsServer) ServerTick();
            UpdateVisuals();
        }

        private void ServerTick()
        {
            var s = m_State.Value;
            float elapsed = Now - s.phaseStart;

            switch ((FloodPhase)s.phase)
            {
                case FloodPhase.Idle:
                    if (Loop != null && Loop.IsBuilding && Now >= m_NextFloodAt && m_Path.Count >= 2)
                    {
                        s.phase = (byte)FloodPhase.Warning;
                        s.phaseStart = Now;
                        m_State.Value = s;
                    }
                    break;

                case FloodPhase.Warning:
                    if (elapsed >= Config.FloodWarnSeconds)
                    {
                        s.phase = (byte)FloodPhase.Flowing;
                        s.phaseStart = Now;
                        m_State.Value = s;
                    }
                    break;

                case FloodPhase.Flowing:
                    FloatMaterialsDownstream();
                    if (elapsed >= Config.FloodDurationSeconds || (Loop != null && !Loop.IsBuilding))
                    {
                        s.phase = (byte)FloodPhase.Idle;
                        s.phaseStart = Now;
                        m_State.Value = s;
                        ScheduleNext();
                        FloodEnded?.Invoke();   // 발굴터: 물이 빠졌으니 새 유구를 드러내라
                    }
                    break;
            }
        }

        // ── 경로 ───────────────────────────────────────────────────────────
        private readonly List<Vector3> m_Path = new();

        /// <summary>수로 위(반경·높이 안)인가. nearest/flowDir는 그 지점의 물길 정보.</summary>
        private bool InChannel(Vector3 world, out Vector3 flowDir)
        {
            flowDir = Vector3.forward;
            if (!NearestOnPath(m_Path, world, out var nearest, out flowDir, out float dist)) return false;
            if (dist > Config.ChannelRadius) return false;
            // 수면 기준 위아래 — 다리 위나 데크 위처럼 높은 곳은 물에 안 닿는다.
            return Mathf.Abs(world.y - nearest.y) <= Config.ChannelHeight;
        }

        // ── 재료 급송(서버): 수로에 놓인 바닥 재료를 하류로 흘려보낸다 ─────────
        // 위험을 감수하고 재료를 물에 넣으면 걸어 옮기는 것보다 빨리 건축장 쪽에 도착한다.
        private IPickupField m_Drop;   // 급송에 필요한 계약만(CollectWithin·ServerFloat) — GridInterfaces.cs 채택 규약
        private readonly List<ulong> m_FloatIds = new();
        private readonly List<Vector3> m_FloatPos = new();
        private float m_NextFloatTick;

        private void FloatMaterialsDownstream()
        {
            if (!Config.CarryMaterials || m_Path.Count < 2) return;
            if (Time.time < m_NextFloatTick) return;
            const float kTick = 0.2f;
            m_NextFloatTick = Time.time + kTick;

            if (m_Drop == null) m_Drop = FindFirstObjectByType<MaterialDropField>();
            if (m_Drop == null) return;

            // 경로 중간쯤을 기준으로 넉넉히 훑고, 실제 수로 판정은 InChannel이 한다.
            var mid = m_Path[m_Path.Count / 2];
            float sweep = PathLength(m_Path) * 0.5f + Config.ChannelRadius + 2f;
            m_Drop.CollectWithin(mid, sweep, m_FloatIds, m_FloatPos);

            float step = Config.MaterialFloatSpeed * kTick;
            for (int i = 0; i < m_FloatIds.Count; i++)
            {
                if (!InChannel(m_FloatPos[i], out var dir)) continue;
                m_Drop.ServerFloat(m_FloatIds[i], dir, step);
            }
        }

        // ── 플레이어 밀림 + 스턴(오너 로컬 폴링 — GustNetwork.CurrentPushAt과 동일 계약) ──
        private bool m_LocalWasSwept;   // 로컬 플레이어가 물에 잠긴 중(벗어나는 순간 스턴)
        private float m_PendingStun;    // 벗어날 때 걸어야 할 스턴(초) — PlayerUnit이 Consume해서 적용

        /// <summary>지금 이 위치의 플레이어가 받아야 할 수평 밀림 속도(m/s). 물길 밖이면 zero.</summary>
        public static Vector3 CurrentPushAt(Transform player)
        {
            var w = Instance;
            return w != null ? w.LocalPush(player) : Vector3.zero;
        }

        /// <summary>휩쓸린 뒤 걸어야 할 스턴(초)을 1회 소비. 없으면 0.
        /// (GridSystem 어셈블리는 Player를 모름 — 스턴 적용은 PlayerUnit이 폴링해서 담당.)</summary>
        public static float ConsumePendingStun()
        {
            var w = Instance;
            if (w == null || w.m_PendingStun <= 0f) return 0f;
            float s = w.m_PendingStun;
            w.m_PendingStun = 0f;
            return s;
        }

        private Vector3 LocalPush(Transform player)
        {
            if (!Active || Phase != FloodPhase.Flowing)
            {
                m_LocalWasSwept = false;
                return Vector3.zero;
            }

            if (InChannel(player.position, out var flowDir))
            {
                m_LocalWasSwept = true;
                return flowDir * Config.CurrentSpeed;   // 하류 방향으로 그대로 쓸려간다
            }

            // 물 밖으로 나온 순간 스턴 예약(떠내려가 처박힘) — PlayerUnit이 소비해 적용
            if (m_LocalWasSwept)
            {
                m_LocalWasSwept = false;
                m_PendingStun = Config.SweptStunSeconds;
            }
            return Vector3.zero;
        }

        // ── 연출(전 클라 로컬): 예고 토스트 + 수면 + 흐름 표시 ──────────────
        private readonly List<GameObject> m_WaterQuads = new();
        private static readonly Color kWaterColor = new Color(0.28f, 0.60f, 0.85f, 1f);
        private static readonly Color kWarnColor  = new Color(0.35f, 0.75f, 0.95f, 1f);

        private void OnStateChanged(FloodState _, FloodState next)
        {
            if ((FloodPhase)next.phase != FloodPhase.Warning) return;

            var nm = NetworkManager.Singleton;
            var po = nm != null && nm.LocalClient != null ? nm.LocalClient.PlayerObject : null;
            if (po != null)
                GridJuice.WorldToast(po.transform.position + Vector3.up * 2.6f, "🌊 수문이 열립니다!", kWarnColor);
            // TODO(사운드팀): 수문 개방 경보 + 물소리 SFX — GridSoundBridge에 전용 이름 추가 후 여기서 호출
        }

        // 수로를 따라 얇은 물판을 깐다. 예고 중엔 반투명하게 미리 보이고, 흐르는 중엔 차오르며 물결친다.
        private void UpdateVisuals()
        {
            var phase = Phase;
            bool show = phase != FloodPhase.Idle && m_Path.Count >= 2;

            if (!show)
            {
                foreach (var q in m_WaterQuads) if (q != null) q.SetActive(false);
                return;
            }

            if (m_WaterQuads.Count == 0) BuildWaterQuads();

            // 차오름: 예고 중엔 얕게(바닥에 물이 비침), 흐르는 중엔 0.15초 만에 가득.
            float elapsed = Now - m_State.Value.phaseStart;
            float fill = phase == FloodPhase.Flowing
                ? Mathf.Clamp01(elapsed / 0.6f)
                : 0.18f;

            var c = Color.Lerp(kWarnColor, kWaterColor, fill);
            c.a = 0.35f + fill * 0.45f;

            for (int i = 0; i < m_WaterQuads.Count; i++)
            {
                var q = m_WaterQuads[i];
                if (q == null) continue;
                if (!q.activeSelf) q.SetActive(true);

                // 흐르는 물결(비주얼만): 세그먼트마다 위상을 어긋나게 해 물이 흘러가는 것처럼 보이게.
                float wave = phase == FloodPhase.Flowing
                    ? Mathf.Sin((Time.time * 3.2f) - i * 0.8f) * 0.05f
                    : 0f;
                var s = q.transform.localScale;
                q.transform.localScale = new Vector3(s.x, Mathf.Max(0.02f, 0.28f * fill), s.z);
                var p = q.transform.position;
                q.transform.position = new Vector3(p.x, m_QuadBaseY[i] + wave, p.z);

                Tint(q, c);
            }
        }

        private readonly List<float> m_QuadBaseY = new();

        private void BuildWaterQuads()
        {
            m_QuadBaseY.Clear();
            for (int i = 1; i < m_Path.Count; i++)
            {
                var a = m_Path[i - 1];
                var b = m_Path[i];
                var seg = b - a;
                float len = seg.magnitude;
                if (len < 0.01f) continue;

                var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                box.name = "~DdpWater";
                var col = box.GetComponent<Collider>();
                if (col != null) Destroy(col);   // 물은 통과 — 밀림은 CurrentPushAt이 담당

                var mid = (a + b) * 0.5f;
                box.transform.position = mid;
                box.transform.rotation = Quaternion.LookRotation(seg.normalized, Vector3.up);
                box.transform.localScale = new Vector3(Config.ChannelRadius * 2f, 0.2f, len);
                m_WaterQuads.Add(box);
                m_QuadBaseY.Add(mid.y);
            }
        }

        private void DestroyVisuals()
        {
            foreach (var q in m_WaterQuads) if (q != null) Destroy(q);
            m_WaterQuads.Clear();
            m_QuadBaseY.Clear();
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
