using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class SessionItem : MonoBehaviour
{
    public TextMeshProUGUI nameText;
    public TextMeshProUGUI playerCountText;

    public void SetData(SessionInfoVM sessionInfo)
    {
        nameText.text = sessionInfo.Name;
        playerCountText.text = $"{sessionInfo.MaxPlayers - sessionInfo.AvailableSlots}/{sessionInfo.MaxPlayers}";
        
        // 값이 변할 때 UI 자동 반영
        sessionInfo.PropertyChanged += (prop) =>
        {
            if (prop == nameof(sessionInfo.Name))
                nameText.text = sessionInfo.Name;

            if (prop == nameof(sessionInfo.AvailableSlots) || prop == nameof(sessionInfo.MaxPlayers))
                playerCountText.text = $"{sessionInfo.MaxPlayers - sessionInfo.AvailableSlots}/{sessionInfo.MaxPlayers}";
        };
    }
}