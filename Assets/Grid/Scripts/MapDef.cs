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
        [Tooltip("이 맵에서만 쓸 배경음악. 비운 칸은 SoundLibrary의 공용 BGM을 그대로 씁니다.")]
        [SerializeField] private MapBgm m_Bgm;

        public string DisplayName => string.IsNullOrEmpty(m_DisplayName) ? name : m_DisplayName;
        public GameObject BackgroundPrefab => m_BackgroundPrefab;
        public IReadOnlyList<MapAnswerData> Answers => m_Answers;
        public Sprite Thumbnail => m_Thumbnail;

        /// <summary>이 맵에서 주문 가능한 재료(비면 카탈로그 전체). 카탈로그 자체는 전역 그대로다.</summary>
        public IReadOnlyList<MaterialDef> AvailableMaterials => m_AvailableMaterials;

        /// <summary>남산타워 기믹 설정(null이면 이 맵엔 기믹 없음).</summary>
        public NamsanGimmickConfig NamsanGimmicks => m_NamsanGimmicks;

        /// <summary>롯데월드 기믹 설정(null이면 이 맵엔 기믹 없음).</summary>
        public LotteGimmickConfig LotteGimmicks => m_LotteGimmicks;

        /// <summary>DDP 기믹 설정(null이면 이 맵엔 기믹 없음).</summary>
        public DdpGimmickConfig DdpGimmicks => m_DdpGimmicks;

        /// <summary>이 맵 전용 BGM 슬롯(비운 칸은 공용 BGM 폴백).</summary>
        public MapBgm Bgm => m_Bgm;

        /// <summary>맵 전용 건축 영역 크기. 세 축이 모두 1 이상일 때만 유효(아니면 씬 기본값 사용).</summary>
        public Vector3Int GridSize => m_GridSize;
        public bool HasGridSize => m_GridSize.x > 0 && m_GridSize.y > 0 && m_GridSize.z > 0;
    }
}
