using System.Collections.Generic;
using GridSystem;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Player
{
    /// <summary>
    /// 들기 + 클릭 배치/공정. 한 번에 '재료' 또는 '도구' 하나만 든다(협동 제약).
    /// 1/2/3 재료 · 4 망치(고정) · 5 페인트통 · 좌클릭(재료=배치 / 도구=공정) · C 취소 · R 회전 · Q/E 층 · Space 버리기.
    /// 든 상태는 NetworkVariable로 복제 → 모든 클라가 머리 위 비주얼 재구성(원격도 보임).
    /// </summary>
    public class PlayerCarry : NetworkBehaviour
    {
        [SerializeField] private Vector3 m_HoldOffset = new Vector3(0f, 2.2f, 0f);
        [SerializeField] private float m_WorkstationRange = 2.5f;

        // 복제 상태(owner write): 든 재료 id(-1=없음) / 든 도구 비트(0=없음)
        private readonly NetworkVariable<int> m_NetMaterialId =
            new(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        private readonly NetworkVariable<int> m_NetTool =
            new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        private int m_Rotation;
        private int m_BuildHeight;
        private MaterialDef m_HeldMaterial;   // owner 로직용
        private ProcessType m_HeldTool;       // owner 로직용
        private GameObject m_HeldVisual;      // 모든 클라 비주얼

        private Camera m_Cam;
        private GridManager m_Grid;
        private MaterialCatalog m_Catalog;
        private GridNetwork m_Net;
        private GameLoopManager m_Loop;
        private MaterialDropField m_Drop;
        private Vector3Int m_Target;
        private bool m_HasTarget;
        private GUIStyle m_HudStyle;
        private static readonly Vector3Int s_NoCell = new Vector3Int(int.MinValue, int.MinValue, int.MinValue);
        private Vector3Int m_LastShockCell = s_NoCell;   // 같은 셀 안에 있는 동안 충격 중복 전송 방지

        // 킥(노답중력): 몸에 닿은(근접) 바닥 재료를 찬다. 줍기 범위(작업장)보다 좁아 살짝 떨어져선 F로 줍기 가능.
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

            if (kb.fKey.wasPressedThisFrame) TryGrab();   // 배송된 재료 / 작업장 도구 줍기

            if (kb.rKey.wasPressedThisFrame) m_Rotation = (m_Rotation + 1) & 3;
            if (kb.qKey.wasPressedThisFrame) m_BuildHeight = Mathf.Max(0, m_BuildHeight - 1);
            if (kb.eKey.wasPressedThisFrame && m_Grid != null)
                m_BuildHeight = Mathf.Min(m_Grid.GridSize.y - 1, m_BuildHeight + 1);
            if (kb.spaceKey.wasPressedThisFrame) Drop();

            UpdateTarget();

            if (kb.cKey.wasPressedThisFrame && m_HasTarget && m_Net != null)
                m_Net.RequestCancelLast(m_Target);

            if (mouse.leftButton.wasPressedThisFrame)
            {
                if (HasMaterial) TryPlace();
                else if (HasTool) TryApplyTool();
            }
            if (mouse.rightButton.wasPressedThisFrame) TryRemove();   // 철거(되돌리기)

            TryBumpCollapse();   // C3: 미고정 기둥/벽에 몸으로 부딪히면 무너뜨림
            TryKickPickups();    // 노답중력: 몸에 닿은 바닥 재료를 찬다
        }

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

        private void HoldTool(ProcessType tool)
        {
            DropHeldMaterialToFloor();        // 도구 들기 전, 들고 있던 재료는 바닥에
            m_HeldMaterial = null;
            m_HeldTool = tool;
            m_NetMaterialId.Value = -1;
            m_NetTool.Value = (int)tool;
        }

        // F: 손 비었으면 '마우스가 가리킨(+손 닿는) 바닥 재료'를 줍고, 없으면 작업장 도구를 든다.
        private void TryGrab()
        {
            if (!HasMaterial && !HasTool && m_Drop != null &&
                m_Drop.TryFindForGrab(transform.position, AimWorldPoint(), m_WorkstationRange, out var pid, out var mid))
            {
                GrabMaterialFromFloor(pid, mid);
                return;
            }
            TryGrabFromWorkstation();
        }

        // 마우스가 가리키는 바닥 지점(픽업 높이 평면). 못 구하면 플레이어 위치.
        private Vector3 AimWorldPoint()
        {
            if (m_Cam == null || Mouse.current == null) return transform.position;
            var plane = new Plane(Vector3.up, new Vector3(0f, 0.5f, 0f));
            var ray = m_Cam.ScreenPointToRay(Mouse.current.position.ReadValue());
            return plane.Raycast(ray, out float d) ? ray.GetPoint(d) : transform.position;
        }

        private void GrabMaterialFromFloor(ulong pickupId, int materialId)
        {
            var def = m_Grid != null && m_Grid.Catalog != null ? m_Grid.Catalog.GetById(materialId) : null;
            if (def == null) return;
            m_Drop.RequestGrab(pickupId);          // 서버가 픽업 제거(낙관적)
            m_HeldMaterial = def;
            m_HeldTool = ProcessType.None;
            m_NetMaterialId.Value = def.Id;
            m_NetTool.Value = 0;
        }

        private void TryGrabFromWorkstation()
        {
            Workstation best = null;
            float bestSqr = m_WorkstationRange * m_WorkstationRange;
            foreach (var w in FindObjectsByType<Workstation>(FindObjectsSortMode.None))
            {
                float d = (w.transform.position - transform.position).sqrMagnitude;
                if (d <= bestSqr) { bestSqr = d; best = w; }
            }
            if (best != null) HoldTool(best.Tool);
        }

        private void Drop()
        {
            DropHeldMaterialToFloor();   // 버리기 = 든 재료를 발밑 바닥에(도구는 그냥 비움)
            ClearHeld();
        }

        // 든 재료가 있으면 발밑 바닥에 떨군다(놓기 외에 손을 떠나는 모든 경로 공통). 다시 주워 재배치 가능.
        private void DropHeldMaterialToFloor()
        {
            if (HasMaterial && m_Drop != null)
                m_Drop.RequestDrop(m_HeldMaterial.Id, transform.position + Vector3.up * 0.6f);
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
            ClearHeld();   // 놓으면 손이 빔 → 재고서 다시 집어야(리썰컴퍼니식)
        }

        private void TryApplyTool()
        {
            if (m_Loop != null && !m_Loop.IsBuilding) return;
            if (!m_HasTarget || m_Net == null) return;
            m_Net.RequestProcess(m_Target, (int)m_HeldTool, true);   // 서버가 점유/순서 검증
        }

        private void TryRemove()
        {
            if (m_Loop != null && !m_Loop.IsBuilding) return;
            if (!m_HasTarget || m_Net == null) return;
            m_Net.RequestRemove(m_Target);   // 서버가 점유 검증 + 재고 환원
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
                m_HeldVisual = GameObject.CreatePrimitive(PrimitiveType.Cube);
                var fp = def.Footprint;
                m_HeldVisual.transform.localScale =
                    new Vector3(Mathf.Max(1, fp.x), Mathf.Max(1, fp.y), Mathf.Max(1, fp.z)) * 0.35f;
                Paint(m_HeldVisual, ColorForMask(def.RequiredMask));
                StripCollider(m_HeldVisual);
            }
            else if (tool != 0)
            {
                m_HeldVisual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                m_HeldVisual.transform.localScale = Vector3.one * 0.4f;
                Paint(m_HeldVisual, ColorForMask(tool));
                StripCollider(m_HeldVisual);
            }

            if (m_HeldVisual != null)
                m_HeldVisual.transform.position = transform.position + m_HoldOffset;
        }

        // 카탈로그(드는 재료 목록)를 lazy-find — 모든 클라에서 동일 에셋. 홀딩/든 비주얼 공용.
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
            if (!IsOwner || !Application.isPlaying || !m_HasTarget) return;
            Gizmos.color = Color.cyan;
            if (HasMaterial)
                foreach (var c in GridFootprint.EnumerateFootprintCells(m_Target, m_HeldMaterial.Footprint, m_Rotation))
                    Gizmos.DrawWireCube(GridCoordinates.CellToWorld(c) + Vector3.one * 0.5f, Vector3.one * 1.02f);
            else if (HasTool)
                Gizmos.DrawWireCube(GridCoordinates.CellToWorld(m_Target) + Vector3.one * 0.5f, Vector3.one * 1.02f);
        }

        private void OnGUI()
        {
            if (!IsOwner || !Application.isPlaying) return;
            if (m_HudStyle == null)
                m_HudStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, normal = { textColor = Color.white } };

            string held = HasMaterial ? $"재료 id{m_HeldMaterial.Id} (R회전 {m_Rotation})"
                        : HasTool     ? (m_HeldTool == ProcessType.Fixed ? "망치(고정)" : "페인트통(페인트)")
                        :               "빈손 — 우상단서 주문 → 배송 구역에서 F로 줍기 (F=도구도)";
            string tgt = m_HasTarget ? $"대상 {m_Target}" : "대상 -";
            string score = m_Net != null ? $"점수 {m_Net.ScorePercent:F0}%" : "";
            string text =
                $"[Carry] 들기: {held}\n" +
                $"좌클릭=배치/공정 · 우클릭=철거 · C 공정취소 · R 회전 · Q/E 층 {m_BuildHeight} · Space 바닥에 버리기\n" +
                $"{tgt}    {score}\n" +
                $"진단: cam={m_Cam != null} grid={m_Grid != null} net={m_Net != null} 대상유효={m_HasTarget}";

            var box = new Rect(10, 174, 680, 100);
            var prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = prev;
            GUI.Label(new Rect(box.x + 8, box.y + 6, box.width - 16, box.height - 12), text, m_HudStyle);
        }
    }
}
