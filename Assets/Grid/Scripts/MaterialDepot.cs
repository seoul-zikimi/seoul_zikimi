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
        [Tooltip("주문한 재료가 배송돼 떨어지는 구역(월드 XZ). 그리드 밖 권장.")]
        [SerializeField] private Vector3 m_DeliveryZone = new Vector3(-3.5f, 0f, 4f);

        private GridManager m_Grid;
        private MaterialDropField m_Drop;
        private GUIStyle m_Style;
        private GameObject m_Marker;

        private void Awake()
        {
            m_Grid = GetComponent<GridManager>();
            m_Drop = GetComponent<MaterialDropField>();
        }

        public override void OnNetworkSpawn()
        {
            // 배송 구역 바닥 마커(로컬 비주얼, 모든 클라) — 어디서 줍는지 보이게
            m_Marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            m_Marker.name = "~DeliveryZone";
            m_Marker.transform.position = new Vector3(m_DeliveryZone.x, 0.05f, m_DeliveryZone.z);
            m_Marker.transform.localScale = new Vector3(3f, 0.1f, 3f);
            var col = m_Marker.GetComponent<Collider>();
            if (col != null) Destroy(col);
            SetColor(m_Marker, new Color(0.95f, 0.8f, 0.2f));
        }

        public override void OnNetworkDespawn()
        {
            if (m_Marker != null) Destroy(m_Marker);
        }

        public void RequestOrder(int materialId) => OrderRpc(materialId);

        [Rpc(SendTo.Server)]
        private void OrderRpc(int materialId)
        {
            if (m_Drop == null) return;
            var cat = m_Grid != null ? m_Grid.Catalog : null;
            if (cat == null || cat.GetById(materialId) == null) return;

            // 배송 구역 위에서 흩뿌려 떨어뜨림(물류창고 더미 느낌). 위에서 낙하.
            var pos = new Vector3(
                m_DeliveryZone.x + Random.Range(-1.3f, 1.3f),
                2.5f,
                m_DeliveryZone.z + Random.Range(-1.3f, 1.3f));
            m_Drop.ServerDrop(materialId, pos);
        }

        // ── 주문 UI ─────────────────────────────────────────────────────────
        private void OnGUI()
        {
            if (!Application.isPlaying || !IsSpawned) return;
            var cat = m_Grid != null ? m_Grid.Catalog : null;
            if (cat == null) return;
            if (m_Style == null)
                m_Style = new GUIStyle(GUI.skin.label) { fontSize = 14, normal = { textColor = Color.white } };

            var mats = cat.Materials;
            const float w = 230f, rowH = 26f, pad = 8f;
            float h = pad * 2 + 22f + mats.Count * rowH;
            var box = new Rect(Screen.width - w - 10f, 10f, w, h);
            DrawBox(box, 0.7f);
            GUI.Label(new Rect(box.x + 8f, box.y + 6f, w - 16f, 20f), "재료 주문 (배송 → Space로 줍기)", m_Style);

            float y = box.y + 30f;
            foreach (var d in mats)
            {
                if (d == null) continue;
                GUI.Label(new Rect(box.x + 8f, y, w - 96f, 22f), d.name, m_Style);
                if (GUI.Button(new Rect(box.x + w - 86f, y, 78f, 22f), "주문"))
                    RequestOrder(d.Id);
                y += rowH;
            }
        }

        private static void DrawBox(Rect r, float a)
        {
            var prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, a);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = prev;
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
