using UnityEngine;
using UnityEngine.UI;

namespace SeoulZikimi.UI.New
{
    public sealed class UiNewLobbySlotView : MonoBehaviour
    {
        [SerializeField] private Image panel;
        [SerializeField] private Sprite normalSprite;
        [SerializeField] private Sprite blueTeamSprite;
        [SerializeField] private Sprite redTeamSprite;
        [SerializeField] private Sprite emptySprite;
        [SerializeField] private Text nickname;
        [SerializeField] private Image readyState;
        [SerializeField] private Sprite readySprite;
        [SerializeField] private Sprite waitingSprite;
        [SerializeField] private GameObject hostBadge;
        [SerializeField] private Image avatarAnchor;

        public void Apply(bool occupied, string displayName, bool isHost, bool isLocal, bool ready,
            int team, bool versusMode, Sprite avatarSprite)
        {
            if (nickname != null)
            {
                nickname.gameObject.SetActive(occupied);
                nickname.text = occupied ? (isLocal ? $"(나) {displayName}" : displayName) : string.Empty;
            }
            if (hostBadge != null) hostBadge.SetActive(occupied && isHost);
            if (readyState != null)
            {
                readyState.gameObject.SetActive(occupied);
                readyState.sprite = isHost || ready ? readySprite : waitingSprite;
                readyState.color = Color.white;
            }
            if (panel != null)
            {
                panel.sprite = !occupied ? emptySprite
                    : !versusMode ? normalSprite
                    : team == 0 ? blueTeamSprite : redTeamSprite;
                panel.color = Color.white;
            }
            if (avatarAnchor != null)
            {
                avatarAnchor.sprite = occupied ? avatarSprite : null;
                avatarAnchor.preserveAspect = true;
                avatarAnchor.raycastTarget = false;
                avatarAnchor.color = occupied && avatarSprite != null
                    ? Color.white
                    : occupied ? new Color(1f, 1f, 1f, 0.12f) : new Color(1f, 1f, 1f, 0.03f);
            }
        }
    }
}
