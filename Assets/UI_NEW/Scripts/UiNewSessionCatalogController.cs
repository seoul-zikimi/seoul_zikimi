using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace SeoulZikimi.UI.New
{
    public sealed class UiNewSessionCatalogController : MonoBehaviour
    {
        [SerializeField] private RoomListPanel roomList;
        [SerializeField] private int maxRooms = 6;

        private void Awake() => roomList.RefreshRequested += Refresh;
        private void Start() => Refresh();
        private void OnDestroy()
        {
            if (roomList != null)
                roomList.RefreshRequested -= Refresh;
        }

        public async void Refresh()
        {
            try
            {
                await RefreshAsync();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UI_NEW] 방 목록 조회 실패: {ex.Message}");
                roomList.SetRooms(Array.Empty<UiNewSessionRoom>());
            }
        }

        private async Task RefreshAsync()
        {
            await UiNewSessionService.EnsureReadyAsync();
            var result = await MultiplayerService.Instance.QuerySessionsAsync(new QuerySessionsOptions
            {
                SortOptions = new List<SortOption> { new(SortOrder.Ascending, SortField.CreationTime) }
            });

            var rooms = new List<UiNewSessionRoom>();
            foreach (ISessionInfo session in result.Sessions)
            {
                if (UiNewSessionService.ReadProperty(session, "State") == "InGame")
                    continue;

                string mapName = MapName(session);
                rooms.Add(new UiNewSessionRoom(
                    session.Id,
                    session.Name,
                    session.Properties != null && session.Properties.ContainsKey("PasswordHash"),
                    Mathf.Max(0, session.MaxPlayers - session.AvailableSlots),
                    session.MaxPlayers,
                    mapName));
                if (rooms.Count >= maxRooms)
                    break;
            }
            roomList.SetRooms(rooms);
        }

        private static string MapName(ISessionInfo session)
        {
            if (!int.TryParse(UiNewSessionService.ReadProperty(session, "MapIndex"), out int index))
                return string.Empty;
            // '랜덤' 방은 실제 맵이 정해지지 않았다 — Get()이 0번으로 폴백해 엉뚱한 맵 이름이 뜨는 걸 막는다.
            if (index == GridSystem.MapCatalog.RandomMapIndex) return UiNewMapOptions.RandomLabel;
            GridSystem.MapCatalog catalog = GridSystem.MapCatalog.Instance;
            var definition = catalog != null ? catalog.Get(index) : null;
            return definition != null ? definition.DisplayName : string.Empty;
        }
    }
}
