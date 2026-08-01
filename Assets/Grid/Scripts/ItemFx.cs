using UnityEngine;
using UnityEngine.Rendering;

namespace GridSystem
{
    /// <summary>
    /// 2vs2 경쟁 아이템 전용 FX — 파티클·사운드 전부 코드 생성(프리팹/에셋 불필요).
    /// 파티클은 GridJuice.JuiceParticle 재사용, 사운드는 사인/노이즈 합성 AudioClip 캐시.
    /// 등장(팡+반짝) · 획득(모임+블링) · 발동(링 충격파+스윕) · 소멸(피식).
    /// </summary>
    public static class ItemFx
    {
        // ── 파티클 조각 (GridJuice.FxMat과 동일 컨셉 — 투명 URP Lit) ──
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
            go.name = "~ItemFx";
            var c = go.GetComponent<Collider>();
            if (c != null) c.enabled = false;
            go.transform.position = pos;
            go.transform.localScale = Vector3.one * size;
            var mat = FxMat();
            if (mat != null) go.GetComponent<Renderer>().sharedMaterial = mat;
            var fx = go.AddComponent<JuiceParticle>();
            fx.color = col;
            return fx;
        }

        // ── 이벤트 FX ────────────────────────────────────────────
        /// <summary>등장: 아래에서 반짝이 팡 + 팝 사운드.</summary>
        public static void Spawned(Vector3 pos, Color col)
        {
            for (int i = 0; i < 8; i++)
            {
                float a = (i / 8f) * Mathf.PI * 2f;
                var fx = MakeBit(pos, 0.09f, col);
                fx.vel = new Vector3(Mathf.Cos(a), 1.6f + Random.value, Mathf.Sin(a)) * 1.1f;
                fx.gravity = -3.5f; fx.life = 0.5f; fx.spinDeg = 320f; fx.spinAxis = Random.onUnitSphere;
            }
            Play(PopClip(), pos, 0.55f);
        }

        /// <summary>획득: 조각이 위로 모이며 사라짐 + 동전 블링.</summary>
        public static void PickedUp(Vector3 pos, Color col)
        {
            for (int i = 0; i < 10; i++)
            {
                var d = Random.insideUnitSphere; d.y = Mathf.Abs(d.y) + 0.6f;
                var fx = MakeBit(pos + Random.insideUnitSphere * 0.25f, 0.08f, col);
                fx.vel = d.normalized * (1.4f + Random.value * 1.2f);
                fx.gravity = 2.5f;   // 위로 가속(모여 올라가는 느낌)
                fx.life = 0.35f; fx.scaleVel = -0.15f; fx.spinDeg = 260f; fx.spinAxis = Random.onUnitSphere;
            }
            Play(BlingClip(), pos, 0.7f);
        }

        /// <summary>발동: 수평 링 충격파 + 위 분수 + 상승 스윕 사운드.</summary>
        public static void Used(Vector3 pos, Color col)
        {
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
            Play(FizzleClip(), pos, 0.4f);
        }

        /// <summary>월드 구슬 꾸미기: 뿅 팝인 + 둥실 부유 + 주기적 반짝이.</summary>
        public static void DecorateOrb(GameObject orb, Color col)
        {
            if (orb == null) return;
            orb.AddComponent<JuiceBob>();
            var tw = orb.AddComponent<ItemOrbTwinkle>();
            tw.color = col;
            orb.AddComponent<ItemPopIn>();
        }

        // ── 합성 사운드 (44.1kHz 모노, 최초 1회 생성 후 캐시) ──
        static AudioClip s_Pop, s_Bling, s_Use, s_Fizzle;

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

        // 팡: 사인 300→120Hz 급강하 + 지수 감쇠 (뽁)
        static AudioClip PopClip() => s_Pop != null ? s_Pop : s_Pop = Synth("~ItemPop", 0.16f, t =>
        {
            float f = Mathf.Lerp(300f, 120f, t / 0.16f);
            return Mathf.Sin(2f * Mathf.PI * f * t) * Mathf.Exp(-t * 22f);
        });

        // 블링: 880Hz → 1320Hz 두 음 동전 소리
        static AudioClip BlingClip() => s_Bling != null ? s_Bling : s_Bling = Synth("~ItemBling", 0.26f, t =>
        {
            float f = t < 0.08f ? 880f : 1318.5f;   // A5 → E6
            float local = t < 0.08f ? t : t - 0.08f;
            return Mathf.Sin(2f * Mathf.PI * f * t) * Mathf.Exp(-local * 16f) * 0.8f;
        });

        // 발동: 220→980Hz 상승 스윕 + 살짝 배음 (뾰로롱↑)
        static AudioClip UseClip() => s_Use != null ? s_Use : s_Use = Synth("~ItemUse", 0.38f, t =>
        {
            float n = t / 0.38f;
            float f = Mathf.Lerp(220f, 980f, n * n);
            float env = Mathf.Sin(n * Mathf.PI);   // 페이드 인/아웃
            return (Mathf.Sin(2f * Mathf.PI * f * t) + 0.35f * Mathf.Sin(4f * Mathf.PI * f * t)) * env * 0.6f;
        });

        // 피식: 감쇠 노이즈(결정적 의사난수 — 캐시라 매번 동일해도 무방)
        static AudioClip FizzleClip() => s_Fizzle != null ? s_Fizzle : s_Fizzle = Synth("~ItemFizzle", 0.3f, t =>
        {
            float r = Mathf.Sin(t * 12345.678f) * 43758.5453f;
            r -= Mathf.Floor(r);
            return (r * 2f - 1f) * Mathf.Exp(-t * 12f) * 0.4f;
        });
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
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "~Twinkle";
            var c = go.GetComponent<Collider>();
            if (c != null) c.enabled = false;
            go.transform.position = pos;
            go.transform.localScale = Vector3.one * 0.05f;
            var fx = go.AddComponent<JuiceParticle>();
            fx.color = Color.Lerp(color, Color.white, 0.7f);
            fx.vel = Vector3.up * 0.5f; fx.life = 0.45f; fx.spinDeg = 240f; fx.spinAxis = Random.onUnitSphere;
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
