using UnityEngine;

namespace GridSystem
{
    /// <summary>
    /// 경복궁 전용 기믹 설정 — 화마(화재) + 사방신 석상. 기획서(08/27) 확정치가 기본값.
    /// 한 문장 규칙: "불나면 양동이로 꺼라. 사방신 석상을 동서남북 제자리에 놓으면 불이 안 난다."
    /// MapDef.m_GyeongbokgungGimmicks에 연결하면 켜지고, 비우면 기믹 전체가 잠잔다.
    /// </summary>
    [CreateAssetMenu(menuName = "Jobsnail/Gyeongbokgung Gimmick Config", fileName = "GyeongbokgungGimmickConfig_")]
    public class GyeongbokgungGimmickConfig : ScriptableObject
    {
        [Header("화마 (화재)")]
        [Tooltip("건축 시작 후 첫 발화까지의 유예(초). 기획: 1분 — 맵·동선 학습 구간.")]
        [Min(0f)] public float FireStartDelay = 60f;

        [Tooltip("첫 발화 시점의 발화 간격(초). 시간이 갈수록 짧아진다.")]
        [Min(5f)] public float FireIntervalStart = 75f;

        [Tooltip("발화 간격 하한(초). 기획: 최소 30초.")]
        [Min(5f)] public float FireIntervalMin = 30f;

        [Tooltip("발화 간격이 시작값에서 하한까지 줄어드는 데 걸리는 시간(초). 라운드 후반 긴장 곡선.")]
        [Min(30f)] public float FireIntervalRampSeconds = 360f;

        [Tooltip("발화한 블록이 소실되기까지의 진화 제한시간(초).")]
        [Min(3f)] public float BurnSeconds = 18f;

        [Tooltip("소실 시 맞닿은 블록으로 번지는 개수(상하좌우앞뒤 인접 블록 중 무작위). 0이면 전이 없음.")]
        [Range(0, 6)] public int SpreadCount = 1;

        [Tooltip("양동이로 물을 붓는 데 걸리는 시간(초, E 꾹 로딩바).")]
        [Min(0.2f)] public float ExtinguishSeconds = 1.2f;

        [Tooltip("드므(물 항아리)에서 이 거리 안에 있으면 양동이가 자동으로 채워진다(m).")]
        [Min(0.5f)] public float DeumeuRefillRange = 2.2f;

        [Header("사방신 석상")]
        [Tooltip("석상이 낙하하는 건축 진행도(%) 문턱들. 기획: 20/30/45/60.")]
        public float[] StatueDropPercents = { 20f, 30f, 45f, 60f };

        [Tooltip("석상 재료 id (동=청룡, 서=백호, 남=주작, 북=현무 순). GyeongbokgungMapTool이 만드는 def와 일치해야 한다.")]
        public int[] StatueMaterialIds = { 50, 51, 52, 53 };

        [Tooltip("석상을 받침대 위에 올린 것으로 인정하는 수평 거리(m). 받침대 근처에 놓거나 던지면 안착.")]
        [Min(0.5f)] public float PedestalSnapRange = 1.8f;

        [Tooltip("틀린 받침대에 놓았을 때 튕겨내는 거리(m).")]
        [Min(0.5f)] public float RejectBounceDistance = 3.5f;

        [Header("보호 구역")]
        [Tooltip("정령이 깬 방위의 화재 면역이 그리드를 어떻게 가르는지 — 각 방위는 건물 중심 기준 해당 사분면(변 기준 절반)을 보호한다.\n동=동쪽 절반, 서=서쪽 절반, 남=남쪽 절반, 북=북쪽 절반. 두 방위가 겹치는 셀은 둘 중 하나만 깨져도 면역.")]
        [Min(0f)] public float ImmunityPadding = 0f;

        /// <summary>현재 시각(라운드 경과 초)에 맞는 발화 간격 — 시작값에서 하한까지 선형 감소.</summary>
        public float FireIntervalAt(float elapsedSinceFirstFire)
        {
            float t = FireIntervalRampSeconds <= 0f ? 1f : Mathf.Clamp01(elapsedSinceFirstFire / FireIntervalRampSeconds);
            return Mathf.Lerp(FireIntervalStart, FireIntervalMin, t);
        }
    }
}
