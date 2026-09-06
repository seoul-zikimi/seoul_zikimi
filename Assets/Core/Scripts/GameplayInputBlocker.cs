/// <summary>키 설정처럼 입력을 캡처하는 화면이 열린 동안 월드/플레이어 입력을 막는 공용 플래그.</summary>
public static class GameplayInputBlocker
{
    /// <summary>읽기 = (수동 잠금 or 매치 시작 게이트). 쓰기 = 수동 잠금(기존 사용처 그대로).</summary>
    public static bool Blocked
    {
        get => s_Manual || MatchGateBlocked || TrailerBlocked;
        set => s_Manual = value;
    }
    /// <summary>촬영 카메라(TrailerCamera)가 켜진 동안 — 내 캐릭터가 WASD·클릭에 반응하지 않고 제자리에 선다.</summary>
    public static bool TrailerBlocked { get; set; }
    private static bool s_Manual;

    /// <summary>수동 잠금만(키 설정 팝업 등). 매치 게이트와 무관한 입력(동의 엔터 등)의 판정용.</summary>
    public static bool ManualBlocked => s_Manual;

    /// <summary>전원 로딩 대기 + 시작 카운트다운 동안의 입력 잠금(GameLoopManager가 관리).</summary>
    public static bool MatchGateBlocked { get; set; }
}
