using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace GridSystem
{
    /// <summary>
    /// DDP 기믹 ② 유구(遺構) 발굴터 —
    /// 2008년 동대문운동장을 철거하다 한양도성 성곽 265m와 이간수문, 조선 전기 유물 2,500여 점이 발굴돼
    /// DDP 설계가 바뀌고 그 자리에 동대문역사문화공원·유구전시장이 남았다. 그 발굴을 기믹으로 옮긴 것.
    ///
    /// · 후보 지점(Spot_DigSite0~N) 중 한 곳이 랜덤으로 드러난다.
    /// · 그 위에 올라서서 버티면(DigSeconds) 보너스 재료가 출토된다 — 주문·배송을 기다리지 않아도 된다.
    ///   여럿이 함께 서면 그만큼 빨리 파진다(협동 보상).
    /// · 물길(WaterGateNetwork)이 흐르는 동안엔 잠겨서 팔 수 없고, 물이 빠지면 즉시 새 유구가 드러난다.
    ///   → 기믹 ①과 한 사이클로 맞물린다.
    ///
    /// 서버 권위: 어느 지점이 드러났는지·진행도·출토를 서버가 정하고 NetworkVariable로 복제.
    /// 발굴 판정은 서버가 접속 플레이어 위치를 직접 보므로 PlayerCarry(도구 시스템)와 결합하지 않는다.
    /// GameLoopManager가 런타임 부착, 맵 카드에 DdpGimmickConfig가 없으면 스스로 잠잔다.
    /// </summary>
    public class ExcavationNetwork : DdpGimmickBase
    {
        public struct DigState : INetworkSerializable, System.IEquatable<DigState>
        {
            public sbyte site;      // 드러난 발굴터 index. -1 = 없음(잠김/쿨다운)
            public byte diggers;    // 지금 파고 있는 인원(연출용)
            public float progress;  // 0~1
            public byte found;      // 이번 라운드 누적 출토 수

            public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
            {
                s.SerializeValue(ref site);
                s.SerializeValue(ref diggers);
                s.SerializeValue(ref progress);
                s.SerializeValue(ref found);
            }
            public bool Equals(DigState o) =>
                site == o.site && diggers == o.diggers &&
                Mathf.Approximately(progress, o.progress) && found == o.found;
        }

        private readonly NetworkVariable<DigState> m_State =
            new(new DigState { site = -1 }, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        /// <summary>이 맵에서 발굴터가 켜져 있으면 인스턴스. 아니면 null.</summary>
        public static ExcavationNetwork Instance { get; private set; }

        /// <summary>지금 드러나 있는 발굴터 index(-1이면 없음).</summary>
        public int ExposedSite => m_State.Value.site;

        private readonly List<Vector3> m_Sites = new();
        private float m_NextExposeAt;    // 서버 전용: 다음 유구가 드러날 시각
        private int m_LastSite = -1;     // 같은 자리 연속 노출 방지
        private MaterialDropField m_Drop;
        private MaterialDepot m_Depot;

        protected override void OnGimmickSpawn()
        {
            Instance = this;
            if (IsServer)
            {
                m_NextExposeAt = Now + 3f;                      // 라운드 시작 직후 첫 유구
                WaterGateNetwork.FloodEnded += OnFloodEnded;    // 물이 빠지면 즉시 새 유구
            }
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this) Instance = null;
            WaterGateNetwork.FloodEnded -= OnFloodEnded;
            DestroyVisuals();
        }

        /// <summary>재시작용(서버): 발굴터 감추고 누적 출토 수 초기화.</summary>
        public void ServerReset()
        {
            if (!IsServer || !Active) return;
            m_State.Value = new DigState { site = -1 };
            m_LastSite = -1;
            m_NextExposeAt = Now + 3f;
        }

        // 물이 빠진 직후 = 새 유구가 드러나는 순간(쿨다운을 건너뛴다).
        private void OnFloodEnded()
        {
            if (!IsServer || !Active) return;
            m_NextExposeAt = Now;
        }

        private void Update()
        {
            if (!Active || !IsSpawned) return;
            RefreshMarkers(DdpSpots.DigSitePrefix, m_Sites, 1);
            if (IsServer) ServerTick();
            UpdateVisuals();
        }

        private void ServerTick()
        {
            if (m_Sites.Count == 0) return;
            var s = m_State.Value;

            // 물이 차면 잠긴다 — 드러나 있던 유구도 다시 묻히고 진행도가 날아간다.
            if (WaterGateNetwork.IsFlooding)
            {
                if (s.site >= 0 || s.progress > 0f)
                {
                    s.site = -1; s.progress = 0f; s.diggers = 0;
                    m_State.Value = s;
                }
                return;
            }

            if (Loop == null || !Loop.IsBuilding) return;

            // 라운드 출토 한도
            bool capped = Config.DigMaxPerRound > 0 && s.found >= Config.DigMaxPerRound;

            if (s.site < 0)
            {
                if (capped || Now < m_NextExposeAt) return;
                s.site = (sbyte)PickSite();
                s.progress = 0f;
                s.diggers = 0;
                m_State.Value = s;
                m_LastSite = s.site;
                return;
            }

            // 드러난 유구 위에 서 있는 인원수만큼 게이지가 찬다(협동일수록 빠르게).
            int diggers = CountDiggersOn(m_Sites[s.site]);
            float perSec = Mathf.Max(0.01f, Config.DigSeconds);
            float delta = diggers * Time.deltaTime / perSec;

            if (diggers == 0)
            {
                // 아무도 없으면 천천히 되돌아간다(자리를 지켜야 한다는 압박)
                delta = -Time.deltaTime / (perSec * 2f);
            }

            float next = Mathf.Clamp01(s.progress + delta);
            if (next >= 1f)
            {
                Unearth(m_Sites[s.site]);
                s.site = -1;
                s.progress = 0f;
                s.diggers = 0;
                s.found = (byte)Mathf.Min(255, s.found + 1);
                m_State.Value = s;
                m_NextExposeAt = Now + Config.DigRespawnSeconds;
                return;
            }

            // 값이 의미 있게 바뀔 때만 복제(매 프레임 쓰기 방지)
            if (Mathf.Abs(next - s.progress) > 0.02f || diggers != s.diggers)
            {
                s.progress = next;
                s.diggers = (byte)Mathf.Min(255, diggers);
                m_State.Value = s;
            }
        }

        private int PickSite()
        {
            if (m_Sites.Count == 1) return 0;
            int idx;
            do { idx = Random.Range(0, m_Sites.Count); } while (idx == m_LastSite);
            return idx;
        }

        // 서버: 발굴터 반경 안에 서 있는(공중이 아닌) 플레이어 수.
        private int CountDiggersOn(Vector3 site)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null) return 0;

            int n = 0;
            float r2 = Config.DigRadius * Config.DigRadius;
            foreach (var c in nm.ConnectedClientsList)
            {
                var po = c.PlayerObject;
                if (po == null) continue;
                var p = po.transform.position;
                if (Mathf.Abs(p.y - site.y) > 2f) continue;   // 위층에서 지나가는 건 발굴이 아니다
                var flat = new Vector2(p.x - site.x, p.z - site.z);
                if (flat.sqrMagnitude <= r2) n++;
            }
            return n;
        }

        // 서버: 보너스 재료 출토 — 이 맵에서 주문 가능한 재료 중 하나를 발굴터 위에 바로 떨군다.
        // 주문(MaterialDepot)을 거치지 않으므로 주문 한도(MaxSpawnCount)와 무관한 '덤'이다.
        // 그래서 DigMaxPerRound로 라운드당 개수를 따로 막는다.
        private void Unearth(Vector3 site)
        {
            if (m_Drop == null) m_Drop = FindFirstObjectByType<MaterialDropField>();
            if (m_Depot == null) m_Depot = FindFirstObjectByType<MaterialDepot>();
            if (m_Drop == null || m_Depot == null) return;

            var pool = m_Depot.OrderableMaterials;
            if (pool == null || pool.Count == 0) return;

            var pick = pool[Random.Range(0, pool.Count)];
            if (pick == null) return;

            var from = site + Vector3.up * 1.2f;
            m_Drop.ServerDeliver(pick.Id, from, site);
            UnearthedRpc(site);
            Debug.Log($"[DDP] 유구 출토: {pick.name} @ {site}");
        }

        [Rpc(SendTo.Everyone)]
        private void UnearthedRpc(Vector3 site)
        {
            GridJuice.WorldToast(site + Vector3.up * 1.6f, "⛏ 유구 출토!", new Color(0.90f, 0.78f, 0.45f));
            GridJuice.GroundHit(site, 1.0f);
            GridSoundBridge.PlaySFXAt("LandObject", site);
        }

        // ── 연출(전 클라 로컬): 드러난 발굴터 표시 + 진행 게이지 ────────────
        private GameObject m_Marker;      // 드러난 발굴터 테두리
        private GameObject m_Gauge;       // 진행도 막대
        private static readonly Color kSoil = new Color(0.62f, 0.48f, 0.32f);
        private static readonly Color kGold = new Color(0.95f, 0.80f, 0.35f);

        private void UpdateVisuals()
        {
            var s = m_State.Value;
            bool show = s.site >= 0 && s.site < m_Sites.Count;

            if (!show)
            {
                if (m_Marker != null) m_Marker.SetActive(false);
                if (m_Gauge != null) m_Gauge.SetActive(false);
                return;
            }

            var site = m_Sites[s.site];

            if (m_Marker == null)
            {
                m_Marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                m_Marker.name = "~DdpDigSite";
                var col = m_Marker.GetComponent<Collider>();
                if (col != null) Destroy(col);
            }
            if (!m_Marker.activeSelf) m_Marker.SetActive(true);
            m_Marker.transform.position = site + Vector3.up * 0.03f;
            m_Marker.transform.localScale = new Vector3(Config.DigRadius * 2f, 0.03f, Config.DigRadius * 2f);
            // 파는 중엔 금빛으로 달아오른다
            float pulse = 0.7f + Mathf.Abs(Mathf.Sin(Time.time * (s.diggers > 0 ? 7f : 2.5f))) * 0.3f;
            Tint(m_Marker, Color.Lerp(kSoil, kGold, s.progress) * pulse);

            if (m_Gauge == null)
            {
                m_Gauge = GameObject.CreatePrimitive(PrimitiveType.Cube);
                m_Gauge.name = "~DdpDigGauge";
                var col = m_Gauge.GetComponent<Collider>();
                if (col != null) Destroy(col);
            }
            bool gaugeOn = s.progress > 0.01f;
            if (m_Gauge.activeSelf != gaugeOn) m_Gauge.SetActive(gaugeOn);
            if (gaugeOn)
            {
                const float kWidth = 2.0f;
                m_Gauge.transform.position = site + Vector3.up * 1.9f;
                m_Gauge.transform.localScale = new Vector3(kWidth * s.progress, 0.16f, 0.16f);
                var cam = Camera.main;
                if (cam != null) m_Gauge.transform.rotation = Quaternion.LookRotation(m_Gauge.transform.position - cam.transform.position);
                Tint(m_Gauge, kGold);
            }
        }

        private void DestroyVisuals()
        {
            if (m_Marker != null) Destroy(m_Marker);
            if (m_Gauge != null) Destroy(m_Gauge);
            m_Marker = null;
            m_Gauge = null;
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
