/// <summary>
/// 조작 안내 문구에 쓰는 키 이름 — 데스크톱 "E", 모바일은 화면의 "공정 버튼".
/// 모바일 판정(MobileControlsHUD.ShouldUseMobileUI)은 Assembly-CSharp에 있어 GridSystem 등 하위 어셈블리가 직접 못 읽으므로,
/// MobileControlsHUD가 매 프레임 여기 값을 갱신하고 안내 문구를 만드는 쪽은 이 값만 읽는다.
/// </summary>
public static class InputHintText
{
    public const string DesktopProcessKey = "E";
    public const string MobileProcessKey = "공정 버튼";

    /// <summary>공정(E 꾹) 안내에 쓸 키 이름. 기본은 데스크톱.</summary>
    public static string ProcessKey { get; set; } = DesktopProcessKey;
}
