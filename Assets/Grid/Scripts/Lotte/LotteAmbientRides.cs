using UnityEngine;

namespace GridSystem
{
    /// <summary>
    /// 롯데월드 배경 앰비언트 연출 — 자이로드롭 원반 승강 + 모노레일 열차 순환.
    /// 게임플레이와 무관한 순수 비주얼이라 네트워크 동기화 없이 Time.time 기반으로
    /// 모든 클라가 같은 그림을 그린다(퍼레이드 카 위치 계산과 같은 원리, 트래픽 0).
    /// 참조가 비어 있으면 해당 연출만 조용히 쉰다(모델 없는 그레이박스 맵도 안전).
    /// </summary>
    public class LotteAmbientRides : MonoBehaviour
    {
        [Header("자이로드롭 — 원반이 기둥을 타고 오르내린다")]
        [SerializeField] private Transform m_GyroDisc;
        [SerializeField] private float m_GyroBottomY = 1.1f;
        [SerializeField] private float m_GyroTopY = 10.5f;

        [Header("모노레일 — 열차가 궤도를 따라 섬을 돈다")]
        [SerializeField] private Transform m_Train;          // (레거시) 단일 열차 — m_Trains가 비었을 때만 사용
        [Tooltip("여러 대 편성 — 경로를 균등 간격으로 나눠 함께 돈다.")]
        [SerializeField] private Transform[] m_Trains;
        [Tooltip("기둥 마커 기반 닫힌 폴리라인(빔 상면 높이). 비어 있으면 아래 타원 궤도 폴백.")]
        [SerializeField] private Vector3[] m_TrainPath;
        [SerializeField] private float m_TrainSpeed = 2.2f;    // m/s (폴리라인 모드)
        [SerializeField] private Vector3 m_RingCenter = new Vector3(6.5f, 2.6f, 8.5f);
        [SerializeField] private float m_RingRadiusX = 17f;
        [SerializeField] private float m_RingRadiusZ = 12f;
        [SerializeField] private float m_TrainSpeedDeg = 9f;   // 초당 각도 — 실물처럼 느긋하게(타원 폴백용)
        [SerializeField] private float m_TrainYawOffsetDeg = -90f;   // 모델 장축(x)을 진행방향(z)에 맞추는 보정 — 뒤로 달리면 +90으로

        // 자이로드롭 사이클(초): 상승 → 꼭대기 정지(긴장) → 낙하! → 바닥 휴식
        private const float kRise = 7f, kHold = 3f, kDrop = 0.8f, kRest = 5f;

        private void Update()
        {
            if (m_GyroDisc != null) UpdateGyro();
            if (m_Trains != null && m_Trains.Length > 0)
            {
                for (int i = 0; i < m_Trains.Length; i++)
                    if (m_Trains[i] != null) UpdateOneTrain(m_Trains[i], i, m_Trains.Length);
            }
            else if (m_Train != null) UpdateOneTrain(m_Train, 0, 1);
        }

        private void UpdateGyro()
        {
            var p = m_GyroDisc.position;
            p.y = Mathf.Lerp(m_GyroBottomY, m_GyroTopY, GyroT(Time.time));
            // 상승 중엔 원반이 천천히 자전(실물 재현)
            float cycle = Mathf.Repeat(Time.time, kRise + kHold + kDrop + kRest);
            if (cycle < kRise + kHold)
                m_GyroDisc.Rotate(0f, 20f * Time.deltaTime, 0f, Space.World);
            m_GyroDisc.position = p;
        }

        /// <summary>시각 → 원반 높이 비율(0=바닥, 1=꼭대기). 순수 계산 — 테스트 대상.</summary>
        public static float GyroT(float time)
        {
            float t = Mathf.Repeat(time, kRise + kHold + kDrop + kRest);
            if (t < kRise) return Mathf.SmoothStep(0f, 1f, t / kRise);          // 천천히 감아 올림
            t -= kRise;
            if (t < kHold) return 1f;                                            // 꼭대기 정지
            t -= kHold;
            if (t < kDrop) { float u = t / kDrop; return 1f - u * u; }           // 자유낙하(가속)
            return 0f;                                                           // 바닥 휴식
        }

        private void UpdateOneTrain(Transform tr, int index, int count)
        {
            if (m_TrainPath != null && m_TrainPath.Length >= 2) { UpdateTrainOnPath(tr, index, count); return; }

            float a = Time.time * m_TrainSpeedDeg * Mathf.Deg2Rad + index * Mathf.PI * 2f / count;
            var pos = new Vector3(
                m_RingCenter.x + Mathf.Sin(a) * m_RingRadiusX,
                m_RingCenter.y,
                m_RingCenter.z + Mathf.Cos(a) * m_RingRadiusZ);
            // 타원 접선 방향으로 기수 회전
            var tangent = new Vector3(Mathf.Cos(a) * m_RingRadiusX, 0f, -Mathf.Sin(a) * m_RingRadiusZ);
            tr.position = pos;
            if (tangent.sqrMagnitude > 1e-4f)
                tr.rotation = Quaternion.LookRotation(tangent) * Quaternion.Euler(0f, m_TrainYawOffsetDeg, 0f);
        }

        /// <summary>닫힌 폴리라인(마지막→첫째 자동 연결) 위 일정 속도 주행 — Time.time 기반이라 전 클라 동일.
        /// 여러 대일 땐 경로를 count등분한 간격으로 뒤따른다.</summary>
        private void UpdateTrainOnPath(Transform tr, int index, int count)
        {
            float total = 0f;
            for (int i = 0; i < m_TrainPath.Length; i++)
                total += Vector3.Distance(m_TrainPath[i], m_TrainPath[(i + 1) % m_TrainPath.Length]);
            if (total < 1e-3f) return;

            float d = Mathf.Repeat(Time.time * m_TrainSpeed + total * index / count, total);
            for (int i = 0; i < m_TrainPath.Length; i++)
            {
                var a = m_TrainPath[i];
                var b = m_TrainPath[(i + 1) % m_TrainPath.Length];
                float seg = Vector3.Distance(a, b);
                if (d > seg) { d -= seg; continue; }
                float t = seg > 1e-4f ? d / seg : 0f;
                tr.position = Vector3.Lerp(a, b, t);
                var dir = b - a; dir.y = 0f;
                if (dir.sqrMagnitude > 1e-4f)
                    tr.rotation = Quaternion.LookRotation(dir.normalized) * Quaternion.Euler(0f, m_TrainYawOffsetDeg, 0f);
                return;
            }
        }
    }
}
