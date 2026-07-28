/// <summary>
/// 튜토리얼 진입 여부를 씬 전환 동안 들고 다니는 정적 플래그.
/// 로비(세션리스트)에서 팝업 "예" 선택 시 true로 설정 후 GameScene 로드.
/// GameLoopManager/GridManager/GameLoopHUD가 이 값을 보고 정상 게임 로직을 건너뛴다.
/// </summary>
public static class TutorialSession
{
    public static bool IsActive { get; set; }
}
