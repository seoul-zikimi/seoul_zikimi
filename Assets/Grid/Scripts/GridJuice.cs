using System.Collections.Generic;
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
        /// <summary>FX 조각용 공용 투명 머티리얼(ItemFx 등도 재사용).</summary>
        internal static Material FxMat()
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

        /// <summary>수명 자율 FX 조각 하나(위치·크기·색). 나머지 물리값은 호출측이 채운다.</summary>
        internal static JuiceParticle MakeBit(Vector3 pos, float size, Color col)
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

        // 페인트: 초록 방울이 팡 튀고 낙하(틱=작게, 완료=크게).
        public static void PaintPop(Vector3 center, float u, float scale = 1f)
        {
            var green = new Color(0.30f, 0.85f, 0.40f);
            int n = Mathf.RoundToInt(4f * scale);
            for (int i = 0; i < n; i++)
            {
                var dir = Random.insideUnitSphere; dir.y = Mathf.Abs(dir.y);
                var fx = MakeBit(center, u * 0.09f * scale, green);
                fx.vel = dir * (u * (1.0f + Random.value) * scale);
                fx.gravity = -u * 5f; fx.life = 0.4f;
                fx.spinDeg = 200f; fx.spinAxis = Random.onUnitSphere; fx.startAlpha = 0.95f;
            }
        }

        // 점수 팝업(+200 등): 월드 텍스트가 떠오르며 사라짐. TextMesh(내장)라 TMP 의존 없음.
        public static void ScorePop(Vector3 pos, int amount, Color color)
            => WorldText(pos, amount > 0 ? $"+{amount}" : "+0", color, 48, 0.9f, 1.2f);

        // 월드 토스트("앗! 무너졌어요!" 등): 대상 위치 바로 위에 떠오르는 안내 텍스트.
        // [08/27 피드백] 잘 안 보인다 → 크게·오래·두꺼운 외곽선으로 전면 보강.
        public static void WorldToast(Vector3 pos, string text, Color color)
            => WorldText(pos, text, color, 52, 2.4f, 1.0f);

        static Font s_WorldFont;
        static void WorldText(Vector3 pos, string text, Color color, int fontSize, float life, float rise)
        {
            if (s_WorldFont == null)   // 폰트 통일: UI와 같은 서울한강 장체M(없으면 내장 폴백)
            {
                s_WorldFont = Resources.Load<Font>("Fonts/서울한강 장체M");
                if (s_WorldFont == null) s_WorldFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
            const float kCharSize = 0.065f;   // 0.05 → 0.065: 전체 텍스트 30% 확대(가독성)
            var go = new GameObject("~WorldText");
            go.transform.position = pos;
            var tm = go.AddComponent<TextMesh>();
            tm.text = text;
            tm.fontSize = fontSize;
            tm.characterSize = kCharSize;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.color = color;
            tm.font = s_WorldFont;
            go.GetComponent<MeshRenderer>().material = s_WorldFont.material;   // 폰트 아틀라스 머티리얼 필수

            // 외곽선 — 밝은 배경(하늘·눈밭)에서 연한 색 글자가 안 보여서, 검정 사본을 4방향으로 깐다.
            // (~shadow 이름은 JuiceFloatText가 같이 페이드시키는 계약 — 첫 번째 것만 그 이름을 쓰고 전부 자식이라 함께 사라진다)
            for (int i = 0; i < 4; i++)
            {
                Vector2[] dirs = { new(0.04f, -0.04f), new(-0.04f, -0.04f), new(0.04f, 0.04f), new(-0.04f, 0.04f) };
                var shadow = new GameObject(i == 0 ? "~shadow" : "~outline");
                shadow.transform.SetParent(go.transform, false);
                shadow.transform.localPosition = new Vector3(dirs[i].x, dirs[i].y, 0.04f);
                var stm = shadow.AddComponent<TextMesh>();
                stm.text = text;
                stm.fontSize = fontSize;
                stm.characterSize = kCharSize;
                stm.anchor = TextAnchor.MiddleCenter;
                stm.color = new Color(0f, 0f, 0f, 0.9f);
                stm.font = s_WorldFont;
                shadow.GetComponent<MeshRenderer>().material = s_WorldFont.material;
            }

            go.AddComponent<JuiceFloatText>().Init(life, rise);
        }

        // 흙 팡(배송 착지·붕괴·플레이어 착지 등). Resources/Fx/GroundHit = CFXR2 Ground Hit 사본.
        static GameObject s_GroundHit;
        static bool s_GroundHitTried;
        public static void GroundHit(Vector3 pos, float scale = 1f)
        {
            if (!s_GroundHitTried) { s_GroundHitTried = true; s_GroundHit = Resources.Load<GameObject>("Fx/GroundHit"); }
            if (s_GroundHit == null) return;
            var go = Object.Instantiate(s_GroundHit, pos, Quaternion.identity);
            go.transform.localScale *= scale;
            Object.Destroy(go, 4f);
        }

        // 젤리 파동: visualRoot 자식 비주얼들을 중심에서 거리순 지연 스퀴시 → 출렁임이 번져나감(민달팽이 시그니처).
        public static void Ripple(Transform visualRoot, Vector3 center, float radius, float amount = 0.08f, float speed = 9f)
        {
            if (visualRoot == null) return;
            var host = new GameObject("~Ripple").AddComponent<JuiceRipple>();
            host.Begin(visualRoot, center, radius, amount, speed);
        }

        // 블록 쫀득 스퀴시: 눌렸다(y↓ xz↑) 출렁이며 복원. 진행 중이면 재시작.
        public static void Squish(GameObject go, float amount = 0.08f)
        {
            if (go == null) return;
            var s = go.GetComponent<JuiceSquish>();
            if (s == null) s = go.AddComponent<JuiceSquish>();
            s.Play(amount);
        }

        /// <summary>시네머신 등 외부 카메라 리그가 설치하는 FOV 펀치 핸들러(설치되면 이쪽 우선).
        /// CinemachineBrain이 Camera.main fov를 매 프레임 덮어써서 직접 펀치가 무효라 vcam 쪽에 위임.</summary>
        public static System.Action<float> FovPunchHandler;

        // 카메라 FOV 펀치(팔로우/오빗이 FOV는 안 건드림 → 트랜스폼 흔들기보다 안전). 카메라에 1회 부착.
        public static void FovPunch(Camera cam, float amount)
        {
            if (FovPunchHandler != null) { FovPunchHandler(amount); return; }   // 시네머신 리그 우선
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

    /// <summary>월드 텍스트: 뿅 커지며 떠오르다 페이드. 카메라 빌보드. 수명 후 자멸.</summary>
    public class JuiceFloatText : MonoBehaviour
    {
        float m_Life = 0.9f, m_Rise = 1.2f, m_T;
        TextMesh[] m_All; Color[] m_AllBase;   // 본문+외곽선 4방향 전부 같이 페이드

        public void Init(float life, float rise) { m_Life = life; m_Rise = rise; }

        void Start()
        {
            m_All = GetComponentsInChildren<TextMesh>();
            m_AllBase = new Color[m_All.Length];
            for (int i = 0; i < m_All.Length; i++) m_AllBase[i] = m_All[i].color;
        }

        void LateUpdate()
        {
            m_T += Time.deltaTime;
            if (m_T >= m_Life) { Destroy(gameObject); return; }
            float n = m_T / m_Life;

            transform.position += Vector3.up * (m_Rise * Time.deltaTime * (1.4f - n));   // 점점 느리게 상승
            if (Camera.main != null) transform.rotation = Camera.main.transform.rotation;

            float s = n < 0.15f ? Mathf.Lerp(0.4f, 1.2f, n / 0.15f)                     // 뿅 팝인
                    : Mathf.Lerp(1.2f, 1f, Mathf.Clamp01((n - 0.15f) / 0.25f));
            transform.localScale = Vector3.one * s;

            float fade = n > 0.6f ? 1f - (n - 0.6f) / 0.4f : 1f;   // 끝 40% 페이드
            if (m_All != null)
                for (int i = 0; i < m_All.Length; i++)
                {
                    if (m_All[i] == null) continue;
                    var c = m_AllBase[i]; c.a *= fade;
                    m_All[i].color = c;
                }
        }
    }

    /// <summary>젤리 파동 실행기: 거리순으로 지연 스퀴시 후 자멸. GridJuice.Ripple로 사용.</summary>
    public class JuiceRipple : MonoBehaviour
    {
        public void Begin(Transform root, Vector3 center, float radius, float amount, float speed)
            => StartCoroutine(Run(root, center, radius, amount, speed));

        System.Collections.IEnumerator Run(Transform root, Vector3 center, float radius, float amount, float speed)
        {
            var targets = new List<(GameObject go, float d)>();
            foreach (Transform t in root)
            {
                var rend = t.GetComponentInChildren<Renderer>();
                if (rend == null) continue;
                float d = Vector3.Distance(rend.bounds.center, center);
                if (d < radius) targets.Add((t.gameObject, d));
            }
            targets.Sort((a, b) => a.d.CompareTo(b.d));

            float elapsed = 0f; int i = 0;
            while (i < targets.Count && root != null)
            {
                elapsed += Time.deltaTime;
                while (i < targets.Count && targets[i].d / speed <= elapsed)
                {
                    if (targets[i].go != null)   // 파동 도달 순간, 멀수록 약하게
                        GridJuice.Squish(targets[i].go, Mathf.Lerp(amount, amount * 0.3f, targets[i].d / radius));
                    i++;
                }
                yield return null;
            }
            Destroy(gameObject);
        }
    }

    /// <summary>둥실둥실 부유(공정 마커 등): 위아래 bob + 천천히 회전. 위치 확정 후 부착.</summary>
    public class JuiceBob : MonoBehaviour
    {
        const float kAmp = 0.09f, kHz = 2.2f, kSpin = 70f;
        Vector3 m_BasePos;
        float m_Phase;

        void Start() { m_BasePos = transform.position; m_Phase = Random.value * Mathf.PI * 2f; }

        void Update()
        {
            transform.position = m_BasePos + Vector3.up * (Mathf.Sin(Time.time * kHz + m_Phase) * kAmp);
            transform.Rotate(Vector3.up, kSpin * Time.deltaTime, Space.World);
        }
    }

    /// <summary>블록 스퀴시(squash&stretch): 눌림 → 감쇠 출렁 복원. GridJuice.Squish로 사용.</summary>
    public class JuiceSquish : MonoBehaviour
    {
        Vector3 m_BaseScale;
        bool m_HasBase;
        float m_T = -1f;
        float m_Amt;
        const float kDur = 0.22f;
        const float kPressPart = 0.25f;   // 앞 25% = 눌림, 나머지 = 출렁 복원

        public void Play(float amount)
        {
            if (!m_HasBase) { m_BaseScale = transform.localScale; m_HasBase = true; }
            m_Amt = amount;
            m_T = 0f;
        }

        void Update()
        {
            if (m_T < 0f) return;
            m_T += Time.deltaTime;
            float n = m_T / kDur;
            if (n >= 1f)
            {
                transform.localScale = m_BaseScale;
                m_T = -1f;
                return;
            }

            float p;   // 눌림 정도(1=최대 눌림, 음수=반동으로 늘어남)
            if (n < kPressPart) p = n / kPressPart;
            else
            {
                float t2 = (n - kPressPart) / (1f - kPressPart);
                p = (1f - t2) * Mathf.Cos(t2 * 5f);   // 감쇠 출렁(음수 구간 = 위로 살짝 늘어남)
            }

            float y  = 1f - p * m_Amt * 1.6f;   // 눌릴 때 납작
            float xz = 1f + p * m_Amt;          // 눌릴 때 옆으로 퍼짐
            transform.localScale = new Vector3(m_BaseScale.x * xz, m_BaseScale.y * y, m_BaseScale.z * xz);
        }
    }
}
