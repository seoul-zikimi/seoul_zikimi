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
        [SerializeField] private bool m_Walkable;              // 바닥처럼 플레이어가 위로 지나갈 수 있나(콜라이더 안 붙음)
        [SerializeField] private bool m_IsBreakable;            // 유리 등
        [SerializeField] private int  m_MaxSpawnCount = -1;     // 스폰 제한 (-1 = 무제한)
        [Tooltip("무거운 재료: 혼자 들면 이동속도 0.7배(땀). 빈손 동료가 옆에 붙으면 정상 속도 — 협동 기믹.")]
        [SerializeField] private bool m_IsHeavy;

        [Header("비주얼 규약 예외")]
        [Tooltip("비주얼이 칸을 꽉 채우지 않는 '자유 형상'인가.\n\n" +
                 "보통 재료는 비주얼 크기 = footprint여야 하고 MaterialPrefabContractTests가 이를 강제한다.\n" +
                 "하지만 하나의 큰 모델(예: DDP 본관)을 격자로 잘라 만든 조각들은 곡면이라\n" +
                 "칸을 꽉 채우지 않고, 억지로 늘리면 조각끼리 곡면이 어긋난다.\n" +
                 "이 값을 켜면 그 두 테스트(피벗·크기)를 건너뛴다 — 대신 조각을 만든 툴이\n" +
                 "피벗을 '칸의 min-corner'에 정확히 맞출 책임을 진다.")]
        [SerializeField] private bool m_FreeformVisual;

        [Tooltip("칸 규격을 '일부러' 벗어나는 비주얼인가(점유 칸은 그대로).\n\n" +
                 "예) 롯데월드 중앙 첨탑 — 바로 아래 성상단의 상단이 움푹 파여 있어서\n" +
                 "밑동을 칸 아래로 연장해야 첨탑이 파인 곳에 꽂힌 것처럼 자연스럽다.\n" +
                 "켜면 칸맞춤 툴(MaterialPrefabFitTool)이 건드리지 않고 규약 테스트도 건너뛴다\n" +
                 "— 대신 _Fit을 굽는 툴이 크기·피벗을 책임진다.\n" +
                 "경복궁 파츠(이음새 가리기용 1.05~1.18배 오버필)는 폴더 경로로 이미 면제된다.")]
        [SerializeField] private bool m_IntentionalOverfill;

        public int Id => m_Id;
        public Vector3Int Footprint => m_Footprint;
        public GameObject Prefab => m_Prefab;
        public IReadOnlyList<ProcessType> RequiredProcesses => m_RequiredProcesses;
        public bool MustBeFixed => m_MustBeFixed;
        public bool Walkable => m_Walkable;
        public bool IsBreakable => m_IsBreakable;
        public int  MaxSpawnCount => m_MaxSpawnCount;
        public bool IsHeavy => m_IsHeavy;
        /// <summary>비주얼이 칸을 꽉 채우지 않는 자유 형상(큰 모델을 잘라 만든 조각 등).
        /// 켜져 있으면 MaterialPrefabContractTests의 피벗·크기 검사를 건너뛴다.</summary>
        public bool FreeformVisual => m_FreeformVisual;

        /// <summary>칸 규격을 일부러 벗어나는 비주얼(밑동 연장 등). 켜져 있으면 칸맞춤 툴과
        /// 규약 테스트(피벗·크기·회전 안착)가 이 정의를 건드리지 않는다. 점유 칸은 footprint 그대로.</summary>
        public bool IntentionalOverfill => m_IntentionalOverfill;

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