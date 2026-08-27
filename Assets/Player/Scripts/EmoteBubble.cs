using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Player
{
    /// <summary>
    /// 머리 위 감정표현 팝(빌보드): 뿅 커졌다(오버슛) → 위로 두둥실 → 끝에 페이드아웃.
    /// ShowText = 대사 말풍선(기획서 감정표현 11종, 왼쪽 아이콘 선택), Show/ShowFull = 스프라이트(파티클 슬롯 폴백용).
    /// PlayerEmote가 띄우며, 수명 끝나면 스스로 파괴.
    /// </summary>
    public class EmoteBubble : MonoBehaviour
    {
        const float kLife = 1.8f;      // 스프라이트 팝 수명
        const float kTextLife = 2.5f;  // 대사 말풍선 수명(읽을 시간 + 보이스 길이 고려)
        const float kPop = 0.18f;      // 팝인 구간
        const float kFadePart = 0.3f;  // 마지막 30% = 페이드
        const float kRise = 0.6f;      // 수명 동안 떠오르는 높이
        const float kScale = 0.9f;     // 최종 크기(월드)
        const float kIcon = 0.6f;      // 대사 왼쪽 아이콘 한 변(월드)
        const float kIconGap = 0.12f;  // 아이콘↔대사 간격

        SpriteRenderer m_Sr;
        TMP_Text m_Text;
        SpriteRenderer m_TextBg;
        SpriteRenderer m_IconSr;
        float m_T;
        float m_Life = kLife;
        Vector3 m_StartPos;

        static Sprite s_BubbleSprite;   // 말풍선 배경(둥근 사각) — 절차 생성 1회 캐시
        static readonly Dictionary<Texture2D, Sprite> s_IconSprites = new Dictionary<Texture2D, Sprite>();

        // 아이콘 스프라이트는 텍스처당 1개만(감정표현마다 새로 만들면 Sprite가 쌓인다)
        static Sprite IconSprite(Texture2D tex)
        {
            if (s_IconSprites.TryGetValue(tex, out var sp) && sp != null) return sp;
            sp = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), tex.width);
            s_IconSprites[tex] = sp;   // 1픽셀=1/width 유닛 → 스프라이트 한 변 = 1유닛
            return sp;
        }

        /// <summary>대사 말풍선(흰 대사 + 반투명 검정 둥근 배경). 감정표현 대사용.</summary>
        public static void ShowText(string line, Vector3 pos) => ShowText(line, null, pos);

        /// <summary>대사 말풍선 + 왼쪽 아이콘(icon이 null이면 대사만 — 기존 동작 그대로).</summary>
        public static void ShowText(string line, Texture2D icon, Vector3 pos)
        {
            if (string.IsNullOrEmpty(line)) return;

            var go = new GameObject("~EmoteText");
            go.transform.position = pos;
            var eb = go.AddComponent<EmoteBubble>();
            eb.m_StartPos = pos;
            eb.m_Life = kTextLife;

            // 배경 말풍선(9슬라이스 없는 단순 둥근 사각 — 대사 길이에 맞춰 가로 스케일)
            var bg = new GameObject("Bg");
            bg.transform.SetParent(go.transform, false);
            var bgSr = bg.AddComponent<SpriteRenderer>();
            bgSr.sprite = BubbleSprite();
            bgSr.color = new Color(0f, 0f, 0f, 0.55f);
            bgSr.sortingOrder = 10;
            eb.m_TextBg = bgSr;

            // 대사 텍스트(TMP 월드 텍스트, 한국어 폰트)
            var txtGo = new GameObject("Line");
            txtGo.transform.SetParent(go.transform, false);
            var tmp = txtGo.AddComponent<TextMeshPro>();
            tmp.font = JobsnailUiKit.TmpFont;
            tmp.text = line;
            tmp.fontSize = 5f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            tmp.enableWordWrapping = false;
            tmp.sortingOrder = 11;
            var rt = (RectTransform)txtGo.transform;
            rt.sizeDelta = new Vector2(4f, 1f);
            eb.m_Text = tmp;

            // 아이콘(선택) — 말풍선 왼쪽에 붙는 이모티콘. 텍스처 한 장을 1x1 유닛 스프라이트로.
            tmp.ForceMeshUpdate();
            float textW = tmp.textBounds.size.x;
            float iconW = icon != null ? kIcon + kIconGap : 0f;
            if (icon != null)
            {
                var iconGo = new GameObject("Icon");
                iconGo.transform.SetParent(go.transform, false);
                var isr = iconGo.AddComponent<SpriteRenderer>();
                isr.sprite = IconSprite(icon);
                isr.sortingOrder = 11;
                iconGo.transform.localScale = Vector3.one * kIcon;
                iconGo.transform.localPosition = new Vector3(-(iconW + textW) * 0.5f + kIcon * 0.5f, 0f, 0f);
                eb.m_IconSr = isr;
            }

            // 아이콘 자리만큼 대사를 오른쪽으로 밀고, 배경은 실측 폭에 맞춰 스케일(여백 포함)
            txtGo.transform.localPosition = new Vector3(iconW * 0.5f, 0f, 0f);
            float w = iconW + textW + 0.55f;
            float h = tmp.textBounds.size.y + 0.35f;
            bg.transform.localScale = new Vector3(Mathf.Max(w, 1f), Mathf.Max(h, 0.7f), 1f);
        }

        public static void Show(Texture2D atlas, int spriteIndex, Vector3 pos)
        {
            if (atlas == null) return;
            int cols = 4;                                   // TMP EmojiOne = 4x4 아틀라스
            int cell = atlas.width / cols;
            int col = spriteIndex % cols, row = spriteIndex / cols;   // row 0 = 위쪽 줄
            var rect = new Rect(col * cell, atlas.height - (row + 1) * cell, cell, cell);
            Spawn(Sprite.Create(atlas, rect, new Vector2(0.5f, 0.5f), cell), pos);   // 1칸 = 1유닛
        }

        /// <summary>아틀라스가 아닌 통짜 텍스처 1장을 이모지로 띄움(개별 PNG).</summary>
        public static void ShowFull(Texture2D tex, Vector3 pos)
        {
            if (tex == null) return;
            Spawn(Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), tex.width), pos);
        }

        static void Spawn(Sprite sprite, Vector3 pos)
        {
            var go = new GameObject("~Emote");
            go.transform.position = pos;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            var eb = go.AddComponent<EmoteBubble>();
            eb.m_Sr = sr;
            eb.m_StartPos = pos;
        }

        // 둥근 사각 말풍선 스프라이트(1회 절차 생성 — 64x40, 모서리 r=12)
        static Sprite BubbleSprite()
        {
            if (s_BubbleSprite != null) return s_BubbleSprite;
            const int kW = 64, kH = 40, kR = 12;
            var tex = new Texture2D(kW, kH, TextureFormat.RGBA32, false);
            var px = new Color32[kW * kH];
            for (int y = 0; y < kH; y++)
                for (int x = 0; x < kW; x++)
                {
                    // 모서리 밖이면 투명(둥근 사각 판정)
                    int cx = Mathf.Clamp(x, kR, kW - 1 - kR);
                    int cy = Mathf.Clamp(y, kR, kH - 1 - kR);
                    float d = Vector2.Distance(new Vector2(x, y), new Vector2(cx, cy));
                    byte a = (byte)(d <= kR ? 255 : 0);
                    px[y * kW + x] = new Color32(255, 255, 255, a);
                }
            tex.SetPixels32(px);
            tex.Apply();
            s_BubbleSprite = Sprite.Create(tex, new Rect(0, 0, kW, kH), new Vector2(0.5f, 0.5f), kH);   // 세로 1유닛
            return s_BubbleSprite;
        }

        void LateUpdate()
        {
            m_T += Time.deltaTime;
            if (m_T >= m_Life) { Destroy(gameObject); return; }

            // 빌보드 + 젤리 흔들(±8° — 텍스트는 가독성 위해 ±3°)
            if (Camera.main != null)
            {
                float wobble = m_Text != null ? 3f : 8f;
                transform.rotation = Camera.main.transform.rotation
                                   * Quaternion.Euler(0f, 0f, Mathf.Sin(m_T * 7f) * wobble);
            }

            // 팝인(오버슛) → 유지
            float s = m_T < kPop
                ? Mathf.LerpUnclamped(0f, kScale, 1f + 0.6f * Mathf.Sin(Mathf.Clamp01(m_T / kPop) * Mathf.PI * 0.75f)) * Mathf.Clamp01(m_T / kPop)
                : kScale;
            transform.localScale = Vector3.one * s;

            // 두둥실 상승
            float n = m_T / m_Life;
            transform.position = m_StartPos + Vector3.up * (kRise * n);

            // 끝 페이드
            float a = n > 1f - kFadePart ? 1f - (n - (1f - kFadePart)) / kFadePart : 1f;
            if (m_Sr != null) m_Sr.color = new Color(1f, 1f, 1f, a);
            if (m_IconSr != null) m_IconSr.color = new Color(1f, 1f, 1f, a);
            if (m_Text != null) m_Text.color = new Color(1f, 1f, 1f, a);
            if (m_TextBg != null) m_TextBg.color = new Color(0f, 0f, 0f, 0.55f * a);
        }
    }
}
