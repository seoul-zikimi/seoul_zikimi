using Unity.Cinemachine;
using UnityEngine;

namespace Player
{
    /// <summary>
    /// 시네머신용 FOV 펀치. CinemachineBrain이 매 프레임 vcam 렌즈를 Camera.main에 덮어쓰므로
    /// Camera.main의 fov를 건드리는 기존 CameraFovPunch는 무효 → vcam 렌즈 자체를 펀치한다.
    /// PlayerUnit(owner)이 부착 + GridJuice.FovPunchHandler로 등록.
    /// </summary>
    public class CinemachineFovPunch : MonoBehaviour
    {
        const float kDecay = 7f;    // 복귀 속도(작을수록 여운 김)
        const float kMax = 10f;

        CinemachineCamera m_Cam;
        float m_Base, m_Punch;

        void Awake()
        {
            m_Cam = GetComponent<CinemachineCamera>();
            if (m_Cam != null) m_Base = m_Cam.Lens.FieldOfView;
        }

        public void Add(float amount)
        {
            if (m_Cam == null) return;
            if (Mathf.Approximately(m_Punch, 0f)) m_Base = m_Cam.Lens.FieldOfView;   // 유휴 fov 기준(줌 등과 합성)
            m_Punch = Mathf.Clamp(m_Punch + amount, -kMax, kMax);
        }

        void LateUpdate()
        {
            if (m_Cam == null || Mathf.Approximately(m_Punch, 0f)) return;
            m_Punch *= Mathf.Exp(-kDecay * Time.deltaTime);
            if (Mathf.Abs(m_Punch) < 0.02f) m_Punch = 0f;

            var lens = m_Cam.Lens;
            lens.FieldOfView = m_Base + m_Punch;
            m_Cam.Lens = lens;
        }
    }
}
