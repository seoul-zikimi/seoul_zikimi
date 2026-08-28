using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace GridSystem
{
    /// <summary>
    /// 화마(화재) 기믹(서버 권위) — 기획서(08/27):
    /// · 건축 시작 1분 후부터 랜덤 완성 블록에 발화(불씨 낙하 연출 + 경고 + 화면 흔들림)
    /// · 제한시간 내 진화 못 하면 블록 소실(재료 환원 없음) + 맞닿은 블록으로 전이
    /// · 발화 간격은 시간이 갈수록 짧아진다(하한 30초)
    /// · 진화 = 양동이(물 든 상태)로 E 꾹 — 물은 드므(Deumeu_*) 근처에서 자동 리필
    /// · 프리셋(기본 제공) 블록과 정령 보호 방위는 발화 대상에서 제외, 사방신 4개 완성 시 완전 봉인
    /// </summary>
    public class FireNetwork : GyeongbokgungGimmickBase
    {
        public static FireNetwork Instance { get; private set; }

        private struct FireEntry : INetworkSerializable, IEquatable<FireEntry>
        {
            public ulong owner;          // 불타는 블록(오브젝트) id
            public int cx, cy, cz;       // 대표 셀(연출 위치·진화 판정용)
            public float endTime;        // 이 시각까지 못 끄면 소실(서버 시간)

            public Vector3Int Cell => new Vector3Int(cx, cy, cz);

            public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
            {
                s.SerializeValue(ref owner);
                s.SerializeValue(ref cx); s.SerializeValue(ref cy); s.SerializeValue(ref cz);
                s.SerializeValue(ref endTime);
            }

            public bool Equals(FireEntry o) => owner == o.owner && cx == o.cx && cy == o.cy && cz == o.cz && Mathf.Approximately(endTime, o.endTime);
        }

        private readonly NetworkList<FireEntry> m_Fires = new();
        private float m_NextFireAt;                       // 서버 전용
        private bool m_FirstFireShown;                    // 서버 전용 — 라운드당 1회 첫 등장 시네마틱
        private bool m_HasPending;                        // 서버 전용 — 화마 전조 중(목표 확정됨)
        private Vector3Int m_PendingCell;                 // 서버 전용 — 전조 목표 셀
        private readonly List<CellEntry> m_CellScratch = new();
        private readonly List<Vector3Int> m_BlockScratch = new();
        private readonly Dictionary<ulong, GameObject> m_Flames = new();
        private readonly List<Transform> m_Deumeus = new();
        private float m_NextDeumeuFindAt;

        protected override void OnGimmickSpawn()
        {
            Instance = this;
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this) Instance = null;
            foreach (var f in m_Flames.Values) if (f != null) Destroy(f);
            m_Flames.Clear();
        }

        public void ServerReset()
        {
            if (!IsServer || !Active) return;
            for (int i = m_Fires.Count - 1; i >= 0; i--) m_Fires.RemoveAt(i);
            m_NextFireAt = 0f;
            m_FirstFireShown = false;
            m_HasPending = false;
        }

        private void Update()
        {
            if (!Active || !IsSpawned) return;
            RefreshDeumeus();
            if (IsServer) ServerTick();
            UpdateFlames();
            UpdateDemon();
        }

        // ── 화마(불꽃 악령) — 위협의 실체화(08/28). 순수 비주얼 레이어: 발화 로직/밸런스는 그대로.
        // 평소엔 궁 위를 선회 → 발화 전조에 목표 위로 날아가 맴돌기 → 발화 순간 급강하 →
        // 진화당하면 비명·도망 → 사방신 봉인 땐 4색 빛살에 맞아 폭발 소멸(GuardianNetwork의 봉인 연출이 호출).
        private FlameDemon m_Demon;

        private bool WantDemon => Active && IsSpawned && Loop != null && Loop.IsBuilding && !GuardianNetwork.IsSealed;

        private FlameDemon Demon()
        {
            if (WantDemon && m_Demon == null) m_Demon = FlameDemon.Spawn(DemonHome());
            return m_Demon;
        }

        private void UpdateDemon()
        {
            if (WantDemon) Demon();
            else if (m_Demon != null) { m_Demon.Vanish(); m_Demon = null; }
        }

        private Vector3 DemonHome()
        {
            // [08/28] 지붕 어깨 높이(그리드 높이의 55%) — 하늘이 아니라 건물·지면을 배경으로 날아야 눈에 띈다
            var size = Grid != null ? Grid.EffectiveSize : new Vector3Int(30, 13, 20);
            return GridCoordinates.CellToWorld(new Vector3Int(size.x / 2, 0, size.z / 2))
                 + Vector3.up * (size.y * 0.55f * GridContract.Unit);
        }

        /// <summary>화마 현재 위치(봉인 빛살 유도용). 없으면 null.</summary>
        public static Vector3? DemonPosition
            => Instance != null && Instance.m_Demon != null ? Instance.m_Demon.transform.position : (Vector3?)null;

        /// <summary>사방신 봉인 클라이맥스 — 화마 폭발 소멸(GuardianNetwork의 봉인 연출이 호출).</summary>
        public static void DemonSlain()
        {
            if (Instance == null || Instance.m_Demon == null) return;
            Instance.m_Demon.Die();
            Instance.m_Demon = null;
        }

        // 발화 전조 — 화마가 목표 블록 위로 출격(전 클라). 도착 후 맴돌다 발화 순간 급강하한다.
        [Rpc(SendTo.Everyone)]
        private void DemonSwoopRpc(Vector3 target, float arriveIn)
        {
            var d = Demon();
            if (d != null) d.SwoopTo(target, arriveIn);
        }

        // ── 서버 로직 ─────────────────────────────────────────────────────
        private void ServerTick()
        {
            if (Loop == null || Net == null) return;

            if (!Loop.IsBuilding)
            {
                if (m_Fires.Count > 0) for (int i = m_Fires.Count - 1; i >= 0; i--) m_Fires.RemoveAt(i);
                m_FirstFireShown = false;   // 다음 라운드에 시네마틱 재생 가능
                m_HasPending = false;
                return;
            }

            // 사방신 봉인 — 남은 불 전부 진화 + 이후 발화 없음
            if (GuardianNetwork.IsSealed)
            {
                for (int i = m_Fires.Count - 1; i >= 0; i--)
                {
                    ExtinguishFxRpc(CellCenter(m_Fires[i].Cell));
                    m_Fires.RemoveAt(i);
                }
                return;
            }

            // ① 소실 판정(전이 포함)
            for (int i = m_Fires.Count - 1; i >= 0; i--)
            {
                var f = m_Fires[i];
                if (Now < f.endTime) continue;

                // 전이 후보는 태우기 '전에' 수집(태우면 이웃 정보가 사라진다)
                var spreadTargets = CollectSpreadTargets(f.Cell, f.owner);
                m_Fires.RemoveAt(i);
                Net.ServerBurnBlock(f.Cell);
                foreach (var cell in spreadTargets) IgniteAt(cell);
            }

            // ② 주기 발화 — [08/28 적의 실체화] 발화 전에 화마(악령)가 목표 위로 날아가는 전조가 붙는다:
            //   (발화시각 - 전조) 목표 확정 + 화마 출격 RPC → (발화시각) 그 셀 발화(재검증).
            float elapsed = Loop.Elapsed;
            if (elapsed < Config.FireStartDelay) return;
            float lead = Mathf.Max(0.5f, Config.DemonLeadSeconds);
            if (m_NextFireAt <= 0f) m_NextFireAt = Now + lead;   // 유예 끝 → 첫 출격 후 발화
            if (!m_HasPending && Now >= m_NextFireAt - lead)
            {
                if (TryPickCandidate(out var cell))
                {
                    m_HasPending = true; m_PendingCell = cell;
                    DemonSwoopRpc(CellCenter(cell), m_NextFireAt - Now);
                }
                else m_NextFireAt = Now + 3f;   // 태울 게 아직 없음 — 잠깐 뒤 재시도
            }
            if (m_HasPending && Now >= m_NextFireAt)
            {
                m_HasPending = false;
                IgniteAt(m_PendingCell);   // 내부 재검증 — 그 사이 사라졌으면 조용히 통과
                m_NextFireAt = Now + Config.FireIntervalAt(elapsed - Config.FireStartDelay);
            }
        }

        private bool TryPickCandidate(out Vector3Int cell)
        {
            cell = default;
            Net.ServerCollectCells(m_CellScratch);
            // 오브젝트(owner) 단위 후보 수집 — 대표 셀 하나씩
            var seen = new HashSet<ulong>();
            var candidates = new List<(ulong owner, Vector3Int cell)>();
            var answer = Grid != null ? Grid.Answer : null;
            foreach (var e in m_CellScratch)
            {
                if (!seen.Add(e.ownerObjectId)) continue;
                if (IsBurning(e.ownerObjectId)) continue;
                if (!Config.BurnPresetBlocks && answer != null && answer.IsPreset(e.cell)) continue;   // 프리셋 불연 옵션
                if (GuardianNetwork.IsCellImmune(e.cell)) continue;           // 정령 보호 방위
                candidates.Add((e.ownerObjectId, e.cell));
            }
            if (candidates.Count == 0) return false;
            cell = candidates[UnityEngine.Random.Range(0, candidates.Count)].cell;
            return true;
        }

        private void IgniteAt(Vector3Int cell)
        {
            if (!Net.TryGetCell(cell, out _, out _)) return;
            // owner 확인(복제 리스트 기준)
            Net.ServerCollectCells(m_CellScratch);
            ulong owner = 0; bool found = false;
            foreach (var e in m_CellScratch)
                if (e.cell == cell) { owner = e.ownerObjectId; found = true; break; }
            if (!found || IsBurning(owner)) return;
            var ansCheck = Grid != null ? Grid.Answer : null;
            if (!Config.BurnPresetBlocks && ansCheck != null && ansCheck.IsPreset(cell)) return;
            if (GuardianNetwork.IsCellImmune(cell)) return;

            m_Fires.Add(new FireEntry { owner = owner, cx = cell.x, cy = cell.y, cz = cell.z, endTime = Now + Config.BurnSeconds });
            IgniteFxRpc(CellCenter(cell));
            if (!m_FirstFireShown)   // 라운드 첫 발화 — 화면 전체 등장 시네마틱(비네트+배너+사방신 안내)
            {
                m_FirstFireShown = true;
                FirstFireCinematicRpc();
            }
        }

        /// <summary>첫 발화 시네마틱 훅 — GameLoopHUD가 Init에서 구독(GridSystem 어셈블리는 UI를 참조 못 하므로 이벤트로 뒤집는다. GridJuice.FovPunchHandler와 같은 계약).</summary>
        public static event Action FirstFireCinematic;

        /// <summary>발화할 때마다 호출 — GameLoopHUD가 짧은 빨간 비네트 펄스를 띄운다(08/28 피드백).</summary>
        public static event Action Ignited;

        // 첫 발화 시네마틱 — 모든 클라 화면에 빨간 비네트 + "화마가 나타났다!" 배너 + 사방신 안내 토스트.
        [Rpc(SendTo.Everyone)]
        private void FirstFireCinematicRpc() => FirstFireCinematic?.Invoke();

        // 대표 셀이 속한 블록의 전(全) 셀에서 면접촉 이웃 블록 대표 셀들을 모아 무작위 SpreadCount개 반환.
        private List<Vector3Int> CollectSpreadTargets(Vector3Int repCell, ulong burningOwner)
        {
            var result = new List<Vector3Int>();
            if (Config.SpreadCount <= 0) return result;
            if (!Net.TryGetBlockCells(repCell, m_BlockScratch)) return result;

            Net.ServerCollectCells(m_CellScratch);
            var byCell = new Dictionary<Vector3Int, CellEntry>();
            foreach (var e in m_CellScratch) byCell[e.cell] = e;

            var neighborOwners = new HashSet<ulong>();
            var candidates = new List<Vector3Int>();
            Vector3Int[] dirs = { Vector3Int.right, Vector3Int.left, Vector3Int.up, Vector3Int.down, new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1) };
            var answer = Grid != null ? Grid.Answer : null;
            foreach (var c in m_BlockScratch)
                foreach (var d in dirs)
                {
                    if (!byCell.TryGetValue(c + d, out var n)) continue;
                    if (n.ownerObjectId == burningOwner) continue;
                    if (IsBurning(n.ownerObjectId)) continue;
                    if (!neighborOwners.Add(n.ownerObjectId)) continue;
                    if (!Config.BurnPresetBlocks && answer != null && answer.IsPreset(n.cell)) continue;
                    if (GuardianNetwork.IsCellImmune(n.cell)) continue;
                    candidates.Add(n.cell);
                }

            for (int k = 0; k < Config.SpreadCount && candidates.Count > 0; k++)
            {
                int idx = UnityEngine.Random.Range(0, candidates.Count);
                result.Add(candidates[idx]);
                candidates.RemoveAt(idx);
            }
            return result;
        }

        private bool IsBurning(ulong owner)
        {
            foreach (var f in m_Fires) if (f.owner == owner) return true;
            return false;
        }

        // ── 진화(양동이) — PlayerCarry가 호출 ─────────────────────────────
        /// <summary>플레이어 근처(range)에서 가장 가까운 불타는 블록. PlayerCarry의 양동이 E 꾹 대상.
        /// [08/28] 판정 후하게: ① 대표 셀 하나가 아니라 '블록 전체 셀' 중 최근접으로 잼(넓은 기와의 반대편에서도 OK)
        /// ② 거리는 수평(XZ) 위주 — 세로는 range의 3배까지 관대: 지상에서 2층 불, 마루에서 지붕 불도 끈다
        /// (등반 강요가 '불 끄기=귀찮은 심부름'을 만들던 문제 해소). 진화 키는 여전히 대표 셀.</summary>
        private static readonly List<Vector3Int> s_NearScratch = new();
        public static bool TryGetNearestBurning(Vector3 pos, float range, out Vector3Int cell, out Vector3 center)
        {
            cell = default; center = default;
            var inst = Instance;
            if (inst == null || !inst.Active) return false;
            float best = float.MaxValue;
            bool found = false;
            foreach (var f in inst.m_Fires)
            {
                var cells = s_NearScratch;
                if (inst.Net == null || !inst.Net.TryGetBlockCells(f.Cell, cells) || cells.Count == 0)
                { cells.Clear(); cells.Add(f.Cell); }
                foreach (var c in cells)
                {
                    Vector3 cc = CellCenter(c);
                    Vector3 d = cc - pos;
                    float dy = Mathf.Abs(d.y); d.y = 0f;
                    if (d.magnitude > range || dy > range * 3f) continue;
                    float score = d.sqrMagnitude + dy * dy * 0.1f;   // 수평 우선, 세로는 약한 가중
                    if (score < best) { best = score; cell = f.Cell; center = cc; found = true; }
                }
            }
            return found;
        }

        /// <summary>양동이 물 붓기 완료 — 서버에 진화 요청.</summary>
        public static void RequestExtinguish(Vector3Int cell)
        {
            if (Instance != null && Instance.Active) Instance.ExtinguishRpc(cell);
        }

        [Rpc(SendTo.Server)]
        private void ExtinguishRpc(Vector3Int cell)
        {
            for (int i = m_Fires.Count - 1; i >= 0; i--)
                if (m_Fires[i].Cell == cell)
                {
                    ExtinguishFxRpc(CellCenter(cell));
                    m_Fires.RemoveAt(i);
                    return;
                }
        }

        /// <summary>드므 근처인가(양동이 자동 리필 판정). 기믹 꺼진 맵에선 false.</summary>
        public static bool NearDeumeu(Vector3 pos, out Vector3 deumeuPos)
        {
            deumeuPos = default;
            var inst = Instance;
            if (inst == null || !inst.Active) return false;
            float range = inst.Config.DeumeuRefillRange;
            foreach (var t in inst.m_Deumeus)
            {
                if (t == null) continue;
                Vector3 d = t.position - pos; d.y = 0f;
                if (d.magnitude <= range) { deumeuPos = t.position; return true; }
            }
            return false;
        }

        /// <summary>양동이 물 붓기 시간(E 꾹). 기믹 없으면 기본 1.2초.</summary>
        public static float ExtinguishSeconds => Instance != null && Instance.Active ? Instance.Config.ExtinguishSeconds : 1.2f;

        private void RefreshDeumeus()
        {
            if (m_Deumeus.Count >= 4 || Time.time < m_NextDeumeuFindAt) return;
            m_NextDeumeuFindAt = Time.time + 0.5f;
            m_Deumeus.Clear();
            for (int i = 1; i <= 4; i++)
            {
                var go = GameObject.Find($"Deumeu_{i}");
                if (go != null) m_Deumeus.Add(go.transform);
            }
        }

        // ── 연출 ─────────────────────────────────────────────────────────
        private static Vector3 CellCenter(Vector3Int cell)
            => GridCoordinates.CellToWorld(cell) + Vector3.one * (0.5f * GridContract.Unit);

        [Rpc(SendTo.Everyone)]
        private void IgniteFxRpc(Vector3 center)
        {
            EmberFall(center);
            if (m_Demon != null) m_Demon.Dive(center);   // 화마 급강하(불씨 버스트 포함)
            GridJuice.FovPunch(Camera.main, -3f);
            Ignited?.Invoke();   // 매 발화 빨간 비네트 펄스(첫 발화 대형 시네마틱과는 HUD 쪽에서 중복 방지)
            GridJuice.WorldToast(center + Vector3.up * 1.6f, "화마가 나타났다!", new Color(1f, 0.35f, 0.1f));
            GridSoundBridge.PlaySFXAt("FireIgnite", center);   // 발화 SFX(08/28 사운드 적용) — 타는 루프는 화염 그룹이 담당
        }

        [Rpc(SendTo.Everyone)]
        private void ExtinguishFxRpc(Vector3 center)
        {
            var splash = Resources.Load<GameObject>("Fx/WaterSplash");
            if (splash != null) { var go = Instantiate(splash, center, Quaternion.identity); Destroy(go, 4f); }
            for (int i = 0; i < 10; i++)
            {
                var fx = GridJuice.MakeBit(center, 0.09f, new Color(0.4f, 0.75f, 1f));
                fx.vel = UnityEngine.Random.insideUnitSphere * 2.2f + Vector3.up * 2f;
                fx.gravity = -7f; fx.life = 0.5f;
            }
            GridSoundBridge.PlaySFXAt("WaterPour", center);   // 물 붓기 SFX(08/28 사운드 적용)
            GridJuice.WorldToast(center + Vector3.up * 1.4f, "진화 성공!", new Color(0.45f, 0.8f, 1f));
            if (m_Demon != null) m_Demon.Flee(center);   // 화마 비명·도망 — 물 부을 맛
        }

        // 불씨 낙하 — 하늘에서 주황 발광 조각이 대상으로 떨어진다(석상 빛기둥과 확실히 구분).
        private static void EmberFall(Vector3 target)
        {
            for (int i = 0; i < 3; i++)
            {
                var fx = GridJuice.MakeBit(target + Vector3.up * (16f + i * 2f) + UnityEngine.Random.insideUnitSphere * 0.8f,
                                           0.16f, new Color(1f, 0.45f, 0.1f));
                fx.vel = Vector3.down * (14f + i * 2f);
                fx.gravity = -4f;
                fx.life = 1.15f;
            }
        }

        // 불타는 블록 위 화염 비주얼 — 셀 자식이 아니라 '독립 오브젝트'(RebuildVisuals가 자식을 갈아엎기 때문).
        private void UpdateFlames()
        {
            // 사라진 불 정리
            var dead = new List<ulong>();
            foreach (var kv in m_Flames)
            {
                bool alive = false;
                foreach (var f in m_Fires) if (f.owner == kv.Key) { alive = true; break; }
                if (!alive) dead.Add(kv.Key);
            }
            foreach (var k in dead) { if (m_Flames[k] != null) Destroy(m_Flames[k]); m_Flames.Remove(k); }

            // 새 불 생성
            foreach (var f in m_Fires)
            {
                if (m_Flames.ContainsKey(f.owner)) continue;
                m_Flames[f.owner] = BuildFlameGroup(f.Cell);
            }
        }

        // 블록 '전체'가 타는 느낌 — 대표 셀 하나가 아니라 블록 중간층 셀들에 2칸 간격으로 불꽃을 깐다.
        // [08/28 피드백] 최상층+위 0.6칸은 블록 위에 떠 보임 → 세로 중앙층에 배치(모델 몸통이 탄다).
        private readonly List<Vector3Int> m_FlameCellScratch = new();
        private GameObject BuildFlameGroup(Vector3Int repCell)
        {
            var root = new GameObject("~FlameGroup");
            root.transform.position = CellCenter(repCell);
            // 타는 소리 루프 — 불이 붙어 있는 동안 그 자리에서 3D 재생(불 꺼지면 그룹째 파괴돼 같이 멎는다)
            var burnClip = Resources.Load<AudioClip>("Sfx/FireBurning");
            if (burnClip != null)
            {
                var src = root.AddComponent<AudioSource>();
                src.clip = burnClip; src.loop = true;
                src.spatialBlend = 1f; src.minDistance = 3f; src.maxDistance = 25f; src.volume = 0.8f;
                src.Play();
            }
            var cells = m_FlameCellScratch;
            if (Net == null || !Net.TryGetBlockCells(repCell, cells) || cells.Count == 0)
            { cells.Clear(); cells.Add(repCell); }

            int minY = int.MaxValue, maxY = int.MinValue;
            foreach (var c in cells) { minY = Mathf.Min(minY, c.y); maxY = Mathf.Max(maxY, c.y); }
            int midY = (minY + maxY) / 2;   // 3층짜리 블록이면 가운데 층
            var spots = new List<Vector3Int>();
            foreach (var c in cells) if (c.y == midY && ((c.x + c.z) & 1) == 0) spots.Add(c);
            if (spots.Count == 0) spots.Add(cells[0]);

            float per = spots.Count > 3 ? 0.7f : 1f;   // 넓은 블록은 개당 살짝 작게(과밀 방지)
            foreach (var c in spots)
                BuildFlame(CellCenter(c) + Vector3.up * (0.1f * GridContract.Unit), per).transform.SetParent(root.transform, true);
            return root;
        }

        private GameObject BuildFlame(Vector3 pos, float sizeMul = 1f)
        {
            float scale = (Config != null ? Config.FlameScale : 2.2f) * sizeMul;
            // CFXR 사본이 Resources/Fx/Fire에 있으면 그걸, 없으면 절차 생성 불꽃(발광 큐브 3개 깜빡임)
            var prefab = Resources.Load<GameObject>("Fx/Fire");
            if (prefab != null)
            {
                var fx = Instantiate(prefab, pos, Quaternion.identity);
                fx.transform.localScale *= scale;
                return fx;
            }

            var root = new GameObject("~Flame");
            root.transform.position = pos;
            var colors = new[] { new Color(1f, 0.32f, 0.05f, 0.85f), new Color(1f, 0.62f, 0.10f, 0.8f), new Color(1f, 0.85f, 0.25f, 0.75f) };
            for (int i = 0; i < 3; i++)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Cube);
                UnityEngine.Object.Destroy(q.GetComponent<Collider>());
                q.transform.SetParent(root.transform, false);
                q.transform.localPosition = new Vector3(0f, i * 0.28f, 0f);
                q.transform.localScale = Vector3.one * (0.8f - i * 0.22f);
                q.GetComponent<Renderer>().sharedMaterial = GuardianNetwork.MakeGlow(colors[i]);
                var flick = q.AddComponent<FlameFlicker>();
                flick.Phase = i * 1.7f;
            }
            root.transform.localScale = Vector3.one * (0.6f * scale);   // 폴백도 배율 반영
            return root;
        }

        private class FlameFlicker : MonoBehaviour
        {
            public float Phase;
            private Vector3 m_Base;
            private void Start() => m_Base = transform.localScale;
            private void Update()
            {
                float k = 1f + 0.25f * Mathf.Sin(Time.time * 11f + Phase) + 0.1f * Mathf.Sin(Time.time * 23f + Phase * 2f);
                transform.localScale = m_Base * k;
                transform.localRotation = Quaternion.Euler(0f, Time.time * 90f + Phase * 40f, 0f);
            }
        }
    }

    /// <summary>화마 비주얼 본체 — 절차 생성(발광 코어+눈+꼬리+불씨 흘리기). 클라 로컬(연출 전용, 판정 없음).
    /// VARCO 카툰 불꽃 악령 모델이 나오면 Build()만 모델 인스턴스로 바꾸면 된다.</summary>
    internal sealed class FlameDemon : MonoBehaviour
    {
        private enum Mode { Orbit, Swoop, Flee }
        private Mode m_Mode = Mode.Orbit;
        private Vector3 m_Home, m_Target, m_FleeDir;
        private float m_ArriveAt, m_FleeUntil, m_Phase, m_EmberAt;
        private Transform m_Body;
        private Vector3 m_BodyBaseScale = Vector3.one;   // 일렁임의 기준(모델 정규화 스케일 보존)
        private bool m_PuffToggle;
        public bool Dying { get; private set; }

        public static FlameDemon Spawn(Vector3 home)
        {
            var root = new GameObject("~FlameDemon");
            root.transform.position = home + new Vector3(13f, 0f, 0f);
            var d = root.AddComponent<FlameDemon>();
            d.m_Home = home;
            d.Build();
            return d;
        }

        private Material m_DarkMat;   // 불투명 암체 — 가산 발광은 밝은 하늘에서 씻겨 보여서 실루엣 담당이 따로 필요

        private void Build()
        {
            // VARCO 불꽃 정령 모델(Resources/Gyeongbokgung/FlameDemon)이 있으면 그걸 쓴다(크기 자동 정규화).
            var model = Resources.Load<GameObject>("Gyeongbokgung/FlameDemon");
            if (model != null)
            {
                m_Body = Instantiate(model, transform).transform;
                foreach (var col in m_Body.GetComponentsInChildren<Collider>()) Destroy(col);
                var rends = m_Body.GetComponentsInChildren<Renderer>();
                if (rends.Length > 0)
                {
                    var b = rends[0].bounds;
                    foreach (var r in rends) b.Encapsulate(r.bounds);
                    float longest = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
                    if (longest > 1e-4f) m_Body.localScale = Vector3.one * (2.4f / longest);
                    b = rends[0].bounds;
                    foreach (var r in rends) b.Encapsulate(r.bounds);
                    m_Body.localPosition = transform.InverseTransformPoint(transform.position * 2f - b.center);   // 중심 정렬
                }
                m_BodyBaseScale = m_Body.localScale;
                // [08/28] '너무 입체적' 피드백 → 유령화: 발광 아우라 2겹(윤곽을 뿌옇게 녹임) + 일렁임/흐느적은 Flicker가 담당
                Halo(2.9f, new Color(1f, 0.45f, 0.10f, 0.20f));
                Halo(3.6f, new Color(1f, 0.30f, 0.05f, 0.09f));
                return;
            }

            // 폴백: 절차 생성 — 이중 구조: 검은 몸체(밝은 배경에서 실루엣) + 그 위 가산 발광 불꽃
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            m_DarkMat = sh != null ? new Material(sh) : null;
            if (m_DarkMat != null) m_DarkMat.SetColor("_BaseColor", new Color(0.16f, 0.07f, 0.05f));

            m_Body = new GameObject("Body").transform;
            m_Body.SetParent(transform, false);
            Orb(Vector3.zero, 1.7f, default, dark: true);                                   // 검은 몸체
            Orb(new Vector3(0f, 0f, -1.05f), 0.95f, default, dark: true);                   // 검은 꼬리
            Orb(new Vector3(0f, 0.05f, 0.3f), 1.35f, new Color(1f, 0.36f, 0.05f, 0.9f));    // 불꽃 코어
            Orb(new Vector3(0f, 0.15f, 0.55f), 0.8f, new Color(1f, 0.85f, 0.3f, 0.95f));    // 속불
            Orb(new Vector3(0f, -0.05f, -0.6f), 0.7f, new Color(1f, 0.3f, 0.05f, 0.6f));    // 꼬리 불꽃
            Orb(new Vector3(-0.3f, 0.32f, 0.95f), 0.24f, default, dark: true);              // 눈(속불 위 검은 점)
            Orb(new Vector3(0.3f, 0.32f, 0.95f), 0.24f, default, dark: true);
            m_BodyBaseScale = m_Body.localScale;
        }

        // 유령 아우라 — 몸 주위 큰 반투명 발광 구(루트에 부착, 윤곽을 뿌옇게 녹여 '실체'가 아니라 '정령'으로 읽히게)
        private void Halo(float dia, Color c)
        {
            var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(s.GetComponent<Collider>());
            s.transform.SetParent(transform, false);
            s.transform.localScale = Vector3.one * dia;
            s.GetComponent<Renderer>().sharedMaterial = GuardianNetwork.MakeGlow(c);
        }

        private void Orb(Vector3 pos, float dia, Color c, bool dark = false)
        {
            var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(s.GetComponent<Collider>());
            s.transform.SetParent(m_Body, false);
            s.transform.localPosition = pos;
            s.transform.localScale = Vector3.one * dia;
            s.GetComponent<Renderer>().sharedMaterial = dark && m_DarkMat != null ? m_DarkMat : GuardianNetwork.MakeGlow(c);
        }

        /// <summary>발화 전조 — 목표 상공으로 출격, arriveIn의 60% 지점에 도착해 위협적으로 맴돈다.</summary>
        public void SwoopTo(Vector3 target, float arriveIn)
        {
            if (Dying) return;
            m_Mode = Mode.Swoop;
            m_Target = target + Vector3.up * 2.2f;   // 목표 바로 위 — 플레이 시야 안에서 맴돈다
            m_ArriveAt = Time.time + Mathf.Max(0.4f, arriveIn * 0.6f);
        }

        /// <summary>발화 순간 — 목표에 내리꽂히며 불씨 버스트, 곧장 선회 복귀.</summary>
        public void Dive(Vector3 igniteCenter)
        {
            if (Dying) return;
            transform.position = igniteCenter + Vector3.up * 1.6f;
            m_Mode = Mode.Orbit;
            for (int i = 0; i < 8; i++)
            {
                var fx = GridJuice.MakeBit(transform.position, 0.14f, new Color(1f, 0.45f, 0.1f));
                fx.vel = UnityEngine.Random.insideUnitSphere * 3f + Vector3.up * 1.5f;
                fx.gravity = -6f; fx.life = 0.6f;
            }
        }

        /// <summary>진화당함 — 비명 지르며 반대 방향으로 도망(1.1초), 이후 선회 복귀.</summary>
        public void Flee(Vector3 from)
        {
            if (Dying) return;
            m_Mode = Mode.Flee;
            m_FleeDir = ((transform.position - from).normalized + Vector3.up * 0.6f).normalized;
            m_FleeUntil = Time.time + 1.1f;
            GridJuice.WorldToast(transform.position + Vector3.up * 1.2f, "캬아악!", new Color(1f, 0.5f, 0.2f));
        }

        /// <summary>봉인 처치 — 폭발 소멸(사방신 빛살 명중 순간 GuardianNetwork가 호출).</summary>
        public void Die()
        {
            if (Dying) return;
            Dying = true;
            for (int i = 0; i < 26; i++)
            {
                var fx = GridJuice.MakeBit(transform.position, 0.18f, i % 3 == 0 ? Color.white : new Color(1f, 0.5f, 0.1f));
                fx.vel = UnityEngine.Random.onUnitSphere * UnityEngine.Random.Range(3f, 7f);
                fx.gravity = -3f; fx.life = 0.9f; fx.spinDeg = 300f; fx.spinAxis = UnityEngine.Random.onUnitSphere;
            }
            GridJuice.WorldToast(transform.position + Vector3.up * 1.5f, "화마 소멸!!", new Color(1f, 0.9f, 0.4f));
            Destroy(gameObject);
        }

        /// <summary>라운드 종료 등 조용한 퇴장.</summary>
        public void Vanish() => Destroy(gameObject);

        private void Update()
        {
            m_Phase += Time.deltaTime;
            Vector3 want;
            switch (m_Mode)
            {
                case Mode.Swoop:   // 목표 상공에서 파닥거리며 맴돌기(위협 예고)
                    want = m_Target + new Vector3(Mathf.Sin(m_Phase * 5f) * 0.7f, Mathf.Sin(m_Phase * 7f) * 0.4f, Mathf.Cos(m_Phase * 5f) * 0.7f);
                    break;
                case Mode.Flee:
                    if (Time.time >= m_FleeUntil) { m_Mode = Mode.Orbit; goto default; }
                    transform.position += m_FleeDir * (11f * Time.deltaTime);
                    FaceAlong(m_FleeDir);
                    Flicker(); DripEmber();
                    return;
                default:           // 지붕 어깨 높이에서 건물을 스치는 큰 타원 선회(벽·기와를 배경으로 지나가 눈에 띈다)
                    want = m_Home + new Vector3(Mathf.Cos(m_Phase * 0.45f) * 17f, Mathf.Sin(m_Phase * 1.3f) * 2.5f, Mathf.Sin(m_Phase * 0.45f) * 12f);
                    break;
            }
            Vector3 delta = want - transform.position;
            float speed = m_Mode == Mode.Swoop
                ? Mathf.Max(6f, delta.magnitude / Mathf.Max(0.15f, m_ArriveAt - Time.time))
                : 7f;
            transform.position += delta.normalized * Mathf.Min(delta.magnitude, speed * Time.deltaTime);
            if (delta.sqrMagnitude > 0.04f) FaceAlong(delta);
            Flicker(); DripEmber();
        }

        private void FaceAlong(Vector3 dir)
        {
            if (dir.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir.normalized), 8f * Time.deltaTime);
        }

        private void Flicker()
        {
            if (m_Body == null) return;
            // 유령 일렁임: 균일 플리커 + 세로로 늘었다 줄었다(부피 보존) + 흐느적 기울기 — '고체'가 아니라 '기체'로
            float k = 1f + 0.08f * Mathf.Sin(m_Phase * 13f) + 0.04f * Mathf.Sin(m_Phase * 29f);
            float stretch = 1f + 0.14f * Mathf.Sin(m_Phase * 6.3f);
            float xz = k / Mathf.Sqrt(stretch);
            m_Body.localScale = new Vector3(m_BodyBaseScale.x * xz, m_BodyBaseScale.y * k * stretch, m_BodyBaseScale.z * xz);
            m_Body.localRotation = Quaternion.Euler(Mathf.Sin(m_Phase * 2.1f) * 7f, 0f, Mathf.Sin(m_Phase * 1.7f) * 9f);
        }

        private void DripEmber()   // 지나간 자리에 불씨·유령 잔불 — '쟤가 불의 근원'이라는 시각 언어
        {
            if (Time.time < m_EmberAt) return;
            m_EmberAt = Time.time + 0.12f;
            m_PuffToggle = !m_PuffToggle;
            if (m_PuffToggle)
            {
                // 유령 잔상 퍼프: 크고 옅은 조각이 제자리에서 부풀며 사라진다(연기 같은 꼬리)
                var p = GridJuice.MakeBit(transform.position + UnityEngine.Random.insideUnitSphere * 0.4f, 0.55f, new Color(1f, 0.42f, 0.1f));
                p.vel = Vector3.up * 0.4f; p.gravity = 0f; p.life = 0.6f; p.scaleVel = 0.9f; p.startAlpha = 0.25f;
                return;
            }
            var fx = GridJuice.MakeBit(transform.position + UnityEngine.Random.insideUnitSphere * 0.5f, 0.1f, new Color(1f, 0.5f, 0.12f));
            fx.vel = Vector3.down * 1.5f + UnityEngine.Random.insideUnitSphere;
            fx.gravity = -2f; fx.life = 0.7f;
        }
    }
}
