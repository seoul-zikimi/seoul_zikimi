using UnityEngine;

namespace GridSystem
{
    /// <summary>
    /// 남산타워 맵 전용 기믹(케이블카·엘리베이터·돌풍)의 튜닝 수치 모음.
    /// 맵 카드(MapDef)의 Namsan Gimmicks 칸에 꽂으면 그 맵에서 기믹이 켜진다 — 비워두면 완전히 꺼짐.
    /// 기획서의 [수정가능하게] 수치는 전부 여기 인스펙터에서 조정한다.
    /// </summary>
    [CreateAssetMenu(menuName = "Jobsnail/Namsan Gimmick Config", fileName = "NamsanGimmickConfig")]
    public class NamsanGimmickConfig : ScriptableObject
    {
        [Header("케이블카 — 주문 배송을 케이블카가 대체한다")]
        [Tooltip("운행 곤돌라 수")]
        [Min(1)] public int CarCount = 3;
        [Tooltip("곤돌라가 산 아래에서 재료 하나를 싣고 하차장까지 오는 시간(초)")]
        [Min(0.5f)] public float CarFetchSeconds = 5f;
        [Tooltip("앞 곤돌라가 떠난 뒤 다음 곤돌라가 하차장에 도착할 때까지(초)")]
        [Min(0f)] public float CarGapSeconds = 3f;
        [Tooltip("재료를 빼간 뒤 곤돌라 문이 닫히고 떠날 때까지(초)")]
        [Min(0f)] public float CarCloseSeconds = 2f;
        [Tooltip("열린 곤돌라에서 재료를 안 가져가면 떠나는 시간(초) — 재료는 삭제되고 재주문 가능")]
        [Min(1f)] public float CarTimeoutSeconds = 20f;
        [Tooltip("연속 주문 시 곤돌라들의 출발 간격(초) — 뒷차가 이만큼 기다렸다 출발")]
        [Min(0f)] public float CarDispatchGapSeconds = 1f;

        [Header("엘리베이터 — 철제 전망대 완성 시 개통")]
        [Tooltip("전망대 판정 영역: 정답 셀 중 그리드 y가 이 범위인 셀이 전부 배치+공정 완료되면 개통")]
        public int ObservatoryMinY = 11;
        public int ObservatoryMaxY = 13;

        [Header("돌풍 — 높이 밴드별로 플레이어를 수평으로 민다")]
        [Tooltip("돌풍 주기 최소(초)")]
        [Min(1f)] public float GustMinInterval = 12f;
        [Tooltip("돌풍 주기 최대(초)")]
        [Min(1f)] public float GustMaxInterval = 18f;
        [Tooltip("'돌풍 주의!' 예고 시간(초) — 이 시간 동안 방향 경고가 뜬다")]
        [Min(0f)] public float GustWarnSeconds = 3f;
        [Tooltip("돌풍이 실제로 부는 시간(초)")]
        [Min(0.5f)] public float GustDurationSeconds = 3f;
        [Tooltip("이 높이(그리드 y)부터 약풍 — 그 아래는 밀림 없음")]
        public int WeakWindMinHeight = 10;
        [Tooltip("이 높이(그리드 y)부터 강풍")]
        public int StrongWindMinHeight = 15;
        [Tooltip("약풍이 돌풍 시간 동안 미는 총 거리(칸) — 점프로 파훼 가능한 수준")]
        [Min(0f)] public float WeakPushCells = 2f;
        [Tooltip("강풍이 돌풍 시간 동안 미는 총 거리(칸)")]
        [Min(0f)] public float StrongPushCells = 4f;
        [Tooltip("바람에 밀려 추락 후 착지 시 스턴(초)")]
        [Min(0f)] public float StunSeconds = 2f;

        /// <summary>높이(그리드 y)에 따른 밀림 총량(칸). 순수 함수 — 테스트 대상.</summary>
        public float PushCellsAtHeight(float gridY)
        {
            if (gridY >= StrongWindMinHeight) return StrongPushCells;
            if (gridY >= WeakWindMinHeight) return WeakPushCells;
            return 0f;
        }
    }
}
