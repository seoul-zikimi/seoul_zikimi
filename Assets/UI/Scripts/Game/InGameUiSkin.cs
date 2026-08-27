using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 인게임 UI 리마스터 공용 헬퍼 — 피그마 '인게임 UI / 완성본 모습'(1338x753 프레임) 좌표를 1920x1080 캔버스로 옮긴다.
/// 스프라이트: Resources/UI_pngs/3.inGame/Remaster/ (피그마 1x 익스포트 · 영문 파일명).
/// 배치 규칙: 피그마 좌상단 기준 (x, y, w, h) 을 그대로 넘기면 부모 모서리 앵커 + 스케일 S 적용.
/// </summary>
public static class InGameUiSkin
{
    /// <summary>피그마 프레임(1338x753) → 캔버스 기준 해상도(1920x1080) 배율.</summary>
    public const float S = 1920f / 1338f;
    public const float FrameW = 1338f, FrameH = 753f;

    public const string Root = "UI_pngs/3.inGame/Remaster/";

    // 피그마 팔레트(에셋에서 샘플)
    public static readonly Color Orange   = new Color32(255, 93, 18, 255);    // 버튼·뱃지 주황
    public static readonly Color TextGray = new Color32(99, 102, 108, 255);   // 폰 본문 텍스트
    public static readonly Color CardGray = new Color32(190, 195, 205, 255);  // 재료 카드 바탕
    public static readonly Color Consent  = new Color(0.56f, 0.86f, 0.48f);   // 종료 동의 상태(기존 색 유지)

    public static Sprite Load(string name) => Resources.Load<Sprite>(Root + name);

    /// <summary>리마스터 에셋이 임포트돼 있는지(없으면 구 비주얼 폴백).</summary>
    public static bool Available => Load("PhoneBg") != null;

    // ── 배치: 피그마 px(좌상단 원점) → 부모 모서리 앵커. 반환 rect 는 pivot 도 같은 모서리 ──
    public static RectTransform TopLeft(RectTransform rt, float x, float y, float w, float h)
        => Place(rt, new Vector2(0, 1), new Vector2(x * S, -y * S), new Vector2(w * S, h * S));

    /// <summary>x = 피그마 프레임 좌상단 기준 x (오른쪽 거리는 내부에서 계산).</summary>
    public static RectTransform TopRight(RectTransform rt, float x, float y, float w, float h, float frameW = FrameW)
        => Place(rt, new Vector2(1, 1), new Vector2(-(frameW - x - w) * S, -y * S), new Vector2(w * S, h * S));

    public static RectTransform BottomRight(RectTransform rt, float x, float y, float w, float h, float frameW = FrameW, float frameH = FrameH)
        => Place(rt, new Vector2(1, 0), new Vector2(-(frameW - x - w) * S, (frameH - y - h) * S), new Vector2(w * S, h * S));

    public static RectTransform TopCenter(RectTransform rt, float x, float y, float w, float h, float frameW = FrameW)
        => Place(rt, new Vector2(0.5f, 1), new Vector2((x + w * 0.5f - frameW * 0.5f) * S, -y * S), new Vector2(w * S, h * S), new Vector2(0.5f, 1));

    private static RectTransform Place(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size, Vector2? pivot = null)
    {
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = pivot ?? anchor;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        return rt;
    }

    /// <summary>스프라이트 이미지 자식 생성(피그마 px 크기 그대로 · 비율 왜곡 없음).</summary>
    public static Image SpriteImage(string name, Transform parent, string spriteName, bool raycast = false)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image)) { layer = 5 };
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.sprite = Load(spriteName);
        img.color = Color.white;
        img.preserveAspect = false;
        img.raycastTarget = raycast;
        return img;
    }
}
