using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace GridSystem
{
    /// <summary>
    /// 사방신 석상 기믹(서버 권위) — 기획서(08/27):
    /// · 건축 진행도 20/30/45/60% 도달마다 석상 1개가 빛기둥과 함께 광장 중앙(GuardianDropPoint)에 낙하
    /// · 석상 = MaterialDef(IsHeavy) 재료 — 들기/2인 운반/던지기는 기존 운반 시스템이 공짜로 처리
    /// · 받침대(Pedestal_East/West/South/North) 근처에 놓으면: 맞는 방위 → 안착 + 정령 등장 + 그 방위 절반 화재 면역
    ///   틀린 방위 → 튕겨냄 + 효과음 (벌점 없음)
    /// · 4개 완성 → 화마 완전 봉인(FireNetwork가 IsSealed를 본다)
    /// 방위 매핑: 동=청룡·서=백호·남=주작·북=현무 (Config.StatueMaterialIds 순서).
    /// </summary>
    public class GuardianNetwork : GyeongbokgungGimmickBase
    {
        public static GuardianNetwork Instance { get; private set; }

        private static readonly string[] kPedestalNames = { "Pedestal_East", "Pedestal_West", "Pedestal_South", "Pedestal_North" };
        private static readonly string[] kKindNames = { "청룡", "백호", "주작", "현무" };
        private static readonly Color[] kKindColors =
        {
            new Color(0.30f, 0.55f, 1.00f),   // 동 청룡 靑
            new Color(0.95f, 0.95f, 1.00f),   // 서 백호 白
            new Color(1.00f, 0.35f, 0.30f),   // 남 주작 赤
            new Color(0.25f, 0.22f, 0.35f),   // 북 현무 黑
        };

        // 비트 i(0~3) = 해당 방위 석상 안착됨. 복제 한 개로 전 클라 정령/면역 상태 동기화.
        private readonly NetworkVariable<int> m_PlacedMask =
            new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        // 낙하한 석상 수(서버 전용 진행 문턱 소비용이지만, HUD 확장 대비 복제로 둔다)
        private readonly NetworkVariable<int> m_DroppedCount =
            new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private MaterialDropField m_Drop;
        private readonly Transform[] m_Pedestals = new Transform[4];
        private Transform m_DropPoint;
        private readonly List<PickupEntry> m_Scratch = new();
        private readonly GameObject[] m_Spirits = new GameObject[4];
        private float m_NextScanAt;
        private float m_NextDropAllowedAt;   // 서버 전용 — 낙하 최소 간격

        /// <summary>4방위 전부 안착 — 화마 봉인. FireNetwork가 매 틱 조회한다(null-safe).</summary>
        public static bool IsSealed => Instance != null && Instance.Active && Instance.m_PlacedMask.Value == 0b1111;

        /// <summary>이 셀이 정령 보호(화재 면역) 구역인가. 방위별로 그리드 절반을 보호한다.</summary>
        public static bool IsCellImmune(Vector3Int cell)
        {
            if (Instance == null || !Instance.Active) return false;
            int mask = Instance.m_PlacedMask.Value;
            if (mask == 0) return false;
            var size = Instance.Grid != null ? Instance.Grid.EffectiveSize : new Vector3Int(30, 13, 20);
            float cx = size.x * 0.5f, cz = size.z * 0.5f;
            if ((mask & (1 << 0)) != 0 && cell.x >= cx) return true;   // 동
            if ((mask & (1 << 1)) != 0 && cell.x < cx) return true;    // 서
            if ((mask & (1 << 2)) != 0 && cell.z < cz) return true;    // 남
            if ((mask & (1 << 3)) != 0 && cell.z >= cz) return true;   // 북
            return false;
        }

        protected override void Awake()
        {
            base.Awake();
            m_Drop = GetComponent<MaterialDropField>();
        }

        protected override void OnGimmickSpawn()
        {
            Instance = this;
            m_PlacedMask.OnValueChanged += OnPlacedChanged;
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this) Instance = null;
            m_PlacedMask.OnValueChanged -= OnPlacedChanged;
            for (int i = 0; i < 4; i++) if (m_Spirits[i] != null) Destroy(m_Spirits[i]);
        }

        public void ServerReset()
        {
            if (!IsServer || !Active) return;
            m_PlacedMask.Value = 0;
            m_DroppedCount.Value = 0;
            m_NextDropAllowedAt = 0f;
            // 바닥 석상 픽업은 MaterialDropField.ServerReset()이 함께 정리한다.
        }

        private void Update()
        {
            if (!Active || !IsSpawned) return;
            RefreshMarkers();
            if (IsServer) ServerTick();
            UpdateSpirits();
        }

        private void RefreshMarkers()
        {
            if (m_DropPoint == null) { var t = FindMarker("GuardianDropPoint"); if (t != null) m_DropPoint = t; }
            for (int i = 0; i < 4; i++)
                if (m_Pedestals[i] == null)
                {
                    var go = GameObject.Find(kPedestalNames[i]);
                    if (go != null) m_Pedestals[i] = go.transform;
                }
        }

        // ── 서버: 진행도 문턱 낙하 + 받침대 안착/거절 판정 ──────────────────
        private void ServerTick()
        {
            if (Loop == null || !Loop.IsBuilding) return;

            // ① 진행도 문턱 → 석상 낙하 (순서대로 하나씩. ScorePercent는 이미 0~100 스케일!)
            // 진행도가 한 번에 여러 문턱을 뛰어넘어도 최소 간격을 두고 한 개씩만 떨어뜨린다(우르르 방지).
            int dropped = m_DroppedCount.Value;
            var percents = Config.StatueDropPercents;
            var ids = Config.StatueMaterialIds;
            if (m_Drop != null && m_DropPoint != null &&
                dropped < Mathf.Min(percents.Length, ids.Length) &&
                Net != null && Net.ScorePercent >= percents[dropped] &&
                Now >= m_NextDropAllowedAt)
            {
                Vector3 to = m_DropPoint.position;
                Vector3 from = to + new Vector3(Random.Range(-1f, 1f), 26f, Random.Range(-1f, 1f));
                m_Drop.ServerDeliver(ids[dropped], from, to);
                m_DroppedCount.Value = dropped + 1;
                m_NextDropAllowedAt = Now + Config.StatueDropMinGapSeconds;
                StatueDropFxRpc(to, dropped);
            }

            // ② 받침대 근처의 석상 픽업 스캔 (0.25초 간격이면 충분)
            if (Time.time < m_NextScanAt) return;
            m_NextScanAt = Time.time + 0.25f;
            if (m_Drop == null) return;

            m_Drop.ServerCollectPickups(m_Scratch);
            foreach (var p in m_Scratch)
            {
                int kind = KindOf(p.materialId);
                if (kind < 0) continue;
                for (int ped = 0; ped < 4; ped++)
                {
                    if (m_Pedestals[ped] == null) continue;
                    Vector3 pp = m_Pedestals[ped].position;
                    Vector3 d = p.pos - pp; d.y = 0f;
                    if (d.magnitude > Config.PedestalSnapRange) continue;

                    if (ped == kind && (m_PlacedMask.Value & (1 << kind)) == 0)
                    {
                        // 안착: 픽업 제거 + 상태 복제(정령은 클라가 마스크 변화로 띄움)
                        m_Drop.ServerRemove(p.pickupId);
                        m_PlacedMask.Value |= 1 << kind;
                        PlacedFxRpc(pp, kind, m_PlacedMask.Value == 0b1111);
                    }
                    else if (ped != kind)
                    {
                        // 틀린 받침대: 튕겨냄 (벌점 없음)
                        m_Drop.ServerRemove(p.pickupId);
                        Vector3 dir = d.sqrMagnitude > 0.01f ? d.normalized : Random.insideUnitSphere.WithY0().normalized;
                        m_Drop.ServerThrow(p.materialId, pp + Vector3.up * 1.2f, pp + dir * Config.RejectBounceDistance);
                        RejectFxRpc(pp, kind);
                    }
                    break;   // 이 픽업은 처리 끝(제거됨) — 다음 픽업으로
                }
            }
        }

        private int KindOf(int materialId)
        {
            var ids = Config.StatueMaterialIds;
            for (int i = 0; i < ids.Length; i++) if (ids[i] == materialId) return i;
            return -1;
        }

        // ── 연출 (전 클라) ──────────────────────────────────────────────
        [Rpc(SendTo.Everyone)]
        private void StatueDropFxRpc(Vector3 pos, int kind)
        {
            LightPillar(pos, kKindColors[kind]);
            GridJuice.WorldToast(pos + Vector3.up * 2.5f, $"사방신 석상이 내려왔다! ({kKindNames[kind]})", new Color(1f, 0.92f, 0.5f));
            GridSoundBridge.PlaySFXAt("LandObject", pos);
        }

        [Rpc(SendTo.Everyone)]
        private void PlacedFxRpc(Vector3 pos, int kind, bool sealedNow)
        {
            LightPillar(pos, kKindColors[kind]);
            GridJuice.GroundHit(pos, 1.1f);
            GridJuice.WorldToast(pos + Vector3.up * 2.2f, $"{kKindNames[kind]}이(가) 깨어났다!", kKindColors[kind]);
            GridSoundBridge.PlaySFXAt("LandObject", pos);
            if (sealedNow)
            {
                GridJuice.FovPunch(Camera.main, -4f);
                GridJuice.WorldToast(pos + Vector3.up * 3.4f, "사방신의 힘이 화마를 억누른다!", new Color(0.55f, 0.9f, 1f));
            }
        }

        [Rpc(SendTo.Everyone)]
        private void RejectFxRpc(Vector3 pos, int kindOnPedestal)
        {
            GridJuice.WorldToast(pos + Vector3.up * 2f, "방위가 다르다…!", new Color(1f, 0.55f, 0.35f));
            GridSoundBridge.PlaySFXAt("BumpPlayers", pos);
        }

        // 절차 생성 빛기둥 — 세로로 긴 발광 기둥이 2초에 걸쳐 사라진다.
        private static void LightPillar(Vector3 basePos, Color c)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "~LightPillar";
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.position = basePos + Vector3.up * 14f;
            go.transform.localScale = new Vector3(1.4f, 28f, 1.4f);
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = MakeGlow(new Color(c.r, c.g, c.b, 0.45f));
            var fade = go.AddComponent<PillarFade>();
            fade.Life = 2.2f;
        }

        // URP Unlit 가산 발광 재질(ItemFx.GlowMat 패턴 — 색만 인스턴스별로 박음)
        internal static Material MakeGlow(Color c)
        {
            var sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            var m = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            m.SetFloat("_Surface", 1f);
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
            m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            m.SetColor("_BaseColor", c);
            m.SetColor("_Color", c);
            return m;
        }

        private class PillarFade : MonoBehaviour
        {
            public float Life = 2f;
            private float m_T;
            private void Update()
            {
                m_T += Time.deltaTime;
                float k = 1f - Mathf.Clamp01(m_T / Life);
                transform.localScale = new Vector3(1.4f * k, 28f, 1.4f * k);
                if (m_T >= Life) Destroy(gameObject);
            }
        }

        // ── 정령(클라 로컬) — 안착된 방위 위에 둥둥. 전용 모델(Resources/Gyeongbokgung/Spirit_*)이 있으면 그걸,
        //    없으면 발광 구 플레이스홀더(유저가 정령 모델 제작 중 — 나오면 Resources에 넣기만 하면 됨). ──
        private void OnPlacedChanged(int _, int __) { /* UpdateSpirits가 다음 프레임에 반영 */ }

        private static readonly string[] kSpiritRes = { "Gyeongbokgung/Spirit_Cheongryong", "Gyeongbokgung/Spirit_Baekho", "Gyeongbokgung/Spirit_Jujak", "Gyeongbokgung/Spirit_Hyeonmu" };

        private void UpdateSpirits()
        {
            int mask = m_PlacedMask.Value;
            for (int i = 0; i < 4; i++)
            {
                bool want = (mask & (1 << i)) != 0 && m_Pedestals[i] != null;
                if (want && m_Spirits[i] == null)
                    m_Spirits[i] = BuildSpirit(i, m_Pedestals[i].position + Vector3.up * 2.6f);
                else if (!want && m_Spirits[i] != null)
                {
                    Destroy(m_Spirits[i]);
                    m_Spirits[i] = null;
                }
            }
        }

        private GameObject BuildSpirit(int kind, Vector3 pos)
        {
            var prefab = Resources.Load<GameObject>(kSpiritRes[kind]);
            GameObject go;
            if (prefab != null)
            {
                go = Instantiate(prefab, pos, Quaternion.identity);
            }
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(go.GetComponent<Collider>());
                go.transform.position = pos;
                go.transform.localScale = Vector3.one * 1.1f;
                var r = go.GetComponent<Renderer>();
                r.sharedMaterial = MakeGlow(new Color(kKindColors[kind].r, kKindColors[kind].g, kKindColors[kind].b, 0.75f));
            }
            go.name = $"~Spirit_{kKindNames[kind]}";
            go.AddComponent<JuiceBob>();
            return go;
        }
    }

    internal static class GuardianVecExt
    {
        public static Vector3 WithY0(this Vector3 v) { v.y = 0f; return v; }
    }
}
