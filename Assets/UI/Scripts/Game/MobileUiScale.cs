using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 모바일 기기 물리 크기(폰 vs 태블릿)별 UI 확대 배율.
/// 캔버스는 전부 1920x1080 저작 기준을 쓰므로 태블릿과 폰에서 UI가 같은 '논리' 크기로 나오는데,
/// 폰은 화면이 물리적으로 작아 터치 타깃이 손가락보다 작아진다 — 대각선 인치 기준으로 폰일수록 키운다.
/// 전체화면 레이아웃(정답 폰·정산서)은 잘림 위험이 있으니 적용하지 말고, 가장자리 앵커 컨트롤 캔버스에만 쓸 것.
/// </summary>
public static class MobileUiScale
{
    public const float kRefW = 1920f, kRefH = 1080f;

    /// <summary>태블릿(8인치 이상) = 1배, 작은 폰(5.5인치 이하) = 1.3배, 사이는 보간. PC/에디터 = 1배.</summary>
    public static float Factor
    {
        get
        {
            if (!MobileControlsHUD.ShouldUseMobileUI) return 1f;
            float d = DiagonalInches();
            if (d <= 0f)   // DPI를 모르면 비율로 추정 — 폰은 17:9 이상, 태블릿은 4:3~16:10
                return Aspect() >= 1.7f ? 1.25f : 1f;
            return Mathf.Lerp(1.3f, 1f, Mathf.InverseLerp(5.5f, 8f, d));
        }
    }

    /// <summary>대각선 8인치 미만이면 폰으로 본다(DPI 없으면 화면 비율로 추정).</summary>
    public static bool IsPhone
    {
        get
        {
            if (!MobileControlsHUD.ShouldUseMobileUI) return false;
            float d = DiagonalInches();
            return d > 0f ? d < 8f : Aspect() >= 1.7f;
        }
    }

    /// <summary>1920x1080 저작 기준을 유지한 채 기기 배율만큼 레퍼런스를 줄여(=UI를 키워) 적용.</summary>
    public static void Apply(CanvasScaler scaler)
    {
        if (scaler == null) return;
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        scaler.referenceResolution = new Vector2(kRefW, kRefH) / Factor;
    }

    private static float Aspect()
        => (float)Mathf.Max(Screen.width, Screen.height) / Mathf.Max(1, Mathf.Min(Screen.width, Screen.height));

    private static float DiagonalInches()
    {
        float dpi = Screen.dpi;
        if (dpi <= 0f) return -1f;
        float w = Screen.width / dpi, h = Screen.height / dpi;
        return Mathf.Sqrt(w * w + h * h);
    }
}
