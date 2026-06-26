using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class SessionItem : MonoBehaviour
{
    public TextMeshProUGUI nameText;
    public TextMeshProUGUI playerCountText;

    private void Awake()
    {
        ApplyFont();
    }

    public void SetData(SessionInfoVM sessionInfo)
    {
        ApplyFont();

        if (nameText != null)
            nameText.text = sessionInfo.Name;
        if (playerCountText != null)
            playerCountText.text = $"{sessionInfo.MaxPlayers - sessionInfo.AvailableSlots}/{sessionInfo.MaxPlayers}";
        
        // 값이 변할 때 UI 자동 반영
        sessionInfo.PropertyChanged += (prop) =>
        {
            if (prop == nameof(sessionInfo.Name) && nameText != null)
                nameText.text = sessionInfo.Name;

            if ((prop == nameof(sessionInfo.AvailableSlots) || prop == nameof(sessionInfo.MaxPlayers)) && playerCountText != null)
                playerCountText.text = $"{sessionInfo.MaxPlayers - sessionInfo.AvailableSlots}/{sessionInfo.MaxPlayers}";
        };
    }

    private void ApplyFont()
    {
        if (JobsnailUiKit.TmpFont == null)
            return;

        if (nameText != null)
            nameText.font = JobsnailUiKit.TmpFont;
        if (playerCountText != null)
            playerCountText.font = JobsnailUiKit.TmpFont;
    }
}
