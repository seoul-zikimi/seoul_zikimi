using System;
using TMPro;
using UnityEngine.UI;

/// <summary>
/// "처음이시군요!" 튜토리얼 안내 팝업. 예/아니오 + "이후 표시 안 함" 체크박스.
/// 비주얼은 Resources/UI/Popup/TutorialConfirmPopup 프리팹(UIBase 규칙 — 코드는 바인딩+로직만).
/// 프리팹 생성/수정: Jobsnail ▸ UI ▸ Generate TutorialConfirmPopup Prefab (이후 에디터에서 자유 편집).
/// 호출측: UIManager.Instance.ShowPopupUI&lt;TutorialConfirmPopup&gt;().Show(onYes);
/// </summary>
public sealed class TutorialConfirmPopup : UIPopup
{
    private enum Texts { Title, Body }
    private enum Btns { YesButton, NoButton }
    private enum Tgls { DontShowAgain }

    private Toggle m_DontShow;
    private Action m_OnYes;

    public override void Init()
    {
        Bind<TextMeshProUGUI>(typeof(Texts));
        Bind<Button>(typeof(Btns));
        Bind<Toggle>(typeof(Tgls));

        m_DontShow = Get<Toggle>((int)Tgls.DontShowAgain);
        if (m_DontShow != null) m_DontShow.isOn = false;

        var yes = Get<Button>((int)Btns.YesButton);
        var no = Get<Button>((int)Btns.NoButton);
        if (yes != null) { yes.onClick.RemoveAllListeners(); yes.onClick.AddListener(() => Close(true)); }
        if (no != null) { no.onClick.RemoveAllListeners(); no.onClick.AddListener(() => Close(false)); }
    }

    /// <summary>예 클릭 시 실행할 콜백을 지정. ShowPopupUI 직후 호출한다.</summary>
    public void Show(Action onYes)
    {
        m_OnYes = onYes;
    }

    private void Close(bool yes)
    {
        if (m_DontShow != null && m_DontShow.isOn)
            SaveService.TutorialPromptDontShow = true;

        UIManager.Instance.ClosePopupUI(this);
        if (yes) m_OnYes?.Invoke();
    }
}
