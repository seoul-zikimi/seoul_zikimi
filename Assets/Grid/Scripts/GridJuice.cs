using UnityEngine;
using UnityEngine.Rendering;

namespace GridSystem
{
    /// <summary>
    /// 코드-only 게임필(juice). 놓기 먼지 · 붕괴 잔해 · 고정 번쩍 · 카메라 FOV 펀치.
    /// GridNetwork.RebuildVisuals가 셀 변화마다 전체 파괴/재생성하므로 실제 블록에 애니를 걸면
    /// 잭거린다 → 대신 수명 자율(스스로 정착·소멸)하는 '독립 FX 오브젝트'를 띄운다.
    /// 프리팹/에디터 셋업 불필요. URP Lit 명시 지정(빌드 셰이더 스트립·CreatePrimitive 기본머티리얼 회피).
    /// </summary>
    public static class GridJuice
    {
        static readonly int s_BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int s_Color = Shader.PropertyToID("_Color");

        static Material s_Mat;
        static Material FxMat()
        {
            if (s_Mat != null) return s_Mat;
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            if (sh == null) return null;
            var m = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            m.SetFloat("_Surface", 1f);
            m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
            s_Mat = m;
            return m;
        }

        static JuiceParticle MakeBit(Vector3 pos, float size, Color col)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "~Fx";
            var c = go.GetComponent<Collider>();
            if (c != null) c.enabled = false;   // 즉시 끔(Destroy는 1프레임 지연 → 물리 간섭 방지). 순수 비주얼.
            go.transform.position = pos;
            go.transform.localScale = Vector3.one * size;
            var mat = FxMat();
            if (mat != null) go.GetComponent<Renderer>().sharedMaterial = mat;
            var fx = go.AddComponent<JuiceParticle>();
            fx.color = col;
            return fx;
        }

        // 놓기: 블록 밑에서 먼지 몇 톨이 바깥으로 퍼지며 사라짐.
        public static void PlacePuff(Vector3 baseCenter, float u)
        {
            var dust = new Color(0.86f, 0.83f, 0.76f);
            for (int i = 0; i < 5; i++)
            {
                float a = (i / 5f) * Mathf.PI * 2f;
                var fx = MakeBit(baseCenter, u * 0.16f, dust);
                fx.vel = new Vector3(Mathf.Cos(a), 0.5f, Mathf.Sin(a)) * (u * 1.1f);
                fx.gravity = -u * 1.5f; fx.life = 0.32f; fx.spinDeg = 120f; fx.scaleVel = u * 0.6f; fx.startAlpha = 0.6f;
            }
        }

        // 붕괴: 돌부스러기(블록색 큐브)가 사방으로 튀며 낙하·회전 + 먼지구름. 젤 극적인 순간.
        public static void CollapseBurst(Vector3 center, float u)
        {
            var stone = new Color(0.62f, 0.60f, 0.56f);
            for (int i = 0; i < 7; i++)
            {
                var dir = Random.insideUnitSphere; dir.y = Mathf.Abs(dir.y) * 0.6f + 0.3f;
                var fx = MakeBit(center, u * (0.22f + Random.value * 0.16f), stone);
                fx.vel = dir.normalized * (u * (2.0f + Random.value * 2.0f));
                fx.gravity = -u * 7f; fx.life = 0.7f + Random.value * 0.25f;
                fx.spinDeg = 200f + Random.value * 400f; fx.spinAxis = Random.onUnitSphere; fx.startAlpha = 0.95f;
            }
            var dust = new Color(0.80f, 0.78f, 0.72f);
            for (int i = 0; i < 5; i++)
            {
                var d = Random.insideUnitSphere; d.y = Mathf.Abs(d.y);
                var fx = MakeBit(center, u * 0.35f, dust);
                fx.vel = d * (u * 1.0f); fx.gravity = -u * 0.5f; fx.life = 0.55f;
                fx.scaleVel = u * 1.2f; fx.startAlpha = 0.5f;
            }
        }

        // 카메라 FOV 펀치(팔로우/오빗이 FOV는 안 건드림 → 트랜스폼 흔들기보다 안전). 카메라에 1회 부착.
        public static void FovPunch(Camera cam, float amount)
        {
            if (cam == null) return;
            var p = cam.GetComponent<CameraFovPunch>();
            if (p == null) p = cam.gameObject.AddComponent<CameraFovPunch>();
            p.Add(amount);
        }
    }

    /// <summary>수명 자율 FX 조각: 이동+중력+회전+스케일+페이드 후 자멸. MPB로 색/알파만 바꿔 배치(batch) 유지.</summary>
    public sealed class JuiceParticle : MonoBehaviour
    {
        public Vector3 vel;
        public float gravity;
        public float life = 0.4f;
        public float spinDeg;
        public Vector3 spinAxis = Vector3.up;
        public float scaleVel;      // 초당 균일 스케일 증감(먼지=팽창)
        public Color color = Color.white;
        public float startAlpha = 1f;

        static readonly int s_BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int s_Color = Shader.PropertyToID("_Color");
        Renderer m_R;
        MaterialPropertyBlock m_Mpb;
        float m_T;

        void Start()
        {
            m_R = GetComponent<Renderer>();
            m_Mpb = new MaterialPropertyBlock();
            if (spinAxis == Vector3.zero) spinAxis = Vector3.up;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            m_T += dt;
            vel.y += gravity * dt;
            transform.position += vel * dt;
            if (spinDeg != 0f) transform.Rotate(spinAxis, spinDeg * dt, Space.World);

            var s = transform.localScale + Vector3.one * (scaleVel * dt);
            if (s.x <= 0.001f) { Destroy(gameObject); return; }
            transform.localScale = s;

            float k = Mathf.Clamp01(1f - m_T / life);
            var c = color; c.a = startAlpha * k;
            if (m_R != null)
            {
                m_R.GetPropertyBlock(m_Mpb);
                m_Mpb.SetColor(s_BaseColor, c);
                m_Mpb.SetColor(s_Color, c);
                m_R.SetPropertyBlock(m_Mpb);
            }
            if (m_T >= life) Destroy(gameObject);
        }
    }

    /// <summary>카메라 FOV를 순간 밀었다가 부드럽게 복원(감쇠). 최대치 클램프 → 연쇄 붕괴에도 안 터짐.</summary>
    public sealed class CameraFovPunch : MonoBehaviour
    {
        Camera m_Cam;
        float m_Base;
        float m_Punch;
        const float kDecay = 9f;
        const float kMax = 8f;

        void Awake()
        {
            m_Cam = GetComponent<Camera>();
            if (m_Cam != null) m_Base = m_Cam.fieldOfView;
        }

        public void Add(float amount)
        {
            if (m_Cam == null) return;
            if (Mathf.Approximately(m_Punch, 0f)) m_Base = m_Cam.fieldOfView;   // 유휴 상태 fov를 기준으로(다른 스크립트와 합성)
            m_Punch = Mathf.Clamp(m_Punch + amount, -kMax, kMax);
        }

        void LateUpdate()
        {
            if (m_Cam == null) return;
            if (!Mathf.Approximately(m_Punch, 0f))
            {
                m_Punch *= Mathf.Exp(-kDecay * Time.deltaTime);
                if (Mathf.Abs(m_Punch) < 0.02f) m_Punch = 0f;
            }
            m_Cam.fieldOfView = m_Base + m_Punch;
        }
    }
}
