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
        // 슈팅겜식 어깨 너머 시점: 카메라는 발밑이 아니라 '머리 위 피벗'을 중심으로 돈다 → 피치가 곧 시선 각도(수평·올려다보기 가능).
        [SerializeField] float m_LookHeight  = 2.8f;   // 피벗 높이. 캐릭터 콜라이더 꼭대기(2.0)보다 높아야 조준선이 머리에 안 가린다. 올릴수록 캐릭터가 화면 아래로
        [SerializeField] float m_ShoulderOffset = 0.7f; // 카메라를 오른쪽으로 평행 이동 — 가까운 시점에서 캐릭터 등이 조준선 앞을 가리지 않게. 키울수록 캐릭터가 화면 왼쪽으로
        [SerializeField] float m_StartPitch    = 20f;
        [SerializeField] float m_StartDistance = 5.5f;  // 시작 거리(휠로 조절)
        [SerializeField] float m_FreeLookReturn = 10f; // 우클릭 놓았을 때 원래 시점으로 돌아오는 빠르기

        // 마우스 감도(설정 팝업 슬라이더와 공유). PlayerPrefs "MouseSensitivity"(0~1) → 0.05~1.5배 곱.
        // 조준선 시점은 마우스가 항상 시점을 돌려서, 우클릭 드래그 시절(0.25~2.5배)보다 전체를 낮추고 바닥도 더 내렸다.
        // 모바일(터치 드래그)은 조작이 그대로라 예전 범위를 유지한다.
        static float SensFrom01(float v) => MobileControlsHUD.ShouldUseMobileUI ? Mathf.Lerp(0.25f, 2.5f, v) : Mathf.Lerp(0.05f, 1.5f, v);
        static float s_SensMul = -1f;
        public static float SensitivityMul
        {
            get { if (s_SensMul < 0f) s_SensMul = SensFrom01(PlayerPrefs.GetFloat("MouseSensitivity", 0.5f)); return s_SensMul; }
        }
        public static void SetSensitivity01(float v)
        {
            v = Mathf.Clamp01(v);
            PlayerPrefs.SetFloat("MouseSensitivity", v);
            s_SensMul = SensFrom01(v);
        }

        Transform          m_CameraArm;
        PlayerInputHandler m_Input;
        CameraOrbit        m_Orbit;   // 피치/줌 공유 로직(정답 패널 카메라와 동일 컴포넌트)
        float m_FreeYaw;      // 둘러보기 중 카메라만 돌아간 각(팔 기준). 0 = 평소
        float m_HomePitch;    // 둘러보기 시작 때 피치 — 놓으면 여기로 복귀
        bool  m_FreeLooking, m_Returning;

        void Awake()
        {
            m_CameraArm = transform.parent;                          // 바로 위 = CameraArm
            m_Input     = GetComponentInParent<PlayerInputHandler>(); // 두 단계 위 = PlayerUnit
            m_Orbit = new CameraOrbit
            {
                RotateSpeed = m_RotateSpeed, ZoomSpeed = m_ZoomSpeed,
                PitchMin = m_VertMin, PitchMax = m_VertMax,
                DistMin  = m_DistMin, DistMax  = m_DistMax,
                Pitch = m_StartPitch,
                Distance = m_StartDistance,
            };
        }

        void Update()
        {
            if (m_Orbit == null) Awake();   // 플레이 중 스크립트 핫 리로드로 비직렬화 필드가 날아간 경우 복구(NRE 스팸 방지)
            if (AnswerPanelFocus.Active) return;   // 커서가 정답 패널 위면 양보(정답 카메라가 입력 소비)
            if (m_Input == null || !m_Input.enabled) return;

            Vector2 rot  = m_Input.ConsumeCameraRotate();
            float   zoom = m_Input.ConsumeCameraZoom();

            // 커서가 UI(주문 패널 등) 위면 휠은 그 UI의 스크롤 몫 — 카메라 줌이 같이 먹지 않게
            var es = UnityEngine.EventSystems.EventSystem.current;
            if (es != null && es.IsPointerOverGameObject()) zoom = 0f;

            // ── 수평 회전: yaw는 CameraArm에 그대로(이동이 카메라 상대라 보존) ──
            float sens = SensitivityMul;
            // 우클릭 홀드 = 둘러보기: 팔(이동·캐릭터 기준)은 그대로 두고 카메라만 돈다. 놓으면 원래 시점으로 복귀.
            bool free = m_Input.FreeLookHeld;
            if (free && !m_FreeLooking) { if (!m_Returning) m_HomePitch = m_Orbit.Pitch; m_Returning = false; }
            if (!free && m_FreeLooking) { m_FreeYaw = Mathf.DeltaAngle(0f, m_FreeYaw); m_Returning = true; }   // 한 바퀴 넘게 돌렸어도 짧은 쪽으로 복귀
            m_FreeLooking = free;

            if (free) m_FreeYaw += rot.x * m_RotateSpeed * sens;
            else      m_CameraArm.Rotate(Vector3.up, rot.x * m_RotateSpeed * sens, Space.World);

            // ── 피치/줌만 공유 오빗으로(yaw=0으로 적분 → 팔이 담당) ──
            m_Orbit.Integrate(new Vector2(0f, rot.y * sens), zoom);
            if (m_Returning)
            {
                float k = 1f - Mathf.Exp(-m_FreeLookReturn * Time.deltaTime);
                m_FreeYaw     = Mathf.Lerp(m_FreeYaw, 0f, k);
                m_Orbit.Pitch = Mathf.Lerp(m_Orbit.Pitch, m_HomePitch, k);
                if (Mathf.Abs(m_FreeYaw) < 0.1f && Mathf.Abs(m_Orbit.Pitch - m_HomePitch) < 0.1f)
                { m_FreeYaw = 0f; m_Orbit.Pitch = m_HomePitch; m_Returning = false; }
            }
            // 피벗(머리 위)을 중심으로 궤도 → 피벗을 바라본 뒤, 회전은 그대로 두고 어깨 쪽으로만 평행 이동(조준 방향 = 이동 방향 유지).
            Vector3 pivot = Vector3.up * m_LookHeight;
            Vector3 local = pivot + Quaternion.Euler(0f, m_FreeYaw, 0f) * m_Orbit.LocalOffset();
            local.y = Mathf.Max(local.y, 0.3f);   // 올려다볼 때(음수 피치) 카메라가 발밑 땅속으로 들어가지 않게
            transform.localPosition = local;
            transform.LookAt(m_CameraArm.position + pivot);
            transform.position += transform.right * m_ShoulderOffset;
        }
    }
}
