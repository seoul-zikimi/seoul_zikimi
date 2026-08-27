using UnityEngine;

namespace Player
{
    /// <summary>
    /// 감정표현 아이콘 임시 아트(코드 생성) — 아트가 아직 없는 아이콘을 대신 그려준다.
    /// Resources/UI_pngs/Emotes/ 에 같은 이름 PNG를 넣으면 그쪽이 우선(EmoteDefs.Icon 참고).
    /// 텍스처는 이름당 1회만 만들고 캐시.
    /// </summary>
    internal static class EmoteIconArt
    {
        /// <summary>이름에 대응하는 임시 아이콘(없으면 null — 아이콘 없이 대사만 뜸).</summary>
        public static Texture2D Placeholder(string name)
        {
            return name == "Emote_Hammer" ? Hammer() : null;
        }

        private const int kSize = 128;          // 아이콘 한 변(px)
        private const float kTilt = 20f;        // 기울기(도) — 이모지 망치처럼 머리가 왼쪽 위
        private const float kEdge = 3.5f;       // 외곽선 두께(px)

        private static readonly Color32 kOutline    = new Color32(42, 33, 24, 255);
        private static readonly Color32 kHeadBody   = new Color32(158, 168, 180, 255);
        private static readonly Color32 kHeadFace   = new Color32(112, 123, 137, 255);   // 타격면(왼쪽)
        private static readonly Color32 kHeadLite   = new Color32(226, 234, 242, 255);   // 윗면 하이라이트
        private static readonly Color32 kHandleBody = new Color32(176, 116, 62, 255);
        private static readonly Color32 kHandleGrip = new Color32(139, 88, 43, 255);     // 손잡이 끝 그립

        private static Texture2D s_Hammer;

        // 망치: 머리(가로 상자) + 손잡이(세로 상자)를 둥근사각 SDF로 합쳐 그린다.
        // SDF라 안티에일리어싱이 공짜 — 픽셀 하나하나 경계까지의 거리로 색/알파를 섞는다.
        private static Texture2D Hammer()
        {
            if (s_Hammer != null) return s_Hammer;

            var px = new Color32[kSize * kSize];
            float rad = kTilt * Mathf.Deg2Rad;
            float cos = Mathf.Cos(rad), sin = Mathf.Sin(rad);
            const float kHalf = kSize * 0.5f;

            for (int y = 0; y < kSize; y++)
            {
                for (int x = 0; x < kSize; x++)
                {
                    // 중심 원점 + 기울기만큼 역회전 → 아이콘 로컬 좌표(위가 +y)
                    float ox = x + 0.5f - kHalf, oy = y + 0.5f - kHalf;
                    var p = new Vector2(ox * cos + oy * sin, -ox * sin + oy * cos);

                    float dHead   = RoundBox(p - new Vector2(0f, 28f), new Vector2(36f, 16f), 8f);
                    float dHandle = RoundBox(p - new Vector2(0f, -20f), new Vector2(10f, 34f), 9f);
                    float d = Mathf.Min(dHead, dHandle);
                    if (d > kEdge) continue;   // 모양 밖 — 투명 그대로

                    // 몸통 색(머리/손잡이 중 가까운 쪽) + 부위별 디테일
                    Color32 body;
                    if (dHead <= dHandle)
                    {
                        body = kHeadBody;
                        if (p.x < -14f) body = kHeadFace;                          // 타격면
                        else if (p.y > 34f && Mathf.Abs(p.x) < 28f) body = kHeadLite;   // 윗면 반짝
                    }
                    else
                    {
                        body = p.y < -38f ? kHandleGrip : kHandleBody;
                    }

                    // 경계 근처는 외곽선, 안쪽은 몸통 — 둘 다 경계에서 부드럽게
                    float line = Smooth(-kEdge, -kEdge + 1f, d);
                    float a = 1f - Smooth(kEdge - 1f, kEdge, d);
                    px[y * kSize + x] = Blend(body, kOutline, line, a);
                }
            }

            var tex = new Texture2D(kSize, kSize, TextureFormat.RGBA32, false)
            {
                name = "Emote_Hammer(generated)",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,   // 씬 전환에도 유지, 에디터에 안 남김
            };
            tex.SetPixels32(px);
            tex.Apply();
            s_Hammer = tex;
            return tex;
        }

        /// <summary>둥근 사각형 SDF — 중심 기준 p, 반크기 h, 모서리 반지름 r. 음수 = 내부.</summary>
        private static float RoundBox(Vector2 p, Vector2 h, float r)
        {
            float dx = Mathf.Abs(p.x) - h.x + r;
            float dy = Mathf.Abs(p.y) - h.y + r;
            float outside = new Vector2(Mathf.Max(dx, 0f), Mathf.Max(dy, 0f)).magnitude;
            return outside + Mathf.Min(Mathf.Max(dx, dy), 0f) - r;
        }

        private static float Smooth(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        private static Color32 Blend(Color32 body, Color32 line, float t, float alpha)
        {
            return new Color32(
                (byte)Mathf.Lerp(body.r, line.r, t),
                (byte)Mathf.Lerp(body.g, line.g, t),
                (byte)Mathf.Lerp(body.b, line.b, t),
                (byte)Mathf.RoundToInt(alpha * 255f));
        }
    }
}
