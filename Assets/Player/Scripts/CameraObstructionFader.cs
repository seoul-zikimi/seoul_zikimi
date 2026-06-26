using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Player
{
    /// <summary>
    /// 카메라→플레이어 사이로 레이를 쏴서, 시야를 가리는 콜라이더를 반투명(α=0.2)으로 만든다.
    /// CinemachineCamera GameObject에 owner 전용으로 붙음(PlayerCameraController와 같은 오브젝트).
    ///
    /// 원본 셰이더(URP Lit / glTF PBR 등)에 의존하지 않으려고, 가린 동안만 렌더러 머티리얼을
    /// "고스트"(URP Lit 반투명·단면 Cull Back)로 교체한다. 원본 색/텍스처는 복사해 외형은 유지.
    /// glTF처럼 양면(double-sided) 셰이더라도 단면 고스트로 바꾸므로 "한 면만 투명" 현상이 없다.
    /// 시야가 트이면 원본 공유 머티리얼로 원복.
    /// </summary>
    public class CameraObstructionFader : MonoBehaviour
    {
        [SerializeField] float m_FadedAlpha = 0.2f;   // 가렸을 때 목표 알파
        [SerializeField] float m_CastRadius = 0.4f;   // 0이면 순수 레이, >0이면 굵은 레이(SphereCast)
        [SerializeField] float m_FadeSpeed  = 10f;     // 페이드 인/아웃 속도(클수록 빠름)
        [SerializeField] bool  m_DebugDraw  = false;   // true면 레이 시각화(빨간선) + 페이드 로그

        Transform m_Target;                            // 카메라가 바라보는 지점(보통 CameraArm = 허리 높이)
        int       m_Mask;                              // 가림 판정 레이어(물/UI/Ignore Raycast 제외)

        static readonly RaycastHit[] s_Hits = new RaycastHit[32];
        static Material s_GhostTemplate;               // 반투명 고스트 원본(공유, 복제해서 씀)

        // 현재 페이드 중인 렌더러 상태. 가림이 시작되면 등록, 완전히 트이면(원복 후) 제거.
        class Entry
        {
            public Material[] originalShared;          // 원본 공유 머티리얼(원복용)
            public Material[] instances;               // 적용 중인 고스트 인스턴스
            public Color[]    baseColors;              // 고스트 색(알파만 매 프레임 갱신)
            public float      factor;                  // 0=불투명, 1=완전 페이드
            public bool       hitThisFrame;
        }

        readonly Dictionary<Renderer, Entry> m_Active = new Dictionary<Renderer, Entry>();
        readonly List<Renderer> m_Remove = new List<Renderer>();
        readonly HashSet<Collider> m_Logged = new HashSet<Collider>();   // 디버그: 콜라이더당 1회만 로그

        public void Init(Transform target)
        {
            m_Target = target;
            // 플레이어 자신은 IsChildOf로 따로 거르고, 물/UI/레이무시 레이어는 가림 대상에서 제외.
            m_Mask = ~LayerMask.GetMask("Water", "UI", "Ignore Raycast", "TransparentFX");
        }

        void LateUpdate()
        {
            if (m_Target != null)
                CastAndMark();

            ApplyFade();
        }

        // 카메라→타깃 레이로 가리는 렌더러를 찾아 hitThisFrame 표시 + 신규는 페이드 등록.
        void CastAndMark()
        {
            foreach (var e in m_Active.Values) e.hitThisFrame = false;

            Vector3 origin = transform.position;
            Vector3 to     = m_Target.position - origin;
            float   dist   = to.magnitude;
            if (dist <= 0.01f) return;
            Vector3 dir = to / dist;

            if (m_DebugDraw) Debug.DrawLine(origin, m_Target.position, Color.red);

            // 보이는 벽은 물리 충돌 없는 Trigger 콜라이더를 쓰므로, 트리거까지 포함해 캐스트한다.
            // (렌더러 없는 경계 콜라이더 AreaWall·~Ground는 아래에서 renderer==null로 걸러짐)
            int n = m_CastRadius > 0f
                ? Physics.SphereCastNonAlloc(origin, m_CastRadius, dir, s_Hits, dist, m_Mask, QueryTriggerInteraction.Collide)
                : Physics.RaycastNonAlloc(origin, dir, s_Hits, dist, m_Mask, QueryTriggerInteraction.Collide);

            for (int i = 0; i < n; i++)
            {
                var col = s_Hits[i].collider;
                if (col == null) continue;
                if (col.transform.IsChildOf(m_Target.parent != null ? m_Target.parent : m_Target))
                    continue;   // 플레이어 자신(타깃과 그 부모 트리) 제외

                var renderer = FindRenderer(col);
                if (renderer == null) continue;    // 렌더러 없는 콜라이더(AreaWall 경계, ~Ground 등)는 페이드 대상 아님

                if (m_DebugDraw && m_Logged.Add(col))
                    Debug.Log($"[Fader] fade target '{col.name}' renderer='{renderer.name}'", col);

                if (m_Active.TryGetValue(renderer, out var e)) e.hitThisFrame = true;
                else                                           m_Active[renderer] = BeginFade(renderer);
            }
        }

        // 콜라이더와 렌더러가 다른 오브젝트에 있을 수 있음 → self → parent → children 순으로 탐색.
        static Renderer FindRenderer(Collider col)
        {
            var r = col.GetComponent<Renderer>();
            if (r != null) return r;
            r = col.GetComponentInParent<Renderer>();
            if (r != null) return r;
            return col.GetComponentInChildren<Renderer>();
        }

        // 각 렌더러의 factor를 목표로 보간하고 알파 적용. 완전히 트인 것은 원복 후 제거.
        void ApplyFade()
        {
            m_Remove.Clear();

            foreach (var kv in m_Active)
            {
                var renderer = kv.Key;
                var e        = kv.Value;

                if (renderer == null) { Restore(null, e); m_Remove.Add(renderer); continue; }

                float target = e.hitThisFrame ? 1f : 0f;
                e.factor = Mathf.MoveTowards(e.factor, target, m_FadeSpeed * Time.deltaTime);

                if (e.factor <= 0f && target == 0f)   // 다 트임 → 원복
                {
                    Restore(renderer, e);
                    m_Remove.Add(renderer);
                    continue;
                }

                float a = Mathf.Lerp(1f, m_FadedAlpha, e.factor);
                for (int i = 0; i < e.instances.Length; i++)
                {
                    var m = e.instances[i];
                    if (m == null) continue;
                    var c = e.baseColors[i];
                    c.a = a;
                    m.SetColor("_BaseColor", c);
                }
            }

            foreach (var r in m_Remove) m_Active.Remove(r);
        }

        // 렌더러 머티리얼을 고스트(반투명·단면) 인스턴스로 교체하고 상태를 만든다.
        Entry BeginFade(Renderer renderer)
        {
            var shared    = renderer.sharedMaterials;
            var instances = new Material[shared.Length];
            var colors    = new Color[shared.Length];

            for (int i = 0; i < shared.Length; i++)
            {
                var ghost = new Material(GhostTemplate());
                Color tint = CopyAppearance(shared[i], ghost);   // 원본 색/텍스처 복사(외형 유지)
                colors[i]    = tint;                             // rgb 보존, 알파는 ApplyFade가 매 프레임 갱신
                instances[i] = ghost;
            }

            renderer.materials = instances;   // 이 렌더러에만 적용(공유 머티리얼은 안 건드림)

            if (m_DebugDraw)
            {
                var sh = shared.Length > 0 && shared[0] != null ? shared[0].shader.name : "null";
                Debug.Log($"[Fader] fade '{renderer.name}' mats={shared.Length} shader='{sh}'", renderer);
            }

            return new Entry { originalShared = shared, instances = instances, baseColors = colors, factor = 0f, hitThisFrame = true };
        }

        // 원본 공유 머티리얼로 되돌리고 복제 인스턴스를 파괴(누수 방지).
        void Restore(Renderer renderer, Entry e)
        {
            if (renderer != null) renderer.sharedMaterials = e.originalShared;
            if (e.instances != null)
                foreach (var m in e.instances)
                    if (m != null) Destroy(m);
        }

        void OnDisable()
        {
            foreach (var kv in m_Active) Restore(kv.Key, kv.Value);
            m_Active.Clear();
        }

        // ── 고스트 머티리얼: URP Lit 반투명, 단면(Cull Back), ZWrite off ──
        static Material GhostTemplate()
        {
            if (s_GhostTemplate != null) return s_GhostTemplate;

            var sh = Shader.Find("Universal Render Pipeline/Lit");
            var m  = new Material(sh);
            m.SetFloat("_Surface",  1f);   // Transparent
            m.SetFloat("_Blend",    0f);   // Alpha blend
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite",   0f);
            m.SetFloat("_Cull",     (float)CullMode.Back);   // 단면 → 양면 셰이더의 "한 면만 투명" 방지
            m.SetFloat("_AlphaClip", 0f);
            m.DisableKeyword("_ALPHATEST_ON");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;

            s_GhostTemplate = m;
            return m;
        }

        // 원본 머티리얼의 메인 색/텍스처를 고스트에 복사. 반환값은 rgb(알파는 별도 페이드).
        static Color CopyAppearance(Material src, Material ghost)
        {
            Color c = Color.white;
            if (src != null)
            {
                foreach (var name in s_ColorProps)
                    if (src.HasProperty(name)) { c = src.GetColor(name); break; }

                foreach (var name in s_TexProps)
                    if (src.HasProperty(name) && src.GetTexture(name) != null)
                    { ghost.SetTexture("_BaseMap", src.GetTexture(name)); break; }
            }
            c.a = 1f;
            ghost.SetColor("_BaseColor", c);
            return c;
        }

        // 흔한 베이스 컬러/텍스처 프로퍼티 이름(URP Lit, Standard, glTFast, UnityGLTF).
        static readonly string[] s_ColorProps = { "_BaseColor", "baseColorFactor", "_BaseColorFactor", "_Color" };
        static readonly string[] s_TexProps   = { "_BaseMap", "baseColorTexture", "_BaseColorTexture", "_MainTex" };
    }
}
