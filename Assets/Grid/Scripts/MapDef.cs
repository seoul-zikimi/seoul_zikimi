using System.Collections.Generic;
using UnityEngine;

namespace GridSystem
{
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

        public string DisplayName => string.IsNullOrEmpty(m_DisplayName) ? name : m_DisplayName;
        public GameObject BackgroundPrefab => m_BackgroundPrefab;
        public IReadOnlyList<MapAnswerData> Answers => m_Answers;
        public Sprite Thumbnail => m_Thumbnail;
    }
}
