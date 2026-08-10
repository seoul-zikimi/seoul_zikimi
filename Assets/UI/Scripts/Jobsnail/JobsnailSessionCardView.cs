using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>세션 목록 카드 1장. 프리팹(JobsnailSessionOverlay) 안의
/// SessionCardTemplate에 붙어 있고, 런타임에는 이 템플릿을 복제해서 목록을 채운다.
/// 표시만 담당하며 네트워크 로직은 전혀 모른다.</summary>
public sealed class JobsnailSessionCardView : MonoBehaviour
{
    [SerializeField] private Button m_Button;
    [SerializeField] private Text m_NameText;
    [SerializeField] private Text m_TagText;
    [SerializeField] private Image m_TagBackground;
    [SerializeField] private Text m_MapText;
    [SerializeField] private Text m_CountText;
    [SerializeField] private Text m_ModeText;

    private static readonly Color kLockedTag = new(1f, 0.55f, 0.55f, 1f);
    private static readonly Color kOpenTag = new(0.58f, 1f, 0.54f, 1f);

    public void Apply(in JobsnailSessionCardData data, int index, Action<int> onSelect)
    {
        if (m_NameText != null)
            m_NameText.text = string.IsNullOrWhiteSpace(data.Name) ? "이름 없는 방" : data.Name;

        if (m_TagText != null)
            m_TagText.text = data.HasPassword ? "🔒 비밀방" : "공개방";

        if (m_TagBackground != null)
            m_TagBackground.color = data.HasPassword ? kLockedTag : kOpenTag;

        // 공개/비밀 태그 아래 → 맵 이름
        if (m_MapText != null)
        {
            bool hasMap = !string.IsNullOrWhiteSpace(data.MapName);
            m_MapText.text = hasMap ? data.MapName : string.Empty;
            m_MapText.gameObject.SetActive(hasMap);
        }

        if (m_CountText != null)
            m_CountText.text = $"인원 {data.Joined} / {data.MaxPlayers}";

        // 인원 표시 아래 → 게임 모드
        if (m_ModeText != null)
        {
            bool hasMode = !string.IsNullOrWhiteSpace(data.ModeLabel);
            m_ModeText.text = hasMode ? data.ModeLabel : string.Empty;
            m_ModeText.gameObject.SetActive(hasMode);
        }

        var button = m_Button != null ? m_Button : GetComponent<Button>();
        if (button == null)
            return;

        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(() => onSelect?.Invoke(index));
        JuicyButton.Attach(button);
    }
}
