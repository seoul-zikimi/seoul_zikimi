using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GridSystem
{
    /// <summary>
    /// 남산 엘리베이터(기획서 §2) — 철제 전망대 구간(정답 셀 y ∈ [ObservatoryMinY, ObservatoryMaxY])이
    /// 전부 배치+공정 완료되면 개통. 개통 후 두 문(데크 밑 건물 ↔ 전망대층) 앞에서 E를 누르면
    /// 반대편으로 순간이동한다(연출은 빠르게 — 카메라 팔로우가 자연스럽게 휙 따라온다).
    /// 판정은 서버(0.5초 폴링), 개통 상태만 복제. 문 비주얼·탑승 입력은 전부 로컬.
    ///
    /// 2vs2: 팀(레인)별로 독립 판정·독립 승강로를 둔다. 팀B의 정답은 점대칭이 아니라
    /// '구역폭만큼 X로 민' 자리에 지어지므로(GridNetwork.RecomputeScore·AnswerPreview와 동일 기준)
    /// 문·판정 셀도 같은 X 오프셋으로 민다. 케이블카/작업대의 점대칭 미러와는 기준이 다르다.
    ///
    /// 배경에 Spot_ElevatorLower/Upper 마커가 없는 맵(2vs2 공터 경기장)에서는 정답의 전망대 밴드에서
    /// 승강로 자리를 계산해 폴백으로 세운다 — 마커가 있으면 언제나 마커가 우선.
    /// </summary>
    public class ElevatorNetwork : NamsanGimmickBase
    {
        private const int kMaxLanes = 2;

        // 레인(팀)별 개통 비트마스크 — 비트0=팀A, 비트1=팀B(협동은 비트0만 쓴다).
        private readonly NetworkVariable<byte> m_OpenMask =
            new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private IGridState m_Net;   // 읽기 계약만 필요(TryGetCell·CellsChanged) — GridInterfaces.cs 채택 규약
        private float m_NextCheck;
        private bool m_CellsDirty = true;   // 그리드 셀이 변한 뒤에만 판정(스폰 직후 1회는 무조건 — 늦참·치트 완성 대비)
        private bool m_WarnedNoBand;   // 판정 영역에 정답 셀이 하나도 없으면 1회 경고
        private float m_RideCooldown;  // 연타로 왕복 떨림 방지
        private int m_Lanes = 1;       // 협동 1, 2vs2 2

        private const float kUseRange = 2.2f;   // 문 앞 E 사용 거리
        private const float kDoorGap = 0.25f;   // (폴백) 전망대 정면에서 상부 문까지 띄우는 거리
        private const float kLowerDoorClear = 1.25f;   // (폴백) 정답 구조물 앞에서 하부 문까지 띄우는 거리
        private const float kPlatformDepth = 1.6f;   // (폴백) 상부 발판 깊이 — 문이 그 위에 얹힌다

        /// <summary>로컬 플레이어 팀의 개통 여부(HUD·연출용).</summary>
        public bool IsOpen => IsOpenFor(LocalLane);

        /// <summary>해당 레인(팀)의 개통 여부.</summary>
        public bool IsOpenFor(int lane) => lane >= 0 && lane < kMaxLanes && (m_OpenMask.Value & (1 << lane)) != 0;

        protected override void Awake()
        {
            base.Awake();
            m_Net = GetComponent<GridNetwork>();
        }

        protected override void OnGimmickSpawn()
        {
            m_Lanes = (Loop != null && Loop.IsVersus) ? 2 : 1;
            m_OpenMask.OnValueChanged += OnOpenChanged;
            if (m_Net != null) m_Net.CellsChanged += OnGridCellsChanged;   // 0.5초 폴링의 더티 게이트
        }

        public override void OnNetworkDespawn()
        {
            m_OpenMask.OnValueChanged -= OnOpenChanged;
            if (m_Net != null) m_Net.CellsChanged -= OnGridCellsChanged;
            DestroyVisuals();
            base.OnNetworkDespawn();
        }

        private void OnGridCellsChanged() => m_CellsDirty = true;

        /// <summary>재시작용(서버): 다음 라운드는 다시 잠긴 상태부터.</summary>
        public void ServerReset()
        {
            if (!IsServer || !Active) return;
            m_OpenMask.Value = 0;
        }

        /// <summary>로컬 플레이어의 레인(팀). 팀 배정 복제 전이거나 협동이면 0.</summary>
        private int LocalLane
        {
            get
            {
                if (Loop == null || !Loop.IsVersus) return 0;
                int t = Loop.LocalTeam;
                return (t >= 0 && t < m_Lanes) ? t : 0;
            }
        }

        private void Update()
        {
            if (!Active || !IsSpawned) return;

            if (IsServer && m_CellsDirty && Time.time >= m_NextCheck)
            {
                m_NextCheck = Time.time + 0.5f;
                m_CellsDirty = false;   // 다음 셀 변경까지 전체 스캔 판정 쉼
                byte mask = m_OpenMask.Value;
                for (int lane = 0; lane < m_Lanes; lane++)
                {
                    if ((mask & (1 << lane)) != 0) continue;
                    if (CheckObservatoryComplete(lane)) mask |= (byte)(1 << lane);
                }
                if (mask != m_OpenMask.Value) m_OpenMask.Value = mask;
            }

            UpdateVisuals();
            UpdateLocalRide();
        }

        // ── 개통 판정(서버) ───────────────────────────────────────────────────
        private bool CheckObservatoryComplete(int lane)
        {
            var answer = Grid != null ? Grid.Answer : null;
            var cat = Grid != null ? Grid.Catalog : null;
            if (answer == null || cat == null || m_Net == null) return false;

            // 팀B의 실제 블록은 자기 구역(x + 구역폭)에 있다 — 채점(GridNetwork.RecomputeScore)과 같은 오프셋.
            var off = new Vector3Int(Grid.ZoneSize.x * lane, 0, 0);

            int band = 0;
            foreach (var a in answer.Cells)
            {
                if (a.cell.y < Config.ObservatoryMinY || a.cell.y > Config.ObservatoryMaxY) continue;
                if (answer.IsPreset(a.cell)) continue;   // 기본 제공 블록은 판정 제외(채점과 동일 규칙)
                band++;

                if (!m_Net.TryGetCell(a.cell + off, out int placedId, out int mask)) return false;
                if (placedId != a.materialId) return false;
                var def = cat.GetById(a.materialId);
                int need = def != null ? def.RequiredMask : 0;
                if ((mask & need) != need) return false;   // 공정(고정·페인트)까지 끝나야 완성
            }

            if (band == 0)
            {
                if (!m_WarnedNoBand)
                {
                    m_WarnedNoBand = true;
                    Debug.LogWarning($"[Namsan] 엘리베이터 판정 영역(y {Config.ObservatoryMinY}~{Config.ObservatoryMaxY})에 정답 셀이 없음 — 개통 불가. NamsanGimmickConfig를 확인하세요.");
                }
                return false;
            }
            return true;
        }

        private void OnOpenChanged(byte prev, byte now)
        {
            for (int lane = 0; lane < m_Lanes; lane++)
            {
                if ((now & (1 << lane)) == 0 || (prev & (1 << lane)) != 0) continue;   // 이번에 새로 열린 레인만
                if (!TryGetDoors(lane, out var d)) continue;

                // 개통 연출(전 클라 로컬) — 두 문 동시에.
                GridJuice.WorldToast(d.lowerPos + Vector3.up * 2.2f, "엘리베이터 개통!", new Color(0.4f, 1f, 0.55f));
                GridJuice.PlacePuff(d.lowerPos, 1f);
                GridJuice.WorldToast(d.upperPos + Vector3.up * 2.2f, "엘리베이터 개통!", new Color(0.4f, 1f, 0.55f));
                GridJuice.PlacePuff(d.upperPos, 1f);
                if (lane < m_DoorAnim.Length) m_DoorAnim[lane] = 1.2f;   // 개통 순간에도 한 번 열렸다 닫힘
            }
        }

        // ── 문 위치(마커 우선, 없으면 정답 전망대 밴드에서 계산) ─────────────────
        private struct DoorPair
        {
            public Vector3 lowerPos, upperPos;
            public Quaternion lowerRot, upperRot;
        }

        // 팀B는 정답과 같은 X 오프셋만큼 밀린 자리에 승강로가 선다(점대칭 아님 — 위 클래스 주석 참고).
        private Vector3 LaneOffset(int lane) =>
            new Vector3((Grid != null ? Grid.ZoneSize.x : 0) * GridContract.Unit * lane, 0f, 0f);

        private bool TryGetDoors(int lane, out DoorPair door)
        {
            door = default;
            var off = LaneOffset(lane);

            var lower = FindSpot(NamsanSpots.ElevatorLower);
            var upper = FindSpot(NamsanSpots.ElevatorUpper);
            if (lower != null || upper != null) m_SawMarker = true;
            if (lower != null && upper != null)
            {
                door.lowerPos = lower.position + off;
                door.upperPos = upper.position + off;
                door.lowerRot = lower.rotation;
                door.upperRot = upper.rotation;
                return true;
            }

            // FindSpot의 재탐색 주기(0.5초)는 마커 이름끼리 공유라 한쪽이 먼저 잡히는 프레임이 있다.
            // 그 틈에 폴백으로 세웠다가 마커 자리로 튀는 걸 막는다 — 마커 맵이면 잠깐 안 보이는 게 낫다.
            if (m_SawMarker) return false;
            if (!EnsureFallbackDoors()) return false;
            door.lowerPos = m_FallbackLower + off;
            door.upperPos = m_FallbackUpper + off;
            door.lowerRot = door.upperRot = s_FallbackRot;
            return true;
        }

        // 마커 없는 맵(2vs2 공터 경기장)용 폴백 —
        // 상부 문은 전망대 판정 밴드의 '남쪽 정면 가운데'. 남산타워 배경의 손배치 마커
        // (Spot_ElevatorUpper = 밴드 정면 중앙에서 0.25 앞)와 같은 자리가 나오도록 맞춘 식이다.
        // 하부 문은 같은 X에 지면(y=0)이되, 정답 구조물 '전체' 앞쪽으로 빼서 세운다 —
        // 상부 문 바로 아래에 두면 타워 밑동 블록 안에 문이 파묻혀 탑승 즉시 끼인다.
        private static readonly Quaternion s_FallbackRot = Quaternion.Euler(0f, 180f, 0f);   // 전망대 반대편(남쪽)을 본다
        private bool m_SawMarker;   // 배경이 마커를 갖고 있는 맵인가(한 번이라도 잡혔으면 폴백 금지)
        private bool m_FallbackReady;
        private Vector3 m_FallbackLower, m_FallbackUpper;
        private Vector3 m_FallbackOrigin;          // 계산에 쓴 그리드 원점 — 바뀌면 다시 계산
        private MapAnswerData m_FallbackAnswer;    // 계산에 쓴 정답 — 라운드마다 바뀔 수 있다

        private bool EnsureFallbackDoors()
        {
            var answer = Grid != null ? Grid.Answer : null;
            if (answer == null || Config == null) return false;
            if (MapLoader.Pending) return false;   // 마커 적용 전이면 그리드 원점이 아직 확정 전
            if (m_FallbackReady && m_FallbackOrigin == GridContract.Origin && ReferenceEquals(m_FallbackAnswer, answer))
                return true;

            int minX = int.MaxValue, maxX = int.MinValue, minZ = int.MaxValue;
            int allMinZ = int.MaxValue;
            foreach (var a in answer.Cells)
            {
                if (a.cell.z < allMinZ) allMinZ = a.cell.z;   // 구조물 전체의 남쪽 끝(하부 문 기준)
                if (a.cell.y < Config.ObservatoryMinY || a.cell.y > Config.ObservatoryMaxY) continue;
                if (a.cell.x < minX) minX = a.cell.x;
                if (a.cell.x > maxX) maxX = a.cell.x;
                if (a.cell.z < minZ) minZ = a.cell.z;
            }
            if (minX > maxX) return false;   // 밴드에 정답 셀이 없음 — 개통 자체가 불가(경고는 판정 쪽에서)

            float u = GridContract.Unit;
            float centerX = (minX + maxX + 1) * 0.5f * u;   // 셀은 min-corner 기준이라 +1 해야 가운데
            m_FallbackUpper = GridContract.Origin + new Vector3(centerX, Config.ObservatoryMinY * u, minZ * u - kDoorGap);
            m_FallbackLower = GridContract.Origin + new Vector3(centerX, 0f, allMinZ * u - kLowerDoorClear);
            m_FallbackOrigin = GridContract.Origin;
            m_FallbackAnswer = answer;
            m_FallbackReady = true;
            Debug.Log($"[Namsan] 엘리베이터 마커 없음 — 정답 전망대 밴드로 승강로 폴백 배치: 하부 {m_FallbackLower}, 상부 {m_FallbackUpper}");
            return true;
        }

        // ── 탑승(로컬 플레이어) ───────────────────────────────────────────────
        private void UpdateLocalRide()
        {
            if (m_RideCooldown > 0f) { m_RideCooldown -= Time.deltaTime; return; }

            int lane = LocalLane;
            if (!IsOpenFor(lane)) return;

            var nm = NetworkManager.Singleton;
            var po = nm != null && nm.LocalClient != null ? nm.LocalClient.PlayerObject : null;
            if (po == null) return;
            if (!TryGetDoors(lane, out var d)) return;

            var pos = po.transform.position;
            Vector3 fromPos, toPos;
            Quaternion toRot;
            if (Near(pos, d.lowerPos)) { fromPos = d.lowerPos; toPos = d.upperPos; toRot = d.upperRot; }
            else if (Near(pos, d.upperPos)) { fromPos = d.upperPos; toPos = d.lowerPos; toRot = d.lowerRot; }
            else return;

            var kb = Keyboard.current;
            if (GameplayInputBlocker.Blocked || kb == null || !kb.eKey.wasPressedThisFrame) return;

            // 순간이동(오너 권위 — ClientNetworkTransform이 알아서 복제). PlaceScaffold와 같은 방식.
            // 문 자리를 그대로 박으면 하부 문이 로비 건물 지오메트리 안일 때 몸이 끼어 못 움직인다 —
            // 캡슐이 실제로 들어갈 빈 자리를 찾아 내려준다.
            var dest = FindFreeExit(toPos + Vector3.up * 0.1f, toRot, po.transform);
            po.transform.position = dest;
            var rb = po.GetComponent<Rigidbody>();
            if (rb != null && !rb.isKinematic)
            {
                rb.position = dest;
                rb.linearVelocity = Vector3.zero;
            }
            Physics.SyncTransforms();
            GridJuice.PlacePuff(fromPos, 0.6f);
            GridJuice.PlacePuff(dest, 0.6f);
            if (lane < m_DoorAnim.Length) m_DoorAnim[lane] = 1.2f;   // 문 열렸다 닫히는 연출
            m_RideCooldown = 0.4f;
        }

        private static bool Near(Vector3 p, Vector3 door)
        {
            var d = p - door;
            d.y = 0f;
            return d.sqrMagnitude <= kUseRange * kUseRange && Mathf.Abs(p.y - door.y) <= 2.5f;
        }

        // ── 하차 지점 보정(끼임 방지) ────────────────────────────────────────
        // 문 앞이 막혀 있으면 문 정면 → 옆 → 뒤 순으로 한 걸음씩 넓혀 가며, 각 단계에서 조금씩 위로도 훑는다.
        // 전부 막혀 있으면 원래 지점을 그대로 쓴다(최소한 지금까지의 동작은 보장).
        private static readonly float[] kExitRises = { 0f, 0.6f, 1.4f, 2.4f };
        private static readonly float[] kExitSteps = { 0f, 1.0f, 1.8f, 2.6f };
        private static readonly Collider[] s_Overlap = new Collider[16];

        private static Vector3 FindFreeExit(Vector3 dest, Quaternion doorRot, Transform self)
        {
            GetCapsule(self, out float radius, out float height);
            var fwd = doorRot * Vector3.forward;   // 문이 바라보는 쪽(= 사람이 서는 쪽)부터 시도
            var right = doorRot * Vector3.right;
            Vector3[] dirs = { fwd, right, -right, -fwd };

            foreach (float rise in kExitRises)
                foreach (float step in kExitSteps)
                {
                    if (step <= 0f)
                    {
                        var p = dest + Vector3.up * rise;
                        if (IsFree(p, self, radius, height)) return p;
                        continue;
                    }
                    foreach (var dir in dirs)
                    {
                        var p = dest + dir * step + Vector3.up * rise;
                        if (IsFree(p, self, radius, height)) return p;
                    }
                }
            return dest;
        }

        private static void GetCapsule(Transform self, out float radius, out float height)
        {
            radius = 0.35f;
            height = 1.8f;
            var cap = self != null ? self.GetComponentInChildren<CapsuleCollider>() : null;
            if (cap == null) return;
            var s = cap.transform.lossyScale;
            radius = cap.radius * Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.z));
            height = Mathf.Max(cap.height * Mathf.Abs(s.y), radius * 2f);
        }

        // 발밑 기준으로 캡슐이 빈 자리인지. 자기 몸(들고 있는 재료 포함)과 트리거는 무시한다.
        private static bool IsFree(Vector3 foot, Transform self, float radius, float height)
        {
            float r = radius * 0.9f;   // 벽·바닥에 스치는 오탐 방지로 살짝 줄인다
            var p0 = foot + Vector3.up * (r + 0.05f);
            var p1 = foot + Vector3.up * Mathf.Max(r + 0.05f, height - r);
            int n = Physics.OverlapCapsuleNonAlloc(p0, p1, r, s_Overlap, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var c = s_Overlap[i];
                if (c == null) continue;
                if (self != null && c.transform.IsChildOf(self)) continue;
                return false;
            }
            return true;
        }

        // ── 문 비주얼(로컬, 레인별) ──────────────────────────────────────────
        private GameObject m_Root;
        private readonly GameObject[] m_DoorLower = new GameObject[kMaxLanes];
        private readonly GameObject[] m_DoorUpper = new GameObject[kMaxLanes];
        private readonly GameObject[] m_Platform = new GameObject[kMaxLanes];   // 폴백 상부 발판(마커 맵은 배경 것을 쓴다)
        private readonly TextMesh[] m_PromptLower = new TextMesh[kMaxLanes];
        private readonly TextMesh[] m_PromptUpper = new TextMesh[kMaxLanes];
        private readonly Transform[] m_PanelLowerL = new Transform[kMaxLanes];
        private readonly Transform[] m_PanelLowerR = new Transform[kMaxLanes];
        private readonly Transform[] m_PanelUpperL = new Transform[kMaxLanes];
        private readonly Transform[] m_PanelUpperR = new Transform[kMaxLanes];
        private readonly float[] m_DoorAnim = new float[kMaxLanes];   // 탑승/개통 시 문 열렸다 닫히는 연출 타이머
        private readonly bool[] m_TintedOpen = new bool[kMaxLanes];
        private GameObject m_ScenePlatform;   // 배경이 제공하는 상부 발판(UpperDoorPlatform) — 있으면 레인0이 쓴다
        private float m_NextPlatformFind;
        private const float kPanelX = 0.22f;   // 문짝 기본 위치(닫힘)

        private void UpdateVisuals()
        {
            if (m_Root == null) m_Root = new GameObject("~NamsanElevator");

            // 배경이 발판을 들고 있으면(남산타워 맵) 그걸 쓰고, 없으면(2vs2 공터) 레인별로 만들어 준다.
            if (m_ScenePlatform == null && Time.time >= m_NextPlatformFind)
            {
                m_NextPlatformFind = Time.time + 0.5f;
                m_ScenePlatform = GameObject.Find("UpperDoorPlatform");   // 비활성화 전에 1회 캐시(Find는 활성만 찾음)
            }

            bool finished = Loop != null && !Loop.IsBuilding;
            int localLane = LocalLane;
            var nm = NetworkManager.Singleton;
            var po = nm != null && nm.LocalClient != null ? nm.LocalClient.PlayerObject : null;
            var ppos = po != null ? po.transform.position : new Vector3(1e6f, 0f, 0f);

            for (int lane = 0; lane < m_Lanes; lane++)
            {
                if (!TryGetDoors(lane, out var d)) { SetLaneActive(lane, false); continue; }

                if (m_DoorLower[lane] == null)
                {
                    m_DoorLower[lane] = MakeDoor(d.lowerPos, d.lowerRot, out m_PromptLower[lane]);
                    m_DoorUpper[lane] = MakeDoor(d.upperPos, d.upperRot, out m_PromptUpper[lane]);
                    m_PanelLowerL[lane] = m_DoorLower[lane].transform.Find("panelL");
                    m_PanelLowerR[lane] = m_DoorLower[lane].transform.Find("panelR");
                    m_PanelUpperL[lane] = m_DoorUpper[lane].transform.Find("panelL");
                    m_PanelUpperR[lane] = m_DoorUpper[lane].transform.Find("panelR");
                    m_TintedOpen[lane] = false;
                    TintDoors(lane, false);
                }

                // 문 열렸다 닫히는 연출(탑승·개통 시): 0.25초 열림 → 잠깐 유지 → 닫힘
                if (m_DoorAnim[lane] > 0f)
                {
                    m_DoorAnim[lane] -= Time.deltaTime;
                    float amt = Mathf.Clamp01(Mathf.Min((1.2f - m_DoorAnim[lane]) / 0.25f, m_DoorAnim[lane] / 0.45f));
                    SetPanels(lane, amt);
                }
                else SetPanels(lane, 0f);

                // 마커 라이브 추적 — 기획자가 씬/프리팹에서 Spot_ElevatorLower/Upper를 끌면 문이 즉시 따라간다
                m_DoorLower[lane].transform.SetPositionAndRotation(d.lowerPos, d.lowerRot);
                m_DoorUpper[lane].transform.SetPositionAndRotation(d.upperPos, d.upperRot);

                bool open = IsOpenFor(lane);
                if (m_TintedOpen[lane] != open)
                {
                    m_TintedOpen[lane] = open;
                    TintDoors(lane, open);
                }

                // 상부 문·발판 표시 조건: ① 개통됨 ② 게임 종료(캡처·한바퀴 둘러보기) 중엔 숨김 — 완성 사진에 안 나오게.
                bool showUpper = open && !finished;
                if (m_DoorLower[lane].activeSelf == finished) m_DoorLower[lane].SetActive(!finished);   // 하부 문도 종료 화면에선 숨김
                if (m_DoorUpper[lane].activeSelf != showUpper) m_DoorUpper[lane].SetActive(showUpper);
                UpdatePlatform(lane, d, showUpper);

                // 프롬프트: 개통 + 로컬 플레이어가 자기 레인 문 근처일 때만
                bool mine = lane == localLane;
                SetPrompt(m_PromptLower[lane], mine && open && Near(ppos, d.lowerPos));
                SetPrompt(m_PromptUpper[lane], mine && open && Near(ppos, d.upperPos));
            }
        }

        // 상부 발판: 배경이 준 게 있으면(레인0) 그걸 켜고 끄고, 없으면 문 아래에 하나 만들어 둔다.
        private void UpdatePlatform(int lane, DoorPair d, bool showUpper)
        {
            if (lane == 0 && m_ScenePlatform != null)
            {
                // 배경 발판을 늦게 찾았으면 그 사이 만들어 둔 임시 발판은 치운다(겹침 방지).
                if (m_Platform[0] != null) { Destroy(m_Platform[0]); m_Platform[0] = null; }
                if (m_ScenePlatform.activeSelf != showUpper) m_ScenePlatform.SetActive(showUpper);
                return;
            }

            if (m_Platform[lane] == null)
            {
                var plat = GameObject.CreatePrimitive(PrimitiveType.Cube);
                plat.name = $"~UpperDoorPlatform{lane}";
                plat.transform.SetParent(m_Root.transform);
                plat.transform.localScale = new Vector3(1.8f, 0.4f, kPlatformDepth);
                Tint(plat, new Color(0.42f, 0.32f, 0.22f));
                m_Platform[lane] = plat;
            }
            // 윗면이 문 발밑에 딱 닿고, 문이 바라보는 쪽(로컬 +Z = 사람이 서는 쪽)으로 깔린다.
            // 문은 전망대 벽에서 kDoorGap 앞이므로, 발판 뒤끝이 딱 벽에 닿는다.
            m_Platform[lane].transform.SetPositionAndRotation(
                d.upperPos + d.upperRot * new Vector3(0f, -0.2f, kPlatformDepth * 0.5f - kDoorGap),
                d.upperRot);
            if (m_Platform[lane].activeSelf != showUpper) m_Platform[lane].SetActive(showUpper);
        }

        private void SetLaneActive(int lane, bool on)
        {
            if (m_DoorLower[lane] != null && m_DoorLower[lane].activeSelf != on) m_DoorLower[lane].SetActive(on);
            if (m_DoorUpper[lane] != null && m_DoorUpper[lane].activeSelf != on) m_DoorUpper[lane].SetActive(on);
            if (m_Platform[lane] != null && m_Platform[lane].activeSelf != on) m_Platform[lane].SetActive(on);
        }

        private void DestroyVisuals()
        {
            if (m_Root != null) Destroy(m_Root);
            m_Root = null;
            for (int i = 0; i < kMaxLanes; i++)
            {
                m_DoorLower[i] = m_DoorUpper[i] = m_Platform[i] = null;
                m_PromptLower[i] = m_PromptUpper[i] = null;
                m_PanelLowerL[i] = m_PanelLowerR[i] = m_PanelUpperL[i] = m_PanelUpperR[i] = null;
            }
        }

        private GameObject MakeDoor(Vector3 pos, Quaternion rot, out TextMesh prompt)
        {
            var root = new GameObject("door");
            root.transform.SetParent(m_Root.transform);
            root.transform.SetPositionAndRotation(pos, rot);

            // 진짜 건물 엘리베이터 느낌: 작은 프레임 + 좌우 슬라이딩 문짝 2개
            var frame = GameObject.CreatePrimitive(PrimitiveType.Cube);
            frame.name = "frame";
            frame.transform.SetParent(root.transform, false);
            frame.transform.localPosition = new Vector3(0f, 0.95f, 0f);
            frame.transform.localScale = new Vector3(1.2f, 1.9f, 0.22f);
            var fcol = frame.GetComponent<Collider>();
            if (fcol != null) Destroy(fcol);
            Tint(frame, new Color(0.25f, 0.27f, 0.3f));

            foreach (int side in new[] { -1, 1 })
            {
                var panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
                panel.name = side < 0 ? "panelL" : "panelR";
                panel.transform.SetParent(root.transform, false);
                panel.transform.localPosition = new Vector3(side * kPanelX, 0.9f, 0.07f);
                panel.transform.localScale = new Vector3(0.44f, 1.6f, 0.12f);
                var pcol = panel.GetComponent<Collider>();
                if (pcol != null) Destroy(pcol);

                // 문짝 텍스처(Resources/Namsan/ElevatorDoor) — 있으면 스틸 문, 상태 틴트는 그 위에 곱해짐
                var doorTex = DoorTexture();
                if (doorTex != null)
                {
                    if (s_DoorMat == null)
                    {
                        var dsh = Shader.Find("Universal Render Pipeline/Lit");
                        if (dsh != null)
                        {
                            s_DoorMat = new Material(dsh) { hideFlags = HideFlags.HideAndDontSave };
                            s_DoorMat.SetTexture("_BaseMap", doorTex);
                        }
                    }
                    if (s_DoorMat != null) panel.GetComponent<Renderer>().sharedMaterial = s_DoorMat;
                }
            }

            var tgo = new GameObject("prompt");
            tgo.transform.SetParent(root.transform, false);
            tgo.transform.localPosition = new Vector3(0f, 2.15f, 0f);
            prompt = tgo.AddComponent<TextMesh>();
            prompt.text = "E 탑승";
            prompt.fontSize = 48;
            prompt.characterSize = 0.05f;
            prompt.anchor = TextAnchor.MiddleCenter;
            prompt.alignment = TextAlignment.Center;
            prompt.color = new Color(0.5f, 1f, 0.6f);
            var font = BuiltinFont();
            if (font != null)
            {
                prompt.font = font;
                var mr = tgo.GetComponent<MeshRenderer>();
                if (mr != null) mr.material = font.material;
            }
            tgo.SetActive(false);
            return root;
        }

        private void SetPrompt(TextMesh prompt, bool on)
        {
            if (prompt == null) return;
            if (prompt.gameObject.activeSelf != on) prompt.gameObject.SetActive(on);
            if (on && Camera.main != null)
                prompt.transform.rotation = Quaternion.LookRotation(prompt.transform.position - Camera.main.transform.position);
        }

        private void TintDoors(int lane, bool open)
        {
            var c = open ? new Color(0.35f, 0.95f, 0.5f) : new Color(0.45f, 0.45f, 0.5f);
            foreach (var p in new[] { m_PanelLowerL[lane], m_PanelLowerR[lane], m_PanelUpperL[lane], m_PanelUpperR[lane] })
                if (p != null) Tint(p.gameObject, c);
        }

        // 문짝 열림량 적용(0=닫힘, 1=활짝) — 양쪽 문 동시에
        private void SetPanels(int lane, float amt)
        {
            float x = kPanelX + 0.30f * amt;
            if (m_PanelLowerL[lane] != null) m_PanelLowerL[lane].localPosition = new Vector3(-x, 0.9f, 0.07f);
            if (m_PanelLowerR[lane] != null) m_PanelLowerR[lane].localPosition = new Vector3(x, 0.9f, 0.07f);
            if (m_PanelUpperL[lane] != null) m_PanelUpperL[lane].localPosition = new Vector3(-x, 0.9f, 0.07f);
            if (m_PanelUpperR[lane] != null) m_PanelUpperR[lane].localPosition = new Vector3(x, 0.9f, 0.07f);
        }

        private static Font BuiltinFont()
        {
            try { return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); }
            catch { }
            try { return Resources.GetBuiltinResource<Font>("Arial.ttf"); }
            catch { }
            return null;
        }

        private static Material s_DoorMat;
        private static Texture2D s_DoorTex;
        private static bool s_DoorTexTried;
        private static Texture2D DoorTexture()
        {
            if (!s_DoorTexTried) { s_DoorTexTried = true; s_DoorTex = Resources.Load<Texture2D>("Namsan/ElevatorDoor"); }
            return s_DoorTex;
        }

        private static Material s_Mat;
        private static void Tint(GameObject go, Color c)
        {
            var r = go.GetComponent<Renderer>();
            if (r == null) return;
            if (s_Mat == null)
            {
                var sh = Shader.Find("Universal Render Pipeline/Lit");
                s_Mat = sh != null ? new Material(sh) { hideFlags = HideFlags.HideAndDontSave } : null;
            }
            if (s_Mat != null) r.sharedMaterial = s_Mat;
            var mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            mpb.SetColor(Shader.PropertyToID("_BaseColor"), c);
            mpb.SetColor(Shader.PropertyToID("_Color"), c);
            r.SetPropertyBlock(mpb);
        }
    }
}
