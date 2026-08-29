using System.Collections.Generic;
using SeoulZikimi.Weather;
using UnityEngine;
using UnityEngine.Rendering;

namespace GridSystem
{
    /// <summary>
    /// 바닥 날씨 연출(로컬 전용). 비=물웅덩이, 눈=눈 쌓임 + 달팽이 미끄러진 자국, 단풍/벚꽃=바닥에 흩뿌려진 잎.
    /// 공중 파티클은 Weather3DVfxRig가, 바닥은 여기가 맡는다. 게임플레이 판정과 무관하다.
    /// 배치 범위는 GridManager(원점+크기)를 따르고, 없으면 카메라 앞 바닥을 쓴다.
    /// </summary>
    public sealed class WeatherGroundFx : MonoBehaviour
    {
        private const string KitPath = "UI_NEW/Weather/3D/WeatherGroundKit";
        private const float Margin = 2.5f;          // 그리드 바깥으로 이 만큼 더 뿌린다
        private const float GroundTolerance = 0.6f; // 원점보다 이보다 높은 면(블록 위)에는 안 놓는다
        private const float BoundsPollInterval = 2f;
        private const float PlayerPollInterval = 1f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int BaseMapStId = Shader.PropertyToID("_BaseMap_ST");

        private sealed class TrailMark
        {
            public Transform Transform;
            public MeshRenderer Renderer;
            public float SpawnTime;
        }

        private WeatherGroundKit m_Kit;
        private WeatherKind m_Weather = WeatherKind.Sunny;
        private Transform m_ScatterRoot;
        private Transform m_TrailRoot;
        private readonly List<TrailMark> m_Trails = new();
        // 수명 다한 자국은 파괴하지 않고 꺼서 여기 쌓아뒀다 재사용 — 이동 중 초당 수십 개
        // GameObject 생성·파괴 churn을 없앤다. 크기는 TrailMax로 자연 제한된다.
        private readonly Stack<TrailMark> m_TrailPool = new();
        private readonly Dictionary<Transform, Vector3> m_LastPlayerPos = new();
        private readonly List<Transform> m_Players = new();
        private readonly RaycastHit[] m_Hits = new RaycastHit[8];
        private MaterialPropertyBlock m_Block;

        private GridManager m_Grid;
        private Bounds m_PlacedBounds;
        private bool m_HasPlaced;
        private float m_NextBoundsPoll;
        private float m_NextPlayerPoll;
        private bool m_MissingKitLogged;

        public void SetWeather(WeatherKind weather)
        {
            if (m_Weather == weather && m_HasPlaced) return;
            m_Weather = weather;
            Rescatter();
            RefreshBlockCaps();
        }

        // 2vs2 아이템 날씨: 당한 팀 진영에만 뿌리도록 범위 제한(null = 맵 전체). TeamWeatherFx가 넣어준다.
        private Bounds? m_AreaOverride;
        private static Bounds? s_CapArea;   // 블록 윗면 덮개(DecorateBlockTop)도 같은 범위를 본다
        public void SetAreaOverride(Bounds? area)
        {
            if (m_AreaOverride == area) return;
            m_AreaOverride = area;
            s_CapArea = area;
            if (m_Weather != WeatherKind.Sunny) { Rescatter(); RefreshBlockCaps(); }
        }

        private static bool CapInArea(Vector3 p)
        {
            if (!s_CapArea.HasValue) return true;
            var b = s_CapArea.Value;
            return p.x >= b.min.x && p.x <= b.max.x && p.z >= b.min.z && p.z <= b.max.z;
        }

        // ───────────────────────── 건축물 윗면 덮개(눈/웅덩이) ─────────────────────────
        // 배치 블록(평평한 윗면 = Walkable)마다 눈 덮개 쿼드 + 웅덩이 디스크를 자식으로 미리 만들어 두고 날씨에 따라 켠다.
        // 블록과 함께 생기고 사라지니 공중에 뜨는 일 없음. 눈 오는 중에 지으면 지은 것부터 눈이 쌓인 건물이 된다.
        private static WeatherGroundFx s_Instance;
        private static readonly List<GameObject> s_Caps = new();
        private static WeatherKind s_CapWeather = WeatherKind.Sunny;

        private void OnEnable() { s_Instance = this; }
        private void OnDisable() { if (s_Instance == this) s_Instance = null; }

        /// <summary>GridNetwork가 블록 비주얼을 스폰할 때 호출. topCenter = 윗면 중심(월드), sizeXZ = 윗면 크기(월드).</summary>
        public static void DecorateBlockTop(GameObject block, Vector3 topCenter, Vector2 sizeXZ)
        {
            var kit = s_Instance != null && s_Instance.EnsureKit() ? s_Instance.m_Kit : Resources.Load<WeatherGroundKit>(KitPath);
            if (kit == null || block == null) return;
            s_Caps.RemoveAll(g => g == null);

            // 눈: 윗면 거의 전체를 덮는 쿼드(가장자리 살짝 안쪽)
            if (kit.SnowPatch != null && kit.Quad != null)
            {
                var snow = MakeCap(block.transform, "~WeatherCap:Snow", kit.Quad, kit.SnowPatch,
                    topCenter + Vector3.up * 0.03f, new Vector3(sizeXZ.x * 0.94f, 1f, sizeXZ.y * 0.94f));
                snow.SetActive(s_CapWeather == WeatherKind.Snow && CapInArea(topCenter));
                s_Caps.Add(snow);
            }
            // 비/태풍: 윗면 가운데 웅덩이 하나(작은 면은 더 작게)
            if (kit.Puddle != null && kit.Disc != null)
            {
                float d = Mathf.Min(sizeXZ.x, sizeXZ.y) * 0.62f;
                var puddle = MakeCap(block.transform, "~WeatherCap:Puddle", kit.Disc, kit.Puddle,
                    topCenter + Vector3.up * 0.025f, new Vector3(d * 1.25f, 1f, d));
                puddle.transform.Rotate(0f, (topCenter.x * 37f + topCenter.z * 53f) % 360f, 0f, Space.World);
                puddle.SetActive((s_CapWeather == WeatherKind.Rain || s_CapWeather == WeatherKind.Typhoon) && CapInArea(topCenter));
                s_Caps.Add(puddle);
            }
        }

        private static GameObject MakeCap(Transform parent, string name, Mesh mesh, Material material, Vector3 pos, Vector3 scale)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, true);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.identity;
            go.transform.localScale = new Vector3(scale.x / Mathf.Max(1e-4f, parent.lossyScale.x), scale.y / Mathf.Max(1e-4f, parent.lossyScale.y), scale.z / Mathf.Max(1e-4f, parent.lossyScale.z));
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return go;
        }

        private void RefreshBlockCaps()
        {
            s_CapWeather = m_Weather;
            s_Caps.RemoveAll(g => g == null);
            bool snow = m_Weather == WeatherKind.Snow;
            bool wet = m_Weather == WeatherKind.Rain || m_Weather == WeatherKind.Typhoon;
            foreach (var g in s_Caps)
            {
                bool on = g.name.EndsWith("Snow") ? snow : wet;
                g.SetActive(on && CapInArea(g.transform.position));
            }
        }

        private void Awake()
        {
            m_Block = new MaterialPropertyBlock();
            m_ScatterRoot = new GameObject("Scatter").transform;
            m_ScatterRoot.SetParent(transform, false);
            m_TrailRoot = new GameObject("SnowTrail").transform;
            m_TrailRoot.SetParent(transform, false);
        }

        private void Update()
        {
            float now = Time.time;
            if (now >= m_NextBoundsPoll)
            {
                m_NextBoundsPoll = now + BoundsPollInterval;
                // 맵 로드/2vs2 전환으로 그리드가 바뀌면 같은 날씨라도 다시 뿌린다.
                if (m_Weather != WeatherKind.Sunny && TryGetArea(out Bounds area) && area != m_PlacedBounds)
                    Rescatter();
            }

            UpdateTrails(now);
        }

        // ───────────────────────── 흩뿌리기 ─────────────────────────

        private void Rescatter()
        {
            ClearScatter();
            if (!EnsureKit()) return;
            if (m_Weather == WeatherKind.Sunny) return;
            if (!TryGetArea(out Bounds area)) return;

            m_PlacedBounds = area;
            m_HasPlaced = true;
            float areaScale = Mathf.Max(1f, area.size.x * area.size.z / 100f);
            // 같은 날씨면 재배치해도 비슷한 그림이 나오게 고정 시드
            var random = new System.Random((int)m_Weather * 7919 + 17);

            switch (m_Weather)
            {
                case WeatherKind.Rain:
                    ScatterDiscs(random, area, m_Kit.Puddle, m_Kit.Disc,
                        Count(m_Kit.PuddleCount, areaScale), m_Kit.PuddleSize, 0.02f, false);
                    break;
                case WeatherKind.Typhoon:
                    ScatterDiscs(random, area, m_Kit.Puddle, m_Kit.Disc,
                        Count(m_Kit.TyphoonPuddleCount, areaScale), m_Kit.PuddleSize, 0.02f, false);
                    break;
                case WeatherKind.Snow:
                    ScatterDiscs(random, area, m_Kit.SnowPatch, m_Kit.Quad,
                        Count(m_Kit.SnowPatchCount, areaScale), m_Kit.SnowPatchSize, 0.015f, true);
                    ScatterSnowmen(random, area);
                    break;
                case WeatherKind.AutumnLeaves:
                    ScatterLeaves(random, area, Count(m_Kit.LeafCount, areaScale));
                    break;
                case WeatherKind.CherryBlossom:
                    ScatterPetals(random, area, Count(m_Kit.PetalCount, areaScale));
                    break;
            }
        }

        private static int Count(int baseCount, float areaScale) => Mathf.RoundToInt(baseCount * areaScale);

        private void ScatterDiscs(System.Random random, Bounds area, Material material, Mesh mesh,
            int count, Vector2 size, float lift, bool jitterLift)
        {
            for (int i = 0; i < count; i++)
            {
                if (!TryGroundPoint(random, area, out Vector3 point, out Vector3 normal)) continue;
                float s = Lerp(random, size);
                float h = jitterLift ? lift + (float)random.NextDouble() * 0.01f : lift;
                Place(mesh, material, point + normal * h, normal, Yaw(random), new Vector3(s, 1f, s), null);
            }
        }

        private void ScatterLeaves(System.Random random, Bounds area, int count)
        {
            if (m_Kit.Leaves == null || m_Kit.Leaves.Length == 0) return;
            for (int i = 0; i < count; i++)
            {
                if (!TryGroundPoint(random, area, out Vector3 point, out Vector3 normal)) continue;
                Material material = m_Kit.Leaves[random.Next(m_Kit.Leaves.Length)];
                float s = Lerp(random, m_Kit.LeafSize);
                float h = 0.02f + (float)random.NextDouble() * 0.015f;
                Place(m_Kit.Quad, material, point + normal * h, normal, Yaw(random),
                    new Vector3(s * 0.6f, 1f, s), null);
            }
        }

        private void ScatterPetals(System.Random random, Bounds area, int count)
        {
            int tilesX = Mathf.Max(1, m_Kit.PetalTilesX), tilesY = Mathf.Max(1, m_Kit.PetalTilesY);
            for (int i = 0; i < count; i++)
            {
                if (!TryGroundPoint(random, area, out Vector3 point, out Vector3 normal)) continue;
                float s = Lerp(random, m_Kit.PetalSize);
                float h = 0.02f + (float)random.NextDouble() * 0.015f;
                // 2x2 아틀라스에서 꽃잎 하나 고르기
                int tx = random.Next(tilesX), ty = random.Next(tilesY);
                m_Block.Clear();
                m_Block.SetVector(BaseMapStId, new Vector4(1f / tilesX, 1f / tilesY, (float)tx / tilesX, (float)ty / tilesY));
                Place(m_Kit.Quad, m_Kit.Petal, point + normal * h, normal, Yaw(random),
                    new Vector3(s * 0.8f, 1f, s), m_Block);
            }
        }

        /// <summary>눈사람은 건축 영역 바깥(여백 띠)에만 세워 플레이에 안 걸리게 한다.</summary>
        private void ScatterSnowmen(System.Random random, Bounds area)
        {
            if (m_Kit.Snowman == null) return;
            var inner = new Bounds(area.center, area.size - new Vector3(Margin * 2f, 0f, Margin * 2f));
            int placed = 0;
            for (int attempt = 0; attempt < m_Kit.SnowmanCount * 12 && placed < m_Kit.SnowmanCount; attempt++)
            {
                if (!TryGroundPoint(random, area, out Vector3 point, out Vector3 normal)) continue;
                var flat = new Vector3(point.x, inner.center.y, point.z);
                if (inner.Contains(flat)) continue;                 // 그리드 안쪽은 제외
                if (Vector3.Distance(flat, inner.ClosestPoint(flat)) < 0.6f) continue; // 경계에 너무 붙은 것도 제외

                GameObject snowman = Instantiate(m_Kit.Snowman, m_ScatterRoot);
                snowman.name = "snowman";
                float s = Lerp(random, m_Kit.SnowmanScale);
                snowman.transform.localScale = Vector3.one * s;
                // 눈사람은 항상 똑바로 서고, 대략 맵 중앙을 바라본다.
                Vector3 toCenter = inner.center - point; toCenter.y = 0f;
                float yaw = toCenter.sqrMagnitude > 0.01f ? Mathf.Atan2(toCenter.x, toCenter.z) * Mathf.Rad2Deg : Yaw(random);
                snowman.transform.SetPositionAndRotation(point, Quaternion.AngleAxis(yaw + (float)(random.NextDouble() * 40 - 20), Vector3.up));
                placed++;
            }
        }

        private MeshRenderer Place(Mesh mesh, Material material, Vector3 position, Vector3 normal,
            float yaw, Vector3 scale, MaterialPropertyBlock block)
        {
            var go = new GameObject("decal");
            go.transform.SetParent(m_ScatterRoot, false);
            go.transform.SetPositionAndRotation(position,
                Quaternion.FromToRotation(Vector3.up, normal) * Quaternion.AngleAxis(yaw, Vector3.up));
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            if (block != null) renderer.SetPropertyBlock(block);
            return renderer;
        }

        private void ClearScatter()
        {
            for (int i = m_ScatterRoot.childCount - 1; i >= 0; i--)
                Destroy(m_ScatterRoot.GetChild(i).gameObject);
            m_HasPlaced = false;
        }

        // ───────────────────────── 눈 자국 ─────────────────────────

        private void UpdateTrails(float now)
        {
            if (m_Weather == WeatherKind.Snow && m_Kit != null)
            {
                if (now >= m_NextPlayerPoll)
                {
                    m_NextPlayerPoll = now + PlayerPollInterval;
                    RefreshPlayers();
                }
                foreach (Transform player in m_Players)
                {
                    if (player == null) continue;
                    Vector3 pos = player.position;
                    if (!m_LastPlayerPos.TryGetValue(player, out Vector3 last))
                    {
                        m_LastPlayerPos[player] = pos;
                        continue;
                    }
                    Vector3 delta = pos - last;
                    delta.y = 0f;
                    if (delta.sqrMagnitude < m_Kit.TrailStride * m_Kit.TrailStride) continue;
                    m_LastPlayerPos[player] = pos;
                    SpawnTrail(pos, delta.normalized, now);
                }
            }

            if (m_Trails.Count == 0) return;
            float life = m_Kit != null ? m_Kit.TrailLifetime : 10f;
            float fade = m_Kit != null ? Mathf.Max(0.01f, m_Kit.TrailFadeDuration) : 3f;
            for (int i = m_Trails.Count - 1; i >= 0; i--)
            {
                TrailMark mark = m_Trails[i];
                if (mark.Transform == null) { m_Trails.RemoveAt(i); continue; }
                float age = now - mark.SpawnTime;
                if (age >= life)
                {
                    RecycleTrail(mark);
                    m_Trails.RemoveAt(i);
                    continue;
                }
                float alpha = Mathf.Clamp01((life - age) / fade);
                if (alpha < 1f)
                {
                    Color color = m_Kit.SnowTrail.HasProperty(BaseColorId) ? m_Kit.SnowTrail.GetColor(BaseColorId) : Color.white;
                    color.a *= alpha;
                    m_Block.Clear();
                    m_Block.SetColor(BaseColorId, color);
                    mark.Renderer.SetPropertyBlock(m_Block);
                }
            }
        }

        private void SpawnTrail(Vector3 playerPos, Vector3 direction, float now)
        {
            if (!CapInArea(playerPos)) return;   // 눈 자국도 제한 범위 안에서만
            if (!RaycastGround(playerPos + Vector3.up * 1.5f, 4f, out Vector3 point, out Vector3 normal))
                return;
            if (m_Trails.Count >= Mathf.Max(1, m_Kit.TrailMax))
            {
                RecycleTrail(m_Trails[0]);
                m_Trails.RemoveAt(0);
            }

            // 풀에서 재사용(씬 전환 등으로 파괴된 항목은 건너뜀), 없으면 새로 만든다.
            TrailMark mark = null;
            while (m_TrailPool.Count > 0)
            {
                var candidate = m_TrailPool.Pop();
                if (candidate.Transform != null) { mark = candidate; break; }
            }
            if (mark == null)
            {
                var go = new GameObject("trail");
                go.transform.SetParent(m_TrailRoot, false);
                go.AddComponent<MeshFilter>().sharedMesh = m_Kit.Quad;
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = m_Kit.SnowTrail;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                mark = new TrailMark { Transform = go.transform, Renderer = renderer };
            }
            else
            {
                mark.Transform.gameObject.SetActive(true);
                mark.Renderer.SetPropertyBlock(null);   // 페이드 알파 초기화 — 새 오브젝트와 같은 상태로
            }

            float yaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            mark.Transform.SetPositionAndRotation(point + normal * 0.025f,
                Quaternion.FromToRotation(Vector3.up, normal) * Quaternion.AngleAxis(yaw, Vector3.up));
            mark.Transform.localScale = new Vector3(m_Kit.TrailSize.x, 1f, m_Kit.TrailSize.y);
            mark.SpawnTime = now;
            m_Trails.Add(mark);
        }

        private void RecycleTrail(TrailMark mark)
        {
            if (mark.Transform == null) return;
            mark.Transform.gameObject.SetActive(false);
            m_TrailPool.Push(mark);
        }

        private void RefreshPlayers()
        {
            m_Players.Clear();
            foreach (GameObject go in GameObject.FindGameObjectsWithTag("Player"))
                m_Players.Add(go.transform);
            // 사라진 플레이어 정리
            var stale = new List<Transform>();
            foreach (Transform t in m_LastPlayerPos.Keys)
                if (t == null || !m_Players.Contains(t)) stale.Add(t);
            foreach (Transform t in stale) m_LastPlayerPos.Remove(t);
        }

        // ───────────────────────── 배치 범위/바닥 ─────────────────────────

        private bool TryGetArea(out Bounds area)
        {
            if (m_AreaOverride.HasValue)
            {
                // y 규약을 그리드 경로와 맞춘다: 중심 y = 바닥(존 min), 높이 +10 (TryGroundPoint의 바닥 필터 기준)
                var b = m_AreaOverride.Value;
                area = new Bounds(new Vector3(b.center.x, b.min.y, b.center.z),
                                  new Vector3(b.size.x, b.size.y + 10f, b.size.z));
                return true;
            }
            if (m_Grid == null) m_Grid = FindFirstObjectByType<GridManager>();
            if (m_Grid != null)
            {
                Vector3Int size = m_Grid.EffectiveSize;
                Vector3 origin = GridContract.Origin;
                var center = new Vector3(origin.x + size.x * 0.5f, origin.y, origin.z + size.z * 0.5f);
                area = new Bounds(center, new Vector3(size.x + Margin * 2f, size.y + 10f, size.z + Margin * 2f));
                return true;
            }

            Camera camera = Camera.main;
            if (camera == null) { area = default; return false; }
            if (!RaycastGround(camera.transform.position + camera.transform.forward * 8f + Vector3.up * 10f, 60f,
                    out Vector3 point, out _))
            {
                area = default;
                return false;
            }
            area = new Bounds(point, new Vector3(18f, 10f, 18f));
            return true;
        }

        private bool TryGroundPoint(System.Random random, Bounds area, out Vector3 point, out Vector3 normal)
        {
            for (int attempt = 0; attempt < 4; attempt++)
            {
                float x = Mathf.Lerp(area.min.x, area.max.x, (float)random.NextDouble());
                float z = Mathf.Lerp(area.min.z, area.max.z, (float)random.NextDouble());
                var from = new Vector3(x, area.max.y, z);
                if (!RaycastGround(from, area.size.y + 5f, out point, out normal)) continue;
                if (point.y > area.center.y + GroundTolerance) continue;   // 블록/구조물 위는 제외
                if (point.y < area.center.y - GroundTolerance) continue;   // 맵 바닥(그리드 층)보다 아래 — 데크 밖 바위·강물 위는 제외
                if (normal.y < 0.92f) continue;                             // 평평한 바닥만(경사면·벽면 제외)
                return true;
            }
            point = default; normal = Vector3.up;
            return false;
        }

        private bool RaycastGround(Vector3 from, float distance, out Vector3 point, out Vector3 normal)
        {
            int count = Physics.RaycastNonAlloc(from, Vector3.down, m_Hits, distance,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            point = default; normal = Vector3.up;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = m_Hits[i];
                if (hit.distance >= best) continue;
                if (hit.collider.CompareTag("Player")) continue;
                if (hit.collider.CompareTag("Boundary")) continue;   // 투명 경계벽은 바닥이 아니다
                if (hit.collider.isTrigger) continue;
                if (hit.transform.IsChildOf(transform)) continue;   // 자기 데칼은 바닥이 아니다
                best = hit.distance;
                point = hit.point;
                normal = hit.normal;
            }
            return best < float.MaxValue;
        }

        private bool EnsureKit()
        {
            if (m_Kit != null) return true;
            m_Kit = Resources.Load<WeatherGroundKit>(KitPath);
            if (m_Kit != null) return true;
            if (!m_MissingKitLogged)
            {
                m_MissingKitLogged = true;
                Debug.LogWarning("[Weather] WeatherGroundKit 을 찾을 수 없습니다. Tools > UI NEW > Rebuild 3D Weather VFX");
            }
            return false;
        }

        private static float Lerp(System.Random random, Vector2 range)
            => Mathf.Lerp(range.x, range.y, (float)random.NextDouble());

        private static float Yaw(System.Random random) => (float)random.NextDouble() * 360f;
    }
}
