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

        /// <summary>튜토리얼 전용 훅(로컬 오너 전용) — 비계를 밟고 올라선 층수(1층부터).</summary>
        public static event System.Action<int> LocalScaffoldFloorReached;

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

            CreateSlimeTrail();   // 민달팽이 점액 트레일(트레이드마크). 더스트트레일(발먼지)과 별개 공존.
            CreateNametag();      // 캐릭터 위 닉네임(월드 텍스트, 모든 클라)

            if (GetComponent<PlayerSplat>() == null)   // 착지 철푸덕(래퍼 스케일 — 리깅과 무관하게 적용)
                gameObject.AddComponent<PlayerSplat>();

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

                    // FOV 펀치를 vcam 렌즈로 라우팅 — CinemachineBrain이 Camera.main fov를 덮어써서
                    // 기존 CameraFovPunch(메인캠 직접 수정)는 화면에 안 나왔음.
                    var punch = m_CinemachineCamera.gameObject.AddComponent<CinemachineFovPunch>();
                    GridSystem.GridJuice.FovPunchHandler = punch.Add;
                }
            }
        }

        public override void OnNetworkDespawn()
        {
            if (IsOwner)
            {
                SceneManager.sceneLoaded -= OnSceneLoaded;
                GridSystem.GridJuice.FovPunchHandler = null;   // vcam 펀치 핸들러 해제(파괴된 컴포넌트 참조 방지)
            }
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

        private int m_KinematicFrames;   // 프리즈 복구용: kinematic 지속 프레임 카운트

        private void FixedUpdate()
        {
            if (!IsOwner) return;
            if (m_Rb != null && m_Rb.isKinematic)
            {
                // 스폰 대기는 GridManager 찾으면 곧 끝남(보통 <1s). 코루틴이 씬전환/비활성으로 죽어
                // kinematic이 안 풀리면 영영 프리즈 → 오래 지속되면 강제로 dynamic 복구.
                if (++m_KinematicFrames > 150) { m_Rb.isKinematic = false; m_KinematicFrames = 0; }
                return;   // 대기 중(정지) — 이동 로직 스킵
            }
            m_KinematicFrames = 0;
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

            LocalScaffoldFloorReached?.Invoke(cell.y + 1);   // 1층부터 세는 도달 층수(튜토리얼 Quest10)
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

        // 민달팽이 점액: 발밑에 반투명 초록 자국이 스르르 남았다 사라짐(모든 클라, 원격 포함).
        private void CreateSlimeTrail()
        {
            if (transform.Find("~SlimeTrail") != null) return;
            var go = new GameObject("~SlimeTrail");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 0.05f, 0f);

            var tr = go.AddComponent<TrailRenderer>();
            tr.time = 1.6f;
            tr.startWidth = 0.45f;
            tr.endWidth = 0.06f;
            tr.minVertexDistance = 0.12f;
            tr.alignment = LineAlignment.View;   // 빌보드 → 바닥이든 벽이든 항상 카메라 향해 잘 보임
            tr.numCapVertices = 4;
            tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            tr.receiveShadows = false;

            var sh = Shader.Find("Universal Render Pipeline/Lit");   // 빌드 셰이더 스트립 안전 계열
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
                var c = new Color(0.97f, 0.98f, 0.95f, 0.30f);   // 하얀 점액
                m.SetColor("_BaseColor", c);
                m.SetColor("_Color", c);
                tr.material = m;
            }

            var grad = new Gradient();   // 갈수록 옅어지는 점액
            grad.SetKeys(
                new[] { new GradientColorKey(new Color(0.97f, 0.98f, 0.95f), 0f), new GradientColorKey(new Color(0.97f, 0.98f, 0.95f), 1f) },
                new[] { new GradientAlphaKey(0.35f, 0f), new GradientAlphaKey(0f, 1f) });
            tr.colorGradient = grad;

            m_SlimeTrail = tr;
        }

        private TrailRenderer m_SlimeTrail;
        private PlayerMovement m_MoveForTrail;
        private Vector3 m_PrevTrailPos;

        // ── 캐릭터 위 닉네임(월드 텍스트) ──
        private TextMesh m_Nametag;
        private GridSystem.GameLoopManager m_LoopForName;

        private void CreateNametag()
        {
            if (transform.Find("~Nametag") != null) return;
            var go = new GameObject("~Nametag");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 1.5f, 0f);   // 머리 바로 위

            var tm = go.AddComponent<TextMesh>();
            tm.text = "";
            tm.fontSize = 60;
            tm.characterSize = 0.05f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = Color.white;
            var font = Resources.Load<Font>("Fonts/서울한강 장체M");   // UI와 같은 폰트
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            tm.font = font;
            go.GetComponent<MeshRenderer>().material = font.material;
            go.SetActive(false);
            m_Nametag = tm;
        }

        private void UpdateNametag()
        {
            if (m_Nametag == null) return;
            if (m_LoopForName == null) m_LoopForName = FindFirstObjectByType<GridSystem.GameLoopManager>();
            string nm = m_LoopForName != null ? m_LoopForName.GetNameFor(OwnerClientId) : "";
            bool show = !string.IsNullOrEmpty(nm);
            m_Nametag.gameObject.SetActive(show);
            if (!show) return;
            if (m_Nametag.text != nm) m_Nametag.text = nm;
            if (Camera.main != null)   // 항상 카메라 향함(빌보드)
                m_Nametag.transform.rotation = Camera.main.transform.rotation;
        }

        private void LateUpdate()
        {
            UpdateNametag();

            if (m_SlimeTrail == null) return;
            if (m_MoveForTrail == null) m_MoveForTrail = GetComponent<PlayerMovement>();
            // 바닥 위 또는 벽타기 중엔 점액이 나옴(민달팽이니 벽에도 자국 남김). 점프/낙하 공중에선 끊김.
            bool climbing = m_MoveForTrail != null && m_MoveForTrail.IsClimbing;
            m_SlimeTrail.emitting = m_MoveForTrail == null || m_MoveForTrail.IsGrounded() || climbing;

            float dt = Time.deltaTime;   // 빨리 갈수록 점액이 굵어짐(스프린트 티)
            if (dt > 0f)
            {
                Vector3 d = transform.position - m_PrevTrailPos;
                if (!climbing) d.y = 0f;   // 벽타기 땐 수직 이동도 속도로 인정(자국 굵기 유지)
                float target = Mathf.Lerp(0.32f, 0.62f, Mathf.Clamp01(d.magnitude / dt / 6f));
                m_SlimeTrail.startWidth = Mathf.Lerp(m_SlimeTrail.startWidth, target, 8f * dt);
            }
            m_PrevTrailPos = transform.position;
        }

        // 스프린트 윈드 트레일(루프 파티클 — 스프린트 중에만 켬)
        private GameObject m_WindTrail;
        private void UpdateWindTrail(bool on)
        {
            if (on && m_WindTrail == null)
            {
                var prefab = Resources.Load<GameObject>("Fx/WindTrails");
                if (prefab == null) return;
                m_WindTrail = Instantiate(prefab, transform);
                m_WindTrail.transform.localPosition = new Vector3(0f, 0.8f, 0f);
            }
            if (m_WindTrail != null && m_WindTrail.activeSelf != on)
                m_WindTrail.SetActive(on);
        }

        private int m_PrevScaffoldCount;   // 새로 추가된 비계만 팝(재구성 시 전체 재생 방지)

        private void RebuildScaffoldVisuals()
        {
            ClearScaffoldVisuals();
            float u = GridSystem.GridContract.Unit;
            Vector3 origin = GridSystem.GridContract.Origin;
            for (int i = 0; i < m_NetScaffolds.Count; i++)
                m_Scaffolds.Add(CreateScaffold(origin + (Vector3)m_NetScaffolds[i] * u, u));

            if (m_NetScaffolds.Count > m_PrevScaffoldCount && m_Scaffolds.Count > 0)
            {
                var last = m_Scaffolds[m_Scaffolds.Count - 1];
                GridSystem.GridJuice.Squish(last, 0.15f);   // 새 비계 뿅
                GridSystem.GridJuice.GroundHit(             // 설치 흙 팡
                    last.transform.position - Vector3.up * (0.5f * GridSystem.GridContract.Unit), 0.5f);
                if (SoundManager.Instance != null)          // 가벼운 설치음(높은 피치 짧게)
                    SoundManager.Instance.PlayTapAt(SFXType.LandObject, last.transform.position, 1.3f, 0.3f);
            }
            m_PrevScaffoldCount = m_NetScaffolds.Count;
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
            UpdateWindTrail(moving && sprinting);   // 스프린트 = 바람 줄기(CFXR4 Wind Trails)

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
