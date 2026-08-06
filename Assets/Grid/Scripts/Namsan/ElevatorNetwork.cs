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
        }

        public override void OnNetworkDespawn()
        {
            m_Open.OnValueChanged -= OnOpenChanged;
            DestroyVisuals();
            base.OnNetworkDespawn();
        }

        /// <summary>재시작용(서버): 다음 라운드는 다시 잠긴 상태부터.</summary>
        public void ServerReset()
        {
            if (!IsServer || !Active) return;
            m_Open.Value = false;
        }

        private void Update()
        {
            if (!Active || !IsSpawned) return;

            if (IsServer && !m_Open.Value && Time.time >= m_NextCheck)
            {
                m_NextCheck = Time.time + 0.5f;
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
            if (kb == null || !kb.eKey.wasPressedThisFrame) return;

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
        private TextMesh m_PromptLower, m_PromptUpper;
        private bool m_TintedOpen;

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
                m_TintedOpen = false;
                TintDoors(false);
            }

            if (m_TintedOpen != m_Open.Value)
            {
                m_TintedOpen = m_Open.Value;
                TintDoors(m_TintedOpen);
            }

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

            var frame = GameObject.CreatePrimitive(PrimitiveType.Cube);
            frame.name = "frame";
            frame.transform.SetParent(root.transform, false);
            frame.transform.localPosition = new Vector3(0f, 1.1f, 0f);
            frame.transform.localScale = new Vector3(1.6f, 2.2f, 0.25f);
            var fcol = frame.GetComponent<Collider>();
            if (fcol != null) Destroy(fcol);
            Tint(frame, new Color(0.25f, 0.27f, 0.3f));

            var panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            panel.name = "panel";
            panel.transform.SetParent(root.transform, false);
            panel.transform.localPosition = new Vector3(0f, 1.05f, 0.08f);
            panel.transform.localScale = new Vector3(1.2f, 1.9f, 0.15f);
            var pcol = panel.GetComponent<Collider>();
            if (pcol != null) Destroy(pcol);

            var tgo = new GameObject("prompt");
            tgo.transform.SetParent(root.transform, false);
            tgo.transform.localPosition = new Vector3(0f, 2.6f, 0f);
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
            if (m_DoorLower != null) Tint(m_DoorLower.transform.Find("panel").gameObject, c);
            if (m_DoorUpper != null) Tint(m_DoorUpper.transform.Find("panel").gameObject, c);
        }

        private static Font BuiltinFont()
        {
            try { return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); }
            catch { }
            try { return Resources.GetBuiltinResource<Font>("Arial.ttf"); }
            catch { }
            return null;
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
