using Unity.Netcode;
using UnityEngine;

namespace GridSystem
{
    /// <summary>
    /// 재료 보급소(M1). 리썰컴퍼니식 '주문 → 재고 → 집어들기'.
    /// 서버 권위: 재고는 NetworkList&lt;int&gt;(인덱스=materialId)로 복제.
    /// '주문' 버튼은 재고 +1, 플레이어가 재료를 들면(PlayerCarry) TryTake로 재고 -1.
    /// GridManager(=Catalog) 와 같은 오브젝트에 둔다.
    /// </summary>
    [RequireComponent(typeof(GridManager))]
    public class MaterialDepot : NetworkBehaviour
    {
        // 인덱스 = materialId, 값 = 현재 재고 수량. (서버만 변경)
        private readonly NetworkList<int> m_Stock = new();

        private GridManager m_Grid;
        private GUIStyle m_Style;

        private void Awake() => m_Grid = GetComponent<GridManager>();

        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;
            var cat = m_Grid != null ? m_Grid.Catalog : null;
            if (cat == null) return;

            // materialId 가 듬성듬성할 수 있으니 (maxId+1) 칸을 0으로 채운다.
            int maxId = -1;
            foreach (var d in cat.Materials)
                if (d != null && d.Id > maxId) maxId = d.Id;
            for (int i = 0; i <= maxId; i++) m_Stock.Add(0);
        }

        /// <summary>해당 재료의 현재 재고(없거나 범위 밖이면 0). 클라도 호출 가능(복제됨).</summary>
        public int StockOf(int materialId)
        {
            if (materialId < 0 || materialId >= m_Stock.Count) return 0;
            return m_Stock[materialId];
        }

        /// <summary>재고가 있으면 1 소비하고 true. 서버가 권위적으로 차감(낙관적).</summary>
        public bool TryTake(int materialId)
        {
            if (StockOf(materialId) <= 0) return false;
            TakeRpc(materialId);
            return true;
        }

        /// <summary>게임 재시작용: 모든 재고를 0으로(서버).</summary>
        public void ServerReset()
        {
            if (!IsServer) return;
            for (int i = 0; i < m_Stock.Count; i++) m_Stock[i] = 0;
        }

        public void RequestOrder(int materialId) => OrderRpc(materialId);

        [Rpc(SendTo.Server)]
        private void OrderRpc(int materialId)
        {
            if (materialId < 0 || materialId >= m_Stock.Count) return;
            m_Stock[materialId] += 1;
        }

        [Rpc(SendTo.Server)]
        private void TakeRpc(int materialId)
        {
            if (materialId < 0 || materialId >= m_Stock.Count) return;
            if (m_Stock[materialId] > 0) m_Stock[materialId] -= 1;
        }

        // ── 주문 UI (씬에 하나뿐이라 한 번만 그려짐) ─────────────────────────
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
            GUI.Label(new Rect(box.x + 8f, box.y + 6f, w - 16f, 20f), "재료 주문 (재고)", m_Style);

            float y = box.y + 30f;
            foreach (var d in mats)
            {
                if (d == null) continue;
                GUI.Label(new Rect(box.x + 8f, y, 120f, 22f), $"{d.name} x{StockOf(d.Id)}", m_Style);
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
    }
}
