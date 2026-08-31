using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace GridSystem
{
    /// <summary>
    /// DDP 기믹 ① 이간수문(二間水門) 물길 —
    /// 예고 후 수문이 열리고 수로(Spot_WaterChannel0~N)를 따라 물이 흐른다.
    ///
    /// 실제 DDP 부지는 2008년 동대문운동장을 철거하다 한양도성 성곽과 이간수문이 발굴돼 설계가 바뀐 곳이다.
    /// 그 수문이 열린다는 설정으로, 수로는 어울림광장(배송·작업대)과 잔디지붕 데크(건축장) 사이를 지난다.
    ///
    /// · 휩쓸리면 하류로 쓸려가고(CurrentSpeed), 벗어나는 순간 스턴 + 든 재료 드롭.
    /// · 대신 재료를 수로에 두면 하류(건축장 쪽)로 빠르게 흘러간다 — 위험을 감수하면 운반이 빨라진다.
    ///
    /// 서버는 페이즈 전이만 복제하고, 물 위치·연출은 페이즈 시작 시각 기반 결정론(전 클라 동일 계산).
    /// 밀림 적용은 각 오너 로컬(PlayerUnit이 CurrentPushAt 폴링 — GustNetwork·ParadeNetwork와 동일 계약).
    /// GameLoopManager가 런타임 부착, 맵 카드에 DdpGimmickConfig가 없으면 스스로 잠잔다.
    /// </summary>
    public class WaterGateNetwork : DdpGimmickBase
    {
        public enum FloodPhase : byte { Idle = 0, Warning = 1, Flowing = 2 }

        public struct FloodState : INetworkSerializable, System.IEquatable<FloodState>
        {
            public byte phase;
            public float phaseStart;     // 서버 시각
            public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
            {
                s.SerializeValue(ref phase);
                s.SerializeValue(ref phaseStart);
            }
            public bool Equals(FloodState o) => phase == o.phase && phaseStart == o.phaseStart;
        }

        private readonly NetworkVariable<FloodState> m_State =
            new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private float m_NextFloodAt;    // 서버 전용: 다음 방류 예고 시각

        /// <summary>이 맵에서 물길이 켜져 있으면 인스턴스(플레이어·발굴터 쪽 접근용). 아니면 null.</summary>
        public static WaterGateNetwork Instance { get; private set; }

        public FloodPhase Phase => (FloodPhase)m_State.Value.phase;

        /// <summary>지금 물이 흐르는 중인가(발굴터가 이 값을 보고 잠긴다). 기믹이 없는 맵에선 항상 false.</summary>
        public static bool IsFlooding => Instance != null && Instance.Phase == FloodPhase.Flowing;

        /// <summary>물이 '방금 빠졌다'는 통지 — 발굴터가 새 유구를 드러내는 트리거로 쓴다(서버에서만 발생).</summary>
        public static event System.Action FloodEnded;

        protected override void OnGimmickSpawn()
        {
            Instance = this;
            m_State.OnValueChanged += OnStateChanged;
            if (IsServer) ScheduleNext();
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this) Instance = null;
            m_State.OnValueChanged -= OnStateChanged;
            DestroyVisuals();
        }

        /// <summary>재시작용(서버): 흐르는 중이면 끊고 새 주기로.</summary>
        public void ServerReset()
        {
            if (!IsServer || !Active) return;
            m_State.Value = new FloodState { phase = (byte)FloodPhase.Idle, phaseStart = Now };
            ScheduleNext();
        }

        private void ScheduleNext() =>
            m_NextFloodAt = Now + Random.Range(Config.FloodMinInterval, Config.FloodMaxInterval);

        private void Update()
        {
            if (!Active || !IsSpawned) return;
            RefreshMarkers(DdpSpots.WaterChannelPrefix, m_Path, 2);
            if (IsServer) ServerTick();
            UpdateVisuals();
        }

        private void ServerTick()
        {
            var s = m_State.Value;
            float elapsed = Now - s.phaseStart;

            switch ((FloodPhase)s.phase)
            {
                case FloodPhase.Idle:
                    if (Loop != null && Loop.IsBuilding && Now >= m_NextFloodAt && m_Path.Count >= 2)
                    {
                        s.phase = (byte)FloodPhase.Warning;
                        s.phaseStart = Now;
                        m_State.Value = s;
                    }
                    break;

                case FloodPhase.Warning:
                    if (elapsed >= Config.FloodWarnSeconds)
                    {
                        s.phase = (byte)FloodPhase.Flowing;
                        s.phaseStart = Now;
                        m_State.Value = s;
                    }
                    break;

                case FloodPhase.Flowing:
                    FloatMaterialsDownstream();
                    if (elapsed >= Config.FloodDurationSeconds || (Loop != null && !Loop.IsBuilding))
                    {
                        s.phase = (byte)FloodPhase.Idle;
                        s.phaseStart = Now;
                        m_State.Value = s;
                        ScheduleNext();
                        FloodEnded?.Invoke();   // 발굴터: 물이 빠졌으니 새 유구를 드러내라
                    }
                    break;
            }
        }

        // ── 경로 ───────────────────────────────────────────────────────────
        private readonly List<Vector3> m_Path = new();

        /// <summary>수로 위(반경·높이 안)인가. nearest/flowDir는 그 지점의 물길 정보.</summary>
        private bool InChannel(Vector3 world, out Vector3 flowDir)
        {
            flowDir = Vector3.forward;
            if (!NearestOnPath(m_Path, world, out var nearest, out flowDir, out float dist)) return false;
            if (dist > Config.ChannelRadius) return false;
            // 수면 기준 위아래 — 다리 위나 데크 위처럼 높은 곳은 물에 안 닿는다.
            return Mathf.Abs(world.y - nearest.y) <= Config.ChannelHeight;
        }

        // ── 재료 급송(서버): 수로에 놓인 바닥 재료를 하류로 흘려보낸다 ─────────
        // 위험을 감수하고 재료를 물에 넣으면 걸어 옮기는 것보다 빨리 건축장 쪽에 도착한다.
        private IPickupField m_Drop;   // 급송에 필요한 계약만(CollectWithin·ServerFloat) — GridInterfaces.cs 채택 규약
        private readonly List<ulong> m_FloatIds = new();
        private readonly List<Vector3> m_FloatPos = new();
        private float m_NextFloatTick;

        private void FloatMaterialsDownstream()
        {
            if (!Config.CarryMaterials || m_Path.Count < 2) return;
            if (Time.time < m_NextFloatTick) return;
            const float kTick = 0.2f;
            m_NextFloatTick = Time.time + kTick;

            if (m_Drop == null) m_Drop = FindFirstObjectByType<MaterialDropField>();
            if (m_Drop == null) return;

            // 경로 중간쯤을 기준으로 넉넉히 훑고, 실제 수로 판정은 InChannel이 한다.
            var mid = m_Path[m_Path.Count / 2];
            float sweep = PathLength(m_Path) * 0.5f + Config.ChannelRadius + 2f;
            m_Drop.CollectWithin(mid, sweep, m_FloatIds, m_FloatPos);

            float step = Config.MaterialFloatSpeed * kTick;
            for (int i = 0; i < m_FloatIds.Count; i++)
            {
                if (!InChannel(m_FloatPos[i], out var dir)) continue;
                m_Drop.ServerFloat(m_FloatIds[i], dir, step);
            }
        }

        // ── 플레이어 밀림 + 스턴(오너 로컬 폴링 — GustNetwork.CurrentPushAt과 동일 계약) ──
        private bool m_LocalWasSwept;   // 로컬 플레이어가 물에 잠긴 중(벗어나는 순간 스턴)
        private float m_PendingStun;    // 벗어날 때 걸어야 할 스턴(초) — PlayerUnit이 Consume해서 적용

        /// <summary>지금 이 위치의 플레이어가 받아야 할 수평 밀림 속도(m/s). 물길 밖이면 zero.</summary>
        public static Vector3 CurrentPushAt(Transform player)
        {
            var w = Instance;
            return w != null ? w.LocalPush(player) : Vector3.zero;
        }

        /// <summary>휩쓸린 뒤 걸어야 할 스턴(초)을 1회 소비. 없으면 0.
        /// (GridSystem 어셈블리는 Player를 모름 — 스턴 적용은 PlayerUnit이 폴링해서 담당.)</summary>
        public static float ConsumePendingStun()
        {
            var w = Instance;
            if (w == null || w.m_PendingStun <= 0f) return 0f;
            float s = w.m_PendingStun;
            w.m_PendingStun = 0f;
            return s;
        }

        private Vector3 LocalPush(Transform player)
        {
            if (!Active || Phase != FloodPhase.Flowing)
            {
                m_LocalWasSwept = false;
                return Vector3.zero;
            }

            if (InChannel(player.position, out var flowDir))
            {
                m_LocalWasSwept = true;
                return flowDir * Config.CurrentSpeed;   // 하류 방향으로 그대로 쓸려간다
            }

            // 물 밖으로 나온 순간 스턴 예약(떠내려가 처박힘) — PlayerUnit이 소비해 적용
            if (m_LocalWasSwept)
            {
                m_LocalWasSwept = false;
                m_PendingStun = Config.SweptStunSeconds;
            }
            return Vector3.zero;
        }

        // ── 연출(전 클라 로컬): 예고 토스트 + 수면 + 수문 물보라 ──────────────
        private readonly List<GameObject> m_WaterQuads = new();
        private static readonly Color kWaterColor = new Color(0.25f, 0.68f, 0.95f, 1f);
        private static readonly Color kWarnColor  = new Color(0.35f, 0.78f, 1.00f, 1f);

        private float m_DrainStart = -999f;   // 방류 종료 시각(로컬) — 물이 '스르륵 빠지는' 연출 기준

        private void OnStateChanged(FloodState prev, FloodState next)
        {
            var phase = (FloodPhase)next.phase;

            if (phase == FloodPhase.Warning)
            {
                var nm = NetworkManager.Singleton;
                var po = nm != null && nm.LocalClient != null ? nm.LocalClient.PlayerObject : null;
                if (po != null)
                    GridJuice.WorldToast(po.transform.position + Vector3.up * 2.6f, "🌊 수문이 열립니다!", kWarnColor);
                // TODO(사운드팀): 수문 개방 경보 + 물소리 SFX — GridSoundBridge에 전용 이름 추가 후 여기서 호출
            }
            else if (phase == FloodPhase.Flowing && m_Splash != null)
            {
                m_Splash.Emit(90);   // 수문이 터지는 첫 물보라 — "콸콸"의 순간
            }
            else if (phase == FloodPhase.Idle && (FloodPhase)prev.phase == FloodPhase.Flowing)
            {
                m_DrainStart = Time.time;   // 뿅 사라지지 않게 — 상류부터 빠져나가는 연출 시작
            }
        }

        // 물살 전선(front) 연출 상수 — 물이 '뿅' 나타나지 않고 상류에서 하류로 뿜어져 퍼지고,
        // 끝나면 상류부터 스르륵 빠진다(09/01 피드백).
        private const float kFrontSpeed = 16f;    // 전선이 하류로 퍼지는 속도(m/s) — 플레이어보다 빠르게
        private const float kDrainSeconds = 1.4f; // 방류 종료 후 물이 다 빠질 때까지

        // 수로를 따라 물판을 깐다. 방류 중엔 전선이 하류로 퍼지고, 끝나면 상류부터 빠진다.
        // ⚠ 예고(Warning) 중엔 물판을 아예 안 깐다 — 얕게 깔았더니 "차기도 전에 하늘색으로 꽉 찬" 것처럼
        //   보였다(09/01). 예고 신호는 토스트 + 수문 잔뿌림 파티클이 담당한다.
        private void UpdateVisuals()
        {
            var phase = Phase;
            bool flowing = phase == FloodPhase.Flowing;
            bool draining = phase == FloodPhase.Idle && Time.time - m_DrainStart < kDrainSeconds;
            bool show = (flowing || draining) && m_Path.Count >= 2;

            // 예고 잔뿌림은 물판과 별개로 돌린다(물판이 꺼져 있어도 수문 앞은 칙칙 튄다)
            if (phase == FloodPhase.Warning && m_Splash == null && m_Path.Count >= 2) BuildSplash();
            if (!show)
            {
                foreach (var q in m_WaterQuads) if (q != null) q.SetActive(false);
                if (m_Splash != null)
                {
                    var em0 = m_Splash.emission;
                    em0.rateOverTime = phase == FloodPhase.Warning ? 7f : 0f;
                }
                return;
            }

            if (m_WaterQuads.Count == 0) BuildWaterQuads();

            float elapsed = Now - m_State.Value.phaseStart;

            var c = kWaterColor;
            c.a = 0.8f;
            UpdateWaterMaterial(c, flowing);

            // 전선 위치(경로 시작점 기준 거리)
            float floodFront = flowing ? elapsed * kFrontSpeed : float.MaxValue;      // 물이 도달한 지점
            float drainFront = draining ? (Time.time - m_DrainStart) * kFrontSpeed * 1.1f : 0f;   // 물이 빠진 지점

            for (int i = 0; i < m_WaterQuads.Count; i++)
            {
                var q = m_WaterQuads[i];
                if (q == null) continue;

                float segStart = m_QuadStart[i];
                float segLen = m_QuadLen[i];

                // 이 세그먼트에서 물이 차 있는 구간 [from..to] (0~1, 상류→하류)
                float from = 0f, to = 1f;
                if (flowing)      to   = Mathf.Clamp01((floodFront - segStart) / segLen);   // 전선이 지나간 만큼만
                else if (draining) from = Mathf.Clamp01((drainFront - segStart) / segLen);  // 상류부터 빈다
                float vis = to - from;

                if (vis <= 0.001f) { q.SetActive(false); continue; }
                if (!q.activeSelf) q.SetActive(true);

                // 보이는 구간만큼 길이를 줄이고, 그 구간의 중앙에 앉힌다(상류/하류 끝에서 자라거나 줄어들게)
                float mid01 = (from + to) * 0.5f;
                var pos = m_QuadA[i] + m_QuadDir[i] * (segLen * mid01);

                float height = 0.3f;
                float wave = flowing ? Mathf.Sin((Time.time * 4.5f) - (segStart + segLen * mid01) * 0.35f) * 0.07f : 0f;
                // 전선 바로 뒤는 살짝 부풀어 '밀려오는 물머리'처럼
                if (flowing && to < 1f) height *= 1.35f;

                q.transform.position = new Vector3(pos.x, m_QuadBaseY[i] + wave, pos.z);
                q.transform.localScale = new Vector3(Config.ChannelRadius * 2f, Mathf.Max(0.02f, height), Mathf.Max(0.05f, segLen * vis));
            }

            // 수문 물보라: 흐르는 동안 세게 뿜는다(예고 잔뿌림은 위 분기에서 처리).
            if (m_Splash == null) BuildSplash();
            if (m_Splash != null)
            {
                var em = m_Splash.emission;
                em.rateOverTime = flowing ? 45f : 0f;
            }
        }

        private readonly List<float> m_QuadBaseY = new();
        private readonly List<float> m_QuadStart = new();   // 경로 시작점부터 이 세그먼트 시작까지 거리
        private readonly List<float> m_QuadLen = new();
        private readonly List<Vector3> m_QuadA = new();     // 세그먼트 상류 끝
        private readonly List<Vector3> m_QuadDir = new();   // 상류→하류 단위 방향

        private void BuildWaterQuads()
        {
            m_QuadBaseY.Clear(); m_QuadStart.Clear(); m_QuadLen.Clear(); m_QuadA.Clear(); m_QuadDir.Clear();
            float dist = 0f;
            for (int i = 1; i < m_Path.Count; i++)
            {
                var a = m_Path[i - 1];
                var b = m_Path[i];
                var seg = b - a;
                float len = seg.magnitude;
                if (len < 0.01f) continue;

                var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                box.name = "~DdpWater";
                var col = box.GetComponent<Collider>();
                if (col != null) Destroy(col);   // 물은 통과 — 밀림은 CurrentPushAt이 담당
                EnsureWaterMaterial();
                if (s_Mat != null) box.GetComponent<Renderer>().sharedMaterial = s_Mat;

                var mid = (a + b) * 0.5f;
                box.transform.position = mid;
                box.transform.rotation = Quaternion.LookRotation(seg.normalized, Vector3.up);
                box.transform.localScale = new Vector3(Config.ChannelRadius * 2f, 0.2f, len);
                m_WaterQuads.Add(box);
                m_QuadBaseY.Add(mid.y);
                m_QuadStart.Add(dist);
                m_QuadLen.Add(len);
                m_QuadA.Add(a);
                m_QuadDir.Add(seg.normalized);
                dist += len;
            }
        }

        private void DestroyVisuals()
        {
            foreach (var q in m_WaterQuads) if (q != null) Destroy(q);
            m_WaterQuads.Clear();
            m_QuadBaseY.Clear(); m_QuadStart.Clear(); m_QuadLen.Clear(); m_QuadA.Clear(); m_QuadDir.Clear();
            if (m_Splash != null) { Destroy(m_Splash.gameObject); m_Splash = null; }
        }

        // ── 물 머티리얼(공유 1장) ──────────────────────────────────────────
        // 예전엔 불투명 Lit + MaterialPropertyBlock 틴트였는데, SRP Batcher가 MPB를 무시해
        // '회색 민짜 박스'로 보였다(프로젝트 관례 주석 참고). 반투명 + 에미션(야경 대응)으로 교체하고
        // 색은 공유 머티리얼에 직접 쓴다 — 물판 전부가 같은 색이라 이걸로 충분하다.
        private static Material s_Mat;

        private static void EnsureWaterMaterial()
        {
            if (s_Mat != null) return;
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) return;
            s_Mat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            s_Mat.SetFloat("_Surface", 1f);   // Transparent
            s_Mat.SetFloat("_Blend", 0f);     // Alpha
            s_Mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            s_Mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            s_Mat.SetFloat("_ZWrite", 0f);
            s_Mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            s_Mat.SetOverrideTag("RenderType", "Transparent");
            s_Mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            s_Mat.EnableKeyword("_EMISSION");   // 밤 맵에서 물이 은은히 빛난다(변형은 가로등 에셋이 실어 나름)
            s_Mat.SetFloat("_Smoothness", 0.85f);
        }

        private void UpdateWaterMaterial(Color c, bool flowing)
        {
            EnsureWaterMaterial();
            if (s_Mat == null) return;
            s_Mat.SetColor("_BaseColor", c);
            s_Mat.SetColor("_EmissionColor", new Color(c.r, c.g, c.b) * (flowing ? 0.55f : 0.25f));
        }

        // ── 수문 물보라(파티클) — "물 콸콸" 담당. 상류(경로 0번) 수문 입에서 하류로 뿜는다 ──
        private ParticleSystem m_Splash;

        private void BuildSplash()
        {
            if (m_Path.Count < 2) return;
            var origin = m_Path[0];
            var dir = (m_Path[1] - m_Path[0]).normalized;

            var go = new GameObject("~DdpGateSplash");
            go.transform.position = origin + dir * 0.6f + Vector3.up * 0.9f;   // 수문 물구멍 높이쯤
            go.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);

            m_Splash = go.AddComponent<ParticleSystem>();
            var main = m_Splash.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.0f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(3.5f, 7f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.18f, 0.55f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.85f, 0.97f, 1f, 0.9f), new Color(0.45f, 0.8f, 1f, 0.8f));
            main.gravityModifier = 1.1f;
            main.maxParticles = 400;

            var shape = m_Splash.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 22f;
            shape.radius = 0.45f;
            // Cone은 로컬 +z로 뿜는다 — go의 회전이 하류를 보고 있으니 그대로.

            var em = m_Splash.emission;
            em.rateOverTime = 0f;   // 페이즈에 따라 UpdateVisuals가 조절

            var col = m_Splash.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.5f, 0.8f, 1f), 1f) },
                new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;

            var r = m_Splash.GetComponent<ParticleSystemRenderer>();
            r.material = BuildSplashMaterial();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        // 파티클용 부드러운 원형 스프라이트 — 에셋 없이 코드로 굽는다(URP Particles Unlit + 가산).
        private static Material s_SplashMat;
        private static Material BuildSplashMaterial()
        {
            if (s_SplashMat != null) return s_SplashMat;
            var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
            s_SplashMat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };

            const int N = 32;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), new Vector2(N / 2f, N / 2f)) / (N / 2f);
                    byte v = (byte)(Mathf.Pow(Mathf.Clamp01(1f - d), 1.6f) * 255f);
                    px[y * N + x] = new Color32(255, 255, 255, v);
                }
            tex.SetPixels32(px);
            tex.Apply();

            s_SplashMat.SetTexture("_BaseMap", tex);
            s_SplashMat.SetColor("_BaseColor", Color.white);
            s_SplashMat.SetFloat("_Surface", 1f);
            s_SplashMat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            s_SplashMat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);   // 가산 섞임 — 물보라 반짝임
            s_SplashMat.SetFloat("_ZWrite", 0f);
            s_SplashMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            s_SplashMat.SetOverrideTag("RenderType", "Transparent");
            s_SplashMat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return s_SplashMat;
        }
    }
}
