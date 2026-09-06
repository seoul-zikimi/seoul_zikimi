using UnityEngine;
using UnityEngine.UI;

namespace SeoulZikimi.UI.New
{
    public sealed class UiNewRoomCardView : MonoBehaviour
    {
        [SerializeField] private Button button;
        [SerializeField] private Text roomName;
        [SerializeField] private Image roomTypeBadge;
        [SerializeField] private Text peopleCount;
        [SerializeField] private Text mapName;
        [SerializeField] private Sprite publicBadge;
        [SerializeField] private Sprite privateBadge;

        public Button Button => button;

        public void Apply(UiNewSessionRoom room)
        {
            if (roomName != null)
                roomName.text = string.IsNullOrWhiteSpace(room.Name) ? "이름 없는 방" : room.Name;
            if (peopleCount != null)
                peopleCount.text = $"{room.Joined}/{room.MaxPlayers}";
            if (mapName != null)
                mapName.text = room.MapName ?? string.Empty;
            if (roomTypeBadge != null)
                roomTypeBadge.sprite = room.HasPassword ? privateBadge : publicBadge;
            if (button != null)
                button.interactable = room.Joined < room.MaxPlayers;
        }
    }
}
