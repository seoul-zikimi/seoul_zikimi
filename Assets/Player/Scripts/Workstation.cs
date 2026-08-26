using GridSystem;
using UnityEngine;

namespace Player
{
    /// <summary>
    /// 도구 작업장(I3b). 플레이어가 근처에서 F를 누르면 이 작업장의 도구를 든다(PlayerCarry).
    /// 망치=고정, 페인트통=페인트. 색으로 구분.
    /// </summary>
    public class Workstation : MonoBehaviour
    {
        [SerializeField] private ProcessType m_Tool = ProcessType.Fixed;
        [SerializeField] private GameObject m_StationModel;        // 있으면 큐브 대신 이 GLB를 외형으로 사용
        [SerializeField] private float m_StationModelScale = 1f;   // GLB 크기 보정(에디터에서 조절)
        [SerializeField] private Vector3 m_StationModelOffset = Vector3.zero;   // GLB 피벗 보정(위치 미세조정)
        [SerializeField] private Vector3 m_StationModelEuler = Vector3.zero;    // GLB 회전 보정(뒤집힘 등)
        public ProcessType Tool => m_Tool;

        private void Start()
        {
            // GLB 모델이 배선돼 있으면: 큐브 렌더러 숨기고 모델을 자식으로 띄운다(콜라이더는 잡기용으로 유지).
            if (m_StationModel != null)
            {
                var own = GetComponent<Renderer>();
                if (own != null) own.enabled = false;
                var model = Instantiate(m_StationModel, transform);
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.Euler(m_StationModelEuler);
                model.transform.localScale = Vector3.one * m_StationModelScale;
                foreach (var col in model.GetComponentsInChildren<Collider>()) Destroy(col);

                // 바닥 스냅: 모델 바운드 최저점을 스테이션 바닥(1×1 큐브 하단)에 맞춘 뒤 수동 오프셋 적용.
                var rends = model.GetComponentsInChildren<Renderer>();
                if (rends.Length > 0)
                {
                    var b = rends[0].bounds;
                    for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
                    float floorY = transform.position.y - 0.5f * transform.lossyScale.y;
                    model.transform.position += Vector3.up * (floorY - b.min.y);
                }
                model.transform.localPosition += m_StationModelOffset;
                return;   // GLB 고유 색 유지(공정색 틴트 생략)
            }

            var r = GetComponentInChildren<Renderer>();
            if (r == null) return;

            Color c = m_Tool switch
            {
                ProcessType.Painted => new Color(0.30f, 0.85f, 0.40f),
                ProcessType.Bucket  => new Color(0.25f, 0.75f, 0.95f),   // 양동이(물) — 하늘색
                _                   => new Color(0.35f, 0.60f, 1.00f),
            };
            var mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            mpb.SetColor(Shader.PropertyToID("_BaseColor"), c);
            mpb.SetColor(Shader.PropertyToID("_Color"), c);
            r.SetPropertyBlock(mpb);
        }
    }
}
