using Unity.Netcode;
using UnityEngine;

namespace GridSystem
{
    /// <summary>
    /// 재료 보급소(물류창고). 리썰컴퍼니식 '주문 → 배송(물리 재료) → 걸어가서 Space로 줍기'.
    /// 주문하면 배송 구역에 실제 재료(MaterialDropField 픽업)가 떨어진다.
    /// 추상 재고 카운트 없음 — 바닥에 놓인 물리 재료가 곧 재고. 한 번에 하나만 들 수 있으니 숫자키 선택도 없음.
    /// GridManager(=Catalog) + MaterialDropField 와 같은 오브젝트에 둔다.
    /// </summary>
    [RequireComponent(typeof(GridManager))]
    [RequireComponent(typeof(MaterialDropField))]
    public class MaterialDepot : NetworkBehaviour
    {
        // 배송 지점은 배경 프리팹의 "Spot_DeliveryZone" 마커가 정한다(다른 Spot_ 마커들과 같은 체계).
        // 마커를 끌어서 옮기면 착지 지점도 실시간으로 따라간다.
        // 마커가 없을 때만 쓰는 폴백 — 그리드 원점 기준 상대 위치라 맵이 옮겨가도 따라간다.
        public const string kSpotName = "Spot_DeliveryZone";
        private static readonly Vector3 kFallbackOffset = new Vector3(-3.5f, 0f, 4f);

        private GridManager m_Grid;
        private MaterialDropField m_Drop;
        private GameLoopManager m_Loop;
        private GameObject m_Marker;
        private GameObject m_MarkerB;   // 2vs2: 팀B 배송 지점 표시(분할벽 점대칭)

        // UI는 별도 어셈블리(UIManager)라 직접 못 부름 → 이벤트로 알리고 Assembly-CSharp 드라이버가 HUD 연결.
        public static event System.Action<MaterialDepot> Spawned;
        public static event System.Action<MaterialDepot> Despawned;
        /// <summary>주문 목록이 바뀜(맵 로드/교체) — HUD 다시 그리기.</summary>
        public static event System.Action<MaterialDepot> MaterialsChanged;

        /// <summary>주문 누적이 바뀜(주문 성공/라운드 리셋) — HUD 잔량 배지 갱신.</summary>
        public static event System.Action<MaterialDepot> OrdersChanged;

        // ── 주문 수량 제한(MaterialDef.MaxSpawnCount) ─────────────────────
        // 라운드 누적 주문 수(서버 권위). NetworkList로 전 클라 복제 → HUD가 잔량을 그린다.
        // 붕괴/철거된 재료는 바닥에 되돌아오므로(GridNetwork.RemoveCollapsed → ServerDrop)
        // 한도를 다 써도 회수로 계속 지을 수 있다 — 소프트락 없음.
        public struct OrderCount : INetworkSerializable, System.IEquatable<OrderCount>
        {
            public int Team; public int MaterialId; public int Ordered;
            public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
            {
                s.SerializeValue(ref Team);
                s.SerializeValue(ref MaterialId);
                s.SerializeValue(ref Ordered);
            }
            public bool Equals(OrderCount o) => Team == o.Team && MaterialId == o.MaterialId && Ordered == o.Ordered;
        }
        private readonly NetworkList<OrderCount> m_Ordered = new();

        // 한도는 팀 단위(협동/솔로 = 팀 0 공유). 2vs2에서 전역으로 세면 한 팀이 상대 몫까지 소진할 수 있다.
        private int TeamOf(ulong clientId) => (m_Loop != null && m_Loop.IsVersus) ? m_Loop.GetTeam(clientId) : 0;
        private int LocalTeam => (m_Loop != null && m_Loop.IsVersus) ? m_Loop.LocalTeam : 0;

        private int OrderedFor(int team, int materialId)
        {
            for (int i = 0; i < m_Ordered.Count; i++)
                if (m_Ordered[i].Team == team && m_Ordered[i].MaterialId == materialId)
                    return m_Ordered[i].Ordered;
            return 0;
        }

        /// <summary>HUD용: 로컬 팀 기준 남은 주문 가능 수. 무제한 재료면 -1.</summary>
        public int RemainingFor(int materialId)
        {
            var def = Catalog != null ? Catalog.GetById(materialId) : null;
            if (def == null || def.MaxSpawnCount < 0) return -1;
            return Mathf.Max(0, def.MaxSpawnCount - OrderedFor(LocalTeam, materialId));
        }

        private void ServerCountOrder(int team, int materialId)
        {
            for (int i = 0; i < m_Ordered.Count; i++)
                if (m_Ordered[i].Team == team && m_Ordered[i].MaterialId == materialId)
                {
                    var e = m_Ordered[i]; e.Ordered++; m_Ordered[i] = e;
                    return;
                }
            m_Ordered.Add(new OrderCount { Team = team, MaterialId = materialId, Ordered = 1 });
        }

        /// <summary>새 라운드 — 주문 누적 초기화. GameLoopManager.Restart가 호출(서버).</summary>
        public void ServerResetOrders()
        {
            for (int i = m_Ordered.Count - 1; i >= 0; i--) m_Ordered.RemoveAt(i);
        }

        private void OnOrderedChanged(NetworkListEvent<OrderCount> _) => OrdersChanged?.Invoke(this);

        public MaterialCatalog Catalog => m_Grid != null ? m_Grid.Catalog : null;

        // 맵별 주문 가능 목록(MapDef.AvailableMaterials). 비면 카탈로그 전체를 쓴다 — 카탈로그는 전역 그대로.
        private System.Collections.Generic.List<MaterialDef> m_Orderable;

        /// <summary>이 맵에서 주문할 수 있는 재료 목록(비어 있으면 카탈로그 전체).</summary>
        public System.Collections.Generic.IReadOnlyList<MaterialDef> OrderableMaterials =>
            (m_Orderable != null && m_Orderable.Count > 0)
                ? (System.Collections.Generic.IReadOnlyList<MaterialDef>)m_Orderable
                : Catalog != null ? Catalog.Materials : System.Array.Empty<MaterialDef>();

        /// <summary>맵이 정한 주문 목록 적용 — MapLoader가 배경 스폰 때 모든 클라에서 호출.</summary>
        public void SetAvailableMaterials(System.Collections.Generic.IReadOnlyList<MaterialDef> materials)
        {
            m_Orderable = new System.Collections.Generic.List<MaterialDef>();
            if (materials != null)
                foreach (var m in materials) if (m != null) m_Orderable.Add(m);
            MaterialsChanged?.Invoke(this);
        }

        /// <summary>서버 검증용: 이 맵에서 주문할 수 있는 재료인가.</summary>
        private bool IsOrderable(int materialId)
        {
            foreach (var m in OrderableMaterials) if (m != null && m.Id == materialId) return true;
            return false;
        }

        private void Awake()
        {
            m_Grid = GetComponent<GridManager>();
            m_Drop = GetComponent<MaterialDropField>();
            m_Loop = GetComponent<GameLoopManager>();
        }

        // 노란 바닥 표시를 현재 배송 지점에 맞춘다(스폰·마커 이동 공용).
        private void SyncMarker()
        {
            if (m_Marker == null) return;

            // 남산 맵: 배송 지점이 없다(케이블카 하차장이 대신) — 노란 판 숨김.
            var cable = GetComponent<CableCarNetwork>();
            bool cableActive = cable != null && cable.Active;
            if (m_Marker.activeSelf == cableActive) m_Marker.SetActive(!cableActive);
            if (cableActive) return;

            var z = ZonePos;
            m_Marker.transform.position = new Vector3(z.x, z.y + 0.05f, z.z);

            // 2vs2: 팀B의 실제 배송 지점(OrderRpc의 점대칭 미러와 동일 계산)에도 노란 판을 그린다 —
            // 안 그리면 팀B는 재료가 어디 떨어지는지 표시가 없다.
            bool versus = m_Loop != null && m_Loop.IsVersus && m_Grid != null;
            if (versus)
            {
                if (m_MarkerB == null) m_MarkerB = MakeMarker("~DeliveryZone_B");
                var pivot = VersusBackground.MirrorPivot(m_Grid.ZoneSize, m_Grid.EffectiveSize);
                m_MarkerB.transform.position = new Vector3(2f * pivot.x - z.x, z.y + 0.05f, 2f * pivot.z - z.z);
            }
            else if (m_MarkerB != null) { Destroy(m_MarkerB); m_MarkerB = null; }
        }

        private Transform m_Spot;
        private float m_NextSpotFind;

        private Vector3 ZonePos => m_Spot != null ? m_Spot.position : GridContract.Origin + kFallbackOffset;

        private void Update()
        {
            // 배경 프리팹이 런타임에 스폰되므로 늦게 생겨도 잡히게 0.5초 간격 재탐색(찾으면 중단)
            if (m_Spot == null && Time.time >= m_NextSpotFind)
            {
                m_NextSpotFind = Time.time + 0.5f;
                var p = GameObject.Find(kSpotName);
                if (p != null)
                {
                    m_Spot = p.transform;
                    Debug.Log($"[Depot] {kSpotName} 연결 — 배송 지점 = {m_Spot.position}");
                }
            }
            // 마커를 끌면서 조정 가능하게 라이브 추적. 마커가 없어도 호출 — 남산(케이블카) 맵에서 노란 판 숨김 처리.
            SyncMarker();
        }

        public override void OnNetworkSpawn()
        {
            // 배송 구역 바닥 마커(로컬 비주얼, 모든 클라) — 어디서 줍는지 보이게
            m_Marker = MakeMarker("~DeliveryZone");
            SyncMarker();

            m_Ordered.OnListChanged += OnOrderedChanged;   // 잔량 복제 → HUD 배지 갱신
            Spawned?.Invoke(this);   // 드라이버가 주문 HUD 띄움
        }

        private static Material s_DecalMat;   // 배송 데칼 공유 머티리얼(전 맵 공통)

        private static GameObject MakeMarker(string name)
        {
            // Resources/DeliveryZoneDecal 텍스처가 있으면 '택배 데칼' 쿼드 — 없으면 기존 노란 판 폴백.
            var decal = Resources.Load<Texture2D>("DeliveryZoneDecal");
            if (decal != null)
            {
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = name;
                quad.transform.rotation = Quaternion.Euler(90f, 0f, 0f);   // 바닥에 눕힘
                quad.transform.localScale = new Vector3(3.4f, 3.4f, 1f);
                var qcol = quad.GetComponent<Collider>();
                if (qcol != null) Destroy(qcol);
                if (s_DecalMat == null)
                {
                    var sh = Shader.Find("Universal Render Pipeline/Unlit");
                    if (sh == null) sh = Shader.Find("Universal Render Pipeline/Lit");
                    if (sh != null)
                    {
                        // 투명 서피스 — 데칼 PNG의 알파(배경 투명)가 그대로 살게
                        s_DecalMat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
                        s_DecalMat.SetTexture("_BaseMap", decal);
                        s_DecalMat.SetFloat("_Surface", 1f);
                        s_DecalMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                        s_DecalMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                        s_DecalMat.SetInt("_ZWrite", 0);
                        s_DecalMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                        s_DecalMat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                    }
                }
                if (s_DecalMat != null) quad.GetComponent<Renderer>().sharedMaterial = s_DecalMat;
                return quad;
            }

            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.localScale = new Vector3(3f, 0.1f, 3f);
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            SetColor(go, new Color(0.95f, 0.8f, 0.2f));
            return go;
        }

        public override void OnNetworkDespawn()
        {
            m_Ordered.OnListChanged -= OnOrderedChanged;
            Despawned?.Invoke(this);   // 드라이버가 주문 HUD 숨김
            if (m_Marker != null) Destroy(m_Marker);
            if (m_MarkerB != null) Destroy(m_MarkerB);
        }

        public void RequestOrder(int materialId) => OrderRpc(materialId);

        [Rpc(SendTo.Server)]
        private void OrderRpc(int materialId, RpcParams rpc = default)
        {
            if (m_Drop == null) return;

            // 주문 해킹: 차단당한 팀은 새 재료를 못 시킨다(서버 권위 검사).
            var loop = GetComponent<GameLoopManager>();
            var items = GetComponent<ItemNetwork>();
            if (loop != null && items != null && loop.IsVersus)
            {
                int team = loop.GetTeam(rpc.Receive.SenderClientId);
                if (items.IsOrderBlocked(team))
                {
                    Debug.Log($"[Depot] 주문 차단됨(해킹) — 팀{(team == 1 ? "B" : "A")}");
                    return;
                }
            }

            var cat = m_Grid != null ? m_Grid.Catalog : null;
            if (cat == null || cat.GetById(materialId) == null)
            {
                // 맵의 AvailableMaterials가 전역 카탈로그에 없는 재료를 팔면 여기서 걸린다 — 조용히 죽지 말고 알린다.
                Debug.LogWarning($"[Depot] 전역 MaterialCatalog에 없는 재료 주문: id {materialId} — 맵의 Available Materials와 카탈로그를 맞추세요.");
                return;
            }
            if (!IsOrderable(materialId))   // 이 맵에서 안 파는 재료(조작된 요청 포함)
            {
                Debug.LogWarning($"[Depot] 이 맵에서 주문할 수 없는 재료: id {materialId}");
                return;
            }

            // 주문 한도(MaxSpawnCount, 라운드 누적): 초과면 거절 + 주문자에게만 안내.
            // HUD가 잔량 0에서 카드를 잠그지만, 동시 주문 레이스로 새는 요청은 여기서 막힌다.
            var def = cat.GetById(materialId);
            int orderTeam = TeamOf(rpc.Receive.SenderClientId);
            if (def.MaxSpawnCount >= 0 && OrderedFor(orderTeam, materialId) >= def.MaxSpawnCount)
            {
                OrderDeniedRpc(RpcTarget.Single(rpc.Receive.SenderClientId, RpcTargetUse.Temp));
                return;
            }
            ServerCountOrder(orderTeam, materialId);

            // 남산 맵: 하늘 배송 대신 케이블카가 나른다 — 검증은 위에서 끝났으니 대기열로 넘기고 끝.
            var cable = GetComponent<CableCarNetwork>();
            if (cable != null && cable.Active)
            {
                cable.ServerEnqueue(materialId);
                return;
            }

            // 배송 비행: 하늘 저편(랜덤 방향)에서 포물선으로 날아와 배송 구역에 착지.
            // ServerThrow의 던지기 비행(포물선+텀블 회전+착지음)을 그대로 재활용.
            var zone = ZonePos;   // 마커 위치(높이 포함)

            // 2vs2: 배송 마커는 팀A 쪽에 하나뿐 — 팀B 주문은 분할벽 중심 점대칭 지점(자기 구역)에 배송한다.
            // 안 하면 팀B 재료가 벽 너머 팀A 구역에 떨어져 주울 수 없다.
            if (loop != null && loop.IsVersus && m_Grid != null && loop.GetTeam(rpc.Receive.SenderClientId) == 1)
            {
                var pivot = VersusBackground.MirrorPivot(m_Grid.ZoneSize, m_Grid.EffectiveSize);
                zone = new Vector3(2f * pivot.x - zone.x, zone.y, 2f * pivot.z - zone.z);
            }
            Debug.Log($"[Depot] 배송 지점 = {zone} (출처: {(m_Spot != null ? kSpotName : "마커 없음 — 그리드 기준 폴백")})");
            var to = new Vector3(
                zone.x + Random.Range(-1.3f, 1.3f),
                zone.y,
                zone.z + Random.Range(-1.3f, 1.3f));
            float ang = Random.Range(0f, Mathf.PI * 2f);
            var from = to + new Vector3(Mathf.Cos(ang) * 12f, 6f, Mathf.Sin(ang) * 12f);
            m_Drop.ServerDeliver(materialId, from, to);   // 배송 지점 높이 그대로 착지
        }

        [Rpc(SendTo.SpecifiedInParams)]
        private void OrderDeniedRpc(RpcParams rpc = default)
        {
            var po = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClient?.PlayerObject : null;
            Vector3 pos = po != null ? po.transform.position + Vector3.up * 1.6f
                                     : ZonePos + Vector3.up * 1f;
            GridJuice.WorldToast(pos, "품절! 더는 주문할 수 없어요", new Color(0.95f, 0.45f, 0.15f));
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
