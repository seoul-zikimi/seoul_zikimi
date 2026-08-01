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
    /// 획득 = 빈손으로 접근(1.2m), 사용 = F키. 효과는 이동속도 버프/디버프부터 실적용(나머지는 순차 연결).
    /// </summary>
    public class ItemNetwork : NetworkBehaviour,
        ICompetitiveItemSpawnGateway, IOpponentTeamResolver,
        IUnfixedConstructionTarget, ITeamMovementModifierTarget, ITeamProcessModifierTarget,
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

            public static TeamEffects None => new() { MoveMul = 1f, ProcessMul = 1f, OrderBlocked = false };

            public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
            {
                s.SerializeValue(ref MoveMul); s.SerializeValue(ref ProcessMul); s.SerializeValue(ref OrderBlocked);
            }
            public bool Equals(TeamEffects o) => MoveMul == o.MoveMul && ProcessMul == o.ProcessMul && OrderBlocked == o.OrderBlocked;
        }

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
        private uint m_NextId;
        // 서버 전용 만료 시각(Time.time). [0]=팀A, [1]=팀B
        private readonly float[] m_MoveUntil = new float[2];
        private readonly float[] m_ProcUntil = new float[2];
        private readonly float[] m_OrderUntil = new float[2];
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
            if (IsServer)
            {
                var definitions = CompetitiveItemDefinitionCatalog.CreateDefault();
                m_Director = DefaultCompetitiveItemFactory.CreateSpawnDirector(this, new UnityRandom(), definitions);
                var effects = DefaultCompetitiveItemFactory.CreateEffects(
                    definitions, this, this, this, this, this, this, this);
                m_UseService = new CompetitiveItemUseService(definitions, effects, this);
            }
            RebuildVisuals();
        }

        public override void OnNetworkDespawn()
        {
            m_Items.OnListChanged -= OnItemsChanged;
            if (m_VisualRoot != null) Destroy(m_VisualRoot);
            if (s_Instance == this) s_Instance = null;
        }

        // ── 조회/조작 API ─────────────────────────────────────────
        /// <summary>씬의 아이템 호스트(없으면 null — 협동 모드 등).</summary>
        public static ItemNetwork Instance => s_Instance;

        /// <summary>내가 아이템을 들고 있는가(E 사용 가능).</summary>
        public bool LocalHasItem => LocalHeldName() != "";

        /// <summary>[기획] 아이템을 든 채로 E — 입력은 PlayerCarry가 공정(도구)과 갈라서 호출한다.</summary>
        public void RequestUseHeld()
        {
            if (LocalHasItem) UseHeldRpc();
        }

        /// <summary>내(로컬 플레이어)가 들고 있는 아이템 이름. 없으면 "".</summary>
        public string LocalHeldName()
        {
            ulong me = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : ulong.MaxValue;
            foreach (var e in m_Items)
                if (e.Held && e.Holder == me) return KindName((CompetitiveItemKind)e.Kind);
            return "";
        }

        /// <summary>로컬 플레이어 팀의 이동속도 배율(PlayerMovement가 곱함). 협동/미배정 = 1.</summary>
        public static float LocalMoveMultiplier() => LocalEffects().MoveMul;

        /// <summary>로컬 플레이어 팀의 공정 진행 속도 배율(PlayerCarry가 곱함). 협동/미배정 = 1.</summary>
        public static float LocalProcessMultiplier() => LocalEffects().ProcessMul;

        /// <summary>내 팀이 주문 해킹당했는가(주문 HUD 안내용).</summary>
        public static bool LocalOrderBlocked() => LocalEffects().OrderBlocked;

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
            float x = (team * zone.x + Random.Range(0.5f, zone.x - 0.5f)) * u;
            float z = Random.Range(0.5f, zone.z - 0.5f) * u;
            Vector3 baseW = GridCoordinates.CellToWorld(Vector3Int.zero);
            return new Vector3(baseW.x + x, baseW.y + 0.45f, baseW.z + z);
        }

        // ── 서버 루프: 규칙 진행 + 완성도 보고 + 근접 획득 + 배율 만료 ──
        private void Update()
        {
            if (!IsSpawned) return;

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

            // 근접 자동 획득(빈손만)
            foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
            {
                var po = client.PlayerObject;
                if (po == null || HasHeld(client.ClientId)) continue;
                for (int i = 0; i < m_Items.Count; i++)
                {
                    var e = m_Items[i];
                    if (e.Held || Vector3.Distance(po.transform.position, e.Pos) > kPickupRange) continue;
                    e.Held = true; e.Holder = client.ClientId;
                    m_Items[i] = e;
                    m_Director?.NotifyConsumed(e.Id.ToString());   // 소지 중엔 60초 소멸 제외
                    break;
                }
            }

            ExpireEffects();
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
            foreach (var e in m_Items) if (e.Held && e.Holder == clientId) return true;
            return false;
        }

        // ── 사용 ─────────────────────────────────────────────────
        [Rpc(SendTo.Server)]
        private void UseHeldRpc(RpcParams rpc = default)
        {
            ulong sender = rpc.Receive.SenderClientId;
            for (int i = m_Items.Count - 1; i >= 0; i--)
            {
                var e = m_Items[i];
                if (!e.Held || e.Holder != sender) continue;
                int team = m_Loop.GetTeam(sender);
                if (team < 0) return;                      // 팀 미배정이면 소비하지 않는다
                m_Items.RemoveAt(i);
                m_UseService?.Use((CompetitiveItemKind)e.Kind, sender.ToString(), TeamId(team));
                return;
            }
        }

        // ── 프레임워크 효과 어댑터 ────────────────────────────────
        // 대상 팀 결정과 효과 실행은 CompetitiveItemUseService가 한다. 이 클래스는 각 효과 인터페이스의
        // '유니티 쪽 구현'만 제공한다(순수 도메인 ↔ 네트워크/월드 경계).
        private const string kTeamAId = kTeamA, kTeamBId = kTeamB;

        private static int TeamIndex(string teamId) => teamId == kTeamBId ? 1 : teamId == kTeamAId ? 0 : -1;
        private static string TeamId(int index) => index == 1 ? kTeamBId : kTeamAId;

        string IOpponentTeamResolver.GetOpponentTeamId(string sourceTeamId)
            => sourceTeamId == kTeamBId ? kTeamAId : kTeamBId;

        void IUnfixedConstructionTarget.CollapseAllUnfixed(string teamId)
        {
            int team = TeamIndex(teamId);
            if (team < 0 || m_Net == null) return;
            int n = m_Net.ServerEarthquake(team);
            Debug.Log($"[Item] 지진 → 팀{teamId}: 미고정 블록 {n}개 붕괴");
        }

        void ITeamMovementModifierTarget.ApplyMovementSpeedMultiplier(string teamId, float multiplier, float durationSeconds)
        {
            int team = TeamIndex(teamId);
            if (team < 0) return;
            var fx = Fx(team); fx.MoveMul = multiplier; SetFx(team, fx);
            m_MoveUntil[team] = Time.time + durationSeconds;
        }

        void ITeamProcessModifierTarget.ApplyProcessSpeedMultiplier(string teamId, float multiplier, float durationSeconds)
        {
            int team = TeamIndex(teamId);
            if (team < 0) return;
            var fx = Fx(team); fx.ProcessMul = multiplier; SetFx(team, fx);
            m_ProcUntil[team] = Time.time + durationSeconds;
        }

        void ITeamOrderLockTarget.LockNewOrders(string teamId, float durationSeconds)
        {
            int team = TeamIndex(teamId);
            if (team < 0) return;
            var fx = Fx(team); fx.OrderBlocked = true; SetFx(team, fx);
            m_OrderUntil[team] = Time.time + durationSeconds;
        }

        // 아래 셋은 아직 월드 쪽 구현이 없다 — 서비스 흐름은 그대로 타고, 효과만 순차 연결 예정.
        void ITeamFogTarget.ApplyFog(string teamId, float durationSeconds)
            => Debug.Log($"[Item] 안개 미구현(소비됨) → 팀{teamId} {durationSeconds}초");

        void ITemporaryTeamWeatherTarget.ApplyTemporaryWeather(string teamId, SeoulZikimi.Weather.WeatherKind weather, float durationSeconds)
            => Debug.Log($"[Item] 날씨({weather}) 미구현(소비됨) → 팀{teamId} {durationSeconds}초");

        void ITeamWeatherImmunityTarget.ApplyWeatherImmunity(string teamId, float durationSeconds)
            => Debug.Log($"[Item] 우산(날씨 면역) 미구현(소비됨) → 팀{teamId} {durationSeconds}초");

        // ── 비주얼(전 클라 로컬) — 종류별 색 구슬 + 이벤트별 FX ─────
        // 증분 갱신: 리스트 변화 종류로 등장/획득/사용/소멸을 구분해야 FX가 맞는 순간에 터진다.
        private void OnItemsChanged(NetworkListEvent<ItemEntry> e)
        {
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
                    if (e.Value.Held) ItemFx.Used(HolderPos(e.Value.Holder, e.Value.Pos), col);   // 사용
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
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = $"~Item_{e.Id}";
            go.transform.SetParent(m_VisualRoot.transform, false);
            go.transform.position = e.Pos;
            go.transform.localScale = Vector3.one * 0.55f;
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            var color = KindColor((CompetitiveItemKind)e.Kind);
            go.GetComponent<Renderer>().sharedMaterial =
                new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = color };
            ItemFx.DecorateOrb(go, color);   // 팝인 + 둥실 + 반짝이
            m_Visuals[e.Id] = go;
        }

        private void RebuildVisuals()
        {
            foreach (var kv in m_Visuals) if (kv.Value != null) Destroy(kv.Value);
            m_Visuals.Clear();
            foreach (var e in m_Items) AddVisual(e);
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
            _ => Color.gray,
        };

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
            _ => k.ToString(),
        };

        private sealed class UnityRandom : IRandomSource
        {
            public float NextFloat() => Random.value;
            public int NextInt(int maxExclusive) => Random.Range(0, maxExclusive);
        }
    }
}
