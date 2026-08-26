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
        [Tooltip("던져진/버려진 '망치'(고정 도구)의 바닥 외형 모델(Hammer.glb). 비우면 파란 구로 폴백.")]
        [SerializeField] private GameObject m_HammerModel;
        [Tooltip("던져진/버려진 '페인트통'(페인트 도구)의 바닥 외형 모델(PaintCan.glb). 비우면 초록 구로 폴백.")]
        [SerializeField] private GameObject m_PaintCanModel;
        [Tooltip("도구 픽업 모델 스케일.")]
        [SerializeField] private float m_ToolModelScale = 0.5f;

        public GameObject HammerModel => m_HammerModel;   // 공정 마커 재사용(미공정 블록 위 망치)
        public GameObject PaintModel  => m_PaintCanModel; // 공정 마커 재사용(미공정 블록 위 페인트통)

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

        /// <summary>배송(보급소 주문): 던지기와 같지만 착지 높이를 배송 지점 높이로 존중한다(높은 곳에 배송 지점을 둘 수 있게).
        /// 반환: 발급된 pickupId(추적용 — 케이블카가 미수령 회수에 쓴다). 실패 시 0.</summary>
        public ulong ServerDeliver(int materialId, Vector3 fromPos, Vector3 toPos)
        {
            if (!IsServer || materialId < 0) return 0;
            var rest = new Vector3(toPos.x, toPos.y + 0.5f, toPos.z);
            ClampToFloor(ref rest);
            m_Pickups.Add(new PickupEntry
            {
                pickupId = ++m_Counter, materialId = materialId, pos = rest, fromPos = fromPos
            });
            return m_Counter;
        }

        /// <summary>서버: 특정 픽업의 현재 위치(권위값). 없으면 false — 이미 주워갔다는 뜻.</summary>
        public bool TryGetPickupPos(ulong pickupId, out Vector3 pos)
        {
            foreach (var p in m_Pickups)
                if (p.pickupId == pickupId) { pos = p.pos; return true; }
            pos = default;
            return false;
        }

        /// <summary>서버: 현재 픽업 목록 스냅샷(기믹용 — 사방신 받침대 판정 등). into를 비우고 채운다.</summary>
        public void ServerCollectPickups(System.Collections.Generic.List<PickupEntry> into)
        {
            into.Clear();
            if (!IsServer) return;
            foreach (var p in m_Pickups) into.Add(p);
        }

        /// <summary>서버: 특정 픽업 제거(케이블카 미수령 회수 등). 있었으면 true.</summary>
        public bool ServerRemove(ulong pickupId)
        {
            if (!IsServer) return false;
            for (int i = 0; i < m_Pickups.Count; i++)
                if (m_Pickups[i].pickupId == pickupId) { m_Pickups.RemoveAt(i); return true; }
            return false;
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
                    ClampToFloor(ref np, 6f);   // 킥 폭주 방지(그리드 주변으로 제한)
                    p.pos = np;
                    m_Pickups[i] = p;   // 값 변경 → 복제 → 클라가 그 위치로 굴림
                    return;
                }
        }

        /// <summary>서버: 픽업을 dir 방향으로 distance만큼 흘려보낸다(DDP 이간수문 물길 운반).
        /// 킥(RequestKick)과 달리 ① 이동 거리를 호출부가 정하고 ② 그리드 ±6m로 좁게 자르지 않는다 —
        /// 수로는 건축장에서 멀리까지 이어질 수 있어 킥의 폭주 방지 마진을 쓰면 중간에 걸려버린다.
        /// 대신 기본 마진(넉넉한 범위)으로는 여전히 잘라 맵 밖으로 흘러나가는 것은 막는다.
        /// 반환: 해당 픽업이 있었으면 true.</summary>
        public bool ServerFloat(ulong pickupId, Vector3 dir, float distance)
        {
            if (!IsServer) return false;
            var d = new Vector3(dir.x, 0f, dir.z);
            if (d.sqrMagnitude < 1e-6f || distance <= 0f) return false;
            d.Normalize();

            for (int i = 0; i < m_Pickups.Count; i++)
            {
                if (m_Pickups[i].pickupId != pickupId) continue;
                var p = m_Pickups[i];
                var np = p.pos + d * distance;
                np.y = p.pos.y;         // 수면 높이 유지(물에 뜬 채로 흘러간다)
                ClampToFloor(ref np);   // 기본 마진 — 맵 밖 유실만 방지
                p.pos = np;
                m_Pickups[i] = p;       // 값 변경 → 복제 → 클라가 그 위치로 굴림
                return true;
            }
            return false;
        }

        // 그리드 주변 월드 사각형으로 제한. 기준은 GridContract.Origin(맵 마커로 그리드가 이동하면 같이 이동) —
        // 예전엔 셀 개수를 월드 좌표처럼 써서, 그리드가 원점 밖에 있는 맵에선 배송 지점이 통째로 잘려 나갔다.
        private void ClampToFloor(ref Vector3 p, float marginUnits = 60f)   // 기본은 넉넉히(배송 구역이 멀 수 있음), 킥만 좁게
            => p = ClampToFloorWorld(p, m_Grid.EffectiveSize, GridContract.Origin, GridContract.Unit, marginUnits);

        /// <summary>그리드 주변 월드 사각형으로 제한(순수 계산 — 테스트 대상).</summary>
        public static Vector3 ClampToFloorWorld(Vector3 p, Vector3Int size, Vector3 origin, float unit, float marginUnits)
        {
            p.x = Mathf.Clamp(p.x, origin.x - marginUnits, origin.x + size.x * unit + marginUnits);
            p.z = Mathf.Clamp(p.z, origin.z - marginUnits, origin.z + size.z * unit + marginUnits);
            return p;
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
            if (p.toolBit != 0)   // 던져진 도구 — 망치(고정)는 모델, 그 외/폴백은 공정색 구슬
            {
                var model = (p.toolBit & (int)ProcessType.Fixed) != 0 ? m_HammerModel
                          : (p.toolBit & (int)ProcessType.Painted) != 0 ? m_PaintCanModel
                          : null;
                if (model != null)
                {
                    go = new GameObject($"~PickupTool{p.pickupId}");
                    var vis = Instantiate(model, go.transform);
                    vis.transform.localPosition = Vector3.zero;
                    go.transform.localScale = Vector3.one * m_ToolModelScale;
                    foreach (var c in go.GetComponentsInChildren<Collider>()) Destroy(c);
                }
                else
                {
                    go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    go.name = $"~PickupTool{p.pickupId}";
                    go.transform.localScale = Vector3.one * 0.5f;
                    var tc = go.GetComponent<Collider>();
                    if (tc != null) Destroy(tc);
                    SetColor(go, (p.toolBit & (int)ProcessType.Bucket) != 0
                        ? new Color(0.30f, 0.80f, 1.00f)     // 양동이 — 하늘색(경복궁)
                        : ColorForMask(p.toolBit));
                }
                go.transform.SetParent(m_Root.transform, true);
                var tbody = go.AddComponent<PickupBody>();
                tbody.SetIdentity(this, p.pickupId, p.materialId, p.toolBit);
                // 집기 판정 후하게: 루트 스케일(도구 모델 축소)이 로컬 콜라이더에 곱해져 실제 판정이
                // 0.3m급으로 쪼그라들던 문제 — 스케일을 역보정하고 월드 1.3m 박스로 키운다.
                float rootScale = Mathf.Max(0.01f, go.transform.localScale.x);
                AddPickupTrigger(go, Vector3.one * (1.3f / rootScale));   // 레이캐스트 집기용(넉넉하게)
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

        private static Material s_RuntimeMat;   // 런타임 프리미티브용 공유 URP Lit (빌드서 기본 머티리얼이 깨져 안 보이는 것 방지)
        private static void SetColor(GameObject go, Color c)
        {
            var r = go.GetComponent<Renderer>();
            if (r == null) return;
            if (s_RuntimeMat == null)
            {
                var sh = Shader.Find("Universal Render Pipeline/Lit");
                if (sh != null) s_RuntimeMat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            }
            if (s_RuntimeMat != null) r.sharedMaterial = s_RuntimeMat;
            var mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            mpb.SetColor(Shader.PropertyToID("_BaseColor"), c);
            mpb.SetColor(Shader.PropertyToID("_Color"), c);
            r.SetPropertyBlock(mpb);
        }
    }
}
