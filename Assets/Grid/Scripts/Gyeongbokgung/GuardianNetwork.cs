using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace GridSystem
{
    /// <summary>
    /// 사방신 석상 기믹(서버 권위) — 기획서(08/27):
    /// · 건축 진행도 20/30/45/60% 도달마다 석상 1개가 빛기둥과 함께 광장 중앙(GuardianDropPoint)에 낙하
    /// · 석상 = MaterialDef(IsHeavy) 재료 — 들기/2인 운반/던지기는 기존 운반 시스템이 공짜로 처리
    /// · 받침대(Pedestal_East/West/South/North) 근처에 놓으면: 맞는 방위 → 안착 + 정령 등장 + 그 방위 절반 화재 면역
    ///   틀린 방위 → 튕겨냄 + 효과음 (벌점 없음)
    /// · 4개 완성 → 화마 완전 봉인(FireNetwork가 IsSealed를 본다)
    /// 방위 매핑: 동=청룡·서=백호·남=주작·북=현무 (Config.StatueMaterialIds 순서).
    /// </summary>
    public class GuardianNetwork : GyeongbokgungGimmickBase
    {
        public static GuardianNetwork Instance { get; private set; }

        private static readonly string[] kPedestalNames = { "Pedestal_East", "Pedestal_West", "Pedestal_South", "Pedestal_North" };
        private static readonly string[] kKindNames = { "청룡", "백호", "주작", "현무" };
        private static readonly Color[] kKindColors =
        {
            new Color(0.30f, 0.55f, 1.00f),   // 동 청룡 靑
            new Color(0.95f, 0.95f, 1.00f),   // 서 백호 白
            new Color(1.00f, 0.35f, 0.30f),   // 남 주작 赤
            new Color(0.25f, 0.22f, 0.35f),   // 북 현무 黑
        };

        // 비트 i(0~3) = 해당 방위 석상 안착됨. 복제 한 개로 전 클라 정령/면역 상태 동기화.
        private readonly NetworkVariable<int> m_PlacedMask =
            new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        // 낙하한 석상 수(서버 전용 진행 문턱 소비용이지만, HUD 확장 대비 복제로 둔다)
        private readonly NetworkVariable<int> m_DroppedCount =
            new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private MaterialDropField m_Drop;
        private readonly Transform[] m_Pedestals = new Transform[4];
        private Transform m_DropPoint;
        private readonly List<PickupEntry> m_Scratch = new();
        private readonly GameObject[] m_Spirits = new GameObject[4];
        private readonly GameObject[] m_Statues = new GameObject[4];   // 받침대 위에 안착한 석상 비주얼(클라 로컬)
        private readonly GameObject[] m_Zones = new GameObject[4];     // 보호 구역 상시 바닥 판(클라 로컬)
        private float m_NextScanAt;
        private float m_NextDropAllowedAt;   // 서버 전용 — 낙하 최소 간격
        private float m_NextBlockedLogAt;    // 서버 전용 — 낙하 막힘 경고 스로틀

        private const float kPedestalTopY = 0.95f;   // 받침대 상판 높이 — 석상이 이 위에 앉는다

        /// <summary>4방위 전부 안착 — 화마 봉인. FireNetwork가 매 틱 조회한다(null-safe).</summary>
        public static bool IsSealed => Instance != null && Instance.Active && Instance.m_PlacedMask.Value == 0b1111;

        /// <summary>이 셀이 정령 보호(화재 면역) 구역인가.
        /// [08/28] 방위당 '그리드 절반'은 둘만 놓아도(동+서, 남+북) 전면 면역이 되는 밸런스 구멍 —
        /// 각 방위는 자기 쪽 '가장자리 띠'(기본 1/3 폭)만 보호한다. 중앙부는 4개 봉인 전까지 계속 탄다.</summary>
        public static bool IsCellImmune(Vector3Int cell)
        {
            if (Instance == null || !Instance.Active) return false;
            int mask = Instance.m_PlacedMask.Value;
            if (mask == 0) return false;
            var size = Instance.Grid != null ? Instance.Grid.EffectiveSize : new Vector3Int(30, 13, 20);
            float f = Instance.Config != null ? Mathf.Clamp01(Instance.Config.ImmunityBandFraction) : 0.34f;
            if ((mask & (1 << 0)) != 0 && cell.x >= size.x * (1f - f)) return true;   // 동쪽 띠
            if ((mask & (1 << 1)) != 0 && cell.x < size.x * f) return true;           // 서쪽 띠
            if ((mask & (1 << 2)) != 0 && cell.z < size.z * f) return true;           // 남쪽 띠
            if ((mask & (1 << 3)) != 0 && cell.z >= size.z * (1f - f)) return true;   // 북쪽 띠
            return false;
        }

        protected override void Awake()
        {
            base.Awake();
            m_Drop = GetComponent<MaterialDropField>();
        }

        protected override void OnGimmickSpawn()
        {
            Instance = this;
            m_PlacedMask.OnValueChanged += OnPlacedChanged;
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this) Instance = null;
            m_PlacedMask.OnValueChanged -= OnPlacedChanged;
            for (int i = 0; i < 4; i++)
            {
                if (m_Spirits[i] != null) Destroy(m_Spirits[i]);
                if (m_Statues[i] != null) Destroy(m_Statues[i]);
                if (m_Zones[i] != null) Destroy(m_Zones[i]);
            }
        }

        public void ServerReset()
        {
            if (!IsServer || !Active) return;
            m_PlacedMask.Value = 0;
            m_DroppedCount.Value = 0;
            m_NextDropAllowedAt = 0f;
            // 바닥 석상 픽업은 MaterialDropField.ServerReset()이 함께 정리한다.
        }

        private void Update()
        {
            if (!Active || !IsSpawned) return;
            RefreshMarkers();
            if (IsServer) ServerTick();
            UpdateSpirits();
        }

        private void RefreshMarkers()
        {
            if (m_DropPoint == null) { var t = FindMarker("GuardianDropPoint"); if (t != null) m_DropPoint = t; }
            for (int i = 0; i < 4; i++)
                if (m_Pedestals[i] == null)
                {
                    var go = GameObject.Find(kPedestalNames[i]);
                    if (go != null) m_Pedestals[i] = go.transform;
                }
        }

        // ── 서버: 진행도 문턱 낙하 + 받침대 안착/거절 판정 ──────────────────
        private void ServerTick()
        {
            if (Loop == null || !Loop.IsBuilding) return;

            // ① 석상 낙하 — 하이브리드: '건축 시간' 문턱(기획서 20/30/45/60%) OR '점수 진행도' 문턱, 먼저 오는 쪽.
            // 빠른 팀은 진행도로 일찍 받고(스피드런 보상), 느린 팀도 시간이 하한을 보장(데스 스파이럴 방지).
            // 제한시간이 무제한(자유모드)이면 10분을 기준으로 삼는다.
            int dropped = m_DroppedCount.Value;
            var percents = Config.StatueDropPercents;
            var scorePercents = Config.StatueDropScorePercents;
            var ids = Config.StatueMaterialIds;
            float limit = Loop.TimeLimit;
            if (limit <= 0f || float.IsInfinity(limit)) limit = 600f;
            bool timeHit  = dropped < percents.Length && Loop.Elapsed >= limit * percents[dropped] * 0.01f;
            bool scoreHit = dropped < scorePercents.Length && Net != null && Net.ScorePercent >= scorePercents[dropped];
            if (dropped < ids.Length && (timeHit || scoreHit) && Now >= m_NextDropAllowedAt)
            {
                if (m_Drop == null || m_DropPoint == null)
                {
                    // 낙하 조건은 됐는데 인프라가 없다 — 원인 즉시 보이게(5초 스로틀)
                    if (Time.time >= m_NextBlockedLogAt)
                    {
                        m_NextBlockedLogAt = Time.time + 5f;
                        Debug.LogWarning($"[경복궁] 석상 낙하 대기: 경과 {Loop.Elapsed:F0}s ≥ 문턱 {limit * percents[dropped] * 0.01f:F0}s 인데 " +
                                         $"{(m_Drop == null ? "MaterialDropField 없음" : "GuardianDropPoint 마커를 못 찾음(맵 생성 재실행 필요?)")}");
                    }
                    return;
                }
                Vector3 to = m_DropPoint.position;
                Vector3 from = to + new Vector3(Random.Range(-1f, 1f), 26f, Random.Range(-1f, 1f));
                m_Drop.ServerDeliver(ids[dropped], from, to);
                m_DroppedCount.Value = dropped + 1;
                m_NextDropAllowedAt = Now + Config.StatueDropMinGapSeconds;
                Debug.Log($"[경복궁] 사방신 석상 낙하 {dropped + 1}/4 ({kKindNames[dropped]}) — 경과 {Loop.Elapsed:F0}초, 진행도 {(Net != null ? Net.ScorePercent : 0f):F0}% ({(scoreHit ? "진행도" : "시간")} 문턱)");
                StatueDropFxRpc(to, dropped);
            }

            // ② 받침대 근처의 석상 픽업 스캔 (0.25초 간격이면 충분)
            if (Time.time < m_NextScanAt) return;
            m_NextScanAt = Time.time + 0.25f;
            if (m_Drop == null) return;

            m_Drop.ServerCollectPickups(m_Scratch);
            foreach (var p in m_Scratch)
            {
                int kind = KindOf(p.materialId);
                if (kind < 0) continue;
                for (int ped = 0; ped < 4; ped++)
                {
                    if (m_Pedestals[ped] == null) continue;
                    Vector3 pp = m_Pedestals[ped].position;
                    Vector3 d = p.pos - pp; d.y = 0f;
                    if (d.magnitude > Config.PedestalSnapRange) continue;

                    if (ped == kind && (m_PlacedMask.Value & (1 << kind)) == 0)
                    {
                        // 안착: 픽업 제거 + 상태 복제(정령은 클라가 마스크 변화로 띄움)
                        m_Drop.ServerRemove(p.pickupId);
                        m_PlacedMask.Value |= 1 << kind;
                        PlacedFxRpc(pp, kind, m_PlacedMask.Value == 0b1111);
                    }
                    else if (ped != kind)
                    {
                        // 틀린 받침대: 튕겨냄 (벌점 없음)
                        m_Drop.ServerRemove(p.pickupId);
                        Vector3 dir = d.sqrMagnitude > 0.01f ? d.normalized : Random.insideUnitSphere.WithY0().normalized;
                        m_Drop.ServerThrow(p.materialId, pp + Vector3.up * 1.2f, pp + dir * Config.RejectBounceDistance);
                        RejectFxRpc(pp, kind);
                    }
                    break;   // 이 픽업은 처리 끝(제거됨) — 다음 픽업으로
                }
            }
        }

        private int KindOf(int materialId)
        {
            var ids = Config.StatueMaterialIds;
            for (int i = 0; i < ids.Length; i++) if (ids[i] == materialId) return i;
            return -1;
        }

        // ── 클릭 배치 API (PlayerCarry가 사용 — 석상 든 채 받침대 클릭) ─────────
        /// <summary>이 재료가 사방신 석상이면 방위(0~3) 반환. 기믹 꺼진 맵에선 false.</summary>
        public static bool TryGetStatueKind(int materialId, out int kind)
        {
            kind = -1;
            if (Instance == null || !Instance.Active || materialId < 0) return false;
            kind = Instance.KindOf(materialId);
            return kind >= 0;
        }

        /// <summary>해당 방위 석상이 이미 안착됐는가.</summary>
        public static bool IsKindPlaced(int kind)
            => Instance != null && Instance.Active && kind >= 0 && (Instance.m_PlacedMask.Value & (1 << kind)) != 0;

        /// <summary>해당 방위 받침대 위치(화살표 안내용). 아직 못 찾았으면 false.</summary>
        public static bool TryGetPedestalPos(int kind, out Vector3 pos)
        {
            pos = default;
            if (Instance == null || !Instance.Active || kind < 0 || kind > 3) return false;
            var t = Instance.m_Pedestals[kind];
            if (t == null) return false;
            pos = t.position;
            return true;
        }

        /// <summary>클릭 배치 확정 요청 — 석상은 종류당 1개뿐이라 클라 낙관 소모 + 서버 재검증으로 충분.</summary>
        public static void RequestPlaceOnPedestal(int kind)
        {
            if (Instance != null && Instance.Active) Instance.PlaceStatueRpc(kind);
        }

        [Rpc(SendTo.Server)]
        private void PlaceStatueRpc(int kind)
        {
            if (kind < 0 || kind > 3) return;
            if ((m_PlacedMask.Value & (1 << kind)) != 0) return;   // 이미 안착됨(중복 요청 무시)
            m_PlacedMask.Value |= 1 << kind;
            Vector3 pp = m_Pedestals[kind] != null ? m_Pedestals[kind].position : Vector3.zero;
            PlacedFxRpc(pp, kind, m_PlacedMask.Value == 0b1111);
        }

        // ── 연출 (전 클라) ──────────────────────────────────────────────
        /// <summary>석상 낙하 화면 연출 훅 — GameLoopHUD가 구독(어셈블리 방향상 이벤트 — FireNetwork.FirstFireCinematic과 같은 계약).</summary>
        public static event System.Action<string, Color> StatueArrived;

        [Rpc(SendTo.Everyone)]
        private void StatueDropFxRpc(Vector3 pos, int kind)
        {
            LightPillar(pos, kKindColors[kind]);
            GridJuice.WorldToast(pos + Vector3.up * 2.5f, $"사방신 석상이 내려왔다! ({kKindNames[kind]})", new Color(1f, 0.92f, 0.5f));
            GridSoundBridge.PlaySFXAt("HolyChime", pos);   // 신 내려오는 소리(08/28 사운드 적용)
            // 화면 전체 연출(방위색 비네트 + 배너) — 화마 등장과 같은 문법(08/28 피드백)
            StatueArrived?.Invoke($"앞마당에 {kKindNames[kind]} 석상이 도착했다..!", kKindColors[kind]);
        }

        [Rpc(SendTo.Everyone)]
        private void PlacedFxRpc(Vector3 pos, int kind, bool sealedNow)
        {
            LightPillar(pos, kKindColors[kind]);
            ZoneFlash(kind);   // 이 방위가 지키는 그리드 절반을 잠깐 발광 — 어디가 화재 면역인지 보여준다(08/28 피드백)
            SpawnApparition(kind, pos + Vector3.up * kPedestalTopY);   // 사방신 환영 — 떠올랐다 사라진다
            GridJuice.GroundHit(pos, 1.1f);
            GridJuice.WorldToast(pos + Vector3.up * 2.2f, $"{kKindNames[kind]}이(가) 깨어났다!", kKindColors[kind]);
            GridSoundBridge.PlaySFXAt("HolyChime", pos);   // 안착·정령 강림(08/28 사운드 적용)
            if (sealedNow)
            {
                GridJuice.FovPunch(Camera.main, -4f);
                GridJuice.WorldToast(pos + Vector3.up * 3.4f, "사방신의 힘이 화마를 억누른다!", new Color(0.55f, 0.9f, 1f));
                StartCoroutine(SealSlayCo());   // 클라이맥스: 4색 빛살 → 화마 처치
            }
        }

        // 봉인 클라이맥스(08/28 '적의 실체화') — 4개 받침대에서 방위색 빛살이 화마에게 수렴, 명중 순간 폭발 소멸.
        private System.Collections.IEnumerator SealSlayCo()
        {
            yield return new WaitForSeconds(0.5f);   // 안착 연출 한 박자 뒤
            for (int i = 0; i < 4; i++)
                if (m_Pedestals[i] != null)
                    SealBolt.Fire(m_Pedestals[i].position + Vector3.up * 2.2f, ZoneDisplayColor(i), 0.9f);
            yield return new WaitForSeconds(0.95f);
            GridJuice.FovPunch(Camera.main, 5f);
            FireNetwork.DemonSlain();
            StatueArrived?.Invoke("사방신이 화마를 물리쳤다!!", new Color(1f, 0.85f, 0.3f));   // 금색 배너+비네트
        }

        // 봉인 빛살 — 화마 위치를 매 프레임 추적하며 가속 유도, 잔광을 흘린다. 수명 끝나면 자멸.
        private sealed class SealBolt : MonoBehaviour
        {
            private float m_Die;
            private Color m_Color;

            public static void Fire(Vector3 from, Color c, float life)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(go.GetComponent<Collider>());
                go.name = "~SealBolt";
                go.transform.position = from;
                go.transform.localScale = Vector3.one * 0.5f;
                go.GetComponent<Renderer>().sharedMaterial = MakeGlow(new Color(c.r, c.g, c.b, 0.95f));
                var b = go.AddComponent<SealBolt>();
                b.m_Die = Time.time + life;
                b.m_Color = c;
            }

            private void Update()
            {
                var tgt = FireNetwork.DemonPosition;
                if (tgt.HasValue)
                {
                    Vector3 d = tgt.Value - transform.position;
                    float sp = Mathf.Max(14f, d.magnitude / Mathf.Max(0.1f, m_Die - Time.time));
                    transform.position += d.normalized * Mathf.Min(d.magnitude, sp * Time.deltaTime);
                }
                var fx = GridJuice.MakeBit(transform.position, 0.09f, m_Color);   // 잔광 꼬리
                fx.vel = Vector3.zero; fx.gravity = 0f; fx.life = 0.3f;
                if (Time.time >= m_Die) Destroy(gameObject);
            }
        }

        [Rpc(SendTo.Everyone)]
        private void RejectFxRpc(Vector3 pos, int kindOnPedestal)
        {
            GridJuice.WorldToast(pos + Vector3.up * 2f, "방위가 다르다…!", new Color(1f, 0.55f, 0.35f));
            GridSoundBridge.PlaySFXAt("BumpPlayers", pos);
        }

        // ── 사방신 환영: T포즈 VARCO 모델(Resources/Gyeongbokgung/Apparition_*)이 석상 위로 떠오르며
        // 빙글 돌다 사르르 사라진다. 알파 페이드 대신 스케일 소멸(glTFast 재질은 투명 전환이 불안정) —
        // 애니메이션 없이도 '소환 환영'으로 자연스럽다. 모델이 없으면 조용히 생략.
        private static readonly string[] kApparitionRes =
        { "Gyeongbokgung/Apparition_Cheongryong", "Gyeongbokgung/Apparition_Baekho", "Gyeongbokgung/Apparition_Jujak", "Gyeongbokgung/Apparition_Hyeonmu" };

        private void SpawnApparition(int kind, Vector3 basePos)
        {
            var prefab = Resources.Load<GameObject>(kApparitionRes[kind]);
            if (prefab == null) return;
            var go = Instantiate(prefab, basePos + Vector3.up * 1.2f, Quaternion.identity);
            go.name = $"~Apparition_{kKindNames[kind]}";
            foreach (var c in go.GetComponentsInChildren<Collider>()) Destroy(c);
            var rends = go.GetComponentsInChildren<Renderer>();
            if (rends.Length > 0)   // VARCO glb는 크기가 제각각 — 높이 2.2m로 정규화
            {
                var b = rends[0].bounds;
                foreach (var r in rends) b.Encapsulate(r.bounds);
                float h = Mathf.Max(0.01f, b.size.y);
                go.transform.localScale *= 2.2f / h;
            }
            go.AddComponent<GuardianApparition>();
        }

        private sealed class GuardianApparition : MonoBehaviour
        {
            const float kLife = 3f;
            float m_T; Vector3 m_Base, m_Scale;
            void Start() { m_Base = transform.position; m_Scale = transform.localScale; }
            void Update()
            {
                m_T += Time.deltaTime;
                float n = m_T / kLife;
                if (n >= 1f) { Destroy(gameObject); return; }
                transform.position = m_Base + Vector3.up * (2.3f * n);        // 천천히 승천
                transform.rotation = Quaternion.Euler(0f, 80f * m_T, 0f);     // 빙글
                float s = n < 0.12f ? Mathf.SmoothStep(0f, 1f, n / 0.12f)     // 뿅 등장
                        : n > 0.6f  ? Mathf.SmoothStep(1f, 0f, (n - 0.6f) / 0.4f)   // 사르르 소멸
                        : 1f;
                transform.localScale = m_Scale * Mathf.Max(0.001f, s);
            }
        }

        // ── 보호 구역 표시(08/28 재작업) ─────────────────────────────────
        // 이전 구현의 실패 요인: ① 그리드 전체 높이 볼륨이라 '안에 서 있으면' 안쪽 면이 컬링돼 아무것도 안 보임
        // ② 어두운 방위색(현무)은 가산 발광값이 0에 수렴해 투명 ③ 2.5초 반짝뿐이라 놓치면 끝.
        // → 바닥에 붙는 얇은 발광 판으로 바꾸고(위에서 항상 보임), 색은 밝게 보정, 안착 동안 '상시' 표시 + 안착 순간 강한 플래시.

        // IsCellImmune과 정확히 같은 경계의 띠(셀 좌표). 시각과 판정이 어긋나면 안 된다.
        private (int x0, int x1, int z0, int z1) ZoneBandCells(int kind)
        {
            var size = Grid != null ? Grid.EffectiveSize : new Vector3Int(30, 13, 20);
            float f = Config != null ? Mathf.Clamp01(Config.ImmunityBandFraction) : 0.34f;
            int x0 = 0, x1 = size.x, z0 = 0, z1 = size.z;
            switch (kind)
            {
                case 0: x0 = Mathf.RoundToInt(size.x * (1f - f)); break;   // 동쪽 띠
                case 1: x1 = Mathf.RoundToInt(size.x * f); break;          // 서쪽 띠
                case 2: z1 = Mathf.RoundToInt(size.z * f); break;          // 남쪽 띠
                case 3: z0 = Mathf.RoundToInt(size.z * (1f - f)); break;   // 북쪽 띠
            }
            return (x0, x1, z0, z1);
        }

        // 어두운 방위색(현무 등)도 보이게 표시용으로 밝힌 색
        private static Color ZoneDisplayColor(int kind) => Color.Lerp(kKindColors[kind], Color.white, 0.3f);

        // 띠 범위를 덮는 얇은 바닥 발광 판(두께 0.08, 발목 높이 — 위에서 항상 보인다)
        private GameObject BuildZoneSlab(int kind, float alpha, float y, float thick)
        {
            var (x0, x1, z0, z1) = ZoneBandCells(kind);
            Vector3 wmin = GridCoordinates.CellToWorld(new Vector3Int(x0, 0, z0));
            Vector3 wmax = GridCoordinates.CellToWorld(new Vector3Int(x1, 0, z1));
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(go.GetComponent<Collider>());
            var c = (wmin + wmax) * 0.5f;
            go.transform.position = new Vector3(c.x, wmin.y + y, c.z);
            go.transform.localScale = new Vector3(wmax.x - wmin.x, thick, wmax.z - wmin.z);
            var col = ZoneDisplayColor(kind);
            go.GetComponent<Renderer>().sharedMaterial = MakeGlow(new Color(col.r, col.g, col.b, alpha));
            return go;
        }

        // 안착 순간 강한 플래시(2초 페이드) — 상시 판 위에 겹쳐서 '지금 여기가 켜졌다'를 보여준다
        private void ZoneFlash(int kind)
        {
            var go = BuildZoneSlab(kind, 0.5f, 0.12f, 0.25f);
            go.name = $"~GuardZoneFlash_{kKindNames[kind]}";
            go.AddComponent<PillarFade>().Life = 2f;
        }

        // 상시 보호 구역 판 — 석상이 안착해 있는 동안 계속 표시(UpdateSpirits가 마스크 따라 켜고 끈다)
        private GameObject BuildZoneOverlay(int kind)
        {
            var go = BuildZoneSlab(kind, 0.2f, 0.05f, 0.08f);
            go.name = $"~GuardZone_{kKindNames[kind]}";
            return go;
        }

        // 절차 생성 빛기둥 — 세로로 긴 발광 기둥이 2초에 걸쳐 사라진다.
        private static void LightPillar(Vector3 basePos, Color c)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "~LightPillar";
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.position = basePos + Vector3.up * 14f;
            go.transform.localScale = new Vector3(1.4f, 28f, 1.4f);
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = MakeGlow(new Color(c.r, c.g, c.b, 0.45f));
            var fade = go.AddComponent<PillarFade>();
            fade.Life = 2.2f;
        }

        // URP Unlit 가산 발광 재질(ItemFx.GlowMat 패턴 — 색만 인스턴스별로 박음). PlayerCarry(다른 asm)도 씀.
        public static Material MakeGlow(Color c)
        {
            var sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            var m = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            m.SetFloat("_Surface", 1f);
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
            m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            m.SetColor("_BaseColor", c);
            m.SetColor("_Color", c);
            return m;
        }

        private class PillarFade : MonoBehaviour
        {
            public float Life = 2f;
            private float m_T;
            private void Update()
            {
                m_T += Time.deltaTime;
                float k = 1f - Mathf.Clamp01(m_T / Life);
                transform.localScale = new Vector3(1.4f * k, 28f, 1.4f * k);
                if (m_T >= Life) Destroy(gameObject);
            }
        }

        // ── 정령(클라 로컬) — 안착된 방위 위에 둥둥. 전용 모델(Resources/Gyeongbokgung/Spirit_*)이 있으면 그걸,
        //    없으면 발광 구 플레이스홀더(유저가 정령 모델 제작 중 — 나오면 Resources에 넣기만 하면 됨). ──
        private void OnPlacedChanged(int _, int __) { /* UpdateSpirits가 다음 프레임에 반영 */ }

        private static readonly string[] kSpiritRes = { "Gyeongbokgung/Spirit_Cheongryong", "Gyeongbokgung/Spirit_Baekho", "Gyeongbokgung/Spirit_Jujak", "Gyeongbokgung/Spirit_Hyeonmu" };

        private void UpdateSpirits()
        {
            int mask = m_PlacedMask.Value;
            for (int i = 0; i < 4; i++)
            {
                bool want = (mask & (1 << i)) != 0 && m_Pedestals[i] != null;
                if (want && m_Statues[i] == null)
                    m_Statues[i] = BuildPlacedStatue(i, m_Pedestals[i].position + Vector3.up * kPedestalTopY);
                else if (!want && m_Statues[i] != null)
                {
                    Destroy(m_Statues[i]);
                    m_Statues[i] = null;
                }

                if (want && m_Spirits[i] == null)
                    m_Spirits[i] = BuildSpirit(i, m_Pedestals[i].position + Vector3.up * (kPedestalTopY + 2.9f));
                else if (!want && m_Spirits[i] != null)
                {
                    Destroy(m_Spirits[i]);
                    m_Spirits[i] = null;
                }

                // 보호 구역 상시 판 — 안착해 있는 동안 바닥에 방위색으로 계속 표시(어디가 면역인지 항상 보이게)
                bool wantZone = (mask & (1 << i)) != 0;
                if (wantZone && m_Zones[i] == null)
                    m_Zones[i] = BuildZoneOverlay(i);
                else if (!wantZone && m_Zones[i] != null)
                {
                    Destroy(m_Zones[i]);
                    m_Zones[i] = null;
                }
            }
        }

        // 안착한 석상 — 재료 def의 프리팹(min-corner 피벗, 2×2×2)을 받침대 상판 정중앙에 앉힌다.
        private GameObject BuildPlacedStatue(int kind, Vector3 topCenter)
        {
            var ids = Config.StatueMaterialIds;
            var def = (Grid != null && Grid.Catalog != null && kind < ids.Length) ? Grid.Catalog.GetById(ids[kind]) : null;
            if (def == null || def.Prefab == null) return null;

            var root = new GameObject($"~PlacedStatue_{kKindNames[kind]}");
            root.transform.position = topCenter;
            var vis = Instantiate(def.Prefab, root.transform);
            var fp = def.Footprint;
            vis.transform.localPosition = new Vector3(-fp.x * 0.5f, 0f, -fp.z * 0.5f);   // min-corner → 중앙 정렬(바닥 기준)
            foreach (var c in root.GetComponentsInChildren<Collider>()) Destroy(c);
            GridJuice.Squish(root, 0.25f);   // 안착 순간 뽁
            return root;
        }

        private GameObject BuildSpirit(int kind, Vector3 pos)
        {
            var prefab = Resources.Load<GameObject>(kSpiritRes[kind]);
            GameObject go;
            if (prefab != null)
            {
                go = Instantiate(prefab, pos, Quaternion.identity);
            }
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(go.GetComponent<Collider>());
                go.transform.position = pos;
                go.transform.localScale = Vector3.one * 1.1f;
                var r = go.GetComponent<Renderer>();
                r.sharedMaterial = MakeGlow(new Color(kKindColors[kind].r, kKindColors[kind].g, kKindColors[kind].b, 0.75f));
            }
            go.name = $"~Spirit_{kKindNames[kind]}";
            go.AddComponent<JuiceBob>();
            return go;
        }
    }

    internal static class GuardianVecExt
    {
        public static Vector3 WithY0(this Vector3 v) { v.y = 0f; return v; }
    }
}
