using SeoulZikimi.Gameplay;
using SeoulZikimi.Weather;
using Unity.Netcode;
using UnityEngine;

namespace GridSystem
{
    /// <summary>
    /// 2vs2 경쟁 아이템 네트워크 호스트(서버 권위) — GameLoopManager와 같은 오브젝트(런타임 자동 부착).
    /// 규칙(30초 월드 스폰 · 완성도 10% 최초 달성 보상 · 미사용 60초 소멸)은 프레임워크
    /// CompetitiveItemSpawnDirector가 결정하고, 이 클래스는 월드 배치/복제/획득/사용만 담당한다.
    /// 획득 = 빈손으로 접근(1.2m), 사용 = E키(도구를 안 든 상태 — PlayerCarry가 공정과 갈라서 호출).
    /// 효과 실행은 프레임워크 CompetitiveItemUseService가 맡고, 이 클래스는 각 효과 인터페이스의 월드 구현이다.
    /// </summary>
    public class ItemNetwork : NetworkBehaviour,
        ICompetitiveItemSpawnGateway, IOpponentTeamResolver,
        IUnfixedConstructionTarget, ICompletedConstructionTarget, ITeamMovementModifierTarget, ITeamProcessModifierTarget,
        ITeamOrderLockTarget, ITeamFogTarget, ITemporaryTeamWeatherTarget, ITeamWeatherImmunityTarget
    {
        private const float kPickupRange = 1.2f;
        private const string kTeamA = "A", kTeamB = "B";

        private struct ItemEntry : INetworkSerializable, System.IEquatable<ItemEntry>
        {
            public uint Id;
            public int Kind;
            public Vector3 Pos;
            public bool Held;
            public ulong Holder;
            public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
            {
                s.SerializeValue(ref Id); s.SerializeValue(ref Kind); s.SerializeValue(ref Pos);
                s.SerializeValue(ref Held); s.SerializeValue(ref Holder);
            }
            public bool Equals(ItemEntry o) => Id == o.Id && Kind == o.Kind && Pos == o.Pos && Held == o.Held && Holder == o.Holder;
        }

        /// <summary>팀에 걸린 지속 효과(서버가 만료 관리, 클라는 값만 읽는다).</summary>
        private struct TeamEffects : INetworkSerializable, System.IEquatable<TeamEffects>
        {
            public float MoveMul;       // 이동속도 배율
            public float ProcessMul;    // 공정(망치·페인트) 진행 속도 배율
            public bool OrderBlocked;   // 재료 주문 차단(해킹)
            public int Weather;         // 이 팀 진영에 걸린 날씨(WeatherKind, Sunny=없음)
            public bool Fog;            // 화면 가림
            public bool WeatherImmune;  // 우산 — 날씨의 게임플레이 피해 무시
            // 만료 시각(NGO ServerTime 초) — HUD가 남은 시간을 표기(만료 판정은 서버 배열이 담당)
            public float MoveUntil, ProcUntil, OrderUntil, WeatherUntil, FogUntil, ImmuneUntil;

            public static TeamEffects None => new()
            {
                MoveMul = 1f, ProcessMul = 1f, OrderBlocked = false,
                Weather = (int)SeoulZikimi.Weather.WeatherKind.Sunny, Fog = false, WeatherImmune = false,
            };

            public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
            {
                s.SerializeValue(ref MoveMul); s.SerializeValue(ref ProcessMul); s.SerializeValue(ref OrderBlocked);
                s.SerializeValue(ref Weather); s.SerializeValue(ref Fog); s.SerializeValue(ref WeatherImmune);
                s.SerializeValue(ref MoveUntil); s.SerializeValue(ref ProcUntil); s.SerializeValue(ref OrderUntil);
                s.SerializeValue(ref WeatherUntil); s.SerializeValue(ref FogUntil); s.SerializeValue(ref ImmuneUntil);
            }
            public bool Equals(TeamEffects o) => MoveMul == o.MoveMul && ProcessMul == o.ProcessMul
                && OrderBlocked == o.OrderBlocked && Weather == o.Weather && Fog == o.Fog && WeatherImmune == o.WeatherImmune
                && MoveUntil == o.MoveUntil && ProcUntil == o.ProcUntil && OrderUntil == o.OrderUntil
                && WeatherUntil == o.WeatherUntil && FogUntil == o.FogUntil && ImmuneUntil == o.ImmuneUntil;
        }

        /// <summary>클라·서버 공용 시계(NGO ServerTime) — 만료 시각 표기 기준.</summary>
        private static float NetNow()
            => NetworkManager.Singleton != null ? (float)NetworkManager.Singleton.ServerTime.Time : Time.time;

        private readonly NetworkList<ItemEntry> m_Items = new();
        private readonly NetworkVariable<TeamEffects> m_FxA =
            new(TeamEffects.None, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<TeamEffects> m_FxB =
            new(TeamEffects.None, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private GameLoopManager m_Loop;
        private GridManager m_Grid;
        private GridNetwork m_Net;
        private CompetitiveItemSpawnDirector m_Director;   // 서버 전용
        private CompetitiveItemUseService m_UseService;    // 서버 전용(대상 팀 결정 + 효과 실행)
        private CompetitiveItemDefinitionCatalog m_Definitions;   // 서버 전용(사용 알림의 대상 팀 판정)
        private uint m_NextId;
        // 서버 전용 만료 시각(Time.time). [0]=팀A, [1]=팀B
        private readonly float[] m_MoveUntil = new float[2];
        private readonly float[] m_ProcUntil = new float[2];
        private readonly float[] m_OrderUntil = new float[2];
        private readonly float[] m_WeatherUntil = new float[2];
        private readonly float[] m_FogUntil = new float[2];
        private readonly float[] m_ImmuneUntil = new float[2];
        private float m_NextWeatherTick;   // 날씨 피해 판정 주기(미끄러짐)
        private float m_NextWindTick;      // 바람 붕괴 주기 — 미끄러짐보다 훨씬 느리게 돈다
        private GameObject m_VisualRoot;
        private readonly System.Collections.Generic.Dictionary<uint, GameObject> m_Visuals = new();

        private static ItemNetwork s_Instance;

        private void Awake()
        {
            m_Loop = GetComponent<GameLoopManager>();
            m_Grid = GetComponent<GridManager>();
            m_Net = GetComponent<GridNetwork>();
            s_Instance = this;
        }

        public override void OnNetworkSpawn()
        {
            m_VisualRoot = new GameObject("~ItemVisuals");
            m_Items.OnListChanged += OnItemsChanged;
            m_LocalHeldDirty = true;   // 늦참: 스폰 시점 리스트로 재검사
            if (IsServer)
            {
                var definitions = m_Definitions = CompetitiveItemDefinitionCatalog.CreateDefault();
                m_Director = DefaultCompetitiveItemFactory.CreateSpawnDirector(this, new UnityRandom(), definitions);
                var effects = DefaultCompetitiveItemFactory.CreateEffects(
                    definitions, this, this, this, this, this, this, this, this);
                m_UseService = new CompetitiveItemUseService(definitions, effects, this);
            }
            RebuildVisuals();
        }

        public override void OnNetworkDespawn()
        {
            m_Items.OnListChanged -= OnItemsChanged;
            if (m_VisualRoot != null) Destroy(m_VisualRoot);
            if (m_EnemyZoneRig != null) Destroy(m_EnemyZoneRig.gameObject);
            foreach (var kv in m_HeldBubbles) if (kv.Value != null) Destroy(kv.Value.gameObject);
            m_HeldBubbles.Clear();
            if (s_Instance == this) s_Instance = null;
        }

        // ── 소지 아이템 버블: 복제 목록(Held/Holder)만 보고 각 클라가 스스로 관리 ──
        private readonly System.Collections.Generic.Dictionary<ulong, HeldItemBubble> m_HeldBubbles = new();
        private static readonly System.Collections.Generic.Dictionary<ulong, CompetitiveItemKind> s_WantBubbles = new();
        private static readonly System.Collections.Generic.List<ulong> s_BubbleGone = new();

        private void SyncHeldBubbles()
        {
            s_WantBubbles.Clear();
            for (int i = 0; i < m_Items.Count; i++)   // NetworkList foreach는 열거자 박싱 — 인덱스 순회
            {
                var e = m_Items[i];
                if (e.Held) s_WantBubbles[e.Holder] = (CompetitiveItemKind)e.Kind;
            }

            s_BubbleGone.Clear();
            foreach (var kv in m_HeldBubbles)
            {
                if (kv.Value != null && s_WantBubbles.TryGetValue(kv.Key, out var kind)) { kv.Value.SetKind(kind); continue; }
                if (kv.Value != null) Destroy(kv.Value.gameObject);
                s_BubbleGone.Add(kv.Key);
            }
            foreach (var id in s_BubbleGone) m_HeldBubbles.Remove(id);

            foreach (var kv in s_WantBubbles)
            {
                if (m_HeldBubbles.ContainsKey(kv.Key)) continue;
                var t = FindPlayerTransform(kv.Key);
                if (t == null) continue;
                m_HeldBubbles[kv.Key] = HeldItemBubble.Create(t, kv.Value);
            }
        }

        // 클라에서 원격 clientId로 GetPlayerNetworkObject를 부르면 예외라, 관측 목록에서 직접 찾는다.
        private static Transform FindPlayerTransform(ulong clientId)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || nm.SpawnManager == null) return null;
            foreach (var no in nm.SpawnManager.SpawnedObjectsList)
                if (no.IsPlayerObject && no.OwnerClientId == clientId) return no.transform;
            return null;
        }

        // ── 조회/조작 API ─────────────────────────────────────────
        /// <summary>씬의 아이템 호스트(없으면 null — 협동 모드 등).</summary>
        public static ItemNetwork Instance => s_Instance;

        /// <summary>내가 아이템을 들고 있는가(E 사용 가능).</summary>
        public bool LocalHasItem => LocalHeldName() != "";

        // 로컬 보유 아이템 캐시 — HUD가 매 프레임 묻는다. NetworkList 전수 스캔(+foreach 박싱)을
        // 리스트가 실제로 바뀐 뒤 1회로 줄인다. NGO의 초기 전체 동기화는 이벤트가 안 오므로 초기값 dirty.
        private bool m_LocalHeldDirty = true;
        private string m_LocalHeldName = "";
        private bool m_LocalHoldsCannon;

        private void RefreshLocalHeld()
        {
            if (!m_LocalHeldDirty) return;
            m_LocalHeldDirty = false;
            m_LocalHeldName = "";
            m_LocalHoldsCannon = false;
            ulong me = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : ulong.MaxValue;
            for (int i = 0; i < m_Items.Count; i++)
            {
                var e = m_Items[i];
                if (!e.Held || e.Holder != me) continue;
                m_LocalHeldName = KindName((CompetitiveItemKind)e.Kind);
                m_LocalHoldsCannon = (CompetitiveItemKind)e.Kind == CompetitiveItemKind.Cannon;
                break;
            }
        }

        /// <summary>내가 든 게 대포인가 — 대포만 '꾹 눌렀다 떼기'로 발사한다(기획서).</summary>
        public bool LocalHoldsCannon
        {
            get { RefreshLocalHeld(); return m_LocalHoldsCannon; }
        }

        /// <summary>[기획] 아이템을 든 채로 E — 입력은 PlayerCarry가 공정(도구)과 갈라서 호출한다.</summary>
        public void RequestUseHeld()
        {
            if (LocalHasItem) UseHeldRpc(Vector3.forward, 1f);
        }

        /// <summary>대포 전용 — 겨눈 방향(수평)과 충전량(0~1)을 실어 보낸다. 명중 판정은 서버가 한다.</summary>
        public void RequestUseHeldAimed(Vector3 aimDir, float charge01)
        {
            if (LocalHasItem) UseHeldRpc(aimDir, Mathf.Clamp01(charge01));
        }

        /// <summary>내(로컬 플레이어)가 들고 있는 아이템 이름. 없으면 "".</summary>
        public string LocalHeldName()
        {
            RefreshLocalHeld();
            return m_LocalHeldName;
        }

        /// <summary>로컬 플레이어 팀의 이동속도 배율(PlayerMovement가 곱함). 협동/미배정 = 1.</summary>
        public static float LocalMoveMultiplier() => LocalEffects().MoveMul;

        /// <summary>로컬 플레이어 팀의 공정 진행 속도 배율(PlayerCarry가 곱함). 협동/미배정 = 1.</summary>
        public static float LocalProcessMultiplier() => LocalEffects().ProcessMul;

        /// <summary>내 팀이 주문 해킹당했는가(주문 HUD 안내용).</summary>
        public static bool LocalOrderBlocked() => LocalEffects().OrderBlocked;

        /// <summary>내 팀 주문 차단이 풀릴 때까지 남은 초(안 걸렸으면 0) — 주문 HUD 카운트다운용.</summary>
        public static float LocalOrderBlockedRemaining()
        {
            var fx = LocalEffects();
            return fx.OrderBlocked ? Mathf.Max(0f, fx.OrderUntil - NetNow()) : 0f;
        }

        /// <summary>팀 진영 월드 경계(2vs2). 비대전이면 null — 승리 시네마틱 카메라 프레이밍용.</summary>
        public static Bounds? TeamZoneBounds(int team)
        {
            if (s_Instance == null || s_Instance.m_Grid == null
                || s_Instance.m_Loop == null || !s_Instance.m_Loop.IsVersus
                || team < 0 || team > 1) return null;
            return s_Instance.ZoneWorldBounds(team);
        }

        /// <summary>대포 조준 연출용 — 상대 진영 중심(월드). 비대전/미배정이면 fallback.</summary>
        public static Vector3 EnemyZoneAimPoint(Vector3 fallback)
        {
            if (s_Instance == null || s_Instance.m_Loop == null || !s_Instance.m_Loop.IsVersus
                || s_Instance.m_Grid == null) return fallback;
            int my = s_Instance.m_Loop.LocalTeam;
            if (my < 0) return fallback;
            var b = s_Instance.ZoneWorldBounds(1 - my);
            return new Vector3(b.center.x, b.min.y + 1.5f, b.center.z);
        }

        /// <summary>HUD 버프 아이콘용 상태 항목 — 어떤 아이템 효과가 몇 초/총 몇 초 남았는지.</summary>
        public struct LocalStatus
        {
            public CompetitiveItemKind Kind;
            public float Remaining;
            public float Total;
        }

        private static CompetitiveItemDefinitionCatalog s_ClientDefs;   // 총 지속시간 조회용(클라 로컬)

        /// <summary>내 팀에 걸린 효과 목록을 채운다(HUD 버프 바 — 매 프레임 호출해 카운트다운).</summary>
        public static void GetLocalStatuses(System.Collections.Generic.List<LocalStatus> list)
        {
            list.Clear();
            var fx = LocalEffects();
            float now = NetNow();
            s_ClientDefs ??= CompetitiveItemDefinitionCatalog.CreateDefault();

            void Add(CompetitiveItemKind kind, float until)
            {
                float rem = until - now;
                if (rem <= 0f) return;
                list.Add(new LocalStatus
                {
                    Kind = kind, Remaining = rem,
                    Total = Mathf.Max(0.01f, s_ClientDefs.Get(kind).EffectDurationSeconds),
                });
            }

            switch ((WeatherKind)fx.Weather)
            {
                case WeatherKind.Rain: Add(CompetitiveItemKind.Rain, fx.WeatherUntil); break;
                case WeatherKind.Snow: Add(CompetitiveItemKind.Snow, fx.WeatherUntil); break;
                case WeatherKind.StrongWind: Add(CompetitiveItemKind.StrongWind, fx.WeatherUntil); break;
                case WeatherKind.Typhoon: Add(CompetitiveItemKind.Typhoon, fx.WeatherUntil); break;
            }
            if (fx.Fog) Add(CompetitiveItemKind.Fog, fx.FogUntil);
            if (fx.WeatherImmune) Add(CompetitiveItemKind.Umbrella, fx.ImmuneUntil);
            if (fx.MoveMul < 1f) Add(CompetitiveItemKind.MovementSlow, fx.MoveUntil);
            else if (fx.MoveMul > 1f) Add(CompetitiveItemKind.MovementBoost, fx.MoveUntil);
            if (fx.ProcessMul < 1f) Add(CompetitiveItemKind.ProcessSlow, fx.ProcUntil);
            else if (fx.ProcessMul > 1f) Add(CompetitiveItemKind.ProcessBoost, fx.ProcUntil);
            if (fx.OrderBlocked) Add(CompetitiveItemKind.OrderHack, fx.OrderUntil);
        }

        /// <summary>내 팀에 걸린 상태 한 줄 요약 + 남은 초(없으면 ""). HUD 표시용 — 매 프레임 갱신되어 카운트다운.</summary>
        public static string LocalStatusLine()
        {
            var fx = LocalEffects();
            float now = NetNow();
            string T(float until) { int s = Mathf.CeilToInt(until - now); return s > 0 ? $" {s}초" : ""; }
            var parts = new System.Collections.Generic.List<string>();
            if (fx.Weather != (int)WeatherKind.Sunny) parts.Add(WeatherName((WeatherKind)fx.Weather) + T(fx.WeatherUntil));
            if (fx.Fog) parts.Add("안개" + T(fx.FogUntil));
            if (fx.WeatherImmune) parts.Add("우산(날씨 면역)" + T(fx.ImmuneUntil));
            if (fx.MoveMul < 1f) parts.Add("이동 느림" + T(fx.MoveUntil)); else if (fx.MoveMul > 1f) parts.Add("이동 빠름" + T(fx.MoveUntil));
            if (fx.ProcessMul < 1f) parts.Add("공정 느림" + T(fx.ProcUntil)); else if (fx.ProcessMul > 1f) parts.Add("공정 빠름" + T(fx.ProcUntil));
            if (fx.OrderBlocked) parts.Add("주문 차단" + T(fx.OrderUntil));
            return parts.Count == 0 ? "" : string.Join(" · ", parts);
        }

        private static string WeatherName(WeatherKind w) => w switch
        {
            WeatherKind.Rain => "비",
            WeatherKind.Snow => "눈",
            WeatherKind.StrongWind => "강풍",
            WeatherKind.Typhoon => "태풍",
            _ => w.ToString(),
        };

        private static TeamEffects LocalEffects()
        {
            if (s_Instance == null || s_Instance.m_Loop == null || !s_Instance.m_Loop.IsVersus) return TeamEffects.None;
            int team = s_Instance.m_Loop.LocalTeam;
            return team < 0 ? TeamEffects.None : s_Instance.Fx(team);
        }

        private TeamEffects Fx(int team) => team == 1 ? m_FxB.Value : m_FxA.Value;

        private void SetFx(int team, TeamEffects v)
        {
            if (team == 1) m_FxB.Value = v; else m_FxA.Value = v;
        }

        /// <summary>서버 검사용: 해당 팀이 지금 재료를 주문할 수 있는가(MaterialDepot이 호출).</summary>
        public bool IsOrderBlocked(int team) => team >= 0 && Fx(team).OrderBlocked;

        /// <summary>서버 검사용: 해당 팀의 주문 차단 잔여 초(안 걸렸으면 0) — 거절 안내에 남은 시간을 싣는다.</summary>
        public float OrderBlockRemaining(int team)
        {
            if (team < 0) return 0f;
            var fx = Fx(team);
            return fx.OrderBlocked ? Mathf.Max(0f, fx.OrderUntil - NetNow()) : 0f;
        }

        // ── 게이트웨이 (SpawnDirector → 월드) — 서버 전용 ─────────
        string ICompetitiveItemSpawnGateway.Spawn(CompetitiveItemSpawnRequest request)
        {
            uint id = ++m_NextId;
            m_Items.Add(new ItemEntry
            {
                Id = id,
                Kind = (int)request.Kind,
                Pos = PickSpawnPos(request.BeneficiaryTeamId),
                Held = false,
                Holder = 0,
            });
            return id.ToString();
        }

        void ICompetitiveItemSpawnGateway.Despawn(string itemInstanceId, ItemDespawnReason reason)
        {
            if (!uint.TryParse(itemInstanceId, out uint id)) return;
            for (int i = m_Items.Count - 1; i >= 0; i--)
                if (m_Items[i].Id == id && !m_Items[i].Held)   // 소지 중이면 소멸 대상 아님(획득=소비 통지됨)
                    m_Items.RemoveAt(i);
        }

        // 스폰 위치: 보상팀 있으면 그 팀 구역, 아니면 양 구역 중 랜덤 — 그리드 위 임의 셀(위에서 떨어뜨림)
        private Vector3 PickSpawnPos(string beneficiaryTeamId)
        {
            var zone = m_Grid.ZoneSize;
            float u = GridContract.Unit;
            int team = beneficiaryTeamId == kTeamB ? 1 : beneficiaryTeamId == kTeamA ? 0 : Random.Range(0, 2);
            Vector3 baseW = GridCoordinates.CellToWorld(Vector3Int.zero);

            // 맵마다 지면이 그리드 범위를 다 못 덮는다(롯데월드 B존 동쪽 끝 = 호수 위 허공).
            // 위에서 레이캐스트로 실제 지면에 붙이고, 지면이 1m 넘게 꺼진 곳(물속 등)은 재추첨.
            for (int attempt = 0; attempt < 8; attempt++)
            {
                float x = (team * zone.x + Random.Range(0.5f, zone.x - 0.5f)) * u;
                float z = Random.Range(0.5f, zone.z - 0.5f) * u;
                var probe = new Vector3(baseW.x + x, baseW.y + 30f, baseW.z + z);
                if (Physics.Raycast(probe, Vector3.down, out var hit, 60f)
                    && hit.point.y > baseW.y - 1f)
                    return new Vector3(probe.x, hit.point.y + 0.45f, probe.z);
            }
            // 폴백: 존 중앙(모든 맵에서 지면 보장)
            return new Vector3(baseW.x + (team + 0.5f) * zone.x * u, baseW.y + 0.45f,
                               baseW.z + zone.z * 0.5f * u);
        }

        // ── 서버 루프: 규칙 진행 + 완성도 보고 + 근접 획득 + 배율 만료 ──
        private void Update()
        {
            if (!IsSpawned) return;

            SyncLocalWeatherFx();   // 연출은 모든 클라가 각자(서버 게이트보다 앞)
            SyncHeldBubbles();      // 마리오카트식 소지 아이템 버블(모든 클라)

            if (!IsServer || m_Loop == null || !m_Loop.IsVersus) return;

            if (m_Loop.IsBuilding && m_Director != null)
            {
                m_Director.Tick(Time.deltaTime);
                if (m_Net != null)
                {
                    m_Director.ReportCompletion(kTeamA, m_Net.ScoreFor(0).Percent);
                    m_Director.ReportCompletion(kTeamB, m_Net.ScoreFor(1).Percent);
                }
            }

            // 근접 자동 획득(빈손만) — 거리 비교는 제곱거리로(sqrt 제거), 위치 조회는 루프 밖에서 1회
            const float pickupRangeSq = kPickupRange * kPickupRange;
            foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
            {
                var po = client.PlayerObject;
                if (po == null || HasHeld(client.ClientId)) continue;
                Vector3 playerPos = po.transform.position;
                for (int i = 0; i < m_Items.Count; i++)
                {
                    var e = m_Items[i];
                    if (e.Held || (playerPos - e.Pos).sqrMagnitude > pickupRangeSq) continue;
                    e.Held = true; e.Holder = client.ClientId;
                    m_Items[i] = e;
                    m_Director?.NotifyConsumed(e.Id.ToString());   // 소지 중엔 60초 소멸 제외
                    break;
                }
            }

            ExpireEffects();
            TickWeatherDamage();
        }

        // ── 날씨: 서버가 피해를 판정하고, 보이는 건 각 클라가 그린다 ──
        // 비/눈 = 미끄러짐(플레이어가 훅 밀림), 강풍/태풍 = 미고정 블록이 바람에 무너짐.
        // 우산(면역)이 걸린 팀은 피해를 건너뛴다.
        //
        // 미끄러짐과 바람 붕괴는 주기가 다르다. 미끄러짐은 자주 와야 '계속 미끄럽다'는 느낌이 나지만,
        // 붕괴는 같은 1초 주기로 돌리면 60초짜리 강풍 한 방에 수십 개가 쓸려나가 게임이 터진다.
        private const float kSlipTickSeconds = 1f;
        private const float kWindTickSeconds = 6f;   // 강풍 60초 = 약 10개. 태풍은 한 번에 2개씩.
        private void TickWeatherDamage()
        {
            bool slipTick = Time.time >= m_NextWeatherTick;
            bool windTick = Time.time >= m_NextWindTick;
            if (!slipTick && !windTick) return;
            if (slipTick) m_NextWeatherTick = Time.time + kSlipTickSeconds;
            if (windTick) m_NextWindTick = Time.time + kWindTickSeconds;

            for (int team = 0; team < 2; team++)
            {
                var fx = Fx(team);
                var weather = (WeatherKind)fx.Weather;
                if (weather == WeatherKind.Sunny || fx.WeatherImmune) continue;

                bool slippery = weather is WeatherKind.Rain or WeatherKind.Snow or WeatherKind.Typhoon;
                bool windy = weather is WeatherKind.StrongWind or WeatherKind.Typhoon;

                if (slipTick && slippery) SlipTeam(team, weather == WeatherKind.Typhoon ? 1f : 0.6f);
                if (windTick && windy && m_Net != null)
                {
                    int blown = m_Net.ServerWindCollapse(team, weather == WeatherKind.Typhoon ? 2 : 1);
                    if (blown > 0) Debug.Log($"[Weather] 바람에 무너짐 → 팀{TeamId(team)} {blown}개");
                }
            }
        }

        // 해당 팀 플레이어들을 무작위 방향으로 살짝 밀어 '미끄러짐'을 만든다(소유 클라가 실제로 밀림).
        private void SlipTeam(int team, float strength)
        {
            if (NetworkManager.Singleton == null) return;
            foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
            {
                if (m_Loop == null || m_Loop.GetTeam(client.ClientId) != team) continue;
                var dir = Random.insideUnitCircle.normalized;
                SlipRpc(new Vector3(dir.x, 0f, dir.y) * (3.5f * strength),
                        RpcTarget.Single(client.ClientId, RpcTargetUse.Temp));
            }
        }

        [Rpc(SendTo.SpecifiedInParams)]
        private void SlipRpc(Vector3 impulse, RpcParams rpc = default)
        {
            var po = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClient.PlayerObject : null;
            if (po == null) return;
            var rb = po.GetComponent<Rigidbody>();
            if (rb != null && !rb.isKinematic) rb.AddForce(impulse, ForceMode.VelocityChange);
            // 날씨 틱마다(1초) 밀리므로 문구는 스로틀 — 매초 도배 방지
            if (Time.time >= m_NextSlipToast)
            {
                m_NextSlipToast = Time.time + 3f;
                GridJuice.WorldToast(po.transform.position + Vector3.up * 2.2f, "미끄덩~", new Color(0.15f, 0.55f, 1f));
            }
        }
        private float m_NextSlipToast;   // 로컬 전용(수신 클라 기준)

        // 전원 화면에 알림(스크린 배너 — 시야 밖이어도 보임). 입장별로:
        // 시전자 = 금색 "사용!" / 피격팀 = 빨강 배너+비네트+흔들림 / 버프받은 아군 = 초록 / 시전자 팀원(공격) = 파랑 정보.
        [Rpc(SendTo.Everyone)]
        private void ItemUsedNoticeRpc(int kind, int casterTeam, int targetTeam, bool buff, ulong casterId)
        {
            if (m_Loop == null) return;
            var k = (CompetitiveItemKind)kind;
            string name = KindName(k);
            var nm = NetworkManager.Singleton;
            bool isCaster = nm != null && nm.LocalClientId == casterId;
            int my = m_Loop.LocalTeam;

            if (isCaster)
            {
                ItemScreenFx.Banner(k, $"{name} 사용!", new Color(0.85f, 0.62f, 0.05f));
                return;
            }
            if (my == targetTeam && !buff)          // 상대 공격에 당함
            {
                ItemScreenFx.Banner(k, $"상대가 {name} 사용!", new Color(0.82f, 0.16f, 0.12f), shake: true);
                ItemScreenFx.Flash(new Color(1f, 0.15f, 0.1f), 0.55f);
                GridJuice.FovPunch(Camera.main, -2f);
                return;
            }
            if (my == targetTeam && buff)           // 아군이 버프를 걸어줌
            {
                ItemScreenFx.Banner(k, $"아군이 {name} 사용!", new Color(0.15f, 0.62f, 0.25f));
                ItemScreenFx.Flash(new Color(0.3f, 1f, 0.4f), 0.3f);
                return;
            }
            if (my == casterTeam)                   // 팀원이 상대를 공격(나는 구경)
                ItemScreenFx.Banner(k, $"아군이 상대에게 {name} 사용!", new Color(0.16f, 0.42f, 0.75f));
        }

        // 내 팀 상태를 로컬 연출(날씨 파티클·안개)에 반영 — 값이 바뀔 때만.
        private WeatherKind m_ShownWeather = WeatherKind.Sunny;
        private bool m_ShownFog;
        private void SyncLocalWeatherFx()
        {
            var fx = LocalEffects();
            var weather = (WeatherKind)fx.Weather;
            if (weather != m_ShownWeather || fx.Fog != m_ShownFog)
            {
                m_ShownWeather = weather; m_ShownFog = fx.Fog;
                var host = TeamWeatherFx.Get();
                // 2vs2 아이템 날씨는 내 진영에만 내리게 존 영역 전달(협동/미배정 = 맵 전체)
                int my = m_Loop != null ? m_Loop.LocalTeam : -1;
                host.SetTemporaryArea(m_Loop != null && m_Loop.IsVersus && my >= 0 && m_Grid != null
                    ? ZoneWorldBounds(my) : (Bounds?)null);
                host.Set(weather, fx.Fog);
            }
            SyncZoneFogFx();
            SyncEnemyZoneWeather();
        }

        // 상대 팀에 걸린 '날씨'를 내 화면에도 상대 진영 위에만 표시 — 시전자/아군이 적용을 본다.
        // (당한 팀 본인 화면은 TeamWeatherFx가 맵 전체로 그림 — 안개 구름과 같은 역할 분담)
        private Weather3DVfxRig m_EnemyZoneRig;
        private WeatherKind m_ShownEnemyWeather = WeatherKind.Sunny;
        private void SyncEnemyZoneWeather()
        {
            if (m_Loop == null || !m_Loop.IsVersus || m_Grid == null) return;
            int my = m_Loop.LocalTeam;
            if (my < 0) return;
            var weather = (WeatherKind)Fx(1 - my).Weather;
            if (weather == m_ShownEnemyWeather) return;
            m_ShownEnemyWeather = weather;
            if (m_EnemyZoneRig == null)
            {
                var prefab = Resources.Load<Weather3DVfxRig>("UI_NEW/Weather/3D/Weather3DVfxRig");
                if (prefab == null) return;
                m_EnemyZoneRig = Instantiate(prefab);
                m_EnemyZoneRig.name = "~EnemyZoneWeather";
            }
            m_EnemyZoneRig.CoverArea(ZoneWorldBounds(1 - my));
            m_EnemyZoneRig.SetWeather(weather);
        }

        // 안개 걸린 '다른' 팀 구역 위에 구름 표시 — 시전자/아군도 적용 여부를 본다.
        // 내 팀 안개는 카메라 포그(TeamWeatherFx)가 담당하므로 구름 생략.
        private void SyncZoneFogFx()
        {
            if (m_Loop == null || !m_Loop.IsVersus || m_Grid == null) return;
            int my = m_Loop.LocalTeam;
            for (int team = 0; team < 2; team++)
            {
                bool want = Fx(team).Fog && team != my;
                if (want) ZoneFogFx.Show(team, ZoneWorldBounds(team));
                else ZoneFogFx.Hide(team);
            }
        }

        private Bounds ZoneWorldBounds(int team)
        {
            var zone = m_Grid.ZoneSize;
            float u = GridContract.Unit;
            Vector3 baseW = GridCoordinates.CellToWorld(Vector3Int.zero);
            var min = new Vector3(baseW.x + team * zone.x * u, baseW.y, baseW.z);
            var size = new Vector3(zone.x * u, zone.y * u * 0.5f, zone.z * u);   // 높이 절반 — 구름은 낮게 깔림
            return new Bounds(min + size * 0.5f, size);
        }

        // 지속 효과 만료(서버) — 시간이 지난 것만 기본값으로 되돌린다.
        private void ExpireEffects()
        {
            for (int team = 0; team < 2; team++)
            {
                var fx = Fx(team);
                bool changed = false;
                if (fx.MoveMul != 1f && Time.time >= m_MoveUntil[team]) { fx.MoveMul = 1f; changed = true; }
                if (fx.ProcessMul != 1f && Time.time >= m_ProcUntil[team]) { fx.ProcessMul = 1f; changed = true; }
                if (fx.OrderBlocked && Time.time >= m_OrderUntil[team]) { fx.OrderBlocked = false; changed = true; }
                if (fx.Weather != (int)SeoulZikimi.Weather.WeatherKind.Sunny && Time.time >= m_WeatherUntil[team])
                { fx.Weather = (int)SeoulZikimi.Weather.WeatherKind.Sunny; changed = true; }
                if (fx.Fog && Time.time >= m_FogUntil[team]) { fx.Fog = false; changed = true; }
                if (fx.WeatherImmune && Time.time >= m_ImmuneUntil[team]) { fx.WeatherImmune = false; changed = true; }
                if (changed) SetFx(team, fx);
            }
        }

        /// <summary>새 라운드(재시작) — 아이템·배율·규칙 초기화. GameLoopManager.Restart가 호출(서버).</summary>
        public void ServerReset()
        {
            if (!IsServer) return;
            for (int i = m_Items.Count - 1; i >= 0; i--) m_Items.RemoveAt(i);
            m_Director?.Reset();
            m_FxA.Value = TeamEffects.None; m_FxB.Value = TeamEffects.None;
        }

        private bool HasHeld(ulong clientId)
        {
            for (int i = 0; i < m_Items.Count; i++)
                if (m_Items[i].Held && m_Items[i].Holder == clientId) return true;
            return false;
        }

        // ── 사용 ─────────────────────────────────────────────────
        [Rpc(SendTo.Server)]
        private void UseHeldRpc(Vector3 aimDir, float charge01, RpcParams rpc = default)
        {
            ulong sender = rpc.Receive.SenderClientId;
            for (int i = m_Items.Count - 1; i >= 0; i--)
            {
                var e = m_Items[i];
                if (!e.Held || e.Holder != sender) continue;
                int team = m_Loop.GetTeam(sender);
                if (team < 0) return;                      // 팀 미배정이면 소비하지 않는다
                m_Items.RemoveAt(i);
                // 대포 발사 위치 = 시전자(포탄 궤적 연출용) — Use 안에서 ServerCannonDestroy가 읽는다
                if ((CompetitiveItemKind)e.Kind == CompetitiveItemKind.Cannon && m_Net != null)
                {
                    m_Net.ServerCannonSource = HolderPos(sender, transform.position);
                    m_Net.ServerCannonAimDir = aimDir;
                    m_Net.ServerCannonCharge = Mathf.Clamp01(charge01);
                }
                m_UseService?.Use((CompetitiveItemKind)e.Kind, sender.ToString(), TeamId(team));
                // 대상 팀 전원에게 알림 — 공격이면 당한 팀에 빨강, 버프면 아군에 초록('누구한테 쓴 건지' 가시화)
                var def = m_Definitions?.Get((CompetitiveItemKind)e.Kind);
                if (def != null)
                {
                    bool buff = def.TargetSide == ItemTargetSide.Ally;
                    ItemUsedNoticeRpc(e.Kind, team, buff ? team : 1 - team, buff, sender);
                }
                return;
            }
        }

        // ── 프레임워크 효과 어댑터 ────────────────────────────────
        // 대상 팀 결정과 효과 실행은 CompetitiveItemUseService가 한다. 이 클래스는 각 효과 인터페이스의
        // '유니티 쪽 구현'만 제공한다(순수 도메인 ↔ 네트워크/월드 경계).
        private static int TeamIndex(string teamId) => teamId == kTeamB ? 1 : teamId == kTeamA ? 0 : -1;
        private static string TeamId(int index) => index == 1 ? kTeamB : kTeamA;

        string IOpponentTeamResolver.GetOpponentTeamId(string sourceTeamId)
            => sourceTeamId == kTeamB ? kTeamA : kTeamB;

        void IUnfixedConstructionTarget.CollapseAllUnfixed(string teamId)
        {
            int team = TeamIndex(teamId);
            if (team < 0 || m_Net == null) return;
            int n = m_Net.ServerEarthquake(team);
            Debug.Log($"[Item] 지진 → 팀{teamId}: 미고정 블록 {n}개 붕괴");
        }

        void ICompletedConstructionTarget.DestroyRandomCompleted(string teamId)
        {
            int team = TeamIndex(teamId);
            if (team < 0 || m_Net == null) return;
            bool hit = m_Net.ServerCannonDestroy(team);
            Debug.Log(hit ? $"[Item] 대포 → 팀{teamId}: 조준한 블록 파괴(위에 얹힌 것은 연쇄 붕괴)"
                          : $"[Item] 대포 → 팀{teamId}: 빗나감(아군 진영에 맞았거나 허공)");
        }

        void ITeamMovementModifierTarget.ApplyMovementSpeedMultiplier(string teamId, float multiplier, float durationSeconds)
        {
            int team = TeamIndex(teamId);
            if (team < 0) return;
            var fx = Fx(team); fx.MoveMul = multiplier; fx.MoveUntil = NetNow() + durationSeconds; SetFx(team, fx);
            m_MoveUntil[team] = Time.time + durationSeconds;
        }

        void ITeamProcessModifierTarget.ApplyProcessSpeedMultiplier(string teamId, float multiplier, float durationSeconds)
        {
            int team = TeamIndex(teamId);
            if (team < 0) return;
            var fx = Fx(team); fx.ProcessMul = multiplier; fx.ProcUntil = NetNow() + durationSeconds; SetFx(team, fx);
            m_ProcUntil[team] = Time.time + durationSeconds;
        }

        void ITeamOrderLockTarget.LockNewOrders(string teamId, float durationSeconds)
        {
            int team = TeamIndex(teamId);
            if (team < 0) return;
            var fx = Fx(team); fx.OrderBlocked = true; fx.OrderUntil = NetNow() + durationSeconds; SetFx(team, fx);
            m_OrderUntil[team] = Time.time + durationSeconds;
        }

        void ITeamFogTarget.ApplyFog(string teamId, float durationSeconds)
        {
            int team = TeamIndex(teamId);
            if (team < 0) return;
            var fx = Fx(team); fx.Fog = true; fx.FogUntil = NetNow() + durationSeconds; SetFx(team, fx);
            m_FogUntil[team] = Time.time + durationSeconds;
            Debug.Log($"[Item] 안개 → 팀{teamId} {durationSeconds}초");
        }

        void ITemporaryTeamWeatherTarget.ApplyTemporaryWeather(string teamId, SeoulZikimi.Weather.WeatherKind weather, float durationSeconds)
        {
            int team = TeamIndex(teamId);
            if (team < 0) return;
            // 이미 날씨가 걸려 있으면 새 날씨로 교체 + 타이머 초기화(기획서 규칙)
            var fx = Fx(team); fx.Weather = (int)weather; fx.WeatherUntil = NetNow() + durationSeconds; SetFx(team, fx);
            m_WeatherUntil[team] = Time.time + durationSeconds;
            Debug.Log($"[Item] 날씨 {weather} → 팀{teamId} {durationSeconds}초");
        }

        void ITeamWeatherImmunityTarget.ApplyWeatherImmunity(string teamId, float durationSeconds)
        {
            int team = TeamIndex(teamId);
            if (team < 0) return;
            var fx = Fx(team); fx.WeatherImmune = true; fx.ImmuneUntil = NetNow() + durationSeconds; SetFx(team, fx);
            m_ImmuneUntil[team] = Time.time + durationSeconds;
            Debug.Log($"[Item] 우산(날씨 면역) → 팀{teamId} {durationSeconds}초");
        }

        // ── 비주얼(전 클라 로컬) — 종류별 색 구슬 + 이벤트별 FX ─────
        // 증분 갱신: 리스트 변화 종류로 등장/획득/사용/소멸을 구분해야 FX가 맞는 순간에 터진다.
        private void OnItemsChanged(NetworkListEvent<ItemEntry> e)
        {
            m_LocalHeldDirty = true;   // 어떤 변경이든 로컬 보유 캐시 재검사
            switch (e.Type)
            {
                case NetworkListEvent<ItemEntry>.EventType.Add:
                    AddVisual(e.Value);
                    ItemFx.Spawned(e.Value.Pos, KindColor((CompetitiveItemKind)e.Value.Kind));
                    break;

                case NetworkListEvent<ItemEntry>.EventType.Value:
                    if (e.Value.Held && !e.PreviousValue.Held)   // 누군가 주움
                    {
                        RemoveVisual(e.Value.Id);
                        ItemFx.PickedUp(e.PreviousValue.Pos, KindColor((CompetitiveItemKind)e.Value.Kind));
                    }
                    break;

                case NetworkListEvent<ItemEntry>.EventType.Remove:
                case NetworkListEvent<ItemEntry>.EventType.RemoveAt:
                    RemoveVisual(e.Value.Id);
                    var col = KindColor((CompetitiveItemKind)e.Value.Kind);
                    if (e.Value.Held) ItemFx.Used(HolderPos(e.Value.Holder, e.Value.Pos), col,
                        KindName((CompetitiveItemKind)e.Value.Kind),
                        (CompetitiveItemKind)e.Value.Kind);   // 사용 — 팡 + "○○ 사용!" 문구 + 아이콘 팝
                    else ItemFx.Expired(e.Value.Pos, col);                                        // 미사용 소멸
                    break;

                default:
                    RebuildVisuals();   // Clear 등(라운드 리셋) — 통째로 맞춤
                    break;
            }
        }

        private static Vector3 HolderPos(ulong clientId, Vector3 fallback)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || nm.SpawnManager == null) return fallback;
            var po = nm.SpawnManager.GetPlayerNetworkObject(clientId);
            return po != null ? po.transform.position + Vector3.up * 0.6f : fallback;
        }

        private void RemoveVisual(uint id)
        {
            if (m_Visuals.TryGetValue(id, out var go) && go != null) Destroy(go);
            m_Visuals.Remove(id);
        }

        private void AddVisual(ItemEntry e)
        {
            if (m_VisualRoot == null || e.Held || m_Visuals.ContainsKey(e.Id)) return;
            var go = ItemFx.MakeItemBox(e.Pos, KindColor((CompetitiveItemKind)e.Kind));   // 무지개 '?' 상자
            go.name = $"~Item_{e.Id}";
            go.transform.SetParent(m_VisualRoot.transform, true);
            m_Visuals[e.Id] = go;
        }

        private void RebuildVisuals()
        {
            foreach (var kv in m_Visuals) if (kv.Value != null) Destroy(kv.Value);
            m_Visuals.Clear();
            for (int i = 0; i < m_Items.Count; i++) AddVisual(m_Items[i]);
        }

        /// <summary>종류별 대표색(비주얼·FX 공용).</summary>
        public static Color KindColor(CompetitiveItemKind k) => k switch
        {
            CompetitiveItemKind.Earthquake => new Color(0.55f, 0.35f, 0.2f),
            CompetitiveItemKind.Rain => new Color(0.3f, 0.5f, 0.95f),
            CompetitiveItemKind.Snow => Color.white,
            CompetitiveItemKind.StrongWind => new Color(0.7f, 0.9f, 0.85f),
            CompetitiveItemKind.Typhoon => new Color(0.25f, 0.3f, 0.5f),
            CompetitiveItemKind.Fog => new Color(0.75f, 0.75f, 0.78f),
            CompetitiveItemKind.MovementSlow => new Color(0.85f, 0.3f, 0.3f),
            CompetitiveItemKind.ProcessSlow => new Color(0.9f, 0.45f, 0.2f),
            CompetitiveItemKind.OrderHack => new Color(0.5f, 0.2f, 0.6f),
            CompetitiveItemKind.Umbrella => new Color(1f, 0.85f, 0.3f),
            CompetitiveItemKind.MovementBoost => new Color(0.3f, 0.85f, 0.4f),
            CompetitiveItemKind.ProcessBoost => new Color(0.2f, 0.7f, 0.9f),
            CompetitiveItemKind.Cannon => new Color(0.25f, 0.25f, 0.3f),
            _ => Color.gray,
        };

        /// <summary>아군에게 이로운 효과인가 — HUD 테두리색(초록/빨강) 구분용.</summary>
        public static bool IsBuff(CompetitiveItemKind k)
            => k is CompetitiveItemKind.Umbrella or CompetitiveItemKind.MovementBoost or CompetitiveItemKind.ProcessBoost;

        public static string KindName(CompetitiveItemKind k) => k switch
        {
            CompetitiveItemKind.Earthquake => "지진",
            CompetitiveItemKind.Rain => "비",
            CompetitiveItemKind.Snow => "눈",
            CompetitiveItemKind.StrongWind => "강풍",
            CompetitiveItemKind.Typhoon => "태풍",
            CompetitiveItemKind.Fog => "안개",
            CompetitiveItemKind.MovementSlow => "속도 디버프",
            CompetitiveItemKind.ProcessSlow => "공정 디버프",
            CompetitiveItemKind.OrderHack => "주문 해킹",
            CompetitiveItemKind.Umbrella => "우산",
            CompetitiveItemKind.MovementBoost => "속도 버프",
            CompetitiveItemKind.ProcessBoost => "공정 버프",
            CompetitiveItemKind.Cannon => "대포",
            _ => k.ToString(),
        };

        private sealed class UnityRandom : IRandomSource
        {
            public float NextFloat() => Random.value;
            public int NextInt(int maxExclusive) => Random.Range(0, maxExclusive);
        }
    }
}
