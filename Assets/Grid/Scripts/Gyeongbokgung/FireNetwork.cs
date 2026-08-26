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
        }

        private void Update()
        {
            if (!Active || !IsSpawned) return;
            RefreshDeumeus();
            if (IsServer) ServerTick();
            UpdateFlames();
        }

        // ── 서버 로직 ─────────────────────────────────────────────────────
        private void ServerTick()
        {
            if (Loop == null || Net == null) return;

            if (!Loop.IsBuilding)
            {
                if (m_Fires.Count > 0) for (int i = m_Fires.Count - 1; i >= 0; i--) m_Fires.RemoveAt(i);
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

            // ② 주기 발화
            float elapsed = Loop.Elapsed;
            if (elapsed < Config.FireStartDelay) return;
            if (m_NextFireAt <= 0f) m_NextFireAt = Now;   // 유예가 끝나는 순간 첫 발화
            if (Now < m_NextFireAt) return;
            m_NextFireAt = Now + Config.FireIntervalAt(elapsed - Config.FireStartDelay);
            TryIgniteRandom();
        }

        private void TryIgniteRandom()
        {
            Net.ServerCollectCells(m_CellScratch);
            // 오브젝트(owner) 단위 후보 수집 — 대표 셀 하나씩
            var seen = new HashSet<ulong>();
            var candidates = new List<(ulong owner, Vector3Int cell)>();
            var answer = Grid != null ? Grid.Answer : null;
            foreach (var e in m_CellScratch)
            {
                if (!seen.Add(e.ownerObjectId)) continue;
                if (IsBurning(e.ownerObjectId)) continue;
                if (answer != null && answer.IsPreset(e.cell)) continue;      // 기본 제공 블록은 불연
                if (GuardianNetwork.IsCellImmune(e.cell)) continue;           // 정령 보호 방위
                candidates.Add((e.ownerObjectId, e.cell));
            }
            if (candidates.Count == 0) return;
            var pick = candidates[UnityEngine.Random.Range(0, candidates.Count)];
            IgniteAt(pick.cell);
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
            if (ansCheck != null && ansCheck.IsPreset(cell)) return;
            if (GuardianNetwork.IsCellImmune(cell)) return;

            m_Fires.Add(new FireEntry { owner = owner, cx = cell.x, cy = cell.y, cz = cell.z, endTime = Now + Config.BurnSeconds });
            IgniteFxRpc(CellCenter(cell));
        }

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
                    if (answer != null && answer.IsPreset(n.cell)) continue;
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
        /// <summary>플레이어 근처(range)에서 가장 가까운 불타는 블록. PlayerCarry의 양동이 E 꾹 대상.</summary>
        public static bool TryGetNearestBurning(Vector3 pos, float range, out Vector3Int cell, out Vector3 center)
        {
            cell = default; center = default;
            var inst = Instance;
            if (inst == null || !inst.Active) return false;
            float best = range * range;
            bool found = false;
            foreach (var f in inst.m_Fires)
            {
                Vector3 c = CellCenter(f.Cell);
                float d = (c - pos).sqrMagnitude;
                if (d <= best) { best = d; cell = f.Cell; center = c; found = true; }
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
            GridJuice.FovPunch(Camera.main, -3f);
            GridJuice.WorldToast(center + Vector3.up * 1.6f, "화마가 나타났다!", new Color(1f, 0.35f, 0.1f));
            GridSoundBridge.PlaySFXAt("FallObjectWhileThrowing", center);
            // TODO 사운드팀: 화재 경보/타는 소리 루프 SFX — SFXType에 추가 후 여기서 호출 (GustNetwork.cs:187과 같은 계약)
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
            GridSoundBridge.PlaySFXAt("LandObject", center);
            GridJuice.WorldToast(center + Vector3.up * 1.4f, "진화 성공!", new Color(0.45f, 0.8f, 1f));
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
                m_Flames[f.owner] = BuildFlame(CellCenter(f.Cell) + Vector3.up * (0.6f * GridContract.Unit));
            }
        }

        private static GameObject BuildFlame(Vector3 pos)
        {
            // CFXR 사본이 Resources/Fx/Fire에 있으면 그걸, 없으면 절차 생성 불꽃(발광 큐브 3개 깜빡임)
            var prefab = Resources.Load<GameObject>("Fx/Fire");
            if (prefab != null) return Instantiate(prefab, pos, Quaternion.identity);

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
}
