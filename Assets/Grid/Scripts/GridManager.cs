using System.Collections.Generic;
using UnityEngine;

namespace GridSystem
{
    /// <summary>
    /// (B) 런타임 그리드의 씬 호스트(싱글플레이). RuntimeGrid(순수 로직)를 보유하고
    /// 입력/비주얼을 붙인다. 멀티는 나중에 GridNetwork(NetworkBehaviour)가 같은 역할.
    /// </summary>
    public class GridManager : MonoBehaviour
    {
        [SerializeField] private Vector3Int m_GridSize = new Vector3Int(8, 4, 8);
        [SerializeField] private MaterialCatalog m_Catalog;
        [Tooltip("정답 맵 목록 — 게임 시작/재시작 때 이 중에서 랜덤으로 하나 선택(서버 권위).")]
        [SerializeField] private List<MapAnswerData> m_Answers = new();
        [SerializeField] private bool m_DrawGizmos = true;

        private int m_ActiveIndex;

        /// <summary>현재 선택된 정답이 바뀌었을 때(랜덤 선택/재시작). 비주얼 갱신용.</summary>
        public event System.Action OnAnswerChanged;

        public RuntimeGrid Grid { get; private set; }
        public Vector3Int GridSize => m_GridSize;
        public MaterialCatalog Catalog => m_Catalog;

        // ── 2vs2: X축으로 그리드 2배(A|B 구역) + 중앙 분할벽 ──
        private bool m_Versus;
        public bool IsVersusGrid => m_Versus;
        /// <summary>실제 그리드 크기(2vs2면 X 2배). 서버 그리드·바닥·구역 검사가 이 값을 쓴다.</summary>
        public Vector3Int EffectiveSize => m_Versus ? new Vector3Int(m_GridSize.x * 2, m_GridSize.y, m_GridSize.z) : m_GridSize;
        /// <summary>한 팀 구역 크기(=인스펙터 그리드 크기). 팀A: x∈[0,W), 팀B: x∈[W,2W).</summary>
        public Vector3Int ZoneSize => m_GridSize;

        /// <summary>2vs2 구성 — 스폰 직후(블록 배치 전) GameLoopManager가 호출. 그리드 재생성 + 바닥 확장 + 분할벽.</summary>
        public void ConfigureVersus(bool on)
        {
            if (m_Versus == on) return;
            m_Versus = on;
            Grid = new RuntimeGrid(EffectiveSize);
            RebuildGround();
            if (on)
            {
                CreateCenterWall();
                MirrorBackgroundForVersus();
            }
        }

        /// <summary>고를 수 있는 정답 개수.</summary>
        public int AnswerCount => m_Answers != null ? m_Answers.Count : 0;

        /// <summary>현재 선택된 정답(없으면 null).</summary>
        public MapAnswerData Answer =>
            (m_Answers != null && m_Answers.Count > 0)
                ? m_Answers[Mathf.Clamp(m_ActiveIndex, 0, m_Answers.Count - 1)]
                : null;

        /// <summary>맵(MapDef)이 전용 정답 세트를 갖고 있으면 교체 — 모든 클라가 같은 MapDef를 로드하므로 인덱스 동기화 유효.</summary>
        public void SetAnswers(System.Collections.Generic.IReadOnlyList<MapAnswerData> answers)
        {
            if (answers == null || answers.Count == 0) return;   // 비면 씬 기본 목록 유지
            m_Answers = new List<MapAnswerData>(answers);
            m_ActiveIndex = 0;
            OnAnswerChanged?.Invoke();
        }

        /// <summary>정답 선택(서버가 뽑은 인덱스를 전 클라가 적용). 바뀌면 OnAnswerChanged 발생.</summary>
        public void SelectAnswer(int index)
        {
            int n = (m_Answers != null) ? m_Answers.Count : 0;
            if (n <= 0) { m_ActiveIndex = 0; return; }   // 리스트 없음 → 레거시 단일만
            index = ((index % n) + n) % n;
            if (index == m_ActiveIndex) return;
            m_ActiveIndex = index;
            OnAnswerChanged?.Invoke();
        }

        private void Awake()
        {
            GridContract.Origin = transform.position;   // 그리드 기준점 = 이 오브젝트 위치(맵 옮기면 정답·그리드도 따라감)
            EnsureGrid();
            CreateGround();   // 물리 점프로 Y가 풀려도 플레이어가 추락하지 않게 그리드 바닥 콜라이더 생성
        }

        // 그리드 XZ를 덮는, 보이지 않는 바닥 콜라이더(윗면 = 그리드 바닥 y). 렌더러 없음.
        private void CreateGround()
        {
            float u = GridContract.Unit;
            Vector3 baseW = GridCoordinates.CellToWorld(Vector3Int.zero);   // 그리드 min-corner(바닥)
            float sx = EffectiveSize.x * u, sz = EffectiveSize.z * u;
            const float thick = 1f, margin = 4f;

            var go = new GameObject("~Ground");
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(baseW.x + sx * 0.5f, baseW.y - thick * 0.5f, baseW.z + sz * 0.5f);
            go.AddComponent<BoxCollider>().size = new Vector3(sx + margin, thick, sz + margin);
        }

        // 그리드 크기가 바뀌면(2vs2) 바닥 콜라이더 재생성.
        private void RebuildGround()
        {
            var old = transform.Find("~Ground");
            if (old != null) Destroy(old.gameObject);
            CreateGround();
        }

        // 2vs2 중앙 분할벽 — 넘어갈 수 없는 물리벽 + 반투명 비주얼. 코스메틱+물리라 각 클라 로컬 생성으로 충분(결정적).
        private void CreateCenterWall()
        {
            float u = GridContract.Unit;
            var size = EffectiveSize;
            Vector3 baseW = GridCoordinates.CellToWorld(Vector3Int.zero);
            float wallH = size.y * u + 6f;

            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "~VersusWall";
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(baseW.x + ZoneSize.x * u, baseW.y + wallH * 0.5f, baseW.z + size.z * 0.5f * u);
            go.transform.localScale = new Vector3(0.3f, wallH, size.z * u + 10f);
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = MakeTransparent(new Color(0.7f, 0.85f, 1f, 0.15f));   // 투명벽(기획) — 상대 진영 보임
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // URP Lit 투명 머티리얼(런타임 생성). 빌드 셰이더 스트립 대비 명시 URP Lit.
        private static Material MakeTransparent(Color color)
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = color };
            mat.SetFloat("_Surface", 1f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return mat;
        }

        // 2vs2: 배경("Background")을 분할벽 중심 기준 180° 회전 복제 — 팀B 진영도 같은 풍경(점대칭).
        // 복제본은 비주얼 전용(콜라이더·스크립트 제거). 배경이 없으면 조용히 넘어감.
        private void MirrorBackgroundForVersus()
        {
            var bg = GameObject.Find("Background");
            if (bg == null) return;

            float u = GridContract.Unit;
            Vector3 baseW = GridCoordinates.CellToWorld(Vector3Int.zero);
            Vector3 pivot = new Vector3(baseW.x + ZoneSize.x * u, 0f, baseW.z + EffectiveSize.z * 0.5f * u);

            var clone = Instantiate(bg);
            clone.name = "~VersusMirrorBackground";
            foreach (var c in clone.GetComponentsInChildren<Collider>(true)) Destroy(c);
            foreach (var s in clone.GetComponentsInChildren<MonoBehaviour>(true)) Destroy(s);   // 비주얼만 유지
            clone.transform.RotateAround(pivot, Vector3.up, 180f);
        }

        public void EnsureGrid()
        {
            Grid ??= new RuntimeGrid(m_GridSize);
        }

        private void OnDrawGizmos()
        {
            GridContract.Origin = transform.position;   // 에디터에서도 옮긴 위치 반영(기즈모 off여도 동기화)
            if (!m_DrawGizmos) return;

            float u = GridContract.Unit;
            Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.25f);
            for (int x = 0; x < m_GridSize.x; x++)
            for (int y = 0; y < m_GridSize.y; y++)
            for (int z = 0; z < m_GridSize.z; z++)
            {
                // CellToWorld는 셀의 min-corner → 중심은 +0.5u. 셀 = 1유닛 와이어 큐브.
                Vector3 center = GridCoordinates.CellToWorld(new Vector3Int(x, y, z)) + Vector3.one * 0.5f * u;
                Gizmos.DrawWireCube(center, Vector3.one * u);
            }
        }
    }
}
