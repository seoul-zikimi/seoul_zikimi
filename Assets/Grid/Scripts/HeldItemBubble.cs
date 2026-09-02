using System.Collections.Generic;
using SeoulZikimi.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace GridSystem
{
    /// <summary>
    /// 마리오카트식 '소지 아이템' 월드 UI — 플레이어 머리 위(공정바보다 높게) 월드 스페이스
    /// 캔버스 버블로 지금 든 경쟁 아이템 아이콘을 띄운다. ItemNetwork가 복제 목록을 보고
    /// 모든 클라에서 관리(자기 것도, 상대 것도 보임).
    /// 아이콘: 날씨 4종은 기존 UI_NEW/Weather/UI 재사용, 나머지는 UI_NEW/Items/{Kind}.png.
    /// 아이콘이 없으면 종류색 버블 + 이름 텍스트 폴백(에셋 임포트 전에도 동작).
    /// </summary>
    public class HeldItemBubble : MonoBehaviour
    {
        const float kHeight = 2.75f;   // 공정바(2.2)보다 한 층 위
        const float kScale = 0.8f;     // 버블 한 변(월드)
        const float kCanvasPx = 100f;  // 캔버스 픽셀 크기(월드 크기는 스케일로 환산)
        const float kPop = 0.2f;

        Transform m_Target;
        Image m_Bg, m_Icon;
        Text m_Fallback;
        CompetitiveItemKind m_Kind = (CompetitiveItemKind)(-1);
        float m_T;

        static Sprite s_Round;
        static readonly Dictionary<CompetitiveItemKind, Sprite> s_Icons = new();

        public static HeldItemBubble Create(Transform target, CompetitiveItemKind kind)
        {
            var go = new GameObject("~HeldItem", typeof(Canvas));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 20;
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(kCanvasPx, kCanvasPx);

            var b = go.AddComponent<HeldItemBubble>();
            b.m_Target = target;
            b.Build(rt);
            b.SetKind(kind);
            return b;
        }

        void Build(RectTransform root)
        {
            m_Bg = MakeImage(root, "Bg", kCanvasPx);
            m_Bg.sprite = RoundSprite();
            m_Bg.color = new Color(1f, 1f, 1f, 0.92f);

            m_Icon = MakeImage(root, "Icon", kCanvasPx * 0.86f);
            m_Icon.preserveAspect = true;

            var txtGo = new GameObject("Name", typeof(Text));
            txtGo.transform.SetParent(root, false);
            ((RectTransform)txtGo.transform).sizeDelta = new Vector2(kCanvasPx, kCanvasPx);
            m_Fallback = txtGo.GetComponent<Text>();
            var f = Resources.Load<Font>("Fonts/서울한강 장체M");
            if (f == null) f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            m_Fallback.font = f;
            m_Fallback.fontSize = 22;
            m_Fallback.alignment = TextAnchor.MiddleCenter;
            m_Fallback.color = Color.white;
        }

        static Image MakeImage(RectTransform parent, string name, float size)
        {
            var go = new GameObject(name, typeof(Image));
            go.transform.SetParent(parent, false);
            ((RectTransform)go.transform).sizeDelta = new Vector2(size, size);
            return go.GetComponent<Image>();
        }

        /// <summary>든 아이템 종류 반영(같으면 무시). 아이콘 없으면 색 버블+이름.</summary>
        public void SetKind(CompetitiveItemKind kind)
        {
            if (m_Kind == kind) return;
            m_Kind = kind;
            var sp = LoadIcon(kind);
            if (sp != null)
            {
                m_Icon.sprite = sp;
                m_Icon.enabled = true;
                m_Fallback.text = "";
                m_Bg.color = new Color(1f, 1f, 1f, 0.92f);
            }
            else
            {
                m_Icon.enabled = false;
                m_Fallback.text = ItemNetwork.KindName(kind);
                var c = ItemNetwork.KindColor(kind);
                m_Bg.color = new Color(c.r, c.g, c.b, 0.92f);
            }
        }

        void LateUpdate()
        {
            if (m_Target == null) { Destroy(gameObject); return; }
            m_T += Time.deltaTime;

            float bob = Mathf.Sin(m_T * 2.4f) * 0.06f;
            transform.position = m_Target.position + Vector3.up * (kHeight + bob);
            if (Camera.main != null)
                transform.rotation = Camera.main.transform.rotation;

            float pop = m_T < kPop   // 뿅 팝인(오버슛)
                ? (m_T / kPop) * (1f + 0.5f * Mathf.Sin(m_T / kPop * Mathf.PI))
                : 1f;
            transform.localScale = Vector3.one * (kScale / kCanvasPx) * pop;
        }

        // 플랫 아이콘 세트(UI_NEW/Items)가 13종 전부를 덮는다. 날씨 4종은 세트가 비어 있을 때만
        // 기존 날씨 UI 아이콘으로 폴백(세트 재생성 전에도 동작).
        static string FallbackPath(CompetitiveItemKind k) => k switch
        {
            CompetitiveItemKind.Rain => "UI_NEW/Weather/UI/Rain",
            CompetitiveItemKind.Snow => "UI_NEW/Weather/UI/Snow",
            CompetitiveItemKind.StrongWind => "UI_NEW/Weather/UI/StrongWind",
            CompetitiveItemKind.Typhoon => "UI_NEW/Weather/UI/Typhoon",
            _ => null,
        };

        /// <summary>아이템 종류 아이콘(HUD 버프 바·배너도 같이 씀). 없으면 null.</summary>
        public static Sprite LoadIcon(CompetitiveItemKind kind)
        {
            if (s_Icons.TryGetValue(kind, out var cached) && cached != null) return cached;
            var sp = LoadSprite("UI_NEW/Items/" + kind);
            if (sp == null && FallbackPath(kind) is string fb) sp = LoadSprite(fb);
            if (sp != null) s_Icons[kind] = sp;
            return sp;
        }

        static Sprite LoadSprite(string path)
        {
            var sp = Resources.Load<Sprite>(path);
            if (sp == null)
            {
                var tex = Resources.Load<Texture2D>(path);
                if (tex != null)
                    sp = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), tex.width);
            }
            return sp;
        }

        // 둥근 사각 버블 스프라이트(1회 절차 생성 — EmoteBubble과 같은 기법, 정사각)
        static Sprite RoundSprite()
        {
            if (s_Round != null) return s_Round;
            const int kW = 64, kR = 14;
            var tex = new Texture2D(kW, kW, TextureFormat.RGBA32, false);
            var px = new Color32[kW * kW];
            for (int y = 0; y < kW; y++)
                for (int x = 0; x < kW; x++)
                {
                    int cx = Mathf.Clamp(x, kR, kW - 1 - kR);
                    int cy = Mathf.Clamp(y, kR, kW - 1 - kR);
                    float d = Vector2.Distance(new Vector2(x, y), new Vector2(cx, cy));
                    px[y * kW + x] = new Color32(255, 255, 255, (byte)(d <= kR ? 255 : 0));
                }
            tex.SetPixels32(px);
            tex.Apply();
            s_Round = Sprite.Create(tex, new Rect(0, 0, kW, kW), new Vector2(0.5f, 0.5f), kW);
            return s_Round;
        }
    }
}
