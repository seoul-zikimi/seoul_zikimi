using UnityEngine;

namespace Player
{
    /// <summary>
    /// 가리킨 집기 대상(바닥 픽업·도구함)에 '진짜 테두리'를 띄운다 — 색 틴트가 아니라,
    /// 메쉬를 법선 방향으로 살짝 키운 인버티드 헐(앞면 컬링)을 자식으로 깔아 실루엣만 그린다.
    /// PlayerCarry가 대상 오브젝트에 AddComponent 해서 SetOutline(true/false)로 토글.
    /// </summary>
    public class OutlineHighlight : MonoBehaviour
    {
        private static Material s_Mat;          // 모든 인스턴스 공유(색·두께 고정) — 누수 없음
        private GameObject[] m_Outlines;
        private bool m_On;

        public void SetOutline(bool on)
        {
            if (on == m_On) return;
            m_On = on;
            if (on) Build(); else Clear();
        }

        private void Build()
        {
            if (s_Mat == null)
            {
                var sh = Shader.Find("Hidden/PickupOutline");
                if (sh == null) return;
                s_Mat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            }

            var filters = GetComponentsInChildren<MeshFilter>();
            m_Outlines = new GameObject[filters.Length];
            for (int i = 0; i < filters.Length; i++)
            {
                var mesh = filters[i].sharedMesh;
                if (mesh == null) continue;
                // 모델에 딸려온 납작한 판때기(평소 투명)까지 그리면 거대한 초록 사각형이 뜬다
                // — 부피 없는 평면 메시와 꺼진 렌더러는 실루엣에서 제외.
                var size = mesh.bounds.size;
                if (Mathf.Min(size.x, Mathf.Min(size.y, size.z)) < 0.01f) continue;
                var srcR = filters[i].GetComponent<MeshRenderer>();
                if (srcR == null || !srcR.enabled) continue;
                var go = new GameObject("~Outline");
                go.transform.SetParent(filters[i].transform, false);   // 부모 메쉬에 정확히 겹침
                go.AddComponent<MeshFilter>().sharedMesh = filters[i].sharedMesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = s_Mat;
                m_Outlines[i] = go;
            }
        }

        private void Clear()
        {
            if (m_Outlines == null) return;
            foreach (var o in m_Outlines) if (o != null) Destroy(o);
            m_Outlines = null;
        }

        private void OnDisable() => Clear();   // 대상이 사라지거나 비활성화돼도 테두리 정리
    }
}
