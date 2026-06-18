/// <summary>
/// 커서가 정답 패널 위에 있는지(=정답 카메라가 입력을 가져갈지) 알리는 전역 플래그.
/// 정답 패널 라우터(Assembly-CSharp)가 매 프레임 set, 플레이어 카메라가 read해서 양보.
/// 완전 로컬 — 네트워크 동기화 없음(스펙 §7).
/// </summary>
public static class AnswerPanelFocus
{
    public static bool Active;
}
