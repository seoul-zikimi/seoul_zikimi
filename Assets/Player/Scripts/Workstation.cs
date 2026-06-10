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
        public ProcessType Tool => m_Tool;

        private void Start()
        {
            var r = GetComponentInChildren<Renderer>();
            if (r == null) return;

            Color c = m_Tool == ProcessType.Painted ? new Color(0.30f, 0.85f, 0.40f)
                                                    : new Color(0.35f, 0.60f, 1.00f);
            var mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            mpb.SetColor(Shader.PropertyToID("_BaseColor"), c);
            mpb.SetColor(Shader.PropertyToID("_Color"), c);
            r.SetPropertyBlock(mpb);
        }
    }
}
