using Unity.Netcode;
using UnityEngine;

namespace GridSystem
{
    /// <summary>
    /// 남산 돌풍(기획서 §3) — 12~18초 랜덤 주기, 예고 3초 후 수평 돌풍이 분다.
    /// 높이 밴드: 그리드 y &lt; Weak(기본 10) 없음 / Weak~Strong 약풍(총 2칸, 점프 파훼 가능) / Strong(기본 15)+ 강풍(총 4칸).
    /// '고정된 물체에 기대거나 뒤에 숨으면' 면제 — 바람 방향으로 레이캐스트해 솔리드가 막고 있으면 안 밀린다.
    /// 서버는 페이즈 전이만 복제하고, 실제 밀림은 각 플레이어 오너가 로컬에서 적용한다
    /// (PlayerUnit이 CurrentPushAt을 폴링해 PlayerMovement.ExternalPush에 넣는 구조).
    /// </summary>
    public class GustNetwork : NamsanGimmickBase
    {
        public enum GustPhase : byte { Idle = 0, Warning = 1, Blowing = 2 }

        public struct GustState : INetworkSerializable, System.IEquatable<GustState>
        {
            public byte phase;
            public float dirX, dirZ;     // 바람이 '불어가는' 방향(정규화, 수평)
            public float phaseStart;     // 서버 시각
            public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
            {
                s.SerializeValue(ref phase);
                s.SerializeValue(ref dirX);
                s.SerializeValue(ref dirZ);
                s.SerializeValue(ref phaseStart);
            }
            public bool Equals(GustState o) =>
                phase == o.phase && dirX == o.dirX && dirZ == o.dirZ && phaseStart == o.phaseStart;
        }

        private readonly NetworkVariable<GustState> m_State =
            new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private float m_NextGustAt;   // 서버 전용: 다음 돌풍 예고 시각

        /// <summary>이 맵에서 돌풍이 켜져 있으면 인스턴스(플레이어 쪽 접근용). 아니면 null.</summary>
        public static GustNetwork Instance { get; private set; }

        public GustPhase Phase => (GustPhase)m_State.Value.phase;
        public Vector3 WindDir => new Vector3(m_State.Value.dirX, 0f, m_State.Value.dirZ);

        private float Now => NetworkManager != null ? (float)NetworkManager.ServerTime.Time : Time.time;

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
            DestroyArrow();
            base.OnNetworkDespawn();
        }

        /// <summary>재시작용(서버): 돌풍 중이면 끊고 새 주기로.</summary>
        public void ServerReset()
        {
            if (!IsServer || !Active) return;
            m_State.Value = new GustState { phase = (byte)GustPhase.Idle, phaseStart = Now };
            ScheduleNext();
        }

        private void ScheduleNext() =>
            m_NextGustAt = Now + Random.Range(Config.GustMinInterval, Config.GustMaxInterval);

        private void Update()
        {
            if (!Active || !IsSpawned) return;
            if (IsServer) ServerTick();
            UpdateArrow();
        }

        private void ServerTick()
        {
            var s = m_State.Value;
            float elapsed = Now - s.phaseStart;

            switch ((GustPhase)s.phase)
            {
                case GustPhase.Idle:
                    if (Loop != null && Loop.IsBuilding && Now >= m_NextGustAt)
                    {
                        float ang = Random.Range(0f, Mathf.PI * 2f);
                        m_State.Value = new GustState
                        {
                            phase = (byte)GustPhase.Warning,
                            dirX = Mathf.Cos(ang), dirZ = Mathf.Sin(ang),
                            phaseStart = Now,
                        };
                    }
                    break;

                case GustPhase.Warning:
                    if (elapsed >= Config.GustWarnSeconds)
                    {
                        s.phase = (byte)GustPhase.Blowing;
                        s.phaseStart = Now;
                        m_State.Value = s;
                    }
                    break;

                case GustPhase.Blowing:
                    if (elapsed >= Config.GustDurationSeconds || (Loop != null && !Loop.IsBuilding))
                    {
                        s.phase = (byte)GustPhase.Idle;
                        s.phaseStart = Now;
                        m_State.Value = s;
                        ScheduleNext();
                    }
                    break;
            }
        }

        // ── 플레이어 밀림(오너 로컬에서 폴링) ─────────────────────────────────
        /// <summary>지금 이 위치의 플레이어가 받아야 할 수평 밀림 속도(m/s). 돌풍 밖/저지대/엄폐 시 zero.</summary>
        public static Vector3 CurrentPushAt(Transform player)
        {
            var g = Instance;
            return g != null ? g.LocalPush(player) : Vector3.zero;
        }

        /// <summary>스턴 시간(돌풍 설정). 기믹이 없으면 기본 2초.</summary>
        public static float StunSecondsOrDefault => Instance != null ? Instance.Config.StunSeconds : 2f;

        private Vector3 LocalPush(Transform player)
        {
            if (!Active || Phase != GustPhase.Blowing) return Vector3.zero;

            float gridY = (player.position.y - GridContract.Origin.y) / GridContract.Unit;
            float cells = Config.PushCellsAtHeight(gridY);
            if (cells <= 0f) return Vector3.zero;

            var dir = WindDir;
            if (dir.sqrMagnitude < 1e-4f) return Vector3.zero;
            dir.Normalize();
            if (IsSheltered(player, dir)) return Vector3.zero;

            // '돌풍 시간 동안 총 cells칸 밀림'이 되는 등속(저항 없이 서 있을 때 기준)
            return dir * (cells * GridContract.Unit / Config.GustDurationSeconds);
        }

        // 고정된 물체에 기대기/뒤에 숨기 — 바람이 '불어오는' 쪽으로 솔리드가 가까이 있으면 면제.
        // 미고정 재료 픽업은 트리거 콜라이더뿐이라 자동 제외(솔리드만 바람을 막는다).
        private static bool IsSheltered(Transform player, Vector3 windDir)
        {
            var origin = player.position + Vector3.up * 0.6f;
            foreach (var h in Physics.RaycastAll(origin, -windDir, 1.2f, ~0, QueryTriggerInteraction.Ignore))
            {
                var t = h.collider.transform;
                if (t == player || t.IsChildOf(player)) continue;
                if (h.collider.CompareTag("Player")) continue;     // 다른 달팽이는 바람막이가 아니다
                if (h.collider.CompareTag("Boundary")) continue;   // 투명 경계벽도 제외
                return true;
            }
            return false;
        }

        // ── 연출(전 클라 로컬): 예고 토스트 + 하늘 풍향 화살표 ─────────────────
        private GameObject m_Arrow;

        private void OnStateChanged(GustState _, GustState next)
        {
            if ((GustPhase)next.phase != GustPhase.Warning) return;

            // 내 화면 위 예고(오너 로컬 위치)
            var nm = NetworkManager.Singleton;
            var po = nm != null && nm.LocalClient != null ? nm.LocalClient.PlayerObject : null;
            if (po != null)
                GridJuice.WorldToast(po.transform.position + Vector3.up * 2.6f, "⚠ 돌풍 주의!", new Color(1f, 0.65f, 0.2f));
            // TODO(사운드팀): 돌풍 예고/바람 루프 SFX — GridSoundBridge에 전용 이름 추가 후 여기서 호출
        }

        private void UpdateArrow()
        {
            bool show = Phase != GustPhase.Idle;
            if (!show) { if (m_Arrow != null) m_Arrow.SetActive(false); return; }

            if (m_Arrow == null)
            {
                m_Arrow = GameObject.CreatePrimitive(PrimitiveType.Cube);
                m_Arrow.name = "~GustArrow";
                var col = m_Arrow.GetComponent<Collider>();
                if (col != null) Destroy(col);
                m_Arrow.transform.localScale = new Vector3(0.5f, 0.5f, 4f);   // 긴 축 = 바람 방향
            }
            if (!m_Arrow.activeSelf) m_Arrow.SetActive(true);

            // 그리드 상공 중앙에 풍향 표시(펄스로 시선 끌기)
            var gm = Grid;
            var size = gm != null ? gm.EffectiveSize : new Vector3Int(16, 10, 16);
            var center = GridContract.Origin + new Vector3(size.x * 0.5f, size.y + 3f, size.z * 0.5f) * GridContract.Unit;
            m_Arrow.transform.position = center;
            var dir = WindDir;
            if (dir.sqrMagnitude > 1e-4f) m_Arrow.transform.rotation = Quaternion.LookRotation(dir);
            bool blowing = Phase == GustPhase.Blowing;
            float pulse = 1f + Mathf.Abs(Mathf.Sin(Time.time * (blowing ? 10f : 5f))) * 0.25f;
            m_Arrow.transform.localScale = new Vector3(0.5f * pulse, 0.5f * pulse, 4f);
            TintArrow(blowing ? new Color(1f, 0.25f, 0.2f) : new Color(1f, 0.65f, 0.2f));
        }

        private void DestroyArrow()
        {
            if (m_Arrow != null) Destroy(m_Arrow);
            m_Arrow = null;
        }

        private static Material s_Mat;
        private void TintArrow(Color c)
        {
            var r = m_Arrow != null ? m_Arrow.GetComponent<Renderer>() : null;
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
