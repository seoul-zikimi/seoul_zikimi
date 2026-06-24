using System.Collections.Generic;
using GridSystem;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

namespace Player
{
    /// <summary>
    /// 들기 + 배치 + 공정. 한 번에 '재료' 또는 '도구' 하나만 든다(협동 제약).
    /// Space 점프 · 좌클릭 집기/배치/내려놓기(토글) · 우클릭 철거 · E(꾹) 공정(든 도구를 로딩바로 적용) · C 공정취소
    /// · R 회전 · Q 층내림 / E톡 층올림 · Space 바닥에 버리기. (어떤 블록에 뭐가 필요한지는 TAB 정답 안내로 확인)
    /// 든 상태는 NetworkVariable로 복제 → 모든 클라가 머리 위 비주얼 재구성(원격도 보임).
    /// </summary>
    public class PlayerCarry : NetworkBehaviour
    {
        [SerializeField] private Vector3 m_HoldOffset = new Vector3(0f, 2.2f, 0f);
        [Tooltip("바닥 재료 줍기 / 작업장 도구 집기 거리.")]
        [FormerlySerializedAs("m_WorkstationRange")]
        [SerializeField] private float m_GrabRange = 2.5f;
        private bool        m_GrabValid;
        private PickupBody  m_GrabBody;     // 레이캐스트로 가리킨 바닥 픽업(소속·정체 보유)
        private Workstation m_GrabStation;  // 레이캐스트로 가리킨 도구함(있으면 그 도구를 집음)
        private GameObject  m_HlGo;         // 현재 테두리 중인 오브젝트(대상 바뀌면 끔)
        [Tooltip("공정 한 단계를 끝내려고 E를 눌러야 하는 시간(초). 로딩바가 차는 속도.")]
        [SerializeField] private float m_ProcessSeconds = 1.2f;
        [Tooltip("재료를 던질 수 있는 최대 거리(칸). 조준점이 더 멀면 이 거리까지만 날아간다.")]
        [SerializeField] private float m_ThrowRange = 6f;

        // 복제 상태(owner write): 든 재료 id(-1=없음) / 든 도구 비트(0=없음)
        private readonly NetworkVariable<int> m_NetMaterialId =
            new(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        private readonly NetworkVariable<int> m_NetTool =
            new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        private int m_Rotation;
        private int m_BuildHeight;
        private MaterialDef m_HeldMaterial;   // owner 로직용
        private ProcessType m_HeldTool;       // owner 로직용(0=없음)
        private GameObject m_HeldVisual;      // 모든 클라 비주얼

        private Camera m_Cam;
        private GridManager m_Grid;
        private MaterialCatalog m_Catalog;
        private GridNetwork m_Net;
        private GameLoopManager m_Loop;
        private MaterialDropField m_Drop;
        private Vector3Int m_Target;
        private bool m_HasTarget;
        private GUIStyle m_HudStyle, m_BarLabel;
        private static readonly Vector3Int s_NoCell = new Vector3Int(int.MinValue, int.MinValue, int.MinValue);
        private Vector3Int m_LastShockCell = s_NoCell;   // 같은 셀 안에 있는 동안 충격 중복 전송 방지

        // E 꾹 공정(로딩바): 든 '도구'가 조준 블록의 '지금 필요한 공정'과 맞으면 누적시간으로 적용.
        private float m_ProcessHold;                   // 0..m_ProcessSeconds 누적
        private Vector3Int m_ProcessCell = s_NoCell;   // 현재 진행 중인 셀
        private ProcessType m_ProcessKind;             // 진행 중인 공정(바 라벨) = 든 도구
        private Vector3Int m_PendingCell = s_NoCell;   // 방금 적용→복제 대기 중인 셀(중복 적용 방지)
        private ProcessType m_PendingKind;             // 그 공정(복제 반영되면 해제)
        private string m_ProcessHint = "";             // 도구 들고 조준 시 "지금 무슨 공정 차례" 안내
        private float m_EHeldTime;                     // E 누른 시간(톡 vs 꾹 판별)
        private bool m_EProcessed;                     // 이번 E 누름에서 공정이 적용됐나(떼도 층 올림 방지)
        private const float kTapMax = 0.25f;           // 이보다 짧게 떼면 '톡'(층 올림), 길면 '꾹'(공정)

        // 킥(노답중력): 몸에 닿은(근접) 바닥 재료를 찬다. 줍기 범위(grab)보다 좁아 살짝 떨어져선 좌클릭으로 줍기 가능.
        private const float kKickRadius = 0.8f;
        private readonly HashSet<ulong> m_Touching = new();
        private readonly List<ulong> m_KickIds = new();
        private readonly List<Vector3> m_KickPos = new();

        private bool HasMaterial => m_HeldMaterial != null;
        private bool HasTool => m_HeldTool != ProcessType.None;

        public override void OnNetworkSpawn()
        {
            m_NetMaterialId.OnValueChanged += OnHeldChanged;
            m_NetTool.OnValueChanged += OnHeldChanged;
            RebuildHeldVisual();                 // 초기/늦참
            if (IsOwner) m_Cam = Camera.main;
        }

        public override void OnNetworkDespawn()
        {
            m_NetMaterialId.OnValueChanged -= OnHeldChanged;
            m_NetTool.OnValueChanged -= OnHeldChanged;
            if (m_HeldVisual != null) Destroy(m_HeldVisual);
            if (m_LineMat != null) Destroy(m_LineMat);
        }

        private void OnHeldChanged(int _, int __) => RebuildHeldVisual();

        private void Update()
        {
            // 모든 클라: 든 비주얼이 플레이어를 따라감
            if (m_HeldVisual != null)
                m_HeldVisual.transform.position = transform.position + m_HoldOffset;

            if (!IsOwner) return;
            OwnerUpdate();
        }

        // ── 소유자 입력 ────────────────────────────────────────────────────
        private void OwnerUpdate()
        {
            if (m_Cam == null) m_Cam = Camera.main;
            if (m_Grid == null) m_Grid = FindFirstObjectByType<GridManager>();   // 씬 전환 뒤 재탐색
            if (m_Net == null) m_Net = FindFirstObjectByType<GridNetwork>();
            if (m_Loop == null) m_Loop = FindFirstObjectByType<GameLoopManager>();
            if (m_Drop == null) m_Drop = FindFirstObjectByType<MaterialDropField>();

            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null || mouse == null) return;

            if (kb.rKey.wasPressedThisFrame) m_Rotation = (m_Rotation + 1) & 3;
            if (m_Grid != null && kb.qKey.wasPressedThisFrame) m_BuildHeight = Mathf.Max(0, m_BuildHeight - 1);   // Q = 층 내림 (올림은 E 톡)
            if (kb.gKey.wasPressedThisFrame) Throw();     // 든 재료를 조준 방향으로 던지기(협동 전달)
            // Space는 점프(PlayerInputHandler). 집기·배치·내려놓기는 좌클릭으로 통합.

            UpdateTarget();
            UpdateGrabTarget();   // 빈손이면 near+aim 집기 대상 산출(하이라이트·집기 공용)

            if (kb.cKey.wasPressedThisFrame && m_HasTarget && m_Net != null)
                m_Net.RequestCancelLast(m_Target);

            // 정답 패널 위에선 마우스 클릭이 게임 조작이 아니라 정답 카메라 조작 → 게임 클릭 무시.
            if (!AnswerPanelFocus.Active)
            {
                // 좌클릭: 빈손→집기 / 재료→건축 배치. (도구 들고 좌클릭은 무동작 — 도구는 우클릭으로 버림)
                if (mouse.leftButton.wasPressedThisFrame)
                {
                    if (HasMaterial)   TryPlace();
                    else if (!HasTool) TryGrab();
                }
                // 우클릭: 도구 들고 있으면 발밑에 버리기, 그 외엔 철거.
                if (mouse.rightButton.wasPressedThisFrame)
                {
                    if (HasTool) Drop();
                    else         TryRemove();
                }
            }

            UpdateEKey(kb);          // E 톡=층 올림 / E 꾹=공정(로딩바)
            UpdateProcessHint();     // 도구 들었을 때 "지금 무슨 공정 차례인지" 안내 갱신

            TryBumpCollapse();   // C3: 미고정 기둥/벽에 몸으로 부딪히면 무너뜨림
            TryKickPickups();    // 노답중력: 몸에 닿은 바닥 재료를 찬다
        }

        // E: 짧게 '톡' 누르면 층 올림, 길게 '꾹' 누르면 공정(로딩바). 한 키에 톡/꾹을 누른 시간으로 구분한다.
        // 꾹: '든 도구'가 조준 블록에 필요할 때만 바가 차고, 다 차면 그 공정을 적용(누른 채로 다음 단계 이어짐).
        private void UpdateEKey(Keyboard kb)
        {
            if (kb.eKey.wasReleasedThisFrame)
            {
                // 짧게 떼고(꾹 아님) 공정도 안 했으면 → 층 올림(톡)
                if (m_EHeldTime > 0f && m_EHeldTime < kTapMax && !m_EProcessed && m_Grid != null)
                    m_BuildHeight = Mathf.Min(m_Grid.GridSize.y - 1, m_BuildHeight + 1);
                m_EHeldTime = 0f; m_EProcessed = false;
                m_ProcessHold = 0f; m_ProcessCell = s_NoCell;
                return;
            }
            if (!kb.eKey.isPressed)
            {
                m_EHeldTime = 0f; m_EProcessed = false;
                m_ProcessHold = 0f; m_ProcessCell = s_NoCell;
                return;
            }

            m_EHeldTime += Time.deltaTime;
            if (!ToolReadyOnTarget())   // 지금 공정 불가(도구 없음/안 맞음/빈 칸) → 바 안 참(톡이면 층 올림은 release에서)
            {
                m_ProcessHold = 0f; m_ProcessCell = s_NoCell;
                return;
            }

            if (m_Target != m_ProcessCell) { m_ProcessCell = m_Target; m_ProcessHold = 0f; }   // 셀 바뀌면 처음부터
            m_ProcessKind = m_HeldTool;
            m_ProcessHold += Time.deltaTime;

            if (m_ProcessHold >= m_ProcessSeconds)
            {
                m_Net.RequestProcess(m_ProcessCell, (int)m_HeldTool, true);   // 서버가 점유/순서 재검증
                PlaySFX(m_HeldTool == ProcessType.Painted ? SFXType.Painting : SFXType.Hammering);
                m_PendingCell = m_ProcessCell;   // 복제 반영 전까지 같은 공정 재적용 방지
                m_PendingKind = m_HeldTool;
                m_ProcessHold = 0f;
                m_EProcessed = true;             // 이번 누름에 공정 적용 → 떼도 층 올림 안 함
            }
        }

        // 든 도구의 공정이 조준 블록의 '지금 필요한 다음 공정'과 일치하면 true. (서버 수락 조건과 동일 판단)
        private bool ToolReadyOnTarget()
        {
            if (!HasTool) return false;                                  // 도구를 들어야 공정 가능
            if (m_Loop != null && !m_Loop.IsBuilding) return false;
            if (!m_HasTarget || m_Net == null) return false;
            if (!m_Net.TryGetCell(m_Target, out int matId, out int completed)) return false;   // 빈 칸이면 공정 없음

            // 방금 보낸 공정이 아직 복제 안 됨 → 잠깐 대기(바 멈춤, 중복 적용 방지)
            if (m_PendingCell == m_Target && (completed & (int)m_PendingKind) == 0) return false;
            m_PendingCell = s_NoCell; m_PendingKind = ProcessType.None;   // 반영됨/다른셀 → 대기 해제

            var def = Catalog() != null ? Catalog().GetById(matId) : null;
            int req = def != null ? def.RequiredMask : 0;
            return NextNeeded(req, completed) == m_HeldTool;   // 든 도구가 지금 필요한 공정과 같아야
        }

        // 고정 → 페인트 순서대로 '첫 미완료 필수 공정'(없으면 None).
        private static ProcessType NextNeeded(int reqMask, int completedMask)
        {
            foreach (var p in ProcessOrder.Sequence)
            {
                int pb = (int)p;
                if ((reqMask & pb) != 0 && (completedMask & pb) == 0) return p;
            }
            return ProcessType.None;
        }

        // 도구를 들고 블록을 조준할 때 "지금 무슨 공정 차례 / 든 도구가 맞는지"를 안내(공정 순서 혼동 방지).
        private void UpdateProcessHint()
        {
            m_ProcessHint = "";
            if (!HasTool || !m_HasTarget || m_Net == null) return;
            if (m_Loop != null && !m_Loop.IsBuilding) return;
            if (!m_Net.TryGetCell(m_Target, out int matId, out int completed)) { m_ProcessHint = "빈 칸 — 블록을 가리키세요"; return; }

            var def = Catalog() != null ? Catalog().GetById(matId) : null;
            int req = def != null ? def.RequiredMask : 0;
            var next = NextNeeded(req, completed);
            if (next == ProcessType.None)
                // 다음 필요 공정이 없음 — 든 도구가 애초에 필요 없는 공정이면 그렇게 알려준다(혼동 방지).
                m_ProcessHint = (req & (int)m_HeldTool) == 0
                    ? $"이 블록엔 {ProcName(m_HeldTool)} 공정이 필요 없어요"
                    : "이 블록은 공정이 다 됐어요";
            else if (next == m_HeldTool)       m_ProcessHint = $"E 꾹 → {ProcName(next)}";
            else                               m_ProcessHint = $"먼저 {ProcName(next)} 차례 — 지금 든 건 {ProcName(m_HeldTool)}";
        }

        private static string ProcName(ProcessType p)
            => p == ProcessType.Painted ? "페인트(페인트통/초록)" : "고정(망치/파랑)";

        // 근접 진입한 바닥 재료를 '닿은 순간' 1회 찬다(서버가 그 방향으로 굴림).
        private void TryKickPickups()
        {
            if (m_Drop == null) return;
            m_Drop.CollectWithin(transform.position, kKickRadius, m_KickIds, m_KickPos);

            for (int i = 0; i < m_KickIds.Count; i++)
            {
                if (m_Touching.Contains(m_KickIds[i])) continue;   // 이미 닿아있던 건 다시 안 참
                Vector3 d = m_KickPos[i] - transform.position; d.y = 0f;
                if (d.sqrMagnitude < 1e-4f) d = transform.forward;
                m_Drop.RequestKick(m_KickIds[i], d.normalized);
            }

            m_Touching.Clear();
            for (int i = 0; i < m_KickIds.Count; i++) m_Touching.Add(m_KickIds[i]);
        }

        // 플레이어가 점유 셀에 들어가면 서버에 충격 전송(서버가 하중부재·미고정만 무너뜨림).
        // 콜라이더 없이 통과하므로 '셀 진입 = 부딪힘'으로 근사. 같은 셀 안에선 1회만.
        private void TryBumpCollapse()
        {
            if (m_Net == null) return;
            if (m_Loop != null && !m_Loop.IsBuilding) return;

            var pc = GridCoordinates.WorldToCell(transform.position);
            if (!m_Net.IsCellFree(pc))
            {
                if (pc != m_LastShockCell) { m_LastShockCell = pc; m_Net.RequestShock(pc); }
            }
            else m_LastShockCell = s_NoCell;
        }

        private void UpdateTarget()
        {
            m_HasTarget = false;
            if (m_Cam == null || m_Grid == null) return;

            float planeY = GridContract.Origin.y + m_BuildHeight * GridContract.Unit;
            var plane = new Plane(Vector3.up, new Vector3(0f, planeY, 0f));
            var ray = m_Cam.ScreenPointToRay(Mouse.current.position.ReadValue());
            if (plane.Raycast(ray, out float d))
            {
                var c = GridCoordinates.WorldToCell(ray.GetPoint(d));
                c.y = m_BuildHeight;
                var s = m_Grid.GridSize;
                m_Target = c;
                m_HasTarget = c.x >= 0 && c.x < s.x && c.z >= 0 && c.z < s.z
                           && m_BuildHeight >= 0 && m_BuildHeight < s.y;
            }
        }

        // 손 비었을 때 '마우스가 가리킨' 바닥 픽업 또는 도구함을 집는다(테두리=집기 동일 대상).
        private void TryGrab()
        {
            if (HasMaterial) return;
            if (m_GrabBody != null)    { GrabFromFloor(m_GrabBody); return; }
            if (m_GrabStation != null) { HoldTool(m_GrabStation.Tool); return; }
        }

        // 마우스 레이캐스트로 '가리킨' 집기 대상을 산출 — 바닥 픽업(트리거) 또는 도구함(콜라이더).
        // 손 닿는 거리(reach) 안에서 레이 최단(커서에 제일 가까운) 1개. 그 오브젝트에 테두리(집기·발광 공용).
        private void UpdateGrabTarget()
        {
            m_GrabBody = null;
            m_GrabStation = null;
            GameObject hitGo = null;

            if (!HasMaterial && !HasTool && m_Cam != null && Mouse.current != null)
            {
                var ray = m_Cam.ScreenPointToRay(Mouse.current.position.ReadValue());
                float reach2 = m_GrabRange * m_GrabRange;
                float best = float.MaxValue;
                foreach (var h in Physics.RaycastAll(ray, 100f, ~0, QueryTriggerInteraction.Collide))
                {
                    var pb = h.collider.GetComponentInParent<PickupBody>();   // 바닥 픽업 우선
                    if (pb != null && pb.Owner != null)
                    {
                        if ((pb.transform.position - transform.position).sqrMagnitude > reach2) continue;   // 손 닿는 거리
                        if (h.distance < best) { best = h.distance; m_GrabBody = pb; m_GrabStation = null; hitGo = pb.gameObject; }
                        continue;
                    }
                    var ws = h.collider.GetComponentInParent<Workstation>();  // 도구함(도구 집기)
                    if (ws != null)
                    {
                        if ((ws.transform.position - transform.position).sqrMagnitude > reach2) continue;
                        if (h.distance < best) { best = h.distance; m_GrabStation = ws; m_GrabBody = null; hitGo = ws.gameObject; }
                    }
                }
            }
            m_GrabValid = m_GrabBody != null || m_GrabStation != null;

            SetGrabHighlight(hitGo);   // 가리킨 대상에 테두리(대상 바뀌면 이전 건 끔)
        }

        // 집기 대상 오브젝트에 인버티드 헐 테두리를 켜고, 직전 대상은 끈다.
        private void SetGrabHighlight(GameObject go)
        {
            if (go == m_HlGo) return;
            if (m_HlGo != null)
            {
                var prev = m_HlGo.GetComponent<OutlineHighlight>();
                if (prev != null) prev.SetOutline(false);
            }
            if (go != null)
            {
                var oh = go.GetComponent<OutlineHighlight>();
                if (oh == null) oh = go.AddComponent<OutlineHighlight>();
                oh.SetOutline(true);
            }
            m_HlGo = go;
        }

        // 마우스가 가리키는 바닥 지점(픽업 높이 평면). 못 구하면 플레이어 위치.
        private Vector3 AimWorldPoint()
        {
            if (m_Cam == null || Mouse.current == null) return transform.position;
            var plane = new Plane(Vector3.up, new Vector3(0f, 0.5f, 0f));
            var ray = m_Cam.ScreenPointToRay(Mouse.current.position.ReadValue());
            return plane.Raycast(ray, out float d) ? ray.GetPoint(d) : transform.position;
        }

        private void GrabFromFloor(PickupBody pb)
        {
            if (pb.ToolBit != 0)                       // 던져진 도구 줍기
            {
                pb.Owner.RequestGrab(pb.PickupId);     // 그 픽업의 '소속' 인스턴스에 요청(드롭필드 2개 문제 회피)
                HoldTool((ProcessType)pb.ToolBit);
                return;
            }
            var def = m_Grid != null && m_Grid.Catalog != null ? m_Grid.Catalog.GetById(pb.MaterialId) : null;
            if (def == null) return;
            pb.Owner.RequestGrab(pb.PickupId);
            m_HeldMaterial = def;
            m_HeldTool = ProcessType.None;
            m_NetMaterialId.Value = def.Id;
            m_NetTool.Value = 0;
            PlaySFX(SFXType.PickUpObject);
        }

        private void HoldTool(ProcessType tool)
        {
            DropHeldToFloor();                // 도구 들기 전, 들고 있던 것은 바닥에
            m_HeldMaterial = null;
            m_HeldTool = tool;
            m_NetMaterialId.Value = -1;
            m_NetTool.Value = (int)tool;
            PlaySFX(SFXType.PickUpObject);
        }

        private void Drop()
        {
            DropHeldToFloor();   // 버리기 = 든 재료/도구를 발밑 바닥에(픽업으로)
            ClearHeld();
        }

        // 든 재료 또는 도구를 마우스 조준 지점으로 던진다(협동 전달). 최대 m_ThrowRange까지.
        private void Throw()
        {
            if (m_Drop == null || (!HasMaterial && !HasTool)) return;
            Vector3 aim = AimWorldPoint();                 // 커서 아래 바닥 지점(y=0.5)
            Vector3 flat = aim - transform.position; flat.y = 0f;
            float dist = flat.magnitude;
            Vector3 to = dist > m_ThrowRange
                ? transform.position + flat / Mathf.Max(dist, 1e-4f) * m_ThrowRange   // 너무 멀면 사거리까지만
                : aim;
            to.y = 0.5f;
            Vector3 from = transform.position + Vector3.up * 1.2f;
            if (HasMaterial) m_Drop.RequestThrow(m_HeldMaterial.Id, from, to);
            else             m_Drop.RequestThrowTool((int)m_HeldTool, from, to);
            PlaySFX(SFXType.ThrowObject);
            ClearHeld();
        }

        // 든 재료가 있으면 발밑 바닥에 떨군다(놓기 외에 손을 떠나는 모든 경로 공통). 다시 주워 재배치 가능.
        // 든 재료/도구를 발밑 바닥에 떨군다(픽업으로 — 주워서 재배치/재사용). 놓기 외 손을 떠나는 공통 경로.
        private void DropHeldToFloor()
        {
            if (m_Drop == null) return;
            if (HasMaterial)
            {
                m_Drop.RequestDrop(m_HeldMaterial.Id, transform.position + Vector3.up * 0.6f);
                PlaySFX(SFXType.LandObject);
            }
            else if (HasTool)
            {
                m_Drop.RequestThrowTool((int)m_HeldTool, transform.position + Vector3.up * 0.6f, transform.position);
                PlaySFX(SFXType.LandObject);
            }
        }

        private void ClearHeld()
        {
            m_HeldMaterial = null;
            m_HeldTool = ProcessType.None;
            m_NetMaterialId.Value = -1;
            m_NetTool.Value = 0;
        }

        private void TryPlace()
        {
            if (m_Loop != null && !m_Loop.IsBuilding) return;
            if (!m_HasTarget || m_Net == null || m_Grid == null) return;
            var s = m_Grid.GridSize;
            foreach (var cell in GridFootprint.EnumerateFootprintCells(m_Target, m_HeldMaterial.Footprint, m_Rotation))
            {
                if (cell.x < 0 || cell.x >= s.x || cell.y < 0 || cell.y >= s.y || cell.z < 0 || cell.z >= s.z) return;
                if (!m_Net.IsCellFree(cell)) return;
            }
            // 서버와 동일한 지지검사 — 거부될 자리면 손에 든 채 유지(재료 손실 방지)
            if (!GridSupport.WouldBeSupported(
                    GridFootprint.EnumerateFootprintCells(m_Target, m_HeldMaterial.Footprint, m_Rotation),
                    cell => !m_Net.IsCellFree(cell)))
                return;

            m_Net.RequestPlace(m_Target, m_HeldMaterial.Id, (byte)m_Rotation);
            PlaySFX(SFXType.LandObject);
            ClearHeld();   // 놓으면 손이 빔 → 재고서 다시 집어야(리썰컴퍼니식)
        }

        private static void PlaySFX(SFXType type)
        {
            if (SoundManager.Instance != null)
                SoundManager.Instance.PlaySFX(type);
        }

        private void TryRemove()
        {
            if (m_Loop != null && !m_Loop.IsBuilding) return;
            if (!m_HasTarget || m_Net == null) return;
            m_Net.RequestRemove(m_Target);   // 서버가 점유 검증 + 재료를 바닥에 떨굼
        }

        // ── 비주얼(상태 구동, 모든 클라) ───────────────────────────────────
        private void RebuildHeldVisual()
        {
            if (m_HeldVisual != null) { Destroy(m_HeldVisual); m_HeldVisual = null; }

            int matId = m_NetMaterialId.Value;
            int tool = m_NetTool.Value;

            if (matId >= 0)
            {
                var def = FindMaterial(matId);
                if (def == null) return;
                var fp = def.Footprint;
                if (def.Prefab != null)   // 진짜 블록 외형(물 재질 등) — 중심 맞춰 작게 들기
                {
                    m_HeldVisual = new GameObject("~Held");
                    var vis = Instantiate(def.Prefab, m_HeldVisual.transform);
                    vis.transform.localPosition = new Vector3(-fp.x * 0.5f, -fp.y * 0.5f, -fp.z * 0.5f);   // 피벗(min-corner) 보정
                    m_HeldVisual.transform.localScale = Vector3.one * 0.35f;
                    foreach (var c in m_HeldVisual.GetComponentsInChildren<Collider>()) Destroy(c);
                }
                else                      // 프리팹 없음 → 공정색 큐브(폴백)
                {
                    m_HeldVisual = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    m_HeldVisual.transform.localScale =
                        new Vector3(Mathf.Max(1, fp.x), Mathf.Max(1, fp.y), Mathf.Max(1, fp.z)) * 0.35f;
                    Paint(m_HeldVisual, ColorForMask(def.RequiredMask));
                    StripCollider(m_HeldVisual);
                }
            }
            else if (tool != 0)   // 든 도구(구) — 망치=파랑(고정) / 페인트통=초록(페인트)
            {
                m_HeldVisual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                m_HeldVisual.transform.localScale = Vector3.one * 0.4f;
                Paint(m_HeldVisual, ColorForMask(tool));
                StripCollider(m_HeldVisual);
            }

            if (m_HeldVisual != null)
                m_HeldVisual.transform.position = transform.position + m_HoldOffset;
        }

        // 카탈로그(드는 재료 목록)를 lazy-find — 모든 클라에서 동일 에셋.
        private MaterialCatalog Catalog()
        {
            if (m_Catalog == null)
            {
                var g = m_Grid != null ? m_Grid : FindFirstObjectByType<GridManager>();
                if (g != null) m_Catalog = g.Catalog;
            }
            return m_Catalog;
        }

        private MaterialDef FindMaterial(int id)
            => Catalog() != null ? Catalog().GetById(id) : null;

        private static Color ColorForMask(int mask)
        {
            if ((mask & (int)ProcessType.Painted) != 0) return new Color(0.30f, 0.85f, 0.40f);
            if ((mask & (int)ProcessType.Fixed) != 0)   return new Color(0.35f, 0.60f, 1.00f);
            return new Color(0.72f, 0.72f, 0.72f);
        }

        private static void Paint(GameObject go, Color c)
        {
            var r = go.GetComponent<Renderer>();
            if (r == null) return;
            var mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            mpb.SetColor(Shader.PropertyToID("_BaseColor"), c);
            mpb.SetColor(Shader.PropertyToID("_Color"), c);
            r.SetPropertyBlock(mpb);
        }

        private static void StripCollider(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
        }

        private void OnDrawGizmos()
        {
            if (!IsOwner || !Application.isPlaying) return;
            if (HasMaterial && m_HasTarget)
            {
                Gizmos.color = Color.cyan;
                foreach (var c in GridFootprint.EnumerateFootprintCells(m_Target, m_HeldMaterial.Footprint, m_Rotation))
                    Gizmos.DrawWireCube(GridCoordinates.CellToWorld(c) + Vector3.one * 0.5f, Vector3.one * 1.02f);
            }
            else if (HasTool && m_HasTarget)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireCube(GridCoordinates.CellToWorld(m_Target) + Vector3.one * 0.5f, Vector3.one * 1.02f);
            }
            if (m_ProcessHold > 0f && m_ProcessCell != s_NoCell)   // 공정 진행 중인 셀 강조
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireCube(GridCoordinates.CellToWorld(m_ProcessCell) + Vector3.one * 0.5f, Vector3.one * 1.05f);
            }
        }

        // ── 인게임 배치 외곽선 (Gizmos 토글과 무관하게 게임 화면에 보임) ──────
        private Material m_LineMat;

        private Material LineMat()
        {
            if (m_LineMat == null)
            {
                var sh = Shader.Find("Hidden/Internal-Colored");
                if (sh == null) sh = Shader.Find("Sprites/Default");
                m_LineMat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
                m_LineMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m_LineMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m_LineMat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
                m_LineMat.SetInt("_ZWrite", 0);
                m_LineMat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);   // 블록에 가려도 보이게
            }
            return m_LineMat;
        }

        // URP 카메라 렌더 콜백에 GL 라인을 그린다(게임뷰에 확실히 보임). 메인 카메라에만.
        private void OnEnable()  => RenderPipelineManager.endCameraRendering += DrawOutline;
        private void OnDisable() => RenderPipelineManager.endCameraRendering -= DrawOutline;

        private void DrawOutline(ScriptableRenderContext ctx, Camera cam)
        {
            if (!IsOwner || !Application.isPlaying || cam != m_Cam) return;

            LineMat().SetPass(0);
            GL.PushMatrix();
            GL.LoadProjectionMatrix(cam.projectionMatrix);
            GL.modelview = cam.worldToCameraMatrix;
            GL.Begin(GL.LINES);

            if (m_HasTarget && HasMaterial)                    // 든 재료의 배치 자리(시안)
            {
                GL.Color(new Color(0.25f, 0.9f, 1f, 0.95f));
                foreach (var c in GridFootprint.EnumerateFootprintCells(m_Target, m_HeldMaterial.Footprint, m_Rotation))
                    GLWireCube(GridCoordinates.CellToWorld(c) + Vector3.one * 0.5f, Vector3.one);
            }
            else if (m_HasTarget && HasTool)                   // 공정 대상(노랑)
            {
                GL.Color(new Color(1f, 0.95f, 0.25f, 0.95f));
                GLWireCube(GridCoordinates.CellToWorld(m_Target) + Vector3.one * 0.5f, Vector3.one);
            }

            // 집기 대상 하이라이트는 GL 박스가 아니라 OutlineHighlight(인버티드 헐 테두리)로 처리(UpdateGrabTarget).

            GL.End();
            GL.PopMatrix();
        }

        private static void GLWireCube(Vector3 center, Vector3 size)
        {
            var h = size * 0.5f;
            for (int a = 0; a < 8; a++)
            for (int b = a + 1; b < 8; b++)
            {
                int d = a ^ b;
                if (d != 1 && d != 2 && d != 4) continue;   // 한 축만 다른 코너 쌍 = 모서리(총 12개)
                GL.Vertex(GLCorner(center, h, a));
                GL.Vertex(GLCorner(center, h, b));
            }
        }

        private static Vector3 GLCorner(Vector3 c, Vector3 h, int k)
            => c + new Vector3((k & 1) != 0 ? h.x : -h.x, (k & 2) != 0 ? h.y : -h.y, (k & 4) != 0 ? h.z : -h.z);

        private void OnGUI()
        {
            if (!IsOwner || !Application.isPlaying) return;
            if (m_HudStyle == null)
                m_HudStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, normal = { textColor = Color.white } };

            string held = HasMaterial ? $"재료 id{m_HeldMaterial.Id} (R회전 {m_Rotation})"
                        : HasTool     ? (m_HeldTool == ProcessType.Fixed ? "망치(고정) — 블록 가리키고 E 꾹" : "페인트통(페인트) — 블록 가리키고 E 꾹")
                        :               "빈손 — 우상단서 주문 → 배송 구역에서 좌클릭으로 줍기 (작업장서 좌클릭=도구)";
            string tgt = m_HasTarget ? $"대상 {m_Target}" : "대상 -";
            string score = m_Net != null ? $"점수 {m_Net.ScorePercent:F0}%" : "";
            string grab = !m_GrabValid ? "없음"
                        : m_GrabStation != null ? "도구함"
                        : m_GrabBody.ToolBit != 0 ? "도구" : "재료" + m_GrabBody.MaterialId;
            string text =
                $"[Carry] 들기: {held}\n" +
                $"좌클릭 집기/배치 · 우클릭 철거 · Space 점프 · E꾹 공정 · G 던지기\n" +
                $"C 공정취소 · R 회전 · 층 {m_BuildHeight}(Q내림·E톡 올림) · TAB 정답/필요공정    {tgt}  {score}\n" +
                $"진단: cam={m_Cam != null} grid={m_Grid != null} net={m_Net != null} 대상유효={m_HasTarget} · 집기대상={grab}";

            var box = new Rect(10, 174, 700, 100);
            var prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = prev;
            GUI.Label(new Rect(box.x + 8, box.y + 6, box.width - 16, box.height - 12), text, m_HudStyle);

            DrawProcessBar();
            DrawProcessHint();
        }

        // 도구 들고 조준 중일 때(바가 안 차는 동안) 대상 블록 위에 공정 안내를 띄운다.
        private void DrawProcessHint()
        {
            if (m_ProcessHold > 0f || string.IsNullOrEmpty(m_ProcessHint) || m_Cam == null || !m_HasTarget) return;
            Vector3 world = GridCoordinates.CellToWorld(m_Target) + new Vector3(0.5f, 1.3f, 0.5f);
            Vector3 sp = m_Cam.WorldToScreenPoint(world);
            if (sp.z <= 0f) return;

            if (m_BarLabel == null)
                m_BarLabel = new GUIStyle(GUI.skin.label)
                    { fontSize = 13, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };

            const float w = 280f, h = 20f;
            var r = new Rect(sp.x - w / 2f, Screen.height - sp.y - h, w, h);
            var prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.72f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = prev;
            GUI.Label(r, m_ProcessHint, m_BarLabel);
        }

        // E 공정 진행 중이면 대상 블록 위에 로딩바 + 라벨(고정/페인트).
        private void DrawProcessBar()
        {
            if (m_ProcessHold <= 0f || m_Cam == null || m_ProcessCell == s_NoCell) return;
            Vector3 world = GridCoordinates.CellToWorld(m_ProcessCell) + new Vector3(0.5f, 1.1f, 0.5f);
            Vector3 sp = m_Cam.WorldToScreenPoint(world);
            if (sp.z <= 0f) return;

            const float bw = 96f, bh = 12f;
            float x = sp.x - bw / 2f;
            float y = Screen.height - sp.y - bh;   // GUI y는 위에서부터
            var prev = GUI.color;

            GUI.color = new Color(0f, 0f, 0f, 0.7f);
            GUI.DrawTexture(new Rect(x - 2f, y - 2f, bw + 4f, bh + 4f), Texture2D.whiteTexture);

            float t = Mathf.Clamp01(m_ProcessHold / m_ProcessSeconds);
            GUI.color = m_ProcessKind == ProcessType.Painted ? new Color(0.30f, 0.85f, 0.40f)
                                                             : new Color(0.35f, 0.60f, 1.00f);
            GUI.DrawTexture(new Rect(x, y, bw * t, bh), Texture2D.whiteTexture);
            GUI.color = prev;

            if (m_BarLabel == null)
                m_BarLabel = new GUIStyle(GUI.skin.label)
                    { fontSize = 13, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            GUI.Label(new Rect(x - 30f, y - 20f, bw + 60f, 18f),
                m_ProcessKind == ProcessType.Painted ? "페인트 중…" : "고정 중…", m_BarLabel);
        }
    }
}
