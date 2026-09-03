using SeoulZikimi.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace GridSystem
{
    /// <summary>
    /// 2vs2 아이템 화면 연출 — 상단 중앙 배너(아이콘+문구)와 화면 가장자리 비네트 플래시.
    /// 월드 토스트(GridJuice.WorldToast)는 시전 위치라 시야 밖이면 놓친다(QA) — 반드시 보이는
    /// 스크린 스페이스로 알린다. 전부 코드 생성(프리팹 불필요), 씬 오브젝트라 전환 시 자동 정리.
    /// </summary>
    public static class ItemScreenFx
    {
        static Canvas s_Canvas;
        static RectTransform s_BannerRoot;
        static Image s_Vignette;
        static Sprite s_RoundRect, s_VignetteSprite;

        /// <summary>상단 배너: 아이콘 + 문구. shake = 피격 강조(좌우 덜덜).</summary>
        public static void Banner(CompetitiveItemKind kind, string text, Color color, bool shake = false)
        {
            if (!Ensure()) return;

            var go = new GameObject("~ItemBanner", typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(s_BannerRoot, false);
            rt.sizeDelta = new Vector2(617f, 89f);

            var bg = go.GetComponent<Image>();
            bg.sprite = RoundRect();
            bg.type = Image.Type.Sliced;
            bg.color = new Color(color.r, color.g, color.b, 0.92f);
            bg.raycastTarget = false;

            // 아이콘(왼쪽) — 없으면 종류색 동그라미 폴백
            var iconGo = new GameObject("Icon", typeof(Image));
            var irt = (RectTransform)iconGo.transform;
            irt.SetParent(rt, false);
            irt.anchorMin = irt.anchorMax = new Vector2(0f, 0.5f);
            irt.anchoredPosition = new Vector2(55f, 0f);
            irt.sizeDelta = new Vector2(66f, 66f);
            var icon = iconGo.GetComponent<Image>();
            icon.raycastTarget = false;
            icon.preserveAspect = true;
            var sp = HeldItemBubble.LoadIcon(kind);
            if (sp != null) icon.sprite = sp;
            else { icon.sprite = RoundRect(); icon.color = ItemNetwork.KindColor(kind); }

            // 문구(아이콘 오른쪽 나머지 영역)
            var txtGo = new GameObject("Text", typeof(Text));
            var trt = (RectTransform)txtGo.transform;
            trt.SetParent(rt, false);
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(100f, 0f); trt.offsetMax = new Vector2(-20f, 0f);
            var txt = txtGo.GetComponent<Text>();
            txt.font = BannerFont();
            txt.fontSize = 37;
            txt.fontStyle = FontStyle.Bold;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = Color.white;
            txt.raycastTarget = false;
            txt.horizontalOverflow = HorizontalWrapMode.Overflow;
            txt.text = text;
            var shadow = txtGo.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.45f);
            shadow.effectDistance = new Vector2(1.5f, -1.5f);

            var anim = go.AddComponent<ItemBannerAnim>();
            anim.Shake = shake;
        }

        /// <summary>화면 가장자리 플래시 — 피격(빨강)/버프(초록) 등 순간 강조.</summary>
        public static void Flash(Color color, float strength = 0.6f)
        {
            if (!Ensure()) return;
            s_Vignette.color = new Color(color.r, color.g, color.b, Mathf.Clamp01(strength));
            var fade = s_Vignette.GetComponent<ItemVignetteFade>();
            if (fade == null) fade = s_Vignette.gameObject.AddComponent<ItemVignetteFade>();
            fade.Restart();
        }

        // ── 내부 구성 ────────────────────────────────────────────
        static bool Ensure()
        {
            if (s_Canvas != null) return true;

            var go = new GameObject("~ItemScreenFx", typeof(Canvas), typeof(CanvasScaler));
            s_Canvas = go.GetComponent<Canvas>();
            s_Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            s_Canvas.sortingOrder = 450;   // HUD 위, 팝업(설정 등)보단 아래 취지의 오버레이
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);   // 전 캔버스 공통 기준(해상도 대응 통일)
            scaler.matchWidthOrHeight = 0.5f;

            // 비네트(배너보다 아래 깔림)
            var vgo = new GameObject("Vignette", typeof(Image));
            var vrt = (RectTransform)vgo.transform;
            vrt.SetParent(go.transform, false);
            vrt.anchorMin = Vector2.zero; vrt.anchorMax = Vector2.one;
            vrt.offsetMin = Vector2.zero; vrt.offsetMax = Vector2.zero;
            s_Vignette = vgo.GetComponent<Image>();
            s_Vignette.sprite = VignetteSprite();
            s_Vignette.color = Color.clear;
            s_Vignette.raycastTarget = false;

            // 배너 스택(상단 중앙, 위에서 아래로)
            var bgo = new GameObject("Banners", typeof(RectTransform));
            s_BannerRoot = (RectTransform)bgo.transform;
            s_BannerRoot.SetParent(go.transform, false);
            s_BannerRoot.anchorMin = s_BannerRoot.anchorMax = new Vector2(0.5f, 1f);
            s_BannerRoot.pivot = new Vector2(0.5f, 1f);
            s_BannerRoot.anchoredPosition = new Vector2(0f, -287f);   // 타이머 → 점수줄 → 버프바(쿨타임) 아래 순서
            s_BannerRoot.sizeDelta = new Vector2(617f, 0f);
            var layout = bgo.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.spacing = 8f;
            layout.childControlWidth = false; layout.childControlHeight = false;
            layout.childForceExpandWidth = false; layout.childForceExpandHeight = false;
            return true;
        }

        static Font BannerFont()
        {
            var f = Resources.Load<Font>("Fonts/서울한강 장체M");
            return f != null ? f : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        // 둥근 사각(슬라이스) — HeldItemBubble.RoundSprite와 같은 기법이나 border를 넣어 9-slice.
        static Sprite RoundRect()
        {
            if (s_RoundRect != null) return s_RoundRect;
            const int kW = 64, kR = 16;
            var tex = new Texture2D(kW, kW, TextureFormat.RGBA32, false);
            var px = new Color32[kW * kW];
            for (int y = 0; y < kW; y++)
                for (int x = 0; x < kW; x++)
                {
                    int cx = Mathf.Clamp(x, kR, kW - 1 - kR);
                    int cy = Mathf.Clamp(y, kR, kW - 1 - kR);
                    float d = Vector2.Distance(new Vector2(x, y), new Vector2(cx, cy));
                    float a = Mathf.Clamp01(kR - d + 1f);   // 1px 안티에일리어싱
                    px[y * kW + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            tex.SetPixels32(px); tex.Apply();
            s_RoundRect = Sprite.Create(tex, new Rect(0, 0, kW, kW), new Vector2(0.5f, 0.5f), kW,
                0, SpriteMeshType.FullRect, new Vector4(kR + 2, kR + 2, kR + 2, kR + 2));
            return s_RoundRect;
        }

        // 비네트: 중앙 투명 → 가장자리로 갈수록 진해지는 방사형 알파(흰색 — 틴트로 색 결정)
        static Sprite VignetteSprite()
        {
            if (s_VignetteSprite != null) return s_VignetteSprite;
            const int kW = 128;
            var tex = new Texture2D(kW, kW, TextureFormat.RGBA32, false);
            var px = new Color32[kW * kW];
            var c = new Vector2(kW * 0.5f, kW * 0.5f);
            float maxD = c.magnitude;
            for (int y = 0; y < kW; y++)
                for (int x = 0; x < kW; x++)
                {
                    float n = Vector2.Distance(new Vector2(x, y), c) / maxD;   // 0(중앙)~1(모서리)
                    float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1.05f, n));
                    px[y * kW + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            tex.SetPixels32(px); tex.Apply();
            tex.wrapMode = TextureWrapMode.Clamp;
            s_VignetteSprite = Sprite.Create(tex, new Rect(0, 0, kW, kW), new Vector2(0.5f, 0.5f), kW);
            return s_VignetteSprite;
        }
    }

    /// <summary>배너 생명주기: 뿅 팝인 → 유지 → 페이드아웃(살짝 상승). Shake면 초반 좌우 덜덜.</summary>
    internal sealed class ItemBannerAnim : MonoBehaviour
    {
        public bool Shake;
        const float kPop = 0.22f, kHold = 2.0f, kFade = 0.45f;
        float m_T;
        CanvasGroup m_Group;

        void Awake() => m_Group = gameObject.AddComponent<CanvasGroup>();

        void Update()
        {
            m_T += Time.deltaTime;
            if (m_T < kPop)   // 오버슛 팝인
            {
                float n = m_T / kPop;
                float s = 1f + 2.7f * Mathf.Pow(n - 1f, 3f) + 1.7f * Mathf.Pow(n - 1f, 2f);
                transform.localScale = new Vector3(s, s, 1f);
            }
            else transform.localScale = Vector3.one;

            // 피격 강조: 팝인 직후 0.35초 좌우 덜덜(감쇠)
            if (Shake && m_T < kPop + 0.35f)
            {
                float st = m_T - kPop;
                float amp = st < 0f ? 0f : Mathf.Exp(-st * 9f) * 9f;
                var lp = transform.localPosition;
                lp.x = Mathf.Sin(m_T * 70f) * amp;
                transform.localPosition = lp;
            }

            float end = kPop + kHold + kFade;
            if (m_T > kPop + kHold)
            {
                float n = Mathf.Clamp01((m_T - kPop - kHold) / kFade);
                m_Group.alpha = 1f - n;
                var lp = transform.localPosition;
                lp.y += Time.deltaTime * 40f;   // 살짝 떠오르며 사라짐
                transform.localPosition = lp;
            }
            if (m_T >= end) Destroy(gameObject);
        }
    }

    /// <summary>비네트 알파 감쇠 — Flash가 색을 세팅하고 Restart를 부른다.</summary>
    internal sealed class ItemVignetteFade : MonoBehaviour
    {
        const float kLife = 0.55f;
        float m_T;
        Image m_Img;
        float m_StartA;

        public void Restart()
        {
            m_T = 0f;
            m_Img = GetComponent<Image>();
            m_StartA = m_Img != null ? m_Img.color.a : 0f;
            enabled = true;
        }

        void Update()
        {
            if (m_Img == null) { enabled = false; return; }
            m_T += Time.deltaTime;
            float n = Mathf.Clamp01(m_T / kLife);
            var c = m_Img.color;
            c.a = m_StartA * (1f - n * n);   // 처음 확 → 끝 천천히
            m_Img.color = c;
            if (n >= 1f) enabled = false;
        }
    }
}
