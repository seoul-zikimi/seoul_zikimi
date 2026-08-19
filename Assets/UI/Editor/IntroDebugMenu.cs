using UnityEditor;
using UnityEngine;

/// <summary>테스트 편의용 메뉴 — 인트로 컷씬 완료 플래그(save.es3의 introSeen)를 지워서 다음 실행 때 다시 뜨게 한다.</summary>
public static class IntroDebugMenu
{
    private const string kFile = "save.es3";
    private const string kIntroSeen = "introSeen";
    private const string kCharacters = "ownedCharacters";

    [MenuItem("Jobsnail/Intro/Reset Intro Flag (테스트용)")]
    public static void ResetIntroFlag()
    {
        if (ES3.KeyExists(kIntroSeen, kFile))
            ES3.DeleteKey(kIntroSeen, kFile);
        Debug.Log("[IntroDebugMenu] introSeen 플래그를 지웠습니다 — 다음 부트스트랩 진입 때 인트로가 다시 뜹니다.");
    }

    [MenuItem("Jobsnail/Intro/Reset Intro + 보유 캐릭터 (테스트용)")]
    public static void ResetIntroAndCharacters()
    {
        if (ES3.KeyExists(kIntroSeen, kFile))
            ES3.DeleteKey(kIntroSeen, kFile);
        if (ES3.KeyExists(kCharacters, kFile))
            ES3.DeleteKey(kCharacters, kFile);
        Debug.Log("[IntroDebugMenu] introSeen + ownedCharacters 를 지웠습니다 — 완전 첫 실행 상태로 인트로가 뜹니다.");
    }
}
