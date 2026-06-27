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
    /// Space 점프/벽점프 · 좌클릭 집기/배치(토글) · C 철거 · Q 버리기 · E(꾹) 공정 · Z(꾹) 공정 되돌리기
    /// · R 회전 · 벽 보고 W/S 기어오르기 · 배치 높이=플레이어가 선 높이 · G 던지기. (우클릭=카메라 회전. TAB 정답 안내)
    /// 든 상태는 NetworkVariable로 복제 → 모든 클라가 머리 위 비주얼 재구성(원격도 보임).
    /// </summary>
    public class PlayerCarry : NetworkBehaviour
    {
        [SerializeField] private Vector3 m_HoldOffset = new Vector3(0f, 1.2f, 0f);
        [SerializeField] private float m_BlockHoldRaise = 0.6f;   // 재료(블록)는 머리 안 가리게 더 위로
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
        [Tooltip("든 '망치'(고정 도구) 외형 모델(Hammer.glb). 비우면 파란 구로 폴백.")]
        [SerializeField] private GameObject m_HammerModel;
        [Tooltip("든 도구 모델 스케일.")]
        [SerializeField] private float m_ToolModelScale = 0.4f;

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
        private PlayerMovement m_Movement;
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

        // C 꾹 되돌리기: 조준 블록에 완료된 공정이 있으면 누적시간으로 마지막 공정을 되돌림.
        private float m_RevertHold;
        private Vector3Int m_RevertCell = s_NoCell;
        private bool m_RevertDone;            // 이번 C 누름에 1회 되돌림(떼야 다음)

        // 킥(노답중력): 몸에 닿은(근접) 바닥 재료를 찬다. 줍기 범위(grab)보다 좁아 살짝 떨어져선 좌클릭으로 줍기 가능.
        private const float kKickRadius = 0.8f;
        private readonly HashSet<ulong> m_Touching = new();
        private readonly List<ulong> m_KickIds = new();
        private readonly List<Vector3> m_KickPos = new();

        private bool HasMaterial => m_HeldMaterial != null;
        private bool HasTool => m_HeldTool != ProcessType.None;

        // 애니메이터/외부용 상태 노출
        public bool IsHolding     => HasMaterial || HasTool;
        public bool IsHoldingTool => HasTool;
        public bool IsProcessing  => m_ProcessHold > 0f;   // E 꾹 도구 작업 중
        public event System.Action OnPlace;   // 배치/버리기(내려놓기 모션)
        public event System.Action OnThrow;   // 던지기

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
            if (m_Preview != null) Destroy(m_Preview);
            if (m_PreviewMat != null) Destroy(m_PreviewMat);
        }

        private void OnHeldChanged(int _, int __) => RebuildHeldVisual();

        // 든 게 블록(재료)이면 머리 안 가리게 더 위로, 도구는 그대로. (복제값 기준 — 원격도 동일)
        private Vector3 HeldOffset() => m_HoldOffset + (m_NetMaterialId.Value >= 0 ? Vector3.up * m_BlockHoldRaise : Vector3.zero);

        private void Update()
        {
            // 모든 클라: 든 비주얼이 플레이어를 따라감
            if (m_HeldVisual != null)
                m_HeldVisual.transform.position = transform.position + HeldOffset();

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
            if (m_Movement == null) m_Movement = GetComponent<PlayerMovement>();

            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null || mouse == null) return;

            if (kb.rKey.wasPressedThisFrame) m_Rotation = (m_Rotation + 1) & 3;
            if (kb.qKey.wasPressedThisFrame) Drop();      // Q = 들고 있는 것 버리기(발밑에)
            if (kb.gKey.wasPressedThisFrame) Throw();     // 든 재료를 조준 방향으로 던지기(협동 전달)
            // Space는 점프(PlayerInputHandler). 집기·배치는 좌클릭. 우클릭은 카메라 회전 전용.

            UpdateTarget();
            UpdateGrabTarget();   // 빈손이면 near+aim 집기 대상 산출(하이라이트·집기 공용)

            if (kb.cKey.wasPressedThisFrame) TryRemove();   // C = 철거(현재 조준 칸)

            // 좌클릭만 게임 조작(빈손→집기 / 재료→배치). 정답 패널 위에선 카메라 조작이라 무시.
            if (!AnswerPanelFocus.Active && mouse.leftButton.wasPressedThisFrame)
            {
                if (HasMaterial)
                {
                    if (m_HasTarget) TryPlace();    // 그리드 위 → 그리드 배치
                    else             TryFreeDrop(); // 그리드 밖 → 바닥 자유 배치
                }
                else if (!HasTool)
                {
                    if (m_GrabValid) TryGrab();          // 바닥 픽업/도구함 우선
                    else             TryPickupPlaced();  // 그리드 위 미고정 블록 집기
                }
            }

            UpdateEKey(kb);          // E 꾹=공정(로딩바)
            UpdateZKey(kb);          // Z 꾹=마지막 공정 되돌리기(로딩바)
            UpdateProcessHint();     // 도구 들었을 때 "지금 무슨 공정 차례인지" 안내 갱신

            TryBumpCollapse();   // C3: 미고정 기둥/벽에 몸으로 부딪히면 무너뜨림
            TryKickPickups();    // 노답중력: 몸에 닿은 바닥 재료를 찬다

            UpdatePreview();     // 배치 미리보기(반투명 박스 GameObject — GL 폐지)
        }
        
        private void TryFreeDrop()
        {
            if (!HasMaterial || m_Drop == null) return;
            // 마우스 레이 → Y=0 평면(바닥)과 교점 구하기
            var ray = m_Cam.ScreenPointToRay(Mouse.current.position.ReadValue());
            var plane = new Plane(Vector3.up, Vector3.zero);  // Y=0 바닥
            if (!plane.Raycast(ray, out float dist)) return;
            Vector3 dropPos = ray.GetPoint(dist);
            dropPos.y = 0.5f;  // 바닥 위 약간 뜨게
            // MaterialDropField의 RequestDrop을 사용해 그 위치에 픽업으로 떨굼
            m_Drop.RequestDrop(m_HeldMaterial.Id, dropPos);
            PlaySFX(SFXType.LandObject);
            ClearHeld();
            OnPlace?.Invoke();
        }

        // 그리드 위 '미고정' 블록을 좌클릭으로 손에 회수. 서버 검증 후 owner 확정(2-hop RPC).
        private void TryPickupPlaced()
        {
            if (m_Loop != null && !m_Loop.IsBuilding) return;
            if (!m_HasTarget || m_Net == null) return;
            if (!m_Net.IsPickupable(m_Target)) return;
            PickupPlacedServerRpc(m_Target);
        }

        [Rpc(SendTo.Server)]
        private void PickupPlacedServerRpc(Vector3Int cell)
        {
            if (m_NetMaterialId.Value >= 0 || m_NetTool.Value != 0) return;   // 복제 상태 기준 이미 손에 뭔가
            var net = m_Net != null ? m_Net : FindFirstObjectByType<GridNetwork>();
            if (net == null) return;
            if (!net.ServerPickupBlock(cell, out int matId)) return;
            m_Net = net;   // 서버 인스턴스 캐시(다음 호출 FindFirstObjectByType 회피)
            PickupPlacedConfirmRpc(matId);
        }

        [Rpc(SendTo.Owner)]
        private void PickupPlacedConfirmRpc(int materialId)
        {
            var def = Catalog() != null ? Catalog().GetById(materialId) : null;
            if (def == null) return;
            if (HasMaterial || HasTool)   // 인플라이트 중 다른 걸 집었음 → 분실 방지로 바닥 재드롭
            {
                if (m_Drop != null) m_Drop.RequestDrop(materialId, transform.position + Vector3.up * 0.6f);
                return;
            }
            m_HeldMaterial = def;
            m_HeldTool = ProcessType.None;
            m_NetMaterialId.Value = def.Id;   // owner write
            m_NetTool.Value = 0;
            PlaySFX(SFXType.PickUpObject);
        }

        // E: 짧게 '톡' 누르면 층 올림, 길게 '꾹' 누르면 공정(로딩바). 한 키에 톡/꾹을 누른 시간으로 구분한다.
        // 꾹: '든 도구'가 조준 블록에 필요할 때만 바가 차고, 다 차면 그 공정을 적용(누른 채로 다음 단계 이어짐).
        private void UpdateEKey(Keyboard kb)
        {
            if (kb.eKey.wasReleasedThisFrame || !kb.eKey.isPressed)
            {
                m_ProcessHold = 0f; m_ProcessCell = s_NoCell;
                return;
            }

            if (!ToolReadyOnTarget())   // 공정 불가(도구 없음/안 맞음/빈 칸) → 바 안 참
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
                PlayProcessSfx(m_HeldTool == ProcessType.Painted);             // 로컬 + 원격 복제(옆 플레이어도 들림)
                m_PendingCell = m_ProcessCell;   // 복제 반영 전까지 같은 공정 재적용 방지
                m_PendingKind = m_HeldTool;
                m_ProcessHold = 0f;
            }
        }

        // Z 꾹: 완료된 공정이 있으면 바가 차고, 다 차면 마지막 공정 되돌림(서버 검증). 한 번 누름에 1회.
        private void UpdateZKey(Keyboard kb)
        {
            if (!kb.zKey.isPressed)
            {
                m_RevertHold = 0f; m_RevertCell = s_NoCell; m_RevertDone = false;
                return;
            }
            if (m_RevertDone) return;   // 이번 누름에 이미 되돌림 → 떼야 다음

            if (!RevertReadyOnTarget())
            {
                m_RevertHold = 0f; m_RevertCell = s_NoCell;
                return;
            }
            if (m_Target != m_RevertCell) { m_RevertCell = m_Target; m_RevertHold = 0f; }
            m_RevertHold += Time.deltaTime;
            if (m_RevertHold >= m_ProcessSeconds)
            {
                m_Net.RequestCancelLast(m_RevertCell);
                m_RevertHold = 0f;
                m_RevertDone = true;
            }
        }

        // 되돌릴 게 있나: 건축 중 + 유효 셀 + 완료된 공정 비트가 하나라도 있으면.
        private bool RevertReadyOnTarget()
        {
            if (m_Loop != null && !m_Loop.IsBuilding) return false;
            if (!m_HasTarget || m_Net == null) return false;
            if (!m_Net.TryGetCell(m_Target, out _, out int completed)) return false;
            return completed != 0;
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
            if (m_Cam == null || m_Grid == null) return;

            // 배치 높이 = 플레이어가 '딛고 선' 높이. 단, 벽타기/점프/낙하 중엔 갱신하지 않는다
            // (그 동안 transform.y가 올라가면 프리뷰가 같이 떠버림 → 접지한 순간에만 층 확정).
            if (m_Movement == null || (!m_Movement.IsClimbing && m_Movement.IsGrounded()))
                m_BuildHeight = Mathf.Clamp(
                    Mathf.RoundToInt((transform.position.y - GridContract.Origin.y) / GridContract.Unit),
                    0, m_Grid.GridSize.y - 1);

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
            if (!HasMaterial && !HasTool) return;   // 빈손 무동작
            DropHeldToFloor();   // 버리기 = 든 재료/도구를 발밑 바닥에(픽업으로)
            ClearHeld();
            OnPlace?.Invoke();
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
            OnThrow?.Invoke();
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
            OnPlace?.Invoke();
        }

        private static void PlaySFX(SFXType type)
        {
            if (SoundManager.Instance != null)
                SoundManager.Instance.PlaySFX(type);
        }

        // 공정 소리: owner 로컬 즉시 + 서버 경유로 다른 클라에도(옆 플레이어 망치질이 들리게).
        private void PlayProcessSfx(bool painted)
        {
            PlaySFX(painted ? SFXType.Painting : SFXType.Hammering);
            if (IsSpawned) RequestProcessSfxRpc(painted);
        }

        [Rpc(SendTo.Server)]
        private void RequestProcessSfxRpc(bool painted) => ProcessSfxRpc(painted);

        [Rpc(SendTo.NotOwner)]
        private void ProcessSfxRpc(bool painted)
            => PlaySFX(painted ? SFXType.Painting : SFXType.Hammering);

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
                    vis.transform.localPosition = new Vector3(-fp.x * 0.5f, -fp.y * 0.5f, -fp.z * 0.5f);   // 피벗(min-corner) → 머리 위 중앙 정렬
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
            else if (tool != 0)   // 든 도구 — 망치(고정)는 모델, 그 외/폴백은 공정색 구
            {
                var model = (tool & (int)ProcessType.Fixed) != 0 ? m_HammerModel : null;
                if (model != null)
                {
                    m_HeldVisual = new GameObject("~Held");
                    var vis = Instantiate(model, m_HeldVisual.transform);
                    vis.transform.localPosition = Vector3.zero;
                    m_HeldVisual.transform.localScale = Vector3.one * m_ToolModelScale;
                    foreach (var c in m_HeldVisual.GetComponentsInChildren<Collider>()) Destroy(c);
                }
                else
                {
                    m_HeldVisual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    m_HeldVisual.transform.localScale = Vector3.one * 0.4f;
                    Paint(m_HeldVisual, ColorForMask(tool));
                    StripCollider(m_HeldVisual);
                }
            }

            if (m_HeldVisual != null)
                m_HeldVisual.transform.position = transform.position + HeldOffset();
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
                HeldPlacementBox(out var center, out var size);
                Gizmos.DrawWireCube(center, size);
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

        // ── 인게임 배치 미리보기: 반투명 박스 GameObject (URP 정상 렌더 — GL 즉시모드 폐지) ──────
        private GameObject m_Preview;
        private Material m_PreviewMat;
        private static readonly int s_PvBase = Shader.PropertyToID("_BaseColor");
        private static readonly int s_PvCol  = Shader.PropertyToID("_Color");

        // 든 재료를 놓을 자리의 월드 박스 — GridNetwork.SpawnPrefabVisual과 동일 산출(프리뷰=실제 배치 정합).
        private void HeldPlacementBox(out Vector3 center, out Vector3 size)
        {
            float u = GridContract.Unit;
            var fp = m_HeldMaterial.Footprint;
            var cells = GridFootprint.EnumerateFootprintCells(m_Target, fp, m_Rotation);
            Vector3Int minCell = cells[0];
            for (int i = 1; i < cells.Count; i++) minCell = Vector3Int.Min(minCell, cells[i]);
            bool swap = ((((m_Rotation % 4) + 4) % 4) % 2) == 1;
            size = new Vector3(swap ? fp.z : fp.x, fp.y, swap ? fp.x : fp.z) * u;
            center = GridCoordinates.CellToWorld(minCell) + size * 0.5f;
        }

        // 매 프레임 배치 미리보기 박스를 대상 칸에 맞춰 갱신. 고스트/놓은블록과 '같은 좌표·같은 렌더 경로'
        // (일반 GameObject) → 정확히 정합. (이전 GL 즉시모드는 URP 클립공간 불일치로 화면에서 떠 보였음.)
        private void UpdatePreview()
        {
            bool show = m_HasTarget && (HasMaterial || HasTool);
            if (!show)
            {
                if (m_Preview != null && m_Preview.activeSelf) m_Preview.SetActive(false);
                return;
            }
            if (m_Preview == null) m_Preview = CreatePreview();

            Vector3 center, size; Color col;
            if (HasMaterial)
            {
                HeldPlacementBox(out center, out size);
                col = new Color(0.25f, 0.9f, 1f, 0.32f);    // 시안: 배치 자리
            }
            else
            {
                float u = GridContract.Unit;
                center = GridCoordinates.CellToWorld(m_Target) + Vector3.one * (0.5f * u);
                size   = Vector3.one * u;
                col = new Color(1f, 0.95f, 0.25f, 0.32f);   // 노랑: 공정 대상
            }
            m_Preview.transform.SetPositionAndRotation(center, Quaternion.identity);
            m_Preview.transform.localScale = size;
            m_PreviewMat.SetColor(s_PvBase, col);
            m_PreviewMat.SetColor(s_PvCol, col);
            if (!m_Preview.activeSelf) m_Preview.SetActive(true);
        }

        private GameObject CreatePreview()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "~PlacePreview";
            var c = go.GetComponent<Collider>(); if (c != null) Destroy(c);
            go.GetComponent<Renderer>().sharedMaterial = PreviewMat();
            return go;
        }

        private Material PreviewMat()
        {
            if (m_PreviewMat == null)
            {
                var sh = Shader.Find("Universal Render Pipeline/Lit");
                m_PreviewMat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
                m_PreviewMat.SetOverrideTag("RenderType", "Transparent");
                m_PreviewMat.SetFloat("_Surface", 1f);   // URP: 0=Opaque 1=Transparent
                m_PreviewMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m_PreviewMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m_PreviewMat.SetInt("_ZWrite", 0);
                m_PreviewMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m_PreviewMat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }
            return m_PreviewMat;
        }

        private void OnGUI()
        {
            if (!IsOwner || !Application.isPlaying) return;
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != SceneNames.GameScene) return;   // 조작법 HUD는 GameScene만
            if (m_HudStyle == null)
                m_HudStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, normal = { textColor = Color.white } };

            string held = HasMaterial ? $"재료 id{m_HeldMaterial.Id} (R회전 {m_Rotation})"
                        : HasTool     ? (m_HeldTool == ProcessType.Fixed ? "망치(고정) — 블록 가리키고 E 꾹" : "페인트통(페인트) — 블록 가리키고 E 꾹")
                        :               "빈손 — 우상단서 주문 → 배송 구역에서 좌클릭으로 줍기 (작업장서 좌클릭=도구)";
            if (!HasMaterial && !HasTool && m_HasTarget && m_Net != null && m_Net.IsPickupable(m_Target))
                held = "빈손 — 좌클릭 = 미고정 블록 집기 (고정 전)";
            string tgt = m_HasTarget ? $"대상 {m_Target}" : "대상 -";
            string score = m_Net != null ? $"점수 {m_Net.ScorePercent:F0}%" : "";
            string grab = !m_GrabValid ? "없음"
                        : m_GrabStation != null ? "도구함"
                        : m_GrabBody.ToolBit != 0 ? "도구" : "재료" + m_GrabBody.MaterialId;
            string text =
                $"[Carry] 들기: {held}\n" +
                $"좌클릭 집기/배치 · C 철거 · Q 버리기 · Space 점프/벽점프 · G 던지기\n" +
                $"E꾹 공정 · Z꾹 되돌리기 · R 회전 · 벽 보고 W/S 기어오르기 · 층 {m_BuildHeight}(자동) · TAB 정답    {tgt}  {score}\n" +
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

        // E 공정 / C 되돌리기 로딩바(대상 블록 위).
        private void DrawProcessBar()
        {
            if (m_ProcessHold > 0f && m_ProcessCell != s_NoCell)
                DrawHoldBar(m_ProcessCell, m_ProcessHold,
                    m_ProcessKind == ProcessType.Painted ? new Color(0.30f, 0.85f, 0.40f) : new Color(0.35f, 0.60f, 1.00f),
                    m_ProcessKind == ProcessType.Painted ? "페인트 중…" : "고정 중…");
            if (m_RevertHold > 0f && m_RevertCell != s_NoCell)
                DrawHoldBar(m_RevertCell, m_RevertHold, new Color(0.90f, 0.45f, 0.30f), "되돌리는 중…");
        }

        private void DrawHoldBar(Vector3Int cell, float hold, Color fill, string label)
        {
            if (m_Cam == null) return;
            Vector3 world = GridCoordinates.CellToWorld(cell) + new Vector3(0.5f, 1.1f, 0.5f);
            Vector3 sp = m_Cam.WorldToScreenPoint(world);
            if (sp.z <= 0f) return;

            const float bw = 96f, bh = 12f;
            float x = sp.x - bw / 2f;
            float y = Screen.height - sp.y - bh;   // GUI y는 위에서부터
            var prev = GUI.color;

            GUI.color = new Color(0f, 0f, 0f, 0.7f);
            GUI.DrawTexture(new Rect(x - 2f, y - 2f, bw + 4f, bh + 4f), Texture2D.whiteTexture);

            float t = Mathf.Clamp01(hold / m_ProcessSeconds);
            GUI.color = fill;
            GUI.DrawTexture(new Rect(x, y, bw * t, bh), Texture2D.whiteTexture);
            GUI.color = prev;

            if (m_BarLabel == null)
                m_BarLabel = new GUIStyle(GUI.skin.label)
                    { fontSize = 13, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            GUI.Label(new Rect(x - 30f, y - 20f, bw + 60f, 18f), label, m_BarLabel);
        }
    }
}
