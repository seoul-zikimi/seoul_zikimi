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
                _ = JoinAsync(room);
        }

        // 비밀방은 UGS 세션에 비밀번호가 걸려 있는 게 아니라 "PasswordHash" 프로퍼티만 붙어 있다.
        // 평문을 JoinSessionOptions.Password로 보내면 UGS가 항상 거부하므로, 여기서 해시를 맞춰보고
        // 통과한 경우에만 비밀번호 없이 입장한다.
        private void OnPasswordSubmitted(string password)
        {
            if (!SessionPasswordHash.Matches(pendingRoom.PasswordHash, password))
            {
                passwordPopup.ShowError();
                return;
            }

            _ = JoinAsync(pendingRoom);
        }

        private async Task JoinAsync(UiNewSessionRoom room)
        {
            if (joining || string.IsNullOrEmpty(room.SessionId))
                return;
            joining = true;
            try
            {
                await UiNewSessionService.EnsureReadyAsync();
                JoinSessionOptions options = sessionSettings.ToJoinSessionOptions();
                ISession session = await MultiplayerService.Instance.JoinSessionByIdAsync(room.SessionId, options);
                sessionState.Set(session);
                lobby.SetRoomName(session.Name);
                // 호스트뿐 아니라 참가자도 GameScene -> Lobby 복귀 시 같은 방을 복원할 수 있어야 한다.
                JobsnailSessionManager.Instance.RegisterActiveSession(session);
                UiNewSessionService.StartNetwork(session);
                router.Show(UiNewScreen.Lobby);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UI_NEW] 방 입장 실패: {ex.Message}");
                roomList.HideJoinSpinner();   // 실패 시 카드 위 스피너 정리
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
