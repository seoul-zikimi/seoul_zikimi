using UnityEngine;

/// <summary>
/// 이동 트레일 카탈로그 — Vefects Trails VFX + Stylized Rainbow (Resources/Trails/*.prefab).
/// 아웃핏과 독립 슬롯(SaveService.EquippedTrail). 보유/구매는 코디 아이템 저장을 재사용(id: trail_*).
/// 부착: Attach(캐릭터 루트, id) — 프리뷰(마이페이지)와 인게임(CodiWearer) 공용.
/// </summary>
public static class TrailCatalog
{
    public readonly struct Entry
    {
        public readonly string Id;
        public readonly string DisplayName;
        public readonly string PrefabName;
        public readonly int Price;
        public readonly float Scale;
        public Entry(string id, string display, string prefab, int price, float scale = 1f)
        { Id = id; DisplayName = display; PrefabName = prefab; Price = price; Scale = scale; }
    }

    public static readonly Entry[] All =
    {
        new Entry("trail_fire",     "불꽃",   "VFX_Trail_Fire",     500),
        new Entry("trail_ice",      "얼음",   "VFX_Trail_Ice",      500),
        new Entry("trail_water",    "물결",   "VFX_Trail_Water",    500),
        new Entry("trail_nature",   "새싹",   "VFX_Trail_Nature",   500),
        new Entry("trail_earth",    "대지",   "VFX_Trail_Earth",    500),
        new Entry("trail_electric", "번개",   "VFX_Trail_Electric", 800),
        new Entry("trail_sound",    "음표",   "VFX_Trail_Sound",    800),
        new Entry("trail_dark",     "어둠",   "VFX_Trail_Dark",     1000),
        new Entry("trail_cosmos",   "우주",   "VFX_Trail_Cosmos",   1200),
        new Entry("trail_void",     "공허",   "VFX_Trail_Void",     1200),
        new Entry("trail_rainbow",  "무지개", "__builtin_rainbow", 1500),   // 자체 제작 리본(에셋은 연출용이라 부적합)
    };

    public static bool TryFind(string id, out Entry entry)
    {
        foreach (var e in All)
            if (e.Id == id) { entry = e; return true; }
        entry = default;
        return false;
    }

    public static Sprite LoadThumbnail(string id) => Resources.Load<Sprite>($"UI_pngs/MyPage/Thumb_{id}");

    private const string kNodeName = "~Trail";

    /// <summary>캐릭터에 트레일 부착(기존 것 제거). id 빈 문자열 = 제거만.</summary>
    public static void Attach(GameObject character, string id)
    {
        if (character == null) return;
        // 기존 트레일 전부 제거(같은 프레임 중복 호출에도 안전)
        for (int i = character.transform.childCount - 1; i >= 0; i--)
        {
            var c = character.transform.GetChild(i);
            if (c.name == kNodeName) Object.Destroy(c.gameObject);
        }
        if (string.IsNullOrEmpty(id) || !TryFind(id, out var entry)) return;

        GameObject go;
        if (entry.PrefabName == "__builtin_rainbow")
        {
            go = BuildRainbow();
            go.transform.SetParent(character.transform, false);
        }
        else
        {
            var prefab = Resources.Load<GameObject>($"Trails/{entry.PrefabName}");
            if (prefab == null) { Debug.LogWarning($"[Trail] 프리팹 없음: {entry.PrefabName}"); return; }
            go = Object.Instantiate(prefab, character.transform);
        }
        go.name = kNodeName;
        go.transform.localPosition = new Vector3(0f, 0.08f, 0f);   // 기본 먼지 트레일과 같은 자리
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one * entry.Scale;
        // 두께 정규화(기본 0.45의 3배 = 1.35) + 길이 제한(0.35초)
        var renderers = go.GetComponentsInChildren<TrailRenderer>(true);
        float maxW = 0.01f;
        foreach (var tr in renderers) maxW = Mathf.Max(maxW, tr.widthMultiplier);
        float widthScale = 1.35f / maxW;
        foreach (var tr in renderers)
        {
            tr.widthMultiplier *= widthScale;
            tr.time = Mathf.Min(tr.time, 0.35f);
        }
        // 프리팹에 딸려오는 효과음 제거
        foreach (var au in go.GetComponentsInChildren<AudioSource>(true))
        {
            au.mute = true;
            au.enabled = false;
        }
        go.AddComponent<TrailTeleportGuard>();   // 스폰/순간이동 시 하늘까지 그려지는 리본 방지
        return;

        // 자체 무지개 리본 — 넓은 무지개 그라데이션 + 얇은 흰 코어(움직일 때만 그려짐)
        static GameObject BuildRainbow()
        {
            var root = new GameObject("RainbowTrail");
            var mat = CreateVertexColorMaterial();

            var grad = new Gradient();
            grad.SetKeys(new[]
            {
                new GradientColorKey(new Color(1f, 0.35f, 0.35f), 0f),
                new GradientColorKey(new Color(1f, 0.75f, 0.25f), 0.2f),
                new GradientColorKey(new Color(1f, 0.95f, 0.35f), 0.4f),
                new GradientColorKey(new Color(0.45f, 0.9f, 0.45f), 0.6f),
                new GradientColorKey(new Color(0.35f, 0.6f, 1f), 0.8f),
                new GradientColorKey(new Color(0.7f, 0.45f, 1f), 1f),
            }, new[] { new GradientAlphaKey(0.95f, 0f), new GradientAlphaKey(0f, 1f) });

            var band = root.AddComponent<TrailRenderer>();
            band.time = 0.35f;
            band.minVertexDistance = 0.03f;
            band.widthMultiplier = 0.16f;   // Attach에서 ×2.2 적용됨
            band.widthCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0.1f);
            band.colorGradient = grad;
            band.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            band.receiveShadows = false;
            if (mat != null) band.material = mat;

            var coreGo = new GameObject("Core");
            coreGo.transform.SetParent(root.transform, false);
            var core = coreGo.AddComponent<TrailRenderer>();
            core.time = 0.3f;
            core.minVertexDistance = 0.03f;
            core.widthMultiplier = 0.05f;
            core.widthCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0.1f);
            var coreGrad = new Gradient();
            coreGrad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
            core.colorGradient = coreGrad;
            core.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            core.receiveShadows = false;
            if (mat != null) core.material = mat;
            return root;
        }

        static Material CreateVertexColorMaterial()
        {
            string[] candidates =
            {
                "Universal Render Pipeline/Particles/Unlit",
                "Universal Render Pipeline/Unlit",
                "Particles/Standard Unlit",
                "Sprites/Default",
            };
            foreach (var name in candidates)
            {
                var shader = Shader.Find(name);
                if (shader != null) return new Material(shader);
            }
            return null;
        }
        Debug.Log($"[Trail] '{entry.Id}' 부착 → {character.name} (렌더러 {go.GetComponentsInChildren<Renderer>(true).Length}개)");
    }
}

/// <summary>한 프레임에 크게 이동(스폰·순간이동)하면 트레일을 끊고, 트레일을 캐릭터 발 높이에 붙인다.</summary>
public class TrailTeleportGuard : MonoBehaviour
{
    private TrailRenderer[] m_Trails;
    private Vector3 m_Last;

    private void OnEnable()
    {
        m_Trails = GetComponentsInChildren<TrailRenderer>(true);
        m_Last = transform.position;
        Clear();   // 부착 직후 잔상 제거
    }

    private void LateUpdate()
    {
        if ((transform.position - m_Last).sqrMagnitude > 9f)   // 한 프레임 3m 이상 = 순간이동
            Clear();
        m_Last = transform.position;
    }

    private void Clear()
    {
        if (m_Trails == null) return;
        foreach (var tr in m_Trails)
            if (tr != null) tr.Clear();
    }
}
