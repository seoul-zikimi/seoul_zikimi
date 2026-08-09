using UnityEditor;
using UnityEngine;

/// <summary>테스트 편의용 메뉴 — "다시 보지 않기" 체크로 저장된 PlayerPrefs 플래그를 지워서 팝업을 다시 뜨게 한다.</summary>
public static class TutorialDebugMenu
{
    private const string kDismissedKey = "TutorialPopupDismissed";

    [MenuItem("Jobsnail/Tutorial/Reset Tutorial Popup Flag (테스트용)")]
    public static void ResetPopupFlag()
    {
        PlayerPrefs.DeleteKey(kDismissedKey);
        PlayerPrefs.Save();
        Debug.Log("[TutorialDebugMenu] TutorialPopupDismissed 플래그를 지웠습니다 — 다음 로비 진입 때 팝업이 다시 뜹니다.");
    }
}
