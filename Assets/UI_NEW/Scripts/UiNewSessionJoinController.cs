using System;
using System.Threading.Tasks;
using Blocks.Sessions.Common;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace SeoulZikimi.UI.New
{
    public sealed class UiNewSessionJoinController : MonoBehaviour
    {
        [SerializeField] private RoomListPanel roomList;
        [SerializeField] private PasswordPopupPanel passwordPopup;
        [SerializeField] private UiNewScreenRouter router;
        [SerializeField] private UiNewSessionState sessionState;
        [SerializeField] private LobbyPanel lobby;
        [SerializeField] private SessionSettings sessionSettings;

        private UiNewSessionRoom pendingRoom;
        private bool joining;

        private void Awake()
        {
            roomList.RoomJoinRequested += OnRoomSelected;
            passwordPopup.PasswordSubmitted += OnPasswordSubmitted;
        }

        private void OnDestroy()
        {
            if (roomList != null)
                roomList.RoomJoinRequested -= OnRoomSelected;
            if (passwordPopup != null)
                passwordPopup.PasswordSubmitted -= OnPasswordSubmitted;
        }

        private void OnRoomSelected(UiNewSessionRoom room)
        {
            pendingRoom = room;
            if (room.HasPassword)
                router.Show(UiNewScreen.Password);
            else
                _ = JoinAsync(room, null);
        }

        private void OnPasswordSubmitted(string password) => _ = JoinAsync(pendingRoom, password);

        private async Task JoinAsync(UiNewSessionRoom room, string password)
        {
            if (joining || string.IsNullOrEmpty(room.SessionId))
                return;
            joining = true;
            try
            {
                await UiNewSessionService.EnsureReadyAsync();
                JoinSessionOptions options = sessionSettings.ToJoinSessionOptions();
                options.Password = string.IsNullOrEmpty(password) ? null : password;
                ISession session = await MultiplayerService.Instance.JoinSessionByIdAsync(room.SessionId, options);
                sessionState.Set(session);
                lobby.SetRoomName(session.Name);
                UiNewSessionService.StartNetwork(session);
                router.Show(UiNewScreen.Lobby);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UI_NEW] 방 입장 실패: {ex.Message}");
                if (room.HasPassword)
                    passwordPopup.ShowError();
            }
            finally
            {
                joining = false;
            }
        }
    }
}
