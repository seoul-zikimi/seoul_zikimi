using Player;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>프리팹에 미리 배치된 키 설정 한 줄. 런타임에는 텍스트와 콜백만 연결한다.</summary>
public sealed class KeyBindingRow : MonoBehaviour
{
    private TextMeshProUGUI m_ActionLabel;
    private TextMeshProUGUI m_BindingLabel;
    private Button m_RebindButton;
    private Button m_ResetButton;
    private KeyBindingPopup m_Owner;
    private GameplayInputBindings.BindingInfo m_Info;

    public void Setup(KeyBindingPopup owner, GameplayInputBindings.BindingInfo info)
    {
        Cache();
        m_Owner = owner;
        m_Info = info;
        m_ActionLabel.text = KeyBindingPopup.ActionLabel(info);
        m_BindingLabel.text = KeyBindingPopup.BindingLabel(info);
        m_RebindButton.onClick.RemoveAllListeners();
        m_RebindButton.onClick.AddListener(() => m_Owner.BeginRebind(this, m_Info));
        m_ResetButton.onClick.RemoveAllListeners();
        m_ResetButton.onClick.AddListener(() => m_Owner.ResetBinding(m_Info));
        SetWaiting(false);
        gameObject.SetActive(true);
    }

    public void SetWaiting(bool waiting)
    {
        Cache();
        m_BindingLabel.text = waiting ? "키를 누르세요…  (ESC 취소)" : KeyBindingPopup.BindingLabel(m_Info);
        if (m_RebindButton != null) m_RebindButton.interactable = !waiting;
        if (m_ResetButton != null) m_ResetButton.interactable = !waiting;
    }

    private void Cache()
    {
        if (m_ActionLabel != null) return;
        m_ActionLabel = Find<TextMeshProUGUI>("ActionLabel");
        m_BindingLabel = Find<TextMeshProUGUI>("BindingLabel");
        m_RebindButton = Find<Button>("RebindButton");
        m_ResetButton = Find<Button>("ResetButton");
    }

    private T Find<T>(string objectName) where T : Component
    {
        foreach (var child in GetComponentsInChildren<T>(true))
            if (child.name == objectName) return child;
        return null;
    }
}
