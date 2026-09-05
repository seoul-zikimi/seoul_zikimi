using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GridSystem
{
    /// <summary>
    /// DDP 기믹 ② 유구(遺構) 발굴터 —
    /// 2008년 동대문운동장을 철거하다 한양도성 성곽 265m와 이간수문, 조선 전기 유물 2,500여 점이 발굴돼
    /// DDP 설계가 바뀌고 그 자리에 동대문역사문화공원·유구전시장이 남았다. 그 발굴을 기믹으로 옮긴 것.
    ///
    /// 흐름:
    ///   ① 후보 지점(Spot_DigSite0~N) 중 한 곳에서 '조사 표지 말뚝'이 땅을 뚫고 솟는다(작고 조용한 신호).
    ///   ② 빈손으로 다가가면 "E 꾹 — 발굴" 프롬프트가 뜬다.
    ///   ③ E를 누르고 있으면 게이지가 찬다. 여럿이 같이 파면 그만큼 빨리 파진다(협동 보상).
    ///   ④ 다 파면 뿅! —
    ///        · 보너스 재료(기본 70%) : 주문·배송을 기다리지 않아도 되는 덤
    ///        · 유물(기본 30%)        : 백자 조각·엽전·수막새. 점수 보너스로 최종 점수에 합산된다.
    ///   ⑤ 물길(WaterGateNetwork)이 흐르는 동안엔 잠겨서 팔 수 없고, 물이 빠지면 즉시 새 말뚝이 솟는다.
    ///     → 기믹 ①과 한 사이클로 맞물린다.
    ///
    /// 서버 권위: 어느 지점이 드러났는지·진행도·출토를 서버가 정하고 NetworkVariable로 복제.
    /// E 입력은 각 클라가 자기 것만 읽어 서버에 의사를 보내고(엣지에서만), 서버가 위치를 다시 검증한다
    /// — 클라 보고만 믿지 않는다. ElevatorNetwork(남산)와 같은 체계.
    /// GameLoopManager가 런타임 부착, 맵 카드에 DdpGimmickConfig가 없으면 스스로 잠잔다.
    /// </summary>
    public class ExcavationNetwork : DdpGimmickBase
    {
        public struct DigState : INetworkSerializable, System.IEquatable<DigState>
        {
            public sbyte site;      // 말뚝이 솟은 지점 index. -1 = 없음(잠김/쿨다운)
            public byte diggers;    // 지금 파고 있는 인원(연출용)
            public float progress;  // 0~1
            public byte found;      // 이번 라운드 누적 출토 수
            public byte artifacts;  // 그중 유물 수(결과 화면용)

            public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
            {
                s.SerializeValue(ref site);
                s.SerializeValue(ref diggers);
                s.SerializeValue(ref progress);
                s.SerializeValue(ref found);
                s.SerializeValue(ref artifacts);
            }
            public bool Equals(DigState o) =>
                site == o.site && diggers == o.diggers &&
                Mathf.Approximately(progress, o.progress) && found == o.found && artifacts == o.artifacts;
        }

        private readonly NetworkVariable<DigState> m_State =
            new(new DigState { site = -1 }, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        /// <summary>이 맵에서 발굴터가 켜져 있으면 인스턴스. 아니면 null.</summary>
        public static ExcavationNetwork Instance { get; private set; }

        /// <summary>지금 말뚝이 솟아 있는 지점 index(-1이면 없음).</summary>
        public int ExposedSite => m_State.Value.site;
        /// <summary>이번 라운드 누적 출토 수(재료+유물).</summary>
        public int TotalFound => m_State.Value.found;
        /// <summary>이번 라운드 출토한 '유물' 수 — 결과 화면에 쓴다.</summary>
        public int ArtifactsFound => m_State.Value.artifacts;

        // 유물 3종(Resources/Ddp/Artifact0~2). 이름은 토스트 문구에 그대로 쓴다.
        private static readonly string[] kArtifactNames = { "백자 조각", "엽전", "수막새" };

        private readonly List<Vector3> m_Sites = new();
        private float m_NextExposeAt;    // 서버 전용: 다음 말뚝이 솟을 시각
        private int m_LastSite = -1;     // 같은 자리 연속 노출 방지
        private MaterialDropField m_Drop;
        private MaterialDepot m_Depot;
        private GridNetwork m_Net;       // 유물 보너스 점수 훅

        // 서버: 지금 "E 누르고 있다"고 보고한 클라들. 위치는 서버가 매 틱 다시 본다.
        private readonly HashSet<ulong> m_Digging = new();
        // 클라: 마지막으로 서버에 보낸 의사(바뀔 때만 RPC — 매 프레임 보내지 않는다)
        private bool m_SentDigging;

        protected override void Awake()
        {
            base.Awake();
            m_Net = GetComponent<GridNetwork>();
        }

        protected override void OnGimmickSpawn()
        {
            Instance = this;
            if (IsServer)
            {
                m_NextExposeAt = Now + 3f;                      // 라운드 시작 직후 첫 말뚝
                WaterGateNetwork.FloodEnded += OnFloodEnded;    // 물이 빠지면 즉시 새 말뚝
                var nm = NetworkManager.Singleton;
                if (nm != null) nm.OnClientDisconnectCallback += OnClientLeft;
            }
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this) Instance = null;
            WaterGateNetwork.FloodEnded -= OnFloodEnded;
            var nm = NetworkManager.Singleton;
            if (nm != null) nm.OnClientDisconnectCallback -= OnClientLeft;
            m_Digging.Clear();
            DestroyVisuals();
        }

        /// <summary>재시작용(서버): 말뚝 감추고 누적 출토 수 초기화.</summary>
        public void ServerReset()
        {
            if (!IsServer || !Active) return;
            m_State.Value = new DigState { site = -1 };
            m_LastSite = -1;
            m_Digging.Clear();
            m_NextExposeAt = Now + 3f;
        }

        // 나간 사람이 계속 파고 있는 것으로 남지 않게.
        private void OnClientLeft(ulong clientId) => m_Digging.Remove(clientId);

        // 물이 빠진 직후 = 새 말뚝이 솟는 순간(쿨다운을 건너뛴다).
        private void OnFloodEnded()
        {
            if (!IsServer || !Active) return;
            m_NextExposeAt = Now;
        }

        private void Update()
        {
            if (!Active || !IsSpawned) return;
            RefreshMarkers(DdpSpots.DigSitePrefix, m_Sites, 1);
            UpdateLocalDigIntent();          // 내 E 입력 → 서버로(바뀔 때만)
            if (IsServer) ServerTick();
            UpdateVisuals();
        }

        // ── 로컬 입력: 빈손으로 말뚝 근처에서 E를 누르고 있으면 '파는 중' 의사를 보낸다 ──────
        // E는 공정(도구 든 상태)·아이템(2vs2)이 이미 쓰고 있다 — 둘 다 아닌 '빈손'일 때만 발굴로 친다.
        private bool m_NearStake;   // 프롬프트 표시용(로컬)

        private void UpdateLocalDigIntent()
        {
            bool inRange = false, digging = false;

            var s = m_State.Value;
            if (s.site >= 0 && s.site < m_Sites.Count)
            {
                var nm = NetworkManager.Singleton;
                var po = nm != null && nm.LocalClient != null ? nm.LocalClient.PlayerObject : null;
                if (po != null && InDigRange(po.transform.position, m_Sites[s.site]))
                {
                    inRange = !LocalPlayerHands.IsEKeyTaken;   // 뭔가 들었으면 E는 그쪽 차지 — 프롬프트도 안 띄운다
                    var kb = Keyboard.current;
                    digging = inRange && kb != null && kb.eKey.isPressed;
                }
            }

            m_NearStake = inRange;
            if (digging == m_SentDigging) return;
            m_SentDigging = digging;
            DigIntentRpc(digging);
        }

        [Rpc(SendTo.Server)]
        private void DigIntentRpc(bool digging, RpcParams p = default)
        {
            ulong id = p.Receive.SenderClientId;
            if (digging) m_Digging.Add(id);
            else m_Digging.Remove(id);
        }

        private bool InDigRange(Vector3 p, Vector3 site)
        {
            if (Mathf.Abs(p.y - site.y) > 2f) return false;   // 위층에서 지나가는 건 발굴이 아니다
            var flat = new Vector2(p.x - site.x, p.z - site.z);
            return flat.sqrMagnitude <= Config.DigRadius * Config.DigRadius;
        }

        private void ServerTick()
        {
            if (m_Sites.Count == 0) return;
            var s = m_State.Value;

            // 물이 차면 잠긴다 — 솟아 있던 말뚝도 다시 묻히고 진행도가 날아간다.
            if (WaterGateNetwork.IsFlooding)
            {
                if (s.site >= 0 || s.progress > 0f)
                {
                    s.site = -1; s.progress = 0f; s.diggers = 0;
                    m_State.Value = s;
                    m_Digging.Clear();
                }
                return;
            }

            if (Loop == null || !Loop.IsBuilding) return;

            // 라운드 출토 한도
            bool capped = Config.DigMaxPerRound > 0 && s.found >= Config.DigMaxPerRound;

            if (s.site < 0)
            {
                if (capped || Now < m_NextExposeAt) return;
                s.site = (sbyte)PickSite();
                s.progress = 0f;
                s.diggers = 0;
                m_State.Value = s;
                m_LastSite = s.site;
                return;
            }

            // 말뚝 앞에서 E를 누르고 있는 인원수만큼 게이지가 찬다(협동일수록 빠르게).
            int diggers = CountDiggersOn(m_Sites[s.site]);
            float perSec = Mathf.Max(0.01f, Config.DigSeconds);
            float delta = diggers * Time.deltaTime / perSec;

            if (diggers == 0)
            {
                // 손을 떼면 천천히 되돌아간다(끝까지 눌러야 한다는 압박)
                delta = -Time.deltaTime / (perSec * 2f);
            }

            float next = Mathf.Clamp01(s.progress + delta);
            if (next >= 1f)
            {
                Unearth(m_Sites[s.site], ref s);
                s.site = -1;
                s.progress = 0f;
                s.diggers = 0;
                s.found = (byte)Mathf.Min(255, s.found + 1);
                m_State.Value = s;
                m_NextExposeAt = Now + Config.DigRespawnSeconds;
                m_Digging.Clear();   // 파던 손 초기화 — 다음 말뚝은 다시 눌러야 한다
                return;
            }

            // 값이 의미 있게 바뀔 때만 복제(매 프레임 쓰기 방지)
            if (Mathf.Abs(next - s.progress) > 0.02f || diggers != s.diggers)
            {
                s.progress = next;
                s.diggers = (byte)Mathf.Min(255, diggers);
                m_State.Value = s;
            }
        }

        private int PickSite()
        {
            if (m_Sites.Count == 1) return 0;
            int idx;
            do { idx = Random.Range(0, m_Sites.Count); } while (idx == m_LastSite);
            return idx;
        }

        // 서버: E를 누르고 있다고 보고했고, 실제로도 말뚝 반경 안에 서 있는 플레이어 수.
        private int CountDiggersOn(Vector3 site)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || m_Digging.Count == 0) return 0;

            int n = 0;
            foreach (var c in nm.ConnectedClientsList)
            {
                if (!m_Digging.Contains(c.ClientId)) continue;
                var po = c.PlayerObject;
                if (po == null) continue;
                if (InDigRange(po.transform.position, site)) n++;   // 위치는 서버가 다시 검증
            }
            return n;
        }

        // 서버: 출토 — 유물(점수 보너스) 또는 보너스 재료.
        // 재료는 주문(MaterialDepot)을 거치지 않으므로 주문 한도(MaxSpawnCount)와 무관한 '덤'이다.
        // 그래서 DigMaxPerRound로 라운드당 개수를 따로 막는다.
        private void Unearth(Vector3 site, ref DigState s)
        {
            bool artifact = Config.ArtifactChance > 0f && Random.value < Config.ArtifactChance;

            if (artifact)
            {
                int kind = Random.Range(0, kArtifactNames.Length);
                int points = Mathf.Max(0, Config.ArtifactScoreBonus);
                // DDP는 협동 전용이지만, 2vs2로 열어도 판 팀에 붙도록 파는 사람의 팀으로 넣는다.
                if (m_Net != null) m_Net.ServerAddBonus(points, TeamOfFirstDigger());
                s.artifacts = (byte)Mathf.Min(255, s.artifacts + 1);
                UnearthedArtifactRpc(site, kind, points);
                Debug.Log($"[DDP] 유물 출토: {kArtifactNames[kind]} (+{points}점) @ {site}");
                return;
            }

            if (m_Drop == null) m_Drop = FindFirstObjectByType<MaterialDropField>();
            if (m_Depot == null) m_Depot = FindFirstObjectByType<MaterialDepot>();
            if (m_Drop == null || m_Depot == null) return;

            var pool = m_Depot.OrderableMaterials;
            if (pool == null || pool.Count == 0) return;

            var pick = pool[Random.Range(0, pool.Count)];
            if (pick == null) return;

            var from = site + Vector3.up * 1.2f;
            m_Drop.ServerDeliver(pick.Id, from, site);
            UnearthedMaterialRpc(site);
            Debug.Log($"[DDP] 유구 출토(재료): {pick.name} @ {site}");
        }

        // 파고 있던 사람 중 첫 번째의 팀(협동이면 항상 0).
        private int TeamOfFirstDigger()
        {
            if (Loop == null || !Loop.IsVersus) return 0;
            foreach (var id in m_Digging) return Loop.GetTeam(id);
            return 0;
        }

        [Rpc(SendTo.Everyone)]
        private void UnearthedMaterialRpc(Vector3 site)
        {
            GridJuice.WorldToast(site + Vector3.up * 1.6f, "유구 출토!", new Color(0.90f, 0.78f, 0.45f));
            GridJuice.GroundHit(site, 1.0f);
            GridSoundBridge.PlaySFXAt("LandObject", site);
        }

        [Rpc(SendTo.Everyone)]
        private void UnearthedArtifactRpc(Vector3 site, int kind, int points)
        {
            string name = kind >= 0 && kind < kArtifactNames.Length ? kArtifactNames[kind] : "유물";
            GridJuice.WorldToast(site + Vector3.up * 1.9f, $"{name} 발굴!  +{points}점", kGold);
            GridJuice.GroundHit(site, 1.3f);
            GridJuice.PlacePuff(site, 1.2f);
            GridSoundBridge.PlaySFXAt("LandObject", site);
            StartCoroutine(ArtifactPop(site, kind));
        }

        // 출토 연출(로컬): 유물이 땅에서 뿅 튀어올라 돌다가 사라진다.
        private IEnumerator ArtifactPop(Vector3 site, int kind)
        {
            var prefab = Resources.Load<GameObject>($"Ddp/Artifact{kind}");
            GameObject go;
            if (prefab != null) go = Instantiate(prefab);
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.transform.localScale = Vector3.one * 0.35f;
                Tint(go, kGold);
            }
            go.name = "~DdpArtifactPop";
            foreach (var col in go.GetComponentsInChildren<Collider>()) Destroy(col);

            const float kDur = 1.6f, kRise = 1.5f;
            var baseScale = go.transform.localScale;
            for (float t = 0f; t < kDur; t += Time.deltaTime)
            {
                float u = t / kDur;
                // 위로 튀었다가 살짝 내려앉고, 끝에서 작아지며 사라진다
                float h = Mathf.Sin(Mathf.Clamp01(u * 1.35f) * Mathf.PI) * kRise;
                go.transform.position = site + Vector3.up * (0.3f + h);
                go.transform.rotation = Quaternion.Euler(0f, t * 220f, 0f);
                go.transform.localScale = baseScale * Mathf.Clamp01((1f - u) * 3f);
                yield return null;
            }
            Destroy(go);
        }

        // ── 연출(전 클라 로컬): 표지 말뚝 + 프롬프트 + 진행 게이지 ────────────
        private GameObject m_Stake;       // 조사 표지 말뚝(솟아오름)
        private TextMesh m_Prompt;        // "E 꾹 — 발굴"
        private GameObject m_Gauge;       // 진행도 막대
        private int m_StakeSite = -1;     // 지금 말뚝이 서 있는 지점(바뀌면 다시 솟는 연출)
        private float m_StakeRiseAt;      // 솟기 시작한 시각
        private static readonly Color kGold = new Color(0.95f, 0.80f, 0.35f);
        private const float kStakeRiseSeconds = 0.55f;

        private void UpdateVisuals()
        {
            var s = m_State.Value;
            bool show = s.site >= 0 && s.site < m_Sites.Count;

            if (!show)
            {
                m_StakeSite = -1;
                if (m_Stake != null) m_Stake.SetActive(false);
                if (m_Gauge != null) m_Gauge.SetActive(false);
                // 프롬프트는 말뚝과 형제(부모가 아니라 루트)라 따로 꺼야 한다 — 안 그러면 허공에 남는다.
                if (m_Prompt != null && m_Prompt.gameObject.activeSelf) m_Prompt.gameObject.SetActive(false);
                return;
            }

            var site = m_Sites[s.site];
            EnsureStake();

            // 새 지점이면 땅을 뚫고 솟는 연출을 처음부터
            if (m_StakeSite != s.site)
            {
                m_StakeSite = s.site;
                m_StakeRiseAt = Time.time;
                if (!m_Stake.activeSelf) m_Stake.SetActive(true);
                GridJuice.GroundHit(site, 0.7f);
            }

            float rise = Mathf.Clamp01((Time.time - m_StakeRiseAt) / kStakeRiseSeconds);
            // 살짝 오버슈트했다 가라앉는다(뿅 하고 꽂히는 느낌)
            float ease = 1f - Mathf.Pow(1f - rise, 3f);
            float y = Mathf.Lerp(-0.9f, 0f, ease) + Mathf.Sin(rise * Mathf.PI) * 0.12f;
            m_Stake.transform.position = site + Vector3.up * y;
            // 파는 중엔 부르르 떨린다
            float shake = s.diggers > 0 ? 0.03f : 0f;
            if (shake > 0f)
                m_Stake.transform.position += new Vector3(Mathf.Sin(Time.time * 41f), 0f, Mathf.Cos(Time.time * 37f)) * shake;

            // 프롬프트: 내가 빈손으로 근처에 있을 때만
            if (m_Prompt != null)
            {
                bool on = m_NearStake && rise > 0.6f;
                if (m_Prompt.gameObject.activeSelf != on) m_Prompt.gameObject.SetActive(on);
                if (on)
                {
                    m_Prompt.transform.position = site + Vector3.up * 1.7f;
                    var cam = Camera.main;
                    if (cam != null)
                        m_Prompt.transform.rotation = Quaternion.LookRotation(m_Prompt.transform.position - cam.transform.position);
                    float pulse = 0.75f + Mathf.Abs(Mathf.Sin(Time.time * 3.2f)) * 0.25f;
                    m_Prompt.color = new Color(kGold.r * pulse, kGold.g * pulse, kGold.b * pulse, 1f);
                }
            }

            // 게이지
            if (m_Gauge == null)
            {
                m_Gauge = GameObject.CreatePrimitive(PrimitiveType.Cube);
                m_Gauge.name = "~DdpDigGauge";
                var col = m_Gauge.GetComponent<Collider>();
                if (col != null) Destroy(col);
            }
            bool gaugeOn = s.progress > 0.01f;
            if (m_Gauge.activeSelf != gaugeOn) m_Gauge.SetActive(gaugeOn);
            if (gaugeOn)
            {
                const float kWidth = 2.0f;
                m_Gauge.transform.position = site + Vector3.up * 2.3f;
                m_Gauge.transform.localScale = new Vector3(kWidth * s.progress, 0.16f, 0.16f);
                var cam = Camera.main;
                if (cam != null) m_Gauge.transform.rotation = Quaternion.LookRotation(m_Gauge.transform.position - cam.transform.position);
                Tint(m_Gauge, kGold);
            }
        }

        // 말뚝 실체: Resources/Ddp/DigStake(VARCO 모델) 있으면 그걸, 없으면 막대 폴백.
        private void EnsureStake()
        {
            if (m_Stake != null) return;

            var prefab = Resources.Load<GameObject>("Ddp/DigStake");
            if (prefab != null) m_Stake = Instantiate(prefab);
            else
            {
                m_Stake = new GameObject("stake");
                var post = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                post.transform.SetParent(m_Stake.transform, false);
                post.transform.localPosition = Vector3.up * 0.55f;
                post.transform.localScale = new Vector3(0.09f, 0.55f, 0.09f);
                Tint(post, new Color(0.52f, 0.38f, 0.24f));
                var flag = GameObject.CreatePrimitive(PrimitiveType.Cube);
                flag.transform.SetParent(m_Stake.transform, false);
                flag.transform.localPosition = new Vector3(0.17f, 0.95f, 0f);
                flag.transform.localScale = new Vector3(0.34f, 0.22f, 0.02f);
                Tint(flag, new Color(0.95f, 0.48f, 0.18f));
            }
            m_Stake.name = "~DdpDigStake";
            foreach (var col in m_Stake.GetComponentsInChildren<Collider>()) Destroy(col);

            var tgo = new GameObject("prompt");
            tgo.transform.SetParent(m_Stake.transform.parent, false);
            m_Prompt = tgo.AddComponent<TextMesh>();
            m_Prompt.text = $"{InputHintText.ProcessKey} 꾹 — 발굴";   // 모바일은 '공정 버튼'(MobileControlsHUD가 갱신)
            m_Prompt.fontSize = 48;
            m_Prompt.characterSize = 0.05f;
            m_Prompt.anchor = TextAnchor.MiddleCenter;
            m_Prompt.alignment = TextAlignment.Center;
            m_Prompt.color = kGold;
            var font = BuiltinFont();
            if (font != null)
            {
                m_Prompt.font = font;
                var mr = tgo.GetComponent<MeshRenderer>();
                if (mr != null) mr.material = font.material;
            }
            tgo.SetActive(false);
        }

        private static Font BuiltinFont()
        {
            try { return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); }
            catch { }
            try { return Resources.GetBuiltinResource<Font>("Arial.ttf"); }
            catch { }
            return null;
        }

        private void DestroyVisuals()
        {
            if (m_Stake != null) Destroy(m_Stake);
            if (m_Prompt != null) Destroy(m_Prompt.gameObject);
            if (m_Gauge != null) Destroy(m_Gauge);
            m_Stake = null;
            m_Prompt = null;
            m_Gauge = null;
            m_StakeSite = -1;
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
