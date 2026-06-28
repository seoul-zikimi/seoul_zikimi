using UnityEngine;

namespace GridSystem
{
    /// <summary>
    /// 그리드 좌표 규약의 단일 소스(Single Source of Truth).
    /// 이 값들이 (A)오서링 그리드와 (B)런타임 그리드를 같은 좌표계에 묶는다.
    /// </summary>
    public static class GridContract
    {
        /// <summary>셀 한 칸 = 월드 1유닛. (Autotiles3D Grid를 Unit=1로 둔 것과 일치)</summary>
        public const float Unit = 1f;
        
        /// <summary>그리드 (0,0,0) 셀의 월드 위치. GridManager가 자기 transform.position으로 동기화(맵 이동 지원). 회전/스케일은 identity 가정.</summary>
        public static Vector3 Origin = Vector3.zero;

        /// <summary>로컬 플레이어가 현재 딛고 선(=배치) 층 Y. PlayerCarry(owner)가 매 프레임 기록 → AnswerPreview가 그 층 고스트만 표시(층별 안내).</summary>
        public static int LocalBuildFloor;

        // 축 규약: X·Z = 평면, Y = 수직(층). Autotiles3D와 동일(Y-up).
    }
}