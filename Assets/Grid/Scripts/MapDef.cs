using System.Collections.Generic;
using UnityEngine;

namespace GridSystem
{
    /// <summary>
    /// 맵 전용 배경음악 슬롯. 비운 칸은 SoundLibrary의 공용 BGM(페이즈별)을 그대로 쓴다 —
    /// 즉 "이 맵만 다른 곡"을 원하는 칸에만 mp3를 꽂으면 된다(기존 맵은 전부 비워두면 지금과 동일).
    /// mp3는 Assets/Sound/Sound_file/브금/맵별/ 에 넣고 여기로 드래그(Resources 로드 아님 — 직접 참조).
    /// </summary>
    [System.Serializable]
    public struct MapBgm
    {
        [Tooltip("건축 중 BGM. 비우면 공용 Building BGM")]
        public AudioClip Building;
        [Tooltip("남은 60초 긴박 BGM. 비우면 공용 BuildingUrgent BGM")]
        public AudioClip Urgent;
        [Tooltip("결과 화면 BGM. 비우면 공용 Result BGM")]
        public AudioClip Result;

        /// <summary>페이즈 이름("Building"/"BuildingUrgent"/"Result")에 해당하는 맵 전용 곡. 없으면 null(공용 폴백).</summary>
        public AudioClip For(string phaseName) => phaseName switch
        {
            "Building"       => Building,
            "BuildingUrgent" => Urgent,
            "Result"         => Result,
            _                => null,
        };
    }

    /// <summary>
    /// 맵 1개 정의(배경 + 정답 세트 + 로비 표시 정보). 씬을 늘리지 않고 배경 프리팹 스왑으로 맵을 추가한다.
    /// 생성: Tools ▸ Map ▸ Extract Background To Prefab (배경 프리팹화 + MapDef + 카탈로그 등록 자동).
    /// </summary>
    [CreateAssetMenu(menuName = "Jobsnail/Map Def", fileName = "Map_")]
    public class MapDef : ScriptableObject
    {
        [SerializeField] private string m_DisplayName;         // 로비 표시 이름(비우면 에셋 파일명)
        [SerializeField] private GameObject m_BackgroundPrefab; // 환경(배경) 통째 프리팹 — MapLoader가 스폰
        [SerializeField] private List<MapAnswerData> m_Answers = new();   // 이 맵 전용 정답 세트(비우면 GridManager 기본 목록 사용)
        [SerializeField] private Sprite m_Thumbnail;           // 로비 "선택된 맵 이미지"용(선택)
        [Tooltip("이 맵의 건축 영역 크기(칸). 비워두면(0) GameScene의 GridManager 값을 씁니다. 2vs2에서는 가로가 2배가 됩니다.")]
        [SerializeField] private Vector3Int m_GridSize;        // (0,0,0) = 미설정
        [Tooltip("이 맵에서 주문할 수 있는 재료. 비워두면 MaterialCatalog 전체가 나옵니다.")]
        [SerializeField] private List<MaterialDef> m_AvailableMaterials = new();
        [Tooltip("남산타워 전용 기믹(케이블카·엘리베이터·돌풍) 설정. 비워두면 기믹 없음 — 일반 맵은 그대로 두세요.")]
        [SerializeField] private NamsanGimmickConfig m_NamsanGimmicks;
        [Tooltip("롯데월드 전용 기믹(퍼레이드) 설정. 비워두면 기믹 없음 — 일반 맵은 그대로 두세요.")]
        [SerializeField] private LotteGimmickConfig m_LotteGimmicks;
        [Tooltip("DDP 전용 기믹(이간수문 물길·유구 발굴터·LED 장미) 설정. 비워두면 기믹 없음.")]
        [SerializeField] private DdpGimmickConfig m_DdpGimmicks;
        [Tooltip("경복궁 전용 기믹(화마·사방신 석상) 설정. 비워두면 기믹 없음 — 일반 맵은 그대로 두세요.")]
        [SerializeField] private GyeongbokgungGimmickConfig m_GyeongbokgungGimmicks;
        [Tooltip("이 맵에서만 쓸 배경음악. 비운 칸은 SoundLibrary의 공용 BGM을 그대로 씁니다.")]
        [SerializeField] private MapBgm m_Bgm;

        [Header("완성체 (조각으로 짓는 맵 전용)")]
        [Tooltip("정답을 다 맞췄을 때 보여줄 '통짜 완성 모델'. 비워두면 조각 그대로 남습니다.\n\n" +
                 "DDP처럼 큰 곡면 모델을 격자로 잘라 짓는 맵은, 조각 이음매가 아무리 잘 맞아도\n" +
                 "잘린 단면 때문에 완성본이 매끈하게 안 보인다. 그래서 다 지으면 조각을 감추고\n" +
                 "자르기 전 원본 하나로 갈아 끼운다 — 완공 계획도(정답 UI)도 이 모델로 보여준다.")]
        [SerializeField] private GameObject m_CompletedModel;
        [Tooltip("완성체 프리팹을 놓을 기준 셀(그 셀의 min-corner에 프리팹 원점이 온다).")]
        [SerializeField] private Vector3Int m_CompletedModelAnchor;

        public string DisplayName => string.IsNullOrEmpty(m_DisplayName) ? name : m_DisplayName;
        public GameObject BackgroundPrefab => m_BackgroundPrefab;
        public IReadOnlyList<MapAnswerData> Answers => m_Answers;
        public Sprite Thumbnail => m_Thumbnail;

        /// <summary>2vs2 전용 공터(경기장) 여부 — 로비 맵 선택지에서 제외되고, 대전 모드에서 배경/그리드로 강제 사용된다.
        /// 에셋 이름 기준(별도 필드 마이그레이션 없이 기존 Map_VersusField.asset 그대로 인식).</summary>
        public bool IsVersusArena => name == "Map_VersusField";

        /// <summary>이 맵에서 주문 가능한 재료(비면 카탈로그 전체). 카탈로그 자체는 전역 그대로다.</summary>
        public IReadOnlyList<MaterialDef> AvailableMaterials => m_AvailableMaterials;

        /// <summary>남산타워 기믹 설정(null이면 이 맵엔 기믹 없음).</summary>
        public NamsanGimmickConfig NamsanGimmicks => m_NamsanGimmicks;

        /// <summary>롯데월드 기믹 설정(null이면 이 맵엔 기믹 없음).</summary>
        public LotteGimmickConfig LotteGimmicks => m_LotteGimmicks;

        /// <summary>DDP 기믹 설정(null이면 이 맵엔 기믹 없음).</summary>
        public DdpGimmickConfig DdpGimmicks => m_DdpGimmicks;

        /// <summary>경복궁 기믹 설정(null이면 이 맵엔 기믹 없음).</summary>
        public GyeongbokgungGimmickConfig GyeongbokgungGimmicks => m_GyeongbokgungGimmicks;

        /// <summary>이 맵 전용 BGM 슬롯(비운 칸은 공용 BGM 폴백).</summary>
        public MapBgm Bgm => m_Bgm;

        /// <summary>정답을 다 맞췄을 때 조각 대신 보여줄 통짜 완성 모델(null이면 조각 그대로).</summary>
        public GameObject CompletedModel => m_CompletedModel;
        /// <summary>완성체를 놓을 기준 셀(min-corner 기준).</summary>
        public Vector3Int CompletedModelAnchor => m_CompletedModelAnchor;

        /// <summary>맵 전용 건축 영역 크기. 세 축이 모두 1 이상일 때만 유효(아니면 씬 기본값 사용).</summary>
        public Vector3Int GridSize => m_GridSize;
        public bool HasGridSize => m_GridSize.x > 0 && m_GridSize.y > 0 && m_GridSize.z > 0;
    }
}
