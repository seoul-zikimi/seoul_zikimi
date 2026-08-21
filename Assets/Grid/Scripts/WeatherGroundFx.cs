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
                    Destroy(mark.Transform.gameObject);
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
            if (!RaycastGround(playerPos + Vector3.up * 1.5f, 4f, out Vector3 point, out Vector3 normal))
                return;
            if (m_Trails.Count >= Mathf.Max(1, m_Kit.TrailMax))
            {
                TrailMark oldest = m_Trails[0];
                if (oldest.Transform != null) Destroy(oldest.Transform.gameObject);
                m_Trails.RemoveAt(0);
            }

            float yaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            var go = new GameObject("trail");
            go.transform.SetParent(m_TrailRoot, false);
            go.transform.SetPositionAndRotation(point + normal * 0.025f,
                Quaternion.FromToRotation(Vector3.up, normal) * Quaternion.AngleAxis(yaw, Vector3.up));
            go.transform.localScale = new Vector3(m_Kit.TrailSize.x, 1f, m_Kit.TrailSize.y);
            go.AddComponent<MeshFilter>().sharedMesh = m_Kit.Quad;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = m_Kit.SnowTrail;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            m_Trails.Add(new TrailMark { Transform = go.transform, Renderer = renderer, SpawnTime = now });
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
                if (normal.y < 0.6f) continue;                              // 벽면 제외
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
