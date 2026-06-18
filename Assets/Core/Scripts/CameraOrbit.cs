using UnityEngine;

/// <summary>
/// 오빗 카메라 회전/줌 상태 + 입력 적분(공유). 플레이어 카메라와 정답 패널 카메라가 같은 조작감을 쓰도록.
/// MonoBehaviour 아님 — 카메라 측 컴포넌트가 인스턴스로 들고, 매 프레임 Integrate 후 위치를 적용한다.
/// </summary>
public class CameraOrbit
{
    // 설정(감도/한계) — 두 카메라가 같은 값을 넣어 조작감 통일
    public float RotateSpeed = 0.3f;   // 도/픽셀
    public float ZoomSpeed   = 0.01f;  // 스크롤 1노치(120) = 1.2유닛
    public float PitchMin    = 15f;
    public float PitchMax    = 80f;
    public float DistMin     = 3f;
    public float DistMax     = 20f;

    // 상태
    public float Yaw;
    public float Pitch    = 45f;
    public float Distance = 10f;

    // 입력을 상태에 적분: yaw 누적, pitch/distance 클램프. 플레이어 카메라와 동일 수식.
    public void Integrate(Vector2 rotDelta, float zoom)
    {
        Yaw     += rotDelta.x * RotateSpeed;
        Pitch    = Mathf.Clamp(Pitch    - rotDelta.y * RotateSpeed, PitchMin, PitchMax);
        Distance = Mathf.Clamp(Distance - zoom       * ZoomSpeed,   DistMin,  DistMax);
    }

    // CameraArm 로컬 오프셋(수평 yaw는 외부=팔이 담당하는 플레이어용). 구면식 (0, d·sin, -d·cos).
    public Vector3 LocalOffset()
    {
        float rad = Pitch * Mathf.Deg2Rad;
        return new Vector3(0f, Distance * Mathf.Sin(rad), -Distance * Mathf.Cos(rad));
    }

    // pivot 주위 월드 위치(yaw 포함). 팔 없는 정답 카메라용.
    public Vector3 WorldPosition(Vector3 pivot)
        => pivot + Quaternion.Euler(0f, Yaw, 0f) * LocalOffset();
}
