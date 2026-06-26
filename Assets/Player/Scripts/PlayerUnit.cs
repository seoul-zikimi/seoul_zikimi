using UnityEngine;
using Unity.Netcode;
using Unity.Cinemachine;
using UnityEngine.SceneManagement;
using System.Collections;
using System.Collections.Generic;

namespace Player
{
    [RequireComponent(typeof(Rigidbody), typeof(PlayerMovement), typeof(PlayerBounce))]
    [RequireComponent(typeof(PlayerDustTrail), typeof(PlayerInputHandler))]
    public class PlayerUnit : NetworkBehaviour, IPlayerProduct
    {
        [SerializeField] private PlayerConfigSO m_Config; // NGO 경로: 프리팹 Inspector에서 설정

        private PlayerMovement     m_Movement;
        private PlayerBounce       m_Bounce;
        private PlayerDustTrail    m_DustTrail;
        private PlayerInputHandler m_InputHandler;
        private Transform          m_CameraArm;
        private CinemachineCamera  m_CinemachineCamera;
        private Rigidbody          m_Rb;
        private float              m_NextDashSfxTime;
        private Coroutine          m_SpawnRoutine;
        private float              m_NextFallRecoveryTime;
        private bool               m_DbgMoving;   // 진단용(원격 먼지 복제 로그 throttle)

        [Header("비계 (더블탭 Space)")]
        [SerializeField] private GameObject m_ScaffoldPrefab;    // 비계 외형(없으면 큐브). 피벗=min-corner 권장.
        [SerializeField] private Material   m_ScaffoldMaterial;  // 폴백 큐브 색(프리팹 없을 때만)
        // 서버 권위 상태: 이 플레이어의 비계 셀 목록. 모든 클라가 이 리스트로 로컬 비계(콜라이더+외형) 재구성.
        private readonly NetworkList<Vector3Int> m_NetScaffolds = new();
        private readonly List<GameObject> m_Scaffolds = new();   // 로컬 비주얼(모든 클라)
        private Vector2Int m_ScaffoldColumn;   // owner 판단용(기둥 칸)
        private bool m_HasScaffolds;            // owner 판단용

        // 원격 클라에 이동/스프린트 상태 복제 → 먼지·스프린트 트레일 동기화 (owner가 write)
        private readonly NetworkVariable<bool> m_NetMoving = new(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        private readonly NetworkVariable<bool> m_NetSprinting = new(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        // 비주얼 모델의 바라보는 yaw 복제 → 원격에서 방향 전환 동기화 (owner가 write, PlayerFacing이 read)
        private readonly NetworkVariable<float> m_NetFacingYaw = new(
            0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        public float FacingYaw => m_NetFacingYaw.Value;
        public void ReportFacingYaw(float yaw) { if (IsSpawned && IsOwner) m_NetFacingYaw.Value = yaw; }

        public string ProductName { get; set; }

        // ── NGO 경로 ──────────────────────────────────────────────────────
        public override void OnNetworkSpawn()
        {
            gameObject.tag = "Player"; // PlayerBounce 태그 체크용
            InitComponents(m_Config);

            var rb = GetComponent<Rigidbody>();

            // 비계: 모든 클라(owner·원격)가 네트워크 리스트로 로컬 비계를 재구성(늦참 포함).
            m_NetScaffolds.OnListChanged += OnScaffoldsChanged;
            RebuildScaffoldVisuals();

            if (!IsOwner)
            {
                // ClientNetworkTransform이 Transform 직접 이동
                // → Rigidbody를 Kinematic으로 설정해 충돌 감지는 유지하되 물리 간섭 제거
                rb.isKinematic = true;
                if (m_CinemachineCamera != null)
                    m_CinemachineCamera.enabled = false;
                return;
            }
            // owner: dynamic Rigidbody 유지 (InitComponents에서 constraints 설정됨)

            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            QueueSpawnOnGrid();

            // ── Smooth Follow: 카메라가 플레이어를 딜레이와 함께 부드럽게 추적 ──
            if (m_CameraArm != null)
            {
                var follow = m_CameraArm.gameObject.AddComponent<PlayerCameraFollow>();
                follow.Init(transform);

                // ── 시야 가림 반투명: 카메라→플레이어 사이 콜라이더를 α=0.2로 ──
                if (m_CinemachineCamera != null)
                {
                    var fader = m_CinemachineCamera.gameObject.AddComponent<CameraObstructionFader>();
                    fader.Init(m_CameraArm);   // 카메라가 바라보는 지점(허리 높이)
                }
            }
        }

        public override void OnNetworkDespawn()
        {
            if (IsOwner)
                SceneManager.sceneLoaded -= OnSceneLoaded;
            m_NetScaffolds.OnListChanged -= OnScaffoldsChanged;
            ClearScaffoldVisuals();
            base.OnNetworkDespawn();
        }

        public override void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            base.OnDestroy();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!IsOwner) return;
            if (scene.name == SceneNames.GameScene)
                QueueSpawnOnGrid();
        }

        private void FixedUpdate()
        {
            if (!IsOwner) return;
            if (m_Rb != null && m_Rb.isKinematic) return;   // 스폰 위치 대기 중(정지) — 이동 로직 스킵
            if (m_InputHandler == null || m_Movement == null || m_CameraArm == null) return;
            if (m_Bounce.IsBouncing) return; // bounce impulse 유지

            RecoverIfFallingThroughStage();

            if (m_InputHandler.ConsumeScaffold()) PlaceScaffold();   // 더블탭 Space = 발밑 비계 + 올라타기
            UpdateScaffolds();                                        // 기둥에서 벗어나면 비계 제거

            if (m_Movement.IsClimbing || m_Movement.TryStartClimb(m_InputHandler.MoveInput, m_CameraArm))
            {
                m_Rb.useGravity = false;
                if (m_InputHandler.ConsumeJump()) m_Movement.ClimbJumpOff(m_CameraArm);
                else                              m_Movement.Climb(m_InputHandler.MoveInput, m_CameraArm);
            }
            else
            {
                m_Rb.useGravity = true;
                m_Movement.Move(m_InputHandler.MoveInput, m_CameraArm, m_InputHandler.IsSprinting);
                if (m_InputHandler.ConsumeJump()) m_Movement.Jump();   // Space 점프(접지 시)
            }
        }

        private void QueueSpawnOnGrid()
        {
            if (!IsOwner || !isActiveAndEnabled)
                return;

            if (m_SpawnRoutine != null)
                StopCoroutine(m_SpawnRoutine);
            m_SpawnRoutine = StartCoroutine(SpawnOnGridWhenReady());
        }

        private IEnumerator SpawnOnGridWhenReady()
        {
            // NetworkManager의 플레이어 프리팹은 BootstrapScene에서 먼저 생길 수 있다.
            // GameScene 로드 후 GridManager.Awake/CreateGround가 끝난 다음 위치를 다시 잡아준다.
            // 그 전까지 dynamic Rigidbody가 빈 BootstrapScene에서 중력으로 떨어지지 않도록 잠깐 정지시킨다.
            if (m_Rb == null)
                m_Rb = GetComponent<Rigidbody>();

            // 대기 동안만 정지(빈 BootstrapScene에서 추락 방지). owner는 끝나면 반드시 dynamic으로 복귀.
            // (velocity는 kinematic 상태에선 의미 없어 건드리지 않음 — 경고 방지)
            if (m_Rb != null)
                m_Rb.isKinematic = true;

            for (int i = 0; i < 300; i++)
            {
                var gm = FindFirstObjectByType<GridSystem.GridManager>();
                if (gm != null)
                {
                    yield return null;
                    PlaceOnGrid(gm);
                    FinishSpawn();
                    yield break;
                }
                yield return null;
            }

            FinishSpawn();
        }

        // 스폰 마무리: owner Rigidbody를 dynamic으로 복귀 + 속도 0. (대기 동안 kinematic이었음 → 안 풀면 안 움직임)
        private void FinishSpawn()
        {
            if (m_Rb != null)
            {
                m_Rb.isKinematic = false;
                m_Rb.linearVelocity = Vector3.zero;
                m_Rb.angularVelocity = Vector3.zero;
            }
            m_SpawnRoutine = null;
        }

        private void PlaceOnGrid(GridSystem.GridManager gm)
        {
            if (gm == null)
                return;

            GridSystem.GridContract.Origin = gm.transform.position;

            float u = GridSystem.GridContract.Unit;
            Vector3Int size = gm.GridSize;
            Vector3 gridCenter = gm.transform.position + new Vector3(size.x * 0.5f, 0f, size.z * 0.5f) * u;
            Vector3 spawn = gridCenter + Vector3.up * 2f;

            Vector3 rayOrigin = gridCenter + Vector3.up * 20f;
            var hits = Physics.RaycastAll(rayOrigin, Vector3.down, 80f, ~0, QueryTriggerInteraction.Ignore);
            float bestY = float.NegativeInfinity;
            foreach (var hit in hits)
            {
                if (hit.collider.transform == transform || hit.collider.transform.IsChildOf(transform))
                    continue;
                if (hit.point.y > bestY)
                    bestY = hit.point.y;
            }

            if (!float.IsNegativeInfinity(bestY))
                spawn.y = bestY - GetColliderLocalBottomY() + 0.05f;

            if (m_Rb == null)
                m_Rb = GetComponent<Rigidbody>();

            if (m_Rb != null)
                m_Rb.position = spawn;   // 대기 중엔 kinematic → position으로 이동(velocity는 FinishSpawn에서 0)
            transform.position = spawn;
            Physics.SyncTransforms();
        }

        private float GetColliderLocalBottomY()
        {
            if (TryGetComponent<CapsuleCollider>(out var capsule))
                return capsule.center.y - capsule.height * 0.5f;
            if (TryGetComponent<BoxCollider>(out var box))
                return box.center.y - box.size.y * 0.5f;
            if (TryGetComponent<SphereCollider>(out var sphere))
                return sphere.center.y - sphere.radius;
            return 0f;
        }

        private void RecoverIfFallingThroughStage()
        {
            if (Time.time < m_NextFallRecoveryTime)
                return;

            float killY = GridSystem.GridContract.Origin.y - 12f;
            if (transform.position.y > killY)
                return;

            m_NextFallRecoveryTime = Time.time + 0.5f;
            var gm = FindFirstObjectByType<GridSystem.GridManager>();
            if (gm != null)
                PlaceOnGrid(gm);
        }

        // ── 비계 (더블탭 Space): 발밑 1×1 비계 + 그 위로 올라타기. 기둥에서 벗어나면 전부 사라짐 ──
        // 네트워크: owner가 ServerRpc로 셀 추가/제거 → 서버 NetworkList → 모든 클라가 로컬 비계 재구성(전원 보고 딛음).
        private void PlaceScaffold()
        {
            if (m_Rb == null || !IsSpawned) return;
            float u = GridSystem.GridContract.Unit;
            Vector3 origin = GridSystem.GridContract.Origin;

            float feetY = transform.position.y + GetColliderLocalBottomY() + 0.05f;
            Vector3Int cell = GridSystem.GridCoordinates.WorldToCell(
                new Vector3(transform.position.x, feetY, transform.position.z));

            if (m_HasScaffolds && (cell.x != m_ScaffoldColumn.x || cell.z != m_ScaffoldColumn.y))
                ClearScaffoldsServerRpc();   // 다른 칸이면 새 기둥

            AddScaffoldServerRpc(cell);
            m_ScaffoldColumn = new Vector2Int(cell.x, cell.z);
            m_HasScaffolds = true;

            // 올라타기(칸 중심 정렬 + 수직속도 0). 위치는 owner 권위. 더블탭 반복 = 한 칸씩 상승.
            float topY = origin.y + (cell.y + 1) * u;
            Vector3 pos = new Vector3(origin.x + (cell.x + 0.5f) * u,
                                      topY - GetColliderLocalBottomY() + 0.02f,
                                      origin.z + (cell.z + 0.5f) * u);
            transform.position = pos;
            m_Rb.position = pos;
            var v = m_Rb.linearVelocity; v.y = 0f; m_Rb.linearVelocity = v;
        }

        // owner: 기둥에서 수평으로 벗어나면(걸어 나가거나 뛰어내리면) 비계 전부 제거 요청.
        private void UpdateScaffolds()
        {
            if (!m_HasScaffolds || !IsSpawned) return;
            float feetY = transform.position.y + GetColliderLocalBottomY() + 0.05f;
            Vector3Int cell = GridSystem.GridCoordinates.WorldToCell(
                new Vector3(transform.position.x, feetY, transform.position.z));
            if (cell.x != m_ScaffoldColumn.x || cell.z != m_ScaffoldColumn.y)
            {
                ClearScaffoldsServerRpc();
                m_HasScaffolds = false;
            }
        }

        [Rpc(SendTo.Server)]
        private void AddScaffoldServerRpc(Vector3Int cell)
        {
            for (int i = 0; i < m_NetScaffolds.Count; i++)
                if (m_NetScaffolds[i] == cell) return;   // 같은 칸 중복 방지
            m_NetScaffolds.Add(cell);
        }

        [Rpc(SendTo.Server)]
        private void ClearScaffoldsServerRpc() => m_NetScaffolds.Clear();

        // 모든 클라: 네트워크 리스트 변경 시 로컬 비계(콜라이더+외형) 재구성.
        private void OnScaffoldsChanged(NetworkListEvent<Vector3Int> _) => RebuildScaffoldVisuals();

        private void RebuildScaffoldVisuals()
        {
            ClearScaffoldVisuals();
            float u = GridSystem.GridContract.Unit;
            Vector3 origin = GridSystem.GridContract.Origin;
            for (int i = 0; i < m_NetScaffolds.Count; i++)
                m_Scaffolds.Add(CreateScaffold(origin + (Vector3)m_NetScaffolds[i] * u, u));
        }

        private void ClearScaffoldVisuals()
        {
            for (int i = 0; i < m_Scaffolds.Count; i++)
                if (m_Scaffolds[i] != null) Destroy(m_Scaffolds[i]);
            m_Scaffolds.Clear();
        }

        // 비계 1개: 칸 크기 BoxCollider(딛고 섬) + 외형(프리팹 또는 큐브).
        private GameObject CreateScaffold(Vector3 cellMin, float u)
        {
            var go = new GameObject("~Scaffold");
            go.transform.position = cellMin + Vector3.one * (0.5f * u);   // 칸 중심
            go.AddComponent<BoxCollider>().size = Vector3.one * u;

            if (m_ScaffoldPrefab != null)
            {
                var vis = Instantiate(m_ScaffoldPrefab, go.transform);
                vis.transform.localPosition = -Vector3.one * (0.5f * u);   // 프리팹 피벗=min-corner → 칸에 맞춤
                vis.transform.localRotation = Quaternion.identity;
                foreach (var c in vis.GetComponentsInChildren<Collider>()) Destroy(c);   // 콜라이더는 루트 1개만
            }
            else
            {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(cube.GetComponent<Collider>());
                cube.transform.SetParent(go.transform, false);
                cube.transform.localScale = Vector3.one * u;
                if (m_ScaffoldMaterial != null)
                    cube.GetComponent<Renderer>().sharedMaterial = m_ScaffoldMaterial;
            }
            return go;
        }

        // ── 이동 FX 동기화 ────────────────────────────────────────────────
        // owner: Rigidbody 속도로 상태 산출 → 로컬 적용 + NetworkVariable로 복제.
        // 원격: 복제된 상태로 적용 (transform 추정은 네트워크 틱 단위라 스파이크/끊김 → 의도값 사용).
        private void Update()
        {
            if (m_DustTrail == null || m_Config == null || m_Rb == null) return;

            bool moving, sprinting;
            if (IsSpawned && !IsOwner)
            {
                moving    = m_NetMoving.Value;
                sprinting = m_NetSprinting.Value;
            }
            else
            {
                Vector3 horiz = m_Rb.linearVelocity; horiz.y = 0f;   // 점프/낙하 Y 제외 → 점프 중 대시 오판 방지
                float speed = horiz.magnitude;
                moving    = speed > 0.2f;
                sprinting = speed > m_Config.MoveSpeed + 0.5f;
                if (m_Movement.IsClimbing) { moving = false; sprinting = false; }   // 기어오르기 중엔 이동 FX 끔
                if (IsSpawned) // owner → 원격에 복제
                {
                    m_NetMoving.Value    = moving;
                    m_NetSprinting.Value = sprinting;
                }
            }
            m_DustTrail.Apply(moving, sprinting);

            if (IsSpawned && !IsOwner && moving != m_DbgMoving)   // 진단: 원격에서 먼지 상태 복제 + 파티클 상태 확인
            {
                m_DbgMoving = moving;
                Debug.Log($"[FXSync] remote dust moving={moving} sprint={sprinting} | {m_DustTrail.DebugState()}", this);
            }

            if ((IsOwner || !IsSpawned) && moving && sprinting && Time.time >= m_NextDashSfxTime)
            {
                m_NextDashSfxTime = Time.time + 0.45f;
                if (SoundManager.Instance != null)
                    SoundManager.Instance.PlaySFX(SFXType.Dash);
            }
        }

        // ── 충돌 FX 멀티캐스트 ─────────────────────────────────────────────
        // owner가 충돌을 감지하면 서버 경유로 나머지 클라이언트에 동일한 피드백을 복제한다.
        // (owner 자신은 로컬에서 이미 재생했으므로 SendTo.NotOwner로 제외)
        private void ReplicateBounce(Vector3 point, bool spawnParticle)
        {
            if (!IsSpawned) return; // 테스트(비네트워크) 경로: 로컬 재생만
            RequestBounceFXRpc(point, spawnParticle);
        }

        [Rpc(SendTo.Server)]
        private void RequestBounceFXRpc(Vector3 point, bool spawnParticle)
            => PlayBounceFXRpc(point, spawnParticle);

        [Rpc(SendTo.NotOwner)]
        private void PlayBounceFXRpc(Vector3 point, bool spawnParticle)
            => m_Bounce.PlayBounceFeedback(point, spawnParticle);

        // ── Factory 테스트 경로 ───────────────────────────────────────────
        public void Initialize(PlayerConfigSO config)
        {
            ProductName     = "Player_" + GetInstanceID();
            gameObject.name = ProductName;
            gameObject.tag  = "Player";
            InitComponents(config);
        }

        // ── 공통 초기화 ───────────────────────────────────────────────────
        private void InitComponents(PlayerConfigSO config)
        {
            if (config == null) return;
            m_Config = config; // 런타임 활성 config 통일 (NGO=serialized, 테스트=주입)

            m_Rb = GetComponent<Rigidbody>();
            m_Rb.constraints = RigidbodyConstraints.FreezeRotationX   // Y 고정 해제 → 중력/점프
                             | RigidbodyConstraints.FreezeRotationY
                             | RigidbodyConstraints.FreezeRotationZ;
            m_Rb.interpolation = RigidbodyInterpolation.Interpolate; // 물리→렌더 프레임 보간

            m_Movement = GetComponent<PlayerMovement>();
            m_Movement.Init(config);

            m_Bounce = GetComponent<PlayerBounce>();
            m_Bounce.Init(config);
            m_Bounce.OnBounceReplicate = ReplicateBounce; // 충돌 FX 멀티캐스트

            m_DustTrail = GetComponent<PlayerDustTrail>();
            m_DustTrail.Init(config);

            m_InputHandler      = GetComponent<PlayerInputHandler>();
            m_CameraArm         = transform.Find("CameraArm");
            m_CinemachineCamera = GetComponentInChildren<CinemachineCamera>(includeInactive: true);

            if (GetComponent<PlayerAnimator>() == null) gameObject.AddComponent<PlayerAnimator>();   // 애니 파라미터 구동(널 가드)
            if (GetComponent<PlayerFacing>() == null) gameObject.AddComponent<PlayerFacing>();       // 비주얼이 이동 방향 보게
        }
    }
}
