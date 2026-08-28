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
        [Tooltip("건축 시작 후 첫 발화까지의 유예(초). [08/28] 60→30: 1층 프리셋 확대로 학습 구간이 짧아져 앞당김.")]
        [Min(0f)] public float FireStartDelay = 30f;

        [Tooltip("첫 발화 시점의 발화 간격(초). 시간이 갈수록 짧아진다. [08/28] 75→60: 1층 프리셋 확대(건축 난이도↓) 보상으로 소폭 상향.")]
        [Min(5f)] public float FireIntervalStart = 60f;

        [Tooltip("발화 간격 하한(초). [08/28] 30→25: 1층 프리셋 확대 보상.")]
        [Min(5f)] public float FireIntervalMin = 25f;

        [Tooltip("발화 간격이 시작값에서 하한까지 줄어드는 데 걸리는 시간(초). 라운드 후반 긴장 곡선.")]
        [Min(30f)] public float FireIntervalRampSeconds = 360f;

        [Tooltip("발화 간격 흔들림(0~1). 곡선 값을 중심으로 매번 ±비율만큼 랜덤. " +
                 "0이면 예전처럼 정확히 일정한 간격이라 석상 낙하와 반복해서 겹친다. 0.3이면 60초 구간에서 42~78초.")]
        [Range(0f, 0.9f)] public float FireIntervalJitter = 0.3f;

        [Tooltip("석상이 낙하한 직후 이 시간(초)만큼 발화를 미룬다. 두 연출이 동시에 터지면 " +
                 "무엇이 일어났는지 읽을 수 없다. 0이면 미루지 않음.")]
        [Min(0f)] public float FireDeferAfterStatueSeconds = 6f;

        [Tooltip("발화한 블록이 소실되기까지의 진화 제한시간(초).")]
        [Min(3f)] public float BurnSeconds = 18f;

        [Tooltip("소실 시 맞닿은 블록으로 번지는 최대 개수. 6 = 면이 닿은 인접 블록 전부('불 꺼야 해' 압박 극대화). 0이면 전이 없음.")]
        [Range(0, 6)] public int SpreadCount = 6;

        [Tooltip("양동이로 물을 붓는 데 걸리는 시간(초, E 꾹 로딩바).")]
        [Min(0.2f)] public float ExtinguishSeconds = 1.2f;

        [Tooltip("드므(물 항아리)에서 이 거리 안에 있으면 양동이가 자동으로 채워진다(m).")]
        [Min(0.5f)] public float DeumeuRefillRange = 2.2f;

        [Tooltip("불타는 블록 위 화염 이펙트 크기 배율.")]
        [Min(0.2f)] public float FlameScale = 2.2f;

        [Tooltip("화마(불꽃 악령)가 발화 전에 목표 블록 위로 날아가 맴도는 전조 시간(초). [08/28 적의 실체화] " +
                 "어디 불이 날지 미리 보인다 — 밸런스는 그대로, 위협이 캐릭터가 된다.")]
        [Range(1f, 10f)] public float DemonLeadSeconds = 4f;

        [Tooltip("기본 제공(프리셋) 블록도 발화 대상인가. [08/28] 1층이 거의 프리셋으로 깔리면서 기본 true — " +
                 "'미리 지어진 건물을 화마로부터 지켜라'가 게임플레이. false면 프리셋 불연(예전 규칙 — 태울 게 거의 없어진다).")]
        public bool BurnPresetBlocks = true;

        [Header("사방신 석상")]
        [Tooltip("석상이 낙하하는 '건축 시간' 진행률(%) 문턱들 — 제한시간 기준. 기획: 20/30/45/60 (10분이면 2분/3분/4분30초/6분).")]
        public float[] StatueDropPercents = { 20f, 30f, 45f, 60f };

        [Tooltip("석상 낙하의 '점수 진행도'(%) 문턱들 — 시간 문턱과 하이브리드: 둘 중 먼저 도달하는 쪽에 낙하.\n" +
                 "빠른 팀은 진행도로 일찍 받고(스피드런 보상), 느린 팀도 시간이 하한을 보장(데스 스파이럴 방지).")]
        public float[] StatueDropScorePercents = { 25f, 40f, 60f, 80f };

        [Tooltip("석상 재료 id (동=청룡, 서=백호, 남=주작, 북=현무 순). GyeongbokgungMapTool이 만드는 def와 일치해야 한다.")]
        public int[] StatueMaterialIds = { 61, 62, 52, 53 };

        [Tooltip("진행도가 한 번에 여러 문턱을 넘어도 석상은 이 간격(초)을 두고 한 개씩만 낙하한다(우르르 방지).")]
        [Min(0f)] public float StatueDropMinGapSeconds = 20f;

        [Tooltip("석상을 받침대 위에 올린 것으로 인정하는 수평 거리(m). 받침대 근처에 놓거나 던지면 안착.")]
        [Min(0.5f)] public float PedestalSnapRange = 2.5f;

        [Tooltip("틀린 받침대에 놓았을 때 튕겨내는 거리(m).")]
        [Min(0.5f)] public float RejectBounceDistance = 3.5f;

        [Header("보호 구역")]
        [Tooltip("방위 석상 하나가 보호하는 가장자리 띠의 폭(그리드 비율). 0.34 = 그 방위 쪽 1/3.\n" +
                 "[08/28] 절반(0.5)은 마주보는 둘만 놓아도 전면 면역이라 하향 — 중앙부는 4개 봉인 전까지 계속 탄다.")]
        [Range(0.1f, 0.5f)] public float ImmunityBandFraction = 0.34f;

        /// <summary>현재 시각(라운드 경과 초)에 맞는 발화 간격 — 시작값에서 하한까지 선형 감소.</summary>
        public float FireIntervalAt(float elapsedSinceFirstFire)
        {
            float t = FireIntervalRampSeconds <= 0f ? 1f : Mathf.Clamp01(elapsedSinceFirstFire / FireIntervalRampSeconds);
            return Mathf.Lerp(FireIntervalStart, FireIntervalMin, t);
        }

        /// <summary>
        /// 실제로 쓸 다음 발화 간격 — 곡선 기준값(FireIntervalAt)에 FireIntervalJitter 만큼 흔들림을 준다.
        /// 곡선을 중심으로 흔들기 때문에 '후반으로 갈수록 잦아진다'는 난이도 설계는 그대로 두면서,
        /// 매 발화 시점만 예측 불가능해진다. 서버에서만 호출된다(발화 판정이 서버 권위라 클라 간 어긋나지 않는다).
        /// </summary>
        public float NextFireInterval(float elapsedSinceFirstFire)
        {
            float baseInterval = FireIntervalAt(elapsedSinceFirstFire);
            if (FireIntervalJitter <= 0f) return baseInterval;
            float scale = Random.Range(1f - FireIntervalJitter, 1f + FireIntervalJitter);
            return Mathf.Max(5f, baseInterval * scale);
        }
    }
}
