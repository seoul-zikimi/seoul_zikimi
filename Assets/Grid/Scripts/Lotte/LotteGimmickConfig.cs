using UnityEngine;

namespace GridSystem
{
    /// <summary>
    /// 롯데월드 전용 기믹(퍼레이드) 튜닝 수치. 맵 카드(MapDef)의 Lotte Gimmicks 칸에 꽂으면 켜진다.
    /// 비워두면 기믹 없음 — 일반 맵은 그대로 두면 된다(남산 NamsanGimmickConfig와 같은 체계).
    /// </summary>
    [CreateAssetMenu(menuName = "Jobsnail/Lotte Gimmick Config", fileName = "LotteGimmickConfig_")]
    public class LotteGimmickConfig : ScriptableObject
    {
        [Header("퍼레이드 — 주기")]
        [Tooltip("다음 퍼레이드까지 최소 간격(초)")] public float ParadeMinInterval = 20f;
        [Tooltip("다음 퍼레이드까지 최대 간격(초)")] public float ParadeMaxInterval = 32f;
        [Tooltip("'퍼레이드 지나갑니다' 예고 시간(초)")] public float ParadeWarnSeconds = 3f;

        [Header("퍼레이드 — 행렬")]
        [Tooltip("퍼레이드 카 수")] public int CarCount = 3;
        [Tooltip("이동 속도(m/s)")] public float CarSpeed = 4f;
        [Tooltip("앞차와의 간격(초)")] public float CarGapSeconds = 1.8f;

        [Header("퍼레이드 — 충돌(치였을 때)")]
        [Tooltip("카 중심 기준 충돌 반경(m)")] public float CarHitRadius = 1.4f;
        [Tooltip("밀쳐내는 속도(m/s) — 경로 밖으로 쓸려남")] public float HitPushSpeed = 7f;
        [Tooltip("밀려난 뒤 스턴(초) — 든 재료를 떨어뜨린다")] public float HitStunSeconds = 2f;
    }
}
