using SeoulZikimi.Gameplay;
using UnityEngine;

namespace GridSystem
{
    /// <summary>
    /// 2vs2 경쟁 아이템 전용 FX — 파티클·사운드 전부 코드 생성(프리팹/에셋 불필요).
    /// 파티클은 GridJuice.JuiceParticle 재사용, 사운드는 사인/노이즈 합성 AudioClip 캐시.
    /// 등장(팡+반짝) · 획득(모임+블링) · 발동(링 충격파+스윕) · 소멸(피식).
    /// </summary>
    public static class ItemFx
    {
        // 일반 조각은 GridJuice와 공용(빛 받는 투명 재질).
        static JuiceParticle MakeBit(Vector3 pos, float size, Color col) => GridJuice.MakeBit(pos, size, col);

        // 반짝이는 조명을 안 받아야 '빛나는' 느낌이 난다 — URP Unlit + 가산 블렌딩 전용 재질.
        static Material s_Glow;
        static Material GlowMat()
        {
            if (s_Glow != null) return s_Glow;
            var sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            if (sh == null) return null;
            var m = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            m.SetFloat("_Surface", 1f);
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);   // 가산 = 겹칠수록 밝게
            m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            s_Glow = m;
            return m;
        }

        // 밝게 빛나는 조각(색은 흰색 쪽으로 끌어올림 — 어두운 아이템 색도 반짝여 보이게)
        static JuiceParticle MakeSpark(Vector3 pos, float size, Color col, float whiten = 0.65f)
        {
            var fx = GridJuice.MakeBit(pos, size, Color.Lerp(col, Color.white, whiten));
            var mat = GlowMat();
            if (mat != null)
            {
                var r = fx.GetComponent<Renderer>();
                if (r != null) r.sharedMaterial = mat;
            }
            return fx;
        }

        /// <summary>구슬 반짝이 등 외부에서 쓰는 발광 조각.</summary>
        internal static JuiceParticle MakeSparkPublic(Vector3 pos, float size, Color col) => MakeSpark(pos, size, col, 0.75f);

        // ── 이벤트 FX ────────────────────────────────────────────
        /// <summary>등장: 사방으로 튀는 반짝이 + 위로 솟는 불꽃 + 반짝 소리.</summary>
        public static void Spawned(Vector3 pos, Color col)
        {
            for (int i = 0; i < 14; i++)   // 작고 밝은 반짝이가 팡
            {
                float a = (i / 14f) * Mathf.PI * 2f + Random.value * 0.3f;
                var fx = MakeSpark(pos, 0.05f + Random.value * 0.03f, col);
                fx.vel = new Vector3(Mathf.Cos(a) * 1.6f, 2.2f + Random.value * 1.6f, Mathf.Sin(a) * 1.6f);
                fx.gravity = -5f; fx.life = 0.45f + Random.value * 0.2f;
                fx.scaleVel = -0.06f;   // 점점 작아지며 사라짐(별 반짝임)
                fx.spinDeg = 500f; fx.spinAxis = Random.onUnitSphere;
            }
            for (int i = 0; i < 4; i++)    // 중심에서 위로 곧게 솟는 심지
            {
                var fx = MakeSpark(pos, 0.07f, col, 0.85f);
                fx.vel = new Vector3(Random.Range(-0.25f, 0.25f), 3.4f + Random.value, Random.Range(-0.25f, 0.25f));
                fx.gravity = -6f; fx.life = 0.5f; fx.scaleVel = -0.08f;
            }
            Play(SparkleClip(), pos, 0.7f);
        }

        /// <summary>획득: 바깥의 반짝이가 중심으로 빨려들며 위로 솟음 + 코인 소리.</summary>
        public static void PickedUp(Vector3 pos, Color col)
        {
            const int n = 12;
            for (int i = 0; i < n; i++)
            {
                float a = (i / (float)n) * Mathf.PI * 2f;
                var offset = new Vector3(Mathf.Cos(a), Random.Range(-0.1f, 0.4f), Mathf.Sin(a)) * 0.75f;
                var fx = MakeSpark(pos + offset, 0.06f, col);
                fx.vel = (-offset).normalized * 2.6f + Vector3.up * 1.4f;   // 중심으로 빨려들며 살짝 상승
                fx.gravity = 3.5f;                                          // 위로 가속 = 빨려 올라감
                fx.life = 0.3f; fx.scaleVel = -0.12f;
                fx.spinDeg = 420f; fx.spinAxis = Random.onUnitSphere;
            }
            Play(CoinClip(), pos, 0.85f);
        }

        /// <summary>발동: 수평 링 충격파 + 위 분수 + 상승 스윕 사운드.</summary>
        static GameObject s_UsePoof;
        static bool s_UsePoofTried;

        public static void Used(Vector3 pos, Color col, string label = "", CompetitiveItemKind? kind = null)
        {
            if (kind.HasValue) IconPop(pos, kind.Value);   // 어떤 아이템인지 아이콘이 뿅 떠오름
            // 카툰 팡(CFXR Magic Poof 사본) — '확실히 써졌다' 한눈에 보이게(GroundHit 패턴)
            if (!s_UsePoofTried) { s_UsePoofTried = true; s_UsePoof = Resources.Load<GameObject>("Fx/ItemUsePoof"); }
            if (s_UsePoof != null)
            {
                var poof = Object.Instantiate(s_UsePoof, pos, Quaternion.identity);
                Object.Destroy(poof, 4f);
            }
            if (label != "")   // 종류색은 흰 계열(눈·안개)이 많아 안 보임 — 항상 진한 금색
                GridJuice.WorldToast(pos + Vector3.up * 1.1f, $"{label} 사용!", new Color(1f, 0.8f, 0.1f));

            for (int i = 0; i < 14; i++)   // 링(수평 확산)
            {
                float a = (i / 14f) * Mathf.PI * 2f;
                var fx = MakeBit(pos + Vector3.up * 0.2f, 0.12f, col);
                fx.vel = new Vector3(Mathf.Cos(a), 0.15f, Mathf.Sin(a)) * 4.2f;
                fx.gravity = -1.2f; fx.life = 0.45f; fx.scaleVel = -0.12f; fx.spinDeg = 420f; fx.spinAxis = Random.onUnitSphere;
            }
            for (int i = 0; i < 6; i++)    // 분수(위로)
            {
                var fx = MakeBit(pos, 0.10f, Color.Lerp(col, Color.white, 0.5f));
                fx.vel = new Vector3(Random.Range(-0.6f, 0.6f), 3.2f + Random.value * 1.5f, Random.Range(-0.6f, 0.6f));
                fx.gravity = -7f; fx.life = 0.7f; fx.spinDeg = 300f; fx.spinAxis = Random.onUnitSphere;
            }
            Play(UseClip(), pos, 0.8f);
        }

        /// <summary>사용 순간 아이템 아이콘이 머리 위로 뿅 떠올라 흔들리며 사라짐 — 멀리서도 종류가 보인다.
        /// SpriteRenderer 대신 월드 캔버스(HeldItemBubble 방식) — UI 재질은 빌드에서 스트립 안 됨.</summary>
        public static void IconPop(Vector3 pos, CompetitiveItemKind kind)
        {
            var sp = HeldItemBubble.LoadIcon(kind);
            if (sp == null) return;
            const float kPx = 100f;
            var go = new GameObject("~ItemIconPop", typeof(Canvas));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 30;
            ((RectTransform)go.transform).sizeDelta = new Vector2(kPx, kPx);
            go.transform.position = pos + Vector3.up * 1.5f;

            var imgGo = new GameObject("Icon", typeof(UnityEngine.UI.Image));
            var irt = (RectTransform)imgGo.transform;
            irt.SetParent(go.transform, false);
            irt.sizeDelta = new Vector2(kPx, kPx);
            var img = imgGo.GetComponent<UnityEngine.UI.Image>();
            img.sprite = sp;
            img.preserveAspect = true;
            img.raycastTarget = false;

            var anim = go.AddComponent<ItemIconPopAnim>();
            anim.BaseScale = 0.9f / kPx;
            anim.Fade = img;
        }

        /// <summary>소멸(60초 미사용): 피식 가라앉는 연기 + 낮은 퐁.</summary>
        public static void Expired(Vector3 pos, Color col)
        {
            var gray = Color.Lerp(col, Color.gray, 0.6f);
            for (int i = 0; i < 6; i++)
            {
                var fx = MakeBit(pos + Random.insideUnitSphere * 0.2f, 0.10f, gray);
                fx.vel = new Vector3(Random.Range(-0.4f, 0.4f), 0.5f, Random.Range(-0.4f, 0.4f));
                fx.gravity = -0.8f; fx.life = 0.5f; fx.scaleVel = 0.25f; fx.startAlpha = 0.5f;
            }
            Play(FizzleClip(), pos, 0.95f);
        }

        /// <summary>월드 구슬 꾸미기: 뿅 팝인 + 둥실 부유 + 주기적 반짝이.</summary>
        // ── 대포: 발사 → 포물선 비행 → 착탄 콜백 ──
        public const float kCannonFlightSeconds = 0.55f;

        /// <summary>포탄 비행 연출. onLand = 착탄 순간 호출(폭발 FX는 호출자가).</summary>
        public static void CannonShot(Vector3 from, Vector3 to, System.Action onLand)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            var col = go.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);
            go.name = "~Cannonball";
            go.transform.localScale = Vector3.one * 0.3f;
            // 발사마다 new Material은 GameObject 파괴로 안 죽고 누적된다(런타임 생성 에셋) — 색 고정이라 공유 1개면 충분.
            if (s_CannonballMat == null)
            {
                var sh = Shader.Find("Universal Render Pipeline/Lit");
                if (sh != null) s_CannonballMat =
                    new Material(sh) { color = new Color(0.12f, 0.12f, 0.14f), hideFlags = HideFlags.HideAndDontSave };
            }
            if (s_CannonballMat != null) go.GetComponent<Renderer>().sharedMaterial = s_CannonballMat;
            var f = go.AddComponent<CannonballFlight>();
            f.From = from + Vector3.up * 0.8f;
            f.To = to;
            f.OnLand = onLand;
            Play(CannonFireClip(), from, 0.9f);
        }

        /// <summary>충전 시작음 — 충전 시간 동안 음이 올라가서 언제 완충되는지 귀로 알 수 있다.</summary>
        public static void CannonChargeSfx(Vector3 pos, float seconds) => Play(CannonChargeClip(seconds), pos, 0.55f);

        static AudioClip s_CannonCharge;
        static AudioClip CannonChargeClip(float seconds) => s_CannonCharge != null ? s_CannonCharge
            : s_CannonCharge = Synth("~CannonCharge", seconds, t =>
            {
                float k = Mathf.Clamp01(t / Mathf.Max(0.01f, seconds));
                float freq = Mathf.Lerp(180f, 720f, k * k);        // 뒤로 갈수록 급하게 올라감 = 임박감
                float env = Mathf.Min(1f, k * 8f) * Mathf.Lerp(0.35f, 1f, k);
                return Mathf.Sin(2f * Mathf.PI * freq * t) * env * 0.5f;
            });

        static Material s_CannonballMat;   // 포탄 공유 재질(색 고정) — 발사당 Material 누수 방지

        // 포성: 낮은 '펑' — 노이즈 버스트 + 90Hz 저음 감쇠
        static AudioClip s_CannonFire;
        static AudioClip CannonFireClip() => s_CannonFire != null ? s_CannonFire : s_CannonFire = Synth("~CannonFire", 0.3f, t =>
        {
            float r = Mathf.Sin(t * 91234.567f) * 43758.5453f;
            r -= Mathf.Floor(r);
            float noise = (r * 2f - 1f) * Mathf.Exp(-t * 18f) * 0.7f;
            float thump = Mathf.Sin(2f * Mathf.PI * 90f * t) * Mathf.Exp(-t * 9f) * 0.8f;
            return noise + thump;
        });

        static GameObject s_ShinyLoop;
        static bool s_ShinyTried;

        /// <summary>아이템 픽업 비주얼: 무지개 '?' 상자 — 무지개색 순환 + 4면 물음표 + 회전·둥실·팝인 + Shiny 루프 FX.</summary>
        public static GameObject MakeItemBox(Vector3 pos, Color kindColor)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = "~ItemBox";
            var col = box.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);
            box.transform.position = pos;
            box.transform.localScale = Vector3.one * 0.55f;

            // 빌드 셰이더 스트립 방지 — 명시적 URP Lit
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            var mat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            box.GetComponent<Renderer>().material = mat;

            // 4면 '?' — 내장 폰트 TextMesh(월드 텍스트, GridJuice와 같은 방식)
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            for (int i = 0; i < 4; i++)
            {
                var tgo = new GameObject("?");
                tgo.transform.SetParent(box.transform, false);
                var rot = Quaternion.Euler(0f, 90f * i, 0f);
                tgo.transform.localRotation = rot;
                tgo.transform.localPosition = rot * new Vector3(0f, 0f, -0.51f);
                var tm = tgo.AddComponent<TextMesh>();
                tm.text = "?";
                tm.font = font;
                tm.fontSize = 90;
                tm.fontStyle = FontStyle.Bold;
                tm.characterSize = 0.1f;
                tm.anchor = TextAnchor.MiddleCenter;
                tm.color = Color.white;
                tgo.GetComponent<MeshRenderer>().material = font.material;   // 폰트 아틀라스 머티리얼 필수
            }

            // 반짝이 루프(CFXR2 Shiny Item 사본) — 멀리서도 '아이템이다' 티가 나게
            if (!s_ShinyTried) { s_ShinyTried = true; s_ShinyLoop = Resources.Load<GameObject>("Fx/ItemShiny"); }
            if (s_ShinyLoop != null)
                Object.Instantiate(s_ShinyLoop, box.transform, false);

            var rainbow = box.AddComponent<ItemBoxRainbow>();
            rainbow.mat = mat;
            box.AddComponent<JuiceBob>();
            var tw = box.AddComponent<ItemOrbTwinkle>();
            tw.color = kindColor;
            box.AddComponent<ItemPopIn>();
            return box;
        }

        // ── 합성 사운드 (44.1kHz 모노, 최초 1회 생성 후 캐시) ──
        static AudioClip s_Sparkle, s_Coin, s_Use, s_Fizzle;

        static void Play(AudioClip clip, Vector3 pos, float vol)
        {
            if (clip != null) AudioSource.PlayClipAtPoint(clip, pos, vol);
        }

        static AudioClip Synth(string name, float dur, System.Func<float, float> wave)
        {
            const int sr = 44100;
            int n = Mathf.CeilToInt(sr * dur);
            var data = new float[n];
            for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(wave((float)i / sr), -1f, 1f);
            var clip = AudioClip.Create(name, n, 1, sr, false);
            clip.SetData(data, 0);
            return clip;
        }

        // 반짝: 고음 3음(G6-C7-E7)이 빠르게 타고 올라가며 각각 짧게 울린다 — 종/유리 느낌.
        // 낮은 음이 없어야 '두꺼운 뽁'이 아니라 '반짝'으로 들린다.
        static readonly float[] kSparkleNotes = { 1568f, 2093f, 2637f };
        static AudioClip SparkleClip() => s_Sparkle != null ? s_Sparkle : s_Sparkle = Synth("~ItemSparkle", 0.3f, t =>
        {
            float v = 0f;
            for (int i = 0; i < kSparkleNotes.Length; i++)
            {
                float start = i * 0.045f;
                if (t < start) continue;
                float local = t - start;
                float f = kSparkleNotes[i];
                // 사인 + 3배음 살짝(유리처럼 맑게) + 빠른 감쇠
                v += (Mathf.Sin(2f * Mathf.PI * f * local) + 0.25f * Mathf.Sin(6f * Mathf.PI * f * local))
                     * Mathf.Exp(-local * 13f) * 0.42f;
            }
            return v;
        });

        // 코인: B5 → E6 두 음(고전 동전 소리 진행). 사각파 성분을 섞어 금속처럼 쨍하게,
        // 첫 음은 아주 짧게 튕기고 둘째 음이 길게 울린다.
        static AudioClip CoinClip() => s_Coin != null ? s_Coin : s_Coin = Synth("~ItemCoin", 0.5f, t =>
        {
            const float kSwitch = 0.07f;
            float f = t < kSwitch ? 987.77f : 1318.5f;          // B5 → E6
            float local = t < kSwitch ? t : t - kSwitch;
            float decay = t < kSwitch ? 6f : 7.5f;              // 둘째 음이 길게 남음
            float phase = 2f * Mathf.PI * f * local;
            float sine = Mathf.Sin(phase);
            float square = Mathf.Sign(sine);                    // 금속성 쨍한 성분
            float body = sine * 0.65f + square * 0.35f;
            float attack = Mathf.Clamp01(local * 400f);         // 딱 튕기는 어택
            return body * attack * Mathf.Exp(-local * decay) * 0.85f;
        });

        // 발동: 220→980Hz 상승 스윕 + 살짝 배음 (뾰로롱↑)
        static AudioClip UseClip() => s_Use != null ? s_Use : s_Use = Synth("~ItemUse", 0.38f, t =>
        {
            float n = t / 0.38f;
            float f = Mathf.Lerp(220f, 980f, n * n);
            float env = Mathf.Sin(n * Mathf.PI);   // 페이드 인/아웃
            return (Mathf.Sin(2f * Mathf.PI * f * t) + 0.35f * Mathf.Sin(4f * Mathf.PI * f * t)) * env * 0.6f;
        });

        // 피식: 감쇠 노이즈 + 아래로 떨어지는 저음(김 빠지는 느낌). 너무 작게 들려서 소리를 키웠다.
        static AudioClip FizzleClip() => s_Fizzle != null ? s_Fizzle : s_Fizzle = Synth("~ItemFizzle", 0.45f, t =>
        {
            float r = Mathf.Sin(t * 12345.678f) * 43758.5453f;
            r -= Mathf.Floor(r);
            float noise = (r * 2f - 1f) * Mathf.Exp(-t * 7f) * 0.55f;
            float f = Mathf.Lerp(420f, 150f, t / 0.45f);                    // 축 처지는 하강음
            float tone = Mathf.Sin(2f * Mathf.PI * f * t) * Mathf.Exp(-t * 5f) * 0.45f;
            return noise + tone;
        });
    }

    /// <summary>포탄 포물선 비행 — 끝나면 OnLand 호출 후 자멸. 연기 꼬리를 흘린다.</summary>
    public sealed class CannonballFlight : MonoBehaviour
    {
        public Vector3 From, To;
        public System.Action OnLand;
        float m_T, m_Trail;

        void Update()
        {
            m_T += Time.deltaTime / ItemFx.kCannonFlightSeconds;
            if (m_T >= 1f) { OnLand?.Invoke(); Destroy(gameObject); return; }
            float h = Mathf.Max(2.5f, Vector3.Distance(From, To) * 0.3f);
            Vector3 p = Vector3.Lerp(From, To, m_T);
            p.y += Mathf.Sin(m_T * Mathf.PI) * h;
            transform.position = p;

            m_Trail -= Time.deltaTime;   // 연기 꼬리
            if (m_Trail <= 0f)
            {
                m_Trail = 0.04f;
                var b = GridJuice.MakeBit(p, 0.09f, new Color(0.6f, 0.6f, 0.6f));
                b.vel = Vector3.up * 0.3f; b.life = 0.35f; b.scaleVel = 0.3f; b.startAlpha = 0.5f;
            }
        }
    }

    /// <summary>구슬 반짝이: 주기적으로 작은 흰 조각이 표면에서 떠오르며 사라짐.</summary>
    public sealed class ItemOrbTwinkle : MonoBehaviour
    {
        public Color color = Color.white;
        float m_Next;

        void Update()
        {
            if (Time.time < m_Next) return;
            m_Next = Time.time + Random.Range(0.25f, 0.5f);
            var pos = transform.position + Random.onUnitSphere * 0.32f;
            var fx = ItemFx.MakeSparkPublic(pos, 0.05f, color);
            fx.vel = Vector3.up * 0.5f; fx.life = 0.45f; fx.spinDeg = 240f; fx.spinAxis = Random.onUnitSphere;
        }
    }

    /// <summary>무지개 상자: 색상환을 천천히 순환(회전·둥실은 JuiceBob 담당).</summary>
    public sealed class ItemBoxRainbow : MonoBehaviour
    {
        public Material mat;
        float m_Hue;

        void Start() => m_Hue = Random.value;   // 상자마다 다른 색에서 출발

        void Update()
        {
            m_Hue = Mathf.Repeat(m_Hue + Time.deltaTime * 0.25f, 1f);
            if (mat != null) mat.color = Color.HSVToRGB(m_Hue, 0.6f, 1f);
        }

        void OnDestroy() { if (mat != null) Destroy(mat); }
    }

    /// <summary>사용 아이콘 연출: 팝인 → 좌우 까딱 → 떠오르며 페이드. 항상 카메라를 본다.</summary>
    public sealed class ItemIconPopAnim : MonoBehaviour
    {
        public float BaseScale = 1f;
        public UnityEngine.UI.Image Fade;
        const float kPop = 0.18f, kHold = 0.75f, kFade = 0.4f;
        float m_T;

        void Update()
        {
            m_T += Time.deltaTime;
            if (Camera.main != null) transform.rotation = Camera.main.transform.rotation;

            float scale = BaseScale;
            if (m_T < kPop)   // 오버슛 팝인
            {
                float n = m_T / kPop;
                scale *= 1f + 2.7f * Mathf.Pow(n - 1f, 3f) + 1.7f * Mathf.Pow(n - 1f, 2f);
            }
            transform.localScale = Vector3.one * scale;
            // 까딱까딱(감쇠 회전) — 정적인 아이콘에 생기
            float wob = Mathf.Sin(m_T * 14f) * Mathf.Exp(-m_T * 3f) * 12f;
            transform.rotation *= Quaternion.Euler(0f, 0f, wob);

            transform.position += Vector3.up * (0.55f * Time.deltaTime);
            if (m_T > kPop + kHold && Fade != null)
            {
                float n = Mathf.Clamp01((m_T - kPop - kHold) / kFade);
                var c = Fade.color; c.a = 1f - n; Fade.color = c;
            }
            if (m_T >= kPop + kHold + kFade) Destroy(gameObject);
        }
    }

    /// <summary>뿅 팝인: 0 → 오버슛 → 원래 크기. 등장 쫀득용.</summary>
    public sealed class ItemPopIn : MonoBehaviour
    {
        const float kDur = 0.28f;
        Vector3 m_Base;
        float m_T;

        void Start() { m_Base = transform.localScale; transform.localScale = Vector3.zero; }

        void Update()
        {
            m_T += Time.deltaTime;
            float n = Mathf.Clamp01(m_T / kDur);
            // 오버슛 이징(back-out)
            float s = 1f + 2.7f * Mathf.Pow(n - 1f, 3f) + 1.7f * Mathf.Pow(n - 1f, 2f);
            transform.localScale = m_Base * s;
            if (n >= 1f) { transform.localScale = m_Base; Destroy(this); }
        }
    }
}
