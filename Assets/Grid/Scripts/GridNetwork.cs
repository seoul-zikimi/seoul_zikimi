using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace GridSystem
{
    /// <summary>
    /// (B) 런타임 그리드의 네트워크 호스트(서버 권위). 서버가 RuntimeGrid로 검증/판정하고,
    /// 상태는 NetworkList&lt;CellEntry&gt;로 복제. 모든 클라이언트는 리스트 변경 시 비주얼을 재구성한다.
    /// 입력은 GridDebugController가 RequestXxx()로 보냄(클라 → 서버 RPC). 늦참은 NetworkList가 자동 복제.
    /// </summary>
    [RequireComponent(typeof(GridManager))]
    public class GridNetwork : NetworkBehaviour
    {
        private readonly NetworkList<CellEntry> m_Cells = new();
        private readonly NetworkVariable<ScoreSnapshot> m_Score =
            new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<ScoreSnapshot> m_ScoreB =
            new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);   // 2vs2 팀B
        public ScoreSnapshot Score => m_Score.Value;
        public float ScorePercent => m_Score.Value.Percent;
        /// <summary>2vs2 팀별 점수(0=A, 1=B). 협동 모드는 팀 무관하게 m_Score.</summary>
        public ScoreSnapshot ScoreFor(int team) => team == 1 ? m_ScoreB.Value : m_Score.Value;

        /// <summary>복제된 상태 기준 해당 셀이 비어있는지(클라이언트도 호출 가능). 배치 전 사전 검사용.</summary>
        public bool IsCellFree(Vector3Int cell)
        {
            foreach (var e in m_Cells)
                if (e.cell == cell) return false;
            return true;
        }

        /// <summary>복제된 상태에서 셀의 재료 id·완료 공정 비트를 읽는다(클라도 호출 — E 공정 다음단계 판단용).</summary>
        public bool TryGetCell(Vector3Int cell, out int materialId, out int completedMask)
        {
            foreach (var e in m_Cells)
                if (e.cell == cell) { materialId = e.materialId; completedMask = e.completedProcessMask; return true; }
            materialId = -1; completedMask = 0;
            return false;
        }

        /// <summary>해당 셀을 차지한 블록이 실제로 차지한 모든 셀을 채운다(멀티셀 블록 포함). 사거리 판정용.</summary>
        public bool TryGetBlockCells(Vector3Int cell, System.Collections.Generic.List<Vector3Int> result)
        {
            result.Clear();
            ulong owner = 0;
            bool found = false;
            foreach (var e in m_Cells)
                if (e.cell == cell) { owner = e.ownerObjectId; found = true; break; }
            if (!found) return false;
            foreach (var e in m_Cells)
                if (e.ownerObjectId == owner) result.Add(e.cell);
            return true;
        }

        /// <summary>해당 셀이 '미고정 하중부재'(고정 전)면 true — 좌클릭 재집기 가능. (복제 상태 기준, 클라/UI도 호출)</summary>
        public bool IsPickupable(Vector3Int cell)
        {
            if (!TryGetCell(cell, out int matId, out int completed)) return false;
            var def = m_Manager.Catalog != null ? m_Manager.Catalog.GetById(matId) : null;
            if (def == null) return false;
            return def.MustBeFixed && (completed & (int)ProcessType.Fixed) == 0;
        }

        private GridManager m_Manager;
        private GameLoopManager m_Loop;        // 같은 오브젝트(2vs2 팀·구역 판정용)
        private MaterialDropField m_DropField; // 같은 오브젝트(붕괴/철거 재료를 바닥에 떨굼)
        private RuntimeGrid m_ServerGrid;     // 서버 전용 권위 상태
        private ulong m_OwnerCounter;         // 서버 전용 고유 ownerObjectId 발급
        private GameObject m_VisualRoot;      // 클라이언트 로컬 비주얼 부모
        [SerializeField] private float m_MarkerScale = 0.5f;   // 공정 마커(도구 모델) 크기

        private void Awake()
        {
            m_Manager = GetComponent<GridManager>();
            m_DropField = GetComponent<MaterialDropField>();
            m_Loop = GetComponent<GameLoopManager>();
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                // 2vs2면 X 2배(A|B 구역) — 서버(=호스트)에서는 로비 선택값을 스폰 순서와 무관하게 읽을 수 있다.
                bool versus = GameLoopManager.HostSelectedMode == (int)SeoulZikimi.Gameplay.GameModeKind.TeamVersus;
                var size = m_Manager.GridSize;
                if (versus) size.x *= 2;
                m_ServerGrid = new RuntimeGrid(size);
                m_ServerGrid.ExternalSupportBelow = c => GridSupport.ExternalSolidAt(c, GridContract.Unit);   // 환경 바닥·스캐폴드도 지지로 인정
            }

            m_VisualRoot = new GameObject("~GridVisuals");
            m_Cells.OnListChanged += OnCellsChanged;
            RebuildVisuals();   // 늦참: 이미 복제된 리스트로 즉시 재구성
        }

        public override void OnNetworkDespawn()
        {
            m_Cells.OnListChanged -= OnCellsChanged;
            if (m_VisualRoot != null) Destroy(m_VisualRoot);
            if (m_ResultCam != null) Destroy(m_ResultCam.gameObject);
            if (m_ResultRT != null) { m_ResultRT.Release(); m_ResultRT = null; }
        }

        // ── 정산서 썸네일: 실제 월드를 그 자리에서 한 컷 촬영 ──
        // 내가 지은 블록 + 미리 있던 주변 건축물/지형/하늘까지 전경으로 담긴다(스카이박스 배경).
        private const int kAnswerPreviewLayer = 30;   // AnswerPreview 미니씬 레이어(사진에서 제외)
        private Camera m_ResultCam;
        private RenderTexture m_ResultRT;

        public RenderTexture BuildResultPreview()
        {
            if (m_VisualRoot == null) return null;

            // 구도 = 그리드 전체 볼륨(건설 현장 통째) — 몇 개만 지어도 항상 전경이 다 보이게.
            float gu = GridContract.Unit;
            var volume = (Vector3)m_Manager.GridSize * gu;
            var b = new Bounds(GridContract.Origin + volume * 0.5f, volume);

            if (m_ResultRT == null) m_ResultRT = new RenderTexture(512, 512, 16);
            if (m_ResultCam == null)
            {
                var camGO = new GameObject("~ResultPreviewCam");
                m_ResultCam = camGO.AddComponent<Camera>();
                m_ResultCam.clearFlags = CameraClearFlags.Skybox;   // 하늘 배경 전경샷
                m_ResultCam.fieldOfView = 42f;
                m_ResultCam.cullingMask = ~(1 << kAnswerPreviewLayer);   // 정답 미니씬만 제외, 월드 전부 포함
                m_ResultCam.targetTexture = m_ResultRT;
                m_ResultCam.depth = -10f;   // 메인 카메라보다 먼저(화면 출력엔 영향 없음 — RT 전용)
            }

            // 쿼터뷰 + 살짝 물러나서 주변 전경이 프레임에 들어오게
            float radius = Mathf.Max(4f, b.extents.magnitude);
            var dir = new Vector3(0.8f, 0.55f, -0.8f).normalized;
            m_ResultCam.transform.position = b.center + dir * (radius * 2.4f);
            m_ResultCam.transform.LookAt(b.center);

            m_ResultCam.enabled = true;   // URP는 수동 Render() 비신뢰 → 정산 동안 라이브 렌더(512² 저부담)
            return m_ResultRT;
        }

        /// <summary>정산 종료(재시작 등) 시 썸네일 카메라 끄기 — GameLoopHUD가 호출.</summary>
        public void EndResultPreview()
        {
            if (m_ResultCam != null) m_ResultCam.enabled = false;
        }

        // ── 입력 진입점 (클라가 호출 → 서버로) ──────────────────────────────
        public void RequestPlace(Vector3Int anchor, int materialId, byte rot) => PlaceRpc(anchor, materialId, rot);
        public void RequestRemove(Vector3Int cell) => RemoveRpc(cell);
        public void RequestProcess(Vector3Int cell, int processBit, bool apply) => ProcessRpc(cell, processBit, apply);
        public void RequestShock(Vector3Int cell) => ShockRpc(cell);   // 트리거①: 외부충격(플레이어 부딪힘)

        // 2vs2: 요청 셀이 요청자 팀 구역(A: x<W, B: x≥W) 안인지. 협동 모드는 항상 허용.
        private bool ZoneAllowed(ulong sender, Vector3Int cell)
        {
            if (m_Loop == null || !m_Loop.IsVersus) return true;
            int team = m_Loop.GetTeam(sender);
            if (team < 0) return false;
            int half = m_Manager.ZoneSize.x;
            return team == 0 ? cell.x < half : cell.x >= half;
        }

        [Rpc(SendTo.Server)]
        private void PlaceRpc(Vector3Int anchor, int materialId, byte rot, RpcParams rpc = default)
        {
            var mat = m_Manager.Catalog != null ? m_Manager.Catalog.GetById(materialId) : null;
            if (mat == null || !m_ServerGrid.CanPlace(anchor, mat, rot)) return;
            if (!m_ServerGrid.WouldBeSupported(anchor, mat, rot)) return;   // 허공(지지 없음) 배치 거부
            foreach (var c in GridFootprint.EnumerateFootprintCells(anchor, mat.Footprint, rot))
                if (!ZoneAllowed(rpc.Receive.SenderClientId, c)) return;    // 2vs2: 자기 구역에만 배치

            ulong owner = ++m_OwnerCounter;
            m_ServerGrid.Place(anchor, mat, rot, owner);

            // 망치질이 필요 없는 재료(바닥 등 비-하중부재)는 배치 즉시 '고정'(앵커) → 위에 다른 블록 놓아도 안 무너짐.
            int initialMask = mat.MustBeFixed ? 0 : (int)ProcessType.Fixed;
            if (initialMask != 0) m_ServerGrid.TryApplyProcess(anchor, ProcessType.Fixed, mat);

            foreach (var c in GridFootprint.EnumerateFootprintCells(anchor, mat.Footprint, rot))
                m_Cells.Add(new CellEntry
                {
                    cell = c, materialId = materialId, rotationStep = rot,
                    completedProcessMask = initialMask, ownerObjectId = owner
                });

            PlacedFxRpc(CellWorld(anchor));   // 놓기 먼지(모든 클라)

            // 점수 팝업: 정답 칸과 일치한 셀 수 × 200 (틀린 자리는 +0 회색 — 즉각 피드백)
            int gained = 0;
            var ans = m_Manager.Answer;
            if (ans != null)
                foreach (var c in GridFootprint.EnumerateFootprintCells(anchor, mat.Footprint, rot))
                    if (ans.TryGet(c, out var ac) && ac.materialId == materialId) gained += 200;
            ScorePopRpc(CellWorld(anchor) + Vector3.up * (GridContract.Unit * 1.2f), gained, 0);

            // 트리거②: 미고정 오브젝트 위에 놓임 → 그 미고정 지지물(+연쇄) 무너짐
            foreach (var t in m_ServerGrid.FindUnfixedSupportsUnder(owner))
                foreach (var co in m_ServerGrid.Collapse(t))
                    RemoveCollapsed(co);
        }

        [Rpc(SendTo.Everyone)]
        private void ScorePopRpc(Vector3 pos, int amount, byte kind)
        {
            Color c = amount <= 0 ? new Color(0.62f, 0.62f, 0.62f, 1f)          // 회색 = 자리 틀림
                    : kind == 1   ? new Color(0.35f, 0.60f, 1.00f, 1f)           // 파랑 = 공정 점수
                                  : new Color(0.25f, 0.80f, 0.35f, 1f);          // 초록 = 배치 점수
            GridJuice.ScorePop(pos, amount, c);
        }

        [Rpc(SendTo.Server)]
        private void RemoveRpc(Vector3Int cell, RpcParams rpc = default)
        {
            if (!ZoneAllowed(rpc.Receive.SenderClientId, cell)) return;   // 2vs2: 자기 구역만 철거
            var cs = m_ServerGrid.GetCell(cell);
            if (!cs.occupied) return;
            ulong owner = cs.ownerObjectId;
            int materialId = cs.materialId;
            m_ServerGrid.Remove(cell);

            Vector3 from = default; bool have = false;
            for (int i = m_Cells.Count - 1; i >= 0; i--)
                if (m_Cells[i].ownerObjectId == owner)
                {
                    if (!have) { from = CellWorld(m_Cells[i].cell); have = true; }
                    m_Cells.RemoveAt(i);
                }
            if (have && m_DropField != null) m_DropField.ServerDrop(materialId, from);   // 철거 재료를 바닥에 떨굼
            RemovedFxRpc(GridCoordinates.CellToWorld(cell) + new Vector3(0.5f, 0f, 0.5f) * GridContract.Unit);   // 철거 먼지

            foreach (var co in m_ServerGrid.SettleUnsupported())     // 받침 사라짐 → 위 미고정 블록 연쇄
                RemoveCollapsed(co);
        }

        [Rpc(SendTo.Everyone)]
        private void RemovedFxRpc(Vector3 baseCenter) => GridJuice.PlacePuff(baseCenter, GridContract.Unit);

        /// <summary>서버: 미고정 블록을 그리드에서 제거(바닥 드롭 없이) + 재료 id 반환. 좌클릭 집기 전용.</summary>
        public bool ServerPickupBlock(Vector3Int cell, out int materialId)
        {
            materialId = -1;
            if (!IsServer) return false;
            var cs = m_ServerGrid.GetCell(cell);
            if (!cs.occupied) return false;

            // 서버 권위 재검증: 미고정 하중부재만(고정 완료 블록은 C로만)
            var def = m_Manager.Catalog != null ? m_Manager.Catalog.GetById(cs.materialId) : null;
            if (def == null || !def.MustBeFixed || (cs.completedProcessMask & (int)ProcessType.Fixed) != 0)
                return false;

            ulong owner = cs.ownerObjectId;
            materialId = cs.materialId;
            m_ServerGrid.Remove(cell);                       // 같은 owner 전 셀 제거(멀티셀)

            for (int i = m_Cells.Count - 1; i >= 0; i--)
                if (m_Cells[i].ownerObjectId == owner) m_Cells.RemoveAt(i);

            foreach (var co in m_ServerGrid.SettleUnsupported())   // 받침 잃은 위 블록은 기존대로 무너져 드롭
                RemoveCollapsed(co);

            return true;   // 집은 블록 자체는 드롭 X → 손으로
        }

        [Rpc(SendTo.Server)]
        private void ShockRpc(Vector3Int cell)
        {
            var cs = m_ServerGrid.GetCell(cell);
            if (!cs.occupied) return;
            var mat = m_Manager.Catalog != null ? m_Manager.Catalog.GetById(cs.materialId) : null;
            if (mat == null || !mat.MustBeFixed) return;   // 하중부재(기둥/벽)만 충격에 무너짐 — 바닥은 밟아도 OK
            foreach (var co in m_ServerGrid.Collapse(cell)) RemoveCollapsed(co);
        }

        /// <summary>무너진 오브젝트를 복제 리스트에서 제거하고 재료를 바닥에 떨군다(주워서 재배치 가능).</summary>
        /// <summary>[아이템: 지진] 해당 팀 구역에서 '고정 공정'이 안 된 하중부재를 전부 무너뜨린다.
        /// 그 위에 얹혀 있던 것들도 기존 붕괴 규칙대로 연쇄로 무너진다. 서버 전용, 무너진 개수 반환.</summary>
        public int ServerEarthquake(int team)
        {
            if (!IsServer || m_ServerGrid == null) return 0;

            var victims = new System.Collections.Generic.List<Vector3Int>();
            foreach (var e in m_Cells)
            {
                if (!InZone(team, e.cell)) continue;
                if ((e.completedProcessMask & (int)ProcessType.Fixed) != 0) continue;   // 고정된 건 버팀
                var def = m_Manager.Catalog != null ? m_Manager.Catalog.GetById(e.materialId) : null;
                if (def == null || !def.MustBeFixed) continue;                            // 바닥 등 비-하중부재는 제외
                victims.Add(e.cell);
            }

            int collapsed = 0;
            foreach (var cell in victims)
            {
                if (!m_ServerGrid.GetCell(cell).occupied) continue;   // 앞선 연쇄로 이미 사라짐
                foreach (var co in m_ServerGrid.Collapse(cell)) { RemoveCollapsed(co); collapsed++; }
            }
            foreach (var co in m_ServerGrid.SettleUnsupported()) { RemoveCollapsed(co); collapsed++; }

            EarthquakeFxRpc(team);
            return collapsed;
        }

        /// <summary>[아이템: 대포] 해당 팀 구역에서 '배치+공정이 모두 끝난' 파츠 하나를 무작위로 파괴한다.
        /// 위에 얹혀 있던 것들은 기존 붕괴 규칙대로 연쇄로 무너진다. 서버 전용, 파괴 성공 여부 반환.</summary>
        public bool ServerCannonDestroy(int team)
        {
            if (!IsServer || m_ServerGrid == null) return false;

            var targets = new System.Collections.Generic.List<Vector3Int>();
            foreach (var e in m_Cells)
            {
                if (!InZone(team, e.cell)) continue;
                var def = m_Manager.Catalog != null ? m_Manager.Catalog.GetById(e.materialId) : null;
                if (def == null) continue;
                if (!IsFullyProcessed(def, e.completedProcessMask)) continue;   // 완성된 파츠만
                targets.Add(e.cell);
            }
            if (targets.Count == 0) return false;

            var hit = targets[Random.Range(0, targets.Count)];
            CannonHitFxRpc(CellWorld(hit));
            foreach (var co in m_ServerGrid.Collapse(hit)) RemoveCollapsed(co);
            foreach (var co in m_ServerGrid.SettleUnsupported()) RemoveCollapsed(co);
            return true;
        }

        // 그 재료가 요구하는 공정이 전부 끝났는가(채점과 같은 기준인 RequiredMask 사용).
        private static bool IsFullyProcessed(MaterialDef def, int completedMask)
        {
            int need = def.RequiredMask;
            return need != 0 && (completedMask & need) == need;
        }

        [Rpc(SendTo.Everyone)]
        private void CannonHitFxRpc(Vector3 center)
        {
            GridJuice.CollapseBurst(center, GridContract.Unit);
            GridJuice.GroundHit(center, 1.6f);
            GridJuice.FovPunch(Camera.main, -7f);
            GridSoundBridge.PlaySFXAt("LandObject", center);
            GridJuice.WorldToast(center + Vector3.up * (GridContract.Unit * 1.5f),
                                 "대포 명중!", new Color(0.95f, 0.55f, 0.15f));
        }

        /// <summary>[날씨: 강풍·태풍] 해당 팀 구역의 미고정 블록 중 최대 count개를 바람에 무너뜨린다.
        /// 지진처럼 전멸이 아니라 조금씩 갉아먹는 압박. 서버 전용, 무너진 개수 반환.</summary>
        public int ServerWindCollapse(int team, int count)
        {
            if (!IsServer || m_ServerGrid == null || count <= 0) return 0;

            var candidates = new System.Collections.Generic.List<Vector3Int>();
            foreach (var e in m_Cells)
            {
                if (!InZone(team, e.cell)) continue;
                if ((e.completedProcessMask & (int)ProcessType.Fixed) != 0) continue;
                var def = m_Manager.Catalog != null ? m_Manager.Catalog.GetById(e.materialId) : null;
                if (def == null || !def.MustBeFixed) continue;
                candidates.Add(e.cell);
            }
            if (candidates.Count == 0) return 0;

            int collapsed = 0;
            for (int i = 0; i < count && candidates.Count > 0; i++)
            {
                int pick = Random.Range(0, candidates.Count);
                var cell = candidates[pick];
                candidates.RemoveAt(pick);
                if (!m_ServerGrid.GetCell(cell).occupied) continue;
                foreach (var co in m_ServerGrid.Collapse(cell)) { RemoveCollapsed(co); collapsed++; }
            }
            foreach (var co in m_ServerGrid.SettleUnsupported()) { RemoveCollapsed(co); collapsed++; }
            return collapsed;
        }

        // 협동 모드에는 구역이 없다 → 전부 대상.
        private bool InZone(int team, Vector3Int cell)
        {
            if (m_Loop == null || !m_Loop.IsVersus) return true;
            int half = m_Manager.ZoneSize.x;
            return team == 0 ? cell.x < half : cell.x >= half;
        }

        // 지진 연출: 맞은 팀은 화면이 크게 흔들리고, 상대는 약하게(무슨 일이 났는지 알 수 있게).
        [Rpc(SendTo.Everyone)]
        private void EarthquakeFxRpc(int team)
        {
            bool mine = m_Loop == null || !m_Loop.IsVersus || m_Loop.LocalTeam == team;
            GridJuice.FovPunch(Camera.main, mine ? -9f : -2f);

            var center = GridCoordinates.CellToWorld(
                new Vector3Int(m_Manager.ZoneSize.x / 2 + (team == 1 ? m_Manager.ZoneSize.x : 0), 1, m_Manager.ZoneSize.z / 2));
            GridSoundBridge.PlaySFXAt("LandObject", center);
            if (mine) GridJuice.WorldToast(center + Vector3.up * (GridContract.Unit * 2f),
                                           "지진! 고정 안 한 블록이 무너져요!", new Color(0.85f, 0.35f, 0.15f));
        }

        private void RemoveCollapsed(CollapsedObject co)
        {
            Vector3 from = default; bool have = false;
            for (int i = m_Cells.Count - 1; i >= 0; i--)
                if (m_Cells[i].ownerObjectId == co.ownerObjectId)
                {
                    if (!have) { from = CellWorld(m_Cells[i].cell); have = true; }
                    m_Cells.RemoveAt(i);
                }
            if (have && m_DropField != null) m_DropField.ServerDrop(co.materialId, from);
            if (have) CollapsedFxRpc(from);   // 붕괴 잔해·먼지·카메라 흔들림(모든 클라)
        }

        private static Vector3 CellWorld(Vector3Int cell)
            => GridCoordinates.CellToWorld(cell) + Vector3.one * 0.5f * GridContract.Unit;

        // ── 게임필 FX(서버 발화 → 모든 클라 동기화). 실제 블록 비주얼과 분리(RebuildVisuals 재생성에 안 흔들림). ──
        [Rpc(SendTo.Everyone)]
        private void PlacedFxRpc(Vector3 baseCenter) => GridJuice.PlacePuff(baseCenter, GridContract.Unit);

        [Rpc(SendTo.Everyone)]
        private void CollapsedFxRpc(Vector3 center)
        {
            GridJuice.CollapseBurst(center, GridContract.Unit);
            GridJuice.GroundHit(center, 1.3f);      // 바닥 흙폭발
            GridJuice.FovPunch(Camera.main, -5f);   // 우르릉 — 화면 살짝 당김
            GridSoundBridge.PlaySFXAt("LandObject", center);   // 무너지는 소리(돌 낙하음)
            GridJuice.WorldToast(center + Vector3.up * (GridContract.Unit * 1.2f),   // 무너진 블록 바로 위에
                "앗! 무너졌어요!", new Color(0.90f, 0.25f, 0.20f));

            // 젤리 파동: 출렁임이 중심에서 주변 블록으로 번져나감
            if (m_VisualRoot != null)
                GridJuice.Ripple(m_VisualRoot.transform, center, GridContract.Unit * 4f, 0.10f, 8f);
        }

        /// <summary>지점 주변 젤리 파동(고정 완료 등 로컬 연출용).</summary>
        public void RippleAround(Vector3 center, float radius, float amount)
        {
            if (m_VisualRoot != null)
                GridJuice.Ripple(m_VisualRoot.transform, center, radius, amount);
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>[개발자 치트] 정답을 그리드에 통째로 심어 즉시 100% 완성(완성 연출 테스트용).</summary>
        public void RequestCheatComplete() => CheatCompleteRpc(false);
        /// <summary>[개발자 치트] 마지막 블록 1개만 빼고 완성(≈99%) — 만점 아닐 때 폭죽 오발화 검증용.</summary>
        public void RequestCheatAlmost() => CheatCompleteRpc(true);

        [Rpc(SendTo.Server)]
        private void CheatCompleteRpc(bool leaveOneOut)
        {
            var ans = m_Manager.Answer;
            var catalog = m_Manager.Catalog;
            if (ans == null || catalog == null) return;

            m_ServerGrid = new RuntimeGrid(m_Manager.GridSize);   // 그리드 리셋
            m_ServerGrid.ExternalSupportBelow = c => GridSupport.ExternalSolidAt(c, GridContract.Unit);
            m_OwnerCounter = 0;
            for (int i = m_Cells.Count - 1; i >= 0; i--) m_Cells.RemoveAt(i);

            // 정답을 블록 단위로 재구성 (leaveOneOut이면 맨 마지막 블록 1개 스킵)
            var cells = new List<AnswerCell>(ans.Cells);
            cells.Sort((a, c) => a.cell.x != c.cell.x ? a.cell.x - c.cell.x
                               : a.cell.y != c.cell.y ? a.cell.y - c.cell.y : a.cell.z - c.cell.z);
            var claimed = new HashSet<Vector3Int>();
            var blocks = new List<(Vector3Int anchor, MaterialDef def, int rot, List<Vector3Int> fcells)>();
            foreach (var a in cells)
            {
                if (claimed.Contains(a.cell)) continue;
                var def = catalog.GetById(a.materialId);
                if (def == null) { claimed.Add(a.cell); continue; }
                int rot = a.rotationStep;
                var fcells = GridFootprint.EnumerateFootprintCells(a.cell, def.Footprint, rot);

                bool ok = true;
                foreach (var fc in fcells)
                    if (claimed.Contains(fc) || !ans.TryGet(fc, out var ac) || ac.materialId != a.materialId || ac.rotationStep != rot)
                    { ok = false; break; }
                if (!ok) { claimed.Add(a.cell); continue; }
                foreach (var fc in fcells) claimed.Add(fc);
                blocks.Add((a.cell, def, rot, fcells));
            }

            int count = leaveOneOut ? blocks.Count - 1 : blocks.Count;   // 하나 빼기
            for (int i = 0; i < count; i++)
            {
                var (anchor, def, rot, fcells) = blocks[i];
                ulong owner = ++m_OwnerCounter;
                if (!m_ServerGrid.Place(anchor, def, rot, owner)) continue;
                foreach (var p in ProcessOrder.Sequence)
                    if ((def.RequiredMask & (int)p) != 0) m_ServerGrid.TryApplyProcess(anchor, p, def);
                int mask = m_ServerGrid.GetCell(anchor).completedProcessMask;
                foreach (var fc in fcells)
                    m_Cells.Add(new CellEntry { cell = fc, materialId = def.Id, rotationStep = (byte)rot, completedProcessMask = mask, ownerObjectId = owner });
            }
            RecomputeScore();
        }
#endif

        [Rpc(SendTo.Server)]
        private void ProcessRpc(Vector3Int cell, int processBit, bool apply, RpcParams rpc = default)
        {
            if (!ZoneAllowed(rpc.Receive.SenderClientId, cell)) return;   // 2vs2: 자기 구역만 공정
            ApplyProcessServer(cell, (ProcessType)processBit, apply);
        }

        public void RequestCancelLast(Vector3Int cell) => CancelLastRpc(cell);

        [Rpc(SendTo.Server)]
        private void CancelLastRpc(Vector3Int cell)
        {
            var cs = m_ServerGrid.GetCell(cell);
            if (!cs.occupied) return;
            // 역순으로 완료된 마지막 공정을 취소(서버가 상태를 알므로 클라는 셀만 지정)
            for (int i = ProcessOrder.Sequence.Length - 1; i >= 0; i--)
            {
                var p = ProcessOrder.Sequence[i];
                if ((cs.completedProcessMask & (int)p) != 0) { ApplyProcessServer(cell, p, false); return; }
            }
        }

        private void ApplyProcessServer(Vector3Int cell, ProcessType proc, bool apply)
        {
            var cs = m_ServerGrid.GetCell(cell);
            if (!cs.occupied) return;
            var mat = m_Manager.Catalog != null ? m_Manager.Catalog.GetById(cs.materialId) : null;

            bool ok = apply ? m_ServerGrid.TryApplyProcess(cell, proc, mat)
                            : m_ServerGrid.TryCancelProcess(cell, proc);
            if (!ok) return;

            ulong owner = cs.ownerObjectId;
            int newMask = m_ServerGrid.GetCell(cell).completedProcessMask;
            for (int i = 0; i < m_Cells.Count; i++)
                if (m_Cells[i].ownerObjectId == owner)
                {
                    var e = m_Cells[i];
                    e.completedProcessMask = newMask;
                    m_Cells[i] = e;   // 값 변경 → 복제
                }

            // 공정 점수 팝업: 요구 공정이 '방금 완성'됐고 정답 자리에 있는 셀 수 × 100
            int req = mat != null ? mat.RequiredMask : 0;
            if (apply && req != 0 && (newMask & req) == req)
            {
                int gained = 0;
                var ans = m_Manager.Answer;
                if (ans != null)
                    for (int i = 0; i < m_Cells.Count; i++)
                        if (m_Cells[i].ownerObjectId == owner
                            && ans.TryGet(m_Cells[i].cell, out var ac) && ac.materialId == cs.materialId)
                            gained += 100;
                if (gained > 0)
                    ScorePopRpc(CellWorld(cell) + Vector3.up * (GridContract.Unit * 1.4f), gained, 1);
            }
        }

        // ── 비주얼 (모든 클라이언트가 리스트로 재구성) ───────────────────────
        private void OnCellsChanged(NetworkListEvent<CellEntry> _)
        {
            RebuildVisuals();
            if (IsServer) RecomputeScore();
        }

        public void RecomputeScore()
        {
            if (m_Manager.Answer == null) return;
            var s = m_ServerGrid.ScoreAgainst(m_Manager.Answer, m_Manager.Catalog);   // 협동=전체 / 2vs2=팀A 구역
            m_Score.Value = Snap(s);
            if (m_Loop != null && m_Loop.IsVersus)   // 팀B: 같은 정답을 구역폭만큼 밀어서 채점
                m_ScoreB.Value = Snap(m_ServerGrid.ScoreAgainst(m_Manager.Answer, m_Manager.Catalog, new Vector3Int(m_Manager.ZoneSize.x, 0, 0)));
        }

        private static ScoreSnapshot Snap(GridScore s) => new ScoreSnapshot
        {
            score = s.score, maxScore = s.maxScore, answerCells = s.answerCellCount,
            placedCorrect = s.placedCorrect, processCorrect = s.processCorrect,
        };

        /// <summary>게임 재시작용: 서버 그리드·복제 리스트를 비운다(→ 비주얼/점수 자동 0 갱신).</summary>
        public void ServerResetGrid()
        {
            if (!IsServer) return;
            m_ServerGrid = new RuntimeGrid(m_Manager.GridSize);
            m_ServerGrid.ExternalSupportBelow = c => GridSupport.ExternalSolidAt(c, GridContract.Unit);
            m_OwnerCounter = 0;
            for (int i = m_Cells.Count - 1; i >= 0; i--) m_Cells.RemoveAt(i);
            if (m_DropField != null) m_DropField.ServerReset();   // 바닥 재료도 정리
            RecomputeScore();   // 새 정답 기준으로 점수 즉시 재계산(빈 그리드라 OnCellsChanged가 안 떠도)
        }

        private void RebuildVisuals()
        {
            if (m_VisualRoot == null) return;
            foreach (Transform t in m_VisualRoot.transform) Destroy(t.gameObject);

            float u = GridContract.Unit;
            var catalog = m_Manager.Catalog;

            // 오브젝트(owner)별 집계: min-corner(프리팹 정렬) + 중심·꼭대기(공정 마커 위치) + 재료/완료공정
            var agg = new Dictionary<ulong, OwnerAgg>();
            foreach (var e in m_Cells)
            {
                Vector3 center = GridCoordinates.CellToWorld(e.cell) + Vector3.one * 0.5f * u;
                float top = GridCoordinates.CellToWorld(e.cell).y + u;
                if (agg.TryGetValue(e.ownerObjectId, out var a))
                {
                    a.minCell = Vector3Int.Min(a.minCell, e.cell);
                    a.sumCenter += center; a.count++;
                    a.topY = Mathf.Max(a.topY, top);
                    agg[e.ownerObjectId] = a;
                }
                else agg[e.ownerObjectId] = new OwnerAgg
                {
                    minCell = e.cell, sumCenter = center, count = 1, topY = top,
                    materialId = e.materialId, completedMask = e.completedProcessMask,
                };
            }

            var done = new HashSet<ulong>();
            foreach (var e in m_Cells)
            {
                bool isNew = !m_SeenVisualOwners.Contains(e.ownerObjectId);   // 이번에 처음 등장한 블록 → 놓기 팝
                var def = catalog != null ? catalog.GetById(e.materialId) : null;
                if (def != null && def.Prefab != null)
                {
                    if (!done.Add(e.ownerObjectId)) continue;   // 오브젝트당 프리팹 1개
                    var vgo = SpawnPrefabVisual(def, e.rotationStep, agg[e.ownerObjectId].minCell);
                    if (isNew) GridJuice.Squish(vgo, 0.12f);    // 쿵 하고 안착(모든 클라)
                }
                else
                {
                    // 프리팹 없음 → 칸마다 색칠 큐브(완료 공정 색)
                    var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    cube.transform.SetParent(m_VisualRoot.transform, true);
                    cube.transform.position = GridCoordinates.CellToWorld(e.cell) + Vector3.one * 0.5f * u;
                    cube.transform.localScale = Vector3.one * (u * 0.95f);
                    var col = cube.GetComponent<Collider>();
                    if (col != null) col.isTrigger = true;   // 물리는 ~Solid가 담당, 트리거는 시야가림 페이드용
                    SetColor(cube, ColorForMask(e.completedProcessMask));
                    if (isNew) GridJuice.Squish(cube, 0.12f);
                }
            }

            // 다음 재구성에서 '새 블록'을 구분하기 위해 현재 owner 집합 기억
            m_SeenVisualOwners.Clear();
            foreach (var e in m_Cells) m_SeenVisualOwners.Add(e.ownerObjectId);

            // 공정 마커: 아직 할 공정이 남은 블록 위에 색 점(파랑=고정 필요 / 초록=페인트 필요). 다 되면 안 띄움.
            foreach (var a in agg.Values)
            {
                var def = catalog != null ? catalog.GetById(a.materialId) : null;
                var next = NextNeeded(def != null ? def.RequiredMask : 0, a.completedMask);
                if (next == ProcessType.None) continue;
                var pos = new Vector3(a.sumCenter.x / a.count, a.topY + 0.35f, a.sumCenter.z / a.count);
                SpawnProcessMarker(pos, next);
            }

            // 단단함: 미고정 하중부재(공정 전)만 통과(부딪혀 무너뜨림). 그 외(바닥·물·공정완료 전부)는 막음.
            // 플레이어는 중력+캡슐 → 막힌 블록 '위에 서고' '옆을 못 지나감'. (Walkable은 Y고정 시절 잔재 — 더는 통과시키지 않음)
            foreach (var e in m_Cells)
            {
                var def = catalog != null ? catalog.GetById(e.materialId) : null;
                if (def == null) continue;
                if (def.MustBeFixed && (e.completedProcessMask & (int)ProcessType.Fixed) == 0) continue;   // 미고정 하중부재 → 통과(무너뜨림)
                AddCellCollider(e.cell, u);                                                                 // 그 외 전부 → 막음
            }
        }

        /// <summary>cell을 덮는 블록 비주얼 루트(프리팹/큐브). 없으면 null — 스퀴시 등 쫀득 연출용.</summary>
        public GameObject VisualAt(Vector3Int cell)
        {
            if (m_VisualRoot == null) return null;
            Vector3 p = GridCoordinates.CellToWorld(cell) + Vector3.one * 0.5f * GridContract.Unit;
            foreach (Transform t in m_VisualRoot.transform)
            {
                var rends = t.GetComponentsInChildren<Renderer>();
                if (rends.Length == 0) continue;   // 콜라이더 전용(~Solid 등)은 건너뜀
                Bounds b = rends[0].bounds;
                for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
                b.Expand(0.05f);
                if (b.Contains(p)) return t.gameObject;
            }
            return null;
        }

        private readonly HashSet<ulong> m_SeenVisualOwners = new();   // 놓기 팝용: 직전 재구성까지 있던 블록들

        private struct OwnerAgg
        {
            public Vector3Int minCell;
            public Vector3 sumCenter;
            public int count;
            public float topY;
            public int materialId;
            public int completedMask;
        }

        // 진짜 블록 프리팹을 점유 칸에 맞춰 1개 인스턴스. 프리팹 피벗=바닥 → X/Z만 중심, Y는 셀 바닥에 안착.
        private GameObject SpawnPrefabVisual(MaterialDef def, int rot, Vector3Int minCell)
        {
            float u = GridContract.Unit;
            var go = Instantiate(def.Prefab, m_VisualRoot.transform);
            // 피벗=min-corner 가정 + 메시가 footprint와 90° 다르면 자동 보정.
            GridFootprint.PlaceRotatedPrefab(go, GridCoordinates.CellToWorld(minCell), def.Footprint, rot, u);
            // 물리는 ~Solid가 담당 → 프리팹 콜라이더 제거. 단, 카메라 시야가림 페이드(CameraObstructionFader)가
            // 잡을 수 있게 렌더러마다 메시 AABB 트리거 콜라이더를 부여(트리거라 충돌·지지·집기 레이엔 안 걸림).
            foreach (var c in go.GetComponentsInChildren<Collider>()) Destroy(c);
            foreach (var mr in go.GetComponentsInChildren<MeshRenderer>())
            {
                var mf = mr.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var box = mr.gameObject.AddComponent<BoxCollider>();
                box.center = mf.sharedMesh.bounds.center;
                box.size = mf.sharedMesh.bounds.size;
                box.isTrigger = true;
            }
            return go;
        }

        // 칸 하나를 막는 보이지 않는 BoxCollider(렌더러 없음). 칸 실제 크기 = 중력 플레이어가 위에 정확히 서고 옆을 못 지나감.
        private void AddCellCollider(Vector3Int cell, float u)
        {
            var go = new GameObject("~Solid");
            go.transform.SetParent(m_VisualRoot.transform, true);
            go.transform.position = GridCoordinates.CellToWorld(cell) + Vector3.one * 0.5f * u;   // 칸 중심
            go.AddComponent<BoxCollider>().size = Vector3.one * u;                                 // 칸 크기
        }

        // 공정이 더 필요한 블록 위에 띄우는 도구 모델(고정=망치 / 페인트=페인트통). 모델 없으면 색 점 폴백. 충돌 없음.
        private void SpawnProcessMarker(Vector3 pos, ProcessType next)
        {
            var model = next == ProcessType.Painted ? (m_DropField != null ? m_DropField.PaintModel  : null)
                      : next == ProcessType.Fixed   ? (m_DropField != null ? m_DropField.HammerModel : null)
                      : null;
            // 마커는 JuiceBob이 둥실둥실(아래 생성 후 부착)

            GameObject go;
            float scale;
            if (model != null)
            {
                go = Instantiate(model);
                foreach (var c in go.GetComponentsInChildren<Collider>()) Destroy(c);
                scale = m_MarkerScale;
            }
            else   // 폴백: 색 점
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                var col = go.GetComponent<Collider>();
                if (col != null) Destroy(col);
                SetColor(go, ColorForMask((int)next));
                scale = 0.35f;
            }

            go.name = "~ProcMarker";
            go.transform.SetParent(m_VisualRoot.transform, false);
            go.transform.localScale = Vector3.one * scale;
            go.transform.position = pos;
            go.AddComponent<JuiceBob>();   // 둥실둥실 + 회전 — "나 눌러줘" 어필
        }

        // 고정 → 페인트 순서로 첫 미완료 필수 공정(없으면 None).
        private static ProcessType NextNeeded(int reqMask, int completedMask)
        {
            foreach (var p in ProcessOrder.Sequence)
            {
                int pb = (int)p;
                if ((reqMask & pb) != 0 && (completedMask & pb) == 0) return p;
            }
            return ProcessType.None;
        }

        private static Color ColorForMask(int mask)
        {
            if ((mask & (int)ProcessType.Painted) != 0) return new Color(0.30f, 0.85f, 0.40f);
            if ((mask & (int)ProcessType.Fixed) != 0)   return new Color(0.35f, 0.60f, 1.00f);
            return new Color(0.72f, 0.72f, 0.72f);
        }

        private static readonly int s_BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int s_Color = Shader.PropertyToID("_Color");
        private static void SetColor(GameObject go, Color c)
        {
            var r = go.GetComponent<Renderer>();
            if (r == null) return;
            var mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            mpb.SetColor(s_BaseColor, c);
            mpb.SetColor(s_Color, c);
            r.SetPropertyBlock(mpb);
        }
    }

    /// <summary>채점 분해 스냅샷(복제용). RuntimeGrid.GridScore를 네트워크로 노출한다.</summary>
    public struct ScoreSnapshot : INetworkSerializable, System.IEquatable<ScoreSnapshot>
    {
        public int score, maxScore, answerCells, placedCorrect, processCorrect;

        public float Percent => maxScore > 0 ? (float)score / maxScore * 100f : 0f;

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref score);
            s.SerializeValue(ref maxScore);
            s.SerializeValue(ref answerCells);
            s.SerializeValue(ref placedCorrect);
            s.SerializeValue(ref processCorrect);
        }

        public bool Equals(ScoreSnapshot o)
            => score == o.score && maxScore == o.maxScore && answerCells == o.answerCells
            && placedCorrect == o.placedCorrect && processCorrect == o.processCorrect;
    }
}
