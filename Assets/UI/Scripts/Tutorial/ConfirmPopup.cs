using System;
using TMPro;
using UnityEngine.UI;

/// <summary>
/// 범용 확인/취소(예-아니오) 팝업 — "다시 보지 않기" 체크박스 옵션 포함.
/// 프리팹: Assets/Resources/UI/Popup/ConfirmPopup.prefab (Jobsnail ▸ UI ▸ Generate Tutorial UI Prefabs로 생성).
/// 여러 곳에서 재사용 가능하도록 범용으로 설계(Assets/Docs/UI/사용법.md에 예약된 이름).
/// </summary>
public class ConfirmPopup : UIPopup
{
    private enum Texts { Message }
    private enum Buttons { YesButton, NoButton }
    private enum Toggles { DontShowAgainToggle }

    private Action m_OnYes;
    private Action m_OnNo;
    private Action<bool> m_OnCheckboxChanged;

    public override void Init()
    {
        Bind<TextMeshProUGUI>(typeof(Texts));
        Bind<Button>(typeof(Buttons));
        Bind<Toggle>(typeof(Toggles));

        Get<Button>((int)Buttons.YesButton).onClick.AddListener(OnYesClicked);
        Get<Button>((int)Buttons.NoButton).onClick.AddListener(OnNoClicked);

        var toggle = Get<Toggle>((int)Toggles.DontShowAgainToggle);
        if (toggle != null)
            toggle.onValueChanged.AddListener(v => m_OnCheckboxChanged?.Invoke(v));
    }

    public void Setup(string message, Action onYes, Action onNo, bool showCheckbox, Action<bool> onCheckboxChanged = null)
    {
        m_OnYes = onYes;
        m_OnNo = onNo;
        m_OnCheckboxChanged = onCheckboxChanged;

        var msg = Get<TextMeshProUGUI>((int)Texts.Message);
        if (msg != null) msg.text = message;

        var toggle = Get<Toggle>((int)Toggles.DontShowAgainToggle);
        if (toggle != null)
        {
            toggle.gameObject.SetActive(showCheckbox);
            toggle.isOn = false;
        }
    }

    private void OnYesClicked()
    {
        m_OnYes?.Invoke();
        UIManager.Instance.ClosePopupUI(this);
    }

    private void OnNoClicked()
    {
        m_OnNo?.Invoke();
        UIManager.Instance.ClosePopupUI(this);
    }
}
