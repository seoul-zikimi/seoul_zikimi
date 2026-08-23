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
        [SerializeField] private Vector3 m_HoldOffset = new Vector3(0f, 1.2f, 0f);   // 도구 들 때(머리 위)
        [Tooltip("재료는 원본 크기 그대로 몸 앞에 안고 간다(무빙아웃식). 앞으로 띄우는 거리(블록 반폭은 자동 가산)·높이.")]
        [SerializeField] private float m_FrontHoldDist = 0.35f;
        [SerializeField] private float m_FrontHoldHeight = 0.55f;
        [Tooltip("무거운 재료를 혼자 들 때 이동속도 배율.")]
        [SerializeField] private float m_HeavySoloSpeed = 0.7f;
        [Tooltip("바닥 재료 줍기 / 작업장 도구 집기 거리.")]
        [FormerlySerializedAs("m_WorkstationRange")]
        [SerializeField] private float m_GrabRange = 2.5f;
        private const float kBuildReachCells = 2f;   // [07/26 기획] 배치/회수/공정 사거리(칸) — 완화/폐기 시 여기만
        private bool        m_GrabValid;
        private PickupBody  m_GrabBody;     // 레이캐스트로 가리킨 바닥 픽업(소속·정체 보유)
        private Workstation m_GrabStation;  // 레이캐스트로 가리킨 도구함(있으면 그 도구를 집음)
        private GameObject  m_HlGo;         // 현재 테두리 중인 오브젝트(대상 바뀌면 끔)
        [Tooltip("공정 한 단계를 끝내려고 E를 눌러야 하는 시간(초). 로딩바가 차는 속도.")]
        [SerializeField] private float m_ProcessSeconds = 1.2f;
        [Tooltip("재료를 던질 수 있는 최대 거리(칸). 조준점이 더 멀면 이 거리까지만 날아간다.")]
        [SerializeField] private float m_ThrowRange = 12f;   // 풀차지 최대 사거리(칸)
        [Tooltip("든 '망치'(고정 도구) 외형 모델(Hammer.glb). 비우면 파란 구로 폴백.")]
        [SerializeField] private GameObject m_HammerModel;
        [Tooltip("든 '페인트통'(페인트 도구) 외형 모델(PaintCan.glb). 비우면 초록 구로 폴백.")]
        [SerializeField] private GameObject m_PaintCanModel;
        [Tooltip("든 도구 모델 스케일.")]
        [SerializeField] private float m_ToolModelScale = 0.4f;
        [SerializeField] private GameObject m_HammerFx;   // 망치질 타격 이펙트 프리팹(CFXR3 Hit Fire B (Air))
        [SerializeField] private GameObject m_FixDoneFx;  // 고정 완료 이펙트 프리팹(CFXR Hit D 3D (Yellow))
        [SerializeField] private GameObject m_PaintFx;    // 페인트 튀김 프리팹(CFXR2 Blood Shape Splash → 주황 틴트)

        // 복제 상태(owner write): 든 재료 id(-1=없음) / 든 도구 비트(0=없음)
        private readonly NetworkVariable<int> m_NetMaterialId =
            new(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        private readonly NetworkVariable<int> m_NetTool =
            new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        // 무거운 재료를 혼자 끙끙 드는 중(땀 이펙트 · 전 클라 표시)
        private readonly NetworkVariable<bool> m_NetStraining =
            new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        private MaterialDef m_HeldDef;        // 모든 클라: 든 재료 정의(화물 크기·무게)
        private PlayerFacing m_Facing;        // 모델이 보는 방향(물리 루트는 회전 안 함)
        // 같이 들기: 내가 돕고 있는 운반자의 NetworkObjectId(없음 = MaxValue). owner write → 운반자가 '도움 받는 중' 판정.
        private const ulong kNoHelp = ulong.MaxValue;
        private readonly NetworkVariable<ulong> m_NetHelping =
            new(ulong.MaxValue, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        private PlayerCarry m_HelpTarget;     // owner: 돕는 중인 운반자(캐시)
        // 같이 들기 슬롯: 화물 4면(0:-Z 1:+Z 2:-X 3:+X · 월드 고정). 운반자 면(-1 = 혼자 들기), 도우미 면, 도우미 입력(월드 방향)
        private readonly NetworkVariable<int> m_NetSide =
            new(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        private readonly NetworkVariable<int> m_NetHelpSide =
            new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        private readonly NetworkVariable<Vector3> m_NetMoveInput =   // 같이 들 때 내 이동 입력(월드) — 전원이 평균내서 같은 그룹 속도로 움직임
            new(Vector3.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        private readonly NetworkVariable<int> m_NetRotation =   // 든 재료 yaw(R 회전) — 화물은 월드 방향 고정
            new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        [Tooltip("같이 들 때 각자를 자기 면 슬롯으로 당기는 스프링 세기(클수록 단단히 붙음).")]
        [SerializeField] private float m_TetherStiffness = 6f;
        [Tooltip("슬롯에서 이만큼 벗어날 때까진 안 당김(네트워크 지연 흡수).")]
        [SerializeField] private float m_TetherSlack = 0.5f;
        [Tooltip("혼자 들 때 화물이 몸 회전을 따라오는 속도(작을수록 묵직하게 늦게 따라옴).")]
        [SerializeField] private float m_CargoTurnFollow = 14f;
        private Vector3 m_CargoDir;           // 댐핑된 화물 방향(혼자 들기)
        private bool m_CargoDirInit;
        private PlayerCarry m_GrabCargoOf;    // 조준 중인 '남이 든 화물'의 운반자(클릭 = 같이 들기)
        [Tooltip("같이 들기 가능한 거리(화물 중심까지, 수평).")]
        [SerializeField] private float m_JoinRange = 3f;
        private ParticleSystem m_SweatFx;     // 땀 이펙트(필요 시 생성)
        private static PlayerCarry[] s_AllCarries;
        private static float s_AllCarriesTime = -1f;

        /// <summary>이동속도 배율(owner) — 무거운 재료 혼자 들면 0.7, 동료가 붙으면 1.</summary>
        public float MoveMultiplier { get; private set; } = 1f;
        public bool IsStraining => m_NetStraining.Value;
        public bool HasMaterialHeld => m_NetMaterialId.Value >= 0;   // 모든 클라
        public bool IsHelping => m_NetHelping.Value != kNoHelp;
        public bool IsSharedCarry => HasMaterialHeld && m_NetSide.Value >= 0;   // 도우미가 붙어 4면 슬롯 모드

        private static readonly Vector3[] kSideDir = { Vector3.back, Vector3.forward, Vector3.left, Vector3.right };
        private static int NearestSide(Vector3 dir)
        {
            int best = 0; float bd = float.NegativeInfinity;
            for (int i = 0; i < 4; i++) { float d = Vector3.Dot(dir, kSideDir[i]); if (d > bd) { bd = d; best = i; } }
            return best;
        }

        // 든 재료의 월드 반폭(x/z · R 회전 반영). 모든 클라.
        private Vector2 HalfExtentXZ()
        {
            if (m_HeldDef == null) return Vector2.one * (0.5f * GridContract.Unit);
            var fp = m_HeldDef.Footprint;
            bool swap = (m_NetRotation.Value & 1) == 1;
            float x = Mathf.Max(1, swap ? fp.z : fp.x), z = Mathf.Max(1, swap ? fp.x : fp.z);
            return new Vector2(x, z) * (0.5f * GridContract.Unit);
        }
        private float HalfExtentAlong(int side) => side < 2 ? HalfExtentXZ().y : HalfExtentXZ().x;

        /// <summary>화물 중심(모든 클라). 혼자면 모델이 보는 방향 앞, 같이 들면 운반자 면의 반대편(월드 고정).</summary>
        private float HalfExtentY()
            => (m_HeldDef != null ? Mathf.Max(1, m_HeldDef.Footprint.y) : 1) * (0.5f * GridContract.Unit);

        /// <summary>화물 yaw(월드): 혼자 들면 몸 방향 + R 회전, 같이 들면 월드 고정(R 회전만).</summary>
        private Quaternion CargoRot()
        {
            Quaternion r = Quaternion.Euler(0f, 90f * m_NetRotation.Value, 0f);
            if (m_NetSide.Value >= 0) return r;
            return Quaternion.LookRotation(CargoDir(), Vector3.up) * r;
        }

        // 혼자 들 때 화물이 향하는 방향 — 몸 방향을 댐핑해서 따라감(묵직하게 같이 돎, 위치·회전 동일 소스라 따로 안 미끄러짐)
        private Vector3 CargoDir()
        {
            Vector3 f = FacingDir();
            if (!m_CargoDirInit) { m_CargoDir = f; m_CargoDirInit = true; }
            else m_CargoDir = Vector3.Slerp(m_CargoDir, f, 1f - Mathf.Exp(-m_CargoTurnFollow * Time.deltaTime));
            return m_CargoDir;
        }

        private Vector3 SharedCargoCenter(int mySide)
        {
            Vector3 up = Vector3.up * (m_FrontHoldHeight + HalfExtentY());
            return transform.position - kSideDir[mySide] * (m_FrontHoldDist + HalfExtentAlong(mySide)) + up;
        }

        private Vector3 CargoCenter()
        {
            Vector3 up = Vector3.up * (m_FrontHoldHeight + HalfExtentY());   // 밑면이 m_FrontHoldHeight 만큼 떠 있게
            int side = m_NetSide.Value;
            if (side >= 0) return SharedCargoCenter(side);
            Vector3 f = CargoDir();
            return transform.position + f * (m_FrontHoldDist + HalfExtentXZ().y) + up;   // 블록이 몸과 같이 도니 앞쪽 반폭 = 로컬 z
        }

        /// <summary>side 면에 서는 사람의 발 위치(y = 운반자 y).</summary>
        private Vector3 SlotPos(int side)
        {
            Vector3 c = CargoCenter();
            Vector3 p = c + kSideDir[side] * (HalfExtentAlong(side) + m_FrontHoldDist);
            p.y = transform.position.y;
            return p;
        }

        /// <summary>같이 드는 중이면 화물을 바라보게(운반자·도우미 공통).</summary>
        public bool TryGetHelperFacing(out Vector3 dir)
        {
            dir = default;
            if (IsHelping)
            {
                var c = ResolveHelpTarget();
                if (c == null || !c.HasMaterialHeld) return false;
                dir = -kSideDir[m_NetHelpSide.Value];
                return true;
            }
            if (IsSharedCarry) { dir = -kSideDir[m_NetSide.Value]; return true; }
            return false;
        }

        /// <summary>같이 들기 그룹 이동(운반자·도우미 공통, owner): 내 입력을 복제하고, 참가자 전원의 입력 평균을 이동 방향으로 돌려준다.
        /// 같은 방향 = 풀속도, 반대 = 상쇄(정지), 직각 = 대각선. 각 클라가 로컬로 계산하므로 내 조작은 즉답.</summary>
        public bool TryGetGroupMove(Vector3 myDir, out Vector3 group)
        {
            group = myDir;
            PlayerCarry carrier;
            if (IsHelping) { carrier = ResolveHelpTarget(); if (carrier == null || !carrier.HasMaterialHeld) { ReleaseHelp(); return false; } }
            else if (IsSharedCarry) carrier = this;
            else return false;

            if ((m_NetMoveInput.Value - myDir).sqrMagnitude > 1e-4f) m_NetMoveInput.Value = myDir;

            Vector3 sum = Vector3.zero; int n = 0;
            void Add(PlayerCarry pc) { sum += pc == this ? myDir : pc.m_NetMoveInput.Value; n++; }
            Add(carrier);
            foreach (var o in AllCarries())
                if (o != null && o != carrier && o.m_NetHelping.Value == carrier.NetworkObjectId) Add(o);
            group = n > 0 ? Vector3.ClampMagnitude(sum / n, 1f) : myDir;
            return true;
        }

        /// <summary>대형 유지용 약한 보정(owner): 각자 로컬 적분이라 조금씩 어긋나는 걸 자기 면 슬롯으로 살살 되돌린다(데드존 있음).</summary>
        public void ApplyTether(Rigidbody rb, float inputMag = 0f)
        {
            Vector3 target;
            if (IsHelping)
            {
                var c = ResolveHelpTarget();
                if (c == null || !c.HasMaterialHeld || c.m_NetSide.Value < 0) return;
                target = c.SlotPos(m_NetHelpSide.Value);
            }
            else return;   // 운반자가 기준점(화물은 운반자 위치에서 나옴) — 도우미만 맞춘다

            Vector3 cur = rb.position;
            Vector3 err = new Vector3(target.x - cur.x, 0f, target.z - cur.z);
            float len = err.magnitude;
            if (len <= m_TetherSlack) return;
            err *= (len - m_TetherSlack) / len;
            Vector3 corr = Vector3.ClampMagnitude(err * m_TetherStiffness, 4f);
            var v = rb.linearVelocity;
            rb.linearVelocity = new Vector3(v.x + corr.x, v.y, v.z + corr.z);
        }

        private static readonly Collider[] s_OverlapBuf = new Collider[16];

        /// <summary>화물 충돌 해소(owner 이동): 벽/배치 블록과 겹치면 그쪽으로 파고드는 속도만 깎고 살짝 밀어낸다.
        /// 빠져나가는 방향은 항상 허용 → 끼어서 못 움직이는 일 없음. 다음 틱 위치도 미리 검사해 벽에 박기 전에 멈춘다.</summary>
        private BoxCollider m_CargoBox;   // 화물 콜라이더(AttachCargoPusher)
        private Vector3 m_BumpVel;        // 벽에 박았을 때 튕김 속도(감쇠)
        private float m_BumpCooldown;

        private static bool IsObstacle(Collider c, Transform self)
        {
            if (c == null || c.transform == self || c.transform.IsChildOf(self)) return false;
            if (c.CompareTag("Player")) return false;                           // 사람은 밀리는 쪽(키네마틱 화물이 밈)
            if (c.GetComponentInParent<PickupBody>() != null) return false;     // 바닥 재료도 차이는 쪽
            if (c.GetComponentInParent<PlayerCarry>() != null) return false;    // 다른 플레이어(자식 콜라이더)
            return true;
        }

        public void ResolveCargoCollision(ref Vector3 v, float dt)
        {
            if (!HasMaterialHeld || m_HeldDef == null || m_HeldVisual == null) return;
            if (m_CargoBox == null) m_CargoBox = m_HeldVisual.GetComponent<BoxCollider>();
            if (m_CargoBox == null) return;

            // 튕김 잔상(감쇠) — 박은 뒤 몇 틱 동안 뒤로 밀려나는 느낌
            if (m_BumpVel.sqrMagnitude > 1e-4f) { v += m_BumpVel; m_BumpVel = Vector3.Lerp(m_BumpVel, Vector3.zero, 1f - Mathf.Exp(-9f * dt)); }

            Vector3 half = new Vector3(HalfExtentXZ().x, HalfExtentY(), HalfExtentXZ().y) * 0.92f;
            Quaternion rot = CargoRot();
            for (int pass = 0; pass < 2; pass++)   // 0 = 지금 위치(겹침 해소) · 1 = 다음 틱 위치(예측 차단)
            {
                Vector3 center = CargoCenter() + (pass == 1 ? v * dt * 1.5f : Vector3.zero);
                int n = Physics.OverlapBoxNonAlloc(center, half, s_OverlapBuf, rot, ~(1 << 2), QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++)
                {
                    var c = s_OverlapBuf[i];
                    if (!IsObstacle(c, transform)) continue;
                    if (!Physics.ComputePenetration(m_CargoBox, center, rot, c, c.transform.position, c.transform.rotation, out var dir, out float dist))
                        continue;
                    dir.y = 0f;
                    if (dir.sqrMagnitude < 1e-6f) continue;   // 위아래로만 겹침(바닥 등) — 수평 이동과 무관
                    dir.Normalize();
                    float into = Vector3.Dot(v, dir);          // dir = 화물을 밖으로 밀어내는 방향
                    if (into < 0f)
                    {
                        v -= dir * into;                        // 파고드는 성분 제거(미끄러짐은 유지)
                        if (pass == 1 && into < -1.2f && Time.time > m_BumpCooldown)   // 제법 빠르게 박았을 때: 통! 튕겨나옴 + 화물 찌그러짐
                        {
                            m_BumpCooldown = Time.time + 0.3f;
                            m_BumpVel = dir * Mathf.Min(-into * 0.9f, 4f);
                            GridJuice.Squish(m_HeldVisual, 0.18f);
                            if (SoundManager.Instance != null) SoundManager.Instance.PlaySFX(SFXType.PlayerBounce);
                        }
                    }
                    if (pass == 0) v += dir * Mathf.Min(dist * 12f, 2.5f);   // 이미 겹쳐 있으면 살짝 밀어냄
                }
            }
        }

        private PlayerCarry ResolveHelpTarget()
        {
            if (!IsHelping) { m_HelpTarget = null; return null; }
            if (m_HelpTarget != null && m_HelpTarget.NetworkObjectId == m_NetHelping.Value) return m_HelpTarget;
            m_HelpTarget = null;
            if (NetworkManager.Singleton != null &&
                NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(m_NetHelping.Value, out var no) && no != null)
                m_HelpTarget = no.GetComponent<PlayerCarry>();
            return m_HelpTarget;
        }

        private void JoinCarry(PlayerCarry carrier)
        {
            if (carrier == null || carrier == this || !carrier.HasMaterialHeld) return;
            // 빈 면 중 내게 가장 가까운 면. 운반자 면은 제외(아직 혼자면 운반자는 '보는 방향의 반대' 면에 선다고 가정).
            int carrierSide = carrier.m_NetSide.Value >= 0 ? carrier.m_NetSide.Value : NearestSide(-carrier.FacingDir());
            var taken = new HashSet<int> { carrierSide };
            foreach (var o in AllCarries())
                if (o != null && o != this && o.m_NetHelping.Value == carrier.NetworkObjectId) taken.Add(o.m_NetHelpSide.Value);
            Vector3 center = carrier.CargoCenter();
            int best = -1; float bd = float.MaxValue;
            for (int i = 0; i < 4; i++)
            {
                if (taken.Contains(i)) continue;
                float d = (center + kSideDir[i] - transform.position).sqrMagnitude;
                if (d < bd) { bd = d; best = i; }
            }
            if (best < 0) return;   // 4면 다 참
            m_NetHelpSide.Value = best;
            m_NetHelping.Value = carrier.NetworkObjectId;
            m_HelpTarget = carrier;
            if (SoundManager.Instance != null) SoundManager.Instance.PlaySFX(SFXType.UIClick);
        }

        private void ReleaseHelp()
        {
            if (!IsHelping) return;
            m_NetHelping.Value = kNoHelp;
            m_NetMoveInput.Value = Vector3.zero;
            m_HelpTarget = null;
        }

        private static PlayerCarry[] AllCarries()
        {
            if (s_AllCarries == null || Time.time - s_AllCarriesTime > 1f)
            {
                s_AllCarries = FindObjectsByType<PlayerCarry>(FindObjectsSortMode.None);
                s_AllCarriesTime = Time.time;
            }
            return s_AllCarries;
        }

        /// <summary>이 운반자를 돕고 있는 도우미 수(모든 클라 계산 가능).</summary>
        private int HelperCount()
        {
            int n = 0;
            foreach (var o in AllCarries())
                if (o != null && o != this && o.m_NetHelping.Value == NetworkObjectId) n++;
            return n;
        }

        // 운반자(owner): 도우미가 붙으면 내 면을 고정(보는 방향의 반대), 다 떠나면 혼자 들기로 복귀
        private void UpdateSharedSide()
        {
            if (!HasMaterial) { if (m_NetSide.Value != -1) m_NetSide.Value = -1; return; }
            bool any = HelperCount() > 0;
            if (any && m_NetSide.Value < 0) m_NetSide.Value = NearestSide(-FacingDir());
            else if (!any && m_NetSide.Value >= 0) m_NetSide.Value = -1;
        }

        // 모델이 보는 수평 방향(PlayerFacing) — 없으면 루트 forward
        private Vector3 FacingDir()
        {
            if (m_Facing == null) m_Facing = GetComponent<PlayerFacing>();
            return m_Facing != null ? m_Facing.Forward : transform.forward;
        }

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
        private CarryHudUI m_Hud;   // 프리팹 HUD(UIManager 관리) — 구 OnGUI 대체
        private bool m_HudMissing;  // 프리팹 미생성 경고 1회용
        private float m_HitFxTimer; // 망치질 이펙트 간격 타이머
        private PickupBody m_PrevGrabBody;   // 조준 펄스 on/off 추적
        private int m_PrevScorePct = -1;     // 완성도 상승 감지(HUD 디용)
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
            if (m_SweatFx != null) Destroy(m_SweatFx.gameObject);
            m_HelpTarget = null;
            if (m_ThrowAim != null) Destroy(m_ThrowAim);
            DestroyPreview();
            if (m_PreviewMat != null) Destroy(m_PreviewMat);
            if (m_Hud != null) m_Hud.gameObject.SetActive(false);   // HUD는 UIManager 캐시 → 숨기기만
        }

        private void OnHeldChanged(int _, int __) => RebuildHeldVisual();

        // 든 게 블록(재료)이면 머리 안 가리게 더 위로, 도구는 그대로. (복제값 기준 — 원격도 동일)
        // 재료 = 몸 앞(바라보는 방향)에 원본 크기로 안고 감 · 도구 = 머리 위
        private Vector3 HeldOffset()
        {
            if (m_NetMaterialId.Value < 0) return m_HoldOffset;
            return CargoCenter() - transform.position;
        }

        private Vector3 m_HeldPrevPos;      // 든 비주얼 바운스/스웨이용 위치 추적
        private Vector3 m_HeldSwayVel;      // 부드럽게 감쇠한 이동속도(관성 스웨이)
        private float   m_HeldBobPhase;     // 통통 밥 위상

        private void Update()
        {
            // 모든 클라: 든 비주얼이 플레이어를 따라감(+ 걸을 때 통통 밥 + 관성 스웨이)
            if (m_HeldVisual != null)
                UpdateHeldVisual();
            UpdateSweatFx();

            if (!IsOwner) return;
            UpdateHeavyState();
            OwnerUpdate();
        }

        // 든 블록/도구 쫀득 연출: 위치델타 기반이라 owner·원격 동일. 망치질 스윙 중엔 회전 양보.
        private void UpdateHeldVisual()
        {
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            Vector3 vel = (transform.position - m_HeldPrevPos) / dt;
            m_HeldPrevPos = transform.position;
            Vector3 horiz = vel; horiz.y = 0f;
            float speed = horiz.magnitude;

            m_HeldBobPhase += dt * (7f + speed * 2f);                                  // 걸을수록 빠르게 통통
            float bob = Mathf.Sin(m_HeldBobPhase) * 0.06f * Mathf.Clamp01(speed / 2f); // 멈추면 밥 사라짐
            m_HeldSwayVel = Vector3.Lerp(m_HeldSwayVel, horiz, 8f * dt);               // 가감속 관성
            Vector3 sway = -m_HeldSwayVel * 0.045f;                                    // 가속 방향 반대로 살짝 처짐

            // 강체 부착: 위치·회전 모두 같은 facing에서 나오므로 몸이 도는 만큼만 같이 돈다(따로 미끄러짐 없음)
            m_HeldVisual.transform.position = transform.position + HeldOffset() + Vector3.up * bob + sway;

            if (m_SwingCo == null)   // 망치질 스윙 코루틴이 회전을 쓰는 동안은 건드리지 않음
            {
                // 재료는 월드 방향 고정(R 회전만 반영) — 몸을 돌려도 제자리 회전하지 않는다. 도구는 몸 회전 따라감.
                Quaternion face = HasMaterialHeld ? CargoRot() : transform.rotation;
                var local = Quaternion.Inverse(face) * m_HeldSwayVel;                     // 몸 기준 기울임
                Quaternion tilt = Quaternion.Euler(local.z * 4f, 0f, -local.x * 4f);
                m_HeldVisual.transform.rotation = face * tilt;
            }
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

            if (IsHelping)   // 같이 드는 중: 클릭 = 손 떼기. 그 외 조작은 전부 잠금(운반자가 놓으면 자동 해제)
            {
                if (ResolveHelpTarget() == null || !m_HelpTarget.HasMaterialHeld) ReleaseHelp();
                else if (!AnswerPanelFocus.Active && mouse.leftButton.wasPressedThisFrame) ReleaseHelp();
                SetGrabHighlight(null);
                DestroyPreview();
                return;
            }

            if (kb.rKey.wasPressedThisFrame) m_Rotation = (m_Rotation + 1) & 3;
            if (m_NetRotation.Value != m_Rotation) m_NetRotation.Value = m_Rotation;
            UpdateSharedSide();
            UpdateThrowCharge(kb);   // G 탭=짧게 던지기 / 꾹=차징(화살표 미리보기) 후 떼면 멀리
            // Q(버리기)·C(철거)는 좌클릭에 통합(07/26 기획): 그리드 밖 배치=발밑 버리기, 미고정 블록 좌클릭=회수.
            // Space는 점프(PlayerInputHandler). 집기·배치는 좌클릭. 우클릭은 카메라 회전 전용.

            UpdateTarget();
            UpdateGrabTarget();   // 빈손이면 near+aim 집기 대상 산출(하이라이트·집기 공용)

            if (m_PrevGrabBody != m_GrabBody)   // 조준 대상 두근두근 on/off
            {
                if (m_PrevGrabBody != null) m_PrevGrabBody.SetTargeted(false);
                if (m_GrabBody != null) m_GrabBody.SetTargeted(true);
                m_PrevGrabBody = m_GrabBody;
            }

            // 좌클릭만 게임 조작(빈손→집기 / 재료→배치). 정답 패널 위에선 카메라 조작이라 무시.
            if (!AnswerPanelFocus.Active && mouse.leftButton.wasPressedThisFrame)
            {
                if (HasMaterial)
                {
                    if (m_HasTarget) TryPlace();   // 그리드 위 → 그리드 배치
                    else             Drop();       // 그리드 밖 '배치' = 발밑에 버리기(기존 Q 통합)
                }
                else if (!HasTool)
                {
                    if (m_GrabCargoOf != null) JoinCarry(m_GrabCargoOf);   // 남이 든 화물 클릭 = 같이 들기
                    else if (m_GrabValid) TryGrab();          // 바닥 픽업/도구함 우선
                    else             TryPickupPlaced();  // 그리드 위 미고정 블록 집기
                }
            }

            UpdateEKey(kb);          // E 꾹=공정(로딩바)
            UpdateZKey(kb);          // Z 꾹=마지막 공정 되돌리기(로딩바)
            UpdateProcessHint();     // 도구 들었을 때 "지금 무슨 공정 차례인지" 안내 갱신

            TryBumpCollapse();   // C3: 미고정 기둥/벽에 몸으로 부딪히면 무너뜨림
            TryKickPickups();    // 노답중력: 몸에 닿은 바닥 재료를 찬다

            UpdatePreview();     // 배치 미리보기(반투명 박스 GameObject — GL 폐지)
            UpdateHud();         // 프리팹 HUD 갱신(조작법·공정바·공정힌트)
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
        // 페인트 공정 시간 = SFX_Painting.wav 길이(1.31s) → 붓질 사운드·로딩바가 함께 시작해서 함께 끝남.
        private const float kPaintSeconds = 1.31f;
        private float ProcessDurationFor(ProcessType kind) => kind == ProcessType.Painted ? kPaintSeconds : m_ProcessSeconds;

        // 대포: E를 꾹 눌러 충전(공정 바를 그대로 재활용해 게이지 표시), 떼면 발사.
        // 조준은 상대 진영을 바라보는 연출이고, 실제 파괴 대상은 서버가 완성 파츠 중 무작위로 고른다(기획서).
        private const float kCannonChargeSeconds = 0.8f;
        private float m_CannonCharge;

        private void UpdateCannonCharge(Keyboard kb, GridSystem.ItemNetwork items)
        {
            if (kb.eKey.isPressed)
            {
                m_CannonCharge += Time.deltaTime;
                return;
            }
            if (kb.eKey.wasReleasedThisFrame && m_CannonCharge > 0f)
            {
                bool charged = m_CannonCharge >= kCannonChargeSeconds;
                m_CannonCharge = 0f;
                if (charged) items.RequestUseHeld();   // 덜 눌렀으면 불발(다시 조준)
            }
        }

        private void UpdateEKey(Keyboard kb)
        {
            // [기획] 2vs2 아이템은 '든 채로 E'. 공정도 E라서, 도구를 안 든 상태에서만 아이템이 발동한다
            // (도구를 들었다 = 공정할 의도). 대포만 예외로 '꾹 눌렀다 떼면 발사'.
            var items = GridSystem.ItemNetwork.Instance;
            if (!HasTool && items != null && items.LocalHasItem)
            {
                if (items.LocalHoldsCannon) { UpdateCannonCharge(kb, items); return; }
                if (kb.eKey.wasPressedThisFrame) { items.RequestUseHeld(); return; }
            }
            m_CannonCharge = 0f;

            if (kb.eKey.wasReleasedThisFrame || !kb.eKey.isPressed)
            {
                CancelPaintStroke();
                m_ProcessHold = 0f; m_ProcessCell = s_NoCell;
                return;
            }

            if (!ToolReadyOnTarget())   // 공정 불가(도구 없음/안 맞음/빈 칸) → 바 안 참
            {
                CancelPaintStroke();
                m_ProcessHold = 0f; m_ProcessCell = s_NoCell;
                return;
            }

            if (m_AimedProcessCell != m_ProcessCell) { CancelPaintStroke(); m_ProcessCell = m_AimedProcessCell; m_ProcessHold = 0f; }   // 셀 바뀌면 처음부터
            m_ProcessKind = m_HeldTool;
            bool strokeStart = m_ProcessHold <= 0f;
            m_ProcessHold += Time.deltaTime * GridSystem.ItemNetwork.LocalProcessMultiplier();   // 2vs2 공정 버프/디버프

            if (strokeStart && m_ProcessKind == ProcessType.Painted)   // 붓질 시작 = 사운드 시작(바와 동시 출발)
            {
                Vector3 sp = GridCoordinates.CellToWorld(m_ProcessCell) + new Vector3(0.5f, 0.9f, 0.5f) * GridContract.Unit;
                PaintStrokeSfx(true, sp);
                if (IsSpawned) RequestPaintStrokeRpc(true, sp);
            }

            float dur = ProcessDurationFor(m_ProcessKind);   // 페인트=사운드 길이, 망치=m_ProcessSeconds

            // 공정 이펙트 0.5초 간격(망치=타격 세트 / 페인트=붓질+초록 방울, 소리는 스트로크가 담당) — 로컬 즉시 + 원격 복제.
            // 완료타와 겹치면 같은 소리가 중복되므로, 완료 직전 틱은 건너뜀.
            m_HitFxTimer -= Time.deltaTime;
            if (m_HitFxTimer <= 0f && m_ProcessHold + 0.45f < dur)
            {
                m_HitFxTimer = 0.5f;
                Vector3 hit = GridCoordinates.CellToWorld(m_ProcessCell) + new Vector3(0.5f, 0.9f, 0.5f) * GridContract.Unit;
                if (m_ProcessKind == ProcessType.Fixed)
                {
                    SpawnHammerFx(hit);
                    if (IsSpawned) RequestHammerFxRpc(hit);
                }
                else
                {
                    SpawnPaintFx(hit);
                    if (IsSpawned) RequestPaintFxRpc(hit);
                }
            }

            if (m_ProcessHold >= dur)
            {
                m_Net.RequestProcess(m_ProcessCell, (int)m_HeldTool, true);   // 서버가 점유/순서 재검증

                Vector3 done = GridCoordinates.CellToWorld(m_ProcessCell) + new Vector3(0.5f, 0.9f, 0.5f) * GridContract.Unit;
                if (m_HeldTool == ProcessType.Fixed)   // 고정 완료 — 스윙 착점에 챙!(소리·별·스퀴시·카메라 전부 싱크)
                {
                    SpawnFixDoneFx(done);
                    if (IsSpawned) RequestFixDoneFxRpc(done);
                }
                else                                   // 페인트 완료 — 붓질 착점에 초록 팡
                {
                    SpawnPaintDoneFx(done);
                    if (IsSpawned) RequestPaintDoneFxRpc(done);
                }

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
            if (m_AimedRevertCell != m_RevertCell) { m_RevertCell = m_AimedRevertCell; m_RevertHold = 0f; }
            m_RevertHold += Time.deltaTime;
            if (m_RevertHold >= m_ProcessSeconds)
            {
                m_Net.RequestCancelLast(m_RevertCell);
                m_RevertHold = 0f;
                m_RevertDone = true;
            }
        }

        // 되돌릴 게 있나: 건축 중 + 조준 XZ 내 층 ±2에 완료된 공정 비트가 있는 블록(공정과 같은 완화 규칙).
        private Vector3Int m_AimedRevertCell = s_NoCell;
        private bool RevertReadyOnTarget()
        {
            m_AimedRevertCell = s_NoCell;
            if (m_Loop != null && !m_Loop.IsBuilding) return false;
            if (!TryFindNearbyCell(c => m_Net.TryGetCell(c, out _, out int completed) && completed != 0, out var cell))
                return false;
            m_AimedRevertCell = cell;
            return true;
        }

        // 공정·되돌리기 층 완화: 조준 XZ에서 내 층 ±2 안의 블록(가까운 층 우선)을 대상으로 삼는다.
        // "같은 층이어야만 망치질 가능"이던 답답함 완화 — 배치는 층 안내(고스트)와 강결합이라 그대로 둔다.
        private static readonly int[] s_FloorSlack = { 0, 1, -1, 2, -2 };
        private Vector3Int m_AimedProcessCell = s_NoCell;   // 이번 프레임 공정 대상(ToolReadyOnTarget이 갱신)

        private bool TryFindNearbyCell(System.Func<Vector3Int, bool> ok, out Vector3Int cell)
        {
            cell = s_NoCell;
            if (!m_HasTarget || m_Net == null) return false;
            foreach (int dy in s_FloorSlack)
            {
                var c = new Vector3Int(m_Target.x, m_Target.y + dy, m_Target.z);
                if (c.y < 0) continue;
                if (ok(c)) { cell = c; return true; }
            }
            return false;
        }

        // 조준 XZ의 내 층 ±2에서 '든 도구가 지금 필요한' 블록 찾기(가까운 층 우선) — 공정·호버 테두리 공용.
        private bool TryAimProcessCell(out Vector3Int cell)
        {
            cell = s_NoCell;
            if (!HasTool || m_Net == null) return false;
            if (m_Loop != null && !m_Loop.IsBuilding) return false;
            return TryFindNearbyCell(c =>
                {
                    if (!m_Net.TryGetCell(c, out int matId, out int completed)) return false;   // 빈 칸이면 공정 없음
                    if (m_PendingCell == c && (completed & (int)m_PendingKind) == 0) return false;   // 복제 대기 중
                    var d = Catalog() != null ? Catalog().GetById(matId) : null;
                    return NextNeeded(d != null ? d.RequiredMask : 0, completed) == m_HeldTool;
                }, out cell);
        }

        // 든 도구의 공정이 조준 블록의 '지금 필요한 다음 공정'과 일치하면 true. (서버 수락 조건과 동일 판단)
        private bool ToolReadyOnTarget()
        {
            if (!TryAimProcessCell(out var cell)) { m_AimedProcessCell = s_NoCell; return false; }
            m_PendingCell = s_NoCell; m_PendingKind = ProcessType.None;   // 반영됨/다른셀 → 대기 해제
            m_AimedProcessCell = cell;
            return true;
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
        // 단, '내가 서 있던 빈 칸이 방금 점유됨' = 블록이 내 위에 배치된 것 — 이때는 블록을 부수지 않고
        // 나를 블록 밖으로 밀어낸다(큰 블록을 유저 근처에 놓아도 억울하게 안 부서지게).
        private Vector3Int m_PrevStandCell = s_NoCell;
        private bool m_PrevStandFree;

        private void TryBumpCollapse()
        {
            if (m_Net == null) return;
            if (m_Loop != null && !m_Loop.IsBuilding) return;

            var pc = GridCoordinates.WorldToCell(transform.position);
            bool free = m_Net.IsCellFree(pc);
            if (!free)
            {
                bool placedOnMe = pc == m_PrevStandCell && m_PrevStandFree;   // 서 있던 자리에 블록이 생김
                if (placedOnMe)
                {
                    PushOutOfBlock(pc);
                    m_LastShockCell = pc;   // 밀려나는 동안 이 셀에 충격 안 보냄(블록 유지)
                }
                else if (pc != m_LastShockCell) { m_LastShockCell = pc; m_Net.RequestShock(pc); }
            }
            else m_LastShockCell = s_NoCell;
            m_PrevStandCell = pc;
            m_PrevStandFree = free;
        }

        // 블록 발자국 바깥 가장 가까운 지점으로 밀어낸다(수평 유지 — 떨어지면 중력이 알아서).
        private readonly System.Collections.Generic.List<Vector3Int> m_PushCells = new();
        private void PushOutOfBlock(Vector3Int cell)
        {
            float u = GridContract.Unit;
            Vector3 min, max;
            if (m_Net.TryGetBlockCells(cell, m_PushCells) && m_PushCells.Count > 0)
            {
                min = GridCoordinates.CellToWorld(m_PushCells[0]);
                max = min + Vector3.one * u;
                foreach (var c in m_PushCells)
                {
                    var w = GridCoordinates.CellToWorld(c);
                    min = Vector3.Min(min, w);
                    max = Vector3.Max(max, w + Vector3.one * u);
                }
            }
            else
            {
                min = GridCoordinates.CellToWorld(cell);
                max = min + Vector3.one * u;
            }

            var center = (min + max) * 0.5f;
            var dir = transform.position - center; dir.y = 0f;
            if (dir.sqrMagnitude < 1e-4f) dir = Vector3.right;   // 정중앙이면 아무 방향
            dir.Normalize();
            float extent = Mathf.Max(max.x - min.x, max.z - min.z) * 0.5f;
            var dest = new Vector3(center.x, transform.position.y, center.z) + dir * (extent + 0.7f);

            transform.position = dest;
            var rb = GetComponent<Rigidbody>();
            if (rb != null && !rb.isKinematic) { rb.position = dest; rb.linearVelocity = Vector3.zero; }
            Physics.SyncTransforms();
            GridJuice.GroundHit(dest, 0.45f);   // 툭 밀려난 느낌
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
            GridContract.LocalBuildFloor = m_BuildHeight;   // 정답 고스트가 '내가 선 층'만 보이게(층별 안내)

            float planeY = GridContract.Origin.y + m_BuildHeight * GridContract.Unit;
            var plane = new Plane(Vector3.up, new Vector3(0f, planeY, 0f));
            var ray = m_Cam.ScreenPointToRay(Mouse.current.position.ReadValue());
            if (plane.Raycast(ray, out float d))
            {
                // 커서 = 블록 '중앙'이 되도록 앵커(min-corner)를 반칸씩 당긴다 — 좌하단 기준이던 어색함 제거.
                var aim = ray.GetPoint(d);
                if (HasMaterial && m_HeldMaterial != null)
                {
                    var fp = m_HeldMaterial.Footprint;
                    bool swap = ((m_Rotation % 4) + 4) % 4 % 2 == 1;   // 90°/270° 회전 시 x/z 치수 스왑
                    aim -= new Vector3(((swap ? fp.z : fp.x) - 1) * 0.5f * GridContract.Unit, 0f,
                                       ((swap ? fp.x : fp.z) - 1) * 0.5f * GridContract.Unit);
                }
                var c = GridCoordinates.WorldToCell(aim);
                c.y = m_BuildHeight;
                var s = m_Grid.EffectiveSize;   // 2vs2는 X 2배 — GridSize(한 팀 폭)로 재면 팀B 구역이 그리드 밖 판정
                var (xMin, xMax) = PlaceableXRange(s);
                m_Target = c;
                m_HasTarget = c.x >= xMin && c.x < xMax && c.z >= 0 && c.z < s.z
                           && m_BuildHeight >= 0 && m_BuildHeight < s.y;

                // 빈손(회수/공정): 평면 교차점 대신 '마우스 레이가 실제로 맞는 배치 블록'의 셀을 우선 — 블록 윗면을 보거나
                // 블록 위에 서 있어도 클릭한 그 블록이 잡힌다(평면만 쓰면 층이 달라 엉뚱한 빈 칸을 가리킴).
                if (!HasMaterial && m_Net != null &&
                    Physics.Raycast(ray, out var bh, 100f, ~(1 << 2), QueryTriggerInteraction.Ignore) &&
                    bh.collider.transform != transform && !bh.collider.transform.IsChildOf(transform) &&
                    !bh.collider.CompareTag("Player"))
                {
                    var bc = GridCoordinates.WorldToCell(bh.point - bh.normal * (0.05f * GridContract.Unit));
                    if (m_Net.IsPickupable(bc) || (HasTool && m_Net.VisualAt(bc) != null))
                    {
                        m_Target = bc;
                        m_HasTarget = bc.x >= xMin && bc.x < xMax && bc.z >= 0 && bc.z < s.z && bc.y >= 0 && bc.y < s.y;
                    }
                }

                // [07/26 기획] 배치/회수/공정 사거리 = 플레이어 최대 2칸.
                // 중심점이 아니라 '블록이 차지한 셀 중 가장 가까운 셀'까지의 거리 — 큰 블록도 가장자리에 서면 닿는다.
                if (m_HasTarget)
                    m_HasTarget = GridReach.InReach(transform.position, ReachCells(m_Target),
                                                    GridContract.Origin, GridContract.Unit, kBuildReachCells);
            }
        }

        // 사거리 판정 대상 셀: 들고 있으면 놓을 자리(풋프린트 전체), 빈손이면 가리킨 블록이 차지한 셀 전체.
        // 어느 쪽도 아니면 가리킨 칸 하나.
        private readonly System.Collections.Generic.List<Vector3Int> m_ReachCells = new();
        private System.Collections.Generic.List<Vector3Int> ReachCells(Vector3Int target)
        {
            if (HasMaterial && m_HeldMaterial != null)
                return GridFootprint.EnumerateFootprintCells(target, m_HeldMaterial.Footprint, m_Rotation);

            if (m_Net != null && m_Net.TryGetBlockCells(target, m_ReachCells)) return m_ReachCells;

            m_ReachCells.Clear();
            m_ReachCells.Add(target);
            return m_ReachCells;
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
            m_GrabCargoOf = null;
            GameObject hitGo = null;

            if (!HasMaterial && !HasTool && m_Cam != null && Mouse.current != null)
            {
                var ray = m_Cam.ScreenPointToRay(Mouse.current.position.ReadValue());
                float reach2 = m_GrabRange * m_GrabRange;
                float best = float.MaxValue;
                foreach (var h in Physics.RaycastAll(ray, 100f, ~0, QueryTriggerInteraction.Collide))
                {
                    var cc = h.collider.GetComponentInParent<CarryCargo>();   // 남이 든 화물 → 클릭하면 같이 들기
                    if (cc != null)
                    {
                        if (cc.Carrier == null || cc.Carrier == this) continue;
                        var dc = cc.transform.position - transform.position; dc.y = 0f;
                        if (dc.sqrMagnitude > m_JoinRange * m_JoinRange) continue;
                        if (h.distance < best) { best = h.distance; m_GrabCargoOf = cc.Carrier; m_GrabBody = null; m_GrabStation = null; hitGo = cc.gameObject; }
                        continue;
                    }
                    var pb = h.collider.GetComponentInParent<PickupBody>();   // 바닥 픽업 우선
                    if (pb != null && pb.Owner != null)
                    {
                        // 손 닿는 거리는 수평(XZ)만 판정 — 높이 차로 범위를 다 까먹어 "같은 층이어야만
                        // 집히는" 답답함 제거(배치처럼 층 제한 없이, 곤돌라 안 화물도 아래에서 집힌다).
                        var dp = pb.transform.position - transform.position; dp.y = 0f;
                        if (dp.sqrMagnitude > reach2) continue;
                        if (h.distance < best) { best = h.distance; m_GrabBody = pb; m_GrabStation = null; hitGo = pb.gameObject; }
                        continue;
                    }
                    var ws = h.collider.GetComponentInParent<Workstation>();  // 도구함(도구 집기)
                    if (ws != null)
                    {
                        var dw = ws.transform.position - transform.position; dw.y = 0f;
                        if (dw.sqrMagnitude > reach2) continue;
                        if (h.distance < best) { best = h.distance; m_GrabStation = ws; m_GrabBody = null; hitGo = ws.gameObject; }
                    }
                }
            }
            m_GrabValid = m_GrabBody != null || m_GrabStation != null || m_GrabCargoOf != null;

            // 바닥 픽업·도구함이 아니면: ① 도구 들고 공정 가능한 블록 ② 빈손으로 회수 가능한(미고정) 배치 블록에
            // 초록 테두리 — "지금 이 블록이 대상"을 노란 큐브 대신 실루엣으로 보여준다.
            if (hitGo == null && m_Net != null)
            {
                var kb = Keyboard.current;
                if (kb != null && kb.zKey.isPressed && RevertReadyOnTarget())
                    hitGo = m_Net.VisualAt(m_AimedRevertCell);          // Z 되돌리기 대상
                else if (HasTool && TryAimProcessCell(out var pc))
                    hitGo = m_Net.VisualAt(pc);                          // 공정 대상
                else if (!HasMaterial && !HasTool && m_HasTarget && m_Net.IsPickupable(m_Target))
                    hitGo = m_Net.VisualAt(m_Target);                    // 회수 가능(미고정)
            }

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

        /// <summary>외부 시스템(돌풍 스턴 등)이 든 것을 강제로 떨어뜨릴 때 — 발밑 픽업으로 떨어져 분실 없음. 오너 로컬 전용.</summary>
        public void ForceDrop() => Drop();

        // ── [07/26 기획] G 차징 던지기: 꾹 누를수록 멀리, 화살표로 방향·거리 미리보기 ──
        private float m_ThrowHold = -1f;   // <0 = 충전 안 함
        private GameObject m_ThrowAim;     // 조준 화살표(오너 로컬 비주얼)
        private LineRenderer m_AimShaft, m_AimHead;
        [SerializeField] private Material m_AimLineMat;   // 궤적 선 머티리얼(Hit Me 에셋 점선)
        private const float kThrowMin = 3f;          // 탭 = 기존 최소 로브
        private const float kThrowChargeTime = 0.9f; // 이 시간 꾹 = 최대 사거리

        private void UpdateThrowCharge(Keyboard kb)
        {
            bool holding = HasMaterial || HasTool;
            if (kb.gKey.wasPressedThisFrame && holding && m_Drop != null) m_ThrowHold = 0f;
            if (m_ThrowHold < 0f) return;

            if (!holding) { CancelThrowAim(); return; }   // 충전 중 손이 비면 취소

            m_ThrowHold += Time.deltaTime;
            float charge = Mathf.Clamp01(m_ThrowHold / kThrowChargeTime);
            float dist = Mathf.Lerp(kThrowMin, m_ThrowRange, charge);
            Vector3 dir = AimDir();
            ShowThrowAim(dir, dist, charge);

            if (kb.gKey.wasReleasedThisFrame)
            {
                Throw(dir, dist);
                CancelThrowAim();
            }
        }

        private Vector3 AimDir()
        {
            Vector3 flat = AimWorldPoint() - transform.position; flat.y = 0f;
            return flat.sqrMagnitude > 0.01f ? flat.normalized : transform.forward;
        }

        // 오버쿡드식 던지기: 조준 '방향'으로 붕~ 포물선 로브. 거리 = 충전량(탭=최소 3, 풀차지=사거리).
        private void Throw(Vector3 dir, float dist)
        {
            if (m_Drop == null || (!HasMaterial && !HasTool)) return;
            Vector3 to = transform.position + dir * dist;
            to.y = 0.5f;
            Vector3 from = transform.position + Vector3.up * 1.2f;
            if (HasMaterial) m_Drop.RequestThrow(m_HeldMaterial.Id, from, to);
            else             m_Drop.RequestThrowTool((int)m_HeldTool, from, to);
            PlaySFX(SFXType.ThrowObject);
            GridJuice.FovPunch(m_Cam, 1.6f);   // 던질 때 화면 살짝 벌어졌다 복귀 — 손맛
            ClearHeld();
            OnThrow?.Invoke();
        }

        // 조준 궤적: 실제 비행(PickupBody.SampleArc)과 같은 수식의 포물선 — 앵그리버드처럼 선 그대로 날아감.
        private readonly Vector3[] m_ArcPts = new Vector3[24];

        private void ShowThrowAim(Vector3 dir, float dist, float charge)
        {
            if (m_ThrowAim == null)
            {
                m_ThrowAim = new GameObject("~ThrowAim");
                m_AimShaft = MakeAimLine(m_ThrowAim.transform, m_ArcPts.Length);
                m_AimHead  = MakeAimLine(m_ThrowAim.transform, 3);
            }
            m_ThrowAim.SetActive(true);

            Vector3 from = transform.position + Vector3.up * 1.2f;              // Throw()의 출발점과 동일
            Vector3 to = transform.position + dir * dist; to.y = 0.5f;          // Throw()의 목표점과 동일
            PickupBody.SampleArc(from, to, m_ArcPts);

            var c = Color.Lerp(new Color(1f, 0.75f, 0.2f, 0.9f), new Color(1f, 0.3f, 0.15f, 0.95f), charge);
            float w = Mathf.Lerp(0.14f, 0.24f, charge);

            m_AimShaft.startColor = m_AimShaft.endColor = c;
            m_AimShaft.startWidth = w; m_AimShaft.endWidth = w * 0.7f;
            m_AimShaft.SetPositions(m_ArcPts);

            // 착지점 V자 촉(마지막 구간 접선 방향)
            Vector3 tang = (m_ArcPts[m_ArcPts.Length - 1] - m_ArcPts[m_ArcPts.Length - 2]).normalized;
            Vector3 side = Vector3.Cross(Vector3.up, dir);
            Vector3 tip = m_ArcPts[m_ArcPts.Length - 1];
            m_AimHead.startColor = m_AimHead.endColor = c;
            m_AimHead.startWidth = m_AimHead.endWidth = w;
            m_AimHead.SetPosition(0, tip - tang * 0.5f + side * 0.32f);
            m_AimHead.SetPosition(1, tip);
            m_AimHead.SetPosition(2, tip - tang * 0.5f - side * 0.32f);
        }

        private LineRenderer MakeAimLine(Transform parent, int points)
        {
            var go = new GameObject("~AimLine");
            go.transform.SetParent(parent, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.positionCount = points;
            lr.useWorldSpace = true;
            lr.numCapVertices = 4;
            lr.numCornerVertices = 4;
            lr.alignment = LineAlignment.View;
            lr.textureMode = LineTextureMode.Tile;   // HitMe 점선 텍스처가 길이 따라 반복
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;

            if (m_AimLineMat != null)
            {
                lr.material = m_AimLineMat;   // Hit Me 에셋의 점선 라인 머티리얼(에셋 참조 → 빌드 안전)
                return lr;
            }
            var sh = Shader.Find("Universal Render Pipeline/Lit");   // 폴백(빌드 셰이더 스트립 안전 계열)
            if (sh == null) sh = Shader.Find("Sprites/Default");
            if (sh != null)
            {
                var m = new Material(sh);
                m.SetFloat("_Surface", 1f);
                m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.SetInt("_ZWrite", 0);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                lr.material = m;
            }
            return lr;
        }

        private void CancelThrowAim()
        {
            m_ThrowHold = -1f;
            if (m_ThrowAim != null) m_ThrowAim.SetActive(false);
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

        // 배치 허용 X 범위 [min, max) — 2vs2는 자기 팀 구역만(서버 GridNetwork.ZoneAllowed와 동일 기준).
        // 클라에서 먼저 거르지 않으면 분할벽 근처에서 상대 구역에 놓는 요청이 서버 거부돼 재료만 사라진다.
        private (int xMin, int xMax) PlaceableXRange(Vector3Int effective)
        {
            if (m_Loop == null || !m_Loop.IsVersus || m_Grid == null) return (0, effective.x);
            int w = m_Grid.ZoneSize.x;
            int team = m_Loop.LocalTeam;
            if (team == 0) return (0, w);
            if (team == 1) return (w, effective.x);
            return (0, effective.x);   // 팀 미배정 과도기 — 서버가 최종 검증
        }

        private void TryPlace()
        {
            if (m_Loop != null && !m_Loop.IsBuilding) return;
            if (!m_HasTarget || m_Net == null || m_Grid == null) return;
            var s = m_Grid.EffectiveSize;   // 2vs2는 X 2배(팀B 구역 포함)
            var (xMin, xMax) = PlaceableXRange(s);
            foreach (var cell in GridFootprint.EnumerateFootprintCells(m_Target, m_HeldMaterial.Footprint, m_Rotation))
            {
                if (cell.x < xMin || cell.x >= xMax || cell.y < 0 || cell.y >= s.y || cell.z < 0 || cell.z >= s.z) { ShakePreview(); return; }
                if (!m_Net.IsCellFree(cell)) { ShakePreview(); return; }
            }
            // 서버와 동일한 지지검사 — 거부될 자리면 손에 든 채 유지(재료 손실 방지). 환경 바닥·스캐폴드도 지지로 인정.
            if (!GridSupport.WouldBeSupported(
                    GridFootprint.EnumerateFootprintCells(m_Target, m_HeldMaterial.Footprint, m_Rotation),
                    cell => !m_Net.IsCellFree(cell),
                    cell => GridSupport.ExternalSolidAt(cell, GridContract.Unit)))
            { ShakePreview(); return; }

            m_Net.RequestPlace(m_Target, m_HeldMaterial.Id, (byte)m_Rotation);
            if (SoundManager.Instance != null)   // 놓는 자리서 3D + 피치 랜덤(단조로움 방지)
                SoundManager.Instance.PlaySFXAt(SFXType.LandObject,
                    GridCoordinates.CellToWorld(m_Target) + Vector3.one * (0.5f * GridContract.Unit),
                    Random.Range(0.92f, 1.08f));
            GridSystem.GridJuice.FovPunch(m_Cam, -1.5f);   // 놓는 순간 카메라 살짝 쿵(owner 즉각 반응)
            ClearHeld();   // 놓으면 손이 빔 → 재고서 다시 집어야(리썰컴퍼니식)
            OnPlace?.Invoke();
        }

        private static void PlaySFX(SFXType type)
        {
            if (SoundManager.Instance != null)
                SoundManager.Instance.PlaySFX(type);
        }

        // 망치질 연출(모든 클라): 스윙부터 시작하고, 망치가 '내려찍히는 순간'(kSwingDown)에
        // 타격 세트(스파크·스퀴시·타격음·카메라펀치)를 재생 → 소리/이펙트가 애니메이션과 싱크.
        private const float kSwingDown = 0.09f;   // 내려찍기 시간 = 타격 싱크 기준
        private const float kSwingBack = 0.16f;   // 복귀 시간

        private void SpawnHammerFx(Vector3 pos)
        {
            SwingHeldTool();                                              // ① 스윙 시작
            if (isActiveAndEnabled) StartCoroutine(HammerImpactCo(pos, big: false));   // ② 착점에 타격
        }

        [Rpc(SendTo.Server)]
        private void RequestHammerFxRpc(Vector3 pos) => HammerFxRpc(pos);

        [Rpc(SendTo.NotOwner)]
        private void HammerFxRpc(Vector3 pos) { if (!IsOwner) SpawnHammerFx(pos); }   // 오너는 이미 로컬 재생(이중 방지)

        // 고정 완료: 같은 싱크로 별 타격 + 큰 스퀴시 + (owner만) 카메라 펀치.
        private void SpawnFixDoneFx(Vector3 pos)
        {
            SwingHeldTool();
            if (isActiveAndEnabled) StartCoroutine(HammerImpactCo(pos, big: true));
        }

        // 망치가 닿는 순간의 타격 세트. big = 고정 완료(별·큰 스퀴시·카메라).
        private System.Collections.IEnumerator HammerImpactCo(Vector3 pos, bool big)
        {
            yield return new WaitForSeconds(kSwingDown);   // 내려찍히는 순간에 맞춤

            var prefab = big ? m_FixDoneFx : m_HammerFx;
            if (prefab != null)
            {
                var go = Instantiate(prefab, pos, Quaternion.identity);
                if (!big)
                {
                    go.transform.localScale *= 0.65f;                                  // 블록 스케일에 맞게 축소
                    foreach (var ps in go.GetComponentsInChildren<ParticleSystem>())
                    { var main = ps.main; main.simulationSpeed = 1.5f; }               // 더 빠르게 탁! 튀고 사라짐
                }
                Destroy(go, 5f);   // CFXR 자체 정리 실패 대비 안전망
            }

            var net = m_Net != null ? m_Net : FindFirstObjectByType<GridNetwork>();   // 원격 인스턴스는 m_Net 미탐색
            if (net != null)
            {
                GridJuice.Squish(net.VisualAt(GridCoordinates.WorldToCell(pos)), big ? 0.14f : 0.08f);
                if (big) net.RippleAround(pos, GridContract.Unit * 2.5f, 0.06f);   // 완료 순간 젤리 파동
            }
            if (SoundManager.Instance != null)   // 단타 클립(SFX_Hammering.wav) + 연타 전용 채널
                SoundManager.Instance.PlayTapAt(SFXType.Hammering, pos,
                    big ? Random.Range(0.72f, 0.80f) : Random.Range(0.9f, 1.1f));
            if (big) GridJuice.FovPunch(m_Cam, -2.5f);   // 원격 인스턴스는 m_Cam null → 무시됨
        }

        [Rpc(SendTo.Server)]
        private void RequestFixDoneFxRpc(Vector3 pos) => FixDoneFxRpc(pos);

        [Rpc(SendTo.NotOwner)]
        private void FixDoneFxRpc(Vector3 pos) { if (!IsOwner) SpawnFixDoneFx(pos); }   // 오너는 이미 로컬 재생(이중 방지)

        // 페인트질 연출: 붓질 스윙 후 착점에 초록 방울 + 페인트 소리(연타 채널·길이 컷).
        private void SpawnPaintFx(Vector3 pos)
        {
            SwingHeldTool();
            if (isActiveAndEnabled) StartCoroutine(PaintSplashCo(pos, big: false));
        }

        private void SpawnPaintDoneFx(Vector3 pos)
        {
            SwingHeldTool();
            if (isActiveAndEnabled) StartCoroutine(PaintSplashCo(pos, big: true));
        }

        private static readonly Color kPaintOrange = new Color(1f, 0.55f, 0.15f);   // 페인트 튀김 색

        private System.Collections.IEnumerator PaintSplashCo(Vector3 pos, bool big)
        {
            yield return new WaitForSeconds(kSwingDown);   // 붓이 닿는 순간에 맞춤

            if (m_PaintFx != null)   // 피 튀김 이펙트를 주황으로 틴트 → 페인트 튀김
            {
                var go = Instantiate(m_PaintFx, pos, Quaternion.identity);
                go.transform.localScale *= big ? 1f : 0.55f;
                TintParticles(go, kPaintOrange);
                Destroy(go, 5f);
            }
            else
                GridJuice.PaintPop(pos, GridContract.Unit, big ? 1.6f : 1f);   // 프리팹 없으면 방울 폴백

            if (big)
            {
                var net = m_Net != null ? m_Net : FindFirstObjectByType<GridNetwork>();
                if (net != null) GridJuice.Squish(net.VisualAt(GridCoordinates.WorldToCell(pos)), 0.10f);
            }
            // 소리는 스트로크 사운드(PaintStrokeSfx)가 로딩바와 함께 1회 담당 — 여기선 비주얼만.
        }

        // 붓질 사운드: 로딩바와 함께 시작·종료(클립 길이 = kPaintSeconds). 피치 고정(길이 싱크 유지).
        private void PaintStrokeSfx(bool start, Vector3 pos)
        {
            if (SoundManager.Instance == null) return;
            if (start) SoundManager.Instance.PlayTapAt(SFXType.Painting, pos, 1f);
            else SoundManager.Instance.StopTap();
        }

        // 스트로크 중단(E 뗌/대상 무효/셀 변경) → 소리도 함께 끊음.
        private void CancelPaintStroke()
        {
            if (m_ProcessKind != ProcessType.Painted || m_ProcessHold <= 0f) return;
            PaintStrokeSfx(false, default);
            if (IsSpawned) RequestPaintStrokeRpc(false, default);
        }

        [Rpc(SendTo.Server)]
        private void RequestPaintStrokeRpc(bool start, Vector3 pos) => PaintStrokeRpc(start, pos);

        [Rpc(SendTo.NotOwner)]
        private void PaintStrokeRpc(bool start, Vector3 pos) { if (!IsOwner) PaintStrokeSfx(start, pos); }

        // 파티클 색 오버라이드(startColor) — CFXR 계열은 버텍스 컬러 기반이라 이걸로 전체 틴트됨.
        private static void TintParticles(GameObject go, Color c)
        {
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>())
            {
                var main = ps.main;
                main.startColor = new ParticleSystem.MinMaxGradient(c);
            }
        }

        [Rpc(SendTo.Server)]
        private void RequestPaintFxRpc(Vector3 pos) => PaintFxRpc(pos);

        [Rpc(SendTo.NotOwner)]
        private void PaintFxRpc(Vector3 pos) { if (!IsOwner) SpawnPaintFx(pos); }

        [Rpc(SendTo.Server)]
        private void RequestPaintDoneFxRpc(Vector3 pos) => PaintDoneFxRpc(pos);

        [Rpc(SendTo.NotOwner)]
        private void PaintDoneFxRpc(Vector3 pos) { if (!IsOwner) SpawnPaintDoneFx(pos); }

        // 든 도구 내려찍기 스윙(플레이어가 보는 방향 기준). 모든 클라에서 재생.
        private Coroutine m_SwingCo;
        private void SwingHeldTool()
        {
            if (m_HeldVisual == null || !isActiveAndEnabled) return;
            if (m_SwingCo != null) StopCoroutine(m_SwingCo);
            m_SwingCo = StartCoroutine(SwingCo());
        }

        private System.Collections.IEnumerator SwingCo()
        {
            var t = m_HeldVisual.transform;
            const float down = kSwingDown, back = kSwingBack;
            for (float e = 0f; e < down && t != null; e += Time.deltaTime)   // 휙 내려찍기
            {
                t.rotation = transform.rotation * Quaternion.Euler(Mathf.Lerp(0f, -70f, e / down), 0f, 0f);
                yield return null;
            }
            for (float e = 0f; e < back && t != null; e += Time.deltaTime)   // 되돌아오기(감속)
            {
                float n = e / back;
                t.rotation = transform.rotation * Quaternion.Euler(Mathf.Lerp(-70f, 0f, 1f - (1f - n) * (1f - n)), 0f, 0f);
                yield return null;
            }
            if (t != null) t.rotation = Quaternion.identity;
            m_SwingCo = null;
        }

        // ── 비주얼(상태 구동, 모든 클라) ───────────────────────────────────
        private void RebuildHeldVisual()
        {
            if (m_HeldVisual != null) { Destroy(m_HeldVisual); m_HeldVisual = null; }
            m_HeldDef = null;

            int matId = m_NetMaterialId.Value;
            int tool = m_NetTool.Value;

            if (matId >= 0)
            {
                var def = FindMaterial(matId);
                if (def == null) return;
                m_HeldDef = def;
                var fp = def.Footprint;
                float u = GridContract.Unit;
                var size = new Vector3(Mathf.Max(1, fp.x), Mathf.Max(1, fp.y), Mathf.Max(1, fp.z)) * u;
                if (def.Prefab != null)   // 진짜 블록 외형 — 원본 크기 그대로, 중심 정렬
                {
                    m_HeldVisual = new GameObject("~Held");
                    var vis = Instantiate(def.Prefab, m_HeldVisual.transform);
                    vis.transform.localPosition = new Vector3(-fp.x * 0.5f, -fp.y * 0.5f, -fp.z * 0.5f) * u;   // 피벗(min-corner) → 중앙 정렬
                    foreach (var c in m_HeldVisual.GetComponentsInChildren<Collider>()) Destroy(c);
                }
                else                      // 프리팹 없음 → 공정색 큐브(폴백)
                {
                    m_HeldVisual = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    m_HeldVisual.transform.localScale = size;
                    Paint(m_HeldVisual, ColorForMask(def.RequiredMask));
                    StripCollider(m_HeldVisual);
                }
                AttachCargoPusher(m_HeldVisual, def.Prefab != null ? size : Vector3.one);   // 앞에 단 화물: 다른 플레이어를 밀어낸다
            }
            else if (tool != 0)   // 든 도구 — 망치(고정)는 모델, 그 외/폴백은 공정색 구
            {
                var model = (tool & (int)ProcessType.Fixed) != 0 ? m_HammerModel
                          : (tool & (int)ProcessType.Painted) != 0 ? m_PaintCanModel
                          : null;
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
            {
                m_HeldVisual.transform.position = transform.position + HeldOffset();
                m_HeldPrevPos = transform.position;   // 바운스 속도 계산 초기화(첫 프레임 튐 방지)
                m_CargoDirInit = false;
                m_HeldSwayVel = Vector3.zero;
                GridJuice.Squish(m_HeldVisual, 0.22f);   // 집는 순간 뽁 — 손맛
            }

            Debug.Log($"[FXSync] RebuildHeld {(IsOwner ? "owner" : "remote")} mat={matId} tool={tool} visual={(m_HeldVisual != null)}", this);
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

        // ── 앞에 단 화물 = 키네마틱 박스 콜라이더: 다른 플레이어(다이내믹 바디)를 채서 밀어낸다(무빙아웃 개그).
        // Ignore Raycast 레이어라 조준/줍기 레이캐스트는 안 막고, 본인 몸과는 충돌 무시.
        private void AttachCargoPusher(GameObject cargo, Vector3 size)
        {
            foreach (var t in cargo.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 2;   // Ignore Raycast
            var box = cargo.AddComponent<BoxCollider>();
            box.size = size * 0.92f;   // 모서리 살짝 여유(스치기만 해도 튕기지 않게)
            box.center = Vector3.zero;
            var rb = cargo.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            foreach (var own in GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(box, own, true);
            cargo.AddComponent<CarryCargo>().Carrier = this;
        }

        // ── 무거운 재료: 혼자 들면 느림(땀) · 빈손 동료가 화물 옆에 붙으면 정상 속도(owner 판정 → 복제)
        private void UpdateHeavyState()
        {
            bool heavy = HasMaterial && m_HeldMaterial.IsHeavy;
            bool helped = heavy && IsSharedCarry;   // 동료가 화물을 클릭해 '같이 들기' 중이면 정상 속도
            MoveMultiplier = heavy && !helped ? m_HeavySoloSpeed : 1f;
            bool straining = heavy && !helped;
            if (m_NetStraining.Value != straining) m_NetStraining.Value = straining;
        }

        // ── 땀 이펙트(모든 클라): 머리 옆에서 물방울 톡톡. 에셋 의존 없이 코드로 생성.
        private void UpdateSweatFx()
        {
            bool on = m_NetStraining.Value;
            if (!on && m_SweatFx == null) return;
            if (m_SweatFx == null) m_SweatFx = BuildSweatFx(transform);
            var em = m_SweatFx.emission;
            if (em.enabled != on) em.enabled = on;
        }

        private static Texture2D s_DropTex;
        private static ParticleSystem BuildSweatFx(Transform parent)
        {
            var go = new GameObject("~SweatFx");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 1.45f, 0f);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true; main.playOnAwake = true;
            main.startLifetime = 0.7f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 2.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.10f, 0.16f);
            main.startColor = new Color(0.55f, 0.80f, 1f, 0.95f);
            main.gravityModifier = 1.2f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 24;
            var em = ps.emission; em.rateOverTime = 7f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Hemisphere; sh.radius = 0.22f;
            var r = ps.GetComponent<ParticleSystemRenderer>();
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Particles/Standard Unlit") ?? Shader.Find("Sprites/Default");
            if (shader != null)
            {
                var mat = new Material(shader);
                if (s_DropTex == null) s_DropTex = MakeDropTexture();
                mat.mainTexture = s_DropTex;
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", s_DropTex);
                if (mat.HasProperty("_Surface"))   // URP 파티클: 투명(알파 블렌드)
                {
                    mat.SetFloat("_Surface", 1f); mat.SetFloat("_Blend", 0f); mat.renderQueue = 3000;
                    mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                    mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                    mat.SetInt("_ZWrite", 0);
                }
                r.material = mat;
            }
            return ps;
        }

        private static Texture2D MakeDropTexture()   // 부드러운 원(물방울)
        {
            const int n = 32;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n - 0.5f, dy = (y + 0.5f) / n - 0.5f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
                    float a = Mathf.Clamp01(1f - Mathf.SmoothStep(0.6f, 1f, d));
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            tex.Apply();
            return tex;
        }

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
        private readonly List<Material> m_PreviewGhostMats = new();   // 프리팹 고스트 머티리얼(정리용)
        private int m_PreviewKey = int.MinValue;                      // 현재 프리뷰 종류((재료Id<<2)|회전, 박스=-1)
        private Vector3 m_PreviewOffset;                              // 프리팹 프리뷰 피벗 오프셋(빌드 시 1회 산출)
        private static readonly int s_PvBase = Shader.PropertyToID("_BaseColor");
        private static readonly int s_PvCol  = Shader.PropertyToID("_Color");
        private static readonly int s_PvBaseMap = Shader.PropertyToID("_BaseMap");
        private static readonly int s_PvMainTex = Shader.PropertyToID("_MainTex");

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

            float u = GridContract.Unit;

            // 재료(프리팹 있음) → 실제 블록을 반투명 고스트로: 놓일 모양 그대로(회전·연석 방향까지).
            if (HasMaterial && m_HeldMaterial.Prefab != null)
            {
                int key = (m_HeldMaterial.Id << 2) | (m_Rotation & 3);
                if (m_Preview == null || m_PreviewKey != key) BuildPrefabPreview(key);   // 재료/회전 바뀔 때만 재빌드

                var cells = GridFootprint.EnumerateFootprintCells(m_Target, m_HeldMaterial.Footprint, m_Rotation);
                Vector3Int minCell = cells[0];
                for (int i = 1; i < cells.Count; i++) minCell = Vector3Int.Min(minCell, cells[i]);
                m_Preview.transform.position = GridCoordinates.CellToWorld(minCell) + m_PreviewOffset;   // 위치만 매 프레임
                if (!m_Preview.activeSelf) m_Preview.SetActive(true);

                float pa = 0.40f + 0.10f * Mathf.Abs(Mathf.Sin(Time.time * 3.5f));   // 살아있는 청사진 숨쉬기
                for (int i = 0; i < m_PreviewGhostMats.Count; i++)
                    if (m_PreviewGhostMats[i] != null)
                    {
                        var c = m_PreviewGhostMats[i].GetColor(s_PvBase); c.a = pa;
                        m_PreviewGhostMats[i].SetColor(s_PvBase, c);
                        m_PreviewGhostMats[i].SetColor(s_PvCol, c);
                    }
                return;
            }

            // 도구 공정 대상은 노란 박스 대신 블록 테두리(UpdateGrabTarget)로 보여준다 — 박스는 배치 폴백 전용.
            if (HasTool)
            {
                if (m_Preview != null && m_Preview.activeSelf) m_Preview.SetActive(false);
                return;
            }

            // 폴백(프리팹 없는 재료) → 반투명 박스.
            if (m_Preview == null || m_PreviewKey != -1) BuildBoxPreview();

            Vector3 center, size; Color col;
            HeldPlacementBox(out center, out size);
            col = new Color(0.25f, 0.9f, 1f, 0.32f);    // 시안: 배치 자리
            m_Preview.transform.SetPositionAndRotation(center, Quaternion.identity);
            m_Preview.transform.localScale = size;
            m_PreviewMat.SetColor(s_PvBase, col);
            m_PreviewMat.SetColor(s_PvCol, col);
            if (!m_Preview.activeSelf) m_Preview.SetActive(true);
        }

        // 실제 블록 프리팹을 반투명 고스트로. 회전/피벗 오프셋은 PlaceRotatedPrefab으로 1회 산출(이후 위치만 갱신).
        private void BuildPrefabPreview(int key)
        {
            DestroyPreview();
            m_Preview = Instantiate(m_HeldMaterial.Prefab);
            m_Preview.name = "~PlacePreview";
            foreach (var c in m_Preview.GetComponentsInChildren<Collider>()) Destroy(c);
            MakePreviewTransparent(m_Preview, 0.45f);
            // cellWorldMin=0으로 배치 → 결과 position = 순수 피벗 오프셋(이후 CellToWorld(minCell)에 더함). 회전은 여기서 확정.
            GridFootprint.PlaceRotatedPrefab(m_Preview, Vector3.zero, m_HeldMaterial.Footprint, m_Rotation, GridContract.Unit);
            m_PreviewOffset = m_Preview.transform.position;
            m_PreviewKey = key;
        }

        private void BuildBoxPreview()
        {
            DestroyPreview();
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "~PlacePreview";
            var c = go.GetComponent<Collider>(); if (c != null) Destroy(c);
            go.GetComponent<Renderer>().sharedMaterial = PreviewMat();
            m_Preview = go;
            m_PreviewKey = -1;
        }

        // 배치 실패 도리도리: 프리뷰가 좌우로 흔들리며 "여긴 안 돼" 신호(감쇠).
        private Coroutine m_ShakeCo;
        private void ShakePreview()
        {
            if (m_Preview == null || !isActiveAndEnabled) return;
            if (m_ShakeCo != null) StopCoroutine(m_ShakeCo);
            m_ShakeCo = StartCoroutine(ShakePreviewCo());
        }

        private System.Collections.IEnumerator ShakePreviewCo()
        {
            var t = m_Preview.transform;
            var baseRot = t.rotation;
            const float dur = 0.25f;
            for (float e = 0f; e < dur && t != null; e += Time.deltaTime)
            {
                float decay = 1f - e / dur;
                t.rotation = baseRot * Quaternion.Euler(0f, Mathf.Sin(e * 55f) * 8f * decay, 0f);
                yield return null;
            }
            if (t != null) t.rotation = baseRot;
            m_ShakeCo = null;
        }

        private void DestroyPreview()
        {
            if (m_Preview != null) { Destroy(m_Preview); m_Preview = null; }
            for (int i = 0; i < m_PreviewGhostMats.Count; i++)
                if (m_PreviewGhostMats[i] != null) Destroy(m_PreviewGhostMats[i]);
            m_PreviewGhostMats.Clear();
        }

        // 렌더러 머티리얼을 반투명 URP Lit 사본으로 교체(원본 색/텍스처 유지 → 진짜 블록처럼 보이되 고스트). 사본은 정리용 리스트에.
        private void MakePreviewTransparent(GameObject go, float alpha)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) return;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                var src = r.sharedMaterials;
                var dst = new Material[src.Length];
                for (int i = 0; i < src.Length; i++)
                {
                    var m = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
                    m.SetOverrideTag("RenderType", "Transparent");
                    m.SetFloat("_Surface", 1f);
                    m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    m.SetInt("_ZWrite", 0);
                    m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

                    Color tint = Color.white;
                    if (src[i] != null)
                    {
                        if      (src[i].HasProperty(s_PvBaseMap) && src[i].GetTexture(s_PvBaseMap) != null) m.SetTexture(s_PvBaseMap, src[i].GetTexture(s_PvBaseMap));
                        else if (src[i].HasProperty(s_PvMainTex) && src[i].GetTexture(s_PvMainTex) != null) m.SetTexture(s_PvBaseMap, src[i].GetTexture(s_PvMainTex));
                        if      (src[i].HasProperty(s_PvBase)) tint = src[i].GetColor(s_PvBase);
                        else if (src[i].HasProperty(s_PvCol))  tint = src[i].GetColor(s_PvCol);
                    }
                    tint.a = alpha;
                    m.SetColor(s_PvBase, tint);
                    m.SetColor(s_PvCol, tint);
                    m_PreviewGhostMats.Add(m);
                    dst[i] = m;
                }
                r.sharedMaterials = dst;
            }
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

        // ── 프리팹 HUD 구동(구 OnGUI 대체 · 비주얼은 Resources/UI/HUD/CarryHudUI 프리팹) ──
        private void UpdateHud()
        {
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != SceneNames.GameScene)   // 조작법 HUD는 GameScene만
            {
                if (m_Hud != null) m_Hud.gameObject.SetActive(false);
                return;
            }
            if (m_Hud == null)
            {
                if (m_HudMissing || UIManager.Instance == null) return;
                if (Resources.Load<GameObject>("UI/HUD/CarryHudUI") == null)
                {
                    m_HudMissing = true;   // 프리팹 없음 → 예외 스팸 방지(1회 경고)
                    Debug.LogWarning("[PlayerCarry] CarryHudUI 프리팹 없음 — 메뉴 Jobsnail ▸ UI ▸ Generate CarryHud Prefab 실행하세요.");
                    return;
                }
                m_Hud = UIManager.Instance.ShowHUDUI<CarryHudUI>();
                if (m_Hud == null) return;
            }
            if (!m_Hud.gameObject.activeSelf) m_Hud.gameObject.SetActive(true);

            m_Hud.SetHint(BuildHintText());
            UpdateHudBars();

            int pct = m_Net != null ? Mathf.RoundToInt(m_Net.ScorePercent) : 0;   // 완성도 오르면 패널 디용
            if (m_PrevScorePct >= 0 && pct > m_PrevScorePct) m_Hud.PopHint();
            m_PrevScorePct = pct;
        }

        private string BuildHintText()
        {
            string heldStr;
            if (HasMaterial)
                heldStr = $"📦 블록을 들고 있어요!  [R] 키로 방향을 바꾸고,  [좌클릭] 으로 놓을 수 있어요.  (현재 회전: {m_Rotation})";
            else if (HasTool)
                heldStr = m_HeldTool == ProcessType.Fixed
                    ? "🔨 망치를 들고 있어요!  블록을 바라보고  [E] 꾹 눌러서 고정하세요."
                    : "🪣 페인트통을 들고 있어요!  블록을 바라보고  [E] 꾹 눌러서 색칠하세요.";
            else if (!HasMaterial && !HasTool && m_HasTarget && m_Net != null && m_Net.IsPickupable(m_Target))
                heldStr = "✋ 이 블록을 집을 수 있어요!  [좌클릭] 으로 집어보세요.";
            else
                heldStr = "오른쪽 하단에서 재료를 주문하세요! ";

            // 조작법 줄은 좌상단 조작법 툴팁(ControlsTooltipHUD), 완성도는 폰 뱃지로 옮겨져 상황 힌트 한 줄만 남긴다.
            return heldStr;
        }

        // E 공정 / Z 되돌리기 로딩바 + 공정 안내(대상 블록 위 · 월드→스크린 좌표는 여기서 계산).
        private void UpdateHudBars()
        {
            Vector2 sp = default;

            if (m_CannonCharge > 0f)   // 대포 충전은 같은 바를 머리 위에 띄워 게이지로 쓴다
            {
                bool ok = WorldToScreen(transform.position + Vector3.up * 2.2f, out sp);
                m_Hud.SetProcessBar(ok, sp, Mathf.Clamp01(m_CannonCharge / kCannonChargeSeconds),
                    new Color(0.95f, 0.55f, 0.15f), "대포 조준 중… (떼면 발사)");
            }
            else
            {
                bool proc = m_ProcessHold > 0f && m_ProcessCell != s_NoCell
                            && WorldToScreen(GridCoordinates.CellToWorld(m_ProcessCell) + new Vector3(0.5f, 1.1f, 0.5f), out sp);
                m_Hud.SetProcessBar(proc, sp, Mathf.Clamp01(m_ProcessHold / ProcessDurationFor(m_ProcessKind)),
                    m_ProcessKind == ProcessType.Painted ? new Color(0.30f, 0.85f, 0.40f) : new Color(0.35f, 0.60f, 1.00f),
                    m_ProcessKind == ProcessType.Painted ? "페인트 중…" : "고정 중…");
            }

            bool rev = m_RevertHold > 0f && m_RevertCell != s_NoCell
                       && WorldToScreen(GridCoordinates.CellToWorld(m_RevertCell) + new Vector3(0.5f, 1.1f, 0.5f), out sp);
            m_Hud.SetRevertBar(rev, sp, Mathf.Clamp01(m_RevertHold / m_ProcessSeconds),
                new Color(0.90f, 0.45f, 0.30f), "되돌리는 중…");

            // 도구 들고 조준 중일 때(바가 안 차는 동안) 공정 안내
            bool hint = m_ProcessHold <= 0f && !string.IsNullOrEmpty(m_ProcessHint) && m_HasTarget
                        && WorldToScreen(GridCoordinates.CellToWorld(m_Target) + new Vector3(0.5f, 1.3f, 0.5f), out sp);
            m_Hud.SetProcessHint(hint, sp, m_ProcessHint);
        }

        private bool WorldToScreen(Vector3 world, out Vector2 screen)
        {
            screen = default;
            if (m_Cam == null) return false;
            Vector3 sp = m_Cam.WorldToScreenPoint(world);
            if (sp.z <= 0f) return false;   // 카메라 뒤 → 표시 안 함
            screen = new Vector2(sp.x, sp.y);
            return true;
        }
    }

    /// <summary>플레이어가 앞에 안고 가는 화물 표식 — 빈손 동료가 클릭하면 같이 든다.</summary>
    public class CarryCargo : MonoBehaviour
    {
        public PlayerCarry Carrier;
    }
}
