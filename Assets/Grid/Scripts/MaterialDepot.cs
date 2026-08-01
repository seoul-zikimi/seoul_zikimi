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
        // 배송 지점은 맵이 정한다: DeliveryPoint 오브젝트(끌어서 조정, 실시간 반영) 또는 Spot_DeliveryZone 마커.
        // 둘 다 없을 때만 쓰는 폴백 — 그리드 원점 기준 상대 위치라 맵이 옮겨가도 따라간다.
        // (인스펙터에 월드 좌표를 직접 입력하던 예전 방식은 제거됨)
        private static readonly Vector3 kFallbackOffset = new Vector3(-3.5f, 0f, 4f);
        private Vector3 m_DeliveryZone;
        private bool m_ZoneSetByMap;

        private GridManager m_Grid;
        private MaterialDropField m_Drop;
        private GameObject m_Marker;

        // UI는 별도 어셈블리(UIManager)라 직접 못 부름 → 이벤트로 알리고 Assembly-CSharp 드라이버가 HUD 연결.
        public static event System.Action<MaterialDepot> Spawned;
        public static event System.Action<MaterialDepot> Despawned;
        public MaterialCatalog Catalog => m_Grid != null ? m_Grid.Catalog : null;

        private void Awake()
        {
            m_Grid = GetComponent<GridManager>();
            m_Drop = GetComponent<MaterialDropField>();
        }

        /// <summary>맵 마커(Spot_DeliveryZone)로 배송 구역 이동 — MapLoader가 배경 스폰 시 호출. 비주얼 마커도 갱신.</summary>
        public void SetDeliveryZone(Vector3 worldPos)
        {
            m_DeliveryZone = worldPos;
            m_ZoneSetByMap = true;
            if (m_Point != null)
                Debug.LogWarning("[Depot] DeliveryPoint와 Spot_DeliveryZone이 둘 다 있음 — DeliveryPoint가 우선됩니다. 배경 프리팹에서 하나만 두세요.");
            SyncMarker();
        }

        // 노란 바닥 표시를 현재 배송 지점에 맞춘다(스폰·마커 적용·포인트 이동 공용).
        private void SyncMarker()
        {
            if (m_Marker == null) return;
            var z = ZonePos;
            m_Marker.transform.position = new Vector3(z.x, z.y + 0.05f, z.z);
        }

        // 우선순위: DeliveryPoint 오브젝트(권장 — 끌어서 조정) > Spot_DeliveryZone 마커 > 그리드 기준 폴백.
        private Transform m_Point;
        private float m_NextPointFind;

        private Vector3 ZonePos =>
            m_Point != null ? m_Point.position :
            m_ZoneSetByMap ? m_DeliveryZone :
                             GridContract.Origin + kFallbackOffset;

        private void Update()
        {
            // 배경 프리팹에서 늦게 생겨도 잡히게 0.5초 간격 재탐색(찾으면 중단)
            if (m_Point == null && Time.time >= m_NextPointFind)
            {
                m_NextPointFind = Time.time + 0.5f;
                var p = GameObject.Find("DeliveryPoint");
                if (p != null)
                {
                    m_Point = p.transform;
                    Debug.Log($"[Depot] DeliveryPoint 연결 — 배송 구역 = {m_Point.position}");
                }
            }
            if (m_Point != null) SyncMarker();   // 에디터에서 끌면서 조정 가능하게 라이브 추적
        }

        public override void OnNetworkSpawn()
        {
            // 배송 구역 바닥 마커(로컬 비주얼, 모든 클라) — 어디서 줍는지 보이게
            m_Marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            m_Marker.name = "~DeliveryZone";
            m_Marker.transform.localScale = new Vector3(3f, 0.1f, 3f);
            SyncMarker();
            var col = m_Marker.GetComponent<Collider>();
            if (col != null) Destroy(col);
            SetColor(m_Marker, new Color(0.95f, 0.8f, 0.2f));

            Spawned?.Invoke(this);   // 드라이버가 주문 HUD 띄움
        }

        public override void OnNetworkDespawn()
        {
            Despawned?.Invoke(this);   // 드라이버가 주문 HUD 숨김
            if (m_Marker != null) Destroy(m_Marker);
        }

        public void RequestOrder(int materialId) => OrderRpc(materialId);

        [Rpc(SendTo.Server)]
        private void OrderRpc(int materialId)
        {
            if (m_Drop == null) return;
            var cat = m_Grid != null ? m_Grid.Catalog : null;
            if (cat == null || cat.GetById(materialId) == null) return;

            // 배송 비행: 하늘 저편(랜덤 방향)에서 포물선으로 날아와 배송 구역에 착지.
            // ServerThrow의 던지기 비행(포물선+텀블 회전+착지음)을 그대로 재활용.
            var zone = ZonePos;   // DeliveryPoint 오브젝트 있으면 그 위치(높이 포함)
            Debug.Log($"[Depot] 배송 지점 = {zone} (출처: {(m_Point != null ? "DeliveryPoint 오브젝트" : "Spot_DeliveryZone/인스펙터 좌표")})");
            var to = new Vector3(
                zone.x + Random.Range(-1.3f, 1.3f),
                zone.y,
                zone.z + Random.Range(-1.3f, 1.3f));
            float ang = Random.Range(0f, Mathf.PI * 2f);
            var from = to + new Vector3(Mathf.Cos(ang) * 12f, 6f, Mathf.Sin(ang) * 12f);
            m_Drop.ServerDeliver(materialId, from, to);   // 배송 지점 높이 그대로 착지
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
