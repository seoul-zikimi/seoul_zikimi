using UnityEngine;

namespace Player
{
    /// <summary>
    /// 머리 위 이모지 팝(빌보드 스프라이트): 뿅 커졌다(오버슛) → 위로 두둥실 → 끝에 페이드아웃.
    /// PlayerEmote가 아틀라스 칸을 잘라 Show로 띄움. 수명 끝나면 스스로 파괴.
    /// </summary>
    public class EmoteBubble : MonoBehaviour
    {
        const float kLife = 1.8f;      // 전체 수명
        const float kPop = 0.18f;      // 팝인 구간
        const float kFadePart = 0.3f;  // 마지막 30% = 페이드
        const float kRise = 0.6f;      // 수명 동안 떠오르는 높이
        const float kScale = 0.9f;     // 최종 크기(월드)

        SpriteRenderer m_Sr;
        float m_T;
        Vector3 m_StartPos;

        public static void Show(Texture2D atlas, int spriteIndex, Vector3 pos)
        {
            if (atlas == null) return;
            int cols = 4;                                   // TMP EmojiOne = 4x4 아틀라스
            int cell = atlas.width / cols;
            int col = spriteIndex % cols, row = spriteIndex / cols;   // row 0 = 위쪽 줄
            var rect = new Rect(col * cell, atlas.height - (row + 1) * cell, cell, cell);
            Spawn(Sprite.Create(atlas, rect, new Vector2(0.5f, 0.5f), cell), pos);   // 1칸 = 1유닛
        }

        /// <summary>아틀라스가 아닌 통짜 텍스처 1장을 이모지로 띄움(붐따 등 개별 PNG).</summary>
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

        void LateUpdate()
        {
            m_T += Time.deltaTime;
            if (m_T >= kLife) { Destroy(gameObject); return; }

            // 빌보드 + 젤리 흔들(±8°)
            if (Camera.main != null)
                transform.rotation = Camera.main.transform.rotation
                                   * Quaternion.Euler(0f, 0f, Mathf.Sin(m_T * 7f) * 8f);

            // 팝인(오버슛) → 유지
            float s = m_T < kPop
                ? Mathf.LerpUnclamped(0f, kScale, 1f + 0.6f * Mathf.Sin(Mathf.Clamp01(m_T / kPop) * Mathf.PI * 0.75f)) * Mathf.Clamp01(m_T / kPop)
                : kScale;
            transform.localScale = Vector3.one * s;

            // 두둥실 상승
            float n = m_T / kLife;
            transform.position = m_StartPos + Vector3.up * (kRise * n);

            // 끝 페이드
            if (m_Sr != null)
            {
                float a = n > 1f - kFadePart ? 1f - (n - (1f - kFadePart)) / kFadePart : 1f;
                m_Sr.color = new Color(1f, 1f, 1f, a);
            }
        }
    }
}
