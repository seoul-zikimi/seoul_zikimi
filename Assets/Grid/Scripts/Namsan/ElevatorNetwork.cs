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
    /// </summary>
    public class ElevatorNetwork : NamsanGimmickBase
    {
        private readonly NetworkVariable<bool> m_Open =
            new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private GridNetwork m_Net;
        private float m_NextCheck;
        private bool m_CellsDirty = true;   // 그리드 셀이 변한 뒤에만 판정(스폰 직후 1회는 무조건 — 늦참·치트 완성 대비)
        private bool m_WarnedNoBand;   // 판정 영역에 정답 셀이 하나도 없으면 1회 경고
        private float m_RideCooldown;  // 연타로 왕복 떨림 방지

        private const float kUseRange = 2.2f;   // 문 앞 E 사용 거리

        /// <summary>개통 여부(HUD·연출용).</summary>
        public bool IsOpen => m_Open.Value;

        protected override void Awake()
        {
            base.Awake();
            m_Net = GetComponent<GridNetwork>();
        }

        protected override void OnGimmickSpawn()
        {
            m_Open.OnValueChanged += OnOpenChanged;
            if (m_Net != null) m_Net.CellsChanged += OnGridCellsChanged;   // 0.5초 폴링의 더티 게이트
        }

        public override void OnNetworkDespawn()
        {
            m_Open.OnValueChanged -= OnOpenChanged;
            if (m_Net != null) m_Net.CellsChanged -= OnGridCellsChanged;
            DestroyVisuals();
            base.OnNetworkDespawn();
        }

        private void OnGridCellsChanged() => m_CellsDirty = true;

        /// <summary>재시작용(서버): 다음 라운드는 다시 잠긴 상태부터.</summary>
        public void ServerReset()
        {
            if (!IsServer || !Active) return;
            m_Open.Value = false;
        }

        private void Update()
        {
            if (!Active || !IsSpawned) return;

            if (IsServer && !m_Open.Value && m_CellsDirty && Time.time >= m_NextCheck)
            {
                m_NextCheck = Time.time + 0.5f;
                m_CellsDirty = false;   // 다음 셀 변경까지 전체 스캔 판정 쉼
                if (CheckObservatoryComplete()) m_Open.Value = true;
            }

            UpdateVisuals();
            UpdateLocalRide();
        }

        // ── 개통 판정(서버) ───────────────────────────────────────────────────
        private bool CheckObservatoryComplete()
        {
            var answer = Grid != null ? Grid.Answer : null;
            var cat = Grid != null ? Grid.Catalog : null;
            if (answer == null || cat == null || m_Net == null) return false;

            int band = 0;
            foreach (var a in answer.Cells)
            {
                if (a.cell.y < Config.ObservatoryMinY || a.cell.y > Config.ObservatoryMaxY) continue;
                if (answer.IsPreset(a.cell)) continue;   // 기본 제공 블록은 판정 제외(채점과 동일 규칙)
                band++;

                if (!m_Net.TryGetCell(a.cell, out int placedId, out int mask)) return false;
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

        private void OnOpenChanged(bool _, bool open)
        {
            if (!open) return;
            // 개통 연출(전 클라 로컬) — 두 문 동시에.
            var lower = FindSpot(NamsanSpots.ElevatorLower);
            var upper = FindSpot(NamsanSpots.ElevatorUpper);
            if (lower != null)
            {
                GridJuice.WorldToast(lower.position + Vector3.up * 2.2f, "엘리베이터 개통!", new Color(0.4f, 1f, 0.55f));
                GridJuice.PlacePuff(lower.position, 1f);
            }
            if (upper != null)
            {
                GridJuice.WorldToast(upper.position + Vector3.up * 2.2f, "엘리베이터 개통!", new Color(0.4f, 1f, 0.55f));
                GridJuice.PlacePuff(upper.position, 1f);
            }
            m_DoorAnim = 1.2f;   // 개통 순간에도 한 번 열렸다 닫힘
        }

        // ── 탑승(로컬 플레이어) ───────────────────────────────────────────────
        private void UpdateLocalRide()
        {
            if (m_RideCooldown > 0f) { m_RideCooldown -= Time.deltaTime; return; }
            if (!m_Open.Value) return;

            var nm = NetworkManager.Singleton;
            var po = nm != null && nm.LocalClient != null ? nm.LocalClient.PlayerObject : null;
            if (po == null) return;

            var lower = FindSpot(NamsanSpots.ElevatorLower);
            var upper = FindSpot(NamsanSpots.ElevatorUpper);
            if (lower == null || upper == null) return;

            var pos = po.transform.position;
            Transform from = null, to = null;
            if (Near(pos, lower.position)) { from = lower; to = upper; }
            else if (Near(pos, upper.position)) { from = upper; to = lower; }
            if (from == null) return;

            var kb = Keyboard.current;
            if (GameplayInputBlocker.Blocked || kb == null || !kb.eKey.wasPressedThisFrame) return;

            // 순간이동(오너 권위 — ClientNetworkTransform이 알아서 복제). PlaceScaffold와 같은 방식.
            var dest = to.position + Vector3.up * 0.1f;
            po.transform.position = dest;
            var rb = po.GetComponent<Rigidbody>();
            if (rb != null && !rb.isKinematic)
            {
                rb.position = dest;
                rb.linearVelocity = Vector3.zero;
            }
            Physics.SyncTransforms();
            GridJuice.PlacePuff(from.position, 0.6f);
            GridJuice.PlacePuff(dest, 0.6f);
            m_DoorAnim = 1.2f;   // 문 열렸다 닫히는 연출
            m_RideCooldown = 0.4f;
        }

        private static bool Near(Vector3 p, Vector3 door)
        {
            var d = p - door;
            d.y = 0f;
            return d.sqrMagnitude <= kUseRange * kUseRange && Mathf.Abs(p.y - door.y) <= 2.5f;
        }

        // ── 문 비주얼(로컬) ──────────────────────────────────────────────────
        private GameObject m_Root;
        private GameObject m_DoorLower, m_DoorUpper;
        private GameObject m_UpperPlatform;   // 배경의 상부 발판(개통 시 등장)
        private TextMesh m_PromptLower, m_PromptUpper;
        private Transform m_PanelLowerL, m_PanelLowerR, m_PanelUpperL, m_PanelUpperR;
        private float m_DoorAnim;        // 탑승/개통 시 문 열렸다 닫히는 연출 타이머
        private bool m_TintedOpen;
        private const float kPanelX = 0.22f;   // 문짝 기본 위치(닫힘)

        private void UpdateVisuals()
        {
            var lower = FindSpot(NamsanSpots.ElevatorLower);
            var upper = FindSpot(NamsanSpots.ElevatorUpper);
            if (lower == null || upper == null) return;

            if (m_Root == null)
            {
                m_Root = new GameObject("~NamsanElevator");
                m_DoorLower = MakeDoor(lower.position, lower.rotation, out m_PromptLower);
                m_DoorUpper = MakeDoor(upper.position, upper.rotation, out m_PromptUpper);
                m_PanelLowerL = m_DoorLower.transform.Find("panelL");
                m_PanelLowerR = m_DoorLower.transform.Find("panelR");
                m_PanelUpperL = m_DoorUpper.transform.Find("panelL");
                m_PanelUpperR = m_DoorUpper.transform.Find("panelR");
                m_TintedOpen = false;
                TintDoors(false);
            }

            // 문 열렸다 닫히는 연출(탑승·개통 시): 0.25초 열림 → 잠깐 유지 → 닫힘
            if (m_DoorAnim > 0f)
            {
                m_DoorAnim -= Time.deltaTime;
                float amt = Mathf.Clamp01(Mathf.Min((1.2f - m_DoorAnim) / 0.25f, m_DoorAnim / 0.45f));
                SetPanels(amt);
            }
            else SetPanels(0f);

            // 마커 라이브 추적 — 기획자가 씬/프리팹에서 Spot_ElevatorLower/Upper를 끌면 문이 즉시 따라간다
            if (m_DoorLower != null) m_DoorLower.transform.SetPositionAndRotation(lower.position, lower.rotation);
            if (m_DoorUpper != null) m_DoorUpper.transform.SetPositionAndRotation(upper.position, upper.rotation);

            if (m_TintedOpen != m_Open.Value)
            {
                m_TintedOpen = m_Open.Value;
                TintDoors(m_TintedOpen);
            }

            // 상부 문·발판 표시 조건: ① 개통됨 ② 게임 종료(캡처·한바퀴 둘러보기) 중엔 숨김 — 완성 사진에 안 나오게.
            bool finished = Loop != null && !Loop.IsBuilding;
            bool showUpper = m_Open.Value && !finished;
            if (m_DoorLower != null && m_DoorLower.activeSelf == finished)
                m_DoorLower.SetActive(!finished);   // 하부 문도 종료 화면에선 숨김
            if (m_DoorUpper != null && m_DoorUpper.activeSelf != showUpper)
                m_DoorUpper.SetActive(showUpper);
            if (m_UpperPlatform == null)
            {
                var plat = GameObject.Find("UpperDoorPlatform");   // 비활성화 전에 1회 캐시(Find는 활성만 찾음)
                if (plat != null) m_UpperPlatform = plat;
            }
            if (m_UpperPlatform != null && m_UpperPlatform.activeSelf != showUpper)
                m_UpperPlatform.SetActive(showUpper);

            // 프롬프트: 개통 + 로컬 플레이어가 근처일 때만
            var nm = NetworkManager.Singleton;
            var po = nm != null && nm.LocalClient != null ? nm.LocalClient.PlayerObject : null;
            var ppos = po != null ? po.transform.position : new Vector3(1e6f, 0f, 0f);
            SetPrompt(m_PromptLower, m_Open.Value && Near(ppos, lower.position));
            SetPrompt(m_PromptUpper, m_Open.Value && Near(ppos, upper.position));
        }

        private void DestroyVisuals()
        {
            if (m_Root != null) Destroy(m_Root);
            m_Root = null;
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

        private void TintDoors(bool open)
        {
            var c = open ? new Color(0.35f, 0.95f, 0.5f) : new Color(0.45f, 0.45f, 0.5f);
            foreach (var p in new[] { m_PanelLowerL, m_PanelLowerR, m_PanelUpperL, m_PanelUpperR })
                if (p != null) Tint(p.gameObject, c);
        }

        // 문짝 열림량 적용(0=닫힘, 1=활짝) — 양쪽 문 동시에
        private void SetPanels(float amt)
        {
            float x = kPanelX + 0.30f * amt;
            if (m_PanelLowerL != null) m_PanelLowerL.localPosition = new Vector3(-x, 0.9f, 0.07f);
            if (m_PanelLowerR != null) m_PanelLowerR.localPosition = new Vector3(x, 0.9f, 0.07f);
            if (m_PanelUpperL != null) m_PanelUpperL.localPosition = new Vector3(-x, 0.9f, 0.07f);
            if (m_PanelUpperR != null) m_PanelUpperR.localPosition = new Vector3(x, 0.9f, 0.07f);
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
