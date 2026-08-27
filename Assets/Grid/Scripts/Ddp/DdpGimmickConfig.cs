using UnityEngine;

namespace GridSystem
{
    /// <summary>
    /// DDP(동대문디자인플라자) 전용 기믹 3종의 튜닝 수치. 맵 카드(MapDef)의 Ddp Gimmicks 칸에 꽂으면 켜진다.
    /// 비워두면 기믹 없음 — 일반 맵은 그대로 두면 된다(남산·롯데와 같은 체계).
    ///
    /// 기믹 ① 이간수문 물길 : 수문이 열려 수로에 물이 흐른다. 휩쓸리면 하류로 떠내려가고 스턴+재료 드롭.
    ///                        대신 재료를 수로에 두면 건축장 쪽으로 빠르게 흘러간다(위험/이득 양날).
    /// 기믹 ② 유구 발굴터   : 물이 빠진 동안에만 표지 말뚝이 솟는다. 다가가 E를 꾹 누르면 파진다.
    ///                        보너스 재료 또는 유물(점수 보너스)이 나온다. 물이 차면 잠긴다 — ①과 한 사이클.
    /// 기믹 ③ LED 장미 발판 : 주기적으로 피고 진다. 피어 있는 동안만 밟고 건널 수 있는 공중 지름길.
    /// </summary>
    [CreateAssetMenu(menuName = "Jobsnail/DDP Gimmick Config", fileName = "DdpGimmickConfig_")]
    public class DdpGimmickConfig : ScriptableObject
    {
        [Header("① 이간수문 물길 — 주기")]
        [Tooltip("다음 방류까지 최소 간격(초)")] public float FloodMinInterval = 24f;
        [Tooltip("다음 방류까지 최대 간격(초)")] public float FloodMaxInterval = 36f;
        [Tooltip("'수문이 열립니다' 예고 시간(초)")] public float FloodWarnSeconds = 3f;
        [Tooltip("물이 흐르는 시간(초)")] public float FloodDurationSeconds = 9f;

        [Header("① 이간수문 물길 — 수로")]
        [Tooltip("수로 중심선 기준 반경(m) — 이 안에 있으면 휩쓸린다")] public float ChannelRadius = 2.2f;
        [Tooltip("수면 기준 위아래 판정 높이(m) — 다리 위 등 높은 곳은 안전")] public float ChannelHeight = 1.8f;
        [Tooltip("플레이어가 하류로 쓸려가는 속도(m/s)")] public float CurrentSpeed = 6f;
        [Tooltip("물길에서 벗어난 뒤 스턴(초) — 든 재료를 떨어뜨린다")] public float SweptStunSeconds = 2f;

        [Header("① 이간수문 물길 — 재료 급송(리스크의 보상)")]
        [Tooltip("수로에 놓인 재료를 하류로 흘려보낸다")] public bool CarryMaterials = true;
        [Tooltip("재료가 하류로 흐르는 속도(m/s) — 걸어 옮기는 것보다 빨라야 의미가 있다")] public float MaterialFloatSpeed = 9f;

        [Header("② 유구 발굴터")]
        [Tooltip("E를 꾹 눌러 파야 하는 시간(초). 여럿이 같이 파면 그만큼 빨라진다")] public float DigSeconds = 2.5f;
        [Tooltip("표지 말뚝 반경(m) — 이 안에서 E를 눌러야 파진다")] public float DigRadius = 2.2f;
        [Tooltip("출토 후 다음 표지 말뚝이 솟을 때까지(초). 물이 한 번 차고 빠지면 즉시 새로 솟는다")]
        public float DigRespawnSeconds = 14f;
        [Tooltip("한 라운드에 출토 가능한 최대 개수(0=무제한) — 보너스가 밸런스를 깨지 않게")]
        public int DigMaxPerRound = 6;

        [Header("② 유구 발굴터 — 출토물")]
        [Tooltip("유물이 나올 확률(0~1). 나머지는 보너스 재료. 0이면 재료만 나온다")]
        [Range(0f, 1f)] public float ArtifactChance = 0.3f;
        [Tooltip("유물 하나당 보너스 점수 — 건축 완성도(%)와는 별도로 최종 점수에 합산된다.\n" +
                 "참고: 정답 한 칸을 제대로 놓고 공정까지 맞추면 300점")]
        public int ArtifactScoreBonus = 300;

        [Header("③ LED 장미 발판")]
        [Tooltip("피어 있는(밟을 수 있는) 시간(초)")] public float RoseBloomSeconds = 7f;
        [Tooltip("져 있는(밟을 수 없는) 시간(초)")] public float RoseFadeSeconds = 5f;
        [Tooltip("지기 직전 깜빡이며 경고하는 시간(초) — RoseBloomSeconds에 포함")] public float RoseWarnSeconds = 1.5f;
        [Tooltip("발판 한 장의 크기(m)")] public float RosePadSize = 2.2f;
        [Tooltip("발판마다 개화 위상을 어긋나게(초) — 0이면 전부 동시에 핀다")] public float RosePadPhaseOffset = 0.9f;
    }
}
