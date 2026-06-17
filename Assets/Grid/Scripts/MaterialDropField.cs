using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace GridSystem
{
    /// <summary>
    /// 바닥에 떨어진 재료들(붕괴/버리기/철거 결과). 서버 권위 NetworkList&lt;PickupEntry&gt; + 클라 로컬 비주얼.
    /// '노답중력': 전체 크기 그대로 떨어져 굴러다니고, 플레이어가 닿으면(근접) 서버가 차서(KickRpc) 굴려보낸다.
    /// 플레이어(PlayerCarry)가 F로 주워 재배치. GridManager(=Catalog) 와 같은 오브젝트에 둔다.
    /// </summary>
    [RequireComponent(typeof(GridManager))]
    public class MaterialDropField : NetworkBehaviour
    {
        private const float kKickDistance = 1.6f;

        private readonly NetworkList<PickupEntry> m_Pickups = new();
        private GridManager m_Grid;
        private ulong m_Counter;                                   // 서버 전용 pickupId 발급
        private GameObject m_Root;                                 // 클라 비주얼 부모
        private readonly Dictionary<ulong, GameObject> m_Visuals = new();
        private bool m_Spawned;                                    // 최초 Reconcile(늦참 복원) 이후 true → 그 후 추가분만 연출

        private void Awake() => m_Grid = GetComponent<GridManager>();

        public override void OnNetworkSpawn()
        {
            m_Root = new GameObject("~Pickups");
            m_Pickups.OnListChanged += OnChanged;
            Reconcile();        // 늦참: 이미 복제된 픽업들은 연출 없이 제자리 스냅(m_Spawned=false 동안)
            m_Spawned = true;   // 이후 추가되는 픽업만 떨굼/던지기 연출
        }

        public override void OnNetworkDespawn()
        {
            m_Pickups.OnListChanged -= OnChanged;
            if (m_Root != null) Destroy(m_Root);
        }

        // ── 서버: 재료를 바닥에 떨군다(fromPos에서 그 XZ 바닥에 안착, 약간 흩어짐) ──
        public void ServerDrop(int materialId, Vector3 fromPos)
        {
            if (!IsServer || materialId < 0) return;
            var rest = new Vector3(
                Mathf.Floor(fromPos.x) + 0.5f + Random.Range(-0.3f, 0.3f),
                0.5f,
                Mathf.Floor(fromPos.z) + 0.5f + Random.Range(-0.3f, 0.3f));
            ClampToFloor(ref rest);
            m_Pickups.Add(new PickupEntry
            {
                pickupId = ++m_Counter, materialId = materialId, pos = rest, fromPos = fromPos
            });
        }

        // ── 클라(소유자): 버리기 → 서버 떨굼 요청 ──────────────────────────
        public void RequestDrop(int materialId, Vector3 fromPos) => DropRpc(materialId, fromPos);

        [Rpc(SendTo.Server)]
        private void DropRpc(int materialId, Vector3 fromPos) => ServerDrop(materialId, fromPos);

        // ── 던지기(협동 전달): 조준 지점(toPos)에 착지하도록 떨군다. fromPos에서 날아오는 건 코스메틱.
        //    위치 권위는 toPos라 착지 지점의 동료가 바로 F로 줍는다. ─────────────
        public void ServerThrow(int materialId, Vector3 fromPos, Vector3 toPos)
        {
            if (!IsServer || materialId < 0) return;
            var rest = new Vector3(toPos.x, 0.5f, toPos.z);
            ClampToFloor(ref rest);
            m_Pickups.Add(new PickupEntry
            {
                pickupId = ++m_Counter, materialId = materialId, pos = rest, fromPos = fromPos
            });
        }

        public void RequestThrow(int materialId, Vector3 fromPos, Vector3 toPos) => ThrowRpc(materialId, fromPos, toPos);

        [Rpc(SendTo.Server)]
        private void ThrowRpc(int materialId, Vector3 fromPos, Vector3 toPos) => ServerThrow(materialId, fromPos, toPos);

        // 도구 던지기(협동 전달) — 재료 던지기와 동일하나 toolBit로 표시(재료 아님).
        public void RequestThrowTool(int toolBit, Vector3 fromPos, Vector3 toPos) => ThrowToolRpc(toolBit, fromPos, toPos);

        [Rpc(SendTo.Server)]
        private void ThrowToolRpc(int toolBit, Vector3 fromPos, Vector3 toPos)
        {
            if (!IsServer || toolBit == 0) return;
            var rest = new Vector3(toPos.x, 0.5f, toPos.z);
            ClampToFloor(ref rest);
            m_Pickups.Add(new PickupEntry
            {
                pickupId = ++m_Counter, materialId = -1, toolBit = toolBit, pos = rest, fromPos = fromPos
            });
        }

        // ── 킥(몸에 닿음): 서버가 dir 방향으로 픽업을 차서 굴려보낸다 ──────────
        public void RequestKick(ulong pickupId, Vector3 dir) => KickRpc(pickupId, dir);

        [Rpc(SendTo.Server)]
        private void KickRpc(ulong pickupId, Vector3 dir)
        {
            var d = new Vector3(dir.x, 0f, dir.z);
            if (d.sqrMagnitude < 1e-6f) return;
            d.Normalize();
            for (int i = 0; i < m_Pickups.Count; i++)
                if (m_Pickups[i].pickupId == pickupId)
                {
                    var p = m_Pickups[i];
                    var np = p.pos + d * kKickDistance;
                    np.y = 0.5f;
                    ClampToFloor(ref np);
                    p.pos = np;
                    m_Pickups[i] = p;   // 값 변경 → 복제 → 클라가 그 위치로 굴림
                    return;
                }
        }

        private void ClampToFloor(ref Vector3 p)
        {
            const float m = 6f;   // 그리드 밖(배송 구역 등)도 허용 — 킥 폭주만 막는 느슨한 경계
            var s = m_Grid.GridSize;
            p.x = Mathf.Clamp(p.x, -m, s.x + m);
            p.z = Mathf.Clamp(p.z, -m, s.z + m);
        }

        // ── 줍기 ────────────────────────────────────────────────────────────
        /// <summary>범위 내 모든 바닥 재료의 (id, pos)를 채운다(킥 감지용, 재사용 리스트).</summary>
        public void CollectWithin(Vector3 from, float range, List<ulong> ids, List<Vector3> positions)
        {
            ids.Clear(); positions.Clear();
            float r2 = range * range;
            foreach (var p in m_Pickups)
                if ((p.pos - from).sqrMagnitude <= r2) { ids.Add(p.pickupId); positions.Add(p.pos); }
        }

        public void RequestGrab(ulong pickupId) => GrabRpc(pickupId);

        [Rpc(SendTo.Server)]
        private void GrabRpc(ulong pickupId)
        {
            for (int i = 0; i < m_Pickups.Count; i++)
                if (m_Pickups[i].pickupId == pickupId) { m_Pickups.RemoveAt(i); return; }
        }

        /// <summary>재시작용: 바닥 재료 전부 제거(서버).</summary>
        public void ServerReset()
        {
            if (!IsServer) return;
            for (int i = m_Pickups.Count - 1; i >= 0; i--) m_Pickups.RemoveAt(i);
        }

        // ── 비주얼(reconcile: 새 픽업 생성, 위치변경 시 굴림 목표 갱신, 사라진 건 제거) ──
        private void OnChanged(NetworkListEvent<PickupEntry> _) => Reconcile();

        private void Reconcile()
        {
            if (m_Root == null) return;

            var present = new HashSet<ulong>();
            foreach (var p in m_Pickups)
            {
                present.Add(p.pickupId);
                if (m_Visuals.TryGetValue(p.pickupId, out var go))
                {
                    if (go != null) go.GetComponent<PickupBody>().SetTarget(p.pos);   // 킥 → 새 목표로 굴림
                }
                else m_Visuals[p.pickupId] = MakeVisual(p, m_Spawned);   // 최초 복원은 스냅, 라이브 추가는 연출
            }

            var gone = new List<ulong>();
            foreach (var kv in m_Visuals) if (!present.Contains(kv.Key)) gone.Add(kv.Key);
            foreach (var id in gone)
            {
                if (m_Visuals[id] != null) Destroy(m_Visuals[id]);
                m_Visuals.Remove(id);
            }
        }

        // 픽업에 '통과는 그대로, 레이캐스트만 맞는' 트리거 콜라이더 부여(마우스로 가리켜 집기).
        private static void AddPickupTrigger(GameObject go, Vector3 size)
        {
            var bc = go.AddComponent<BoxCollider>();
            bc.isTrigger = true;
            bc.size = size;
        }

        private GameObject MakeVisual(PickupEntry p, bool animate)
        {
            GameObject go;
            if (p.toolBit != 0)   // 던져진 도구 — 든 도구와 같은 색 구슬(파랑=망치/초록=페인트통)
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.name = $"~PickupTool{p.pickupId}";
                go.transform.localScale = Vector3.one * 0.5f;
                var tc = go.GetComponent<Collider>();
                if (tc != null) Destroy(tc);
                SetColor(go, ColorForMask(p.toolBit));
                go.transform.SetParent(m_Root.transform, true);
                var tbody = go.AddComponent<PickupBody>();
                tbody.SetIdentity(this, p.pickupId, p.materialId, p.toolBit);
                AddPickupTrigger(go, Vector3.one * 0.6f);   // 레이캐스트 집기용
                if (animate) tbody.Init(p.fromPos, p.pos); else tbody.Snap(p.pos);
                return go;
            }

            var def = m_Grid.Catalog != null ? m_Grid.Catalog.GetById(p.materialId) : null;
            var fp = def != null ? def.Footprint : Vector3Int.one;

            if (def != null && def.Prefab != null)   // 진짜 블록 외형(물 재질 등) — 중심을 홀더 원점에 맞춰 굴림
            {
                go = new GameObject($"~Pickup{p.pickupId}");
                var vis = Instantiate(def.Prefab, go.transform);
                vis.transform.localPosition = new Vector3(-fp.x * 0.5f, -fp.y * 0.5f, -fp.z * 0.5f);   // 피벗(min-corner) 보정
                foreach (var c in go.GetComponentsInChildren<Collider>()) Destroy(c);
            }
            else                                     // 프리팹 없음 → 공정색 큐브(폴백)
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = $"~Pickup{p.pickupId}";
                // 전체 크기 그대로(축소 X) — 배치된 블록과 같은 크기로 굴러다님
                go.transform.localScale =
                    new Vector3(Mathf.Max(1, fp.x), Mathf.Max(1, fp.y), Mathf.Max(1, fp.z)) * (GridContract.Unit * 0.9f);
                var col = go.GetComponent<Collider>();
                if (col != null) Destroy(col);
                SetColor(go, ColorForMask(def != null ? def.RequiredMask : 0));
            }

            go.transform.SetParent(m_Root.transform, true);
            var body = go.AddComponent<PickupBody>();
            body.SetIdentity(this, p.pickupId, p.materialId, p.toolBit);
            AddPickupTrigger(go, new Vector3(Mathf.Max(1, fp.x), Mathf.Max(1, fp.y), Mathf.Max(1, fp.z)) * GridContract.Unit);   // 레이캐스트 집기용
            if (animate) body.Init(p.fromPos, p.pos);   // 새로 떨굼/던짐 → 비행 연출
            else body.Snap(p.pos);                       // 늦참 복원 → 제자리 스냅(유령 비행 방지)
            return go;
        }

        private static Color ColorForMask(int mask)
        {
            if ((mask & (int)ProcessType.Painted) != 0) return new Color(0.30f, 0.85f, 0.40f);
            if ((mask & (int)ProcessType.Fixed) != 0)   return new Color(0.35f, 0.60f, 1.00f);
            return new Color(0.72f, 0.72f, 0.72f);
        }

        private static void SetColor(GameObject go, Color c)
        {
            var r = go.GetComponent<Renderer>();
            if (r == null) return;
            var mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            mpb.SetColor(Shader.PropertyToID("_BaseColor"), c);
            mpb.SetColor(Shader.PropertyToID("_Color"), c);
            r.SetPropertyBlock(mpb);
        }
    }
}
