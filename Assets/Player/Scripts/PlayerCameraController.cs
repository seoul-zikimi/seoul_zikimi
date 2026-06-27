using UnityEngine;

namespace Player
{
    public class PlayerCameraController : MonoBehaviour
    {
        [SerializeField] float m_RotateSpeed = 0.3f;   // 도/픽셀
        [SerializeField] float m_ZoomSpeed   = 0.01f;  // 스크롤 1노치=120 → 1.2유닛
        [SerializeField] float m_VertMin     = 15f;
        [SerializeField] float m_VertMax     = 80f;
        [SerializeField] float m_DistMin     = 3f;
        [SerializeField] float m_DistMax     = 20f;

        Transform          m_CameraArm;
        PlayerInputHandler m_Input;
        CameraOrbit        m_Orbit;   // 피치/줌 공유 로직(정답 패널 카메라와 동일 컴포넌트)

        void Awake()
        {
            m_CameraArm = transform.parent;                          // 바로 위 = CameraArm
            m_Input     = GetComponentInParent<PlayerInputHandler>(); // 두 단계 위 = PlayerUnit
            m_Orbit = new CameraOrbit
            {
                RotateSpeed = m_RotateSpeed, ZoomSpeed = m_ZoomSpeed,
                PitchMin = m_VertMin, PitchMax = m_VertMax,
                DistMin  = m_DistMin, DistMax  = m_DistMax,
                Pitch = 45f,      // 30° → 45°: 더 위에서 내려다봄
                Distance = 12f,   // 10f → 12f: 조금 더 멀리
            };
        }

        void Update()
        {
            if (AnswerPanelFocus.Active) return;   // 커서가 정답 패널 위면 양보(정답 카메라가 입력 소비)
            if (m_Input == null || !m_Input.enabled) return;

            Vector2 rot  = m_Input.CameraRotate;
            float   zoom = m_Input.CameraZoom;

            // ── 수평 회전: yaw는 CameraArm에 그대로(이동이 카메라 상대라 보존) ──
            m_CameraArm.Rotate(Vector3.up, rot.x * m_RotateSpeed, Space.World);

            // ── 피치/줌만 공유 오빗으로(yaw=0으로 적분 → 팔이 담당) ──
            m_Orbit.Integrate(new Vector2(0f, rot.y), zoom);
            transform.localPosition = m_Orbit.LocalOffset();
            transform.LookAt(m_CameraArm.position + Vector3.up * 1.0f);
        }
    }
}
