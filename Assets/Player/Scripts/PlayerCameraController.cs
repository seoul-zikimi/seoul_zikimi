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

        float m_VertAngle = 45f;
        float m_Distance  = 10f;

        void Awake()
        {
            m_CameraArm = transform.parent;                          // 바로 위 = CameraArm
            m_Input     = GetComponentInParent<PlayerInputHandler>(); // 두 단계 위 = PlayerUnit
        }

        void Update()
        {
            if (m_Input == null || !m_Input.enabled) return;

            Vector2 rot  = m_Input.CameraRotate;
            float   zoom = m_Input.CameraZoom;

            // ── 수평 회전: CameraArm을 월드 y축으로 돌림 ──────────
            m_CameraArm.Rotate(Vector3.up, rot.x * m_RotateSpeed, Space.World);

            // ── 수직 각도: 위아래 마우스 드래그 ──────────────────
            m_VertAngle = Mathf.Clamp(
                m_VertAngle - rot.y * m_RotateSpeed,
                m_VertMin, m_VertMax
            );

            // ── 줌 ────────────────────────────────────────────────
            m_Distance = Mathf.Clamp(
                m_Distance - zoom * m_ZoomSpeed,
                m_DistMin, m_DistMax
            );

            // ── 구면좌표 → 로컬 위치 ──────────────────────────────
            // CameraArm이 수평 처리 → x=0, y/z만 계산
            float rad = m_VertAngle * Mathf.Deg2Rad;
            transform.localPosition = new Vector3(
                0f,
                 m_Distance * Mathf.Sin(rad),   // 높이
                -m_Distance * Mathf.Cos(rad)    // 앞뒤 (음수 = 뒤)
            );

            // ── 피벗 바라보기 ─────────────────────────────────────
            transform.LookAt(m_CameraArm.position);
        }
    }
}