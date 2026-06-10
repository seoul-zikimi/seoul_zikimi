using System.Collections.Generic;
using UnityEngine;

namespace GridSystem
{
    /// <summary>
    /// 재료(블록) 1종의 정의. 그리드 점유(footprint)와 비주얼 메시는 분리된다 —
    /// 어떤 모양의 메시든 디자이너가 footprint(점유 칸)만 선언하면 된다.
    /// </summary>
    [CreateAssetMenu(fileName = "MaterialDef", menuName = "Grid/MaterialDef")]
    public class MaterialDef : ScriptableObject
    {
        [Header("식별")]
        [SerializeField] private int m_Id = -1;                 // 재료 ID — (A)정답·(B)런타임 공통 키. -1 = 미설정

        [Header("그리드 점유 (메시 모양과 무관)")]
        [SerializeField] private Vector3Int m_Footprint = Vector3Int.one;

        [Header("비주얼")]
        [SerializeField] private GameObject m_Prefab;           // 비주얼 + 콜라이더

        [Header("공정 (일부 재료만)")]
        [SerializeField] private List<ProcessType> m_RequiredProcesses = new();  // 순서대로 요구

        [Header("규칙")]
        [SerializeField] private bool m_MustBeFixed;            // 하중 부재(기둥/벽)면 true
        [SerializeField] private bool m_IsBreakable;            // 유리 등
        [SerializeField] private int  m_MaxSpawnCount = -1;     // 스폰 제한 (-1 = 무제한)

        public int Id => m_Id;
        public Vector3Int Footprint => m_Footprint;
        public GameObject Prefab => m_Prefab;
        public IReadOnlyList<ProcessType> RequiredProcesses => m_RequiredProcesses;
        public bool MustBeFixed => m_MustBeFixed;
        public bool IsBreakable => m_IsBreakable;
        public int  MaxSpawnCount => m_MaxSpawnCount;

        /// <summary>요구 공정들을 합친 비트마스크. 채점 시 "완료 ⊇ 요구"를 한 번에 비교하려고 쓴다.</summary>
        public int RequiredMask
        {
            get
            {
                int mask = 0;
                foreach (var p in m_RequiredProcesses)
                    mask |= (int)p;
                return mask;
            }
        }
    }
}