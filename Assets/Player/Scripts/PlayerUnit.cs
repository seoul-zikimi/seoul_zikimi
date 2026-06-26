using UnityEngine;
using Unity.Netcode;
using Unity.Cinemachine;
using UnityEngine.SceneManagement;
using System.Collections;

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

        // 원격 클라에 이동/스프린트 상태 복제 → 먼지·스프린트 트레일 동기화 (owner가 write)
        private readonly NetworkVariable<bool> m_NetMoving = new(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        private readonly NetworkVariable<bool> m_NetSprinting = new(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        public string ProductName { get; set; }

        // ── NGO 경로 ──────────────────────────────────────────────────────
        public override void OnNetworkSpawn()
        {
            gameObject.tag = "Player"; // PlayerBounce 태그 체크용
            InitComponents(m_Config);

            var rb = GetComponent<Rigidbody>();

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
            }
        }

        public override void OnNetworkDespawn()
        {
            if (IsOwner)
                SceneManager.sceneLoaded -= OnSceneLoaded;
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
            if (m_InputHandler == null || m_Movement == null || m_CameraArm == null) return;
            if (m_Bounce.IsBouncing) return; // bounce impulse 유지

            RecoverIfFallingThroughStage();

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

            bool wasKinematic = m_Rb != null && m_Rb.isKinematic;
            if (m_Rb != null)
            {
                m_Rb.linearVelocity = Vector3.zero;
                m_Rb.angularVelocity = Vector3.zero;
                m_Rb.isKinematic = true;
            }

            for (int i = 0; i < 300; i++)
            {
                var gm = FindFirstObjectByType<GridSystem.GridManager>();
                if (gm != null)
                {
                    yield return null;
                    PlaceOnGrid(gm);
                    if (m_Rb != null)
                    {
                        m_Rb.isKinematic = wasKinematic;
                        m_Rb.linearVelocity = Vector3.zero;
                        m_Rb.angularVelocity = Vector3.zero;
                    }
                    m_SpawnRoutine = null;
                    yield break;
                }
                yield return null;
            }

            if (m_Rb != null)
            {
                m_Rb.isKinematic = wasKinematic;
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
            {
                m_Rb.linearVelocity = Vector3.zero;
                m_Rb.angularVelocity = Vector3.zero;
                m_Rb.position = spawn;
            }
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
